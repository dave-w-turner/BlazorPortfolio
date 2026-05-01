using CorporatePortfolio.Services.DTO;

namespace CorporatePortfolio.DTO
{
    public class ChatRequest
    {
        public string Question { get; set; }
        public List<ChatMessage> History { get; set; }
    }
}
