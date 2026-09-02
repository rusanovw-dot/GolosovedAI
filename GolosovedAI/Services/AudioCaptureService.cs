using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GolosovedAI.Services
{
    public class AudioCaptureService
    {
        private IWaveIn _captureDevice;
        private WaveFileWriter _writer;
        private string _tempRawFile;
        private bool _isRecording;
        private int _microphoneDeviceNumber = 0;

        public bool IsRecording => _isRecording;
        public event Action<string> RecordingFinished;

        public static List<(int DeviceNumber, string Name)> GetMicrophones()
        {
            var list = new List<(int, string)>();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                list.Add((i, caps.ProductName));
            }
            return list;
        }

        public void SetMicrophoneDevice(int deviceNumber)
        {
            _microphoneDeviceNumber = deviceNumber;
        }

        public void StartRecording(bool useSystemSound)
        {
            if (_isRecording) return;

            _tempRawFile = Path.Combine(Path.GetTempPath(), $"echo_raw_{Guid.NewGuid()}.wav");

            if (useSystemSound)
            {
                _captureDevice = new WasapiLoopbackCapture();
            }
            else
            {
                _captureDevice = new WaveInEvent
                {
                    DeviceNumber = _microphoneDeviceNumber,
                    WaveFormat = new WaveFormat(44100, 16, 1)
                };
            }

            _captureDevice.DataAvailable += OnDataAvailable;
            _captureDevice.RecordingStopped += OnRecordingStopped;
            _writer = new WaveFileWriter(_tempRawFile, _captureDevice.WaveFormat);
            _captureDevice.StartRecording();
            _isRecording = true;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_writer != null && e.BytesRecorded > 0)
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private async void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            _isRecording = false;
            _writer?.Dispose();
            _writer = null;
            _captureDevice?.Dispose();
            _captureDevice = null;

            // Запускаем тяжёлую конвертацию в фоновом потоке, чтобы не вешать интерфейс
            string rawFile = _tempRawFile;
            string finalWav = null;

            try
            {
                finalWav = await Task.Run(() => ConvertToWhisperFormat(rawFile));
            }
            catch (Exception ex)
            {
                // Показываем ошибку в UI-потоке
                Application.Current.Dispatcher.Invoke(() =>
                    LocalizationManager.Show("Error_AudioConversion", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }

            // Оповещаем подписчиков в UI-потоке
            Application.Current.Dispatcher.Invoke(() =>
            {
                RecordingFinished?.Invoke(finalWav);
            });
        }

        public void StopRecording()
        {
            if (!_isRecording) return;
            _captureDevice.StopRecording();
        }

        private string ConvertToWhisperFormat(string rawFile)
        {
            string outputFile = Path.Combine(Path.GetTempPath(), $"echo_final_{Guid.NewGuid()}.wav");
            string args = $"-i \"{rawFile}\" -ac 1 -ar 16000 -sample_fmt s16 -y \"{outputFile}\"";

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

            // Запускаем чтение потоков ДО ожидания завершения, чтобы избежать deadlock
            var stdErrTask = process.StandardError.ReadToEndAsync();
            var stdOutTask = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException(LocalizationManager.Get("Error_FFmpegTimeout"));
            }

            // Получаем вывод после завершения
            string stdErr = stdErrTask.Result;
            string stdOut = stdOutTask.Result;

            if (process.ExitCode != 0)
            {
                throw new Exception(LocalizationManager.Format("Error_FFmpegExitedWithCode", process.ExitCode, stdErr));
            }

            if (File.Exists(rawFile)) File.Delete(rawFile);
            return outputFile;
        }
    }
}
