using System.Text;
using Xceed.Words.NET;

namespace CorporatePortfolio.Services
{
    public class ResumeService(DocX document)
    {
        private readonly DocX _document = document;

        public async Task<string> GetResumeText(bool isDevelopment)
        {
            if (isDevelopment) return _document.Text;

            var documentTextSb = new StringBuilder();
            var experienceList = new List<string>();

            foreach (var bm in _document.Bookmarks)
            {
                var isExperience = bm.Name.StartsWith("Experience_");
                var currentBmSb = new StringBuilder();

                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == bm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == bm.Name)));

                if (startTag == null) continue;

                string bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var currentParagraph = bm.Paragraph;
                var expParagraphCounter = 0;
                var expLine = string.Empty;

                if (!isExperience)
                    currentBmSb.Append($"{bm.Name.ToUpper()}: ");

                while (currentParagraph != null)
                {
                    var text = currentParagraph.Text.Trim();

                    // Replace 'goto' logic with a standard if check
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (bm.Name.Equals("Skills") || bm.Name.Equals("Education"))
                        {
                            currentBmSb.AppendLine();
                            currentBmSb.Append($"* {text}");
                        }
                        else if (isExperience)
                        {
                            if (expParagraphCounter == 0) expLine += $"* {text}: ";
                            else if (expParagraphCounter == 1) expLine += $"{text} ";
                            else if (expParagraphCounter == 2) expLine += $"({text})";
                            expParagraphCounter++;
                        }
                        else
                        {
                            currentBmSb.AppendLine();
                            currentBmSb.Append(text);
                        }
                    }

                    // The loop always proceeds to check the end tag and move to next paragraph
                    if (currentParagraph.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                        x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bookmarkId)))
                        break;

                    currentParagraph = currentParagraph.NextParagraph;
                }

                if (isExperience && !string.IsNullOrWhiteSpace(expLine))
                    experienceList.Add(expLine.Trim());
                else if (currentBmSb.Length > 0)
                    documentTextSb.AppendLine(currentBmSb.ToString().TrimEnd() + "\r\n");
            }

            if (experienceList.Count > 0)
            {
                documentTextSb.AppendLine("EXPERIENCE:");
                foreach (var exp in experienceList) documentTextSb.AppendLine(exp);
            }

            return documentTextSb.ToString().Trim();
        }

    }
}
