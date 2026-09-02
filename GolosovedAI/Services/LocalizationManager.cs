using System;
using System.Windows;

namespace GolosovedAI.Services
{
    public static class LocalizationManager
    {
        public static void SetLanguage(string cultureName)
        {
            // Сохраняем в настройки
            AppSettings.Current.Language = cultureName;
            AppSettings.Current.Save();

            // Меняем словарь в приложении
            var dict = new ResourceDictionary
            {
                Source = new Uri($"Resources/Lang.{cultureName}.xaml", UriKind.Relative)
            };

            var appResources = Application.Current.Resources;
            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(dict);
        }

        public static string Get(string key)
        {
            try
            {
                if (Application.Current?.Resources.Contains(key) == true)
                    return Application.Current.Resources[key]?.ToString() ?? $"[{key}]";
            }
            catch { }
            return $"[{key}]";
        }

        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return args == null || args.Length == 0 ? template : string.Format(template, args);
            }
            catch { return template; }
        }

        public static MessageBoxResult Show(string messageKeyOrText, string titleKey = "Title_Info", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, bool isRawText = false)
        {
            string msg = isRawText ? messageKeyOrText : Get(messageKeyOrText);
            string title = Get(titleKey);
            return MessageBox.Show(msg, title, buttons, icon);
        }
    }
}