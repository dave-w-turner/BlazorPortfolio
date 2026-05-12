using CorporatePortfolio.Services.DTO;
using System.Text;
using Xceed.Document.NET;
using Xceed.Words.NET;
using static System.Net.Mime.MediaTypeNames;

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
            var projects = false;

            foreach (var bm in _document.Bookmarks)
            {
                var isExperience = bm.Name.StartsWith("Experience_");
                var isContactInfo = bm.Name.Equals("Contact_Info", StringComparison.OrdinalIgnoreCase);
                var isCompetencies = bm.Name.Equals("Competencies", StringComparison.OrdinalIgnoreCase);
                var isProject = bm.Name.StartsWith("Project_", StringComparison.OrdinalIgnoreCase);
                var currentBmSb = new StringBuilder();

                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == bm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == bm.Name)));

                if (startTag == null) continue;

                string bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value ?? string.Empty;
                var currentParagraph = bm.Paragraph;

                var expParagraphCounter = 0;
                var projParagraphCounter = 0;
                var companyName = string.Empty;
                var roleDetails = string.Empty;
                var projectDetailsSb = new StringBuilder();


                if (!isExperience && !isContactInfo && !isCompetencies && !isProject)
                    currentBmSb.Append($"# {bm.Name.ToUpper()}\r\n");

                if (isContactInfo)
                    currentBmSb.AppendLine("<contact_data>");

                if (isCompetencies)
                    currentBmSb.AppendLine("<competencies_data>");

                if (isProject && !projects)
                {
                    currentBmSb.AppendLine("#PROJECTS");
                    projects = true;
                }

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
                        else if (isProject)
                        {
                            currentBmSb.AppendLine($"{(projParagraphCounter == 0 ? "- " : "  * ")}{text}");

                            if (projParagraphCounter == 0)
                                projParagraphCounter++;
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

                if (!isProject)
                    projects = false;
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

        public async Task<List<CompetencyData>> GetCompetencies()
        {
            var competencyBm = _document.Bookmarks.Where(bm => bm.Name.Equals("Competencies")).FirstOrDefault();
            var bmId = competencyBm != null ? _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == competencyBm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == competencyBm.Name)))
                    ?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value : null;
            List<CompetencyData> competencies = [];
            var competenciesSb = new StringBuilder();

            if (competencyBm != null)
            {
                
                var currentParagraph = competencyBm.Paragraph;

                while ((!currentParagraph?.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                            x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bmId))) ?? false)
                {
                    competenciesSb.AppendLine(currentParagraph?.Text.Trim());
                    currentParagraph = currentParagraph?.NextParagraph;
                }
            }

            var skillList = await GetTagsFromProject(competenciesSb.ToString());

            foreach (var skill in skillList)
            {
                competencies.Add(new CompetencyData
                {
                    Name = skill,
                    Icon = await GetIconForCompetency(skill)
                });
            }

            return [.. competencies.Where(c => !string.IsNullOrEmpty(c.Icon)).ToList().OrderBy(c => c.Name).Take(6)];
        }

        public async Task<List<ExperienceData>> GetExperience()
        {
            var experienceBms = _document.Bookmarks.Where(bm => bm.Name.StartsWith("Experience_") && !bm.Name.EndsWith("_Details")).ToList();
            List<ExperienceData> experiences = [];

            foreach (var experienceBm in experienceBms)
            {
                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == experienceBm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == experienceBm.Name)));

                if (startTag == null) continue;

                string? bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var currentParagraph = experienceBm.Paragraph;
                ExperienceData? experience = null;

                var text = currentParagraph.Text.Trim();

                experience ??= new ExperienceData
                {
                    CompanyName = text.Split('–')[0].Trim(),
                    LocationName = text.Split('–').Length > 1 ? text.Split('–')[1].Trim() : string.Empty,
                    Title = currentParagraph.NextParagraph?.Text.Trim() ?? string.Empty,
                    Date = currentParagraph.NextParagraph?.NextParagraph?.Text.Trim() ?? string.Empty,
                };

                var experienceDetailsBm = _document.Bookmarks.FirstOrDefault(bm => bm.Name == $"{experienceBm.Name}_Details");
                var experienceParagraph = experienceDetailsBm?.Paragraph;
                var experienceBmId = experienceDetailsBm != null ? _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == experienceDetailsBm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == experienceDetailsBm.Name)))
                    ?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value : null;

                while ((!experienceParagraph?.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                         x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == experienceBmId))) ?? false)
                {
                    if (experienceParagraph != null)
                    {
                        experience?.Details.Add(experienceParagraph.Text.Trim());
                        experienceParagraph = experienceParagraph.NextParagraph;

                        if (experienceParagraph?.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                         x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == experienceBmId)) ?? false)
                            experience?.Details.Add(experienceParagraph.Text.Trim());
                    }
                }

                currentParagraph = currentParagraph?.NextParagraph?.NextParagraph?.NextParagraph;

                if (experience != null)
                    experiences.Add(experience);
            }

            return experiences;
        }

        public async Task<List<ProjectData>> GetProjects()
        {
            var projectBms = _document.Bookmarks.Where(bm => bm.Name.StartsWith("Project_")).ToList();
            List<ProjectData> projects = [];

            foreach (var projectBm in projectBms)
            {
                var startTag = _document.Xml.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "bookmarkStart" &&
                                         (x.Attribute("name")?.Value == projectBm.Name ||
                                          x.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == projectBm.Name)));

                if (startTag == null) continue;

                string? bookmarkId = startTag.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var currentParagraph = projectBm.Paragraph;

                var text = currentParagraph.Text.Trim();
                var project = new ProjectData
                {
                    Title = text.Split('|')[0].Trim(),
                    Date = text.Split('|').Length > 1 ? text.Split('|')[1].Trim() : string.Empty,
                    SourceUrl = text.Split('|').Length > 2 ? text.Split('|')[2].Trim() : string.Empty
                };

                currentParagraph = currentParagraph.NextParagraph;

                while ((!currentParagraph?.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                         x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bookmarkId))) ?? false)
                {
                    if (currentParagraph != null)
                    {
                        project?.Details.Add(currentParagraph.Text.Trim());
                        currentParagraph = currentParagraph.NextParagraph;

                        if (currentParagraph?.Xml.DescendantsAndSelf().Any(x => x.Name.LocalName == "bookmarkEnd" &&
                         x.Attributes().Any(a => a.Name.LocalName == "id" && a.Value == bookmarkId)) ?? false)
                            project?.Details.Add(currentParagraph.Text.Trim());
                    }
                }

                currentParagraph = currentParagraph?.NextParagraph?.NextParagraph?.NextParagraph;

                if (project != null)
                    projects.Add(project);
            }

            return projects;
        }

        public static async Task<List<string>> GetTagList()
        {
            return
                 [.. new List<string>() {
                     "SignalR", "Entity Framework", "C#", "ASP.NET Core", "LINQ", ".NET Core", ".NET", "Azure", "SQL", "MVC",
                    "TypeScript", "Angular", "JavaScript", "Agile", "Azure",
                    "RESTful APIs", "Microservices", "Unit Testing", "Dependency Injection", "Design Patterns",
                    "Continuous Integration", "Continuous Deployment", "Docker", "Kubernetes",
                    "Azure Functions", "Azure Logic Apps", "Azure Service Bus", "Azure Event Grid", "Azure Key Vault",
                    "Azure Application Insights", "Azure Monitor", "Azure Blob Storage", "Azure Cosmos DB",
                    "Azure Active Directory", "Azure API Management", "Azure Cognitive Services",
                    "Azure Kubernetes Service", "Azure Container Instances", "Azure Virtual Machines",
                    "Azure Virtual Network", "Azure Load Balancer", "YAML Pipelines", "CI/CD", "Scrum", "Kanban",
                    "Jira", "TFS", "Git", "GitHub", "GitLab", "Active Directory", "OAuth", "OpenID Connect", "JWT",
                    "SAML", "SSO", "IdentityServer", "ASP.NET Identity", "ADFS", "LDAP", "OAuth2", "OpenID", "SAML2",
                    "SSO Integration", "Identity Management", "SCCM", "PowerShell", "Windows Server", "IIS",
                    "SQL Server", "SSRS", "SSIS", "SSAS", "Visual Studio", "VS Code", "ReSharper", "NuGet", "NUnit",
                    "xUnit", "MSTest", "Selenium", "Postman", "Swagger", "Fiddler", "Wireshark", "JMeter",
                    "Load Testing", "Performance Testing", "Security Testing", "Penetration Testing",
                    "Vulnerability Assessment", "OWASP", "CIS Benchmarks", "NIST", "ISO 27001", "SOC 2", "HIPAA",
                    "GDPR", "PCI DSS", "FISMA", "SOX Compliance", "ITIL", "COBIT", "ISO 20000", "ISO 22301",
                    "Business Continuity", "Disaster Recovery", "Risk Management", "Incident Response",
                    "Change Management", "Configuration Management", "Release Management", "Problem Management",
                    "Service Level Agreements", "Key Performance Indicators", "Metrics", "Reporting", "Dashboards",
                    "Business Intelligence", "Data Warehousing", "ETL", "Data Mining", "Data Analytics",
                    "Machine Learning", "Artificial Intelligence", "Deep Learning", "Natural Language Processing",
                    "Computer Vision", "Robotics", "IoT", "Blockchain", "Cryptography", "Quantum Computing",
                    "Augmented Reality", "Virtual Reality", "Mixed Reality", "3D Modeling", "Game Development",
                    "Mobile Development", "iOS", "Android", "React Native", "Flutter", "Xamarin",
                    "Progressive Web Apps", "PWA", "WebAssembly", "Blazor", "gRPC", "WebSockets", "REST",
                    "GraphQL", "SOAP", "JSON", "XML", "YAML", "CSV", "HTML", "CSS", "SASS", "LESS", "Bootstrap",
                    "Tailwind CSS", "Material Design", "Responsive Design", "Accessibility", "WCAG", "ARIA",
                    "SEO", "Performance Optimization", "Cross-Browser Compatibility", "Cross-Platform Development",
                    "Cloud Computing", "AWS", "Google Cloud Platform", "Serverless Architecture", "Edge Computing",
                    "Fog Computing", "Containerization", "Orchestration", "DevSecOps", "Site Reliability Engineering",
                    "Observability", "Logging", "Monitoring", "Alerting", "Tracing", "Metrics Collection",
                    "Distributed Systems", "Event-Driven Architecture", "Message Queues", "Pub/Sub", "Streaming Data",
                    "Real-Time Processing", "Batch Processing", "Data Lakes", "Data Pipelines", "ETL Processes",
                    "Data Governance", "Data Quality", "Master Data Management", "Data Cataloging", "Data Lineage",
                    "Data Privacy", "Kendo UI", "Syncfusion", "Telerik", "DevExpress", "Component Libraries", "UI Frameworks",
                    "jQuery", "React", "Vue.js", "AngularJS", "Ember.js", "Backbone.js", "Knockout.js", "Responsive UI",
                    "Single Page Applications", "SOLID Principles", "Clean Code", "Refactoring", "Code Reviews",
                    "Pair Programming", "TDD", "BDD", "Agile Methodologies", "Lean", "XP", "SDLC", "Waterfall",
                    "Practices", "Infrastructure as Code", "Monitoring and Logging", ".NET Framework", ".NET 5",
                    ".NET 6", ".NET 7", "VB.NET", "F#", "ASP.NET", "Razor Pages", "Entity Framework Core", "Web API",
                    "RESTful Services", "Microservices Architecture", "Middleware", "Routing", "Authentication and Authorization",
                    "JWT Tokens", "Web Forms", "Lambda Expressions", "Delegates", "Events", "Generics", "Collections",
                    "Data Structures", "App Services", "Azure Storage", "Azure SQL Database", "Cosmos DB", "HTML5", "CSS3",
                    "Node.js", "Express.js", "MongoDB", "PostgreSQL", "MySQL", "SQLite", "Redis", "code analysis",
                    "static code analysis", "dynamic code analysis", "profiling", "performance tuning", "memory management",
                    "garbage collection", "Razor", "App Service", "Ollama", "LLM", "Llama 3.2", ".NET 10 (LTS)", "Groq",
                    "DevOps", "AD", "Generative AI", "AI Vision", "SmartSimple", "CRM", "CRM Dynamics", "MS Test",
                    "Angular 2.0", "Windows", "Linux", "Ubuntu", "Fedora", "Redhat", "Kali", "Llama3.2", "Model Training",
                    "Automation"
                 }.Distinct()];
        }

        private static async Task<List<string>> GetTagsFromProject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];

            var allTags = await GetTagList();

            var matchedTags = allTags
                .Where(tag => text.Contains(tag, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matchedTags;
        }

        private static async Task<string> GetIconForCompetency(string competency)
        {
            return competency.ToLower() switch
            {
                var s when s.Contains("sql") || s.Contains("database") => "table",
                var s when s.Contains("c#") => "code",
                var s when s.Contains("azure") || s.Contains("devops") => "cloud",
                var s when s.Contains("automation") => "cpu",
                var s when s.Contains("responsive ui") => "window-dock",
                _ => string.Empty
            };
        }
    }
}