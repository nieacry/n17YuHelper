using System.Runtime.InteropServices;

namespace N17YuHelper.Voice;

/// <summary>
/// Recording from the default microphone via winmm (waveIn): 16 kHz, mono, 16 bit —
/// exactly what the model's audio encoder expects. No third-party libraries.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int BufferMs = 50;
    private const int BufferCount = 10;
    private const int BufferBytes = SampleRate * 2 * BufferMs / 1000;

    private readonly MemoryStream _pcm = new();
    private readonly object _lock = new();
    private IntPtr _hwi;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private Thread? _thread;
    private volatile bool _stopping;

    /// <summary>Loudness of the last chunk (0…1, RMS). Called from a background thread.</summary>
    public event Action<float>? Level;

    public TimeSpan Duration
    {
        get { lock (_lock) return TimeSpan.FromSeconds(_pcm.Length / 2.0 / SampleRate); }
    }

    /// <summary>Whether the system has at least one microphone.</summary>
    public static bool HasDevice => waveInGetNumDevs() > 0;

    /// <summary>Microphone names (Windows returns at most 31 characters).</summary>
    public static List<string> DeviceNames()
    {
        var list = new List<string>();
        uint n = waveInGetNumDevs();
        for (uint i = 0; i < n; i++)
            if (waveInGetDevCaps((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<WAVEINCAPS>()) == 0)
                list.Add(caps.szPname);
        return list;
    }

    /// <summary>Microphone by name (empty or not found — the Windows default microphone).</summary>
    public string Device { get; init; } = "";

    private uint DeviceId()
    {
        if (string.IsNullOrWhiteSpace(Device)) return WAVE_MAPPER;
        var names = DeviceNames();
        int i = names.FindIndex(x => string.Equals(x, Device, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = names.FindIndex(x => x.StartsWith(Device, StringComparison.OrdinalIgnoreCase) || Device.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        if (i < 0) Log.Info($"Microphone \"{Device}\" not found, using the default one");
        return i < 0 ? WAVE_MAPPER : (uint)i;
    }

    public void Start()
    {
        if (!HasDevice) throw new InvalidOperationException("No microphone found. Connect a microphone and try again.");

        var fmt = new WAVEFORMATEX
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = SampleRate * 2,
            cbSize = 0,
        };
        int r = waveInOpen(out _hwi, DeviceId(), ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
        if (r != 0) throw new InvalidOperationException(OpenError(r));

        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        for (int i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
            _headers[i] = Marshal.AllocHGlobal(hdrSize);
            Marshal.StructureToPtr(new WAVEHDR { lpData = _buffers[i], dwBufferLength = BufferBytes }, _headers[i], false);
            Check(waveInPrepareHeader(_hwi, _headers[i], hdrSize), "prepare");
            Check(waveInAddBuffer(_hwi, _headers[i], hdrSize), "add buffer");
        }
        Check(waveInStart(_hwi), "start");

        _thread = new Thread(Pump) { IsBackground = true, Name = "mic", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        Log.Info("Microphone recording started" + (string.IsNullOrWhiteSpace(Device) ? "" : $" ({Device})"));
    }

    /// <summary>Stops recording and returns 16 kHz mono 16-bit PCM.</summary>
    public byte[] Stop()
    {
        if (_hwi == IntPtr.Zero) return [];
        _stopping = true;
        _thread?.Join(1000);
        waveInStop(_hwi);
        waveInReset(_hwi); // returns all buffers with whatever was recorded
        Drain(requeue: false);
        Release();
        lock (_lock)
        {
            Log.Info($"Microphone recording stopped: {_pcm.Length / 2.0 / SampleRate:0.0} s");
            return _pcm.ToArray();
        }
    }

    private void Pump()
    {
        while (!_stopping)
        {
            Drain(requeue: true);
            Thread.Sleep(15);
        }
    }

    private static readonly int FlagsOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags));
    private static readonly int RecordedOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwBytesRecorded));

    private int _next; // the buffer the driver will return next (the queue is circular)

    /// <summary>Takes filled buffers strictly in queue order and requeues them if needed.</summary>
    private void Drain(bool requeue)
    {
        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        for (int k = 0; k < BufferCount; k++)
        {
            var h = _headers[_next];
            if (h == IntPtr.Zero) return;
            int flags = Marshal.ReadInt32(h, FlagsOffset);
            if ((flags & WHDR_DONE) == 0) return;
            int n = Marshal.ReadInt32(h, RecordedOffset);
            if (n > 0) Consume(_buffers[_next], n);
            Marshal.WriteInt32(h, RecordedOffset, 0);
            Marshal.WriteInt32(h, FlagsOffset, flags & ~WHDR_DONE);
            if (requeue && !_stopping) waveInAddBuffer(_hwi, h, hdrSize);
            _next = (_next + 1) % BufferCount;
        }
    }

    private readonly byte[] _tmp = new byte[BufferBytes];

    private void Consume(IntPtr data, int n)
    {
        Marshal.Copy(data, _tmp, 0, n);
        double sum = 0;
        for (int i = 0; i + 1 < n; i += 2)
        {
            short s = (short)(_tmp[i] | (_tmp[i + 1] << 8));
            sum += (double)s * s;
        }
        lock (_lock) _pcm.Write(_tmp, 0, n);
        float rms = (float)(Math.Sqrt(sum / Math.Max(1, n / 2)) / 32768.0);
        Level?.Invoke(rms);
    }

    private void Release()
    {
        if (_hwi == IntPtr.Zero) return;
        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        for (int i = 0; i < BufferCount; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                waveInUnprepareHeader(_hwi, _headers[i], hdrSize);
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }
            if (_buffers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffers[i]);
                _buffers[i] = IntPtr.Zero;
            }
        }
        waveInClose(_hwi);
        _hwi = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hwi != IntPtr.Zero)
        {
            _stopping = true;
            _thread?.Join(1000);
            waveInReset(_hwi);
            Release();
        }
        _pcm.Dispose();
    }

    private static void Check(int result, string what)
    {
        if (result != 0) throw new InvalidOperationException($"Microphone error ({what}: {result}).");
    }

    private static string OpenError(int code) => code switch
    {
        4 => "The microphone is busy in another app.",                              // MMSYSERR_ALLOCATED
        2 or 6 => "No microphone found. Connect a microphone and try again.",        // BADDEVICEID / NODRIVER
        _ => $"Could not open the microphone (error {code}). Check Windows Settings > Privacy > Microphone.",
    };

    // ---------------- WAV ----------------

    /// <summary>16 kHz mono 16-bit PCM → WAV file.</summary>
    public static byte[] ToWav(ReadOnlySpan<byte> pcm)
    {
        var wav = new byte[44 + pcm.Length];
        var w = new BinaryWriter(new MemoryStream(wav));
        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SampleRate);
        w.Write(SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Flush();
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    // ---------------- winmm ----------------

    private const uint WAVE_MAPPER = unchecked((uint)-1);
    private const uint CALLBACK_NULL = 0;
    private const int WHDR_DONE = 1;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] private static extern int waveInGetDevCaps(UIntPtr uDeviceID, out WAVEINCAPS pwic, uint cbwic);
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr hwi);
}
