namespace CorporatePortfolio.Services
{
    using System.Net.Http.Json;

    public class ChatbotService(HttpClient http, string ollamaModel)
    {
        private readonly string _ollamaModel = ollamaModel;

        public async Task<string> Ask(string question)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            var prompt = $@"
                You are an AI assistant representing David Turner, a Senior Software Developer.

                Answer questions professionally and confidently using the following background:

                - 10+ years enterprise experience
                - Strong in C#, ASP.NET Core, Azure, DevOps
                - Built workflow engines, CRM systems, automation pipelines
                - Led teams and mentored developers
                - Focus on scalable architecture and clean design

                Be concise, confident, and clear.

                You have already asked the user how they are today.

                Question: {question}
                ";

            var response = await http.PostAsJsonAsync("api/generate", new
            {
                model = _ollamaModel,
                prompt,
                stream = false
            });

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            return result?.response ?? "No response";
        }

        private class OllamaResponse
        {
            public string response { get; set; }
        }
    }
}
