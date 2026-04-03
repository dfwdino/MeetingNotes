using System.IO;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingNotes.Services;

public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _systemCapture;
    private WaveInEvent?           _micCapture;
    private WaveFileWriter?        _wavWriter;     // system audio
    private WaveFileWriter?        _micWavWriter;  // mic audio (separate file)

    // One-shot loopback alignment: WASAPI only fires DataAvailable when audio is playing.
    // If recording starts during silence, the loopback file would begin at the wrong time.
    // We record exactly when the first data arrives and prepend that much silence once,
    // so both the loopback and mic files are always anchored to the same t=0.
    private bool     _loopbackPadded;
    private DateTime _captureStartTime;

    // Waveform level: both sources update the peak; a fixed-rate timer fires
    // AudioLevelChanged at a steady 20 fps so the waveform scrolls consistently.
    private float _peakLevel;
    private System.Threading.Timer? _levelTimer;

    private string? _finalOutputPath;
    private string? _tempWavPath;
    private string? _micTempPath;
    private string? _requestedFormat;
    private int     _requestedBitrate;

    private bool _isRecording;

    // Per-session mute flags — write silence instead of real audio so files stay time-aligned
    private volatile bool _micMuted;
    private volatile bool _loopbackMuted;

    // Lock that guards writer swaps during live chunk splitting.
    // Audio callbacks hold this lock only while writing (microseconds);
    // SplitChunkAsync holds it only while swapping references (nanoseconds).
    private readonly object _writerLock = new();

    public bool    IsRecording    => _isRecording;
    public bool    IsMicMuted     => _micMuted;
    public bool    IsLoopbackMuted => _loopbackMuted;
    public string? CurrentFilePath => _finalOutputPath;

    /// <summary>
    /// Task that completes when the audio file has been fully written and converted.
    /// Await this before reading or transcribing the audio file.
    /// </summary>
    public Task AudioSaveTask { get; private set; } = Task.CompletedTask;

    public event EventHandler<float>? AudioLevelChanged;

    public void SetMicMuted(bool muted)      => _micMuted      = muted;
    public void SetLoopbackMuted(bool muted) => _loopbackMuted = muted;

    // ──────────────────────────────────────────────────────────────
    //  START
    // ──────────────────────────────────────────────────────────────
    public void StartRecording(string outputPath, string format = "MP3", int mp3Bitrate = 64)
    {
        if (_isRecording) return;

        _finalOutputPath  = outputPath;
        _requestedFormat  = format;
        _requestedBitrate = mp3Bitrate;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // System audio → temp WAV (WASAPI provides silence automatically when nothing plays,
        // so the file is always correctly timed from t=0 without any padding needed)
        _tempWavPath   = Path.ChangeExtension(outputPath, ".tmp.wav");
        _systemCapture = new WasapiLoopbackCapture();
        _wavWriter     = new WaveFileWriter(_tempWavPath, _systemCapture.WaveFormat);

        _micMuted      = false;
        _loopbackMuted = false;
        _loopbackPadded    = false;
        _captureStartTime  = DateTime.UtcNow;

        _systemCapture.DataAvailable += OnSystemAudioAvailable;
        _systemCapture.StartRecording();

        // Mic → separate temp WAV (mixed in during conversion)
        try
        {
            var dir  = Path.GetDirectoryName(_tempWavPath)!;
            var stem = Path.GetFileNameWithoutExtension(_tempWavPath);
            _micTempPath  = Path.Combine(dir, stem + ".mic.wav");
            _micCapture   = new WaveInEvent { WaveFormat = new WaveFormat(44100, 16, 1) };
            _micWavWriter = new WaveFileWriter(_micTempPath, _micCapture.WaveFormat);
            _micCapture.DataAvailable += OnMicAudioAvailable;
            _micCapture.StartRecording();
        }
        catch
        {
            _micCapture   = null;
            _micWavWriter = null;
            _micTempPath  = null;
        }

        // Waveform display at a fixed 20 fps
        _peakLevel  = 0f;
        _levelTimer = new System.Threading.Timer(_ =>
        {
            var level = Interlocked.Exchange(ref _peakLevel, 0f);
            AudioLevelChanged?.Invoke(this, level);
        }, null, 50, 50);

        _isRecording = true;
    }

    // ──────────────────────────────────────────────────────────────
    //  STOP
    // ──────────────────────────────────────────────────────────────
    public string StopRecording()
    {
        if (!_isRecording) return _finalOutputPath ?? string.Empty;

        _levelTimer?.Dispose();
        _levelTimer = null;

        _systemCapture?.StopRecording();
        _micCapture?.StopRecording();

        _wavWriter?.Flush();
        _wavWriter?.Dispose();
        _wavWriter = null;

        _micWavWriter?.Flush();
        _micWavWriter?.Dispose();
        _micWavWriter = null;

        _systemCapture?.Dispose();
        _systemCapture = null;
        _micCapture?.Dispose();
        _micCapture = null;

        _isRecording = false;

        // Capture locals so the background task doesn't race on instance fields
        var tempPath  = _tempWavPath;
        var micPath   = _micTempPath;
        var outPath   = _finalOutputPath!;
        var format    = _requestedFormat;
        var bitrate   = _requestedBitrate;

        // Run the mix/convert on a background thread so the UI can navigate immediately.
        // Callers that need to read the finished file should await AudioSaveTask first.
        AudioSaveTask = Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    if (format == "MP3")
                        ConvertAndMix(tempPath, micPath, outPath, bitrate);
                    else
                        MixToWav(tempPath, micPath, outPath);
                }
            }
            finally
            {
                DeleteFileIfExists(tempPath);
                DeleteFileIfExists(micPath);
            }
        });

        return _finalOutputPath ?? string.Empty;
    }

    // ──────────────────────────────────────────────────────────────
    //  AUDIO CALLBACKS
    // ──────────────────────────────────────────────────────────────
    private void OnSystemAudioAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        lock (_writerLock)
        {
            if (!_loopbackPadded)
            {
                _loopbackPadded = true;
                var delay = DateTime.UtcNow - _captureStartTime;
                if (delay.TotalMilliseconds > 80 && _wavWriter is not null)
                {
                    var wf    = _wavWriter.WaveFormat;
                    var bytes = (int)(wf.AverageBytesPerSecond * delay.TotalSeconds);
                    bytes     = (bytes / wf.BlockAlign) * wf.BlockAlign;
                    if (bytes > 0)
                        _wavWriter.Write(new byte[bytes], 0, bytes);
                }
            }

            if (_loopbackMuted)
                _wavWriter?.Write(new byte[e.BytesRecorded], 0, e.BytesRecorded);
            else
            {
                _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                UpdatePeakLevel(CalculateLevelFloat(e.Buffer, e.BytesRecorded));
            }
        }
    }

    private void OnMicAudioAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        lock (_writerLock)
        {
            if (_micMuted)
                _micWavWriter?.Write(new byte[e.BytesRecorded], 0, e.BytesRecorded);
            else
            {
                _micWavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                UpdatePeakLevel(CalculateLevelPcm16(e.Buffer, e.BytesRecorded));
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  LIVE CHUNK SPLIT
    // ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Atomically splits the recording without stopping the audio devices.
    /// Swaps writers so callbacks immediately target the new chunk; finalizes
    /// and converts the completed chunk in the background.
    /// </summary>
    /// <param name="newOutputPath">Output file path for the next chunk.</param>
    /// <returns>Path of the fully converted completed chunk, or null if not recording.</returns>
    public async Task<string?> SplitChunkAsync(string newOutputPath)
    {
        if (!_isRecording) return null;

        Directory.CreateDirectory(Path.GetDirectoryName(newOutputPath)!);

        var newTempWavPath = Path.ChangeExtension(newOutputPath, ".tmp.wav");
        var newTempDir  = Path.GetDirectoryName(newTempWavPath)!;
        var newTempStem = Path.GetFileNameWithoutExtension(newTempWavPath);
        var newMicTempPath = Path.Combine(newTempDir, newTempStem + ".mic.wav");

        // Create writers for the next chunk before acquiring the lock
        var newWavWriter = new WaveFileWriter(newTempWavPath, _systemCapture!.WaveFormat);
        WaveFileWriter? newMicWavWriter = _micCapture != null && _micWavWriter != null
            ? new WaveFileWriter(newMicTempPath, _micCapture.WaveFormat)
            : null;

        // Capture completed-chunk state and atomically swap writers
        string  completedOutputPath;
        string  oldTempWavPath;
        string? oldMicTempPath;
        WaveFileWriter? oldWavWriter;
        WaveFileWriter? oldMicWavWriter;

        lock (_writerLock)
        {
            completedOutputPath = _finalOutputPath!;
            oldTempWavPath      = _tempWavPath!;
            oldMicTempPath      = _micTempPath;
            oldWavWriter        = _wavWriter;
            oldMicWavWriter     = _micWavWriter;

            _finalOutputPath  = newOutputPath;
            _tempWavPath      = newTempWavPath;
            _micTempPath      = newMicTempPath;
            _wavWriter        = newWavWriter;
            _micWavWriter     = newMicWavWriter;
            _loopbackPadded   = false;
            _captureStartTime = DateTime.UtcNow;
        }

        // Flush and dispose old writers — callbacks no longer reference them
        oldWavWriter?.Flush();
        oldWavWriter?.Dispose();
        oldMicWavWriter?.Flush();
        oldMicWavWriter?.Dispose();

        // Convert completed chunk to final format on background thread
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(oldTempWavPath))
                {
                    if (_requestedFormat == "MP3")
                        ConvertAndMix(oldTempWavPath, oldMicTempPath, completedOutputPath, _requestedBitrate);
                    else
                        MixToWav(oldTempWavPath, oldMicTempPath, completedOutputPath);
                }
            }
            finally
            {
                DeleteFileIfExists(oldTempWavPath);
                DeleteFileIfExists(oldMicTempPath);
            }
        });

        return completedOutputPath;
    }

    /// <summary>Thread-safe peak-hold: keeps the highest level seen since last timer tick.</summary>
    private void UpdatePeakLevel(float level)
    {
        float current;
        do { current = _peakLevel; }
        while (level > current &&
               Interlocked.CompareExchange(ref _peakLevel, level, current) != current);
    }

    // ──────────────────────────────────────────────────────────────
    //  MIXING + CONVERSION
    // ──────────────────────────────────────────────────────────────
    private const float SystemAudioVolume = 0.65f;
    private const float MicVolume         = 1.20f;

    private static ISampleProvider BuildMixedProvider(string sysWavPath, string? micWavPath,
        out AudioFileReader sysReader, out AudioFileReader? micReader)
    {
        sysReader = new AudioFileReader(sysWavPath);

        ISampleProvider sysProvider = new WdlResamplingSampleProvider(sysReader, 44100);
        if (sysProvider.WaveFormat.Channels == 1)
            sysProvider = new MonoToStereoSampleProvider(sysProvider);
        sysProvider = new VolumeSampleProvider(sysProvider) { Volume = SystemAudioVolume };

        micReader = null;

        if (string.IsNullOrEmpty(micWavPath) || !File.Exists(micWavPath))
            return sysProvider;

        try
        {
            micReader = new AudioFileReader(micWavPath);

            ISampleProvider micProvider = new WdlResamplingSampleProvider(micReader, 44100);
            if (micProvider.WaveFormat.Channels == 1)
                micProvider = new MonoToStereoSampleProvider(micProvider);
            micProvider = new VolumeSampleProvider(micProvider) { Volume = MicVolume };

            var mixer = new MixingSampleProvider(sysProvider.WaveFormat) { ReadFully = false };
            mixer.AddMixerInput(sysProvider);
            mixer.AddMixerInput(micProvider);
            return mixer;
        }
        catch
        {
            micReader?.Dispose();
            micReader = null;
            return sysProvider;
        }
    }

    private static void ConvertAndMix(string sysWavPath, string? micWavPath,
                                      string mp3Path, int bitrate)
    {
        var preset = bitrate switch
        {
            32  => LAMEPreset.ABR_32,
            128 => LAMEPreset.ABR_128,
            _   => LAMEPreset.ABR_64
        };

        var mixed = BuildMixedProvider(sysWavPath, micWavPath,
                        out var sysReader, out var micReader);
        try
        {
            var pcm16 = new SampleToWaveProvider16(mixed);
            using var mp3Writer = new LameMP3FileWriter(mp3Path, pcm16.WaveFormat, preset);
            var buffer = new byte[8192];
            int read;
            while ((read = pcm16.Read(buffer, 0, buffer.Length)) > 0)
                mp3Writer.Write(buffer, 0, read);
        }
        finally
        {
            sysReader.Dispose();
            micReader?.Dispose();
        }
    }

    private static void MixToWav(string sysWavPath, string? micWavPath, string outPath)
    {
        var mixed = BuildMixedProvider(sysWavPath, micWavPath,
                        out var sysReader, out var micReader);
        try
        {
            var pcm16 = new SampleToWaveProvider16(mixed);
            WaveFileWriter.CreateWaveFile(outPath, pcm16);
        }
        finally
        {
            sysReader.Dispose();
            micReader?.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  LEVEL CALCULATORS
    // ──────────────────────────────────────────────────────────────
    private static float CalculateLevelFloat(byte[] buffer, int bytesRecorded)
    {
        float max = 0;
        for (int i = 0; i < bytesRecorded - 3; i += 4)
        {
            var sample = Math.Abs(BitConverter.ToSingle(buffer, i));
            if (sample > max) max = sample;
        }
        return Math.Min(1f, max);
    }

    private static float CalculateLevelPcm16(byte[] buffer, int bytesRecorded)
    {
        float max = 0;
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
            if (sample > max) max = sample;
        }
        return max;
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_isRecording) StopRecording();
    }
}
