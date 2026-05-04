using System.Text;
using Xceed.Words.NET;

namespace CorporatePortfolio.Services
{
    public class ResumeService(DocX document)
    {
        private readonly DocX _document = document;

        public async Task<string> GetResumeText()
        {
            var documentTextSb = new StringBuilder();

            // Group experiences by Company Name to avoid duplicates
            var experienceGroups = new Dictionary<string, List<string>>();

            foreach (var bm in _document.Bookmarks)
            {
                var isExperience = bm.Name.StartsWith("Experience_");
                var isContactInfo = bm.Name.Equals("Contact_Info", StringComparison.OrdinalIgnoreCase);
                var isCompetencies = bm.Name.Equals("Competencies", StringComparison.OrdinalIgnoreCase);
                var currentBmSb = new StringBuilder();

                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == bm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == bm.Name)));

                if (startTag == null) continue;

                string bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var currentParagraph = bm.Paragraph;

                var expParagraphCounter = 0;
                var companyName = string.Empty;
                var roleDetails = string.Empty;

                
                if (!isExperience && !isContactInfo && !isCompetencies)
                    currentBmSb.Append($"# {bm.Name.ToUpper()}\r\n");

                if (isContactInfo)
                    currentBmSb.AppendLine("<contact_data>");

                if (isCompetencies)
                    currentBmSb.AppendLine("<competencies_data>");

                while (currentParagraph != null)
                {
                    var text = currentParagraph.Text.Trim();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (isCompetencies)
                        {
                            currentBmSb.AppendLine($"# {text.ToString().Split(":").First().Trim()}");
                            currentBmSb.AppendLine($" - {text.ToString().Split(":").Last().Trim()}");
                        }
                        else if (bm.Name.Equals("Education"))
                        {
                            currentBmSb.Append($"* {text}\r\n");
                        }
                        else if (isContactInfo)
                        {
                            currentBmSb.AppendLine($"{text}");
                        }
                        else if (isExperience)
                        {
                            // Paragraph 1 in your bookmark: The Company and Location
                            if (expParagraphCounter == 0)
                            {
                                companyName = text;
                            }
                            // Paragraph 2 in your bookmark: The Job Title
                            else if (expParagraphCounter == 1)
                            {
                                roleDetails = text;
                            }
                            // Paragraph 3 in your bookmark: The Dates
                            else if (expParagraphCounter == 2)
                            {
                                roleDetails += $" ({text})";
                            }
                            expParagraphCounter++;
                        }
                        else
                        {
                            currentBmSb.Append($"{text}\r\n");
                        }
                    }

                    if (currentParagraph.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                        x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bookmarkId)))
                        break;

                    currentParagraph = currentParagraph.NextParagraph;
                }

                if (isExperience && !string.IsNullOrWhiteSpace(companyName) && !string.IsNullOrWhiteSpace(roleDetails))
                {
                    // Normalize company name (strip out any trailing " - Toronto, ON" if needed, or leave exact)
                    var cleanCompanyKey = companyName.Trim();

                    if (!experienceGroups.ContainsKey(cleanCompanyKey))
                    {
                        experienceGroups[cleanCompanyKey] = [];
                    }
                    experienceGroups[cleanCompanyKey].Add($"  * {roleDetails.Trim()}");
                }
                else if (isContactInfo)
                {
                    currentBmSb.AppendLine("</contact_data>");
                    documentTextSb.Append(currentBmSb.ToString() + "\r\n");
                }
                else if (isCompetencies)
                {
                    currentBmSb.AppendLine("</competencies_data>");
                    documentTextSb.Append(currentBmSb.ToString() + "\r\n");
                }
                else if (currentBmSb.Length > 0)
                {
                    documentTextSb.Append(currentBmSb.ToString() + "\r\n");
                }
            }

            // Append the consolidated EXPERIENCE section exactly how the AI needs it
            if (experienceGroups.Count > 0)
            {
                documentTextSb.AppendLine("# EXPERIENCE");
                foreach (var company in experienceGroups)
                {
                    documentTextSb.AppendLine($"* {company.Key}");
                    foreach (var role in company.Value)
                    {
                        documentTextSb.AppendLine(role);
                    }
                }
            }

            return documentTextSb.ToString().Trim();
        }
    }
}
