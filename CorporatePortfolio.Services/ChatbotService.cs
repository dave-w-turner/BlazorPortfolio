namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.FileProviders;
    using System.Net.Http.Json;
    using System.Text.Json;

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
            var messages = new List<dynamic>
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

            object payload;

            if (isDevelopment)
            {
                //Ollama-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    messages,
                    stream = true,
                    options = new
                    {
                        num_ctx = 4096,
                        num_batch = 256,
                        temperature = 0.0,
                        repeat_penalty = 1.1,
                        num_predict = 250,
                        top_p = 0.9,
                        top_k = 40,
                        num_thread = 4
                    }
                };
            }
            else
            {
                var cleanMessages = messages.Select(m => new {
                    role = m.role,
                    content = m.content
                }).ToList();

                //Grok-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    messages = cleanMessages,
                    stream = true,
                    temperature = 0.0,
                    max_tokens = 250
                };
            }                   

            var request = new HttpRequestMessage(HttpMethod.Post, isDevelopment ? "api/chat" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            // 2. Use ResponseHeadersRead
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!isDevelopment && !response.IsSuccessStatusCode)
            {
                // This string will tell you EXACTLY which field Groq hates
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            // 3. Return the IAsyncEnumerable directly
            // Do NOT read the stream here. If you want to log, do it inside StreamResponse.
            if (isDevelopment)
                return StreamResponseOllama(response);
            else
                return StreamResponseGrok(response);
        }

        private static async IAsyncEnumerable<string?> StreamResponseOllama(HttpResponseMessage response)
        {
            // The 'using' here ensures the connection stays open until the loop is finished
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // Optional: Log to console here if you need to see it on the server
                        // Console.WriteLine($"DEBUG: {line}");

                        var chunk = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(line);
                        if (chunk?.message?.content != null)
                        {
                            yield return chunk.message.content;
                        }
                    }
                }
            }
        }

        private static async IAsyncEnumerable<string?> StreamResponseGrok(HttpResponseMessage response)
        {
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    // Groq streams start with "data: " and end with "data: [DONE]"
                    if (line.StartsWith("data: ") && !line.Contains("[DONE]"))
                    {
                        var json = line.Substring(6); // Strip "data: " prefix
                        var chunk = JsonSerializer.Deserialize<GroqResponse>(json);

                        var content = chunk?.choices?[0]?.delta?.content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            yield return content;
                        }
                    }
                }
            }
        }

        public class GroqResponse
        {
            public List<Choice> choices { get; set; }
            public class Choice { public Delta delta { get; set; } }
            public class Delta { public string content { get; set; } }
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
