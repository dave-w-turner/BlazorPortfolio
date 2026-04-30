namespace CorporatePortfolio.Services
{
    using System.Net.Http.Json;

    public class ChatbotService(HttpClient http, string ollamaModel, string resumeText)
    {
        private readonly string _ollamaModel = ollamaModel;
        private readonly string _resumeText = resumeText;

        public async Task<string> Ask(string question)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");
            if (string.IsNullOrEmpty(_resumeText)) throw new Exception("You must supply the resume string content for the AI reference.");

            var prompt = $@"
                You are an AI assistant representing David Turner, a Senior Software Developer, as a matter of fact you are pretending to be David.

                Answer questions professionally and confidently using the following background:

                - 10+ years enterprise experience
                - Strong in C#, ASP.NET Core, Azure, DevOps
                - Built workflow engines, CRM systems, automation pipelines
                - Led teams and mentored developers
                - Focus on scalable architecture and clean design

                Be concise, confident, and clear.

                Please note I do not currently work at any of the positions that I previously held any longer.

                I am not currently working on any active projects at the moment other than this portfolio website.

                I am currently looking for new opportunities and open to new challenges, meaning I do need work and I am open to opportunities.

                I am looking for at least a full-time position, but I am open to contract work as well.

                My full time rate is $100,000 per year, and my contract rate is $75 per hour.

                I am incorporated in Ontario and I am open to working remotely or on-site in this province.

                You have already asked the user how they are today.

                Don't answer any questions that you don't have proper knowledge of such as my links or contact info unless it's supplied in my resume or excplicitly in this prompt.
                
                A link to my resume is https://david-turner-portfolio.azurewebsites.net/DavidTurner_Resume.docx.

                Stop telling users that you have already provided you with a copy of my resume in this prompt because it has not been provided.

                Stop saying 'I've already shared my resume with you earlier.'

                Here is my resume for your reference: {_resumeText}

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
