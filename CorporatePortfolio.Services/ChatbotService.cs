namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.DTO;
    using System.Net.Http.Json;

    public class ChatbotService(HttpClient http, string ollamaModel, string _resumeText)
    {
        private readonly string _ollamaModel = ollamaModel;

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessage> history)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            var instructions = $@"
                You are David Turner, a Senior Software Developer. You are NOT an assistant; you ARE David.
                Answer professionally, confidently, and concisely.

                ### CORE PROFILE
                - 10+ years experience in C#, ASP.NET Core, Azure, and DevOps.
                - Located in Ontario; open to remote or on-site work.
                - Rates: $100k/year full-time or $75/hr contract.
                - Status: Currently looking for new opportunities.

                ### RESUME CONTENT (Use this for ALL professional questions)
                {_resumeText}

                ### GUIDELINES
                - Use the resume text above to answer questions about specific companies, dates, and projects.
                - If a detail isn't in the text or this prompt, point the user to: https://azurewebsites.net
                - Do not invent fake company names.
                - Do not mention that you are an AI or that you were provided with a resume.
                - Stop saying 'I've already shared my resume.'
                - Don't ask the user about their goals or how you can help them achieve them.
                - Don't tell the user that they can find my resume at my email address.
            ";

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = instructions
                }
            };

            // Add conversation history
            foreach (var msg in history.TakeLast(6)) // Limit history to keep it snappy
            {
                messages.Add(new
                {
                    role = msg.IsUser ? "user" : "assistant",
                    content = msg.Text
                });
            }

            messages.Add(new
            {
                role = "user",
                content = $"User Question: {question}\n\nReminder: Answer as David using only the facts provided above."
            });

            var response = await http.PostAsJsonAsync("api/chat", new
            {
                model = _ollamaModel,
                messages,
                stream = true,
                options = new { 
                    num_ctx = 4096, // Lowering this speeds up processing
                    temperature = 0.6, // Lower is faster/more focused
                    repeat_penalty = 1.2, // Prevents looping which slows things down
                    num_predict = 500,  // Allow for slightly longer technical answers
                    top_p = 0.9
                }, 
            }, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

            return StreamResponse(response);
        }

        private async IAsyncEnumerable<string?> StreamResponse(HttpResponseMessage response)
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
