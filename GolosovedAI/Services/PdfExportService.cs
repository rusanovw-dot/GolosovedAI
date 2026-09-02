using iText.Html2pdf;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GolosovedAI.Services
{
    public static class PdfExportService
    {
        public static void CreatePdf(string text, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(text))
                text = LocalizationManager.Get("Message_NoData");

            // --- 1. Подготовка текста: удаляем дублирующий заголовок и вставляем пробелы ---
            string safeText = text;

            // Удаляем бинарный мусор
            safeText = Regex.Replace(safeText, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

            // Удаляем "ОТЧЕТ ГОЛОСОВЕД ИИ" (потому что мы добавим его сами)
            safeText = safeText.Replace("ОТЧЕТ ГОЛОСОВЕД ИИ", "").Trim();

            // Вставляем пробел перед каждым заголовком ###, если его нет
            safeText = Regex.Replace(safeText, @"([^\s])(###)", "$1 $2");

            // Вставляем пробел после заголовка ###, если после нет пробела
            safeText = Regex.Replace(safeText, @"(###[^\s])([^\s])", "$1 $2");

            // Вставляем перенос строки перед каждым заголовком ###, если его нет
            safeText = Regex.Replace(safeText, @"([^\n])(###)", "$1\n$2");

            // Вставляем перенос строки перед маркером списка "-", если его нет
            safeText = Regex.Replace(safeText, @"([^\n])(\s*-\s)", "$1\n$2");

            // Убираем множественные переносы
            safeText = Regex.Replace(safeText, @"\n{3,}", "\n\n");

            // --- 2. Строим HTML ---
            var htmlBuilder = new StringBuilder();
            htmlBuilder.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
            htmlBuilder.AppendLine("body { font-family: 'Arial', sans-serif; margin: 30px; color: #333; line-height: 1.5; }");
            htmlBuilder.AppendLine("h1 { color: #4B0082; border-bottom: 2px solid #4B0082; padding-bottom: 8px; font-size: 22px; }");
            htmlBuilder.AppendLine("h3 { color: #00008B; margin-top: 20px; font-size: 15px; border-left: 4px solid #00008B; padding-left: 8px; }");
            htmlBuilder.AppendLine("ul { margin-left: 15px; }");
            htmlBuilder.AppendLine("li { margin-bottom: 6px; list-style-type: square; }");
            htmlBuilder.AppendLine("p { text-align: justify; margin-bottom: 12px; }");
            htmlBuilder.AppendLine("</style></head><body>");
            htmlBuilder.AppendLine("<h1>ОТЧЕТ ГОЛОСОВЕД ИИ</h1>");

            // Разбиваем текст на строки
            string[] lines = safeText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            bool inList = false;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine.StartsWith("###"))
                {
                    if (inList) { htmlBuilder.AppendLine("</ul>"); inList = false; }
                    string headerText = trimmedLine.Replace("###", "").Trim();
                    htmlBuilder.AppendLine($"<h3>{headerText}</h3>");
                }
                else if (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("•"))
                {
                    if (!inList) { htmlBuilder.AppendLine("<ul>"); inList = true; }
                    string itemText = trimmedLine.TrimStart('-', '•', ' ').Replace("**", "");
                    htmlBuilder.AppendLine($"<li>{itemText}</li>");
                }
                else
                {
                    if (inList) { htmlBuilder.AppendLine("</ul>"); inList = false; }
                    string paragraphText = trimmedLine.Replace("**", "");
                    htmlBuilder.AppendLine($"<p>{paragraphText}</p>");
                }
            }

            if (inList) htmlBuilder.AppendLine("</ul>");
            htmlBuilder.AppendLine("</body></html>");

            // --- 3. Запись PDF ---
            using (var pdfStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                HtmlConverter.ConvertToPdf(htmlBuilder.ToString(), pdfStream);
            }
        }
    }
}
