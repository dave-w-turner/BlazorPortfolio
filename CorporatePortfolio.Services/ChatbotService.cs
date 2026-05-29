namespace CorporatePortfolio.Services
{
    using CorporatePortfolio.Services.DTO;
    using Microsoft.Extensions.Caching.Memory;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    public class ChatbotService
    {
        private readonly HttpClient _http;
        private readonly bool _isDevelopment;
        private readonly string _ollamaModel;
        private readonly IMemoryCache _memoryCache;
        private readonly string _filePath;
        private readonly FileSystemWatcher? _fileWatcher;

        public ChatbotService(HttpClient http, bool isDevelopment, string ollamaModel, IMemoryCache memoryCache, string rulesPath)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _isDevelopment = isDevelopment;
            _ollamaModel = ollamaModel;
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _filePath = Path.GetFullPath(rulesPath ?? throw new ArgumentNullException(nameof(rulesPath)));

            if (_isDevelopment)
            {
                string? directory = Path.GetDirectoryName(_filePath);
                string fileName = Path.GetFileName(_filePath);

                if (!string.IsNullOrEmpty(directory) && File.Exists(_filePath))
                {
                    _fileWatcher = new FileSystemWatcher(directory, fileName)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    _fileWatcher.Changed += (s, e) => _memoryCache.Remove("#aiInstructions");
                    _fileWatcher.Renamed += (s, e) => _memoryCache.Remove("#aiInstructions");
                }
            }
        }

        public async Task<IAsyncEnumerable<string?>> Ask(string question, List<ChatMessageRequest> history, string resumeText)
        {
            if (string.IsNullOrEmpty(_ollamaModel)) throw new Exception("Ollama model is not specified.");

            var systemContent = await FormatResumeText(resumeText);

            var messages = new List<dynamic>
            {
                new { role = "system", content = systemContent }
            };

            // Keep history processing tight to prevent attention decay on small models
            int historyCountToTake = 4;
            var historyToProcess = history.TakeLast(historyCountToTake);
            var historyList = historyToProcess.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();

            // Track the last appended role type to ensure the array strictly alternates: user -> assistant -> user
            string lastAppendedRole = "system";

            for (int i = 0; i < historyList.Count; i++)
            {
                var msg = historyList[i];
                string currentRole = msg.IsUser ? "user" : "assistant";

                if (msg.Text.Trim().Contains(question.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (currentRole == "user" && lastAppendedRole == "user")
                    continue;

                messages.Add(new { role = currentRole, content = msg.Text.Trim() });
                lastAppendedRole = currentRole;
            }

            string cleanCurrentQuestion = question.Trim();
            messages.Add(new { role = "user", content = cleanCurrentQuestion });

            object payload;

            if (_isDevelopment)
            {
                payload = new
                {
                    model = _ollamaModel,
                    messages,
                    stream = true,
                    options = new
                    {
                        num_ctx = 8192,
                        num_batch = 512,
                        presence_penalty = 0.0, // Set to 0 to prevent the model from fleeing your raw datasets
                        temperature = 0.1,      // CRITICAL: Dropped to near 0 to kill random creativity and enforce rules
                        repeat_penalty = 1.1,
                        repeat_last_n = 64,
                        num_predict = 1500,
                        top_p = 0.1,            // CRITICAL: Restricts the token pool to ONLY highly accurate choices
                        top_k = 10,             // CRITICAL: Shrinks the word list so it cannot choose rogue paths
                        num_thread = 4
                    }
                };
            }
            else
            {
                var cleanMessages = messages.Select(m => new {
                    role = (string)m.role,
                    content = (string)m.content
                }).ToList();

                payload = new
                {
                    model = _ollamaModel,
                    messages = cleanMessages,
                    stream = true,
                    temperature = 0.0,
                    max_tokens = 1500
                };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, _isDevelopment ? "api/chat" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!_isDevelopment && !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            if (_isDevelopment)
                return StreamResponseOllama(response);
            else
                return StreamResponseGrok(response);
        }


        public async Task<string> Generate(string question, string resumeText = "")
        {
            object payload;

            if (_isDevelopment)
            {
                //Ollama-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    prompt = !string.IsNullOrEmpty(resumeText) ?
                        $"Given the data available in the DATA section, answer this question: {question}\r\n\r\n#DATA\r\n\r\n{await FormatResumeText(resumeText, false)}" :
                        question,
                    stream = false,
                    options = new
                    {
                        num_ctx = 8192,
                        num_batch = 512,
                        presence_penalty = 0.2, // Boosted slightly to discourage repeating ideas
                        temperature = 0.75,     // Set between 0.7 and 0.8 for normal human conversation
                        repeat_penalty = 1.2,
                        repeat_last_n = 128,
                        num_predict = 1500,
                        top_p = 0.9,            // Opened up to 90% of the plausible word pool
                        top_k = 40,             // Opened up to look at the top 40 best words
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
                    max_tokens = 1500
                };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, _isDevelopment ? "api/generate" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!_isDevelopment && !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            JsonObject? responseObject = JsonSerializer.Deserialize<JsonObject>(await response.Content.ReadAsStringAsync());
            string message = _isDevelopment ? responseObject?["response"]?.ToString() ?? string.Empty 
                : responseObject?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;

            return message;
        }        

        public async Task<string> GenerateVerbalSummary(string unformattedResponse)
        {
            var systemContent = "You are David Turner, a seasoned software engineer speaking naturally in a casual conversation about your professional background. Respond in FIRST PERSON.\n\n" +
                    "CRITICAL CONSTRUCTIONS:\n" +
                    "1. VOICE STYLE: Speak like an actual human developer talking to a colleague. Use natural transitions. Avoid stiff corporate vocabulary or resume jargon.\n" +
                    "2. STRICT LENGTH LIMIT: Create exactly ONE cohesive, flowing paragraph of 2 to 3 short sentences. Keep the total count strictly under 45 words.\n" +
                    "3. NO filler intros or structural pre-ambles (Do NOT say 'Here is a brief summary...', 'Sure thing...', or 'Based on my resume...'). Start speaking your actual thoughts instantly.\n" +
                    "4. NO markdown symbols, asterisks, bullet indicators, or lists. Output purely raw, unformatted conversational speech text.\n\n" +
                    "CONVERSATIONAL TASK:\n" +
                    "Review the technical content below and summarize the highest-level theme of your engineering expertise in a punchy, confident tone. Focus on what you love building or your core strength, rather than rattling off an alphabetical list of tools or acronyms.\n\n" +
                    "Resume Data:\n" +
                    unformattedResponse;

            object payload;

            if (_isDevelopment)
            {
                //Ollama-specific payload structure
                payload = new
                {
                    model = _ollamaModel,
                    prompt = systemContent,
                    stream = false,
                    options = new
                    {
                        num_ctx = 8192,
                        num_batch = 512,
                        presence_penalty = 0.0,
                        temperature = 0.0,
                        repeat_penalty = 1.2,
                        repeat_last_n = 128,
                        num_predict = 1500,
                        top_p = 0.01,
                        top_k = 1,
                        num_thread = 4
                    }
                };
            }
            else
            {
                var messages = new List<dynamic>
                {
                    new { role = "user", content = systemContent }
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
                    max_tokens = 1500
                };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, _isDevelopment ? "api/generate" : "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!_isDevelopment && !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq Error: {errorBody}");
            }
            else
                response.EnsureSuccessStatusCode();

            JsonObject? responseObject = JsonSerializer.Deserialize<JsonObject>(await response.Content.ReadAsStringAsync());
            string message = _isDevelopment ? responseObject?["response"]?.ToString() ?? string.Empty
                : responseObject?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;

            return message;
        }

        public static async Task<FormattedText> FormatMessage(string text, bool isComplete = true, string? specificKeyword = null, bool applyEnhancedKeywordStyling = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return new FormattedText("");

            var hasKeyword = false;

            text = Regex.Replace(text, @"<[^>]*\Z", "");

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

            formatted = Regex.Replace(
                formatted,
                @"<h2[^>]*>(Lifemark Health Group – Toronto, ON)<\/h2>",
                "<div style=\"font-size: 1rem; font-weight: 600; color: #E2E8F0; margin-top: 14px; margin-bottom: 4px;\">$1</div>"
            );

            // UNIVERSAL DATA BOUNDARY CLEANSER
            // Forcefully catches any raw XML opening/closing tags or layout data fragments 
            // generated by the LLM stream and surgically deletes them before text rendering runs.
            formatted = Regex.Replace(formatted, @"<\/?(?:experience|summary|details|mappings|competencies|contact|raw|projects)[^>]*>", "").Trim();

            formatted = Regex.Replace(formatted, @"\)\s*(?=[A-Z])", ")\n");

            // Repair smashed text formatting
            // Directly fix escaped C# characters generated by the LLM
            formatted = formatted.Replace("C\\#", "C#").Replace("c\\#", "c#");

            //Use a robust negative lookbehind to strictly protect C# from being parsed as a heading
            formatted = Regex.Replace(
                formatted,
                @"(?<![Cc]\s*)\s*(?:#|&amp;#35;)(?=\s)",
                "\n#"
            );

            formatted = Regex.Replace(formatted, @"\)\s*(?:#|&amp;#35;)", ")\n#");
            formatted = Regex.Replace(formatted, @"\s{2,}(?=(?:#|&amp;#35;)\s+[A-Za-z])", "\n\n");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*-\s*(?=[A-Z][a-z])", "\n-");
            formatted = Regex.Replace(formatted, @"(?<=\))\s*(?=-\s*[A-Z])", "\n");

            // Repair conversational bullet points smashed against punctuation AND parentheses
            formatted = Regex.Replace(formatted, @"(?<=[.:)])\s*([\*-])\s*", "\n$1 ");

            // Sentence fixer: ignore line breaks and PROTECT .NET from being split across lines
            formatted = Regex.Replace(
                formatted,
                @"(?<=[a-zA-Z])\s*([.!?])\s*(?=[A-Z])(?!NET)(?! [^\n]*\n)",
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

            formatted = Regex.Replace(
                formatted,
                @"(?:\[|&#91;|&amp;#91;)([^\]&]+)(?:\]|&#93;|&amp;#93;)\s*(?:\(|&#40;|&amp;#40;)((?:https?://|/)[^)&]*)(?:\)|&#41;|&amp;#41;)",
                "<a href=\"$2\" target=\"_blank\" style=\"color: #64B5F6; text-decoration: underline; font-weight: 600;\">$1</a>",
                RegexOptions.IgnoreCase
            );

            // Raw URLs 
            formatted = Regex.Replace(
                formatted,
                @"(?<!href=\x22|href=\x27|\[|<|>\s*)_https?://[^\s<\x22\x27)]+",
                "<a href=\"$0\" target=\"_blank\" style=\"color: #64B5F6; text-decoration: underline; font-weight: 600;\">$0</a>",
                RegexOptions.IgnoreCase
            );

            formatted = Regex.Replace(
                formatted,
                @"\((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s\d{4}.*?\)",
                "<span style=\"color: #94A3B8; font-size: 0.9em; font-weight: normal;\">$0</span>"
            );

            formatted = Regex.Replace(
                formatted,
                @"(?<=\))\s{1,2}(?=[A-Z][a-z])(?!.*?\))",
                "<br /><br /><div style=\"margin-top: 10px; color: #CBD5E1;\">"
            );

            // If the trailer tag was opened, ensure we append a closing div marker
            if (formatted.Contains("<div style=\"margin-top: 10px;"))
            {
                formatted += "</div>";
            }

            // Highlight bold Markdown items in Ice Blue (#E0F2FE)
            formatted = Regex.Replace(
                formatted,
                @"\*\*(.*?)\*\*",
                "<span style=\"color: #E0F2FE; font-weight: bold;\">$1</span>"
            );

            // Explicitly force summary text following colons to move to a new line
            formatted = Regex.Replace(
                formatted,
                @"(?<=\w):(?:\s*[\r\n]{2,}\s*(?=[A-Z])|\s*(?=[A-Z]))",
                ":\n\n"
            );

            // Use a robust negative lookbehind to strictly protect C# from being parsed as a heading
            formatted = Regex.Replace(formatted, @"(?<![Cc]\s*)\s*#(?=\s)", "\n#");


            // Handle the Heading Row -> White
            formatted = Regex.Replace(
                formatted,
                @"^\s*(?:#|&amp;#35;|&#35;)\s*(.+)$",
                "<div style=\"color: #FFFFFF; font-size: 1.05em; font-weight: bold; margin-top: 14px; margin-bottom: 4px;\">$1</div>",
                RegexOptions.Multiline
            );

            // CALENDAR DATE MONTH/YEAR HEALER
            // Instantly merges month names and years separated by breaks before <br /> conversions run!
            formatted = Regex.Replace(
                formatted,
                @"(?<=\b(?:January|February|March|April|May|June|July|August|September|October|November|December))\s*[\r\n\t]+\s*(?=\b\d{4}\b)",
                " ",
                RegexOptions.IgnoreCase
            );

            // Repair consecutive line entries and normalize whitespace trees
            formatted = Regex.Replace(formatted, @"\s{2,}(?=-\s+[A-Za-z])", "\n");
            formatted = Regex.Replace(formatted, @"\s{2,}(?=#\s+[A-Za-z])", "\n\n");

            // UNIVERSAL CONVERSATIONAL TEXT DOWN-SHIFT (FOR BULLETS & SUMMARIES)
            // Pattern A: Look behind for a closing </span> tag (Handles high-level overview lists)
            // Pattern B: Look behind for a standard sentence completion period followed by spaces and a known chat phrase
            // This cleanly isolates ANY dynamic phrase the LLM creates during deep-dive lookups!
            formatted = Regex.Replace(
                formatted,
                @"(?<=<\/span>)\s{2,}(?=[A-Z][a-z])|(?<=\.)\s{2,}(?=(?:[Ii]f you'd like|[Ii]f you would like|[Ff]eel free to ask|[Ww]ould you like|[Ii]'m happy to provide))",
                "\n\n\n"
            ); // Cleanly removed RegexOptions.IgnoreCase

            // BULLET CONVERSION MATRIX (SUPPORTS BOTH HYPHENS AND ASTERISKS)
            formatted = Regex.Replace(
              formatted,
              @"^[ \t]*[\-\*▪]\s*(?!(?:#|&amp;#35;))(\S.+?)(?=\s{2,}|$)",
              "<div style=\"color: #E0F2FE; margin-left: 26px; margin-bottom: 6px; font-size: 0.95em; line-height: 1.4;\">▪ $1</div>",
              RegexOptions.Multiline
            );

            formatted = formatted.Replace("\r\n", "\n").Replace("\r", "\n");
            formatted = formatted.Replace("\n\n", "<div style=\"height: 18px;\"></div>");
            formatted = Regex.Replace(
                formatted,
                @"(?<=<div style=\x22height:\s*18px;\x22><\/div>)([^<]+(?:<br\s*/?>[^<]+)*)",
                "<div style=\"padding-left: 15px; color: #CBD5E1; line-height: 1.5;\">$1</div>"
            );

            formatted = formatted.Replace("\n", "<br />");

            formatted = Regex.Replace(formatted, @"(</div>)<br\s*/?>", "$1");
            formatted = Regex.Replace(formatted, @"<br\s*/?>(<div)", "$1");

            formatted = Regex.Replace(
                formatted,
                @"^[ \t]*[\-\*]\s+(.+)$",
                @"<div style=""display: flex; gap: 8px; margin-left: 15px; margin-bottom: 6px; line-height: 1.4; color: #CBD5E1;""><span style=""color: #7DD3FC;"">•</span><span>$1</span></div>",
                RegexOptions.Multiline
            );
            
            var sortedKeywords = (await ResumeService.GetTagList())
                .OrderByDescending(k => k.Length)
                .ToList();

            if (specificKeyword != null)
            {
                sortedKeywords = [.. sortedKeywords.Where(k => k.Equals(specificKeyword, StringComparison.OrdinalIgnoreCase))];
            }

            foreach (var kw in sortedKeywords)
            {
                if (!hasKeyword && formatted.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    hasKeyword = true;

                string escapedKw = Regex.Escape(kw);

                string pattern = @"(?<!<div style=[^>]*font-weight:\s*bold[^>]*>[^<]*)" +
                                 @"(?<![Cc]\s*)(?<!^#\s.*)(?<!https?:\/\/\S*)(?<!www\.\S*)(?<![a-zA-Z0-9])" +
                                 escapedKw +
                                 @"(?![a-zA-Z0-9])(?![^<]*>)";

                formatted = Regex.Replace(
                    formatted,
                    pattern,
                    $"<b style=\"color: #7DD3FC; font-weight: bold;\">$0</b>",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
            }

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
                        var chunk = JsonSerializer.Deserialize<OllamaResponse>(line);
                        if (chunk?.message?.content != null)
                        {
                            string textChunk = chunk.message.content;

                            if (textChunk.Contains("<!-- STOP -->"))
                            {
                                yield break; 
                            }

                            yield return textChunk;
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
            if (includeInstructions)
            {
                if (!_memoryCache.TryGetValue("#aiInstructions", out string? _aiInstructions))
                {
                    _aiInstructions = await File.ReadAllTextAsync(_filePath);
                    _memoryCache.Set("#aiInstructions", _aiInstructions);
                }

                if (!string.IsNullOrEmpty(_aiInstructions))
                {
                    var systemContent = _aiInstructions.Contains("{resumeContent}")
                        ? _aiInstructions.Replace("{resumeContent}", resumeText)
                        : _aiInstructions + "\n" + resumeText;

                    return systemContent;
                }
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
