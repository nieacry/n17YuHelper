namespace N17YuHelper.Tools;

/// <summary>
/// Text-to-speech with Windows voices (SAPI). The voice is chosen by the text language;
/// if there is no voice for the language, a hint about what to install is returned.
/// </summary>
public static class Speech
{
    private static dynamic? _voice;

    /// <summary>Speak the text. Returns null or an error message for the user.</summary>
    public static string? Speak(string text, string languageCode)
    {
        try
        {
            if (_voice is null)
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("Windows speech is not available.");
                _voice = Activator.CreateInstance(type);
            }
            int lcid = languageCode switch { "zh" => 0x804, "ru" => 0x419, "uk" => 0x422, _ => 0x409 };
            dynamic voices = _voice!.GetVoices($"Language={lcid:X}", "");
            if (voices.Count > 0)
                _voice.Voice = voices.Item(0);
            else if (languageCode != "en")
                return $"No {LanguageName(languageCode)} voice is installed. Windows Settings > Time & language > Speech > Add voices.";

            const int SVSFlagsAsync = 1, SVSFPurgeBeforeSpeak = 2;
            _voice.Speak(text, SVSFlagsAsync | SVSFPurgeBeforeSpeak);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Speech", ex);
            return "Could not speak: " + ex.Message;
        }
    }

    public static void Stop()
    {
        try { _voice?.Speak("", 2); } catch { /* nothing was being spoken */ }
    }

    private static string LanguageName(string code) => code switch
    {
        "zh" => "Chinese",
        "ru" => "Russian",
        "uk" => "Ukrainian",
        _ => "English",
    };
}
