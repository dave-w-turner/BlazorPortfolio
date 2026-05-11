namespace CorporatePortfolio.Services.DTO
{
    public class ExperienceData
    {
        public string CompanyName { get; set; }
        public string LocationName { get; set; }
        public string Title { get; set; }
        public string Date { get; set; }
        public List<string> Details { get; set; } = [];
    }
}
