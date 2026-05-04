namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.FileProviders;
    using System.Net.Http.Json;


    public class ChatbotService(HttpClient http, bool isDevelopment, string ollamaModel, ResumeService resumeService, IMemoryCache memoryCache)
    {
        private readonly string _ollamaModel = ollamaModel;
        private readonly ResumeService _resumeService = resumeService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private string? _cachedInstructions;
        private DateTime _lastReadTime = DateTime.MinValue;
        private readonly string _filePath = "AIInstructions.txt";

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessage> history)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            if (!_memoryCache.TryGetValue("#resumeText", out string _resumeText))
            {
                _resumeText = await _resumeService.GetResumeText();

                // Path to your resume file
                var fileInfo = new FileInfo("DavidTurner_Resume.docx");
                var fileProvider = new PhysicalFileProvider(fileInfo.DirectoryName!);

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .AddExpirationToken(fileProvider.Watch(fileInfo.Name)); // Evicts on file save

                _memoryCache.Set("#resumeText", _resumeText, cacheEntryOptions);
            }

            // 1. Handle Lazy Loading of Instructions
            var lastWrite = File.GetLastWriteTime(_filePath);
            if (_cachedInstructions == null || lastWrite > _lastReadTime)
            {
                _cachedInstructions = await File.ReadAllTextAsync(_filePath);
                _lastReadTime = lastWrite;
            }

            // Replace resumeContent placeholder
            var systemContent = _cachedInstructions.Contains("{resumeContent}")
                ? _cachedInstructions.Replace("{resumeContent}", _resumeText)
                : _cachedInstructions + "\n" + _resumeText;

            // Replace todaysDate without falling back to appending text at the end
            systemContent = systemContent.Contains("{todaysDate}")
                ? systemContent.Replace("{todaysDate}", DateTime.Now.ToString("MMMM dd, yyyy"))
                : systemContent;

            // Inject dynamic date rules
            var start = new DateTime(2026, 4, 29);
            var today = DateTime.Now;

            // Calculate years, months, and days exactly
            int years = today.Year - start.Year;
            int months = today.Month - start.Month;
            int days = today.Day - start.Day;

            if (days < 0)
            {
                // Borrow days from the previous month
                var previousMonth = today.AddMonths(-1);
                days += DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);
                months--;
            }

            if (months < 0)
            {
                // Borrow months from the previous year
                months += 12;
                years--;
            }

            // Build the readable string
            var parts = new List<string>();
            if (years > 0) parts.Add($"{years} {(years == 1 ? "year" : "years")}");
            if (months > 0) parts.Add($"{months} {(months == 1 ? "month" : "months")}");
            if (days > 0) parts.Add($"{days} {(days == 1 ? "day" : "days")}");

            // Fallback if today is exactly the start date
            string durationText = parts.Count > 0 ? string.Join(", ", parts) : "0 days";

            string dynamicRule = $"IF {{todaysDate}} is {today:MMMM dd, yyyy}: Duration is {durationText}";

            systemContent = systemContent.Contains("{dateLogic}")
                ? systemContent.Replace("{dateLogic}", dynamicRule)
                : systemContent;

            // 3. Build the Message Collection (Single system message used here)
            var messages = new List<object>
            {
                new { role = "system", content = systemContent }
            };

            var historyToProcess = isDevelopment ? history.TakeLast(10) : history.TakeLast(5);
            var historyList = historyToProcess.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();

            for (int i = 0; i < historyList.Count; i++)
            {
                var msg = historyList[i];

                if (i == historyList.Count - 1 && msg.IsUser && msg.Text.Trim().Equals(question.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                messages.Add(new { role = msg.IsUser ? "user" : "assistant", content = msg.Text });
            }

            // 5. Add the Current Question
            messages.Add(new { role = "user", content = question });

            // 6. Send to Ollama
            var response = await http.PostAsJsonAsync("api/chat", new
            {
                model = _ollamaModel,
                messages,
                stream = true,
                options = new
                {
                    num_ctx = isDevelopment ? 4096 : 2048,
                    temperature = 0.0,
                    repeat_penalty = 1.1,
                    num_predict = 250,
                    top_p = isDevelopment ? 0.9 : 0.4,
                    top_k = isDevelopment ? 40 : 20,
                    num_thread = isDevelopment ? 4 : 2,
                    seed = isDevelopment ? (int?)null : 42
                },
            }, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

            return StreamResponse(response);
        }

        private static async IAsyncEnumerable<string?> StreamResponse(HttpResponseMessage response)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var chunk = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(line);
                    if (chunk?.message?.content != null)
                    {
                        yield return chunk.message.content;
                    }
                }
            }
        }

        private class OllamaResponse
        {
            public ChatMessagePart message { get; set; }
        }

        private class ChatMessagePart
        {
            public string content { get; set; }
        }
    }
}
