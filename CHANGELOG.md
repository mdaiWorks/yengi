# mdaiAgent — Değişiklik Günlüğü

Tüm önemli değişiklikler bu dosyada belgelenir.

Format: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/)
Sürümleme: [Semantic Versioning](https://semver.org/lang/tr/)

---

## [0.9.0] — 2026-09-08
### Agent Hardening — "mdaiAgent artık yaptığı değişikliklerin arkasında durur"

### ✅ Eklenenler

#### 1. Cancellation Threading + Loop-Guard
- **CancellationToken desteği:** SearchService, ProjectWatcherService, UtilityService, CommandService, VoiceCommandService, CodeChunker gibi 6 eksik servisin async metotlarına `CancellationToken cancellationToken = default` parametresi eklendi. Stop butonu artık uzun süren servis işlemlerini (dosya arama, RAG indeksleme, komut yürütme, ses metne çevirme) anında iptal edebiliyor.
- **Loop-Guard mekanizması:** ChatFlowService içinde aynı tool'un aynı argümanlarla art arda `maxSameToolRetry = 3` kez başarısız olması durumunda sonsuz döngüyü kıran bir koruma eklendi. Sayaç `toolFailureStreak` sözlüğü ile izlenir, başarılı bir çağrı sayacı sıfırlar. Tetiklenince kullanıcıya anlaşılır bir uyarı mesajı gösterilir.

#### 2. Tool Output Store Genişletmesi
- `Services/ToolOutputStore.cs` içindeki `SaveIfTruncated` + `[FULL_OUTPUT_ID:...]` deseni yalnızca BuildService'te değil, artık **CommandService** (hızlı komutlar) ve **WebService** (WebSearch/WebFetch) içinde de kullanılıyor. Uzun çıktılar otomatik olarak `%TEMP%\mdaiAgent\tool-output` altında spool dosyasına yazılıyor, AI `ReadToolOutput` tool'u ile istediği satır aralığını okuyabiliyor.

#### 3. Verification İmza Formatı
- ChatFlowService sonunda AI'ın ürettiği final yanıtın **sonuna sabit formatlı bir doğrulama imzası** eklendi. Örnek çıktı:
  ```
  ---
  🧪 Doğrulama: 3 dosya · ✅ 4 başarılı, 1 uyarı
  ✓ Changed Files Review
  ✓ Initial Build
  ✓ Tests After Build Fix
  ✓ Static Analysis
  ⚠️ Initial Tests
  📝 Ayrıntı: xUnit 2 testten 1'i geçti, 1 uyarı...
  ```
- İmza, `VerificationResult.BuildTestResults` ve `ErrorSummary` listelerinden `FormatVerificationSignature` yardımcı metodu ile üretilir. Hem normal akışta hem de QA/UI Agent pipeline sonrasında eklenir.

#### 4. Controller Ayrımı (yeni kod için)
- **Controllers/** klasörü oluşturuldu. Gelecekteki her yeni özellik, ilgili controller sınıfına yazılacak:
  - `ChatController` — Chat panel olayları
  - `AgentController` — Plan Mode, verification, delegation koordinasyonu
  - `EditorController` — Kod editörü, LSP navigasyonu, diff
  - `TerminalController` — Terminal paneli, komut, build/test
  - `GitController` — Git entegrasyonu, stage/commit/push
  - `SessionController` — Chat oturumları, arşivleme
  - `PluginController` — Plugin/LSP yükleme ve yönetimi
- Mevcut `MainWindow.*.cs` partial class'larına dokunulmadı; yeni kod artık controller'lara gidecek.

#### 5. Release Temizliği + Tip Güvenliği
- **9 adet artık/dosya silindi:** `*.bak` yedekleri (MainWindow, ChatFlowService, ToolExecutor, App, SettingsWindow, PlanWindow) ve `NvidiaApiClient_*.txt` döküm dosyaları kaldırıldı.
- **`.gitignore` güncellendi:** `*.bak`, `*_lines.txt`, `*_numbered.txt`, `bin/`, `obj/`, `.vs/` artık takip dışı.
- **`DecisionRecord` record modeli:** PlanningService içindeki `WriteDecisionAsync` metodu artık `List<object>` yerine tip güvenli `List<DecisionRecord(Date, Topic, Decision, Reason)>` kullanıyor. Bozuk bir JSON dosyası kararların sessizce kaybolmasına neden olmuyor (deserialization toleranslı).
- **`MemorySearchResult` record modeli:** `SearchProjectMemoryAsync` içindeki anonim nesneler yerine `List<MemorySearchResult(Category, Key, Value)>` kullanılıyor.
- **Standart release dosyaları:** `VERSION`, `LICENSE` (AGPL-3.0), `CHANGELOG.md` oturtuldu.

### 🛡️ Önceden Var Olan, Doğrulanmış Özellikler
v1 UPDATE'de yanlışlıkla "eksik" olarak işaretlenen ancak zaten çalışan özellikler:
- Patch/stale-file koruması (PATCH_CONFLICT)
- Context Engine önceliklendirmesi (keyword çıkarma, implementation↔test eşleştirme)
- LSP navigasyonu (F12 / Shift+F12 / Ctrl+T)
- Diagnostics/Tanılama paneli
- Router confidence normalizasyonu + 25s timeout
- RAG stale chunk temizliği

### Önemli Not
- `MainWindow.xaml.cs` içindeki XAML eleman referans hataları (CS0103) bu sürümde bilinen bir durumdur ve bu hardening fazının kapsamı dışındadır (UI partial'ları XAML ile tutarsız).
- Chat Panel 2.0 (madde 6) görsel yeniden tasarımı bir sonraki faz için planlanmıştır.
