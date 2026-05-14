using Microsoft.AspNetCore.Components;

namespace CorporatePortfolio.Services.DTO
{
    public class FormattedText
    {
        public MarkupString Text { get; set; }
        public bool HasKeyword { get; set; }

        public FormattedText(string text, bool hasKeyword = false)
        {
            Text = new MarkupString(text);
            HasKeyword = hasKeyword;
        }
    }
}
