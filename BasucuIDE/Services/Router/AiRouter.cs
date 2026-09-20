// ============================================================
// Faz 1-B: AiRouter — Sağlayıcı Bağımsız Router (BYOM)
// ============================================================
// Bu sınıf artık herhangi bir OpenAI-uyumlu modelle çalışır.
// Kullanılacak model ve URL, AppSettings'ten (RouterModel, RouterBaseUrl)
// okunarak dışarıdan verilir. Gemini'ye veya başka bir sağlayıcıya
// bağımlılık yoktur.
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent
{
    /// <summary>
    /// Sağlayıcı bağımsız AI Router — herhangi bir OpenAI-uyumlu endpoint kullanır.
    /// Eski adı GeminiRouter'dı; artık tam BYOM (Bring Your Own Model) desteği var.
    /// </summary>
    public class AiRouter : IAiRouter
    {
        private static readonly TimeSpan RouterTimeout = TimeSpan.FromSeconds(90);
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        /// <param name="apiKey">Router için kullanılacak API anahtarı (AppSettings.RouterApiKey)</param>
        /// <param name="modelName">Router modeli — herhangi bir OpenAI-uyumlu model (AppSettings.RouterModel)</param>
        /// <param name="baseUrl">OpenAI-uyumlu endpoint (AppSettings.RouterBaseUrl)</param>
        public AiRouter(string apiKey, string modelName, string baseUrl)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
            _httpClient.Timeout = RouterTimeout;
        }


        public async Task<RouterDecision?> RouteAsync(string userTask, RouterContext context, CancellationToken cancellationToken = default)
        {
            bool isLocalEndpoint = !string.IsNullOrWhiteSpace(_baseUrl) &&
                (_baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                 _baseUrl.Contains("127.0.0.1") ||
                 _baseUrl.Contains("0.0.0.0"));

            if (string.IsNullOrWhiteSpace(_apiKey) && !isLocalEndpoint) return null;

            var toolDescriptions = new StringBuilder();
            foreach (var tool in context.AvailableTools)
            {
                if (tool.Function != null)
                {
                    var hint = !string.IsNullOrEmpty(tool.Function.RouterHint)
                        ? tool.Function.RouterHint
                        : tool.Function.Description?.Split('.')[0] ?? tool.Function.Name;
                    toolDescriptions.AppendLine($"- {tool.Function.Name}: {hint}");
                }
            }

            var systemPrompt = $@"You are an AI Tool Router for an autonomous coding agent.
Your job is to select the EXACT set of tools the agent will need to FULLY complete the user's task.

CRITICAL RULES:
1. If the task requires creating new features, writing code, or making a project, you MUST select file editing tools (like CreateOrUpdateFile, ReadFile).
2. If the task requires debugging, you MUST select tools for searching code and reading files.
3. If the user's message is just a simple greeting (e.g., 'merhaba', 'hello'), a polite response, or general conversation without requiring concrete actions, you MUST return an EMPTY tools array ([]). Do not select ANY tools for casual chat.
4. Set needsPlanning to true ONLY if the task is a complex coding feature requiring architectural steps.
5. If the user asks for choices, options, decision making, or step-by-step questions, you MUST include 'AskUserOptions' in the tools array.

Available Tools:
{toolDescriptions}

Return ONLY a valid JSON object with the following schema:
{{
  ""modelCategory"": ""coding"" | ""general"",
  ""tools"": [""tool_name_1"", ""tool_name_2""],
  ""needsPlanning"": true,
  ""confidence"": 0.95
}}
Do NOT output anything else (no markdown blocks, no explanations).";

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userTask }
                },
                response_format = new { type = "json_object" },
                temperature = 0.1
            };

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                }

                var url = $"{_baseUrl.TrimEnd('/')}/chat/completions";
                var response = await _httpClient.PostAsync(
                    url,
                    new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
                    cancellationToken);

                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    var firstError = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (firstError.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
                        firstError.Contains("json_object", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("[Router] response_format desteklenmedi; uyumluluk modu deneniyor.");
                        var compatibilityBody = new
                        {
                            model = _modelName,
                            messages = new[]
                            {
                                new { role = "system", content = systemPrompt },
                                new { role = "user", content = userTask }
                            },
                            temperature = 0.1
                        };
                        response = await _httpClient.PostAsync(
                            url,
                            new StringContent(JsonSerializer.Serialize(compatibilityBody), Encoding.UTF8, "application/json"),
                            cancellationToken);
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Router API Error] {response.StatusCode}: {err}");
                    return new RouterDecision { ErrorMessage = $"API Hatası ({response.StatusCode}): {err}" };
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseDoc = JsonDocument.Parse(responseString);

                if (responseDoc.RootElement.TryGetProperty("usage", out var usageProp))
                {
                    var promptTokens = usageProp.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
                    var completionTokens = usageProp.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
                    Console.WriteLine($"[Router Telemetry] Prompt: {promptTokens}, Completion: {completionTokens}");
                }

                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                    if (string.IsNullOrWhiteSpace(content)) 
                        return new RouterDecision { ErrorMessage = "API'den boş yanıt geldi." };

                    return TryParseRouterDecision(content);
                }

                return new RouterDecision { ErrorMessage = "API'den beklenen JSON formatı (choices) gelmedi." };
            }
            catch (TaskCanceledException)
            {
                var msg = $"[Router Timeout] Router isteği {_httpClient.Timeout.TotalSeconds}s içinde yanıt alamadı.";
                Console.WriteLine(msg);
                return new RouterDecision { ErrorMessage = msg };
            }
            catch (Exception ex)
            {
                var msg = $"[Router Exception] {ex.GetType().Name}: {ex.Message}";
                Console.WriteLine(msg);
                return new RouterDecision { ErrorMessage = msg };
            }
        }

        /// <summary>
        /// 3 aşamalı kurtarma stratejisi:
        /// 1. Direkt JSON deserialize (temiz çıktı)
        /// 2. Regex ile metin içindeki JSON bloğunu çek (açıklama metni karışmışsa)
        /// 3. Regex ile sadece "tools" dizisini çek (tamamen bozuk JSON ama araç listesi okunabilirse)
        /// </summary>
        private static RouterDecision? TryParseRouterDecision(string rawContent)
        {
            // Markdown bloklarını temizle
            var content = rawContent.Trim();
            if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                content = content.Substring(7);
            else if (content.StartsWith("```"))
                content = content.Substring(3);
            if (content.EndsWith("```"))
                content = content.Substring(0, content.Length - 3);
            content = content.Trim();

            // --- Strateji 1: Doğrudan parse ---
            var result = TryDeserialize(content);
            if (result != null) return result;

            // --- Strateji 2: Metin içinden JSON bloğunu çek ---
            // Model bazen "Here is the decision: {...}" gibi yazıyor
            var jsonMatch = Regex.Match(content, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (jsonMatch.Success)
            {
                result = TryDeserialize(jsonMatch.Value);
                if (result != null)
                {
                    Console.WriteLine("[Router] JSON gömülü metin içinden çıkarıldı.");
                    return result;
                }
            }

            // --- Strateji 3: Sadece "tools" dizisini regex ile çek ---
            var toolsMatch = Regex.Match(content, "\"tools\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (toolsMatch.Success)
            {
                var toolsRaw = toolsMatch.Groups[1].Value;
                var toolNames = Regex.Matches(toolsRaw, "\"([^\"]+)\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                var confMatch = Regex.Match(content, "\"confidence\"\\s*:\\s*([\\d.]+)");
                double confidence = confMatch.Success && double.TryParse(
                    confMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var conf) ? conf : 0.8;

                var planMatch = Regex.Match(content, "\"needsPlanning\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                bool needsPlanning = planMatch.Success &&
                    planMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                Console.WriteLine($"[Router] Kısmi kurtarma: {toolNames.Count} araç regex ile çıkarıldı.");
                return new RouterDecision
                {
                    Tools = toolNames,
                    Confidence = confidence,
                    NeedsPlanning = needsPlanning,
                    ModelCategory = "coding"
                };
            }

            // Tamamen başarısız — null döndür (ChatFlowService fallback kararını verir)
            Console.WriteLine($"[Router Parse Tamamen Başarısız] Ham içerik: {rawContent.Substring(0, Math.Min(200, rawContent.Length))}...");
            return null;
        }

        private static RouterDecision? TryDeserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<RouterDecision>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }
    }
}
