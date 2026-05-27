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

    /// <param name="audioPath">Path to the audio file to transcribe.</param>
    /// <param name="beamSize">
    /// Beam search width. 1 = greedy (fastest); 5 = recommended balance of speed and accuracy.
    /// Higher values are especially helpful for accented or non-native speech because Whisper
    /// scores multiple candidate sequences and picks the most likely overall, rather than
    /// committing greedily to each token in turn.
    /// </param>
    /// <param name="initialPrompt">
    /// Optional seed text Whisper sees before the audio. Use participant names, technical
    /// abbreviations, or domain vocabulary so Whisper maps unfamiliar pronunciations to the
    /// right words. Example: "Alice, Bob, sprint review, JIRA, API gateway."
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> TranscribeFileAsync(string audioPath,
        int beamSize = 5,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (_factory is null) throw new InvalidOperationException("Model not loaded.");

        var transcript = new System.Text.StringBuilder();

        // Sliding window of recent unique texts — suppresses Whisper's hallucination
        // loop where it repeats the last phrase endlessly over silent/low-energy audio.
        var recentTexts = new Queue<string>();
        const int dedupWindow = 10;

        var builder = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithProbabilities();

        // Prime Whisper with expected vocabulary (names, acronyms, domain terms).
        // This is especially effective for accented speech because Whisper biases its
        // token probabilities toward words it has already "seen" in the prompt.
        if (!string.IsNullOrWhiteSpace(initialPrompt))
            builder = builder.WithPrompt(initialPrompt);

        // Beam search evaluates beamSize candidate sequences simultaneously instead of
        // committing greedily to each token. Significantly reduces mis-transcriptions of
        // non-native pronunciation patterns. beamSize=1 reverts to greedy (default before
        // this change); beamSize=5 is the Whisper paper's recommended setting.
        //
        // WithBeamSearchSamplingStrategy() is declared as returning IWhisperSamplingStrategyBuilder
        // but always yields a BeamSearchSamplingStrategyBuilder at runtime. Cast to reach
        // WithBeamSize, which is only on the concrete type.
        using var processor = beamSize > 1
            ? ((BeamSearchSamplingStrategyBuilder)builder.WithBeamSearchSamplingStrategy())
                  .WithBeamSize(beamSize)
                  .ParentBuilder
                  .Build()
            : builder.Build();

        // Whisper.net requires 16kHz mono 16-bit PCM WAV; AudioFileReader handles MP3 and WAV.
        var memoryStream = new MemoryStream();
        using (var audioFileReader = new AudioFileReader(audioPath))
        {
            var mono      = new StereoToMonoSampleProvider(audioFileReader);
            var resampled = new WdlResamplingSampleProvider(mono, 16000);
            WaveFileWriter.WriteWavFileToStream(memoryStream, resampled.ToWaveProvider16());
        }
        memoryStream.Position = 0;

        try
        {
            var inDupeStreak = false;

            await foreach (var segment in processor.ProcessAsync(memoryStream, cancellationToken))
            {
                var text = segment.Text.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (recentTexts.Any(t => string.Equals(t, text, StringComparison.OrdinalIgnoreCase)))
                {
                    // First repeat in a streak: emit one marker so the user knows audio was unclear here.
                    // All further repeats in the same streak are dropped silently.
                    if (!inDupeStreak)
                    {
                        inDupeStreak = true;
                        var marker = $"[{segment.Start:mm\\:ss}] [Audio unclear]";
                        transcript.AppendLine(marker);
                        SegmentTranscribed?.Invoke(this, marker);
                    }
                    continue;
                }

                inDupeStreak = false;
                recentTexts.Enqueue(text);
                if (recentTexts.Count > dedupWindow)
                    recentTexts.Dequeue();

                // Whisper probability is 0–1; values below ~0.2 indicate very low confidence.
                // Threshold is intentionally kept low (was 0.3) because accented speakers
                // often produce valid transcriptions with probabilities in the 0.2–0.3 range —
                // a threshold of 0.3 was incorrectly flagging those as unclear.
                var display = segment.Probability is > 0f and < 0.2f
                    ? $"[Low confidence: {text}]"
                    : text;

                var line = $"[{segment.Start:mm\\:ss}] {display}";
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

    public void UnloadModel()
    {
        _factory?.Dispose();
        _factory = null;
        _loadedModelPath = null;
    }

    public void Dispose() => _factory?.Dispose();
}
