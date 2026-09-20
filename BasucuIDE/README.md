# 🚀 Yengi

> **Y**our **E**ngineering **N**exus, **G**enerative **I**ntelligence — *Mühendisliğinin Merkezi, Üretken Zekâ.*  
> *"Yengi; Uzun süre uğraştığın bir şeyin sonunda başarıyla sonuçlanması, emeğinin karşılığında ulaştığın o mutlu sonucun adı."*

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](LICENSE)
[![Framework: .NET 8 WPF](https://img.shields.io/badge/Framework-.NET%208%20WPF-purple.svg)](https://dotnet.microsoft.com/)
[![Support: BuyMeACoffee](https://img.shields.io/badge/Support-Buy%20Me%20A%20Coffee-FFDD00.svg)](https://buymeacoffee.com/mdaiyazilim)

**Yengi**, Windows üzerinde çalışan; kod okuma, düzenleme, derleme, test ve git yönetimi gibi işleri kendi başına ("otonom") yürütebilen, çok sağlayıcılı (Claude / Gemini / OpenAI / yerel LLM) bir **AI kodlama asistanı masaüstü uygulamasıdır.** WPF (.NET 8) üzerine yazılmıştır ve tek bir editör/agent penceresinde; dosya gezgini, kod editörü, git paneli, terminal, RAG destekli proje hafızası ve sesli komut desteğini bir araya getirir.

---

## İçindekiler

- [Genel Bakış](#genel-bakış)
- [Mimari](#mimari)
- [Uzmanlık Modları (Workspace Modes)](#uzmanlık-modları-workspace-modes)
- [AI Sağlayıcıları ve Router](#ai-sağlayıcıları-ve-router)
- [Agent Araç Seti (Tools)](#agent-araç-seti-tools)
- [Arayüz ve Buton Referansı](#arayüz-ve-buton-referansı)
- [Arka Planda Çalışan Servisler](#arka-planda-çalışan-servisler)
- [Eklenti (Plugin) Sistemi](#eklenti-plugin-sistemi)
- [Proje Anayasası ve Hafıza (constitution.md / memory.md)](#proje-anayasası-ve-hafıza)
- [Çoklu Dil Desteği (i18n)](#çoklu-dil-desteği-i18n)
- [Gizlilik ve Telemetri](#gizlilik-ve-telemetri)
- [Ayarlar](#ayarlar)
- [Kurulum ve Çalıştırma](#kurulum-ve-çalıştırma)
- [Proje Yapısı](#proje-yapısı)
- [Bilinen Kısıtlar / Yol Haritası](#bilinen-kısıtlar--yol-haritası)

---

## Genel Bakış

mdaiAgent, klasik bir "chat penceresi" değil; bir projeyi uçtan uca yönetebilen bir **agent runtime**'dır:

- Bir proje klasörü seçtiğinizde, dosya ağacını, git durumunu ve proje bağlamını otomatik keşfeder.
- Kullanıcı bir görev tanımladığında, model (LLM) gerekli gördüğü araçları (`ReadFile`, `ReplaceFileContent`, `ExecuteTerminalCommand`, `BuildProject` vb.) sırayla çağırarak görevi kendi kendine tamamlar.
- Değişiklik sonrası **sessizce arka planda derleme** yaparak kendi kendini doğrular; hata varsa 3 defaya kadar kendi kendini onarmaya çalışır, olmazsa web'den araştırıp tekrar dener.
- Tüm bu davranış `ToolDefinitions.cs` içindeki sistem promptunda (`GetCoreSystemPrompt`) ayrıntılı kurallarla tanımlıdır: dosya düzenleme kuralları, belirsizlik yönetimi, TODO event akışı, self-healing döngüsü, plan onayı vb.

Uygulama; C#/.NET projeleri kadar Flutter/mobil ve web projeleriyle de çalışacak şekilde tasarlanmıştır (sistem promptu "Windows, Web ve Mobil (Flutter/App)" ifadesini açıkça belirtir).

---

## Mimari

```
UI (WPF)  ──►  MainWindow (+ partial dosyalar)  ──►  IAgentCore / ChatFlowService
                                                        │
                    ┌───────────────────────────────────┼─────────────────────────────┐
                    ▼                                    ▼                             ▼
            ToolExecutor (21+ araç)              Router (opsiyonel, küçük        Provider Katmanı
         (FileOperations/Search/Build/            model ile araç seçimi)      (IAiProvider: Anthropic,
        Git/Web/Command/Delegation/RAG/...)                                    Gemini, OpenAI-uyumlu)
```

Ana bileşenler:

| Katman | Sorumluluk | Başlıca dosyalar |
|---|---|---|
| **UI** | Pencere, sohbet paneli, dosya ağacı, editör, terminal, git paneli, mod seçici | `MainWindow.xaml(.cs)`, `WorkspaceSettingsWindow.xaml(.cs)`, `ImageViewerWindow.xaml(.cs)`, `MainWindow.ChatPanel.cs`, `MainWindow.Terminal.cs` |
| **Agent Core** | UI'dan bağımsız araç çalıştırma, planlama, checkpoint, mod yönlendirme | `IAgentCore.cs`, `AgentCoreRuntime.cs`, `IChatFlowService.cs` / `ChatFlowService.cs` |
| **Uzmanlık Modları** | Görsel Üretimi, Blender 3D, Unity ve IDE modları | `ToolDefinitions.cs`, `ChatFlowService.cs` (`HandleImageStudioRequestAsync`, `HandleBlenderRequestAsync`) |
| **Araçlar (Tools)** | LLM'nin çağırabileceği somut yetenekler | `ToolDefinitions.cs` (şema + sistem promptu), `ToolExecutor.cs` (yürütme), `Services/*` |
| **Sağlayıcılar** | Farklı LLM API'lerine ortak arayüzle erişim | `Interfaces/IAiProvider.cs`, `Providers/*` |
| **Router** | Küçük/ucuz bir modelle hangi araçların gerekli olduğuna karar verme | `Services/Router/*` |
| **Kalıcılık / Güvenlik** | Checkpoint, backup, secret saklama, ayarlar | `CheckpointManager.cs`, `SecretStore.cs`, `SettingsWindow.xaml.cs` |
| **Eklentiler** | Dil bazlı statik analiz eklentileri (dinamik yüklenebilir) | `PluginSystem.cs`, `PluginManagerWindow.xaml(.cs)` |
| **Lokalizasyon** | TR/EN/ZH arayüz metinleri | `Localization*.cs`, `LocExtension.cs`, `Resources/Strings*.resx` |

---

## Uzmanlık Modları (Workspace Modes)

Yengi, klasik kod yazma asistanlığının ötesine geçerek farklı disiplinlere özel **Uzmanlık Modları** sunar. Üst araç çubuğundaki (Split View yanındaki) mod seçici ile anında geçiş yapılabilir:

| Mod | İkon | Açıklama | Davranış & Özellikler |
|---|---|---|---|
| **Code IDE** | 💻 | Otonom Kodlama Asistanı | Varsayılan kodlama modu. Dosya okuma/yazma, derleme, test ve self-healing döngülerini yürütür. |
| **Görsel Stüdyosu** | 🎨 | AI Görsel Üretimi | LLM token maliyetini 0'a indiren doğrudan geçiş (Pass-Through) mimarisi. Metinden görsel üretir. |
| **Blender Asistanı** | 🧊 | 3D Modelleme Copilot'u | Blender ile canlı TCP Socket (port 8181) bağlantısı. Python `bpy` kodları ile 3D sahneleri yönetir. |
| **Unity Copilot** | 🎮 | Oyun Geliştirme Asistanı | Unity Editor ile canlı Socket (port 8282) bağlantısı. Sahne düzenleme ve C# oyun betiği otomasyonu. |

---

### 🎨 Görsel Stüdyosu (Image Studio)
- **Doğrudan Geçiş Mimarisi (Direct Pass-Through):** Görsel modundayken mesajlarınız metin modellerine (LLM) yollanmaz; 0 LLM token harcanarak doğrudan Görsel API'sine iletilir.
- **Ücretsiz Mod (Pollinations.ai):** API anahtarı girilmediğinde veya ücretsiz servis seçildiğinde otomatik olarak Pollinations.ai altyapısı kullanılır (Key gerektirmez, 1024x1024 yüksek kaliteli görseller sunar).
- **Özel API Desteği:** DALL-E 3, Flux veya OpenRouter API anahtarları mod yanındaki ⚙️ ayarlar penceresinden tanımlanabilir.
- **Otomatik Kaydetme:** Üretilen tüm görseller projenizdeki `generated_images/` klasörüne (veya Masaüstünüze) tarih damgalı `.png` olarak indirilir.
- **Entegre Görsel Görüntüleyici (ImageViewerWindow):** Projenizdeki görsellere çift tıklandığında kod yerine özel WPF görsel penceresi açılır. Çözünürlük, dosya boyutu ve "Windows'ta Aç" / "Klasörde Göster" seçenekleri sunar.

---

### 🧊 Blender Asistanı (Blender Copilot)
- **Canlı 3D Modelleme:** Yengi'ye verdiğiniz Türkçe komutlar (örn: *"Yeşil bir silindir çiz, rengini kahverengi yap ve üstüne ağaç oluştur"*) `bpy` koduna çevrilerek Blender'a iletilir.
- **Sohbet Hafızası (Iterative Modeling):** Yengi önceki adımlarda oluşturduğu objelerin adlarını ve materyallerini hatırlar; üzerine eklemeler yapabilir veya var olan nesneleri düzenleyebilir.
- **Tek Tıkla Otomatik Kurulum:** ⚙️ ayarlar penceresindeki **"🚀 Eklentiyi Blender'a Otomatik Kur"** butonuna basıldığında `yengi_copilot.py` eklentisi Blender AppData dizinine otomatik yüklenir.
- **Sıfır Bağımlılık (Stdlib Socket):** Dış kütüphane (pip) gerektirmeyen standart Python Socket motoru (port 8181).

---

### 🎮 Unity Copilot
- **Sahne & Oyun Betiği Otomasyonu:** Sahnede GameObject, Material, Component yönetimi ve C# betiği (`PlayerController.cs` vb.) üretimi.
- **Canlı Socket Haberleşmesi:** Port 8282 üzerinden Unity Editor ile çift yönlü iletişim.

---

## AI Sağlayıcıları ve Router

Uygulama tek bir `IAiProvider` arayüzü üzerinden birden fazla sağlayıcıyı destekler (`AiProviderFactory`):

| Sağlayıcı türü | Açıklama | İstemci sınıfı |
|---|---|---|
| `Anthropic` | Claude modelleri (varsayılan model örn. `claude-sonnet-4-5`) | `AnthropicApiClient` |
| `Google` | Gemini modelleri | `GeminiApiClient` |
| `ApiService` | OpenRouter, DeepSeek, Nvidia NIM gibi OpenAI-uyumlu bulut servisleri | `OpenAiCompatibleClient` |
| `LocalModel` | Ollama, LM Studio gibi yerel/offline modeller | `OpenAiCompatibleClient` (yerel base URL ile) |
| `OpenAI` | Doğrudan OpenAI API'si | `OpenAiCompatibleClient` |

Ayrıca ayrı ayrı yapılandırılabilen iki yardımcı model kanalı vardır:
- **STT (Sesli Komut)** – varsayılan olarak Groq Whisper (`whisper-large-v3`) kullanır, ana modelden bağımsızdır.
- **RAG / Embedding** – varsayılan olarak OpenAI `text-embedding-3-small`, proje kodunu vektörleştirip anlamsal arama yapmak için kullanılır.

**AI Router** (opsiyonel, `RouterEnabled`): Her istekte tüm araç kataloğunu ana modele göndermek yerine, küçük/ucuz bir model (varsayılan `gemini-2.5-flash` veya yerel `mdai-router-1.5b-q4`) hangi araçların gerekli olduğuna önceden karar verir; böylece sistem promptu kısalır ve token maliyeti düşer. `RouterBlacklistWindow` ile router'ın hiç önermeyeceği araçlar hariç tutulabilir; `IsManualToolManagement` açıksa araç seçimini tamamen kullanıcı yapar (`btnTools` panelinden).

- **Koşullu Çekirdek Araçlar & Dinamik Re-Routing:** Sadece okuma sorgularında gereksiz araçlar elenerek token/latency tasarrufu sağlanır; kodlama/planlama anlarında ise `ExecuteTerminalCommand` gibi kritik araçlar otomatik dahil edilir. Ana model eksik araç tespit ederse `[REQUEST_TOOL: ...]` ile dinamik talep edebilir.
- **Runaway Thinking & UI Stream Protection:** Düşünme modellerinin sonsuz monologlarını ve WPF sohbet arayüzünün donmasını engelleyen otomatik daraltma ve 150ms scroll koruması.

`ProviderFallbackStrategy` STT/RAG için ana sağlayıcı desteklemiyorsa otomatik olarak uygun bir yedek sağlayıcı seçer (`ProviderCapability.cs`).

---

## Agent Araç Seti (Tools)

Modelin fonksiyon çağrısı (function calling) ile erişebildiği araçlar `ToolDefinitions.cs`'de tanımlanır, gerçek yürütme `ToolExecutor.cs` ve `Services/*` sınıflarında yapılır:

| Araç | Ne işe yarar |
|---|---|
| `ReadFile` | Dosya içeriğini (opsiyonel satır aralığıyla) okur |
| `CreateOrUpdateFile` | Yeni dosya oluşturur veya baştan yazar |
| `ReplaceFileContent` | Dosyanın belirli bir bölümünü değiştirir (büyük dosyalarda tercih edilir) |
| `ExecuteTerminalCommand` | Belirtilen (opsiyonel) klasörde terminal komutu çalıştırır |
| `TakeScreenshot` | Ekran görüntüsü alır (görsel UI hatalarını analiz etmek için) |
| `DelegateTask` | Swarm mimarisinde arka planda çalışacak bağımsız bir alt-ajan (subagent) başlatır |
| `AskUserOptions` | Kullanıcıya popup pencerede seçimli/serbest metinli soru sorar |
| `FindFiles` | Ada/desene göre dosya bulur |
| `SearchCode` | Proje içinde metin/kod deseni arar |
| `ListDirectory` | Dizin içeriğini listeler |
| `BuildProject` | Projeyi/çözümü derler, sonucu raporlar |
| `RunTests` | Test projesini çalıştırır |
| `CreatePlan` | Görev için kısa bir uygulama planı (`implementation_plan.md`) üretir |
| `GenerateDiff` | Eski/yeni içerik arasında diff önizlemesi üretir |
| `ReadProjectMemory` | Proje için saklanan son görev/bağlam bilgisini okur (`.mdai/memory.md`) |
| `RetryPlan` | Başarısız adımdan sonra kısa bir kurtarma (recovery) planı üretir |
| `CreateCheckpoint` | Geri alınabilir bir checkpoint oluşturur |
| `RollbackToCheckpoint` | Önceki bir checkpoint'e geri döner |
| `CreateTaskGraph` | Görev bağımlılıklarını içeren bir task graph planı üretir |
| `DiscoverProjectContext` | Proje yapısını, teknoloji türünü ve önemli dosyaları keşfeder |
| `CreateQuickCommand` / `ExecuteQuickCommand` | Sık kullanılan terminal komutlarını kaydeder/çalıştırır |
| `WebSearch` | İnternette arama yapar (Tavily API) |
| `WebFetch` | Bir URL'nin içeriğini okur |

Sistem promptu (`GetCoreSystemPrompt`), modele şu davranışları **zorunlu** kılar:
- Değişiklik öncesi mutlaka `ReadFile`; küçük değişiklikte `ReplaceFileContent`, dosyanın %80'inden fazlası değişecekse `CreateOrUpdateFile`.
- Dosya okuma/yazma/arama için asla ham terminal komutu (`cat`, `grep`, `sed`...) kullanılmaz — bunun yerine yukarıdaki araçlar kullanılır.
- Kullanıcıya asla iç araç adları söylenmez; yalnızca yapılan eylem sade Türkçe ile anlatılır.
- Mimari belirsizlik varsa `CreatePlan`/kısa plan özeti ile kullanıcı onayı alınmadan koda geçilmez.
- Her düzenlemeden sonra sessizce `BuildProject` çalıştırılır; hata varsa kendi kendine en fazla 3 deneme, sonra `WebSearch → WebFetch` ile araştırma, hâlâ çözülmezse kullanıcıya net açıklama.
- TODO panelini güncellemek için model kendi sayaç tutmaz; `ReadingFile / EditingFile / CreatingFile / DeletingFile / RunningTerminal / Completed / Failed` event'lerini yayınlar, panel bunlardan otomatik ☐ → 🟡 → ✅ durumuna geçer.
- Her görev döngüsü sonunda kullanıcıya kısa bir özet (ne yapıldı / dikkat edilmesi gereken / sıradaki adım) sunulur.

---

## Arayüz ve Buton Referansı

### Üst araç çubuğu / proje paneli
| Buton | İşlev |
|---|---|
| `btnSelectFolder` ("Seç") | Üzerinde çalışılacak proje klasörünü seçer |
| `btnShowBackups` ("🗄️ Backups") | mdaiAgent'ın otomatik oluşturduğu yedekleri görüntüler |
| `btnGitHubSync` | Projeyi GitHub'a senkronize eder (push) |
| `btnGitRefresh` ("🔄 Refresh") | Git değişikliklerini/durumunu yeniler |
| `btnStageAll` | Tüm değişiklikleri stage'ler |
| `btnCommit` | Stage edilen değişiklikleri commit'ler |
| Sağ tık menüsü (git listesi) | `Stage` / `Unstage` |
| `btnSearchFiles` ("🔍") / `btnClearSearch` ("❌") | Dosya adında arama yapar / aramayı temizler |
| Dosya ağacı sağ tık menüsü | `🔄 Refresh`, `📄 New File`, `📁 New Folder`, `❌ Delete`, `🔍 Show in Explorer` |

### Kod editörü çubuğu
| Buton | İşlev |
|---|---|
| `btnSaveFile` ("💾") | Geçerli dosyayı kaydeder (Ctrl+S) |
| `btnSaveAll` ("💾+") | Açık tüm dosyaları kaydeder (Ctrl+Shift+S) |
| `btnFormatDocument` ("🧹") | Açık dosyayı otomatik biçimlendirir (boşluk/indent düzenler) |
| `btnQuickOpenFile` ("📂") | Hızlı dosya seç/aç penceresi açar |
| `btnRunApp` ("▶") | Uygulamayı seçili cihaz/hedefte çalıştırır |
| `btnHotReload` ("⚡") | Uygulamayı kapatmadan değişiklikleri canlı uygular |
| `btnStopApp` ("⏹") | Çalışan uygulamayı durdurur |
| `btnCommandPalette` | Komut paletini açar (Ctrl+P) — tüm komutları arayarak bulma |
| `btnSafeAutomation` ("Güvenli Mod") | Tüm agent işlemleri için onay ister; yanlışlıkları/hataları önler |
| `btnAiActions` ("AI Actions") + bağlam menüsü | `Dosyayı Refaktor Et`, `Yorumları Geliştir`, `Dosyayı Açıkla`, `Plan Modu Oluştur`, `Test Senaryosu Üret`, `Son AI Yanıtını Dosyaya Uygula` |
| `btnPlanMode` | Mevcut proje için adım adım uygulama planı hazırlar |
| `btnPluginManager` | Eklenti (dil analiz eklentisi) yükleme/yönetim penceresini açar |
| `btnAbout` ("ℹ") | mdaiAgent hakkında bilgi penceresi |

### Terminal paneli
| Buton | İşlev |
|---|---|
| `btnOpenTerminalPath` ("📂") | Terminal çalışma dizinini açar |
| `btnClearTerminal` ("🗑️") | Terminal çıktısını temizler |
| `btnKillProcess` ("🛑") | Çalışan işlemi sonlandırır |
| `btnRunCommand` ("▶️") | Yazılan komutu çalıştırır |

### RAG paneli
| Buton | İşlev |
|---|---|
| `btnRagSettings` | RAG (kod embedding/anlamsal arama) ayarlarını açar |
| `btnRagReindex` | Proje kodunu yeniden indeksler |

### Üst menü çubuğu
| Buton | İşlev |
|---|---|
| `btnTools` ("Araçlar") | Modelin kullanabileceği sistem araçlarını yönetir; otomatik/manuel mod seçimi |
| `btnAgents` ("Ajan Modülleri") | Özel görevler/test UI için uzmanlaşmış alt-ajanları yönetir |
| `btnKeyboardShortcuts` ("Kısayollar") | Program içi klavye kısayollarını gösterir |
| `btnSettings` ("Ayarlar") | API anahtarları (Gemini/Groq/vb.), AI Router ayarları ve tema seçimini yönetir |

### Sohbet paneli
| Buton | İşlev |
|---|---|
| `btnNewChatMain` / `BtnNewChat_Click` | Yeni sohbet başlatır |
| `ChatSessionRename_Click` / `ChatSessionDelete_Click` | Sohbet oturumunu yeniden adlandırır / siler |
| `btnBackToList2` ("←") | Sohbet listesine geri döner |
| `btnRenameCurrentChat` ("✏️") | Geçerli sohbeti yeniden adlandırır |
| `btnShowChatHistory` ("💬") | Sohbet geçmişini gösterir |
| `btnShowTimeline` ("📊") | Timeline ile AI çalışmasını (adım adım aksiyonları) izlemeyi sağlar |
| `btnShowTodoList` ("✅") | TODO görevlerini takip etmeyi sağlar |
| `btnShowFileChanges` ("📝") | Üzerinde değişiklik yapılan dosyaları gösterir |
| `btnClearHistory` ("🗑️") | Sohbet geçmişini temizler (proje ayarları korunur) |
| `btnAttach` ("📎") | Dosya ekler |
| `btnVoice` ("🎤") | Basılı tutarak konuş (push-to-talk) ile sesli komut girer |
| `btnSend` ("⏎") | Mesajı gönderir |
| `btnStop` ("🛑") | Devam eden AI yanıtını/aracı durdurur |
| `btnInlineEditCancel` / `btnInlineEditApply` | Satır içi (inline) AI düzenlemesini iptal eder / uygular (Enter) |

Bunlara ek olarak ayrı pencereler (`.xaml` + `.xaml.cs`) mevcuttur: `SettingsWindow`, `PluginManagerWindow`, `RagSettingsWindow`, `RouterBlacklistWindow`, `KeyboardShortcutsWindow`, `VoiceSettingsWindow`, `BackupViewerWindow`, `DiffWindow`, `PlanWindow`, `AskUserDialog`, `ConfirmCommandWindow`, `InputDialog`, `MessageDialog`, `PreviewWindow`, `LanguageSelectionWindow`, `AboutWindow`.

---

## Arka Planda Çalışan Servisler

`Services/` klasöründeki sınıflar, `ToolExecutor` tarafından çağrılan iş mantığını barındırır ve UI'dan bağımsızdır:

| Servis | Sorumluluk |
|---|---|
| `FileOperationsService` | Tüm dosya tabanlı işlemler: okuma, yazma, dizin listeleme |
| `SearchService` | Dosya arama ve kod arama işlemleri |
| `BuildService` | Derleme, test ve terminal komutu çalıştırma |
| `CommandService` | Hızlı komut oluşturma ve çalıştırma |
| `CheckpointService` | Checkpoint oluşturma/rollback (atomik işlemler için `CheckpointManager`'a devreder) |
| `PlanningService` | Plan oluşturma, yürütme ve proje hafızası işlemleri (`AiPlanGenerator`'a devreder — ana sohbetten izole) |
| `DelegationService` | Görev delegasyonu (subagent) ve proje bağlamı keşfi |
| `WebService` | Web arama, sayfa okuma ve ekran görüntüsü işlemleri |
| `UtilityService` | Kullanıcıya soru sorma, akıllı kurtarma ve çeşitli yardımcı görevler |
| `VoiceCommandService` | Mikrofon kaydı ve STT (konuşmadan metne) transkripsiyonu |
| `RagService` | Kod embedding'lerini, vektör aramayı ve AI promptu için bağlam getirmeyi yönetir |
| `CodeChunker` | Roslyn tabanlı semantik kod parçalama (RAG için sınıf/metot/özellik düzeyinde) |
| `ProjectWatcherService` | Proje dizinini izler, kod dosyası değişince RAG'ı otomatik yeniden indeksler |
| `Services/Router/*` (`GeminiRouter`, `IAiRouter`, `RouterContext`, `RouterDecision`) | Küçük modelle hangi araçların gerektiğine karar veren router mantığı |

Diğer kök seviyesi arka plan bileşenleri:

| Bileşen | Sorumluluk |
|---|---|
| `AgentCoreRuntime` / `IAgentCore` | UI'dan bağımsız agent runtime: tüm tool execution, planlama, checkpoint, delegasyon mantığının merkezi |
| `AgentVerificationLoopService` | Değişiklik sonrası build/test doğrulama döngüsünü (self-healing) yürütür |
| `ChatFlowService` / `IChatFlowService` | Sohbet akışını, tool-call döngüsünü ve mesajlaşmayı yönetir |
| `ChatSessionService` | Sohbet oturumlarının (yeni/sil/yeniden adlandır/geçmiş) yönetimi |
| `ContextOptimizerService` | Bağlam/istem boyutunu optimize eder (token tasarrufu) |
| `SubAgentResultCoordinator` | `DelegateTask` ile başlatılan alt-ajanların sonuçlarını koordine eder |
| `AiActionService` | "AI Actions" menüsündeki refaktör/yorum/açıklama/test üretme işlemlerini yürütür |
| `AiPlanGenerator` | Plan modu için ayrı, ana sohbetten izole bir plan üretim çağrısı yapar |
| `LanguageServerClient` / `LanguageServerService` | LSP (Language Server Protocol) entegrasyonu — kod tamamlama/hata denetimi |
| `GitService` | LibGit2Sharp tabanlı git işlemleri (stage/commit/push/durum) |
| `TerminalService` | Entegre terminal süreç yönetimi |
| `CheckpointManager` | Checkpoint dosyalarının atomik kaydı ve geri yüklenmesi |
| `ProcessQueue` | Arka plan süreçlerinin sıraya alınması |
| `TokenTrackerService` | Token kullanımı/maliyet takibi |
| `NotificationService` / `ToastService` | Bildirim ve toast mesajları |
| `ProjectContextDiscoveryService` | Proje türü/teknolojisini ve önemli dosyaları keşfeder |
| `DependencyInstaller` | Eksik bağımlılıkların tespiti/kurulumu |
| `SecretStore` | API anahtarları gibi hassas verilerin güvenli saklanması |
| `Logger` | Uygulama genelinde loglama (`mdai_YYYYMMDD.log` dosyaları) |

---

## Eklenti (Plugin) Sistemi

`PluginSystem.cs`, `ILanguageErrorCheckerPlugin` arayüzü üzerinden **dile özel statik hata denetleyicileri** tanımlar. Her eklenti:

- Bir kimlik (`Id`), isim, desteklediği dosya uzantıları, versiyon ve açıklamaya sahiptir,
- `GetDiagnostics(filePath, content)` ile dosya için tanı (uyarı/hata) listesi döndürür,
- `IsBuiltIn` ile yerleşik mi yoksa dışarıdan yüklenmiş bir eklenti mi olduğunu belirtir.

`PluginManifest` eklenti meta verisini (JSON) tanımlar ve `DownloadUrl` ile dışarıdan indirilebilir eklentilere de izin verir. Roslyn (`Microsoft.CodeAnalysis.CSharp`) kullanılarak C# kodu üzerinde analiz yapılabilir. Eklentiler **`btnPluginManager`** üzerinden açılan `PluginManagerWindow` ile yüklenir/yönetilir.

---

## Proje Anayasası ve Hafıza

mdaiAgent, her görevden önce proje kökünde şu iki dosyayı arar ve varsa okur:

- **`.mdai/constitution.md`** — projeye özel mimari/kodlama kuralları (repo içindeki örnek şablon: `constitution.md`). İçinde AI persona seçimi (Expert / Mentor modu), evrensel kurallar (yer tutucu kullanma, belirsizlikte soru sor, gerçek dizin yapısını kullan) ve projeye özel teknoloji/kodlama-stili/dokunulmaz-alan tanımları bulunur. Bu dosya varsa kurallarına **kesinlikle** uyulur.
- **`.mdai/memory.md`** — agent'ın kritik mimari kararları, çözülen zor hataları, doğrulanmış gerçekleri (paket/model adları vb.) ve kullanıcı düzeltmelerini "Tarih + Konu + Sonuç" formatında kaydettiği kalıcı not defteri.

---

## Çoklu Dil Desteği (i18n)

Arayüz metinleri `Resources/Strings.resx` (varsayılan) ve `Resources/Strings.en.resx` (İngilizce) kaynak dosyalarında tutulur; `LocExtension` ve `Localization` / `LocalizationManager` sınıfları XAML içinde `{local:Loc Key=...}` söz dizimiyle çalışma zamanında dil seçimine göre metni çözer. Dil seçimi `LanguageSelectionWindow` üzerinden yapılır ve `AppSettings.Language` içinde saklanır.

---

## Gizlilik ve Telemetri

Yengi, kullanıcı gizliliğine ve şeffaflığa büyük önem verir.

### Toplanan Anonim Veriler:
Uygulama açıldığında, yalnızca geliştirme süreçlerini ve aktif kullanımı takip edebilmek amacıyla aşağıdaki **anonim** veriler toplanır:
- Rastgele oluşturulmuş benzersiz cihaz kimliği (`InstallationId` GUID)
- İşletim sistemi sürümü (örn. `Microsoft Windows 10.0.22631`)
- Yengi uygulama sürümü (örn. `v1.0.0`)
- İlk ve son görülme zaman damgası

### 🛡️ KESİNLİKLE TOPLANMAYAN VERİLER:
- ❌ Ad, soyad, e-posta veya kimlik bilgileri
- ❌ IP adresi veya konum bilgisi
- ❌ Kodlarınız, proje dosyalarınız veya dosya içerikleriniz
- ❌ AI modelleriyle yaptığınız sohbetler ve komutlar

### ⚙️ Telemetriyi Kapatma (Opt-Out):
Dilediğiniz zaman telemetri gönderimini tamamen kapatabilirsiniz:
1. **Ayarlar (Settings)** penceresini açın.
2. **🔒 Gizlilik & Telemetri** bölümündeki **"Anonim kullanım istatistiklerini paylaş (Telemetri)"** kutucuğunun işaretini kaldırın.
3. **Kaydet** butonuna basın.

---

## Ayarlar

`SettingsWindow` üzerinden yönetilen başlıca ayarlar (`AppSettings`):

- **Sağlayıcı seçimi**: `ActiveProvider` (ApiService / LocalModel / Anthropic / Google / OpenAI) ve her biri için ayrı API anahtarı, taban URL ve model adı.
- **Genel**: API zaman aşımı süresi, varsayılan sistem promptu, otomatik kaydetme (açık/kapalı + aralık).
- **Alt ajanlar**: `QaAgentEnabled`, `UiAgentEnabled` bayrakları.
- **Entegrasyonlar**: `UploadEndpoint`, `TavilyApiKey` (web arama), `GitHubUsername` / `GitHubToken`.
- **Sesli Komut (STT)**: ayrı API anahtarı/URL/model (varsayılan Groq Whisper).
- **RAG (Embedding)**: ayrı API anahtarı/URL/model, `RagEnabled` açma/kapama.
- **Araç Yönetimi**: `DisabledTools`, `RouterBlacklistedTools`, `IsManualToolManagement` (otomatik/manuel araç seçimi).
- **AI Router**: `RouterEnabled`, `RouterUseMainModel`, `RouterConfidenceThreshold`, ayrı router taban URL/model/API anahtarı.

Hassas veriler (API anahtarları vb.) `SecretStore.cs` üzerinden saklanır.

---

## Kurulum ve Çalıştırma

**Gereksinimler:** Windows, .NET 8 SDK (`net8.0-windows`, WPF).

Başlıca NuGet bağımlılıkları (`mdaiAgent.csproj`):

| Paket | Amaç |
|---|---|
| `AvalonEdit` | Kod editörü (sözdizimi vurgulama vb.) |
| `LibGit2Sharp` | Git entegrasyonu |
| `Microsoft.CodeAnalysis.CSharp` | Roslyn tabanlı kod analizi / RAG chunking |
| `Microsoft.Web.WebView2` | Gömülü web görünümü (ör. önizleme) |
| `NAudio` | Ses kaydı (sesli komut) |
| `StreamJsonRpc` | LSP (Language Server Protocol) iletişimi |

```bash
git clone <repo-url>
cd mdaiAgent
dotnet restore
dotnet build
dotnet run --project mdaiAgent
```

İlk açılışta **Ayarlar** penceresinden en az bir AI sağlayıcı için API anahtarı girilmesi (ya da yerel bir model — Ollama/LM Studio — adresinin tanımlanması) gerekir.

---

## Proje Yapısı

```
mdaiAgent/
├── MainWindow.xaml(.cs)          # Ana pencere (UI kompozisyonu)
├── MainWindow.ChatPanel.cs       # Sohbet paneli mantığı (partial class)
├── MainWindow.Terminal.cs        # Terminal paneli mantığı (partial class)
├── MainWindow.RunDebug.cs        # Çalıştır/Hot Reload/Durdur mantığı (partial class)
├── MainWindow.SessionRecovery.cs # Oturum kurtarma (partial class)
├── AgentCoreRuntime.cs / IAgentCore.cs
├── ChatFlowService.cs / IChatFlowService.cs
├── ToolDefinitions.cs            # Araç şemaları + sistem promptu
├── ToolExecutor.cs               # Araç yürütme mantığı
├── PluginSystem.cs                # Eklenti arayüzü ve yönetimi
├── GitService.cs, TerminalService.cs, CheckpointManager.cs, SecretStore.cs, Logger.cs, ...
├── Interfaces/                    # IAiProvider, IProviderService
├── Providers/                     # Anthropic / Gemini / Nvidia / OpenAI-uyumlu istemciler, factory, fallback
├── Services/                      # FileOperations, Search, Build, Command, Checkpoint,
│   │                              #   Planning, Delegation, Web, Utility, Voice, RAG, CodeChunker, ProjectWatcher
│   └── Router/                    # Router tabanlı akıllı araç seçimi (GeminiRouter, RouterContext/Decision)
├── Resources/                     # Strings.resx / Strings.en.resx (i18n)
├── *.xaml(.cs)                    # Ayarlar, Eklenti Yöneticisi, RAG Ayarları, Router Kara Liste,
│                                  #   Klavye Kısayolları, Ses Ayarları, Yedek Görüntüleyici, Diff,
│                                  #   Plan, Kullanıcıya Sor, Komut Onayı, Girdi/Mesaj Diyalogları, Önizleme,
│                                  #   Dil Seçimi, Hakkında
└── constitution.md                # Proje anayasası şablonu (embedded resource olarak da paketlenir)
```

---

## Bilinen Kısıtlar / Yol Haritası

- `MainWindow.xaml.cs` çok büyük (~28K satır) ve `MainWindow` sınıfı UI ile iş mantığını bir arada tutan bir "god-object" örüntüsüne sahip; işlevsellik kısmen partial dosyalara (`MainWindow.ChatPanel.cs`, `MainWindow.Terminal.cs` vb.) bölünmüş olsa da tam bir MVVM ayrımı henüz tamamlanmamıştır.
- `IAiProvider` soyutlaması mevcuttur ancak bazı sağlayıcıya özgü mantığın UI/servis katmanına sızma riski değerlendirilmelidir.
- `EventBus` (agent olay yayını — TODO panelini besleyen event akışı) için unsubscribe mekanizmasının gözden geçirilmesi gerekmektedir.
- Dosya işlemlerinde path güvenliği (path traversal vb.) sağlamlaştırılmalıdır.
- Offline/gizlilik odaklı kullanım (Ollama/LM Studio üzerinden tam yerel çalışma) bir farklılaşma/geliştirme alanı olarak değerlendirilmektedir.

---

*Bu README, mdaiAgent'ın kaynak kodu (WPF/.NET 8, ~100+ dosya) taranarak otomatik olarak derlenmiştir. Kod tabanı değiştikçe güncellenmesi önerilir.*
