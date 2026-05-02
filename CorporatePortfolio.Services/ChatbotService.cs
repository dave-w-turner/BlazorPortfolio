namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using System.Net.Http.Json;


    public class ChatbotService(HttpClient http, bool isDevelopment, string ollamaModel, string _resumeText)
    {
        private readonly string _ollamaModel = ollamaModel;
        private string? _cachedInstructions;
        private DateTime _lastReadTime = DateTime.MinValue;
        private readonly string _filePath = "AIInstructions.txt";

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessage> history)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            // 1. Handle Lazy Loading of Instructions
            var lastWrite = File.GetLastWriteTime(_filePath);
            if (_cachedInstructions == null || lastWrite > _lastReadTime)
            {
                _cachedInstructions = await File.ReadAllTextAsync(_filePath);
                _lastReadTime = lastWrite;
            }
                       
            var systemContent = _cachedInstructions.Contains("{resumeContent}")
                ? _cachedInstructions.Replace("{resumeContent}", _resumeText)
                : _cachedInstructions + "\n" + _resumeText;

            systemContent = systemContent.Contains("{todaysDate}")
                ? systemContent.Replace("{todaysDate}", DateTime.Now.ToString("MMMM dd, yyyy"))
                : systemContent + "\n" + DateTime.Now.ToString("MMMM dd, yyyy");

            //Inject dynamic date rules
            var start = new DateTime(2026, 4, 29);
            var today = DateTime.Now;
            int daysCount = (today - start).Days; // +1 to include today

            string dynamicRule = $"IF {{todaysDate}} is {today:MMMM dd, yyyy}: Duration is {daysCount} days.";

            systemContent = systemContent.Contains("{dateLogic}")
                ? systemContent.Replace("{dateLogic}", dynamicRule)
                : systemContent;

            // 3. Build the Message Collection
            var messages = new List<object>
            {
                new { role = "system", content = systemContent },
                new { role = "system", content = "I'm David Turner. I'm a Senior Developer currently looking for new opportunities in the .NET and Azure space. How can I help you today?" }
            };

            var historyToProcess = isDevelopment ? history.TakeLast(10) : history.TakeLast(5);
            var historyList = historyToProcess.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();

            for (int i = 0; i < historyList.Count; i++)
            {
                var msg = historyList[i];

                // Only skip the very last message in history if it is a user message 
                // identical to the current question.
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
                    num_ctx = isDevelopment ? 4096 : 1024,
                    temperature = 0.0, // Critical for stopping hallucinations
                    repeat_penalty = 1.1,
                    num_predict = 500,
                    top_p = isDevelopment ? 0.9 : 0.2,
                    top_k = isDevelopment ? 40 : 10,
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

            // Read full lines (Ollama sends 1 JSON object per word/token per line)
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
