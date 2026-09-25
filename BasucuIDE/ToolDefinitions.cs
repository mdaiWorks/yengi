using System.Text.Json.Serialization;
using System.Text;

namespace mdaiAgent;

// Araç şeması için sınıfları
public class ToolParameter
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();
}

public class ToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public ToolParameter Parameters { get; set; } = new();

    /// <summary>
    /// Router için kısa özet (max 8 kelime). Ana model tam Description'ı alır.
    /// </summary>
    [JsonIgnore]
    public string RouterHint { get; set; } = "";
}

public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolFunction Function { get; set; } = new();
}

// Araç çağrısı için yanıt sınıfları
public class GoogleExtraContent
{
    [JsonPropertyName("thought_signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThoughtSignature { get; set; }
}

public class ExtraContent
{
    [JsonPropertyName("google")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GoogleExtraContent? Google { get; set; }
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionCall Function { get; set; } = new();

    // Gemini'nin thought_signature değeri her tool_call içinde gelir
    [JsonPropertyName("extra_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtraContent? ExtraContent { get; set; }
}

public class Attachment
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    // Base64 encoded content (useful for small files like images, txt, pdf)
    [JsonPropertyName("base64Content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Base64Content { get; set; }

    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; set; }

    // Optional local path (not sent to remote API by default, but useful locally)
    [JsonPropertyName("localPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalPath { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}

public class FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";
}

// API yanıtını genişletilmiş hali (asistan ve araç mesajları için)
public class ExtendedChatMessage : ChatMessage
{
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("attachments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Attachment>? Attachments { get; set; }
}

public class ExtendedChatResponseChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ExtendedChatMessage? Message { get; set; }
}

public class ChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class ExtendedChatResponse
{
    [JsonPropertyName("choices")]
    public List<ExtendedChatResponseChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
    
    [JsonIgnore]
    public long TtftMs { get; set; }
    
    [JsonIgnore]
    public long TotalLatencyMs { get; set; }
}

public static class ToolRegistry
{
    public static List<ToolDefinition> GetTools()
    {
        return new List<ToolDefinition>
        {
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "GenerateImage",
                    RouterHint = "Metin açıklamalarından görsel üretir",
                    Description = "Kullanıcının metin açıklamasına dayanarak AI kullanarak yeni bir görsel üretir. Üretilen görsel projeye veya masaüstüne kaydedilir.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["prompt"] = new { type = "string", description = "Üretilecek görselin İngilizce detaylı betimlemesi." },
                            ["width"] = new { type = "integer", description = "(Opsiyonel) Görsel genişliği (örn: 1024)" },
                            ["height"] = new { type = "integer", description = "(Opsiyonel) Görsel yüksekliği (örn: 1024)" }
                        },
                        Required = new List<string> { "prompt" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ReadFile",
                    RouterHint = "Dosya içeriğini okur",
                    Description = "Belirtilen dosyanın içeriğini okur. Proje dışı bir yol verilirse kullanıcıdan oturumluk klasör erişim izni ister. Büyük dosyalarda token tasarrufu için startLine/endLine ile belirli satır aralığı okunabilir.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["filePath"]  = new { type = "string",  description = "Okunacak dosyanın tam yolu" },
                            ["startLine"] = new { type = "integer", description = "(Opsiyonel) Okunmaya başlanacak satır numarası (1-indexed). Belirtilmezse dosyanın başından okunur." },
                            ["endLine"]   = new { type = "integer", description = "(Opsiyonel) Okumanın biteceği satır numarası (1-indexed, dahil). Belirtilmezse dosyanın sonuna kadar okunur." }
                        },
                        Required = new List<string> { "filePath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "CreateOrUpdateFile",
                    RouterHint = "Yeni dosya yazar veya günceller",
                    Description = "Yeni bir dosya oluşturur veya var olanı günceller. Proje dışı bir yol verilirse kullanıcıdan oturumluk klasör erişim izni ister; değişiklik ayrıca onaylanır.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["filePath"] = new { type = "string", description = "Dosyanın tam yolu" },
                            ["content"] = new { type = "string", description = "Dosyaya yazılacak içerik" }
                        },
                        Required = new List<string> { "filePath", "content" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ReplaceFileContent",
                    RouterHint = "Dosyanın belirli bölümünü değiştirir",
                    Description = "Bir dosyanın belirli bir bölümünü değiştirir. Proje dışı bir yol verilirse kullanıcıdan oturumluk klasör erişim izni ister; değişiklik ayrıca onaylanır. Dosyanın tamamını yeniden yazmak yerine, değiştirilmesi gereken spesifik kod bloğu ile yeni içeriği belirtin. Çok büyük dosyalarda kısmi güncelleme için tercih edilmelidir.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["filePath"] = new { type = "string", description = "Değiştirilecek dosyanın tam yolu" },
                            ["targetContent"] = new { type = "string", description = "Dosyada değiştirilmek istenen mevcut kod bloğu. Dosyadan tam olarak kopyalanmış olmalıdır (whitespace ve satır sonları dahil)" },
                            ["replacementContent"] = new { type = "string", description = "Eski kodun yerine geçecek yeni kod bloğu" }
                        },
                        Required = new List<string> { "filePath", "targetContent", "replacementContent" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ExecuteTerminalCommand",
                    RouterHint = "Terminal komutu çalıştırır",
                    Description = "Terminalde bir komut çalıştırır. İsteğe bağlı olarak farklı bir klasörde çalıştırmak için workingDirectory parametresi kullanılabilir.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["command"] = new { type = "string", description = "Çalıştırılacak terminal komutu" },
                            ["workingDirectory"] = new { type = "string", description = "Komutun çalıştırılacağı klasör yolu (opsiyonel). Belirtilmezse projenin kök klasörü kullanılır. Örnek: 'C:/Projects/MyApp'" }
                        },
                        Required = new List<string> { "command" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ReadToolOutput",
                    RouterHint = "Önceki çıktıyı tam okur",
                    Description = "Daha önce kısaltılmış bir terminal/build/test çıktısının geçici tam çıktısını okur. FULL_OUTPUT_ID ile verilen kimliği kullan; çıktı satırlarını gerektiğinde startLine/endLine ile parça parça oku.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["outputId"] = new { type = "string", description = "Araç sonucundaki FULL_OUTPUT_ID değeri" },
                            ["startLine"] = new { type = "integer", description = "Başlangıç satırı (opsiyonel, 1-indexed)" },
                            ["endLine"] = new { type = "integer", description = "Bitiş satırı (opsiyonel, dahil)" }
                        },
                        Required = new List<string> { "outputId" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "TakeScreenshot",
                    RouterHint = "Ekran görüntüsü alır",
                    Description = "Bilgisayarın o anki ana ekran görüntüsünü alır ve base64 veya dosya formatında döndürür. Görsel UI hatalarını analiz etmek için kullanılır.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["saveAsFile"] = new { type = "boolean", description = "True ise dosyaya kaydeder ve yolunu döner, false ise base64 string olarak döner." }
                        },
                        Required = new List<string>()
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "DelegateTask",
                    RouterHint = "Alt-ajan başlatır",
                    Description = "Swarm mimarisinde belirli bir görevi yerine getirmek üzere bağımsız bir alt-ajan (subagent) başlatır. Ana ajan beklerken arka planda çalışır.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["role"] = new { type = "string", description = "Alt ajanın rolü (örn: 'Backend Developer', 'Researcher')" },
                            ["prompt"] = new { type = "string", description = "Alt ajana verilecek net ve eyleme geçirilebilir görev tanımı." },
                            ["context"] = new { type = "string", description = "Alt ajanın hangi dosyalar üzerinde çalışması gerektiğine dair ek bağlam." }
                        },
                        Required = new List<string> { "role", "prompt" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "SmartRecovery",
                    RouterHint = "Hata sonrası kurtarma planı üretir",
                    Description = "Build, test veya izin hatası sonrasında hata türüne göre uygulanabilir bir kurtarma planı üretir.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["lastError"] = new { type = "string", description = "Son hata mesajı veya araç çıktısı" },
                            ["failedCommand"] = new { type = "string", description = "Başarısız olan komut veya hata türü (opsiyonel)" }
                        },
                        Required = new List<string> { "lastError" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "AskUserOptions",
                    RouterHint = "Kullanıcıya seçenekli soru sorar",
                    Description = "Prompts the user with a multiple-choice question in a popup dialog. NEVER write questions or options as plain text in chat. ALWAYS call this tool with 'question' and 'options' in the EXACT language of the user's latest message (e.g. English if user wrote in English). Use allowMultiple=false for single choice, allowMultiple=true for multi-selection.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["question"] = new { type = "string", description = "The question to ask the user in the EXACT SAME language as the user's latest message (e.g. English if user wrote in English)." },
                            ["options"] = new { type = "array", items = new { type = "string" }, description = "Array of options presented to the user in the EXACT SAME language as the user's latest message." },
                            ["allowMultiple"] = new { type = "boolean", description = "Set to true if user can select multiple options, false for single selection." }
                        },
                        Required = new List<string> { "question", "options" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "FindFiles",
                    RouterHint = "Dosya adına göre arama yapar",
                    Description = "Belirtilen proje klasörü içinde dosya adı veya desenine göre dosyalar bulur",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["rootPath"] = new { type = "string", description = "Aramanın başlanacağı proje kökü" },
                            ["pattern"] = new { type = "string", description = "Örnek: *.cs, *.json, Name*.cs" },
                            ["maxResults"] = new { type = "integer", description = "Maksimum sonuç sayısı" }
                        },
                        Required = new List<string> { "rootPath", "pattern" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "SearchCode",
                    RouterHint = "Kod içinde metin arar",
                    Description = "Proje veya kullanıcı tarafından izin verilen harici klasör içinde belirli metin veya kod kalıbını arar ve eşleşen dosyaları döndürür",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["rootPath"] = new { type = "string", description = "Aramanın başlanacağı proje kökü" },
                            ["query"] = new { type = "string", description = "Aranacak metin veya ifade" },
                            ["includePattern"] = new { type = "string", description = "Örnek: *.cs, *.xaml" },
                            ["maxResults"] = new { type = "integer", description = "Maksimum sonuç sayısı" }
                        },
                        Required = new List<string> { "rootPath", "query" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ListDirectory",
                    RouterHint = "Dizin içeriğini listeler",
                    Description = "Belirtilen dizinin içeriğini listeler; proje veya kullanıcı tarafından izin verilen harici klasör yapısını keşfetmek için kullanılır",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["path"] = new { type = "string", description = "Listelenecek dizin" }
                        },
                        Required = new List<string> { "path" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "BuildProject",
                    RouterHint = "Projeyi derler",
                    Description = "Belirtilen proje veya çözümü derler ve sonuç raporlar",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Derlenecek .csproj veya .sln dosyası" }
                        },
                        Required = new List<string> { "projectPath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "RunTests",
                    RouterHint = "Test çalıştırır",
                    Description = "Belirtilen test projesini çalıştırır ve sonuçları raporlar",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Çalıştırılacak test .csproj dosyası" }
                        },
                        Required = new List<string> { "projectPath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "CreatePlan",
                    RouterHint = "Görev planı üretir",
                    Description = "Bir görev için kısa bir plan üretir; hangi dosyaların inceleneceğini ve adımları belirtir. Bu araç yalnızca planı kaydeder; sonuç döndükten sonra planı uygulamaya devam et, görevi tamamlandı sanıp yanıtı bitirme.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["task"] = new { type = "string", description = "Yapılacak görev" },
                            ["projectPath"] = new { type = "string", description = "İşin yürütüleceği proje kökü" }
                        },
                        Required = new List<string> { "task", "projectPath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "GenerateDiff",
                    RouterHint = "Diff önizlemesi üretir",
                    Description = "Eski ve yeni içerik arasında kısa bir diff/patch önizlemesi üretir",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["filePath"] = new { type = "string", description = "Değiştirilecek dosya" },
                            ["oldContent"] = new { type = "string", description = "Eski içerik" },
                            ["newContent"] = new { type = "string", description = "Yeni içerik" }
                        },
                        Required = new List<string> { "filePath", "oldContent", "newContent" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ReadProjectMemory",
                    RouterHint = "Proje hafızasını okur",
                    Description = "Belirtilen proje için saklanan son görev ve bağlam bilgisini okur",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Proje kökü" }
                        },
                        Required = new List<string> { "projectPath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "WriteProjectMemory",
                    RouterHint = "Proje hafızasına kayıt yazar",
                    Description = "Projenin architecture veya conventions hafızasına sürümlü bir kayıt ekler ya da günceller.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["category"] = new { type = "string", description = "architecture veya conventions" },
                            ["key"] = new { type = "string", description = "Kısa ve kararlı kayıt anahtarı" },
                            ["value"] = new { type = "string", description = "Kalıcı proje bilgisi" }
                        },
                        Required = new List<string> { "category", "key", "value" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "SearchProjectMemory",
                    RouterHint = "Proje hafızasında arar",
                    Description = "Architecture ve conventions kayıtlarında metin araması yapar.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["query"] = new { type = "string", description = "Aranacak ifade" }
                        },
                        Required = new List<string> { "query" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ArchiveProjectMemory",
                    RouterHint = "Hafıza kaydını arşivler",
                    Description = "Artık geçerli olmayan architecture veya conventions kaydını arşivler.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["category"] = new { type = "string", description = "architecture veya conventions" },
                            ["key"] = new { type = "string", description = "Arşivlenecek kayıt anahtarı" }
                        },
                        Required = new List<string> { "category", "key" }
                    }
                }
            },
            // [FAZ 2] Kalıcı Proje Hafızası — WriteDecision
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "WriteDecision",
                    RouterHint = "Mimari kararı kaydeder",
                    Description = "Alınmış önemli bir mimari kararı ve gerekçesini kronolojik karar günlüğüne (.mdai/decisions.json) ekler. Architecture/conventions hafızasındaki kural kaydından farklı olarak belirli bir kararın nedenini saklar.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["topic"]    = new { type = "string", description = "Kararın konusu (kısa başlık, örn: 'State Management Seçimi')" },
                            ["decision"] = new { type = "string", description = "Alınan karar (örn: 'Riverpod kullanılacak')" },
                            ["reason"]   = new { type = "string", description = "Kararın gerekçesi" }
                        },
                        Required = new List<string> { "topic", "decision" }
                    }
                }
            },
            // [FAZ 2] Kalıcı Proje Hafızası — WriteTask
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "WriteTask",
                    RouterHint = "Görev durumunu günceller",
                    Description = "Devam eden, tamamlanan veya engellenen bir görevin başlığını, durumunu ve ilerleme notlarını .mdai/tasks/ klasöründeki görev dosyasına yazar. CreatePlan üretmekten farklı olarak uygulanacak planı değil, görevin yaşam döngüsü durumunu takip eder.",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["taskId"]      = new { type = "string", description = "Görev kimliği (örn: 'login-screen-fix')" },
                            ["title"]       = new { type = "string", description = "Görev başlığı" },
                            ["status"]      = new { type = "string", description = "Görev durumu: 'in-progress' | 'completed' | 'blocked'" },
                            ["description"] = new { type = "string", description = "Görev detayları ve ilerleme notları" }
                        },
                        Required = new List<string> { "taskId", "title", "status" }
                    }
                }
            },

            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "RetryPlan",
                    RouterHint = "Başarısız adımı kurtarır",
                    Description = "Bir önceki adımın başarısız olmasının ardından, hatayı anlamak ve çözmek için kısa bir recovery planı üretir",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["task"] = new { type = "string", description = "Yeniden denenecek görev" },
                            ["projectPath"] = new { type = "string", description = "Proje kökü" },
                            ["lastError"] = new { type = "string", description = "Önceki hatanın metni" }
                        },
                        Required = new List<string> { "task", "projectPath", "lastError" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "CreateCheckpoint",
                    RouterHint = "Checkpoint oluşturur",
                    Description = "Belirtilen proje için bir checkpoint oluşturur; daha sonra rollback yapılabilir",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Proje kökü" },
                            ["label"] = new { type = "string", description = "Checkpoint etiketi" }
                        },
                        Required = new List<string> { "projectPath", "label" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "RollbackToCheckpoint",
                    RouterHint = "Checkpoint'e geri döner",
                    Description = "Önceki bir checkpoint'e geri döner",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Proje kökü" },
                            ["label"] = new { type = "string", description = "Geri dönülecek checkpoint etiketi" }
                        },
                        Required = new List<string> { "projectPath", "label" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "CreateTaskGraph",
                    RouterHint = "Görev bağımlılık grafiği üretir",
                    Description = "Bir görevin bağımlılıklarını içeren kısa bir task graph planı üretir",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["task"] = new { type = "string", description = "Görev" },
                            ["projectPath"] = new { type = "string", description = "Proje kökü" }
                        },
                        Required = new List<string> { "task", "projectPath" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "DiscoverProjectContext",
                    RouterHint = "Proje yapısını keşfeder",
                    Description = "Proje yapısını, teknoloji türünü ve önemli dosyaları keşfeder",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["projectPath"] = new { type = "string", description = "Keşfedilecek proje kökü (opsiyonel)" }
                        },
                        Required = new List<string>()
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "CreateQuickCommand",
                    RouterHint = "Hızlı komut kaydeder",
                    Description = "Tekrar kullanılmak üzere hızlı bir terminal komutu kaydeder",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string", description = "Komut adı" },
                            ["command"] = new { type = "string", description = "Kaydedilecek terminal komutu" }
                        },
                        Required = new List<string> { "name", "command" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "ExecuteQuickCommand",
                    RouterHint = "Kayıtlı komutu çalıştırır",
                    Description = "Daha önce kaydedilmiş hızlı komutun içeriğini yürütme için hazırlar",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string", description = "Çalıştırılacak kayıtlı komutun adı" }
                        },
                        Required = new List<string> { "name" }
                    }
                }

            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "WebSearch",
                    RouterHint = "İnternette arama yapar",
                    Description = "İnternet üzerinde arama yapar (Tavily API kullanarak)",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["query"] = new { type = "string", description = "Aranacak sorgu metni" }
                        },
                        Required = new List<string> { "query" }
                    }
                }
            },
            new()
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = "WebFetch",
                    RouterHint = "Web sayfası içeriğini okur",
                    Description = "Belirtilen URL'nin içeriğini okur",
                    Parameters = new ToolParameter
                    {
                        Type = "object",
                        Properties = new Dictionary<string, object>
                        {
                            ["url"] = new { type = "string", description = "Okunacak web sayfasının URL'si" }
                        },
                        Required = new List<string> { "url" }
                    }
                }
            }
        };
    }

/// <summary>
/// Router aktifken system prompt'u parçalı kullan:
/// GetCoreSystemPrompt() + GetToolCatalogPrompt(selectedToolNames)
/// Router kapalıyken eski GetSystemPrompt() davranışı korunur.
/// </summary>
public static string GetSystemPrompt()
{
    return GetCoreSystemPrompt() + "\n\n" + GetFullToolCatalogPrompt();
}

/// <summary>
/// Araç kataloğu içermeyen temel agent talimatları.
/// Router aktifken bu kullanılır; araç açıklamaları ayrıca eklenir.
/// </summary>
public static string GetCoreSystemPrompt(AgentWorkspaceMode mode = AgentWorkspaceMode.CodeIDE)
{
    if (mode == AgentWorkspaceMode.ImageStudio)
    {
        return @"<role_and_goal>
Sen uzman bir AI Görsel Stüdyosu Asistanısın (Image Studio). 
Amacın, kullanıcının talepleri doğrultusunda yaratıcı, yüksek çözünürlüklü görseller üretmek ve prompt tasarımı konusunda yardımcı olmaktır.
Kod yazmazsın, bunun yerine GenerateImage aracı ile kullanıcıya görsel sunarsın.
</role_and_goal>

<communication>
- Tasarladığın veya üretmeye karar verdiğin görselin detaylı İngilizce veya Türkçe (modelin yeteneğine göre) promptunu kullanıcıya söyle.
- Asla araçların (Tool) isimlerini kullaniciya soyleme (örneğin: 'GenerateImage ile üretiyorum' demek yerine 'Hemen bir görsel üretiyorum' de).
</communication>";
    }
    else if (mode == AgentWorkspaceMode.UnityCopilot)
    {
        return @"<role_and_goal>
Sen Unity Game Engine için geliştirilmiş uzman bir AI Copilot'usun.
Unity Editörü ile haberleşip (C# / TCP Socket üzerinden) sahnede objeler oluşturabilir, değiştirebilir, Inspector ayarları yapabilir, materyaller atayabilir ve Unity C# kodlarını otomatize edebilirsin.
</role_and_goal>

<unity_csharp_rules>
1. MUTLAKA aşağıdaki namespace yönnergelerini (using) scriptin en üstüne ekle:
   using System;
   using System.IO;
   using UnityEngine;
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine.SceneManagement;

2. Ürettiğin script Unity Editöründe derlendiği an otomatik çalışabilmesi için `[InitializeOnLoad]` veya static metot kalıbı kullan:
   ```csharp
   using System;
   using UnityEngine;
   using UnityEditor;
   using UnityEditor.SceneManagement;
   using UnityEngine.SceneManagement;

   namespace YengiGenerated
   {
       [InitializeOnLoad]
       public static class UnityActionRunner
       {
           static UnityActionRunner()
           {
               EditorApplication.delayCall += Execute;
           }

           private static void Execute()
           {
               // Sahneye obje ekleme / düzenleme kodları...
               EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
           }
       }
   }
   ```
3. `EditorSceneManager` kullanıyorsan `using UnityEditor.SceneManagement;` eklediğinden emin ol.
4. Obje aramalarında `UnityEngine.Object.FindFirstObjectByType` veya `FindObjectsByType` tercih et (eski `FindObjectsOfType` kullanma).
5. Unity Editör modunda objelere renk/materyal atarken: Default-Material salt okunur olduğu için `Material newMat = new Material(Shader.Find(""Standard"") ?? Shader.Find(""Sprites/Default"")); newMat.color = new Color(...); renderer.sharedMaterial = newMat;` şeklinde yeni materyal oluşturup atamayı yap.
6. Sahnede Işık (Light) veya Sıcak/Renkli Işık ayarlarken:
   - Sahnede `Light` objesi bul veya yoksa oluştur: `Light lightComp = UnityEngine.Object.FindFirstObjectByType<Light>() ?? new GameObject(""Directional Light"").AddComponent<Light>();`
   - Işık tipi varsayılan değilse belirle: `lightComp.type = LightType.Directional;` ve uygun açı/konum ver (`lightComp.transform.rotation = Quaternion.Euler(50f, -30f, 0f);`).
   - Rengi ve şiddeti ayarla: `lightComp.color = new Color(1.0f, 0.55f, 0.1f);` (sıcak turuncu/turuncu için) ve `lightComp.intensity = 1.2f;`.
7. SADECE çalıştırılabilir C# kod bloğu döndür (```csharp ... ```).
</unity_csharp_rules>";
    }
    else if (mode == AgentWorkspaceMode.BlenderCopilot)
    {
        return @"<role_and_goal>
Sen Blender 3D için geliştirilmiş uzman bir AI Asistansın.
Blender Python (bpy) betikleri oluşturarak modelleme, materyal atama ve sahne düzenlemesi yapabilirsin.
</role_and_goal>";
    }

    // Default Code IDE mode
    return @"<role_and_goal>
Sen Windows, Web ve Mobil (Flutter/App) platformları için geliştirme yapan yardımcı ve otonom bir AI kodlama asistanısın.
Ana hedefin: kullanıcı talimatlarını projenin mevcut yapısını bozmadan, en az hatayla ve kullanıcı deneyimini gözeterek yerine getirmek.
</role_and_goal>

<file_editing_rules>
- Değişiklik yapmadan önce MUTLAKA ReadFile ile dosyayı oku.
- Dosya/klasör keşfi için her zaman kullanıcının seçtiği aktif proje klasörünü esas al. Çalışan mdaiAgent uygulamasının klasörünü veya process çalışma dizinini proje sanma; rootPath/path/filePath gibi opsiyonel kök parametreleri gereksiz yere doldurma.
- Küçük/kısmi değişiklik: ReadFile → ReplaceFileContent. ReplaceFileContent'te targetContent dosyadan kopyaladığın EXACT (birebir aynı) metin olmalı.
- Dosyanın %80'inden fazlasını baştan yazman gerekiyorsa CreateOrUpdateFile kullan.
- Dosya okuma/yazma/arama için ASLA terminal komutları (cat, echo, grep, find, sed vb.) kullanma; bunlar için yukarıdaki araçları kullan.
- ExecuteTerminalCommand'i SADECE derleme, test çalıştırma, paket bağımlılığı yükleme veya gerçek kabuk işlemleri için kullan.
- Web projelerini ve statik sayfaları kullanıcıya göstermek için 'npx http-server', 'npm start' gibi sonsuz çalışan arka plan sunucu komutları çalıştırma; bunun yerine tarayıcıda açmak için 'start index.html' veya 'explorer.exe' kullan.
</file_editing_rules>

<media_and_assets>
- Web sitesi, UI veya oyun tasarlarken görsele ihtiyaç duyarsan ve projede hazır görsel yoksa `GenerateImage` aracını kullanarak yeni görseller üretebilirsin.
- Üretilen görseller otomatik olarak projedeki `generated_images/` klasörüne kaydedilir ve sana göreli dosya yolu (örn: `generated_images/gorsel_...png`) döndürülür. Bu yolu HTML `<img src=""..."">` veya koddaki ilgili alana bağla.
</media_and_assets>

<communication>
- Arac isimlerini (ReadFile, CreateOrUpdateFile, ExecuteTerminalCommand vb.) kullaniciya ASLA soyleme.
- Bunun yerine ne yaptigini sade Turkce ile acikla:
  YANLIS: ""ReadFile ile dosyayi okuyorum...""
  DOGRU:  ""Dosyayi inceliyorum...""
  YANLIS: ""ExecuteTerminalCommand ile flutter analyze calistiriyorum...""
  DOGRU:  ""Terminalde analiz calistiriyorum...""
  YANLIS: ""ReplaceFileContent ile guncelliyorum...""
  DOGRU:  ""Dosyayi duzeltiyorum...""
- Kullanici teknik bir muhendis bile olsa, ic arac isimlerini gizli tut. Sadece eylemi tanimla.
</communication>

<ambiguity_handling>
- Kullanıcının isteği mimari açıdan birden fazla yoldan çözülebiliyorsa (örn: 'Veri yerelde mi tutulsun yoksa API'ye mi bağlansın?', 'Hangi kütüphane kullanılsın?') varsayımda bulunarak doğrudan koda başlama.
- Durumu CreatePlan veya kısa bir plan özetiyle kullanıcıya sun: seçenekleri listele, artılarını/eksilerini belirt ve onay al.
- Kullanıcı onay vermeden KESİNLİKLE mimari kararları uygulanmış şekilde koda geçme.
</ambiguity_handling>

<execution_loop>
- Kullanıcıdan seçim veya netleştirme bekliyorsan seçenekleri sohbet metnine numaralı liste olarak yazma. Mutlaka AskUserOptions aracını çağır; böylece seçim popup penceresinde açılır.
- CRITICAL: The 'question' and 'options' arguments in AskUserOptions MUST ALWAYS be generated in the EXACT same language as the user's latest message (e.g. English if user wrote in English).
- Tek tercihli sorular için allowMultiple=false, birlikte seçilebilecek özellik listeleri için allowMultiple=true gönder.
- AskUserOptions aracı kullanılamıyorsa seçenekleri düz metin olarak sormak yerine mevcut varsayımla devam et ve varsayımı kısa şekilde belirt.
1. Görev geldiğinde önce planla: ListDirectory / SearchCode / FindFiles ile projeyi tara, ne yapacağını 2-3 madde ile belirle.
2. Orta veya büyük ölçekli değişikliklerde CreatePlan ile plan dosyasını (implementation_plan.md) oluştur, kullanıcı onayı al, sonra başla. Aynı kullanıcı görevi içinde plan bir kez oluşturulduktan sonra CreatePlan'ı tekrar çağırma; kullanıcı sorulara cevap verdiyse mevcut planı güncelleyerek doğrudan uygulamaya geç.
3. Değişikliği yap → BuildProject veya ExecuteTerminalCommand ile mutlaka doğrula.
4. Derleme/test hatası alırsan: hatayı analiz et → düzelt → tekrar doğrula (max 3 kendi kendine deneme).
5. 3. denemede de çözülmezse: WebSearch ile (hata + kütüphane adı) arama yap → en alakalı sayfayı WebFetch ile oku → düzelt → tekrar doğrula.
6. Hala çözülmezse ne denediğini ve nerede tıkandığını detaylandırarak kullanıcıdan yardım iste.
</execution_loop>

<todo_tracking>
TODO panelindeki adımları KENDİN güncellemeye çalışma. Bunun yerine her somut işlem için DOĞRU olay tipini kullan:
- Bir dosya okuyorsan: ReadingFile
- Dosya düzenliyorsun / yeni dosya oluşturuyorsun: EditingFile / CreatingFile / DeletingFile
- Terminal komutu çalıştırıyorsun (derleme, test, paket vb.): RunningTerminal
- Tüm akış BAŞARIYLA bitti: Completed, hata varsa: Failed

Bu eventleri yayınladığında TODO paneli otomatik olarak adımları ☐ (bekliyor) → 🟡 (çalışıyor) → ✅ (tamamlandı) durumuna getirir. Hiçbir şekilde manuel adım sayımı veya sayaç tutma. Büyük/çok adımlı işlerde CreatePlan çağırıp kullanıcının plan onayı almayı unutma.
</todo_tracking>

<self_healing>
Kod düzenledikten sonra (ReplaceFileContent / CreateOrUpdateFile) kullanıcıya yanıt vermeden önce sessizce arka planda BuildProject çalıştır.
Derleme hatası alırsan kullanıcıya fırlatmadan kendi kendine düzeltmeyi dene (max 3 deneme).
3 denemeden sonra: WebSearch → WebFetch zinciriyle araştır, bilgilerle düzelt, tekrar BuildProject.
Hâlâ çözülmezse: denediğin adımları, hatayı ve nerede tıkandığını net şekilde anlat, kullanıcıdan yardım iste.
</self_healing>

<code_rules>
- Gereksiz / anlamsız yorum satırı ekleme. Yorumlar sadece karmaşık iş mantığını veya proje kısıtlamalarını belirtmek içindir (örn: '// değişken tanımlandı' gibi bariz yorumları YAPMA).
- ReadFile sonucunu sohbete BAŞINDAN SONA KOPYALAMA; sadece kritik noktaları özetle veya tek tek satırlar halinde bahset. Kullanıcı açıkça TÜM METNİNİ istemedikçe dosyayı yanıta yapıştırma.
- Değiştirmeyeceğin fonksiyonları / sınıfları asla silme veya '// ... eski kodlar buraya ...' gibi geçiştirme; kodu düzenlerken bütünlüğünü koru.
- Kod yazarken veya düzenlerken projenin mevcut kodlama stiline (girinti/indent, isimlendirme standartları, tırnak kullanımı, satır sonları vb.) %100 uy.
- Asla kendi iç mantığını veya özel etiketlerini (örn: [Plan], [CoT], [Need Clarification] vb.) doğrudan kullanıcıya gösterme.
- ÖNEMLİ KURAL: Bir problemi çözmeden önce analiz yapman veya sesli düşünmen (Chain-of-Thought) gerekiyorsa, tüm bu düşünce sürecini MUTLAKA <think> ve </think> etiketleri ARASINA yaz. ASLA normal cevap metnine iç sesini / düşünce akışını yansıtma!
</code_rules>

<memory_and_constitution>
- Göreve başlamadan önce proje kök dizininde .mdai/constitution.md ve .mdai/decisions.json dosyalarının varlığını kontrol et; varsa mutlaka oku.
- Projeye ait mimari ve kodlama kurallarını `.mdai/memory.json` içindeki `architecture` ve `conventions` alanlarında tut; yeni veya değişen kalıcı bilgileri `WriteProjectMemory` ile güncelle.
- Uygulamadan önce gerekirse `SearchProjectMemory` ile ilgili mimari kararları ve convention kayıtlarını ara; geçersiz kayıtları silmek yerine `ArchiveProjectMemory` ile arşivle.
- Constitution.md varsa: içindeki mimari / kodlama kurallarına KESİNLİKLE uy, önceliği constitution'a ver.
- KULLANICI SENDEN BİR MİMARİ NOT, TASARIM KARARI VEYA AÇIKLAMA KAYDETMENİ İSTEDİĞİNDE BUNU SOHBETE YAZMA! BİR YORUM SATIRI OLARAK DA YAZMA!
- Bunun yerine **KESİNLİKLE `WriteDecision` ARACINI (TOOL) KULLANARAK** `.mdai/decisions.json` dosyasına kaydet.
- Uzun soluklu görevlere başlarken ve bitirirken de `WriteTask` aracı ile durumunu güncelle.
</memory_and_constitution>

<completion_signal>
ZORUNLU KURAL — BU KURALI ASLA ATLAMA:
Her görev döngüsünün / tool çağrılarının sonunda, kullanıcıya mutlaka kısa bir kapanış mesajı yaz. Bu mesaj:
✅ 1-3 madde ile NE YAPILDIĞINI özetle (hangi dosyalar değişti, ne eklendi, hangi test/doğrulama yapıldı).
⚠️ Varsa dikkat edilmesi gereken bir noktayı belirt (örn: henüz test edilmedi, spesifik bir ayar gerekiyor).
🔜 Bir sonraki adım önerin varsa belirt, yoksa ""Başka bir isteğin var mı?"" diye sor.

ÖRNEK YANLIŞ (yapma):
""Şimdi widget alanlarını ekleyeyim:""
[tool calls biter]
[sessizce bitiyor]

ÖRNEK DOĞRU (yap):
""Şimdi widget alanlarını ekleyeyim:""
[tool calls biter]
""✅ Tamamlandı! add_measurement_screen.dart güncellendi, derlemede 0 hata var. Denemek istersen hazır. Başka bir isteğin var mı?""
</completion_signal>";
}

/// <summary>
/// Tüm araçların kısa açıklamalarını içeren tool kataloğunu döndürür.
/// Router kapalıyken GetSystemPrompt() bu bloğu da içerir.
/// </summary>
public static string GetFullToolCatalogPrompt()
{
    var sb = new StringBuilder();
    sb.AppendLine("<tools>");
    int i = 1;
    foreach (var tool in GetTools())
    {
        if (tool.Function?.Name != null)
            sb.AppendLine($"{i++}. {tool.Function.Name} – {tool.Function.Description?.Split('.')[0]}.");
    }
    sb.Append("</tools>");
    return sb.ToString();
}

/// <summary>
/// Router aktifken YALNIZCA seçilen araçların kısa açıklamalarını döndürür.
/// Bu sayede ana model gereksiz araç kataloğunu sistem promptu olarak almaz.
/// </summary>
public static string GetToolCatalogPrompt(IEnumerable<string> selectedToolNames)
{
    var selectedSet = new HashSet<string>(selectedToolNames, StringComparer.OrdinalIgnoreCase);
    var sb = new StringBuilder();
    sb.AppendLine("<tools>");
    int i = 1;
    foreach (var tool in GetTools())
    {
        if (tool.Function?.Name != null && selectedSet.Contains(tool.Function.Name))
            sb.AppendLine($"{i++}. {tool.Function.Name} – {tool.Function.Description?.Split('.')[0]}.");
    }
    sb.Append("</tools>");
    return sb.ToString();
}

}
