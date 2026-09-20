import json
import os
import random

os.makedirs("data", exist_ok=True)

# ==============================================================================
# BİLİNGUAL (TR / EN) ROUTER DATASET GENERATOR
# All 32 Yengi IDE Tools & Scenarios
# ==============================================================================

dataset = []

# ------------------------------------------------------------------------------
# 1. CASUAL / CHIT-CHAT (Tools: [], needsPlanning: False)
# ------------------------------------------------------------------------------
casual_queries = [
    # TR
    "merhaba", "selam nasılsın", "günaydın", "iyi akşamlar", "kimsin sen",
    "bana kendini anlat", "bugün hava nasıl", "teşekkür ederim sağol",
    "harika iş çıkardın", "kolay gelsin", "ne haber", "selamlar", "nasılsın",
    "neler yapabilirsin", "sen bir yapay zeka mısın", "tamam harika", "anlaşıldı",
    "sağol teşekkürler", "eline sağlık", "süpersin", "nasılsın nasıl gidiyor",
    # EN
    "hello", "hi there", "how are you", "good morning", "good evening",
    "who are you", "tell me about yourself", "thank you so much",
    "great job", "nice to meet you", "what's up", "greetings",
    "what can you do", "are you an ai", "okay great", "understood",
    "thanks a lot", "awesome work", "cool", "how is it going"
]

for q in casual_queries * 10:
    dataset.append({"user": q, "tools": [], "needsPlanning": False})

# ------------------------------------------------------------------------------
# 2. ASK USER OPTIONS (Tools: ["AskUserOptions"], needsPlanning: True/False)
# ------------------------------------------------------------------------------
ask_user_templates_alone = [
    # TR
    "bana bu konuda soru sorarak karar verdir",
    "hangi teknolojiyi kullanmam gerektiği hakkında seçenekli soru sor",
    "tasarım seçeneklerini bana soru sorarak sun",
    "bana seçenekler sun, hangisini seçtiğimi sor",
    "birkaç soru sorarak projeyi netleştir",
    # EN
    "ask me a question with options to decide",
    "give me choices for which framework or tech stack to use",
    "ask me step by step questions before proceeding",
    "present me with a list of options to choose from",
    "ask options regarding the implementation strategy"
]
for t in ask_user_templates_alone * 6:
    dataset.append({"user": t, "tools": ["AskUserOptions"], "needsPlanning": False})

ask_user_with_creation = [
    # TR
    "bana basit bir {} oyunu yap, kararlar için adım adım soru sor",
    "bir {} uygulaması geliştir ama önce bana seçenekli sorular sor",
    "sıfırdan bir {} projesi oluştur, teknoloji seçimini soru sorarak bana bırak",
    "{} projesi yaz, özellikler için bana seçenekler sun",
    # EN
    "Can you make me a simple web-based {} game? If you need to make some decisions, you can ask me questions step by step.",
    "Build a {} app for me, but first ask me questions with options to clarify requirements.",
    "Create a {} project, ask me step by step questions for choices.",
    "Develop a {} application, present choices to me before writing code."
]
topics = ["Tetris", "Yılan (Snake)", "Hesap Makinesi", "To-Do List", "Hava Durumu", "Not Defteri", "Portfolio", "Chat Bot"]

for top in topics:
    for t in ask_user_with_creation:
        q = t.format(top)
        dataset.append({"user": q, "tools": ["AskUserOptions", "CreateOrUpdateFile"], "needsPlanning": True})

# ------------------------------------------------------------------------------
# 3. READ & INSPECT FILES (Tools: ["ReadFile", "ListDirectory", "FindFiles"])
# ------------------------------------------------------------------------------
files = ["index.html", "MainWindow.xaml.cs", "app.js", "pubspec.yaml", "README.md", "styles.css", "main.dart", "ChatFlowService.cs", "SettingsWindow.xaml", "package.json"]

read_templates = [
    # TR
    "{} dosyasını oku", "{} içeriğini göster", "{} dosyasında ne yazıyor",
    "{} klasöründeki dosyaları listele", "{} dosyasının ilk 50 satırını oku",
    "projedeki {} uzantılı dosyaları bul", "{} dosyasını incele ve özetle",
    # EN
    "read the contents of {}", "show me {}", "what is inside {}",
    "list files in the folder", "find files with {} extension",
    "inspect and summarize {}", "view file {}"
]
for f in files:
    for t in read_templates:
        q = t.format(f)
        dataset.append({"user": q, "tools": ["ReadFile", "ListDirectory"], "needsPlanning": False})

# ------------------------------------------------------------------------------
# 4. CODE SEARCH & CONTEXT DISCOVERY (Tools: ["SearchCode", "FindFiles", "DiscoverProjectContext"])
# ------------------------------------------------------------------------------
search_templates = [
    # TR
    "projede {} ifadesini ara", "{} nerede tanımlanmış bul",
    "projenin mimarisini ve klasör yapısını keşfet", "{} kod satırlarını bul",
    # EN
    "search for {} across the codebase", "find where {} is defined",
    "discover project architecture and context", "search code for {}"
]
search_terms = ["HttpClient", "SaveSettings", "OnClick", "DatabaseContext", "AuthMiddleware"]

for term in search_terms:
    for t in search_templates:
        q = t.format(term)
        tools = ["SearchCode", "DiscoverProjectContext"] if "keşfet" in q or "discover" in q else ["SearchCode", "ReadFile"]
        dataset.append({"user": q, "tools": tools, "needsPlanning": False})

# ------------------------------------------------------------------------------
# 5. CREATION & CODING (Tools: ["CreateOrUpdateFile", "ReadFile"], needsPlanning: True)
# ------------------------------------------------------------------------------
creation_templates = [
    # TR
    "bana basit bir {} oyunu yap", "sıfırdan bir {} uygulaması yaz",
    "{} dosyası oluştur ve içine kodları yaz", "{} için yeni bir component ekle",
    "{} sayfasına giriş formu ekle", "bana güzel bir {} örneği kodla",
    # EN
    "make me a simple {} game", "write a {} app from scratch",
    "create {} file and add implementation", "add a new component for {}",
    "implement a login form in {}", "code a clean {} example"
]
for top in topics:
    for t in creation_templates:
        q = t.format(top)
        dataset.append({"user": q, "tools": ["CreateOrUpdateFile", "ReadFile"], "needsPlanning": True})

# ------------------------------------------------------------------------------
# 6. FIXES & EDITS (Tools: ["SearchCode", "ReadFile", "ReplaceFileContent"])
# ------------------------------------------------------------------------------
fix_templates = [
    # TR
    "{} dosyasındaki hatayı bul ve düzelt", "{} içindeki null reference hatasını çöz",
    "{} kodunda performans optimizasyonu yap", "{} fonksiyonunu refaktör et",
    # EN
    "find and fix the error in {}", "resolve null reference exception in {}",
    "optimize performance in {}", "refactor the function in {}"
]
for f in files:
    for t in fix_templates:
        q = t.format(f)
        dataset.append({"user": q, "tools": ["SearchCode", "ReadFile", "ReplaceFileContent"], "needsPlanning": False})

# ------------------------------------------------------------------------------
# 7. TERMINAL, BUILD & TESTS (Tools: ["ExecuteTerminalCommand", "BuildProject", "RunTests"])
# ------------------------------------------------------------------------------
terminal_queries = [
    # TR
    ("projeyi derle ve test et", ["BuildProject", "RunTests"], False),
    ("terminalde npm install komutunu çalıştır", ["ExecuteTerminalCommand"], False),
    ("flutter analyze çalıştır", ["ExecuteTerminalCommand"], False),
    ("unit testleri koştur", ["RunTests"], False),
    ("dotnet build çalıştır", ["BuildProject"], False),
    ("git status kontrol et", ["ExecuteTerminalCommand"], False),
    # EN
    ("build the project and run unit tests", ["BuildProject", "RunTests"], False),
    ("run npm install in terminal", ["ExecuteTerminalCommand"], False),
    ("execute flutter analyze command", ["ExecuteTerminalCommand"], False),
    ("run unit tests", ["RunTests"], False),
    ("run dotnet build", ["BuildProject"], False),
    ("check git status via terminal", ["ExecuteTerminalCommand"], False)
]
for q, t_list, plan in terminal_queries * 12:
    dataset.append({"user": q, "tools": t_list, "needsPlanning": plan})

# ------------------------------------------------------------------------------
# 8. WEB SEARCH & FETCH (Tools: ["WebSearch", "WebFetch"])
# ------------------------------------------------------------------------------
web_queries = [
    # TR
    ("internette .NET 8 WPF modern UI kütüphanelerini ara", ["WebSearch"], False),
    ("https://react.dev sayfasının içeriğini çek ve oku", ["WebFetch"], False),
    ("en son Flutter sürümlerini google'da ara", ["WebSearch"], False),
    ("dokümantasyon URL'sini oku: https://docs.ollama.com", ["WebFetch"], False),
    # EN
    ("search the web for modern WPF UI libraries", ["WebSearch"], False),
    ("fetch content from https://react.dev and explain it", ["WebFetch"], False),
    ("search online for latest Next.js 14 features", ["WebSearch"], False),
    ("read documentation at https://docs.flutter.dev", ["WebFetch"], False)
]
for q, t_list, plan in web_queries * 10:
    dataset.append({"user": q, "tools": t_list, "needsPlanning": plan})

# ------------------------------------------------------------------------------
# 9. GENERATE IMAGE & SCREENSHOT (Tools: ["GenerateImage"], ["TakeScreenshot"])
# ------------------------------------------------------------------------------
media_queries = [
    # TR
    ("bana bir futuristik cyber-punk arayüz görseli üret", ["GenerateImage"], False),
    ("ekran görüntüsü al ve UI hatasını incele", ["TakeScreenshot"], False),
    ("oyun için bir uzay gemisi sprite görseli oluştur", ["GenerateImage"], False),
    ("o anki ekranın resmini çek", ["TakeScreenshot"], False),
    # EN
    ("generate a futuristic game logo image", ["GenerateImage"], False),
    ("take a screenshot of my screen to analyze layout error", ["TakeScreenshot"], False),
    ("create an image asset for hero banner", ["GenerateImage"], False),
    ("capture screenshot now", ["TakeScreenshot"], False)
]
for q, t_list, plan in media_queries * 10:
    dataset.append({"user": q, "tools": t_list, "needsPlanning": plan})

# ------------------------------------------------------------------------------
# 10. SUBAGENTS & DELEGATION (Tools: ["DelegateTask"])
# ------------------------------------------------------------------------------
agent_queries = [
    # TR
    ("arka planda çalışması için bir araştırmacı alt-ajan (subagent) başlat", ["DelegateTask"], True),
    ("backend refactoring işini ayrı bir ajana devret", ["DelegateTask"], True),
    # EN
    ("spawn a research subagent to explore the repo in background", ["DelegateTask"], True),
    ("delegate database migration task to a subagent", ["DelegateTask"], True)
]
for q, t_list, plan in agent_queries * 15:
    dataset.append({"user": q, "tools": t_list, "needsPlanning": plan})

# ------------------------------------------------------------------------------
# 11. PROJECT MEMORY & CHECKPOINTS (Tools: ["ReadProjectMemory", "WriteProjectMemory", "CreateCheckpoint"])
# ------------------------------------------------------------------------------
memory_queries = [
    # TR
    ("proje hafızasını oku", ["ReadProjectMemory"], False),
    ("önemli mimari kararı proje hafızasına kaydet", ["WriteProjectMemory"], False),
    ("mevcut kod durumundan bir checkpoint oluştur", ["CreateCheckpoint"], False),
    # EN
    ("read project memory context", ["ReadProjectMemory"], False),
    ("write architecture decisions into project memory", ["WriteProjectMemory"], False),
    ("create a checkpoint before starting refactoring", ["CreateCheckpoint"], False)
]
for q, t_list, plan in memory_queries * 12:
    dataset.append({"user": q, "tools": t_list, "needsPlanning": plan})


# Shuffle dataset
random.shuffle(dataset)

def format_qwen(item):
    tools_json = json.dumps({"tools": item["tools"], "needsPlanning": item["needsPlanning"]})
    return {
        "text": f"<|im_start|>system\nYou are an AI Tool Router for mdaiAgent IDE. Output JSON with tools and needsPlanning.<|im_end|>\n<|im_start|>user\n{item['user']}<|im_end|>\n<|im_start|>assistant\n{tools_json}<|im_end|>"
    }

# Split %90 Train, %10 Valid
split_idx = int(len(dataset) * 0.9)
train_data = dataset[:split_idx]
valid_data = dataset[split_idx:]

with open("data/train.jsonl", "w", encoding="utf-8") as f:
    for item in train_data:
        f.write(json.dumps(format_qwen(item), ensure_ascii=False) + "\n")

with open("data/valid.jsonl", "w", encoding="utf-8") as f:
    for item in valid_data:
        f.write(json.dumps(format_qwen(item), ensure_ascii=False) + "\n")

print(f"Basariyla {len(train_data)} egitim, {len(valid_data)} dogrulama verisi uretildi! (Toplam: {len(dataset)})")