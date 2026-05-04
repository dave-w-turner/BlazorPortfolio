namespace CorporatePortfolio.Services.DTO
{
    public class ChatMessage
    {
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public string UserName { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public bool IsComplete { get; set; } = true; // Default to true
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
