namespace CorporatePortfolio.Services.DTO
{
    public class ProjectData
    {
        public string Title { get; set; }
        public string Date { get; set; }
        public string SourceUrl { get; set; }
        public List<string> Details { get; set; } = [];
        public List<string> Tags { get; set; } = [];
    }
}
