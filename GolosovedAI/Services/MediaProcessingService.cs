using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GolosovedAI.Services
{
    public class MediaProcessingService
    {
        /// <summary>
        /// Извлекает аудиодорожку из медиафайла в WAV 16kHz mono для Whisper.
        /// </summary>
        /// <param name="inputPath">Путь к исходному медиафайлу.</param>
        /// <returns>Путь к временному WAV-файлу.</returns>
        public async Task<string> ExtractAudioAsync(string inputPath)
        {
            string outputFile = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid()}.wav");

            // --- Добавляем фильтр silenceremove для удаления пауз ---
            // stop_periods=-1  — удалять все паузы
            // stop_duration=0.5 — паузы длиннее 0.5 секунды
            // stop_threshold=-45dB — порог тишины
            string args = $"-i \"{inputPath}\" -ac 1 -ar 16000 -sample_fmt s16 -af \"silenceremove=stop_periods=-1:stop_duration=0.5:stop_threshold=-45dB\" -y \"{outputFile}\"";

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Native", "ffmpeg.exe"),
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(psi);
            var errTask = process.StandardError.ReadToEndAsync();
            var outTask = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException(LocalizationManager.Get("Error_FFmpegTimeout"));
            }

            string error = await errTask;
            if (process.ExitCode != 0)
            {
                throw new Exception(LocalizationManager.Format("Error_FFmpegErrorMsg", error));
            }

            return outputFile;
        }
    }
}
