namespace CorporatePortfolio.Services
{
    public interface IWebHostEnvironment
    {
        public string WebRootPath { get; set; }
        public string ContentRootPath { get; set; }
    }
}