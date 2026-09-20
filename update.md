# mdaiAgent Geliştirme Güncellemesi

DİKKAT BU DOSYADADAKİ GÜNCELLEMELERİN TAMAMI TAMAMLANMIŞTIR. UPDATE2.md den devam EDİLECEK.

Tarih: 2026-09-06

## Genel Değerlendirme

mdaiAgent artık basit bir AI sohbet penceresi değildir. Kullanıcının gerçek bir yazılım projesi üzerinde:

```text
Görev → Proje bağlamı → Araç seçimi → Dosya değişikliği → Build/Test → Verification → Recovery → Sonuç
```

döngüsünü yürütebilen, güvenlik ve proje hafızası olan kişisel bir AI geliştirme asistanıdır.

Tek kişi tarafından geliştirildiği için hedef, Cursor veya VS Code'un bütün özelliklerini kopyalamak değildir. Daha doğru hedef şudur:

> Güvenilir, şeffaf, güvenlik kontrollü ve proje hafızası olan kişisel AI yazılım geliştirme asistanı.

## Şu Anda Hazır Olan Önemli Alanlar

- Unified Agent Runtime
- Verification 2.0
- Build, test, statik analiz ve değişen dosya incelemesi
- Warning/failure ayrımı
- Runtime Timeline/EventBus akışı
- Manuel araç seçimi
- Router tabanlı araç seçimi
- Router blacklist ve fallback araç listesi
- Proje bazlı merkezi telemetri
- Model latency, retry, provider failure ve verification istatistikleri
- Kalıcı Hafıza 2.0
- Architecture/conventions kayıtları
- Hafıza arama ve arşivleme
- Proje dışındaki onaylı yedek klasörlerine erişim
- Symlink/junction güvenlik kontrolü
- Terminal risk politikası ve kullanıcı onayı
- Checkpoint/rollback ve Backup Viewer
- Türkçe, İngilizce ve Çince localization altyapısı
- Ana pencerenin önemli sabit metinlerinin resource sistemine taşınması
- RAG ve proje izleyici
- Plan Mode, Task Graph ve Recovery Plan
- Sub-agent/delegation altyapısı
- Sesli komut
- LSP altyapısı
- 14 yerleşik dil denetimi plugin'i
- Harici plugin manifest/hash doğrulaması
- HTTPS, güvenilir host ve imzalı katalog doğrulaması

## Son Durum (2026-09-07)

- [x] Stabilite: terminal timeout/cancel sırasında süreç ağacının kapatılması ve hata raporlaması düzeltildi.
- [x] AI değişiklik kalitesi: otomatik context keşfi, ilgili dosya/test önceliklendirmesi, güvenli patch hedefleri, conflict recovery, kısaltılmış build/test özeti ve uzun çıktı için geçici `ReadToolOutput` akışı iyileştirildi.
- [x] Kullanıcı deneyimi: agent işlem adımı temizlendiğinde aktif ilerleme ve agent durum göstergeleri artık birlikte kapanıyor; stale çalışma durumu bırakılmıyor.
- [ ] Gerçek proje kullanım testi: otomatik demo smoke pass tamamlandı (19 integration test başarılı); kullanıcının gerçek proje/provider ile UI kabul testi bekleniyor.
- [x] Editor deneyimi: Go to Definition, Find References, document symbols ve Diagnostics sekmesinde dosya/satır/message sunumu geliştirildi.
- [x] RAG ve context kalitesi: doğrudan belirtilen dosya yolları, implementation/test eşleri, stale RAG index temizliği ve Context Discovery/RAG prompt tekrarlarının azaltılması iyileştirildi.
- [ ] Ürünleşme: installer, release ve marketplace süreçleri sonrası aşamada.

Bu nedenle proje şu anda kullanılabilir bir ürün çekirdeğine sahiptir. Eksikler, temel işlevselliğin yokluğu değil; ürünün daha güvenilir, pürüzsüz ve dağıtılabilir hale getirilmesiyle ilgilidir.

## Gerçek Eksikler

### 1. Stabilite ve güvenilirlik

- Uzun görevlerde timeout ve iptal davranışı daha da güçlendirilmeli.
- Yarım kalan işlemlerde rollback davranışı genişletilmeli.
- Tool tekrarları ve sonsuz retry durumları sınırlandırılmalı.
- Gerçek projeler üzerinde uzun süreli kullanım testleri artırılmalı.

### 2. AI kod değişikliği kalitesi

- Büyük dosyanın tamamı yerine ilgili kod bölümleri önceliklendirilmeli.
- Patch çakışmaları daha iyi ele alınmalı.
- Değişiklik öncesi ve sonrası bağlam kontrolü güçlendirilmeli.
- Hata sonrası yalnızca ilgili testler seçilebilmeli.
- Build/test çıktısı modele daha kısa ve anlamlı biçimde aktarılmalı.
- Terminal, build ve test gibi yüksek-risk eylemler için "capability gate" / izin kontrolü eklenmeli. Modelin ihtiyaç duyduğu araçların seçimden bağımsız olarak tekrar doğrulanması gerekir; örneğin "terminal kullanmam lazım ama emin değilim" gibi bir ifade, otomatik olarak yetki gerektiren bir eylem olarak işlenmeli ve gerekliyse router/approval katmanından onay alınmalı.

> Not: Bu, sıradan bir router fallback değildir. Router araç seçimi ile ayrı çalışan "güvenli eylem izni" katmanıdır. Yalnızca terminal/build/test gibi yüksek etkili işlemlerde devreye girer; read/search/write gibi düşük risk eylemler için ayrı, daha hafif kontrol yeterlidir.

### 3. Editor deneyimi

VS Code seviyesinde her özellik hedeflenmemeli. Önce C# ve Python için şu deneyimler iyileştirilmeli:

- Symbol arama
- Go to Definition
- Find References
- Daha iyi diagnostics
- Kod tamamlama
- Format-on-save
- Diff/merge deneyimi
- Büyük dosya performansı

### 4. RAG ve context kalitesi

- Değişen dosyalar context içinde önceliklendirilmeli.
- Implementation ve ilgili test dosyası birlikte getirilmeli.
- Constitution, decisions ve memory kayıtları doğru sırada sunulmalı.
- RAG sonuçlarının neden seçildiği daha görünür olmalı.
- Eski ve geçersiz context temizlenmeli.

### 5. Ürünleşme

- Installer ve Release dağıtımı sadeleştirilmeli.
- İlk açılış/onboarding akışı iyileştirilmeli.
- Crash/log görünürlüğü artırılmalı.
- Sürüm numarası ve changelog düzeni oluşturulmalı.
- Temiz bir demo proje ile baştan sona kullanım senaryosu doğrulanmalı.

- Gerçek marketplace endpoint'i işletilmeli.
- Harici DLL plugin'leri ayrı Plugin Host sürecine taşınmalı.
- IPC ve plugin izin sınırları eklenmeli.
- Plugin timeout ve host çökmesi yönetilmeli.

## Öncelik Sırası

Bütün maddelerin aynı anda yapılması gerekmiyor. Tek kişi için önerilen sıra:

### Öncelik 1: Stabilite

İlk hedef, mevcut özelliklerin güvenilir çalışmasıdır.

- [x] Cancellation/timeout
- [x] Retry sınırları (temel süreç sonlandırma kontrolü ve hata dönüşü)
- [ ] Hata ve rollback akışları
- [ ] Gerçek proje kullanım testleri: otomatik smoke pass tamam; gerçek proje üzerinde kullanıcı kabul adımı bekleniyor.

### Öncelik 2: AI Değişiklik Kalitesi

mdaiAgent'in asıl farkı burada oluşur.

- [x] Daha iyi dosya/context seçimi
- [x] İlgili test dosyaları için önceliklendirme
- [x] Küçük ve güvenli patch'ler
- [x] Patch conflict recovery: hedef bulunamadığında dosya değiştirilmeden `PATCH_CONFLICT` sonucu ve `ReadFile` ile yeniden bağlam alma yönlendirmesi veriliyor.
- [x] Daha temiz build/test çıktısı: uzun terminal çıktısında hata ve uyarı sayısı, seçilmiş bağlam satırlarıyla birlikte modele aktarılıyor.
- [x] Uzun araç çıktısı spool sistemi: kısaltılan build/test/terminal çıktıları geçici kimlikle saklanıyor ve gerektiğinde `ReadToolOutput` ile satır aralığı okunabiliyor.
- [x] Seçili proje yolu güvenliği: göreli araç yolları artık process çalışma dizinine değil, aktif seçili proje köküne göre doğrulanıyor.
- [x] Router güven eşiği: `0.8`, `0,8` ve yüzde biçimi girişleri `0..1` aralığına normalize ediliyor; eski hatalı `8.0` değerleri çalışma anında `0.8` olarak düzeltiliyor.
- [x] Router gecikmesi: basit selamlaşmalarda router çağrısı atlanıyor; gerçek görevlerde 25 saniyelik üst timeout korunuyor.
- [x] Görev belgesi ve plan akışı: `.txt/.md` görev dosyaları context discovery'ye dahil ediliyor, plan üretim akışı ham model muhakemesini terminale yazmıyor ve plan sonrası uygulamaya devam etme kuralı güçlendirildi.
- [ ] Capability gate / yetki doğrulama katmanı: terminal, build ve test gibi yüksek-risk eylemler için modelin gereksinimini tekrar doğrulayan ve izin veren bir katman eklenmeli; router fallback yalnızca araç seti seçimi için kullanılmalı, ayrı bir güvenli eylem yetki kontrolü de olmalı.

### Öncelik 3: Kullanıcı Deneyimi

- [x] Agent'ın ne yaptığını gösteren net durum akışı
- [x] Onboarding temeli: boş editör ekranında üç adımlı Quick Start akışı mevcut.
- [x] Araç ve Router modlarının temel açıklaması: Manual/Automatic toggle, Router kısıtları ve blacklist ayarları mevcut.
- [x] Değişiklik inceleme ve geri alma temeli: `DiffWindow`/`GenerateDiff`, otomatik yedekler/`BackupViewerWindow` ve checkpoint rollback mevcut. Ayrı bir ikinci undo mekanizması eklenmeyecek.

### Öncelik 4: Editor İyileştirmeleri

Önce C# ve Python olmak üzere sembol arama, definition/reference ve diagnostics kalitesi geliştirilmeli.

- [x] Go to Definition: mevcut LSP taşıma katmanı üzerinden F12 ile tanım konumuna gidiliyor.
- [x] Find References ve sembol arama: `Shift+F12` LSP referanslarını, `Ctrl+T` aktif dosyanın sembollerini mevcut sonuç listesinde gösteriyor.
- [x] Diagnostics sunumunun iyileştirilmesi: mevcut LSP/plugin diagnostics listeleniyor ve çift tıklamayla ilgili dosya/satıra gidiliyor.

### Öncelik 5: Dağıtım ve Plugin Host

Installer, gerçek marketplace ve ayrı plugin süreci daha sonra ele alınmalı.

## Şimdilik Yapılmaması Gerekenler

- Cursor/VS Code'daki her özelliği kopyalamaya çalışma.
- Çok sayıda yeni araç ekleme.
- Çoklu Router/teacher consensus geliştirme.
- Her dil için aynı anda derin editor desteği yapma.
- Plugin marketplace'i gerçek endpoint olmadan tamamlanmış sayma.
- Stabilite sorunları çözülmeden büyük yeni mimari ekleme.

## Ürün Hedefi

mdaiAgent'in en güçlü ve gerçekçi konumu:

> Türkçe ve çok dilli, güvenlik kontrollü, proje hafızası olan, yaptığı değişiklikleri build/test ile doğrulayan kişisel AI yazılım geliştirme asistanı.

Bu hedef için mevcut temel yeterince güçlüdür. Bundan sonraki çalışma “programı baştan yapmak” değil; mevcut çekirdeği daha güvenilir, daha hızlı ve daha akıcı hale getirmektir.

## Sonraki Tek Adım

Bir sonraki çalışma için yalnızca şu konu seçilmelidir:


> RAG ve context kalitesi tamamlandı; sonraki değerlendirme ürünleşme öncesi gerçek proje kullanım testi olmalı.

Bu iş tamamlanınca yeniden değerlendirme yapılmalı; başka çok büyük bir “daha iyi yapalım” döngüsüne girmeden, yalnızca bir sonraki gerçek gerekliliğe geçilecektir.

..............

ChatFlowService.cs içinde final completion branch’i devre dışı bırak
AgentVerificationLoopService.cs üzerinde “verification required before task complete” katmanı ekle
“task completion attestation” helper oluştur
görev başarısızsa otomatik continue / retry / user escalation
terminal/build/test gibi riskli araçlar için capability gate eklenecek
Bu işin doğru çözümü budur.
