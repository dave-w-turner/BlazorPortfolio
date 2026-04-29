namespace CorporatePortfolio.Controller
{
    using CorporatePortfolio.Services;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("api/chat")]
    public class ChatbotController(ChatbotService chatbot) : ControllerBase
    {
        private readonly ChatbotService _chatbot = chatbot;

        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] string question)
        {
            var response = await _chatbot.Ask(question);
            return Ok(response);
        }
    }
}
    