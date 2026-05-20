namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.FileProviders;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    public class ChatbotService(HttpClient http, bool isDevelopment, string ollamaModel, IMemoryCache memoryCache)
    {
        private readonly string _ollamaModel = ollamaModel;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly string _filePath = "AIInstructions.txt";

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessageRequest> history, string resumeText)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            var systemContent = await FormatResumeText(resumeText);

            // 3. Build the Message Collection (Single system message used here)
            var messages = new List<dynamic>
            {
                new { role = "system", content = systemContent }
            };

            var historyToProcess = isDevelopment ? history.TakeLast(10) : history.TakeLast(5);
            var historyList = historyToProcess.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();

            for (int i = 0; i < historyList.Count; i++)
            {
                var msg = historyList[i];

                if (i == historyList.Count - 1 && msg.IsUser && msg.Text.Trim().Equals(question.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                messages.Add(new { role = msg.IsUser ? "user" : "assistant", content = msg.Text });
            }

            // 5. Add the Current Question
            messages.Add(new { role = "user", content = question });

            object payload;

            if (isDevelopment)
            {
                //Ollama-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    messages,
                    stream = true,
                    options = new
                    {
                        num_ctx = 4096,
                        num_batch = 256,
                        temperature = 0.0,
                        repeat_penalty = 1.1,
                        num_predict = 250,
                        top_p = 0.9,
                        top_k = 40,
                        num_thread = 4
                    }
                };
            }
            else
            {
                var cleanMessages = messages.Select(m => new {
                    role = m.role,
                    content = m.content
                }).ToList();

                //Grok-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    messages = cleanMessages,
                    stream = true,
                    temperature = 0.0,
                    max_tokens = 250
                };
            }                   

            var request = new HttpRequestMessage(HttpMethod.Post, isDevelopment ? "api/chat" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!isDevelopment && !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            if (isDevelopment)
                return StreamResponseOllama(response);
            else
                return StreamResponseGrok(response);
        }

        public async Task<string> Generate(string question, string resumeText)
        {
            object payload;

            if (isDevelopment)
            {
                //Ollama-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    prompt = $"Given the data available in the DATA section, answer this question: {question}\r\n\r\n#DATA\r\n\r\n{await FormatResumeText(resumeText, false)}",
                    stream = false,
                    options = new
                    {
                        num_ctx = 4096,
                        num_batch = 256,
                        temperature = 0.0,
                        repeat_penalty = 1.1,
                        num_predict = 250,
                        top_p = 0.9,
                        top_k = 40,
                        num_thread = 4
                    }
                };
            }
            else
            {
                var messages = new List<dynamic>
                {
                    new { role = "system", content = $"#DATA\r\n\r\n{await FormatResumeText(resumeText, false)}" },
                    new { role = "user", content = $"Given the data available in the DATA section, answer this question: {question}" }
                };

                var cleanMessages = messages.Select(m => new
                {
                    role = m.role,
                    content = m.content
                }).ToList();

                //Grok-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    messages = cleanMessages,
                    stream = false,
                    temperature = 0.0,
                    max_tokens = 250
                };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, isDevelopment ? "api/generate" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!isDevelopment && !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            JsonObject? responseObject = JsonSerializer.Deserialize<JsonObject>(await response.Content.ReadAsStringAsync());
            string message = isDevelopment ? responseObject?["response"]?.ToString() ?? string.Empty 
                : responseObject?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;

            return message;
        }        

        public static async Task<FormattedText> FormatMessage(string text,   bool isComplete = true, string? specificKeyword = null, bool applyEnhancedKeywordStyling = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return new FormattedText("");

            var hasKeyword = false;

            text = Regex.Replace(text, @"<[^>]*$", "");

            if (!isComplete)
            {
                // If there's an odd number of '**', append one for rendering purposes only
                int boldOccurrences = Regex.Matches(text, @"\*\*").Count;
                if (boldOccurrences % 2 != 0)
                {
                    text += "**";
                }
            }

            var formatted = System.Net.WebUtility.HtmlEncode(text);

            // Strip out any leaked XML tags
            formatted = formatted.Replace("&lt;contact_data&gt;", "").Replace("&lt;/contact_data&gt;", "");

            // Repair smashed text formatting
            // Directly fix escaped C# characters generated by the LLM
            formatted = formatted.Replace("C\\#", "C#").Replace("c\\#", "c#");

            //Use a robust negative lookbehind to strictly protect C# from being parsed as a heading
            formatted = Regex.Replace(
                formatted,
                @"(?<![Cc]\s*)\s*#(?=\s)",
                "\n#"
            );

            // Repair smashed jobs text and separate consecutive roles onto new lines
            formatted = Regex.Replace(formatted, @"\)\s*#", ")\n#");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*-\s*(?=[A-Z][a-z])", "\n-");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*(?=-\s*[A-Z])", "\n");

            // Repair conversational bullet points smashed against punctuation AND parentheses
            formatted = Regex.Replace(formatted, @"(?<=[.:)])\s*([\*-])\s*", "\n$1 ");

            // Sentence fixer: ignore line breaks and PROTECT .NET from being split
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-z])\s*([.!?])\s*(?=[A-Z])(?!NET)(?! [^\n]*\n)",
                "$1 "
            );

            // Fix numbered lists and inline phone numbers smashed directly against sentence text
            formatted = Regex.Replace(formatted, @"(?<=[a-zA-Z.:)])\s*(\d+\.\s+)", "\n$1");

            // Fix parenthetical text running together into consecutive listed lines
            formatted = Regex.Replace(formatted, @"(?=\))\s*(?=\d+\.)", "\n");

            // Match individual numbered lines but completely ignore four-digit years and decimals
            formatted = Regex.Replace(
                formatted,
                @"^\s*(?!\d{4}\.)(?!\d+\.\d)(\d+\..+?)(?=\n|$)",
                "<div style=\"margin-bottom: 12px; margin-top: 4px;\">$1</div>",
                RegexOptions.Multiline
            );

            // Markdown Links [Text](URL)
            formatted = Regex.Replace(
                formatted,
                @"\[([^\]]+)\]\s*\(((?:https?://|/)[^)]*)\)", // Changed + to * to allow empty/single slash
                "<a href=\"$2\" target=\"_blank\" style=\"color: #64B5F6; text-decoration: underline; font-weight: 600;\">$1</a>",
                RegexOptions.IgnoreCase
            );

            // Raw URLs 
            formatted = Regex.Replace(
                formatted,
                @"(?<!href=\x22|href=\'|\[|<)https?://[^\s<\"" \)]+|(?<=\s|^)/(?![^<>]*>)[^\s<\"" \)]*",
                "<a href=\"$0\" target=\"_blank\" style=\"color: #64B5F6; text-decoration: underline; font-weight: 600;\">$0</a>",
                RegexOptions.IgnoreCase
            );

            formatted = Regex.Replace(
                formatted,
                @"\((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s\d{4}.*?\)",
                "<span style=\"color: #94A3B8; font-size: 0.9em; font-weight: normal;\">$0</span>"
            );

            // Skip highlights if this text is the skills list
            var sortedKeywords = (await ResumeService.GetTagList()).OrderByDescending(k => k.Length).ToList();

            if (specificKeyword != null)
            {
                sortedKeywords = [.. sortedKeywords.Where(k => k.Equals(specificKeyword, StringComparison.OrdinalIgnoreCase))];
            }

            foreach (var kw in sortedKeywords)
            {
                if (!hasKeyword && formatted.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    hasKeyword = true;

                string escapedKw = Regex.Escape(kw);

                string pattern = @"(?<!^#\s.*)(?<![a-zA-Z0-9])" + escapedKw + @"(?![a-zA-Z0-9])(?![^<]*>)";

                formatted = Regex.Replace(
                    formatted,
                    pattern,
                    $"<b {$"{(applyEnhancedKeywordStyling ? "class=\"keyword-highlight\"" : "style=\"color: #7DD3FC; font-weight: bold;\"")}"}>{kw}</b>",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );                
            }

            // Highlight bold Markdown items in Ice Blue (#E0F2FE)
            formatted = Regex.Replace(
                formatted,
                @"\*\*(.*?)\*\*",
                "<span style=\"color: #E0F2FE; font-weight: bold;\">$1</span>"
            );

            // Insert structural breaks for phone-only messages so they format like full contact info
            if (formatted.Contains("905-926-2398") && !formatted.Contains("David W. Turner"))
            {
                formatted = "David W. Turner\nOshawa, Ontario\n" + formatted;
            }

            // Explicitly force summary text following colons to move to a new line
            formatted = Regex.Replace(
                formatted,
                @"(?<=\w):(?=[A-Z])",
                ":\n"
            );

            // Ensure "Some of my key skills include:" sits exactly 2 lines below the preceding paragraph
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-zA-Z.:)])\s*(Some of my key skills include:)",
                "\n\n$1",
                RegexOptions.IgnoreCase
            );

            // Push closing hooks down if smashed after a parenthesis OR a period
            formatted = Regex.Replace(
                formatted,
                @"(?<=[.)])\s*(Would you prefer to see my skills, experience, or contact info\?)",
                "\n\n\n$1",
                RegexOptions.IgnoreCase
            );

            // Push "Here's a brief overview" onto a new line if smashed against previous paragraph words
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-zA-Z])\s*\.?\s*(Here's a brief overview of my experience:)",
                ".\n\n$1",
                RegexOptions.IgnoreCase
            );

            // Handle the Heading Row -> White
            formatted = Regex.Replace(
                formatted,
                @"^#\s+(.+)$",
                "<div style=\"color: #FFFFFF; font-size: 1.05em; font-weight: bold; margin-top: 8px; margin-bottom: 4px;\">$1</div>",
                RegexOptions.Multiline
            );

            // Put the dash at the very end of the brackets to make it match exactly instead of forming a range
            formatted = Regex.Replace(
                formatted,
                @"^\s*[\*▪-]\s*(.+)$",
                "<div style=\"color: #E0F2FE; margin-left: 26px; margin-bottom: 6px; font-size: 0.95em; line-height: 1.4;\">▪ $1</div>",
                RegexOptions.Multiline
            );

            // Convert all line endings to <br /> and preserve double spaces
            formatted = formatted.Replace("\r\n", "\n").Replace("\r", "\n");
            formatted = formatted.Replace("\n\n", "<div style=\"height: 18px;\"></div>");
            formatted = formatted.Replace("\n", "<br />");

            formatted = Regex.Replace(
                formatted,
                @"^\s*-\s+(.+)$",
                @"<div style=""display: flex; gap: 8px; margin-left: 15px; margin-bottom: 6px; line-height: 1.4; color: #CBD5E1;"">
                    <span style=""color: #7DD3FC;"">•</span>
                    <span>$1</span>
                </div>",
                RegexOptions.Multiline
            );

            // Remove any double breaks introduced around our custom divs
            formatted = Regex.Replace(formatted, @"(</div>)<br\s*/?>", "$1");
            formatted = Regex.Replace(formatted, @"<br\s*/?>(<div)", "$1");

            // Final Wrapper
            // Ensure the font-size matches the wrapper in the Razor markup above
            var finalHtml = $"<div style=\"color: #F8FAFC; line-height: 1.6; font-size: 1.02em;\">{formatted.Trim()}</div>";

            return new FormattedText(finalHtml, hasKeyword);
        }

        private static async IAsyncEnumerable<string?> StreamResponseOllama(HttpResponseMessage response)
        {
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // Optional: Log to console here if you need to see it on the server
                        // Console.WriteLine($"DEBUG: {line}");

                        var chunk = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(line);
                        if (chunk?.message?.content != null)
                        {
                            yield return chunk.message.content;
                        }
                    }
                }
            }
        }

        private static async IAsyncEnumerable<string?> StreamResponseGrok(HttpResponseMessage response)
        {
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    // Groq streams start with "data: " and end with "data: [DONE]"
                    if (line.StartsWith("data: ") && !line.Contains("[DONE]"))
                    {
                        var json = line.Substring(6); // Strip "data: " prefix
                        var chunk = JsonSerializer.Deserialize<GroqResponse>(json);

                        var content = chunk?.choices?[0]?.delta?.content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            yield return content;
                        }
                    }
                }
            }
        }

        private async Task<string?> FormatResumeText(string resumeText, bool includeInstructions = true)
        {
            if (!_memoryCache.TryGetValue("#aiInstructions", out string? _aiInstructions))
            {
                _aiInstructions = await File.ReadAllTextAsync(_filePath);

                var fileInfo = new FileInfo("DavidTurner_Resume.docx");
                var fileProvider = new PhysicalFileProvider(fileInfo.DirectoryName!);

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .AddExpirationToken(fileProvider.Watch(fileInfo.Name)); // Evicts on file save

                _memoryCache.Set("#aiInstructions", _aiInstructions, cacheEntryOptions);
            }

            if (includeInstructions && !string.IsNullOrEmpty(_aiInstructions))
            {
                var systemContent = _aiInstructions.Contains("{resumeContent}")
                    ? _aiInstructions.Replace("{resumeContent}", resumeText)
                    : _aiInstructions + "\n" + resumeText;

                systemContent = systemContent.Contains("{todaysDate}")
                    ? systemContent.Replace("{todaysDate}", DateTime.Now.ToString("MMMM dd, yyyy"))
                    : systemContent;

                // Inject dynamic date rules
                var start = new DateTime(2026, 4, 29);
                var today = DateTime.Now;

                // Calculate years, months, and days exactly
                int years = today.Year - start.Year;
                int months = today.Month - start.Month;
                int days = today.Day - start.Day;

                if (days < 0)
                {
                    // Borrow days from the previous month
                    var previousMonth = today.AddMonths(-1);
                    days += DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);
                    months--;
                }

                if (months < 0)
                {
                    // Borrow months from the previous year
                    months += 12;
                    years--;
                }

                // Build the readable string
                var parts = new List<string>();
                if (years > 0) parts.Add($"{years} {(years == 1 ? "year" : "years")}");
                if (months > 0) parts.Add($"{months} {(months == 1 ? "month" : "months")}");
                if (days > 0) parts.Add($"{days} {(days == 1 ? "day" : "days")}");

                // Fallback if today is exactly the start date
                string durationText = parts.Count > 0 ? string.Join(", ", parts) : "0 days";

                string dynamicRule = $"IF {{todaysDate}} is {today:MMMM dd, yyyy}: Duration is {durationText}";

                systemContent = systemContent.Contains("{dateLogic}")
                    ? systemContent.Replace("{dateLogic}", dynamicRule)
                    : systemContent;

                return systemContent;
            }

            return resumeText;
        }

        public class GroqResponse
        {
            public List<Choice> choices { get; set; }
            public class Choice { public Delta delta { get; set; } }
            public class Delta { public string content { get; set; } }
        }

        private class OllamaResponse
        {
            public ChatMessagePart message { get; set; }
        }

        private class ChatMessagePart
        {
            public string content { get; set; }
        }
    }
}
