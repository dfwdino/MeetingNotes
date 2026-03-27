using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.Ggml;

namespace MeetingNotes.Services;

public class TranscriptionService
{
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public event EventHandler<string>? SegmentTranscribed;

    public async Task<string> EnsureModelAsync(string modelType, string cacheFolder,
        IProgress<long>? progress = null)
    {
        Directory.CreateDirectory(cacheFolder);
        var ggmlType = ParseModelType(modelType);
        var modelPath = Path.Combine(cacheFolder, $"ggml-{modelType.ToLower()}.bin");

        if (!File.Exists(modelPath))
        {
            using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(ggmlType);
            using var fileStream = File.Create(modelPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await modelStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                progress?.Report(totalRead);
            }
        }

        return modelPath;
    }

    public void LoadModel(string modelPath)
    {
        if (_loadedModelPath == modelPath) return;
        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
    }

    public async Task<string> TranscribeFileAsync(string audioPath,
        CancellationToken cancellationToken = default)
    {
        if (_factory is null) throw new InvalidOperationException("Model not loaded.");

        var transcript = new System.Text.StringBuilder();

        using var processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        // Whisper.net requires a WAV/PCM stream — decode MP3 on the fly if needed
        // Whisper.net requires 16kHz mono 16-bit PCM WAV
        // AudioFileReader handles both MP3 and WAV, then we resample + convert
        var memoryStream = new MemoryStream();
        using (var audioFileReader = new AudioFileReader(audioPath))
        {
            var mono       = new StereoToMonoSampleProvider(audioFileReader);
            var resampled  = new WdlResamplingSampleProvider(mono, 16000);
            WaveFileWriter.WriteWavFileToStream(memoryStream, resampled.ToWaveProvider16());
        }
        memoryStream.Position = 0;

        try
        {
            await foreach (var segment in processor.ProcessAsync(memoryStream, cancellationToken))
            {
                var line = $"[{segment.Start:mm\\:ss}] {segment.Text.Trim()}";
                transcript.AppendLine(line);
                SegmentTranscribed?.Invoke(this, line);
            }
        }
        finally
        {
            memoryStream.Dispose();
        }

        return transcript.ToString();
    }

    private static GgmlType ParseModelType(string model) => model.ToLower() switch
    {
        "tiny"   => GgmlType.Tiny,
        "base"   => GgmlType.Base,
        "small"  => GgmlType.Small,
        "medium" => GgmlType.Medium,
        _        => GgmlType.Base
    };

    public void Dispose() => _factory?.Dispose();
}
