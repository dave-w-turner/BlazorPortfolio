namespace CorporatePortfolio.Controller
{
    using CorporatePortfolio.DTO;
    using CorporatePortfolio.Services;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("api/chat")]
    public class ChatbotController(ChatbotService chatbot) : ControllerBase
    {
        private readonly ChatbotService _chatbot = chatbot;

        [HttpPost]
        public async Task Ask([FromBody] ChatRequest request)
        {
            Response.ContentType = "text/plain";

            // This header tells browsers and proxies not to buffer your stream
            Response.Headers.Append("X-Content-Type-Options", "nosniff");

            // Get the IAsyncEnumerable stream from your service
            var stream = await _chatbot.Ask(request.Question, request.History);

            // Iterate over the tokens as they arrive
            await foreach (var chunk in stream)
            {
                if (!string.IsNullOrEmpty(chunk))
                {
                    // Write the token directly to the HTTP response body
                    await Response.WriteAsync(chunk);
                    await Response.Body.FlushAsync(); // Force the word to travel to the browser now
                }
            }
        }
    }
}
    