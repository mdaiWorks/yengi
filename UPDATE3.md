# mdaiAgent — UPDATE.md (v3)
## Milestone: `0.9 — Agent Hardening` → durum: neredeyse tamamlandı

Bu sürüm v2'nin devamıdır. v2'deki **5 maddenin 5'i de** ve ayrıca "Chat Panel 2.0" başlığı altındaki **6 alt maddenin 6'sı da** kod üzerinden tek tek doğrulandı — hepsi gerçek ve doğru çalışıyor. Bu dosya artık kısa: sadece bu oturumda **yeni bulunan, hâlâ açık** maddeleri içeriyor.

---

## 0. Tamamlanmış olarak doğrulanmış maddeler (referans için, artık gündemde değil)

| Madde | Kanıt |
|---|---|
| CancellationToken (11 servis) | Hepsinde kullanım var, sıfır kalmadı |
| Loop-guard | `maxSameToolRetry = 3`, `loopGuardTriggered` gerçek |
| Tool output store genişletmesi | `WebService`, `CommandService`, `BuildService` hepsi kullanıyor |
| Verification "imza" formatı | `FormatVerificationSignature` — ✅/⚠️/❌ + özet |
| Controller ayrımı | 7 controller gerçek (`Controllers/*.cs`) |
| Release temizliği | `.bak`/`.txt` dosyaları sıfır |
| `decisions.json` tip güvenliği | `List<DecisionRecord>` |
| @mention → chip | `icMentionedChips`, `MentionedFileViewModel` |
| Context selector (UI) | `cmbContextMode` (Auto/Dosya/Seçim/Proje/RAG) |
| Model seçici composer'da | `cmbComposerModel` |
| Agent activity inline kart | `"🧠 Working (N işlem)"` |
| Kod bloğu "Uygula" butonu | `"Uygula ↓"` |
| Todo bar composer üstünde | `todoProgressBar`, katlanabilir |
| Otomatik chat başlığı | `ChatFlowService.cs:718` |
| Yerel Router (Ollama + `mdai-router.gguf`) | `RouterUseLocalModel` → `localhost:11434/v1` |

---

## 1. 🥇 Context Selector Doğruluğu — UI'ın vaat ettiğiyle arka planda olan uyuşmuyor

Bu madde 1 numaraya alınmayı hak ediyor çünkü ikisi de aynı köke iniyor: **kullanıcı bir seçenek seçiyor, sistem sanki onu yapmış gibi davranıyor ama gerçekte yapmıyor.** Bu bir performans sorunu değil, bir **güven** sorunu — tıpkı Cancellation/loop-guard'ın "Stop gerçekten durduruyor mu" sorunu gibi.

### 1.1 "Dosya" modu — tüm dosyayı gönderiyor, kırpmıyor

**Doğrulama:** `MainWindow.ChatPanel.cs:2346`
```csharp
apiMessage += $"\n\n[BAĞLAM: Aktif Dosya ({dosyaAdı})]\n{ctxEditor.Text}";
```
`ctxEditor.Text` = dosyanın **tamamı**. 3.000 satırlık bir dosyada "şu metodu düzelt" gibi bir istekte bile dosyanın tamamı gönderiliyor — hem gereksiz token maliyeti hem de modelin dikkatinin dağılması riski.

**Yapılacaklar:**
- Kullanıcının mesajından (veya imleç konumundan) hedef sembolü/metodu tespit et.
- Şunu gönder: hedef sembol + ±50 satır bağlam + varsa interface tanımı + varsa eşleşen test dosyası (`ProjectContextDiscoveryService.IsImplementationTestPair` zaten bunu yapıyor, buraya bağlanabilir) — dosyanın tamamı değil.
- Dosya küçükse (örn. <200 satır) tamamını göndermek zaten sorun değil — kırpma mantığı satır sayısına göre devreye girsin.

### 1.2 "RAG" modu — gerçek arama yapmıyor, sadece talimat metni gönderiyor

**Doğrulama:** `MainWindow.ChatPanel.cs:2364`
```csharp
apiMessage += "\n\n[BAĞLAM TALİMATI: Cevabı üretirken RAG anlamsal aramasını önceliklendir.]";
```
Bu, `RagService.SearchAsync` gibi gerçek bir retrieval çağrısı **değil** — modele "RAG kullanmış gibi davran" diyen bir metin. Model gerçekte hiçbir embedding araması yapmadan, "RAG sonucuymuş gibi" bir cevap üretebilir. Kullanıcı "RAG seçtim" dediğinde gerçek bir anlamsal arama beklemeli.

**Yapılacaklar:**
- `cmbContextMode.SelectedIndex == 4` durumunda, mesaj gönderilmeden önce gerçekten `RagService`'in mevcut arama fonksiyonunu (`topK=5` ile) çağır, dönen chunk'ları `[BAĞLAM: RAG Sonuçları]` başlığıyla context'e ekle — talimat metni yerine gerçek veri.
- Bu, yeni bir RAG motoru yazmak değil; zaten var olan `RagService` çağrısını burada da tetiklemek.

---

## 2. Router Context Zenginleştirmesi — şimdilik bilinçli olarak ERTELE

**Doğrulama:** `Services/Router/RouterContext.cs`
```csharp
public class RouterContext {
    public List<ToolDefinition> AvailableTools { get; set; }
    public string? SystemPrompt { get; set; }   // tanımlı ama AiRouter.cs'de hiç kullanılmıyor
    // Additional context can be added here (e.g., active file, selected folder)
}
```
Router'a şu an aktif dosya, seçili kod, mention edilen dosyalar, önceki agent adımı gibi hiçbir şey gitmiyor — sadece görev metni + tüm 31 tool kataloğu.

**Neden şimdi değil:** 1.5B'lik yerel bir router modelinin en büyük avantajı hız/ucuzluk; ona fazladan bağlam yüklemek bu avantajı zedeleyebilir. Bu maddeyi **listeye yazıyoruz ama önceliklendirmiyoruz** — önce `mdai-router`'ın gerçek kullanım istatistiklerini (kaç kez fallback'e düştü, hangi görev tiplerinde yanlış tool seçti) topla, veri gösterirse bu maddeye dön.

**Yapılacaklar (şimdilik yok, sadece not):** Telemetri (zaten `TokenTrackerService`/`tool_telemetry.json` altyapısı var) üzerinden Router'ın yanlış/fallback kararlarını izlemeye başla. Belirli bir eşiği (örn. %15+ fallback oranı) geçerse, `RouterContext`'e aktif dosya + son 1-2 agent adımını eklemeyi değerlendir — tüm chat geçmişini değil, sadece en ucuz/en etkili 2-3 alanı.

---

## otomatik chat title


Yeni chat:

Yeni Sohbet

olarak açılıyor.


İlk mesajdan:

JWT authentication hatası

gibi bir başlık.

Ama bunun için ekstra LLM çağrısı yapma.

Router'ın veya ana response metadata'sının mevcut çıktısından üretilebilir ya da basit bir local heuristic olabilir.



---


*Bu doküman, mdaiAgent kaynak kodunun doğrudan taranmasıyla hazırlanmıştır. v2'deki tüm maddeler doğrulanıp kapatıldı; bu sürüm yalnızca yeni bulunan, hâlâ açık maddeleri içerir.*

## 2. 2026-09-08 Uygulama Durumu

- [x] Context Selector için `ChatContextMode` sözleşmesi eklendi; UI seçimi ChatFlow katmanına aktarılıyor.
- [x] Active File modu tam dosya yerine mevcut imleç çevresi penceresini kullanıyor.
- [x] Explicit Active File ve Selection modlarında otomatik Context Discovery devre dışı bırakıldı.
- [x] RAG modu sahte talimat metni yerine gerçek `RagService.SearchAsync` çağrısı kullanıyor; explicit RAG için `topK=5`.
- [x] Auto/Project ve explicit RAG akışları ayrıştırıldı; duplicate retrieval riski azaltıldı.
- [x] Otomatik sohbet başlığı ilk kullanıcı mesajından üretiliyor, slash komutu temizleniyor, aktif başlık ve session kaydediliyor.
- [x] İngilizce `New Chat` varsayılan adı da otomatik başlık akışına dahil edildi.
- [x] Geriye dönük `IChatFlowService` imza uyumluluğu korundu.

### Doğrulama

- `dotnet build BasucuIDE\\mdaiAgent.csproj --no-restore --nologo` başarılı.
- `dotnet test tests\\mdaiAgent.Tests\\mdaiAgent.Tests.csproj --no-build --filter ChatFlowServiceTests --nologo --verbosity:minimal` başarılı: 11/11.

### Açık Sonraki İş

- Router fallback oranı ve yanlış araç seçimi için gerçek kullanım telemetrisi toplanacak.
- Eşik aşılırsa Router Context'e yalnızca aktif dosya ve son agent adımı gibi düşük maliyetli bilgiler eklenecek.

## 3. Router Telemetri Durumu

- [x] Router çağrıları proje bazında `.mdai/router_telemetry.json` dosyasına kaydediliyor.
- [x] Toplam route, başarılı seçim, fallback, timeout, API error ve düşük güven sayaçları tutuluyor.
- [x] Router'ın aktif tool kataloğunda bulunmayan araçları seçmesi `InvalidSelectionRoutes` olarak ayrı ölçülüyor.
- [x] Bilinçli boş araç seçimi (`EmptyToolSelections`) fallback olarak sayılmıyor.
- [x] Ortalama Router latency ve fallback oranı hesaplanıyor.
- [x] `ClearProjectTelemetry` Router telemetrisini de temizliyor.
- [x] Router telemetri persistence regresyon testi eklendi.

### Router Context Karar Eşiği

Şimdilik Router'a aktif dosya veya chat geçmişi eklenmeyecek. Önce gerçek kullanım verisi toplanacak. Aşağıdaki sinyallerden biri anlamlı seviyeye ulaşırsa düşük maliyetli context genişletmesi değerlendirilecek:

- Fallback oranı yaklaşık `%15` veya üzerinde.
- Timeout oranı belirgin biçimde yüksek.
- `InvalidSelectionRoutes` tekrar eden bir sorun haline geliyor.

Doğrulama: `ProjectTelemetryTests` 4/4 başarılı; mdaiAgent build başarılı.

## 4. Yerel Router Model Entegrasyonu

- [x] Yerel Router artık sabit `mdai-router` değerini kullanmıyor; `RouterModel` ve `RouterBaseUrl` ayarlarını kullanıyor.
- [x] Yerel Router varsayılanları `http://localhost:11434/v1` ve `mdai-router-1.5b-q4` olarak hizalandı.
- [x] İndirilen GGUF dosyası artık yalnızca diske yazılmıyor; Ollama `create` komutuyla gerçek model olarak register ediliyor.
- [x] Ollama kaydı tamamlanmadan Settings ekranında model hazır gösterilmiyor.
- [x] Daha önce kaydedilmiş `mdai-router` ve eski cloud Router ayarları yerel Router seçiliyken otomatik migrate ediliyor.
- [x] Mevcut GGUF dosyası varsa indirme butonu dosyayı yeniden indirmek yerine Ollama kurulumunu tamamlıyor.
- [x] Settings ekranındaki yerel Router adı gerçek `mdai-router-1.5b-q4` adıyla eşitlendi.
- [x] Buy Me a Coffee butonu eklendi.


Doğrulama: mdaiAgent build başarılı; ChatFlowServiceTests ve ProjectTelemetryTests toplam 15/15 başarılı.

## 7. Alt Panel Sekme Şeridi Düzeni

- [x] Gereksiz `Terminal & İşlem` başlık satırı kaldırıldı.
- [x] Panel büyüt/küçült ve Tanılama ayırma butonları sekme şeridinin sağına taşındı.
- [x] Terminal, İşlemler, RAG ve Tanılama sekmeleri aynı kontrol satırında tutuldu.
- [x] Sağdaki kontrol alanı sekme başlıklarını kapatmayacak şekilde sınırlandı.

Doğrulama: mdaiAgent build başarılı; ChatFlowServiceTests ve ProjectTelemetryTests toplam 15/15 başarılı.

## 5. Güncelleme ve Yeniden Markalama Temeli

- [x] About penceresine `Güncellemeleri denetle` butonu eklendi.
- [x] Güncelleme kanalı henüz yapılandırılmadıysa kullanıcıya açık bilgi veriliyor; sahte bir indirme yapılmıyor.
- [x] GitHub Releases kanalı bağlandığında aynı buton release adresini açabilecek şekilde hazırlandı.
- [x] Router model adı, GGUF dosya adı ve yerel endpoint `RouterModelInfo.cs` içinde merkezi hale getirildi.
- [x] Ürün adı ile teknik model adı birbirinden ayrılabilecek duruma getirildi.

### Markalama Notu

Model adını yeniden adlandırırken yalnızca global metin değiştirme yapılmamalı. Üç kimlik ayrı düşünülmeli:

1. Ürün adı: kullanıcıya görünen uygulama adı.
2. Ollama model alias'ı: `ollama create` ve API çağrılarında kullanılan teknik ad.
3. GGUF dosya adı: indirme ve yerel cache adı.

Bu kimlikler `RouterModelInfo.cs` üzerinden yönetiliyor. Yeni isim değişikliği bu dosyadan ve yayın metadata'sından yapılmalı; eski Ollama alias'ı için migration stratejisi korunmalı.


### Kalan Dağıtım Gereksinimi

Gerçek kullanıcı dağıtımı için `LocalModelDownloadUrl` içindeki placeholder Hugging Face adresi gerçek yayın adresiyle değiştirilmeli. Ayrıca model lisansı, temel model lisansı ve GGUF checksum bilgisi yayın paketine eklenmelidir.

## 6. Hibrit Tanılama Paneli

- [x] Alt panel varsayılan olarak editörü ezmeyecek şekilde 230 px yüksekliğe alındı.
- [x] Alt panel için `MinHeight=170` ve `MaxHeight=420` sınırları eklendi.
- [x] Editör alanı için minimum 220 px yükseklik korundu.
- [x] Alt panel başlığına büyüt/küçült kontrolü eklendi.
- [x] Tanılama sekmesi ayrı, yeniden boyutlandırılabilir `DiagnosticsWindow` penceresine ayrılabiliyor.
- [x] Ayrı pencere kapatıldığında Tanılama sekmesi alt panele geri dönüyor.

Doğrulama: mdaiAgent build başarılı; ChatFlowServiceTests ve ProjectTelemetryTests toplam 15/15 başarılı.
