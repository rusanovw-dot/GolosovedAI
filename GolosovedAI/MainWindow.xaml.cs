using GolosovedAI.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GolosovedAI
{
    public partial class MainWindow : Window
    {
        private AudioCaptureService _audioCapture;
        private bool _isInitializing = false;
        private TranscriptionResult _lastTranscription;
        private CancellationTokenSource _cancellationTokenSource;
        private SummarizationService _summarizationService;

        private readonly Dictionary<string, (string DisplayName, string Url, string Size)> _aiModels = new()
        {
            { "qwen2.5-3b-instruct-q4_k_m.gguf", ("Qwen2.5-3B (Q4_K_M)", "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf", "2.1 ГБ") }
        };

        public MainWindow()
        {
            InitializeComponent();
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "donate_qr.png");
            if (System.IO.File.Exists(path))
                DonateQRImage.Source = new BitmapImage(new Uri(path));


            _isInitializing = true;
            var settings = AppSettings.Load();

            if (!string.IsNullOrEmpty(settings.LastAIModelTag))
            {
                foreach (ComboBoxItem item in AIModelCombo.Items)
                {
                    if (item.Tag?.ToString() == settings.LastAIModelTag)
                    {
                        AIModelCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            foreach (ComboBoxItem item in ModelCombo.Items)
            {
                if (item.Tag?.ToString() == settings.LastModelTag)
                {
                    ModelCombo.SelectedItem = item;
                    break;
                }
            }

            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == settings.LastLanguageTag)
                {
                    LanguageCombo.SelectedItem = item;
                    break;
                }
            }

            // Восстанавливаем шаблон (старая логика)
            if (!string.IsNullOrEmpty(settings.SelectedTemplate))
            {
                foreach (ComboBoxItem item in TemplateCombo.Items)
                {
                    if (item.Content?.ToString() == settings.SelectedTemplate)
                    {
                        TemplateCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            SaveAudioGlobalCheckBox.IsChecked = settings.SaveAudioForFiles;
            SaveAudioRecordingCheckBox.IsChecked = settings.SaveAudioForRecording;

            if (!string.IsNullOrEmpty(settings.SaveFormat))
            {
                foreach (ComboBoxItem item in FormatCombo.Items)
                {
                    if (item.Tag?.ToString() == settings.SaveFormat)
                    {
                        FormatCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(settings.LastMicrophoneTag))
            {
                foreach (ComboBoxItem item in MicrophoneCombo.Items)
                {
                    if (item.Content?.ToString() == settings.LastMicrophoneTag)
                    {
                        MicrophoneCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            _audioCapture = new AudioCaptureService();
            _summarizationService = new SummarizationService();
            _audioCapture.RecordingFinished += OnRecordingFinished;
            RefreshMicrophones();

            MicrophoneCombo.SelectionChanged += MicrophoneCombo_SelectionChanged;
            TemplateCombo.SelectionChanged += TemplateCombo_SelectionChanged;
            FormatCombo.SelectionChanged += FormatCombo_SelectionChanged;
            SaveAudioGlobalCheckBox.Checked += SaveAudioCheckBox_Changed;
            SaveAudioGlobalCheckBox.Unchecked += SaveAudioCheckBox_Changed;
            SaveAudioRecordingCheckBox.Checked += SaveAudioCheckBox_Changed;
            SaveAudioRecordingCheckBox.Unchecked += SaveAudioCheckBox_Changed;

            RefreshModelStatus();
            RefreshAIModels();
            UpdateWhisperStatus();
            UpdateAIStatus();

            if (!string.IsNullOrEmpty(settings.LastAIModelTag))
            {
                foreach (ComboBoxItem item in AIModelCombo.Items)
                {
                    if (item.Tag?.ToString() == settings.LastAIModelTag)
                    {
                        AIModelCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            SummaryTextBox.TextChanged += (s, e) =>
            {
                SummaryPlaceholder.Visibility = string.IsNullOrEmpty(SummaryTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            SummaryPlaceholder.Visibility = Visibility.Visible;

            LoadAIModelFromSelected();
            // Применить сохранённую тему
            bool isLight = AppSettings.Current.IsLightTheme;
            ApplyTheme(isLight);
            ThemeToggle.Content = isLight ? "🌙" : "☀️";

            _isInitializing = false;
            CheckAndSuggestModelOnStartup();
            LoadSettings();
            this.Closed += OnWindowClosed;
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем сохранённый язык из настроек (по умолчанию "ru-RU")
            string currentLang = AppSettings.Current.Language ?? "ru-RU";
            UpdateLanguageButtons(currentLang);
        }
        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = LocalizationManager.Get("Dialog_OpenMediaFilter"),
                Title = LocalizationManager.Get("Dialog_OpenMediaTitle")
            };
            if (dlg.ShowDialog() != true) return;

            ProcessFile(dlg.FileName);
        }

        private async void ProcessFile(string filePath)
        {
            if (!EnsureModelExists(out string modelPath))
                return;

            // --- Проверка на текстовый файл ---
            string extension = Path.GetExtension(filePath).ToLower();
            if (extension == ".txt")
            {
                try
                {
                    StatusText.Text = LocalizationManager.Get("Status_LoadingTextFile");
                    string content = ReadTextFileWithEncoding(filePath);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        ResultTextBox.Text = LocalizationManager.Get("Result_FileEmpty");
                        MainTabControl.SelectedIndex = 0;
                        StatusText.Text = LocalizationManager.Get("Status_ReadyEmptyFile");
                        return;
                    }
                    ResultTextBox.Text = FormatTextWithParagraphs(content);
                    MainTabControl.SelectedIndex = 0;
                    _lastTranscription = new TranscriptionResult { Text = content };
                    SummaryTextBox.Text = "";
                    SummaryPlaceholder.Visibility = Visibility.Visible;
                    StatusText.Text = LocalizationManager.Format("Status_TextFileLoaded", Path.GetFileName(filePath));
                    RecordingStatus.Text = "";
                    ProcessingProgress.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    LocalizationManager.Show("Error_ReadTextFile", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, isRawText: false);
                    StatusText.Text = LocalizationManager.Get("Status_ErrorReadingTxt");
                }
                return;
            }
            if (extension == ".pdf")
            {
                try
                {
                    StatusText.Text = LocalizationManager.Get("Status_ExtractingPdf");
                    string content = ExtractTextFromPdf(filePath);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        ResultTextBox.Text = LocalizationManager.Get("Result_PdfNoText");
                        StatusText.Text = LocalizationManager.Get("Status_ReadyEmptyPdf");
                        return;
                    }
                    ResultTextBox.Text = FormatTextWithParagraphs(content);
                    MainTabControl.SelectedIndex = 0;
                    _lastTranscription = new TranscriptionResult { Text = content };
                    SummaryTextBox.Text = "";
                    SummaryPlaceholder.Visibility = Visibility.Visible;
                    StatusText.Text = LocalizationManager.Format("Status_PdfTextLoaded", Path.GetFileName(filePath));
                    RecordingStatus.Text = "";
                    ProcessingProgress.Visibility = Visibility.Collapsed;
                    CancelButton.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    LocalizationManager.Show("Error_ExtractPdf", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, isRawText: false);
                    StatusText.Text = LocalizationManager.Get("Status_ErrorReadingPdf");
                }
                return;
            }

            // --- Обычная обработка аудио/видео ---
            StatusText.Text = LocalizationManager.Get("Status_Converting");
            RecordingStatus.Text = "";
            ProcessingProgress.Visibility = Visibility.Visible;
            ProcessingProgress.Value = 0;
            ResultTextBox.Text = "";

            // Создаём токен отмены и показываем кнопку
            _cancellationTokenSource = new CancellationTokenSource();
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;

            string tempWav = null;
            string savedAudioPath = null;

            try
            {
                var extractor = new MediaProcessingService();
                tempWav = await extractor.ExtractAudioAsync(filePath);
                ProcessingProgress.Value = 15;

                if (AppSettings.Current.SaveAudioForFiles)
                {
                    string targetFolder = AppSettings.Current.CustomAudioFolder;
                    if (string.IsNullOrWhiteSpace(targetFolder))
                        targetFolder = Path.GetDirectoryName(filePath);
                    Directory.CreateDirectory(targetFolder);
                    string savedAudio = Path.Combine(targetFolder,
                        Path.GetFileNameWithoutExtension(filePath) + "_audio.wav");
                    File.Copy(tempWav, savedAudio, overwrite: true);
                    savedAudioPath = savedAudio;
                    StatusText.Text = LocalizationManager.Format("Status_AudioSavedAndRecognizing", savedAudio);
                }
                else
                {
                    StatusText.Text = LocalizationManager.Get("Status_RecognizingInProgress");
                }

                // VAD
                string vadWav = VadService.RemoveSilence(tempWav, silenceThresholdDb: -40, minSpeechBlocks: 10, minSilenceBlocks: 30, preSpeechBlocks: 10);
                ProcessingProgress.Value = 30;

                StatusText.Text = LocalizationManager.Get("Status_RecognizingSpeech");
                using var service = new TranscriptionService(modelPath);
                string langCode = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";

                var progress = new Progress<int>(percentFromWhisper =>
                {
                    int overallPercent = 30 + (int)((float)percentFromWhisper / 100 * 70);
                    Dispatcher.Invoke(() =>
                    {
                        ProcessingProgress.Value = overallPercent;
                        StatusText.Text = LocalizationManager.Format("Status_RecognizingProgress", overallPercent);
                    });
                });

                // Передаём токен в TranscribeAsync
                var transcription = await service.TranscribeAsync(vadWav, langCode, progress, _cancellationTokenSource.Token);
                _lastTranscription = transcription;

                if (File.Exists(vadWav) && vadWav != tempWav)
                    File.Delete(vadWav);

                ResultTextBox.Text = string.IsNullOrWhiteSpace(transcription.Text)
                    ? LocalizationManager.Get("Result_SpeechNotRecognized")
                    : FormatTextWithParagraphs(transcription.Text);
                MainTabControl.SelectedIndex = 0;
                SummaryTextBox.Text = "";
                SummaryPlaceholder.Visibility = Visibility.Visible;

                ProcessingProgress.Value = 100;
                if (!string.IsNullOrEmpty(savedAudioPath))
                    StatusText.Text = LocalizationManager.Format("Status_DoneAudioSaved", savedAudioPath);
                else
                    StatusText.Text = LocalizationManager.Get("Status_Done");
                RecordingStatus.Text = "";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = LocalizationManager.Get("Status_RecognitionCancelled");
                RecordingStatus.Text = "";
                ProcessingProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                // Не возвращаемся, чтобы finally отработал
            }
            catch (Exception ex)
            {
                LocalizationManager.Show(LocalizationManager.Format("Error_Generic", ex.Message), "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, isRawText: true);
                StatusText.Text = LocalizationManager.Get("Title_Error");
                RecordingStatus.Text = "";
            }
            finally
            {
                if (tempWav != null && File.Exists(tempWav))
                    File.Delete(tempWav);
                ProcessingProgress.Visibility = Visibility.Collapsed;
                ProcessingProgress.Value = 0;
                CancelButton.Visibility = Visibility.Collapsed;
                CancelButton.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        private async void DownloadModel_Click(object sender, RoutedEventArgs e)
        {
            string modelFile = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(modelFile))
            {
                MessageBox.Show("Сначала выберите модель из списка.");
                return;
            }

            string modelUrl = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelFile}";
            string modelFolder = AppSettings.GetModelsFolder();
            string modelPath = Path.Combine(modelFolder, modelFile);

            if (File.Exists(modelPath))
            {
                DownloadProgressText.Text = "Уже скачана";
                StatusText.Text = $"Модель {modelFile} уже загружена.";
                return;
            }

            Directory.CreateDirectory(modelFolder);

            var result = MessageBox.Show($"Скачать модель {modelFile}?\nЭто может занять несколько минут.", "Скачивание модели", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return;

            var downloadButton = sender as Button;
            if (downloadButton != null) downloadButton.IsEnabled = false;
            DownloadProgressText.Text = "0%";

            try
            {
                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long downloaded = 0;
                int read;
                int lastPercent = 0;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes.HasValue)
                    {
                        int percent = (int)(downloaded * 100 / totalBytes.Value);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            DownloadProgressText.Text = $"{percent}%";
                        }
                    }
                }

                DownloadProgressText.Text = "Готово";
                StatusText.Text = $"Модель {modelFile} загружена. Можете использовать.";
                RefreshModelStatus();
                UpdateWhisperStatus();
            }
            catch (Exception ex)
            {
                DownloadProgressText.Text = "Ошибка";
                StatusText.Text = "Ошибка скачивания";
                MessageBox.Show($"Не удалось скачать модель: {ex.Message}", "Ошибка");
                if (File.Exists(modelPath)) File.Delete(modelPath);
            }
            finally
            {
                if (downloadButton != null) downloadButton.IsEnabled = true;
                // Через 3 секунды очищаем прогресс и статус
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressText.Text = "";
                        StatusText.Text = "";
                    });
                });
            }
        }

        private void SaveResult_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text))
            {
                MessageBox.Show("Нет текста для сохранения.");
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Текстовые файлы|*.txt",
                FileName = "Распознанный_текст.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, ResultTextBox.Text);
                StatusText.Text = "Сохранено: " + System.IO.Path.GetFileName(dlg.FileName);
            }
        }
        private void SaveResultAs_Click(object sender, RoutedEventArgs e)
        {
            // Определяем, какая вкладка активна (0 – полный текст, 1 – резюме)
            bool isSummaryTabActive = (MainTabControl.SelectedIndex == 1);

            // Если активна вкладка резюме, проверяем, есть ли что сохранять
            // --- Вкладка резюме ---
            if (isSummaryTabActive)
            {
                if (string.IsNullOrWhiteSpace(SummaryTextBox.Text))
                {
                    MessageBox.Show("Нет резюме для сохранения. Сначала сгенерируйте AI-резюме.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string format = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "txt";
                if (format != "txt" && format != "md" && format != "pdf")
                {
                    MessageBox.Show("Для резюме доступны форматы TXT, MD и PDF. Выберите один из них.", "Неподдерживаемый формат", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string extension = format == "pdf" ? ".pdf" : (format == "md" ? ".md" : ".txt");
                string filter = format == "pdf" ? "PDF файлы|*.pdf" : (format == "md" ? "Markdown файлы|*.md" : "Текстовые файлы|*.txt");

                var dlg = new SaveFileDialog
                {
                    Filter = filter,
                    FileName = $"Резюме_{DateTime.Now:yyyyMMdd_HHmmss}{extension}"
                };
                if (dlg.ShowDialog() != true) return;

                if (format == "pdf")
                {
                    PdfExportService.CreatePdf(SummaryTextBox.Text, dlg.FileName);
                }
                else
                {
                    File.WriteAllText(dlg.FileName, SummaryTextBox.Text, Encoding.UTF8);
                }
                StatusText.Text = $"Резюме сохранено: {Path.GetFileName(dlg.FileName)}";
                return;
            }

            // --- Вкладка полного текста (существующая логика) ---
            if (_lastTranscription == null || string.IsNullOrWhiteSpace(_lastTranscription.Text))
            {
                MessageBox.Show("Нет данных для сохранения. Сначала распознайте речь.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string formatFull = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "txt";
            string filterFull = formatFull switch
            {
                "srt" => "SRT файлы|*.srt",
                "json" => "JSON файлы|*.json",
                "vtt" => "VTT файлы|*.vtt",
                "md" => "Markdown файлы|*.md",
                "pdf" => "PDF файлы|*.pdf",
                _ => "Текстовые файлы|*.txt"
            };

            var dlgFull = new SaveFileDialog
            {
                Filter = filterFull,
                FileName = $"Распознанный_текст_{DateTime.Now:yyyyMMdd_HHmmss}.{formatFull}"
            };
            if (dlgFull.ShowDialog() != true) return;

            string contentFull = formatFull switch
            {
                "srt" => GenerateSrt(_lastTranscription),
                "json" => GenerateJson(_lastTranscription),
                "vtt" => GenerateVtt(_lastTranscription),
                "md" => ResultTextBox.Text,
                "pdf" => ResultTextBox.Text, // для PDF используем тот же текст
                _ => ResultTextBox.Text
            };

            if (formatFull == "pdf")
            {
                // Вызываем наш чистый автономный iText-конвертер
                PdfExportService.CreatePdf(contentFull, dlgFull.FileName);
            }
            else
            {
                // Все остальные текстовые форматы (TXT, SRT, VTT, JSON, MD) пишем стандартно
                File.WriteAllText(dlgFull.FileName, contentFull, Encoding.UTF8);
            }

            // УДАЛЕНО: Строка File.WriteAllText, которая затирала PDF и ломала поток файла!

            StatusText.Text = $"Сохранено: {Path.GetFileName(dlgFull.FileName)}";
        }

        // ===== Генераторы форматов =====
        private string GenerateSrt(TranscriptionResult result)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < result.Segments.Count; i++)
            {
                var seg = result.Segments[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
                sb.AppendLine(seg.Text);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string GenerateVtt(TranscriptionResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("WEBVTT\n");
            for (int i = 0; i < result.Segments.Count; i++)
            {
                var seg = result.Segments[i];
                sb.AppendLine($"{FormatVttTime(seg.Start)} --> {FormatVttTime(seg.End)}");
                sb.AppendLine(seg.Text);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string GenerateJson(TranscriptionResult result)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
        }

        private string FormatSrtTime(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        private string FormatVttTime(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            bool useSystemSound = RadioSystemSound.IsChecked == true;

            if (!useSystemSound)
            {
                var selectedItem = MicrophoneCombo.SelectedItem as ComboBoxItem;
                if (selectedItem != null && selectedItem.Tag is int deviceNum)
                    _audioCapture.SetMicrophoneDevice(deviceNum);
            }

            try
            {
                SetControlsEnabled(false);   // блокируем до старта записи
                _audioCapture.StartRecording(useSystemSound);
                RecordingStatus.Text = useSystemSound ? "Идёт запись системного звука..." : "Идёт запись с микрофона...";
            }
            catch (Exception ex)
            {
                SetControlsEnabled(true);   // если ошибка — разблокируем
                MessageBox.Show($"Ошибка при начале записи: {ex.Message}");
            }
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            _audioCapture.StopRecording();
            RecordingStatus.Text = "Останавливаю...";
        }
        private void RefreshMicrophones()
        {
            MicrophoneCombo.Items.Clear();
            var mics = AudioCaptureService.GetMicrophones();
            foreach (var (num, name) in mics)
            {
                // Ограничиваем отображаемое имя 30 символами, добавляем многоточие
                string displayName = name.Length > 30 ? name.Substring(0, 27) + "..." : name;

                MicrophoneCombo.Items.Add(new ComboBoxItem
                {
                    Content = displayName,
                    Tag = num
                });
            }
            if (MicrophoneCombo.Items.Count > 0)
                MicrophoneCombo.SelectedIndex = 0;
        }
        private void RefreshAIModels()
        {
            AIModelCombo.Items.Clear();
            // Сортировка по размеру (можно сортировать по размеру, но проще вручную перечислить в нужном порядке)
            // Для сортировки по размеру нужно распарсить размер, но пока просто перечислим в нужном порядке.
            // Можно отсортировать по Size, но проще задать порядок в словаре, но словарь не гарантирует порядок.
            // Поэтому отсортируем список перед добавлением.
            var sortedModels = _aiModels.OrderBy(m =>
            {
                // Извлекаем числовое значение из размера (например, "0.7 ГБ" -> 0.7)
                var sizeStr = m.Value.Size.Replace(" ГБ", "").Replace(" ", "");
                if (double.TryParse(sizeStr, out double size))
                    return size;
                return double.MaxValue;
            }).ToList();

            foreach (var model in sortedModels)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{model.Value.DisplayName} ({model.Value.Size})",
                    Tag = model.Key // имя файла
                };
                // Проверяем, скачана ли модель
                string modelPath = Path.Combine(AppSettings.GetModelsFolder(), model.Key);
                if (File.Exists(modelPath))
                    item.Content += " ✔";
                AIModelCombo.Items.Add(item);
            }
            if (AIModelCombo.Items.Count > 0)
                AIModelCombo.SelectedIndex = 0;
        }
        private void SaveAudioCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            var settings = AppSettings.Current;
            settings.SaveAudioForFiles = SaveAudioGlobalCheckBox.IsChecked ?? false;
            settings.SaveAudioForRecording = SaveAudioRecordingCheckBox.IsChecked ?? false;
            settings.Save();
        }
        private void UpdateAIStatus()
        {
            var selectedItem = AIModelCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null || selectedItem.Tag == null)
            {
                AIStatusText.Text = "Модель не выбрана";
                return;
            }

            string fileName = selectedItem.Tag.ToString();
            string modelPath = Path.Combine(AppSettings.GetModelsFolder(), fileName);

            if (File.Exists(modelPath))
            {
                // Модель скачана – показываем это
                AIStatusText.Text = $"Модель {selectedItem.Content} скачана";
            }
            else
            {
                AIStatusText.Text = "Модель не скачана. Нажмите 'Скачать'.";
            }
        }
        private async void DownloadAIModel_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = AIModelCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null || selectedItem.Tag == null)
            {
                MessageBox.Show("Сначала выберите модель из списка.");
                return;
            }

            string fileName = selectedItem.Tag.ToString();
            if (!_aiModels.TryGetValue(fileName, out var modelInfo))
            {
                MessageBox.Show("Модель не найдена в списке.");
                return;
            }

            string url = modelInfo.Url;
            string modelFolder = AppSettings.GetModelsFolder();
            string modelPath = Path.Combine(modelFolder, fileName);

            // Проверяем, скачана ли уже
            if (File.Exists(modelPath))
            {
                AIDownloadProgress.Text = "Уже скачана";
                AIStatusText.Text = $"Модель {modelInfo.DisplayName} уже загружена.";
                return;
            }

            Directory.CreateDirectory(modelFolder);

            var result = MessageBox.Show($"Скачать модель {modelInfo.DisplayName}? (размер {modelInfo.Size})\nЭто может занять несколько минут.", "Скачивание AI-модели", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return;

            var downloadButton = sender as Button;
            if (downloadButton != null) downloadButton.IsEnabled = false;
            AIDownloadProgress.Text = "0%";
            AIStatusText.Text = "Скачивание...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(30);
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long downloaded = 0;
                int read;
                int lastPercent = 0;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes.HasValue)
                    {
                        int percent = (int)(downloaded * 100 / totalBytes.Value);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            AIDownloadProgress.Text = $"{percent}%";
                        }
                    }
                }

                AIDownloadProgress.Text = "Готово";
                AIStatusText.Text = $"Модель {modelInfo.DisplayName} загружена.";
                RefreshAIModels();
                UpdateAIStatus();
            }
            catch (Exception ex)
            {
                AIDownloadProgress.Text = "Ошибка";
                AIStatusText.Text = "Ошибка скачивания";
                MessageBox.Show($"Не удалось скачать модель: {ex.Message}", "Ошибка");
                if (File.Exists(modelPath)) File.Delete(modelPath);
            }
            finally
            {
                if (downloadButton != null) downloadButton.IsEnabled = true;
                // после завершения скачивания
                RefreshAIModels();
                UpdateAIStatus();
            }
        }
        private void LoadAIModelFromSelected()
        {
            var selectedItem = AIModelCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null || selectedItem.Tag == null)
            {
                AIStatusText.Text = "Модель не выбрана";
                return;
            }

            string fileName = selectedItem.Tag.ToString();
            string modelPath = Path.Combine(AppSettings.GetModelsFolder(), fileName);

            if (!File.Exists(modelPath))
            {
                AIStatusText.Text = "Модель не скачана. Нажмите 'Скачать'.";
                return;
            }

            try
            {
                AIStatusText.Text = "Загрузка AI-модели...";
                // Синхронная загрузка (может занять несколько секунд)
                _summarizationService.LoadModel(modelPath);
                AIStatusText.Text = "AI-модель загружена";
            }
            catch (Exception ex)
            {
                AIStatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Не удалось загрузить AI-модель: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void RefreshModelStatus()
        {
            foreach (ComboBoxItem item in ModelCombo.Items)
            {
                string modelFile = item.Tag?.ToString();
                if (string.IsNullOrEmpty(modelFile)) continue;

                string modelPath = Path.Combine(AppSettings.GetModelsFolder(), modelFile);
                // Сохраняем оригинальное название (без пометок) в отдельном поле, если ещё не сохранили
                if (item.ToolTip == null || !item.ToolTip.ToString().StartsWith("ORIG:"))
                {
                    item.ToolTip = "ORIG:" + item.Content.ToString();
                }
                string originalName = item.ToolTip.ToString().Replace("ORIG:", "");

                if (File.Exists(modelPath))
                    item.Content = $"{originalName} ✔";
                else
                    item.Content = originalName;
            }
            ModelCombo.Items.Refresh();
            UpdateWhisperStatus();
        }
        private void UpdateWhisperStatus()
        {
            if (WhisperStatusText == null) return; // защита от null

            var selectedItem = ModelCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null || selectedItem.Tag == null)
            {
                WhisperStatusText.Text = "Модель не выбрана";
                return;
            }

            string fileName = selectedItem.Tag.ToString();
            string modelPath = Path.Combine(AppSettings.GetModelsFolder(), fileName);

            if (File.Exists(modelPath))
            {
                WhisperStatusText.Text = $"Модель {selectedItem.Content} скачана";
            }
            else
            {
                WhisperStatusText.Text = "Модель не скачана. Нажмите 'Скачать'.";
            }
        }
        private void RefreshMicrophones_Click(object sender, RoutedEventArgs e)
        {
            RefreshMicrophones();
            MessageBox.Show("Список микрофонов обновлён.", "Обновлено");
        }
        private void OnWindowClosed(object sender, EventArgs e)
        {
            try
            {
                var settings = AppSettings.Current;

                settings.LastModelTag = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ggml-small.bin";
                settings.LastLanguageTag = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
                settings.LastAIModelTag = (AIModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
                settings.SelectedTemplate = (TemplateCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Совещание";
                settings.SaveAudioForFiles = SaveAudioGlobalCheckBox.IsChecked ?? false;
                settings.SaveAudioForRecording = SaveAudioRecordingCheckBox.IsChecked ?? false;
                settings.SaveFormat = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "txt";
                settings.LastMicrophoneTag = (MicrophoneCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                settings.CustomAudioFolder = AudioFolderTextBox.Text;
                settings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка сохранения настроек: " + ex.Message);
            }
        }
        private void LoadSettings()
        {
            var settings = AppSettings.Current;

            // 1. Восстанавливаем микрофон
            if (!string.IsNullOrEmpty(settings.LastMicrophoneTag))
            {
                foreach (ComboBoxItem item in MicrophoneCombo.Items)
                {
                    if (item.Content?.ToString() == settings.LastMicrophoneTag)
                    {
                        MicrophoneCombo.SelectedItem = item;
                        break;
                    }
                }
            }


            // 3. Чекбокс сохранения аудио (синхронизируем оба чекбокса)
            SaveAudioGlobalCheckBox.IsChecked = settings.SaveAudioForFiles;
            SaveAudioRecordingCheckBox.IsChecked = settings.SaveAudioForRecording;

            // 4. Формат сохранения
            if (!string.IsNullOrEmpty(settings.SaveFormat))
            {
                foreach (ComboBoxItem item in FormatCombo.Items)
                {
                    if (item.Tag?.ToString()?.ToLower() == settings.SaveFormat.ToLower())
                    {
                        FormatCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            // 5. Пользовательская папка для аудио (если есть)
            AudioFolderTextBox.Text = settings.CustomAudioFolder;
            if (!string.IsNullOrEmpty(settings.CustomAudioFolder))
            {
                // У нас пока нет поля для отображения папки, но мы сохраним её в переменную, чтобы использовать позже.
                // Для демонстрации можно вывести в статус.
                StatusText.Text = $"Папка для аудио: {settings.CustomAudioFolder}";
            }
        }
        private void MicrophoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            var selected = MicrophoneCombo.SelectedItem as ComboBoxItem;
            if (selected != null && selected.Content != null)
            {
                AppSettings.Current.LastMicrophoneTag = selected.Content.ToString();
                AppSettings.Current.Save();
            }
        }

        private void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            var selected = TemplateCombo.SelectedItem as ComboBoxItem;
            if (selected != null && selected.Content != null)
            {
                AppSettings.Current.SelectedTemplate = selected.Content.ToString();
                AppSettings.Current.Save();
            }
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            var selected = FormatCombo.SelectedItem as ComboBoxItem;
            if (selected != null && selected.Tag != null)
            {
                AppSettings.Current.SaveFormat = selected.Tag.ToString().ToLower();
                AppSettings.Current.Save();
            }
        }
        private void ResultTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PlaceholderText != null)
                PlaceholderText.Visibility = string.IsNullOrEmpty(ResultTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    ProcessFile(files[0]);   // распознаём первый перетащенный файл
                }
            }
        }
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            // Разрешаем копирование, если перетаскивается файл
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void TextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    ProcessFile(files[0]);   // запускаем распознавание
                }
            }
            e.Handled = true;
        }
        private void SetControlsEnabled(bool enabled)
        {
            ModelCombo.IsEnabled = enabled;
            LanguageCombo.IsEnabled = enabled;
            MicrophoneCombo.IsEnabled = enabled;
            RadioSystemSound.IsEnabled = enabled;
            RadioMicrophone.IsEnabled = enabled;
            SaveAudioGlobalCheckBox.IsEnabled = enabled;
            // Кнопки (кроме Стоп)
            ButtonSelectFile.IsEnabled = enabled;
            ButtonDownloadModel.IsEnabled = enabled;
            ButtonSaveAs.IsEnabled = enabled;
            ButtonStartRecording.IsEnabled = enabled;
        }
        private void AIModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Очищаем прогресс и обновляем статус для выбранной модели
            AIDownloadProgress.Text = "";
            UpdateAIStatus();
        }
        private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateWhisperStatus();
        }
        private async void GenerateSummary_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text) || ResultTextBox.Text == "Речь не распознана." || ResultTextBox.Text == "Результат расшифровки появится здесь...")
            {
                MessageBox.Show("Сначала распознайте речь или откройте файл.", "Нет текста", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_summarizationService.IsLoaded)
            {
                var selectedItem = AIModelCombo.SelectedItem as ComboBoxItem;
                string fileName = selectedItem?.Tag?.ToString();
                string modelPath = Path.Combine(AppSettings.GetModelsFolder(), fileName);

                if (!File.Exists(modelPath))
                {
                    MessageBox.Show(
                        "AI-модель для резюмирования не скачана.\n\n" +
                        "Для создания резюме необходима языковая модель (LLM).\n" +
                        "Рекомендуем использовать модель 'Qwen2.5-3B (Q4_K_M)' (2.1 ГБ) — она отлично работает с русским языком.\n\n" +
                        "Скачайте модель через интерфейс:\n" +
                        "1. В левой панели найдите раздел 'AI-Резюмирование'.\n" +
                        "2. Выберите модель 'Qwen2.5-3B (Q4_K_M)'.\n" +
                        "3. Нажмите кнопку 'Скачать'.\n" +
                        "После скачивания нажмите 'Создать AI-резюме' снова.",
                        "Модель не скачана", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    LoadAIModelFromSelected();
                    if (!_summarizationService.IsLoaded)
                    {
                        MessageBox.Show("Не удалось загрузить модель. Попробуйте перезапустить программу или скачать модель заново.", "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке модели: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            ProcessingProgress.Visibility = Visibility.Visible;
            ProcessingProgress.IsIndeterminate = false;
            ProcessingProgress.Value = 0;
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            StatusText.Text = "Генерация AI-резюме...";

            (sender as Button).IsEnabled = false;

            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                string template = (TemplateCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Совещание";

                var progress = new Progress<int>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProcessingProgress.Value = percent;
                        StatusText.Text = $"Генерация AI-резюме... {percent}%";
                    });
                });

                string summary = await _summarizationService.GenerateSummaryAsync(
                    ResultTextBox.Text,
                    template,
                    progress,
                    cancellationToken);

                SummaryTextBox.Text = summary;
                SummaryPlaceholder.Visibility = Visibility.Collapsed;
                MainTabControl.SelectedIndex = 1;
                StatusText.Text = "AI-резюме готово";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Генерация резюме отменена.";
                ProcessingProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка генерации резюме: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка генерации";
            }
            finally
            {
                (sender as Button).IsEnabled = true;
                ProcessingProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                CancelButton.IsEnabled = true;
                ProcessingProgress.Value = 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Если активна вкладка резюме (индекс 1)
            if (MainTabControl.SelectedIndex == 1)
            {
                // Проверяем текущий формат
                string currentFormat = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "txt";
                // Если формат не TXT и не MD – переключаем на TXT
                if (currentFormat != "txt" && currentFormat != "md")
                {
                    // Находим элемент с тегом "txt" и выбираем его
                    foreach (ComboBoxItem item in FormatCombo.Items)
                    {
                        if (item.Tag?.ToString() == "txt")
                        {
                            FormatCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            // Если выбрана вкладка "Справка" (индекс 2 или по имени)
            if (MainTabControl.SelectedItem is TabItem tab && tab.Header?.ToString() == "❓ Справка")
            {
                HelpScrollViewer.ScrollToHome();
            }

        }
        private void SaveSettings()
        {
            try
            {
                var settings = AppSettings.Current;
                settings.SelectedTemplate = (TemplateCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Совещание";
                settings.SaveAudioForFiles = SaveAudioGlobalCheckBox.IsChecked ?? false;
                settings.SaveAudioForRecording = SaveAudioRecordingCheckBox.IsChecked ?? false;
                settings.SaveFormat = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "txt";
                settings.LastMicrophoneTag = (MicrophoneCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                // Можно также сохранять папку, если будет поле
                settings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка сохранения настроек: " + ex.Message);
            }
        }
        /// <summary>
        /// Форматирует текст: группирует предложения в абзацы по 3-5 предложений.
        /// </summary>
        private string FormatTextWithParagraphs(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Разбиваем на предложения (по . ! ? с пробелом или концом строки)
            var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+")
                                                              .Where(s => !string.IsNullOrWhiteSpace(s))
                                                              .ToList();

            if (sentences.Count <= 3)
                return text; // если предложений мало, не меняем

            // Группируем по 5 предложения (можно настроить)
            int groupSize = 5;
            var paragraphs = new List<string>();

            for (int i = 0; i < sentences.Count; i += groupSize)
            {
                var group = sentences.Skip(i).Take(groupSize);
                paragraphs.Add(string.Join(" ", group));
            }

            // Склеиваем абзацы с двойным переносом
            return string.Join("\n\n", paragraphs);
        }
        private async void OnRecordingFinished(string wavFilePath)
        {
            if (string.IsNullOrEmpty(wavFilePath) || !File.Exists(wavFilePath))
            {
                RecordingStatus.Text = "Ошибка записи: файл не найден";
                return;
            }

            // Создаём токен отмены и показываем кнопку
            _cancellationTokenSource = new CancellationTokenSource();
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;

            string savedFilePath = null;
            RecordingStatus.Text = "Обработка аудио...";
            ProcessingProgress.Visibility = Visibility.Visible;
            ProcessingProgress.Value = 0;

            if (AppSettings.Current.SaveAudioForRecording)
            {
                try
                {
                    string targetFolder = AppSettings.Current.CustomAudioFolder;
                    if (string.IsNullOrWhiteSpace(targetFolder))
                        targetFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Records");
                    Directory.CreateDirectory(targetFolder);
                    string savedFile = Path.Combine(targetFolder, $"Record_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
                    savedFilePath = savedFile;
                    RecordingStatus.Text = $"Сохраняю аудио в: {savedFile}";
                    using (var source = File.OpenRead(wavFilePath))
                    using (var dest = File.Create(savedFile))
                    {
                        source.CopyTo(dest);
                    }
                    RecordingStatus.Text = $"Аудио сохранено: {Path.GetFileName(savedFile)}. Идёт распознавание...";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось сохранить аудио: {ex.Message}\nПуть: {wavFilePath}", "Сохранение");
                    RecordingStatus.Text = "Аудио не сохранено. Идёт распознавание...";
                }
            }
            else
            {
                StatusText.Text = "";
                RecordingStatus.Text = "Идёт распознавание...";
            }

            if (!EnsureModelExists(out string modelPath))
            {
                RecordingStatus.Text = "Модель не найдена. Скачайте её через интерфейс.";
                ProcessingProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                return;
            }

            SetControlsEnabled(false);

            try
            {
                RecordingStatus.Text = "Фильтрация тишины (VAD)...";
                string vadWav = VadService.RemoveSilence(wavFilePath);
                ProcessingProgress.Value = 20;

                RecordingStatus.Text = "Распознавание речи...";
                using var service = new TranscriptionService(modelPath);
                string langCode = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";

                var progress = new Progress<int>(percentFromWhisper =>
                {
                    int overallPercent = 20 + (int)((float)percentFromWhisper / 100 * 80);
                    Dispatcher.Invoke(() =>
                    {
                        ProcessingProgress.Value = overallPercent;
                        RecordingStatus.Text = $"Распознавание речи... {overallPercent}%";
                    });
                });

                // Передаём токен
                var transcription = await service.TranscribeAsync(vadWav, langCode, progress, _cancellationTokenSource.Token);
                _lastTranscription = transcription;

                if (File.Exists(vadWav) && vadWav != wavFilePath)
                    File.Delete(vadWav);

                ResultTextBox.Text = string.IsNullOrWhiteSpace(transcription.Text)
                    ? "Речь не распознана."
                    : FormatTextWithParagraphs(transcription.Text);
                MainTabControl.SelectedIndex = 0;
                SummaryTextBox.Text = "";
                SummaryPlaceholder.Visibility = Visibility.Visible;

                ProcessingProgress.Value = 100;
                if (!string.IsNullOrEmpty(savedFilePath))
                    StatusText.Text = $"Готово. Аудио сохранено: {savedFilePath}";
                else
                    StatusText.Text = "Готово";
                RecordingStatus.Text = "";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Распознавание отменено.";
                RecordingStatus.Text = "";
                ProcessingProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка распознавания: {ex.Message}", "Ошибка");
                RecordingStatus.Text = "Ошибка распознавания";
            }
            finally
            {
                if (File.Exists(wavFilePath))
                    File.Delete(wavFilePath);
                SetControlsEnabled(true);
                ProcessingProgress.Visibility = Visibility.Collapsed;
                ProcessingProgress.Value = 0;
                CancelButton.Visibility = Visibility.Collapsed;
                CancelButton.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CancelButton.IsEnabled = false; // сразу отключаем, чтобы не нажимали дважды
            StatusText.Text = "Отмена операции...";
        }
        /// <summary>
        /// Извлекает текст из PDF-файла с помощью iTextSharp.
        /// </summary>
        private string ExtractTextFromPdf(string pdfPath)
        {
            var sb = new StringBuilder();
            using (var pdfReader = new PdfReader(pdfPath))
            using (var pdfDoc = new PdfDocument(pdfReader))
            {
                for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                {
                    var page = pdfDoc.GetPage(pageNum);
                    // Используем LocationTextExtractionStrategy для сохранения порядка
                    ITextExtractionStrategy strategy = new LocationTextExtractionStrategy();
                    string currentPageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                    sb.AppendLine(currentPageText);
                    sb.AppendLine(); // разделитель страниц
                }
            }
            string fullText = sb.ToString().Trim();
            // Убираем множественные переводы
            fullText = Regex.Replace(fullText, @"\n{3,}", "\n\n");
            return fullText;
        }
        private string ReadTextFileWithEncoding(string filePath)
        {
            // Пробуем кодировки в порядке приоритета: UTF-8 (самый частый), затем 1251, затем Unicode
            Encoding[] encodings;
            try
            {
                // Если провайдер зарегистрирован, 1251 доступна
                encodings = new[] { Encoding.UTF8, Encoding.GetEncoding(1251), Encoding.Unicode };
            }
            catch (NotSupportedException)
            {
                // Если 1251 всё ещё недоступна (редко, но на всякий случай) – пробуем только UTF-8 и Unicode
                encodings = new[] { Encoding.UTF8, Encoding.Unicode };
            }

            foreach (var enc in encodings)
            {
                try
                {
                    string content = File.ReadAllText(filePath, enc);
                    // Если после чтения есть символы замены (�), значит кодировка не подошла
                    int replacementCount = content.Count(c => c == '\uFFFD');
                    if (replacementCount < 5) // мало замен – значит, кодировка определена верно
                        return content;
                }
                catch
                {
                    // Если чтение не удалось – пробуем следующую кодировку
                    continue;
                }
            }

            // Если ничего не подошло – читаем как UTF-8 (стандарт)
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        private bool EnsureModelExists(out string modelPath)
        {
            string modelFile = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ggml-small.bin";
            modelPath = Path.Combine(AppSettings.GetModelsFolder(), modelFile);

            if (File.Exists(modelPath))
                return true;

            string modelName = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Small";
            string message =
                $"Модель распознавания '{modelName}' не найдена.\n\n" +
                "Для работы программы необходима модель Whisper — нейросеть, которая преобразует речь в текст.\n" +
                "Рекомендуем использовать модель 'Small' (488 МБ) — она даёт хороший баланс скорости и качества для русского языка.\n\n" +
                "Доступные модели (от быстрых к точным):\n" +
                "• Base (142 МБ) — быстрая, приемлемая точность.\n" +
                "• Small (488 МБ) — ★ рекомендуемая ★, лучший баланс.\n" +
                "• Medium (1.5 ГБ) — высокая точность, но медленнее.\n" +
                "• Large (2.9 ГБ) — максимальная точность, требует много ресурсов.\n\n" +
                "Скачайте модель через интерфейс: нажмите кнопку 'Скачать' в разделе 'Настройки модели распознавания' (левая панель).\n" +
                "После скачивания повторите попытку распознавания.";
            MessageBox.Show(message, "Модель не найдена", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        private void CheckAndSuggestModelOnStartup()
        {
            // Список возможных файлов моделей Whisper (как в выпадающем списке)
            string[] modelFiles = new[]
            {
        "ggml-tiny.bin",
        "ggml-base.bin",
        "ggml-small.bin",
        "ggml-medium.bin",
        "ggml-large-v2.bin",
        "ggml-large-v3.bin"
    };

            string modelsFolder = AppSettings.GetModelsFolder();
            bool anyModelExists = false;

            // Проверяем, есть ли хоть один файл модели
            foreach (var file in modelFiles)
            {
                string fullPath = Path.Combine(modelsFolder, file);
                if (File.Exists(fullPath))
                {
                    anyModelExists = true;
                    break;
                }
            }

            // Если моделей нет — показываем приветственное сообщение
            if (!anyModelExists)
            {
                string message =
                    "Добро пожаловать в программу Голосовед ИИ!\n\n" +
                    "Для работы необходимо скачать модель распознавания речи (Whisper).\n" +
                    "Рекомендуем модель 'Small' (488 МБ) — она даёт хороший баланс скорости и качества для русского языка.\n\n" +
                    "Доступные модели (от быстрых к точным):\n" +
                    "• Base (142 МБ) — быстрая, приемлемая точность.\n" +
                    "• Small (488 МБ) — ★ рекомендуемая ★, лучший баланс.\n" +
                    "• Medium (1.5 ГБ) — высокая точность, но медленнее.\n" +
                    "• Large (2.9 ГБ) — максимальная точность, требует много ресурсов.\n\n" +
                    "Скачайте модель через интерфейс: нажмите кнопку 'Скачать' в разделе 'Настройки модели распознавания' (левая панель).\n" +
                    "Также для AI-резюме потребуется скачать языковую модель (Qwen) — она загружается аналогично.\n\n" +
                    "После скачивания моделей перезапустите программу или просто начните распознавание.";

                MessageBox.Show(message, "Добро пожаловать", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void SelectAudioFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Выберите папку для сохранения аудио";
            dialog.ShowNewFolderButton = true;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                AudioFolderTextBox.Text = dialog.SelectedPath;
                AppSettings.Current.CustomAudioFolder = dialog.SelectedPath;
                AppSettings.Current.Save();
            }
        }
        private void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Замените ссылку на вашу страницу донатов
                string url = "https://pay.cloudtips.ru/p/31f0f59a"; // или ваша ссылка
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть страницу: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isLight = !AppSettings.Current.IsLightTheme; // переключаем
            ApplyTheme(isLight);
            AppSettings.Current.IsLightTheme = isLight;
            AppSettings.Current.Save();

            // Обновляем иконку переключателя
            ThemeToggle.Content = isLight ? "🌙" : "☀️";
        }
        private void ApplyTheme(bool isLight)
        {
            var bg = isLight ? Brushes.White : (Brush)new BrushConverter().ConvertFrom("#121212");
            var fg = isLight ? Brushes.Black : Brushes.White;
            var placeholderFg = isLight ? (Brush)new BrushConverter().ConvertFrom("#666666") : (Brush)new BrushConverter().ConvertFrom("#66FFFFFF");

            // 1. Основные текстовые поля
            ResultTextBox.Background = bg;
            ResultTextBox.Foreground = fg;
            SummaryTextBox.Background = bg;
            SummaryTextBox.Foreground = fg;

            // 2. Плейсхолдеры
            PlaceholderText.Foreground = placeholderFg;
            SummaryPlaceholder.Foreground = placeholderFg;

            // 3. Блоки "Поддержать проект" и "Информация о версии"
            var borderBg = isLight ? Brushes.LightGray : (Brush)new BrushConverter().ConvertFrom("#2A2A2A");
            DonateBorder.Background = borderBg;
            VersionBorder.Background = borderBg;

            // 4. Справка – фон и цвет текста
            if (HelpScrollViewer.Content is Panel panel)
            {
                panel.Background = bg;
                SetForegroundRecursive(panel, fg);
            }
        }

        private void SetForegroundRecursive(DependencyObject parent, Brush foreground)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock tb)
                {
                    tb.Foreground = foreground;
                    // Меняем цвет у всех Run внутри TextBlock
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run)
                            run.Foreground = foreground;
                    }
                }
                else if (child is Border border)
                {
                    // Если Border содержит дочерний элемент, обходим его
                    if (border.Child != null)
                    {
                        SetForegroundRecursive(border.Child, foreground);
                    }
                }

                // Рекурсивно для всех дочерних
                SetForegroundRecursive(child, foreground);
            }
        }
        private void LangRuButton_Click(object sender, RoutedEventArgs e)
        {
            LocalizationManager.SetLanguage("ru-RU");
            UpdateLanguageButtons("ru-RU");
        }

        private void LangEnButton_Click(object sender, RoutedEventArgs e)
        {
            LocalizationManager.SetLanguage("en-US");
            UpdateLanguageButtons("en-US");
        }
        private void UpdateLanguageButtons(string currentLang)
        {
            LangRuButton.BorderBrush = currentLang == "ru-RU" ? Brushes.Blue : Brushes.Transparent;
            LangRuButton.BorderThickness = currentLang == "ru-RU" ? new Thickness(2) : new Thickness(0);

            LangEnButton.BorderBrush = currentLang == "en-US" ? Brushes.Blue : Brushes.Transparent;
            LangEnButton.BorderThickness = currentLang == "en-US" ? new Thickness(2) : new Thickness(0);
        }
    }
}