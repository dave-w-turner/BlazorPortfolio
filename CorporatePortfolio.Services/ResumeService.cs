using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xceed.Words.NET;

namespace CorporatePortfolio.Services
{
    public class ResumeService(DocX document)
    {
        private readonly DocX _document = document;

        public async Task<string> GetResumeText(bool isDevelopment)
        {
            if (isDevelopment)
                return _document.Text;

            var documentTextSb = new StringBuilder();
            XNamespace w = "http://openxmlformats.org";

            foreach (var bm in _document.Bookmarks)
            {
                var currentBmSb = new StringBuilder($"{bm.Name.ToUpper()}:\r\n");

                // 1. Find the start tag by checking ONLY the LocalName (ignoring namespaces)
                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == bm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == bm.Name)));

                if (startTag == null)
                {
                    // Debug: If still null, try to see if the library's Paragraph property works at all
                    if (bm.Paragraph != null)
                    {
                        currentBmSb.AppendLine(bm.Paragraph.Text + " (End tag not found)");
                    }
                    continue;
                }

                // 2. Get the ID using LocalName
                string bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var currentParagraph = bm.Paragraph;

                while (currentParagraph != null)
                {
                    if (bm.Name.Equals("Competencies"))
                        currentBmSb.AppendLine($"- {currentParagraph.Text}");
                    else
                        currentBmSb.AppendLine(currentParagraph.Text);

                    // 3. Check for end tag using LocalName
                    bool hasEndTag = currentParagraph.Xml.DescendantsAndSelf()
                        .Any(x => x.Name.LocalName == "bookmarkEnd" &&
                                  x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bookmarkId));

                    if (hasEndTag)
                        break;

                    currentParagraph = currentParagraph.NextParagraph;
                    if (currentParagraph == null) break;
                }

                documentTextSb.AppendLine(currentBmSb.ToString());
                documentTextSb.AppendLine(string.Empty);
            }

            return documentTextSb.ToString();
        }
    }
}
