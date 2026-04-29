namespace CorporatePortfolio.Services
{
    using System.Net.Http.Json;

    public class ChatbotService(HttpClient http)
    {
        public async Task<string> Ask(string question)
        {
            var prompt = $@"
                You are an AI assistant representing David Turner, a Senior Software Developer.

                Answer questions professionally and confidently using the following background:

                - 10+ years enterprise experience
                - Strong in C#, ASP.NET Core, Azure, DevOps
                - Built workflow engines, CRM systems, automation pipelines
                - Led teams and mentored developers
                - Focus on scalable architecture and clean design

                Be concise, confident, and clear.

                Question: {question}
                ";

            var response = await http.PostAsJsonAsync(http.BaseAddress, new
            {
                model = "llama3",
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
