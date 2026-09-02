using LLama;
using LLama.Common;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GolosovedAI.Services
{
    public class SummarizationService : IDisposable
    {
        private LLamaWeights _weights;
        private StatelessExecutor _executor;
        private ModelParams _modelParams;
        private bool _isLoaded = false;
        private string _loadedModelPath = "";
        public bool IsLoaded => _isLoaded;

        public void LoadModel(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Модель не найдена: {modelPath}");

            if (_loadedModelPath == modelPath && _isLoaded)
                return;

            _modelParams = new ModelParams(modelPath)
            {
                ContextSize = 8192,
                GpuLayerCount = 32,
                BatchSize = 512
            };

            _weights = LLamaWeights.LoadFromFile(_modelParams);
            _executor = new StatelessExecutor(_weights, _modelParams);
            _isLoaded = true;
            _loadedModelPath = modelPath;
        }

        public async Task<string> GenerateSummaryAsync(
            string fullText,
            string template,
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_isLoaded)
                throw new InvalidOperationException(LocalizationManager.Get("Model_NotDownloaded"));

            if (string.IsNullOrWhiteSpace(fullText))
                return LocalizationManager.Get("Message_NoTextForSummary");

            progress?.Report(0);
            fullText = Regex.Replace(fullText, @"\s+", " ");
            fullText = fullText.Trim();

            // ===== АВТООПРЕДЕЛЕНИЕ ЯЗЫКА =====
            string languageInstruction = "";
            int cyrillicCount = fullText.Count(c => (c >= 'А' && c <= 'я') || c == 'ё' || c == 'Ё');
            int latinCount = fullText.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
            if (cyrillicCount > latinCount)
                languageInstruction = "Важно: весь ответ пиши строго на русском языке.\n";
            else
                languageInstruction = "Important: answer strictly in English.\n";

            // ===== ШАБЛОНЫ =====
            string strictRule = "ВАЖНОЕ ПРАВИЛО: Используй СТРОГО только факты из предоставленного текста. Категорически запрещено выдумывать имена людей, должности, даты, количество участников и цифры, которых нет в исходном тексте!\n\n";
            string templateInstruction = "";
            string responseHeader = "";

            switch (template)
            {
                case "Совещание":
                    templateInstruction =
                        strictRule +
                        "Роль: Ты корпоративный секретарь. Составь краткий протокол встречи.\n" +
                        "Инструкция по извлечению данных:\n" +
                        "1. В разделе 'Краткая суть созвона' напиши сжатый обзор встречи в 2-3 предложениях.\n" +
                        "2. В разделе 'Принятые решения' перечисли ключевые договоренности сторон.\n" +
                        "3. В разделе 'Задачи и поручения' укажи исполнителя и его задачу. Пиши имя только если оно прозвучало в тексте, иначе пиши 'Исполнитель: Не указан'.\n\n" +
                        "Формат вывода:\n" +
                        "### 📌 Краткая суть созвона\n\n" +
                        "### 📈 Принятые решения\n\n" +
                        "### 📋 Задачи и поручения";
                    responseHeader = "### 📌 Краткая суть созвона\n- ";
                    break;

                case "Интервью":
                    templateInstruction =
                        strictRule +
                        "Роль: Ты HR-аналитик. Составь резюме собеседования.\n" +
                        "Инструкция по извлечению данных:\n" +
                        "1. В разделе 'Профиль кандидата' укажи имя кандидата и целевую должность. Если имя не указано в тексте, напиши 'Не указан'. Если должность не указана, напиши 'Не указана'.\n" +
                        "2. В разделе 'Ключевые навыки и сильные стороны' перечисли опыт и скиллы соискателя.\n" +
                        "3. В разделе 'Критические маркеры (Red Flags)' выдели проблемы или напиши 'Не обнаружено'.\n\n" +
                        "Формат вывода:\n" +
                        "### 🔎 Профиль кандидата\n" +
                        "- **Кандидат**: если имя не указано, напиши 'Не указан'.\n" +
                        "- **Должность**: если не указана, напиши 'Не указана'.\n" +
                        "- **Тема встречи**: сформулируй кратко.\n\n" +
                        "### 🌟 Ключевые навыки и сильные стороны\n\n" +
                        "### ⚠️ Критические маркеры (Red Flags)";
                    responseHeader = "### 🔎 Профиль кандидата\n- **Кандидат**: ";
                    break;

                case "Лекция":
                    templateInstruction =
                        strictRule +
                        "Роль: Ты учебный методист. Сделай структурированный конспект лекции.\n" +
                        "Инструкция по извлечению данных:\n" +
                        "1. В разделе 'Главная тема лекции' сформулируй предмет лекции одной строкой.\n" +
                        "2. В разделе 'Основные тезисы и определения' выпиши ключевые понятия. Каждое понятие пиши жирным шрифтом.\n" +
                        "3. В разделе 'Ключевые примеры и выводы' зафиксируй только те выводы и примеры, которые явно озвучил спикер. Если примеров кода в тексте нет, не пиши код самостоятельно.\n\n" +
                        "Формат вывода:\n" +
                        "### 📖 Главная тема лекции\n\n" +
                        "### 💡 Основные тезисы и определения\n\n" +
                        "### 🗒 Ключевые примеры и выводы";
                    responseHeader = "### 📖 Главная тема лекции\n- ";
                    break;

                case "Общий":
                default:
                    templateInstruction =
                        strictRule +
                        "Ты — главный редактор. Сделай подробный и развернутый информационный обзор предоставленного текста.\n" +
                        "Инструкция:\n" +
                        "1. В разделе 'Центральная идея текста' подробно опиши главную мысль документа.\n" +
                        "2. В разделе 'Ключевые факты, цифры и статистика' выпиши ВСЕ доступные факты, имена, числовые данные и рекомендации из текста в виде подробного списка.\n" +
                        "3. В разделе 'Итоговый вывод' зафиксируй заключительный тезис.\n\n" +
                        "Формат вывода:\n" +
                        "### 📝 Центральная идея текста\n\n" +
                        "### 📊 Ключевые факты, цифры и статистика\n\n" +
                        "### 🏁 Итоговый вывод";
                    responseHeader = "### 📝 Центральная идея текста\n- ";
                    break;
            }

            templateInstruction = languageInstruction + templateInstruction;

            int wordCount = Regex.Matches(fullText, @"\b\w+\b").Count;

            // ===== КОРОТКИЙ ТЕКСТ =====
            if (wordCount < 1200)
            {
                progress?.Report(30);

                string prompt = $"<|im_start|>system\n{templateInstruction}\nВыдавай ТОЛЬКО текст отчета по шаблону. Никаких вступлений.<|im_end|>\n" +
                                $"<|im_start|>user\nСделай резюме для этого текста:\n{fullText}<|im_end|>\n" +
                                $"<|im_start|>assistant\n{responseHeader}";

                int shortMaxTokens = (template == "Общий") ? 3072 : 2048;

                string result = await RunInferenceInternalAsync(prompt, shortMaxTokens, cancellationToken);
                progress?.Report(100);

                if (string.IsNullOrWhiteSpace(result))
                    return "Не удалось сгенерировать резюме.";

                string finalShortResult = result.StartsWith("###") ? result : (responseHeader + result);
                return CleanLlmResponse(finalShortResult);
            }

            // ===== ДЛИННЫЙ ТЕКСТ – MAP-REDUCE =====
            int chunkSize = 1500;
            var words = Regex.Matches(fullText, @"\b\w+\b")
                             .Select(m => m.Value)
                             .ToArray();

            var chunks = new List<string>();
            for (int i = 0; i < words.Length; i += chunkSize)
            {
                var chunkWords = words.Skip(i).Take(chunkSize);
                chunks.Add(string.Join(" ", chunkWords));
            }

            int totalChunks = chunks.Count;
            var chunkSummaries = new List<string>();

            for (int i = 0; i < totalChunks; i++)
            {
                int percent = (i + 1) * 80 / totalChunks;
                progress?.Report(percent);

                string chunkPrompt = $"<|im_start|>system\nВыпиши ключевые факты из фрагмента текста. Ответ должен быть кратким списком.<|im_end|>\n" +
                                     $"<|im_start|>user\nФрагмент {i + 1}/{totalChunks}:\n{chunks[i]}<|im_end|>\n" +
                                     $"<|im_start|>assistant\n- ";

                string chunkResult = await RunInferenceInternalAsync(chunkPrompt, maxTokens: 200, cancellationToken);
                chunkSummaries.Add($"- Часть {i + 1}:\n- {chunkResult}");
            }

            progress?.Report(85);
            string combinedSummaries = string.Join("\n\n", chunkSummaries);

            string finalPrompt = $"<|im_start|>system\n{templateInstruction}\nПеред тобой краткие выжимки из разных частей одной длинной записи. Объедини их в один итоговый отчет. Выдавай ТОЛЬКО структурированный Markdown-текст, без вводных фраз.<|im_end|>\n" +
                                 $"<|im_start|>user\nВыжимки всех частей записи:\n{combinedSummaries}<|im_end|>\n" +
                                 $"<|im_start|>assistant\n{responseHeader}";

            progress?.Report(90);
            string finalResult = await RunInferenceInternalAsync(finalPrompt, maxTokens: 3072, cancellationToken);
            progress?.Report(100);

            if (string.IsNullOrWhiteSpace(finalResult))
                return "Не удалось сгенерировать резюме.";

            string finalMappedResult = finalResult.StartsWith("###") ? finalResult : (responseHeader + finalResult);
            return CleanLlmResponse(finalMappedResult);
        }

        private async Task<string> RunInferenceInternalAsync(
            string prompt,
            int maxTokens,
            CancellationToken cancellationToken = default)
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new List<string> { "<|end|>", "<|im_end|>", "|end|", "|user|", "|assistant|", "Выдавай только" },
                SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline()
                {
                    Temperature = 0.15f,
                    TopP = 0.85f,
                    RepeatPenalty = 1.45f
                }
            };

            var sb = new StringBuilder();

            await foreach (var text in _executor.InferAsync(prompt, inferenceParams))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append(text);
            }

            return sb.ToString().Trim();
        }

        private string CleanLlmResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Replace("<|end|>", "")
                       .Replace("<|im_end|>", "")
                       .Replace("|end|", "")
                       .Replace("<|end", "")
                       .Replace("<|im_start|>", "")
                       .Replace("<|assistant|", "")
                       .Replace("|user|", "")
                       .Replace("|assistant|", "")
                       .Trim();

            string[] loopMarkers = new[] { "|user|", "|assistant|", "<|user|>" };
            foreach (var marker in loopMarkers)
            {
                int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index != -1)
                {
                    text = text.Substring(0, index).Trim();
                    break;
                }
            }

            string[] trashMarkers = new[] { "Извините", "Я здесь чтобы помочь", "Обратите внимание", "Надеюсь, это" };
            foreach (var marker in trashMarkers)
            {
                int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index != -1)
                {
                    text = text.Substring(0, index).Trim();
                }
            }

            string[] garbagePatterns = new[]
            {
                @"\bMENTO\b", @"\bvor\b", @"\banalysis\b", @"\binfo\b", @"\bable\b",
                @"\bобъ\b", @"\bза\b", @"\bсо\b", @"\bна\b", @"\bво\b", @"\bпро\b"
            };
            foreach (var pattern in garbagePatterns)
                text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var uniqueLines = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("###") && uniqueLines.Contains(line))
                    continue;
                uniqueLines.Add(line);
            }
            text = string.Join(Environment.NewLine, uniqueLines).Trim();

            return text;
        }

        public void Dispose()
        {
            _weights?.Dispose();
        }
    }
}
