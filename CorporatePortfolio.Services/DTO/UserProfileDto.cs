namespace CorporatePortfolio.Services.DTO
{
    public class UserProfileDto
    {
        public bool IsAuthenticated { get; set; }
        public string UserName { get; set; } = "";
        public List<string> Roles { get; set; } = new();
    }
}
