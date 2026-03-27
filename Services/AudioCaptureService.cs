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

    public bool    IsRecording    => _isRecording;
    public string? CurrentFilePath => _finalOutputPath;

    public event EventHandler<float>? AudioLevelChanged;

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

        // Always clean up temp files even if conversion throws
        try
        {
            if (!string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath))
            {
                if (_requestedFormat == "MP3")
                    ConvertAndMix(_tempWavPath, _micTempPath, _finalOutputPath!, _requestedBitrate);
                else
                    MixToWav(_tempWavPath, _micTempPath, _finalOutputPath!);
            }
        }
        finally
        {
            DeleteFileIfExists(_tempWavPath);
            DeleteFileIfExists(_micTempPath);
        }

        return _finalOutputPath ?? string.Empty;
    }

    // ──────────────────────────────────────────────────────────────
    //  AUDIO CALLBACKS
    // ──────────────────────────────────────────────────────────────
    private void OnSystemAudioAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // One-shot: on the very first loopback packet, pad the file with silence
        // covering the gap between StartRecording() and now.  This aligns the
        // loopback WAV with the mic WAV (which starts writing immediately).
        // Done once only — never touches the file again, so no mid-stream echo.
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

        _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        UpdatePeakLevel(CalculateLevelFloat(e.Buffer, e.BytesRecorded));
    }

    private void OnMicAudioAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        _micWavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        UpdatePeakLevel(CalculateLevelPcm16(e.Buffer, e.BytesRecorded));
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
