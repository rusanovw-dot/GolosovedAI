using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace GolosovedAI.Services
{
    public class AudioExtractionService
    {
        private readonly string _ffmpegPath;

        public AudioExtractionService()
        {
            // Ищем ffmpeg.exe в папке Native рядом с исполняемым файлом
            _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Native", "ffmpeg.exe");
            if (!File.Exists(_ffmpegPath))
                throw new FileNotFoundException($"FFmpeg не найден: {_ffmpegPath}");
        }

        /// <summary>
        /// Извлекает аудио из видео/аудио в WAV 16kHz mono 16-bit
        /// </summary>
        /// <param name="inputPath">Путь к исходному файлу</param>
        /// <returns>Путь к временному WAV-файлу</returns>
        public async Task<string> ExtractAudioAsync(string inputPath)
        {
            // Создаём временный файл для аудио
            string tempWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

            // Параметры FFmpeg: моно, 16kHz, 16-bit PCM
            string args = $"-i \"{inputPath}\" -ac 1 -ar 16000 -sample_fmt s16 -y \"{tempWav}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(processStartInfo);
            // Ждём завершения
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                // Сначала покажем ошибку в отдельном окне, чтобы мы её увидели
                System.Windows.MessageBox.Show($"Ошибка FFmpeg:\n{error}", "Ошибка FFmpeg");
                throw new Exception($"FFmpeg завершился с ошибкой: {error}");
            }

            return tempWav;
        }
    }
}
