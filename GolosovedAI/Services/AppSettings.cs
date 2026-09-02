using Newtonsoft.Json;
using System.IO;

namespace GolosovedAI.Services
{
    public class AppSettings
    {
		private static readonly string SettingsPath =
	Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				 "GolosovedAI", "settings.json");

		public string LastModelTag { get; set; } = "ggml-small.bin";
        public string LastLanguageTag { get; set; } = "auto";
        public string LastAIModelTag { get; set; } = "";
        public string LastMicrophoneTag { get; set; } = "";
        public string Language { get; set; } = "ru-RU";

        // Новые настройки для сохранения между сессиями
        public string SelectedTemplate { get; set; } = "Совещание";
        public bool SaveAudioForFiles { get; set; } = false;
        public bool SaveAudioForRecording { get; set; } = false;
        public string SaveFormat { get; set; } = "txt";
        public string CustomAudioFolder { get; set; } = "";
        public bool IsLightTheme { get; set; } = false;

        private static AppSettings _current;
        public static AppSettings Current
        {
            get
            {
                if (_current == null)
                    _current = Load();
                return _current;
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsPath);
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        // НОВЫЙ МЕТОД — добавляем сюда
        public static string GetModelsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GolosovedAI",
                "Models"
            );
        }
    }
}
