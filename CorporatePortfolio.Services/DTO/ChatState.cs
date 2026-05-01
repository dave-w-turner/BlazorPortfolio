namespace CorporatePortfolio.Services.DTO
{
    public class ChatState
    {
        public string? Top { get; set; }
        public string? Left { get; set; }
        public string? Width { get; set; }
        public string? Height { get; set; }

        public bool HasPosition => !string.IsNullOrEmpty(Top);
    }
}
