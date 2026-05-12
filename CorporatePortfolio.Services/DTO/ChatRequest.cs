using CorporatePortfolio.Services.DTO;

namespace CorporatePortfolio.DTO
{
    public class ChatRequest
    {
        public string Question { get; set; }
        public List<ChatMessageRequest> History { get; set; }
    }
}
