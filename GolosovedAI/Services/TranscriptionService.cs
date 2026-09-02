using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace GolosovedAI.Services
{
    public class TranscriptionService : IDisposable
    {
        private readonly WhisperFactory _factory;

        public TranscriptionService(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException(LocalizationManager.Format("Model_NotDownloaded", modelPath));

            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
            {
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu
            };

            _factory = WhisperFactory.FromPath(modelPath);
        }

        public async Task<TranscriptionResult> TranscribeAsync(
    string wavFilePath,
    string language = "auto",
    IProgress<int> progress = null,
    CancellationToken cancellationToken = default)
        {
            var result = new TranscriptionResult();
            if (!File.Exists(wavFilePath)) return result;

            using var fileStream = File.OpenRead(wavFilePath);

            var builder = _factory.CreateBuilder()
                .WithLanguage(language)
                .WithTemperature(0);

            if (progress != null)
            {
                builder.WithProgressHandler(p => progress.Report(p));
            }

            await using var processor = builder.Build();

            // Используем WithCancellation, чтобы прервать ProcessAsync при отмене
            var asyncEnumerable = processor.ProcessAsync(fileStream).WithCancellation(cancellationToken);

            await foreach (var segment in asyncEnumerable)
            {
                // Дополнительная проверка (на всякий случай)
                cancellationToken.ThrowIfCancellationRequested();

                result.Segments.Add(new TranscriptionResult.Segment
                {
                    Start = (float)segment.Start.TotalSeconds,
                    End = (float)segment.End.TotalSeconds,
                    Text = segment.Text.Trim()
                });
                result.Text += segment.Text + " ";
            }

            result.Text = result.Text.Trim();
            return result;
        }

        public void Dispose()
        {
            _factory?.Dispose();
        }
    }
}
