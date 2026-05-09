namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using Microsoft.AspNetCore.Components;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.FileProviders;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    public class ChatbotService(HttpClient http, bool isDevelopment, string ollamaModel, ResumeService resumeService, IMemoryCache memoryCache)
    {
        private readonly string _ollamaModel = ollamaModel;
        private readonly ResumeService _resumeService = resumeService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private string? _cachedInstructions;
        private DateTime _lastReadTime = DateTime.MinValue;
        private readonly string _filePath = "AIInstructions.txt";

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessage> history)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            if (!_memoryCache.TryGetValue("#resumeText", out string _resumeText))
            {
                _resumeText = await _resumeService.GetResumeText();

                // Path to your resume file
                var fileInfo = new FileInfo("DavidTurner_Resume.docx");
                var fileProvider = new PhysicalFileProvider(fileInfo.DirectoryName!);

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .AddExpirationToken(fileProvider.Watch(fileInfo.Name)); // Evicts on file save

                _memoryCache.Set("#resumeText", _resumeText, cacheEntryOptions);
            }

            // 1. Handle Lazy Loading of Instructions
            var lastWrite = File.GetLastWriteTime(_filePath);
            if (_cachedInstructions == null || lastWrite > _lastReadTime)
            {
                _cachedInstructions = await File.ReadAllTextAsync(_filePath);
                _lastReadTime = lastWrite;
            }

            // Replace resumeContent placeholder
            var systemContent = _cachedInstructions.Contains("{resumeContent}")
                ? _cachedInstructions.Replace("{resumeContent}", _resumeText)
                : _cachedInstructions + "\n" + _resumeText;

            // Replace todaysDate without falling back to appending text at the end
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

            // 2. Use ResponseHeadersRead
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!isDevelopment && !response.IsSuccessStatusCode)
            {
                // This string will tell you EXACTLY which field Groq hates
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            // 3. Return the IAsyncEnumerable directly
            // Do NOT read the stream here. If you want to log, do it inside StreamResponse.
            if (isDevelopment)
                return StreamResponseOllama(response);
            else
                return StreamResponseGrok(response);
        }

        public MarkupString FormatMessage(string text, bool isComplete = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return new MarkupString("");

            // Step 1: Strip out unclosed HTML tags to prevent UI flashing
            text = Regex.Replace(text, @"<[^>]*$", "");

            // Step 1.1: If streaming, temporarily "close" bold tags so they render live
            if (!isComplete)
            {
                // If there's an odd number of '**', append one for rendering purposes only
                int boldOccurrences = Regex.Matches(text, @"\*\*").Count;
                if (boldOccurrences % 2 != 0)
                {
                    text += "**";
                }
            }

            // Step 2: Escape special HTML characters (Keep your original logic)
            var formatted = System.Net.WebUtility.HtmlEncode(text);

            // Step 3: Strip out any leaked XML tags
            formatted = formatted.Replace("&lt;contact_data&gt;", "").Replace("&lt;/contact_data&gt;", "");

            // Step 4: Repair smashed text formatting
            // 4.0. Directly fix escaped C# characters generated by the LLM
            formatted = formatted.Replace("C\\#", "C#").Replace("c\\#", "c#");

            // 4.1. Use a robust negative lookbehind to strictly protect C# from being parsed as a heading
            formatted = Regex.Replace(
                formatted,
                @"(?<![Cc]\s*)\s*#(?=\s)",
                "\n#"
            );

            // 4.2. Repair smashed jobs text and separate consecutive roles onto new lines
            formatted = Regex.Replace(formatted, @"\)\s*#", ")\n#");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*-\s*(?=[A-Z][a-z])", "\n-");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*(?=-\s*[A-Z])", "\n");

            // 4.3. Repair conversational bullet points smashed against punctuation AND parentheses
            formatted = Regex.Replace(formatted, @"(?<=[.:)])\s*([\*-])\s*", "\n$1 ");

            // 4.4. Sentence fixer: ignore line breaks and PROTECT .NET from being split
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-z])\s*([.!?])\s*(?=[A-Z])(?!NET)(?! [^\n]*\n)",
                "$1 "
            );

            // 4.6. Fix numbered lists and inline phone numbers smashed directly against sentence text
            formatted = Regex.Replace(formatted, @"(?<=[a-zA-Z.:)])\s*(\d+\.\s+)", "\n$1");

            // 4.7. Fix parenthetical text running together into consecutive listed lines
            formatted = Regex.Replace(formatted, @"(?=\))\s*(?=\d+\.)", "\n");

            // 4.8. Match individual numbered lines but completely ignore four-digit years and decimals
            formatted = Regex.Replace(
                formatted,
                @"^\s*(?!\d{4}\.)(?!\d+\.\d)(\d+\..+?)(?=\n|$)",
                "<div style=\"margin-bottom: 12px; margin-top: 4px;\">$1</div>",
                RegexOptions.Multiline
            );

            // A. Markdown Links [Text](URL)
            formatted = Regex.Replace(
                formatted,
                @"\[([^\]]+)\]\s*\(((?:https?://|/)[^)]*)\)", // Changed + to * to allow empty/single slash
                "<a href=\"$2\" target=\"_blank\" style=\"color: #64B5F6; text-decoration: underline; font-weight: 600;\">$1</a>",
                RegexOptions.IgnoreCase
            );

            // B. Raw URLs 
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

            // 4.9. Skip highlights if this text is the skills list
            var sortedKeywords = resumeService.GetTagList().Result.OrderByDescending(k => k.Length).ToList();

            foreach (var kw in sortedKeywords)
            {
                string escapedKw = Regex.Escape(kw);

                // Using the "Gold Standard" boundary fix from before
                string pattern = @"(?<!^#\s.*)(?<![a-zA-Z0-9])" + escapedKw + @"(?![a-zA-Z0-9])(?![^<]*>)";

                formatted = Regex.Replace(
                    formatted,
                    pattern,
                    $"<b style=\"color: #7DD3FC; font-weight: bold;\">{kw}</b>",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline // Multiline is key here!
                );
            }

            // 4.10. Highlight bold Markdown items in Ice Blue (#E0F2FE)
            formatted = Regex.Replace(
                formatted,
                @"\*\*(.*?)\*\*",
                "<span style=\"color: #E0F2FE; font-weight: bold;\">$1</span>"
            );

            // 4.11: Insert structural breaks for phone-only messages so they format like full contact info
            if (formatted.Contains("905-926-2398") && !formatted.Contains("David W. Turner"))
            {
                formatted = "David W. Turner\nOshawa, Ontario\n" + formatted;
            }

            // 4.12: Explicitly force summary text following colons to move to a new line
            formatted = Regex.Replace(
                formatted,
                @"(?<=\w):(?=[A-Z])",
                ":\n"
            );

            // 4.13: Ensure "Some of my key skills include:" sits exactly 2 lines below the preceding paragraph
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-zA-Z.:)])\s*(Some of my key skills include:)",
                "\n\n$1",
                RegexOptions.IgnoreCase
            );

            // 4.14: Push closing hooks down if smashed after a parenthesis OR a period
            formatted = Regex.Replace(
                formatted,
                @"(?<=[.)])\s*(Would you prefer to see my skills, experience, or contact info\?)",
                "\n\n\n$1",
                RegexOptions.IgnoreCase
            );

            // 4.15: Push "Here's a brief overview" onto a new line if smashed against previous paragraph words
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-zA-Z])\s*\.?\s*(Here's a brief overview of my experience:)",
                ".\n\n$1",
                RegexOptions.IgnoreCase
            );

            // Step 5: Handle the Heading Row -> White
            formatted = Regex.Replace(
                formatted,
                @"^#\s+(.+)$",
                "<div style=\"color: #FFFFFF; font-size: 1.05em; font-weight: bold; margin-top: 8px; margin-bottom: 4px;\">$1</div>",
                RegexOptions.Multiline
            );

            // FIX Step 6: Put the dash at the very end of the brackets to make it match exactly instead of forming a range
            formatted = Regex.Replace(
                formatted,
                @"^\s*[\*▪-]\s*(.+)$",
                "<div style=\"color: #E0F2FE; margin-left: 26px; margin-bottom: 6px; font-size: 0.95em; line-height: 1.4;\">▪ $1</div>",
                RegexOptions.Multiline
            );

            // Step 7: Convert all line endings to <br /> and preserve double spaces
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

            // Step 8: Remove any double breaks introduced around our custom divs
            formatted = Regex.Replace(formatted, @"(</div>)<br\s*/?>", "$1");
            formatted = Regex.Replace(formatted, @"<br\s*/?>(<div)", "$1");

            // Step 9: Final Wrapper
            // Ensure the font-size matches the wrapper in the Razor markup above
            var finalHtml = $"<div style=\"color: #F8FAFC; line-height: 1.6; font-size: 1.02em;\">{formatted.Trim()}</div>";

            return new MarkupString(finalHtml);
        }

        private static async IAsyncEnumerable<string?> StreamResponseOllama(HttpResponseMessage response)
        {
            // The 'using' here ensures the connection stays open until the loop is finished
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
