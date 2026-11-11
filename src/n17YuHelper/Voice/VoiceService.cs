using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using N17YuHelper.Llm;

namespace N17YuHelper.Voice;

/// <summary>
/// Speech → text with the same model that translates (Gemma 4 with its audio part, mmproj).
/// A long recording is cut into pieces of up to ~28 s at pauses: the model handles 30 s of audio at a time.
/// </summary>
public sealed class VoiceService(LlamaServer server, Func<string?> audioEncoderFile, Func<string> modelName)
{
    private const int MaxChunkSeconds = 28;
    private const int MinChunkSeconds = 16;

    /// <summary>Whether the model's audio part is present (otherwise voice input is unavailable).</summary>
    public bool IsAvailable => audioEncoderFile() is not null;

    public async Task<string> TranscribeAsync(byte[] pcm, IProgress<string>? progress, CancellationToken ct)
    {
        if (audioEncoderFile() is null)
            throw new ModelSetupException(
                $"Voice input needs the audio part of the translation model ({modelName()}). " +
                "Open Models... and click \"Add voice\", or choose Gemma 4 E2B, E4B or 12B.");

        if (pcm.Length > AudioRecorder.SampleRate && IsDigitalSilence(pcm))
            throw new InvalidOperationException(
                "The microphone recorded only silence. Check the default microphone and that desktop apps may use it " +
                "(Windows Settings > Privacy & security > Microphone).");

        var trimmed = TrimSilence(pcm);
        if (trimmed.Length < AudioRecorder.SampleRate * 2 / 4) return ""; // under 0.25 s — nothing to recognize

        var sw = Stopwatch.StartNew();
        if (server.State != ServerState.Ready) progress?.Report("Loading the model...");
        await server.EnsureReadyAsync(ct);
        if (!await AudioSupportedAsync(ct))
        {
            // The model may have been loaded before the audio part was downloaded — restart once.
            if (!server.UsedFallback)
            {
                progress?.Report("Reloading the model with its voice part...");
                server.Stop("reload with the voice part");
                await server.EnsureReadyAsync(ct);
            }
            if (server.UsedFallback)
                throw new ModelSetupException(
                    "This llama.cpp build cannot load the voice part of the model. Update llama.cpp in Models... (Update button).");
            if (!await AudioSupportedAsync(ct))
                throw new ModelSetupException("The loaded model does not accept audio. Choose Gemma 4 E2B, E4B or 12B with its voice part in Models...");
        }

        var chunks = Split(trimmed);
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            progress?.Report(chunks.Count == 1 ? "Recognizing speech..." : $"Recognizing speech ({i + 1}/{chunks.Count})...");
            var text = (await TranscribeChunkAsync(chunks[i], ct)).Trim();
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
        var result = Clean(sb.ToString());
        Log.Info($"Speech: {trimmed.Length / 2.0 / AudioRecorder.SampleRate:0.0} s audio, {chunks.Count} part(s) -> {result.Length} chars, {sw.Elapsed.TotalSeconds:0.00} s");
        return result;
    }

    private async Task<string> TranscribeChunkAsync(ArraySegment<byte> pcm, CancellationToken ct)
    {
        var wav = AudioRecorder.ToWav(pcm);
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_audio",
                        ["input_audio"] = new JsonObject { ["data"] = Convert.ToBase64String(wav), ["format"] = "wav" },
                    },
                    new JsonObject { ["type"] = "text", ["text"] = Prompt },
                },
            },
        };
        // About 25 tokens per second of speech — with a margin.
        int maxTokens = Math.Max(256, pcm.Count / 2 / AudioRecorder.SampleRate * 40);
        return await ChatClient.StreamAsync(server, messages, 0, maxTokens, null, ct);
    }

    // Tested on mixed speech (Russian + English): this way the model does not drop the parts in the other language.
    private const string Prompt =
        "Transcribe all speech in this audio word for word. " +
        "If the speaker switches languages, write each part in its own language. " +
        "Do not translate. Output only the transcript.";

    /// <summary>Whether the running server accepts audio (otherwise it silently ignores it).</summary>
    private async Task<bool> AudioSupportedAsync(CancellationToken ct)
    {
        if (server.UsedFallback) return false;
        try
        {
            using var resp = await LlamaServer.Http.GetAsync(new Uri(server.BaseUri, "props"), ct);
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            return json?["modalities"]?["audio"]?.GetValue<bool>() != false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Info("Could not check audio support: " + ex.Message); // old llama-server without /props — try anyway
            return true;
        }
    }

    // ---------------- audio processing ----------------

    private static bool IsDigitalSilence(byte[] pcm)
    {
        for (int i = 0; i + 1 < pcm.Length; i += 2)
            if (Math.Abs((short)(pcm[i] | (pcm[i + 1] << 8))) > 2) return false;
        return true;
    }

    /// <summary>Loudness in 100 ms windows.</summary>
    private static float[] Energy(ReadOnlySpan<byte> pcm, int windowSamples)
    {
        int samples = pcm.Length / 2;
        var e = new float[(samples + windowSamples - 1) / windowSamples];
        for (int w = 0; w < e.Length; w++)
        {
            double sum = 0;
            int start = w * windowSamples, end = Math.Min(samples, start + windowSamples);
            for (int i = start; i < end; i++)
            {
                short s = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
                sum += (double)s * s;
            }
            e[w] = (float)Math.Sqrt(sum / Math.Max(1, end - start)) / 32768f;
        }
        return e;
    }

    /// <summary>Trims silence at the start and end (keeping 0.3 s each).</summary>
    public static byte[] TrimSilence(byte[] pcm)
    {
        const int win = AudioRecorder.SampleRate / 10;
        var e = Energy(pcm, win);
        if (e.Length == 0) return pcm;
        float peak = e.Max();
        if (peak < 0.003f) return []; // silence only
        float threshold = Math.Max(0.01f, peak * 0.08f);
        int first = Array.FindIndex(e, x => x > threshold);
        int last = Array.FindLastIndex(e, x => x > threshold);
        int from = Math.Max(0, first - 3) * win * 2;
        int to = Math.Min(pcm.Length, (last + 4) * win * 2);
        return pcm[from..to];
    }

    /// <summary>Cuts into pieces of up to 28 s at the quietest spot between 16 and 28 seconds.</summary>
    public static List<ArraySegment<byte>> Split(byte[] pcm)
    {
        const int win = AudioRecorder.SampleRate / 10;
        int bytesPerSecond = AudioRecorder.SampleRate * 2;
        var result = new List<ArraySegment<byte>>();
        var e = Energy(pcm, win);
        int pos = 0;
        while (pcm.Length - pos > MaxChunkSeconds * bytesPerSecond)
        {
            int fromWin = (pos + MinChunkSeconds * bytesPerSecond) / (win * 2);
            int toWin = Math.Min(e.Length - 1, (pos + MaxChunkSeconds * bytesPerSecond) / (win * 2));
            int best = toWin;
            for (int w = fromWin; w <= toWin; w++)
                if (e[w] < e[best]) best = w;
            int cut = best * win * 2 + win; // middle of the quiet window
            result.Add(new ArraySegment<byte>(pcm, pos, cut - pos));
            pos = cut;
        }
        result.Add(new ArraySegment<byte>(pcm, pos, pcm.Length - pos));
        return result;
    }

    /// <summary>Removes what the model sometimes adds around the transcript.</summary>
    private static string Clean(string text)
    {
        text = text.Trim();
        foreach (var prefix in new[] { "Transcript:", "Transcription:", "Расшифровка:" })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) text = text[prefix.Length..].Trim();
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
        return text;
    }
}
