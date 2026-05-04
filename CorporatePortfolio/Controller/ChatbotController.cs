using CorporatePortfolio.DTO;
using CorporatePortfolio.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace CorporatePortfolio.Controller
{
    [ApiController]
    [Route("api/chat")]
    public class ChatbotController(ChatbotService chatbot) : ControllerBase
    {
        private readonly ChatbotService _chatbot = chatbot;

        [HttpPost]
        public async Task Ask([FromBody] ChatRequest request)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Content-Type-Options", "nosniff");

            var stream = await _chatbot.Ask(request.Question, request.History);

            var buffer = new StringBuilder();
            bool isBuffering = false;

            await foreach (var chunk in stream)
            {
                if (string.IsNullOrEmpty(chunk)) continue;

                // Detect when a Markdown link starts
                if (chunk.Contains("["))
                {
                    isBuffering = true;
                }

                if (isBuffering)
                {
                    buffer.Append(chunk);

                    // Detect when the Markdown link completes
                    if (chunk.Contains(")"))
                    {
                        var completeLink = buffer.ToString();
                        await Response.WriteAsync($"data: {completeLink}\n\n");
                        await Response.Body.FlushAsync();
                        buffer.Clear();
                        isBuffering = false;
                    }
                }
                else
                {
                    // Regular text is streamed instantly
                    await Response.WriteAsync($"data: {chunk}\n\n");
                    await Response.Body.FlushAsync();
                }
            }

            // Flush any leftover text in the buffer just in case
            if (buffer.Length > 0)
            {
                await Response.WriteAsync($"data: {buffer}\n\n");
                await Response.Body.FlushAsync();
            }
        }
    }
}
