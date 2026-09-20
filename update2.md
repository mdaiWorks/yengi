# mdaiAgent — UPDATE.md (v2)
## Milestone: `0.9 — Agent Hardening`

> **Hedef cümle:** "mdaiAgent artık sadece kod yazabilen bir AI değil, yaptığı değişikliklerin arkasında durabilen ve kontrolü her zaman kullanıcıda bırakan bir AI geliştirme agent'ı."

**Bu doküman v1'in düzeltilmiş hâlidir.** İlk versiyonda birkaç madde için yanlış arama terimleri kullanıldığı için ("hash/staleness" ararken kod `PATCH_CONFLICT` diye geçiyordu, "Services/" altında ararken bazı dosyalar kök dizindeydi) bazı maddeler "eksik" olarak işaretlenmişti — halbuki zaten yapılmıştı. Bu sürüm, doğru terimlerle yeniden, satır referanslarıyla doğrulanmıştır.

---

## 0. Doğrulanmış olarak ZATEN TAMAMLANMIŞ maddeler (v1'de yanlışlıkla "eksik" denmişti)

| Madde | Kanıt | Not |
|---|---|---|
| **Patch / stale-file koruması** | `Services/FileOperationsService.cs:190` → `"PATCH_CONFLICT: Hedef içerik dosyada bulunamadı; dosya değişmiş olabilir..."` | Hedef metin güncel dosyada yoksa dosyaya hiç yazmadan reddediyor, modele `ReadFile` ile yeniden okuyup patch'i güncel bağlama göre üretmesini söylüyor. Ayrıca boş/whitespace-only hedef metin de ayrıca reddediliyor (`FileOperationsService.cs:177`). |
| **Context Engine önceliklendirmesi** | `ProjectContextDiscoveryService.cs` → `ExtractKeywords`, `FindRelevantFilesAsync(keywords, prioritizedFiles)`, `IsImplementationTestPair(...)` | Mesajdan keyword çıkarma, kullanıcının açıkça belirttiği dosyaya öncelik (`prioritizedFiles`), ve implementation↔test dosya eşleştirmesi (`OrderService.cs` → `OrderServiceTests.cs`) zaten kodda. v1'deki "Context Engine önceliklendirmesi yok" maddesi geçersiz. |
| **LSP navigasyonu (F12 / Shift+F12 / Ctrl+T)** | `MainWindow.xaml.cs:7581-7737` → `GoToDefinitionAsync`, `FindReferencesAsync`, `ShowDocumentSymbolsAsync` | Bu, v1'de hiç değerlendirilmemiş ama README'de iddia edilmiş bir özellikti — kontrol edildi, gerçek. |
| **Diagnostics/Tanılama paneli** | `MainWindow.xaml` + `.xaml.cs` içinde gerçek UI | LSP + plugin diagnostics dosya/satır/mesaj listesi olarak gösteriliyor. |
| **Router confidence normalizasyonu + 25s timeout** | `SettingsWindow.xaml.cs:90` (`GetNormalizedRouterConfidenceThreshold`), `Services/Router/AiRouter.cs:28` (`RouterTimeout = TimeSpan.FromSeconds(25)`) | `0.8` / `0,8` / `80` gibi farklı girişleri normalize ediyor. |
| **RAG stale chunk temizliği** | `Services/RagService.cs:216` → `"Removed {removed} stale RAG chunks for deleted file"` | Silinen dosyaların eski embedding'leri index'ten çıkarılıyor. |

Bu maddeler artık gündemden düşüyor — aşağıdaki liste yalnızca **gerçekten hâlâ açık olan** işleri içerir.

---

## 1. 🥇 Cancellation Threading + Loop-Guard — hâlâ gerçek bir gap

**Doğrulama (bu kez geniş, doğru terimlerle tekrar arandı):** `CancellationToken` şu servislerin **hiçbirinde yok** (0 sonuç, doğrulandı):
`FileOperationsService`, `PlanningService`, `WebService`, `DelegationService`, `RagService`, `CheckpointService`, `SearchService`, `ProjectWatcherService`, `CodeChunker`, `UtilityService`, `CommandService`.

`CancellationTokenSource` UI tarafında (`MainWindow.ChatPanel.cs`, `BuildService.cs`, `ProcessQueue.cs`) var, ama bu token yukarıdaki servislere hiç ulaşmıyor — Stop butonu bu servislerden biri çalışırken basılırsa iş kendiliğinden bitene kadar duruyor.

`maxSameToolRetry` / loop-guard (aynı tool'un art arda başarısız çağrılmasını kesen bir mekanizma) da hâlâ **yok** — `ChatFlowService.cs`'de böyle bir sayaç/kontrol bulunamadı. (`maxIterations=10` ve `MaxAutoFixAttempts=2` zaten var, bu ayrı ve ek bir koruma katmanı.)

**Yapılacaklar:** (v1'deki plan aynen geçerli)
1. Listedeki 11 servisin async metotlarına `CancellationToken cancellationToken = default` parametresi ekle.
2. `ChatFlowService`'te son N tool çağrısını (isim + argüman hash'i) tutan basit bir sayaç ekle; aynı tool aynı argümanla `maxSameToolRetry` (örn. 3) kez üst üste başarısız olursa döngüyü kır.

---

## 2. Tool Output Store'u Genişlet — mekanizma hazır, kapsam dar

**Doğrulama:** `Services/ToolOutputStore.cs` + `ReadToolOutput` tool'u tam çalışır durumda, ama `SaveIfTruncated` çağrısı **hâlâ sadece `Services/BuildService.cs`'de** var (tekrar arandı, tek sonuç). Terminal (`CommandService`), test çıktısı, web search/fetch sonuçları bu mekanizmayı kullanmıyor.

**Yapılacaklar:** `BuildService.cs`'deki `SaveIfTruncated` + `[FULL_OUTPUT_ID:...]` deseni `CommandService` ve `WebService`'e kopyala. Yeni bir tasarım gerekmiyor, var olanı 2 yere daha bağlamak yeterli.

---

## 3. Verification Özetini Kullanıcıya Görünür "İmza" Formatına Çevir

**Doğrulama:** `AgentVerificationLoopService.cs`'de `BuildTestResults` (adım + başarı + hata) ve `ErrorSummary` zaten toplanıyor — veri tarafı tam. Ama bunu kullanıcıya `✓ N dosya incelendi / ✓ Build başarılı / ✓ Statik analiz temiz` şeklinde sabit formatlı bir "imza" olarak sunan bir render adımı bulunamadı.

**Yapılacaklar:** `ChatFlowService`'te final yanıt oluşturulmadan hemen önce, zaten var olan `result.BuildTestResults` listesinden sabit formatlı bir özet string'i üret ve yanıtın sonuna ekle. Bu salt string formatting — yeni veri toplamaya gerek yok.

---

## 4. MainWindow Büyümesini Durdur (yeni kod için Controller ayrımı)

**Doğrulama:** `Controllers/` klasörü ya da `ChatController`/`AgentController` gibi bir isimlendirme projede yok (aranıp doğrulandı). README'nin kendisi de bunu Bölüm 27/29'da açıkça kabul ediyor ("UI ayrıştırması devam ediyor").

**Yapılacaklar:** (v1'deki plan aynen geçerli, düşük riskli)
1. Mevcut `MainWindow.*.cs` dosyalarına dokunma.
2. `Controllers/ChatController`, `AgentController`, `EditorController`, `TerminalController`, `GitController`, `SessionController`, `PluginController` iskeletini oluştur.
3. Kural: bundan sonraki her yeni özellik ilgili controller'a gitsin, `MainWindow` sadece ona ince bir çağrı yapsın.

---

## 5. Release Temizliği — hâlâ yapılmamış, en kolay kazanç

**Doğrulama:** Proje kökünde hâlâ **9 dosya** duruyor: `MainWindow.xaml.cs.bak`, `MainWindow.ChatPanel.cs.bak`, `ChatFlowService.cs.bak`, `ToolExecutor.cs.bak`, `App.xaml.cs.bak`, `SettingsWindow.xaml.cs.bak`, `PlanWindow.xaml.cs.bak`, `NvidiaApiClient_lines.txt`, `NvidiaApiClient_numbered.txt`.

`Services/PlanningService.cs`'de `WriteDecisionAsync` hâlâ `List<object>` kullanıyor (satır 447-455) — tip güvenliği hâlâ zayıf, bozuk bir JSON dosyası kararların sessizce kaybolmasına yol açabilir.

**Yapılacaklar:**
1. 9 dosyayı sil, `.gitignore`'a `*.bak`, `*_lines.txt`, `*_numbered.txt`, `bin/`, `obj/`, `.vs/` ekle.
2. `DecisionRecord { string Date, Topic, Decision, Reason }` modeli tanımla, `List<object>` yerine `List<DecisionRecord>` kullan.
3. `CHANGELOG.md`, `VERSION`, `LICENSE` dosyalarını oturt.

---


6. Chat Panel 2.0 — mevcut motoru tek, doğal bir yüzeye topla

Genel teşhis (ChatGPT'nin analizinden, koddan doğrulanmış): Sorun özellik eksikliği değil — backend zaten @mention, attachment, streaming, tool activity, timeline, todo, file changes, retry, edit gibi 20+ özelliği destekliyor. Sorun bunların kullanıcıya tek bir pürüzsüz yüzey olarak hissettirilmemesi; şu an her biri ayrı bir kontrol/panel/sekme olarak duruyor.

6.1 Composer'ı tek parçaya indir

Doğrulama: Şu an input alanı (btnSend, MainWindow.xaml:3014) ve yanındaki 🗑️/📎/🎤 butonları dikey, ayrı bir toolbar olarak duruyor — tek bir "AI çalışma alanı" hissi vermiyor.
Yapılacaklar: Tüm composer'ı (attachment chip'leri + metin alanı + alt araç çubuğu: +, 📎, @, model seçici, Plan, 🎤, gönder) tek bir kart/border içinde, üstte context/attachment chip'leri, altta ince bir araç çubuğu şeklinde yeniden düzenle.

6.2 @mention'ı chip sistemine çevir

Doğrulama: @mention popup'ı zaten çalışıyor (MainWindow.ChatPanel.cs: ShowAtMentionPopup, popupAtMention, lstAtMentions, _mentionedFiles) — mekanizma sağlam. Ama seçilen dosyalar mesajın üstüne düz metin olarak yazılıyor: "[Ekli Dosyalar: {dosya1, dosya2}]" (MainWindow.ChatPanel.cs:2281).
Yapılacaklar: _mentionedFiles listesini düz metin yerine composer üstünde silinebilir chip'ler ([ Foo.cs ✕ ]) olarak render et. Yeni bir seçim mekanizması gerekmiyor, sadece gösterim katmanı değişiyor — _mentionedFiles zaten var olan veri kaynağı.

6.3 Context selector ekle

Doğrulama: Şu an context seçimi kullanıcıya görünür değil — RAG/proje keşfi arka planda otomatik çalışıyor (ProjectContextDiscoveryService, RagService), kullanıcının "hangi bağlam kullanılıyor" üzerinde görünür kontrolü yok.
Yapılacaklar: Composer'ın alt araç çubuğuna [Auto ▾] bir context seçici ekle: Auto / Current File / Current Selection / Project / RAG. Backend zaten bu kaynakların hepsini besliyor — bu sadece kullanıcıya hangisinin aktif olduğunu seçme/görme hakkı veriyor.

6.4 Model seçiciyi composer'a taşı

Doğrulama: Model seçimi şu an sadece SettingsWindow'da (ActiveProvider, RouterModel vb.) — MainWindow.xaml'de composer'a yakın bir model seçici yok (aranıp doğrulandı, sıfır sonuç).
Yapılacaklar: Composer'a küçük bir [Auto ▾] / [deepseek-v4 ▾] seçici ekle. Router açıksa varsayılan "Auto" olsun, kullanıcı Router'ın ne seçtiğini düşünmek zorunda kalmasın — Settings'teki tam kontrol de kalsın, bu sadece hızlı erişim.

6.5 Agent activity'yi chat akışının içine sız

Doğrulama: Tool çağrıları (ReadFile, BuildProject vb.) şu an sadece terminal log paneline ve ayrı Timeline/Todo sekmelerine yazılıyor — chat mesajının kendisi içinde görünmüyor (AddChatMessage akışında tool-call kartı yok, aranıp doğrulandı).
Yapılacaklar: AI mesajının altına, küçük ve katlanabilir bir "🧠 Working" durum kartı ekle (✓ N dosya incelendi / ● şu an X dosyasını düzenliyor / ○ testler bekliyor). Timeline/Todo/File Changes sekmeleri kalsın — bu kart sadece özet, "Detaylar" ile tam panele geçilebilsin. Veri zaten mevcut event akışından (ReadingFile/EditingFile/RunningTerminal/Completed event'leri) besleniyor, yeni bir veri kaynağı gerekmiyor.

6.6 Otomatik chat başlığı

Doğrulama: Yeni sohbetler "Yeni Sohbet" olarak kalıyor, otomatik başlık üretimi yok (aranıp doğrulandı — GenerateTitle/AutoTitle gibi hiçbir şey yok).
Yapılacaklar: İlk kullanıcı mesajından sonra, ayrı ve ucuz bir çağrı ile (ana sohbet akışını şişirmeden — AiPlanGenerator'ın plan üretimini izole ettiği yöntemin aynısı) kısa bir başlık üret, ChatSession.Name'i güncelle. Kullanıcı hâlâ elle ✏️ ile değiştirebilsin.

Kod bloğu / mesaj action'ları — küçük ek not

Doğrulama: Kod bloklarında zaten Kopyala + Editörde Aç var (MainWindow.ChatPanel.cs:4145-4207), AI mesajlarında Copy/Retry/Continue var. Eksik olan tek şey: kod bloğu varsa "Apply to file" contextual action'ı — global "Son AI Yanıtını Uygula" komutu var ama kod-bloğu-özel, diff-önizlemeli bir buton yok.
Yapılacaklar: CreateCodeBlockParagraph'a (satır 4097) üçüncü bir buton ekle: "Uygula" — tıklanınca DiffWindow'u (zaten var olan bileşen) açıp o kod bloğunu ilgili dosyaya uygulama önizlemesi göstersin.

Öncelik notu: Bunların içinde en yüksek etkiyi 6.1 (composer birleştirme) ve 6.2 (chip'e çevirme) verir — ikisi de görsel/etkileşim değişikliği, backend'e dokunmuyor, düşük risk. 6.5 (agent activity) en çok işi gerektiren ama en çok "Cursor hissi" katan madde.

Referans: Trae.ai composer/activity:
"Composer tasarımı: tek kart içinde metin alanı + altında ince araç çubuğu (+, onay modu dropdown'u, @ Agent mod seçici, model seçici, mikrofon, gönder). Tool çağrıları chat akışının içinde tek satırlık özet olarak gösterilsin (Read 1 file, Searched files 2 times ›, tıklanınca genişler) — ayrı bir terminal paneline yönlendirme. Todo ilerlemesi composer'ın hemen üstünde ince, katlanabilir bir bar olarak (0/6 tasks Completed), ayrı bir sekme değil. Mesaj balonları hafif — ağır border/kutu kullanma, sadece kullanıcı mesajı belirgin bir balon olsun."

## Öncelik Sıralaması (v2, kısaltılmış)

| Sıra | Madde | Durum | Efor |
|---|---|---|---|
| 1 | Cancellation threading + loop-guard | Gerçek gap, doğrulandı | Orta |
| 2 | Chat Panel 2.0 | Backend hazır, sadece sunum | Orta-Yüksek |
| 3 | Tool output store'u terminal/test/web'e genişlet | Mekanizma hazır, sadece bağlanacak | Düşük |
| 4 | Verification özet formatı | Veri hazır, sadece format | Düşük |
| 5 | Controller ayrımı (yeni kod için) | Yok, mevcut koda dokunmuyor | Düşük |
| 6 | Release temizliği + decisions.json tip güvenliği | Yok | Çok düşük |




~~Patch güvenilirliği~~ ve ~~Context Engine önceliklendirme~~ v1'de listede en üstteydi — **ikisi de zaten tamamlanmış**, listeden çıkarıldı.

---

## Standart Demo Senaryosu (değişmedi, hâlâ geçerli)

```text
Boş bir klasörde:
"Basit bir TODO API projesi oluştur (C# .NET 8 minimal API).
Task modeli, in-memory repository, CRUD endpoint'leri ekle.
Bilerek kullanılmayan bir alan bırak.
Tasarım kararını kaydet.
Sonra Task modeline yeni bir alan ekle (elle dosyayı değiştirdikten hemen sonra
aynı isteği tekrar ver — patch conflict senaryosunu tetiklemek için).
İşlem bitince Stop'a bas, gerçekten her şeyin durduğunu doğrula."
```

Bu senaryo artık şunları test eder: dosya oluşturma, statik analiz, `WriteDecision`, **zaten çalışan** patch conflict koruması (doğrulama amaçlı), ve **hâlâ eksik olan** cancellation (madde 1) ile verification özeti (madde 3).

---

*Bu doküman, mdaiAgent kaynak kodunun doğrudan taranmasıyla hazırlanmıştır; v1'e göre fark, aynı iddiaların bu kez doğru arama terimleriyle yeniden doğrulanmış/düzeltilmiş olmasıdır.*