using CorporatePortfolio.Services.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.Text.RegularExpressions;
using Xceed.Words.NET;

namespace CorporatePortfolio.Services
{
    public class ResumeService(ChatbotService chatbotService, DocX document, IMemoryCache memoryCache)
    {
        private readonly DocX _document = document;
        private readonly ChatbotService _chatbotService = chatbotService;
        private readonly IMemoryCache _memoryCache = memoryCache;

        public async Task<string> GetResumeText()
        {
            var fileInfo = new FileInfo("DavidTurner_Resume.docx");
            return await _memoryCache.GetOrCreateAsync("#resumeText", async entry =>
            {
                var fileProvider = new PhysicalFileProvider(Path.GetDirectoryName(fileInfo.FullName)!);
                var changeToken = fileProvider.Watch(Path.GetFileName(fileInfo.Name));
                entry.ExpirationTokens.Add(changeToken);

                // Fix 1: Use isolated builders to keep summary blocks and detail blocks separated
                var summarySectionSb = new StringBuilder();
                var dataBlocksSb = new StringBuilder();

                var experienceGroups = new Dictionary<string, List<string>>();
                var projects = false;
                var expCounter = 0;
                var expDetailsCounter = 0;

                foreach (var bm in _document.Bookmarks)
                {
                    var isExperience = bm.Name.StartsWith("Experience_", StringComparison.OrdinalIgnoreCase) &&
                                       !bm.Name.EndsWith("_Details", StringComparison.OrdinalIgnoreCase);
                    var isExperienceDetails = bm.Name.StartsWith("Experience_", StringComparison.OrdinalIgnoreCase) &&
                                              bm.Name.EndsWith("_Details", StringComparison.OrdinalIgnoreCase);
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

                    if (isContactInfo)
                        currentBmSb.AppendLine("<contact_data>");
                    else if (isCompetencies)
                        currentBmSb.AppendLine("<competencies_data>");
                    else if (isProject && !projects)
                    {
                        currentBmSb.AppendLine();
                        currentBmSb.AppendLine("<projects_data>");
                        projects = true;
                    }
                    else if (isExperienceDetails)
                    {
                        expDetailsCounter++;
                        currentBmSb.AppendLine($"<experience_{expDetailsCounter}_details>");
                    }
                    else if (!isExperience)
                    {
                        currentBmSb.AppendLine($"<{bm.Name.ToLower().Replace(" ", "_")}_data>");
                    }

                    while (currentParagraph != null)
                    {
                        var text = currentParagraph.Text.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            if (isCompetencies)
                            {
                                currentBmSb.AppendLine($"# {text.ToString().Split(':').First().Trim()}");
                                currentBmSb.AppendLine($"- {text.ToString().Split(':').Last().Trim()}");
                            }
                            else if (bm.Name.Equals("Education"))
                            {
                                currentBmSb.Append($"* {text}\r\n");
                            }
                            else if (isContactInfo)
                            {
                                string cleanLine = text;
                                int portfolioIdx = text.IndexOf("| Portfolio:", StringComparison.OrdinalIgnoreCase);
                                if (portfolioIdx >= 0)
                                {
                                    cleanLine = text[..portfolioIdx].TrimEnd();
                                }
                                currentBmSb.AppendLine($"{cleanLine}");
                            }
                            else if (isExperience)
                            {
                                if (expParagraphCounter == 0) companyName = text;
                                else if (expParagraphCounter == 1) roleDetails = text;
                                else if (expParagraphCounter == 2) roleDetails += $" ({text})";
                                expParagraphCounter++;
                            }
                            else if (isExperienceDetails)
                            {
                                currentBmSb.AppendLine($"- {text}");
                            }
                            else if (isProject)
                            {
                                currentBmSb.AppendLine($"{(projParagraphCounter == 0 ? $"# {string.Join(" | ", text.Split(" | ").Take(2))}" : $"  - {text}")}");
                                if (projParagraphCounter == 0) projParagraphCounter++;
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
                        var cleanCompanyKey = companyName.Trim();
                        if (!experienceGroups.ContainsKey(cleanCompanyKey))
                        {
                            experienceGroups[cleanCompanyKey] = [];
                        }
                        // Fix 2: Pre-render structural indentation spacing natively right inside our data block collection
                        experienceGroups[cleanCompanyKey].Add($"    - {roleDetails.Trim()}");
                    }
                    else if (isContactInfo)
                    {
                        currentBmSb.AppendLine("</contact_data>");
                        dataBlocksSb.Append(currentBmSb.ToString() + "\r\n");
                    }
                    else if (isCompetencies)
                    {
                        currentBmSb.AppendLine("</competencies_data>");
                        dataBlocksSb.Append(currentBmSb.ToString() + "\r\n");
                    }
                    else if (isProject)
                    {
                        currentBmSb.AppendLine("</projects_data>");
                        dataBlocksSb.Append(currentBmSb.ToString() + "\r\n");
                    }
                    else if (isExperienceDetails)
                    {
                        currentBmSb.AppendLine($"</experience_{expDetailsCounter}_details>");
                        dataBlocksSb.Append(currentBmSb.ToString() + "\r\n");
                    }
                    else
                    {
                        currentBmSb.AppendLine($"</{bm.Name.ToLower().Replace(" ", "_")}_data>");
                        dataBlocksSb.Append(currentBmSb.ToString() + "\r\n");
                    }

                    if (!isProject) projects = false;
                }

                // 1. Initialize the final output payload container
                var finalPayloadAssembly = new StringBuilder();

                // 2. FORCE the clean experience list block straight to the top of the data stream
                if (experienceGroups.Count > 0)
                {
                    finalPayloadAssembly.AppendLine("<experience_summary>");
                    foreach (var company in experienceGroups)
                    {
                        finalPayloadAssembly.AppendLine($"# {company.Key}");
                        foreach (var role in company.Value)
                        {
                            expCounter++;
                            // Adding 2 spaces at the end forces Markdown components to break lines
                            finalPayloadAssembly.AppendLine(role + "  ");
                        }
                        if (experienceGroups.Last().Key != company.Key)
                        {
                            finalPayloadAssembly.AppendLine();
                            finalPayloadAssembly.AppendLine();
                        }
                    }
                    finalPayloadAssembly.AppendLine("</experience_summary>");
                    finalPayloadAssembly.AppendLine();

                    // Append the structural mapping keys right below it
                    finalPayloadAssembly.AppendLine("<experience_mappings>");
                    expCounter = 0;
                    foreach (var company in experienceGroups)
                    {
                        var appendCounter = 0;
                        // Safely split the company name to remove trailing locations for the query matching text
                        string companySearchName = company.Key.Split('–')[0].Trim();
                        finalPayloadAssembly.Append($"- If user asks about {companySearchName} accomplishments, read ");
                        foreach (var role in company.Value)
                        {
                            finalPayloadAssembly.Append($"<experience_{++expCounter}_details>");
                            if (++appendCounter < company.Value.Count)
                                finalPayloadAssembly.Append(" and ");
                            else
                                finalPayloadAssembly.AppendLine();
                        }
                    }
                    finalPayloadAssembly.AppendLine("</experience_mappings>");
                    finalPayloadAssembly.AppendLine();
                }

                // 3. Append the heavy technical details and projects below the summary line
                finalPayloadAssembly.Append(dataBlocksSb.ToString());

                // 4. Return the complete, compiled, error-free text payload
                return finalPayloadAssembly.ToString().Trim();
            }) ?? string.Empty;
        }

        public async Task<List<CompetencyData>> GetCompetencies(List<ExperienceData> experiences, List<ProjectData> projects)
        {
            var fileInfo = new FileInfo("DavidTurner_Resume.docx");
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
                    if (!string.IsNullOrEmpty(currentParagraph?.Text))
                    {
                        competenciesSb.AppendLine(currentParagraph?.Text.Trim());
                    }

                    currentParagraph = currentParagraph?.NextParagraph;
                }
            }

            var skillList = await GetTagsFromProject(competenciesSb.ToString());

            var flatExperienceDetails = experiences.SelectMany(e => e.Details).ToList();
            var flatProjectDetails = projects.SelectMany(p => p.Details).ToList();

            var skillRegexes = skillList.Select(skill =>
            {
                string escapedSkill = Regex.Escape(skill);

                string endBoundary = Regex.IsMatch(skill, @"[a-zA-Z0-9]$") ? @"\b" : @"(?![a-zA-Z0-9])";
                string startBoundary = Regex.IsMatch(skill, @"^[a-zA-Z0-9]") ? @"\b" : @"(?<![a-zA-Z0-9])";

                return new
                {
                    SkillName = skill,
                    Pattern = new Regex(startBoundary + escapedSkill + endBoundary, RegexOptions.IgnoreCase | RegexOptions.Compiled)
                };
            }).ToList();

            var matchingExperiences = skillRegexes
                .Where(sr => flatExperienceDetails.Any(detail => sr.Pattern.IsMatch(detail)))
                .Select(sr => sr.SkillName);

            var matchingProjects = skillRegexes
                .Where(sr => flatProjectDetails.Any(detail => sr.Pattern.IsMatch(detail)))
                .Select(sr => sr.SkillName);

            List<string> distinctMatchingSkills = [.. matchingExperiences.Union(matchingProjects)];
            List<string> filteredSkills = [.. distinctMatchingSkills
                    .Where(skill => !distinctMatchingSkills.Any(otherSkill =>
                        otherSkill.Length > skill.Length &&
                        otherSkill.Contains(skill, StringComparison.OrdinalIgnoreCase)))];

            var randomSkillSet = new List<string>();

            Random randomGenerator = new();

            for (int i = 0; i < 6; i++)
            {
                int randomSkillIndex = randomGenerator.Next(0, filteredSkills.Count - 1);
                var skill = filteredSkills[randomSkillIndex];

                if (!randomSkillSet.Contains(skill))
                    randomSkillSet.Add(skill);
                else
                {
                    i--;
                }
            }

            foreach (var skill in randomSkillSet)
            {
                competencies.Add(new CompetencyData
                {
                    Name = skill,
                    Icon = await GetIconForCompetency(skill)
                });
            }

            List<CompetencyData> selectedSkills = [.. competencies
                    .OrderBy(c => c.Name)];

            string resumeText = await GetResumeText();

            foreach (var skill in selectedSkills)
            {
#if DEBUG
                skill.Summary = new FormattedText($"Summary {selectedSkills.IndexOf(skill) + 1}");
#else
                    skill.Summary = await ChatbotService.FormatMessage(await _chatbotService.Generate(
                                            $@"Please summarize this skill: '{skill.Name}'. DO NOT MENTION SUMMARY IN YOUR ANSWER. DO NOT MENTION THE EMPLOYERS. 
                                                JUST SUMMARIZE THE SKILL AND INCLUDE THE SKILL NAME ONLY ONCE WITHIN THE SUMMARY ITSELF.
                                                Provide a concise summary of 2 sentences that highlights the key aspects and importance of this skill in the context of
                                                the resume data provided. Avoid generic descriptions and focus on what makes this skill valuable to potential employers.
                                                Only 1 paragraph MAX! Instead of mentioning developers, speak in the first person context.",
                                            resumeText), true, null, true);
#endif

            }

            return selectedSkills;
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
                    "Automation", "Arduino", "C++", "Android"
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
            if (string.IsNullOrWhiteSpace(competency)) return "tag";

            return competency.ToLower().Trim() switch
            {
                // --- Languages ---
                var s when s.Contains("c#") => "code-square",
                var s when s.Contains("c++") => "filetype-cpp",
                var s when s.Contains("vb.net") => "file-earmark-code",
                var s when s.Contains("f#") => "hash",
                var s when s.Contains("typescript") => "filetype-tsx",
                var s when s.Contains("javascript") => "filetype-js",
                var s when s.Contains("html5") || s.Contains("html") => "filetype-html",
                var s when s.Contains("css3") || s.Contains("css") => "filetype-css",
                var s when s.Contains("sass") => "filetype-sass",
                var s when s.Contains("less") => "magic",
                var s when s.Contains("json") => "filetype-json",
                var s when s.Contains("xml") => "filetype-xml",
                var s when s.Contains("csv") => "filetype-csv",

                // --- Databases & Data ---
                var s when s.Contains("sql server") => "database-fill-lock",
                var s when s.Contains("cosmos db") => "database-fill-gear",
                var s when s.Contains("mongodb") => "database-add",
                var s when s.Contains("postgresql") => "database-fill-up",
                var s when s.Contains("mysql") => "database-fill-down",
                var s when s.Contains("sqlite") => "database-fill",
                var s when s.Contains("redis") => "lightning-fill",
                var s when s.Contains("data warehousing") || s.Contains("data lakes") => "stack",
                var s when s.Contains("etl") => "arrow-left-right",
                var s when s.Contains("data mining") => "search-heart",
                var s when s.Contains("data analytics") || s.Contains("data quality") || s.Contains("metrics") => "bar-chart-line-fill",
                var s when s.Contains("sql") || s.Contains("database") || s.Contains("entity framework") => "database",

                // --- .NET & Web Architecture ---
                var s when s.Contains("signalr") || s.Contains("websockets") => "broadcast-pin",
                var s when s.Contains("asp.net core") => "cpu",
                var s when s.Contains("entity framework core") => "diagram-2-fill",
                var s when s.Contains("web api") || s.Contains("restful services") || s.Contains("restful apis") || s.Contains("rest") => "cloud-lightning",
                var s when s.Contains("grpc") => "arrow-repeat",
                var s when s.Contains("graphql") => "hub",
                var s when s.Contains("soap") => "envelope-paper",
                var s when s.Contains("microservices architecture") || s.Contains("microservices") => "boxes",
                var s when s.Contains("middleware") => "intersect",
                var s when s.Contains("routing") => "signpost-split",
                var s when s.Contains("razor pages") || s.Contains("razor") => "layers",
                var s when s.Contains("blazor") => "lightning-charge",
                var s when s.Contains("mvc") => "diagram-3",
                var s when s.Contains("web forms") => "window-stack",
                var s when s.Contains(".net core") || s.Contains(".net 5") || s.Contains(".net 6") || s.Contains(".net 7") || s.Contains(".net 10") => "box-seam",
                var s when s.Contains(".net framework") || s.Contains(".net") => "braces-asterisk",
                var s when s.Contains("asp.net") => "server",

                // --- Frontend & UI Libraries ---
                var s when s.Contains("angular 2.0") => "triangle-fill",
                var s when s.Contains("angularjs") => "hash",
                var s when s.Contains("angular") => "triangle",
                var s when s.Contains("react native") => "phone-vibrate",
                var s when s.Contains("react") => "activity",
                var s when s.Contains("vue.js") => "chevron-down",
                var s when s.Contains("ember.js") => "fire",
                var s when s.Contains("backbone.js") => "heart-pulse",
                var s when s.Contains("knockout.js") => "bookmark-star",
                var s when s.Contains("jquery") => "file-earmark-code",
                var s when s.Contains("kendo ui") => "grid-3x3-gap",
                var s when s.Contains("syncfusion") => "exclude",
                var s when s.Contains("telerik") => "gem",
                var s when s.Contains("devexpress") => "grid-1x2",
                var s when s.Contains("bootstrap") => "bootstrap",
                var s when s.Contains("tailwind css") => "wind",
                var s when s.Contains("material design") => "palette",
                var s when s.Contains("responsive ui") || s.Contains("responsive design") => "window-fullscreen",
                var s when s.Contains("component libraries") || s.Contains("ui frameworks") => "collection",
                var s when s.Contains("single page applications") || s.Contains("pwa") || s.Contains("progressive web apps") => "window-fullscreen",
                var s when s.Contains("webassembly") => "plugin",
                var s when s.Contains("accessibility") || s.Contains("wcag") || s.Contains("aria") => "universal-access",
                var s when s.Contains("seo") => "search",

                // --- Cloud (Azure, AWS, GCP) ---
                var s when s.Contains("azure functions") || s.Contains("serverless") => "lightning",
                var s when s.Contains("azure logic apps") => "diagram-2",
                var s when s.Contains("azure service bus") || s.Contains("message queues") || s.Contains("pub/sub") => "queue",
                var s when s.Contains("azure event grid") || s.Contains("event-driven") || s.Contains("streaming data") || s.Contains("real-time processing") => "envelope-open",
                var s when s.Contains("azure key vault") => "safe",
                var s when s.Contains("azure application insights") || s.Contains("azure monitor") || s.Contains("observability") || s.Contains("logging") || s.Contains("monitoring") || s.Contains("alerting") || s.Contains("tracing") => "graph-up",
                var s when s.Contains("azure blob storage") || s.Contains("azure storage") => "cloud-download",
                var s when s.Contains("azure active directory") => "shield-lock-fill",
                var s when s.Contains("azure api management") => "sliders",
                var s when s.Contains("azure cognitive services") => "brain",
                var s when s.Contains("azure kubernetes service") || s.Contains("kubernetes") => "vimeo",
                var s when s.Contains("azure container instances") || s.Contains("containerization") || s.Contains("container") || s.Contains("docker") => "box-seam-fill",
                var s when s.Contains("azure virtual machines") => "cpu-fill",
                var s when s.Contains("azure virtual network") || s.Contains("azure load balancer") || s.Contains("distributed systems") => "cloud-haze",
                var s when s.Contains("azure sql database") => "database-fill-check",
                var s when s.Contains("app services") || s.Contains("app service") => "cloud-haze2",
                var s when s.Contains("azure") => "cloud-arrow-up",
                var s when s.Contains("aws") => "cloud-sun",
                var s when s.Contains("google cloud") => "cloud-fill",
                var s when s.Contains("cloud computing") || s.Contains("edge computing") || s.Contains("fog computing") => "cloudy",

                // --- DevOps & Pipelines ---
                var s when s.Contains("yaml pipelines") => "terminal-split",
                var s when s.Contains("yaml") => "filetype-yml",
                var s when s.Contains("ci/cd") || s.Contains("continuous integration") || s.Contains("continuous deployment") || s.Contains("infrastructure as code") => "arrow-repeat",
                var s when s.Contains("devsecops") => "shield-shaded",
                var s when s.Contains("sre") || s.Contains("site reliability") => "heart-pulse-fill",
                var s when s.Contains("devops") => "infinity",
                var s when s.Contains("sccm") => "pc-display-horizontal",
                var s when s.Contains("iis") => "globe",

                // --- Identity & Security ---
                var s when s.Contains("openid connect") || s.Contains("openid") => "person-vcard",
                var s when s.Contains("oauth2") || s.Contains("oauth") => "person-check-fill",
                var s when s.Contains("jwt tokens") || s.Contains("jwt") => "pass",
                var s when s.Contains("saml2") || s.Contains("saml") => "file-earmark-lock",
                var s when s.Contains("sso integration") || s.Contains("sso") => "person-bounding-box",
                var s when s.Contains("identityserver") || s.Contains("asp.net identity") || s.Contains("identity management") => "person-badge-fill",
                var s when s.Contains("authentication and authorization") => "lock",
                var s when s.Contains("active directory") => "shield-check",
                var s when s.Contains("adfs") => "key",
                var s when s.Contains("ad") => "person-badge",
                var s when s.Contains("cryptography") => "shield-lock",
                var s when s.Contains("ldap") => "diagram-3-fill",

                // --- Source Control ---
                var s when s.Contains("github") => "github",
                var s when s.Contains("gitlab") => "envelope-heart",
                var s when s.Contains("git") => "git",
                var s when s.Contains("tfs") => "folder-symlink",

                // --- Testing & Tools ---
                var s when s.Contains("visual studio") => "code-slash",
                var s when s.Contains("vs code") => "file-code",
                var s when s.Contains("resharper") => "magic",
                var s when s.Contains("nuget") => "box",
                var s when s.Contains("nunit") => "check2-all",
                var s when s.Contains("xunit") => "check2",
                var s when s.Contains("mstest") || s.Contains("ms test") => "check2-circle",
                var s when s.Contains("selenium") => "eye",
                var s when s.Contains("postman") => "send",
                var s when s.Contains("swagger") => "journal-text",
                var s when s.Contains("fiddler") => "bug",
                var s when s.Contains("wireshark") => "reception-4",
                var s when s.Contains("jmeter") || s.Contains("load testing") || s.Contains("performance testing") => "speedometer",
                var s when s.Contains("code analysis") || s.Contains("static code analysis") || s.Contains("dynamic code analysis") || s.Contains("profiling") => "clipboard-pulse",
                var s when s.Contains("performance optimization") || s.Contains("performance tuning") || s.Contains("memory management") || s.Contains("garbage collection") => "lightning-charge-fill",

                // --- Compliance & Frameworks ---
                var s when s.Contains("security testing") || s.Contains("penetration testing") || s.Contains("vulnerability assessment") || s.Contains("owasp") => "shield-fill-exclamation",
                var s when s.Contains("cis benchmarks") || s.Contains("nist") || s.Contains("iso 27001") || s.Contains("soc 2") || s.Contains("hipaa") || s.Contains("gdpr") || s.Contains("pci dss") || s.Contains("fisma") || s.Contains("sox compliance") => "shield-lock-fill",
                var s when s.Contains("itil") || s.Contains("cobit") || s.Contains("iso 20000") || s.Contains("iso 22301") => "journal-check",
                var s when s.Contains("business continuity") || s.Contains("disaster recovery") || s.Contains("risk management") || s.Contains("incident response") => "shield-fill-plus",
                var s when s.Contains("change management") || s.Contains("configuration management") || s.Contains("release management") || s.Contains("problem management") => "gear-wide-connected",

                // --- Operating Systems ---
                var s when s.Contains("android") => "android2",
                var s when s.Contains("windows server") => "server",
                var s when s.Contains("windows") => "windows",
                var s when s.Contains("ubuntu") => "ubuntu",
                var s when s.Contains("fedora") => "app-indicator",
                var s when s.Contains("redhat") => "hat",
                var s when s.Contains("kali") => "incognito",
                var s when s.Contains("linux") => "terminal",
                var s when s.Contains("powershell") => "terminal-fill",

                // --- AI / ML / Emerging Tech ---
                var s when s.Contains("llama 3.2") || s.Contains("llama3.2") => "bata-llama",
                var s when s.Contains("ollama") => "egg-fried",
                var s when s.Contains("groq") => "cpu",
                var s when s.Contains("llm") || s.Contains("generative ai") || s.Contains("artificial intelligence") || s.Contains("machine learning") || s.Contains("deep learning") || s.Contains("natural language processing") || s.Contains("model training") => "robot",
                var s when s.Contains("ai vision") || s.Contains("computer vision") => "eye-fill",
                var s when s.Contains("blockchain") || s.Contains("cryptography") => "link-45deg",
                var s when s.Contains("quantum computing") => "mortorboard",
                var s when s.Contains("augmented reality") || s.Contains("virtual reality") || s.Contains("mixed reality") => "vr",
                var s when s.Contains("3d modeling") => "box",
                var s when s.Contains("iot") || s.Contains("arduino") => "cpu",
                var s when s.Contains("robotics") || s.Contains("automation") => "robot",

                // --- Project Management / Management ---
                var s when s.Contains("jira") => "kanban-fill",
                var s when s.Contains("kanban") => "columns-gap",
                var s when s.Contains("scrum") => "people",
                var s when s.Contains("agile methodologies") || s.Contains("agile") => "speedometer2",
                var s when s.Contains("lean") || s.Contains("xp") => "lightning",
                var s when s.Contains("sdlc") || s.Contains("waterfall") => "kanban",
                var s when s.Contains("practices") => "journal-code",

                // Grouped SmartSimple together with CRM Dynamics and general CRMs
                var s when s.Contains("crm dynamics") || s.Contains("smartsimple") || s.Contains("crm") => "graph-up-arrow",

                var s when s.Contains("business intelligence") || s.Contains("reporting") || s.Contains("dashboards") || s.Contains("ssrs") || s.Contains("ssis") || s.Contains("ssas") => "graph-up-arrow",
                var s when s.Contains("service level agreements") || s.Contains("key performance indicators") => "file-earmark-check",
                var s when s.Contains("code reviews") || s.Contains("pair programming") => "chat-left-text",
                var s when s.Contains("tdd") || s.Contains("bdd") => "patch-check",
                var s when s.Contains("solid principles") || s.Contains("clean code") || s.Contains("refactoring") => "gem",
                var s when s.Contains("game development") => "controller",
                var s when s.Contains("mobile development") || s.Contains("ios") || s.Contains("flutter") || s.Contains("xamarin") => "phone",

                // --- Default Global Fallback ---
                _ => "tag"
            };
        }
    }
}