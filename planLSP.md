# mdaiAgent LSP ve Eklenti Sistemi Yenileme Plani

Tarih: 2026-09-10
Durum: Analiz tamamlandi, uygulama baslamadi

## 1. Amac

LSP sunuculari, dahili kod denetleyicileri ve indirilebilir eklentiler arasindaki kavramsal karisikligi gidermek.

Kullanici su sorulara net cevap alabilmeli:

- Bu dil icin LSP sunucusu kurulu mu?
- Kuruluysa gercekten baslatilabilir mi?
- Su anda hangi LSP sunucusu calisiyor?
- Dosyadaki hata denetimi dahili plugin tarafindan mi, LSP tarafindan mi geliyor?
- Bir kurulum veya baslatma sorunu varsa gercek nedeni ne?

Temel ilke:

> Bir dosyanin bulunmasi, bir eklentinin bulunmasi ve bir LSP sunucusunun hazir olmasi ayni durum degildir.

## 2. Mevcut Durum ve Kanitlar

### 2.1 Iki farkli sistem ayni arayuzde karisiyor

`BasucuIDE/PluginSystem.cs` icinde iki ayri model bulunuyor:

- `ILanguageErrorCheckerPlugin`: Uygulama icinde calisan dahili/harici kod hata denetleyicileri.
- `LspServerInfo`: Harici process olarak calisan LSP sunuculari.

`PluginManagerWindow.xaml` icindeki `Yuklu Eklentiler` sekmesi `PluginManager.GetAllPluginManifests()` kullaniyor. Bu liste LSP sunucularini degil, `ILanguageErrorCheckerPlugin` nesnelerini gosteriyor.

Sonuc:

- `HTML / CSS` satirinin Yüklü Eklentiler sekmesinde gorunmesi HTML LSP sunucusunun hazir oldugunu gostermez.
- Bu sekme kullaniciya su anda yanlis bir butunluk hissi veriyor.

### 2.2 LSP tik durumu gercek hazirlik durumu degil

`LspServerInfo.IsInstalled` su anda genel olarak PATH veya `%LOCALAPPDATA%\\mdaiAgent\\LspServers` altinda dosya ariyor.

Bu kontrol su soruyu cevapliyor:

> Beklenen isimde bir dosya bulundu mu?

Su sorulari cevaplamiyor:

- Process baslatilabiliyor mu?
- LSP `initialize` istegine cevap veriyor mu?
- Process hemen kapaniyor mu?
- Proje icin dogru executable mi?
- Windows shim (`.cmd`, `.ps1`) dogru calistirilabiliyor mu?

### 2.3 UI ile runtime ayni executable cozumlemesini kullanmiyor

`PluginSystem.cs` ve `LanguageServerService.cs` executable aramasini farkli kurallarla yapiyor.

Runtime tarafinda su kaynaklar kullaniliyor:

1. Proje veya ust klasorlerindeki `node_modules/.bin`.
2. mdaiAgent yerel LSP klasoru.
3. Sistem PATH'i.

UI tarafindaki `IsInstalled` kontrolu bu kaynaklarin tamamini ve ayni sirayi kullanmiyor.

Windows ortaminda HTML icin su durum goruldu:

- `html-languageserver.cmd` mevcut.
- `html-languageserver.ps1` mevcut.
- `vscode-html-language-server` PATH uzerinde bulunamadi.

Bu nedenle `.ps1` dosyasinin bulunmasi ile `.cmd` dosyasinin baslatilabilir olmasi ayni sekilde degerlendirilmemeli.

### 2.4 Baslatma hatasi ile kurulu olmama ayni mesaja dusuyor

`MainWindow.xaml.cs` icindeki `StartSmartLspServerAsync` ve `PromptInstallLspForLanguage` akisi, su durumlari ayni genel mesaja ceviriyor:

- executable bulunamadi
- process baslatilamadi
- initialize basarisiz oldu
- process kapandi
- protokol/arguman uyumsuzlugu

Kullanici bunlarin hepsini "kurulu degil" olarak goruyor.

### 2.5 Tek LSP servisi birden cok dili tasiyamiyor

`MainWindow` icinde tek bir `LanguageServerService` var.

`StartSmartLspServerAsync` bir proje icin dosya turlerini tarayip sirayla baslatmayi deniyor. Ancak `LanguageServerService.StartServerAsync` bagli bir process varsa `true` donuyor.

Bu nedenle cok dilli bir projede su risk var:

1. Once C# icin OmniSharp baslar.
2. Sonra HTML icin tekrar baslatma denenir.
3. Servis zaten bagli oldugu icin HTML baslatilmis gibi kabul edilir.
4. HTML belgesi yanlis LSP process'ine gonderilebilir.

### 2.6 Indirilebilir eklenti katalogu tamamlanmis degil

`GetAvailablePluginsFromGitHubAsync()` su anda bos liste donuyor.

`GetAvailablePlugins()` ise ornek Kotlin ve Swift kayitlari donuyor. Bunlar gercek marketplace akisinin yerine gecmiyor.

`Yuklu Eklentiler` sekmesinde dahili plugin'lere de `Guncelle` butonu gosteriliyor. Dahili plugin ile indirilebilir plugin guncellemesi ayrilmamis.

## 3. Hedef Mimari

### 3.1 Kavramlar

Sistem uc ayri kavram kullanacak:

#### A. LSP Server

Harici process'tir. Ornek:

- OmniSharp
- Pyright
- TypeScript Language Server
- HTML Language Server
- clangd

#### B. Diagnostic Plugin

Uygulama icinde calisan basit veya gelismis hata denetleyicisidir.

#### C. Marketplace Plugin

Guvenilir kaynaktan indirilen, manifest ve SHA-256 ile dogrulanan harici plugin paketidir.

Bu kavramlar ayni DataGrid modelinde birbirine karistirilmayacak.

### 3.2 LSP durum modeli

Basit `bool IsInstalled` yerine en azindan su bilgileri tasiyan bir durum modeli kullanilacak:

- `NotInstalled`
- `Installed`
- `Ready`
- `Running`
- `Failed`
- `Unknown`

Ek bilgiler:

- Bulunan executable yolu
- Kaynak: proje, local LSP klasoru veya PATH
- Son kontrol zamani
- Son hata mesaji
- Baslatilan process id
- Desteklenen uzantilar

UI'da sadece tik kullanilmayacak. Kullanici durumu metin ve tooltip ile gorecek.

### 3.3 Tek executable cozumleme noktasi

LSP registry ve runtime ayni cozumleyiciyi kullanacak.

Onerilen sorumluluk:

`LanguageServerResolver` veya `LspExecutableResolver`

Girdi:

- LSP tanimi
- proje yolu

Cikti:

- executable tam yolu
- kullanilacak argumanlar
- kaynak bilgisi
- cozumleme hatasi

Windows icin destek:

- `.exe`
- `.cmd`
- `.bat`
- kontrollu `.ps1` senaryosu

UI'nin kurulu gosterimi ile runtime baslatmasi bu resolver olmadan ayri ayri dosya aramayacak.

## 4. Uygulama Sirasi

## Faz 0 - Sozlesme ve regresyon zemini

Durum: Baslanmadi

### Yapilacaklar

- LSP, Diagnostic Plugin ve Marketplace Plugin kavramlarini kod seviyesinde ayirmak.
- Mevcut davranisi koruyan kucuk test altyapisini hazirlamak.
- `planLSP.md` maddelerini uygulama sirasinda kanitlarla guncellemek.

### Testler

- LSP registry kayitlari benzersiz ID tasiyor mu?
- HTML, CSS, TS ve JS uzantilari dogru LSP kaydina baglaniyor mu?
- Dahili plugin listesi LSP listesine karismiyor mu?

### Kapanis kaniti

- Mevcut build basarili.
- Yeni testler once mevcut davranisin beklenen kismini yakaliyor.

## Faz 1 - LSP ve plugin arayuzlerini ayirmak

Durum: Tamamlandi (ilk UI dilimi)

### UI hedefi

`PluginManagerWindow.xaml` sekmeleri su anlama gelecek:

1. `LSP Sunuculari`
   - Harici LSP server kayitlari.
   - Kurulu, hazir, calisiyor veya hatali durumu.
   - `Kur`, `Kontrol Et`, `Klavuz` islemleri.

2. `Kod Denetleyicileri`
   - Dahili ve harici `ILanguageErrorCheckerPlugin` kayitlari.
   - Dahili plugin icin `Guncelle` butonu gosterilmemeli.
   - Harici plugin icin manifest durumu gosterilmeli.

3. `Indirilebilir Eklentiler`
   - Sadece gercek marketplace plugin kayitlari.
   - Bos katalog varsa bunun nedeni acikca gosterilmeli.
   - Sahte veya ornek indirme kaydi kullaniciya gercek paket gibi sunulmamali.

### Onemli karar

`Yuklu Eklentiler` sekmesi ya `Kod Denetleyicileri` olarak yeniden adlandirilacak ya da sekme aciklamasi ile bunun LSP olmadigi acikca belirtilecek. Tercih edilen secenek `Kod Denetleyicileri` adidir.

### Kapanis kaniti

- HTML satiri LSP sekmesinde ve kod denetleyicisi sekmesinde farkli anlamlarla gorunur.
- Dahili HTML denetleyicisi yuku HTML LSP durumunu degistirmez.
- UI'da dahili plugin icin anlamsiz `Guncelle` butonu yoktur.

### Uygulanan degisiklikler

- `Yuklu Eklentiler` sekmesi kullaniciya `Kod Denetleyicileri` olarak gosteriliyor.
- `IsBuiltIn == true` olan dahili denetleyiciler icin guncelleme butonu gizleniyor.
- XAML ve uc dil kaynagi derleme ile dogrulandi.

## Faz 2 - Ortak executable resolver

Durum: Tamamlandi

### Yapilacaklar

- LSP executable aramasini tek serviste toplamak.
- Arama sirasini standartlastirmak:
  1. Proje `node_modules/.bin` ve ust klasorler.
  2. `%LOCALAPPDATA%\\mdaiAgent\\LspServers`.
  3. PATH.
- Windows shim uzantilarini ayni sekilde ele almak.
- Her sonuc icin kaynak ve tam yolu raporlamak.
- `LspServerManager.IsInstalled` degerini bu resolver sonucundan uretmek.
- `LanguageServerService.StartServerAsync` icinde ayni resolver sonucunu kullanmak.

### HTML ozel kontrolu

Asagidaki iki paket/komut ayrimi acikca tanimlanacak:

- `html-languageserver`
- `vscode-html-language-server`

Bir komutun PATH'te bulunmasi, process baslatilmasi ve LSP initialize olmasi ayri asamalar olarak raporlanacak.

### Kapanis kaniti

- UI ve runtime ayni executable yolunu raporlar.
- HTML `.cmd` shim'i olan bir makinede ayni dosya bulunur.
- Proje icindeki `node_modules/.bin` HTML server'i, global PATH'ten once bulunur.
- Bulunamayan durumda hangi kaynaklarin denendigi loglanir.

### Uygulanan degisiklikler

- `LspExecutableResolver` eklendi.
- Runtime LSP baslatmasi HTML/CSS/Pyright alias'lari dahil resolver kullaniyor.
- HTML LSP durum kontrolu ayni resolver kullaniyor.
- Windows'ta yalnizca `.ps1` veya uzantisiz Unix shim'i launchable kabul edilmiyor.
- Resolver testleri: 2/2 basarili.

### Kapanis kaniti

- Uygulama build: 0 uyari, 0 hata.
- Resolver testleri: 2/2 basarili.

## Faz 3 - LSP health check ve gercek durum gostergesi

Durum: Devam ediyor (health check uygulandi, bagimsiz testler acik)

### Yapilacaklar

`IsInstalled` tek basina ana durum olmaktan cikarilacak.

Kontrol akisi:

1. Executable cozumle.
2. Process baslatilabilir mi kontrol et.
3. Gerekirse izole bir health check ile `initialize` dene.
4. Process'i temiz kapat.
5. Sonucu `Installed`, `Ready` veya `Failed` olarak raporla.

Normal kullanici akisi ile health check birbirine karistirilmayacak. UI'daki `Durumu Yenile` islemi kontrollu ve iptal edilebilir olacak.

### UI hedefi

Her satirda su bilgilerden en az ikisi gorunecek:

- Durum
- Bulunan yol veya kaynak
- Son hata

Ornek:

```text
HTML & CSS Language Server | Hazir | npm global
```

veya:

```text
HTML & CSS Language Server | Baslatilamadi | html-languageserver.cmd
```

### Kapanis kaniti

- Sadece dosya var diye `Hazir` gosterilmez.
- Initialize basarisizsa kullanici "kurulu degil" yerine gercek nedeni gorur.
- Health check sonrasi process artigi kalmaz.

### Uygulanan ara duzeltme

- LSP satirinda tik yerine `Kurulu`, `Hazır` veya `Başlatılamadı` durumu gosteriliyor.
- Bulunan executable yolu ve kaynagi tooltip olarak gosteriliyor.
- `initialize` basarili olunca server `Hazır` olarak isaretleniyor.
- Executable bulunamaz veya baslatma basarisiz olursa son hata LSP kaydina yaziliyor.
- Bagimsiz health check butonu ve process temizligi icin ayri testler Faz 3'te acik kaldi.

### Health check uygulamasi

- `Durumu Yenile` artik kurulu LSP'leri tek tek gercek process olarak baslatip `initialize` el sikismasini deniyor.
- Her server icin 8 saniyelik timeout uygulaniyor.
- Kontrol sonrasi process `StopServer()` ile temizleniyor.
- Basarili kontrol `Hazır`, basarisiz kontrol `Başlatılamadı` durumunu koruyor.
- Uygulama build: 0 uyari, 0 hata.
- Resolver testleri: 2/2 basarili.

## Faz 4 - Tek LSP process yerine dil bazli session yonetimi

Durum: Tamamlandi (dil bazli lazy session yonetimi)

### Hedef

Cok dilli projelerde LSP process'leri birbirine karismayacak.

Onerilen model:

```text
Dictionary<string, LanguageServerSession>
```

Ornek:

```text
html -> HTML Language Server
css  -> CSS Language Server
cs   -> OmniSharp
py   -> Pyright
```

### Yapilacaklar

- `LanguageServerService` tek process varsayimindan cikarilacak veya dil bazli session yoneticisine donusturulecek.
- Her session kendi process, cancellation token, pending request ve acik belge durumunu tasiyacak.
- Dosya acarken `languageId` ile dogru session secilecek.
- Proje degisince tum session'lar temiz kapatilacak.
- Ayni dil icin gereksiz ikinci process acilmayacak.
- LSP server secimi, proje tarama sirasina bagli yan etki olmaktan cikarilacak.

### Kapanis kaniti

- C# + HTML ayni projede acilir.
- HTML belgesi OmniSharp'a gitmez.
- C# belgesi HTML server'a gitmez.
- Projede sadece HTML varsa sadece gerekli session baslar.
- Proje degistirince eski process'ler kapanir.

### Uygulanan degisiklikler

- `MainWindow` artik dil uzantisi anahtarli `LanguageServerService` oturumlari tutuyor.
- HTML, C#, Python ve diger desteklenen diller kendi LSP process'lerini kullanabiliyor.
- Dosya acma ve belge degisikligi dogru dil oturumuna yonlendiriliyor.
- Completion, definition, reference ve document symbol istekleri aktif dosyanin oturumuna gidiyor.
- Proje degisirken tum oturumlar temiz kapatiliyor.
- Oturumlar ihtiyac aninda olusturuluyor; acilmayan dil icin process baslatilmiyor.
- Uygulama build: 0 uyari, 0 hata.
- Resolver testleri: 2/2 basarili.

## Faz 5 - Tum desteklenen diller icin gercek dunya LSP akisi

Durum: Devam ediyor (dil kapsami ve diagnostics dilimi)

### Kapsam ilkesi

HTML, CSS ve JavaScript bu fazin ilk gercek dunya pilotudur; kalite standardi bunlarla sinirli degildir. Uygulama tarafindan desteklenen her dil ayni LSP sozlesmesine ve ayni kullanici deneyimine sahip olmalidir.

Hedef dil gruplari:

- Web: HTML, CSS, SCSS, LESS, JavaScript, TypeScript
- .NET: C#; XAML is currently a syntax/highlighting path and remains an explicit LSP registry gap.
- Python: Python
- C/C++: C, C++, C/C++ header uzantilari
- JVM: Java, Kotlin
- Mobil: Dart
- Sistem: Rust, Swift, Go
- Sunucu/veri: PHP, Ruby, SQL

Objective-C and XAML are tracked as registry gaps rather than being presented as already supported LSP languages.

### Yapilacaklar

- Her dil ve uzanti icin tek bir registry kaydi kullanmak: `languageId`, dosya uzantilari, executable alias'lari, argumanlar ve kurulum stratejisi.
- Her dilde ayni lifecycle'i dogrulamak: executable cozumleme, process baslatma, LSP `initialize`, `initialized`, belge acma, degisiklik gonderme ve temiz kapatma.
- Her dil icin dogru `languageId` gonderildigini test etmek.
- Her dilde diagnostics akisini ve dosya/konum eslestirmesini test etmek.
- Desteklenen capability'ler varsa completion, definition, references ve document symbols akislarini dogrulamak.
- Uzanti gruplarini acikca ayirmak: HTML/HTM, CSS/SCSS/LESS, JS/JSX, TS/TSX, C/C++ basliklari, Kotlin KT/KTS gibi.
- Harici LSP bulunamadiginda o dile ait dahili diagnostic plugin varsa onun bagimsiz calismaya devam etmesini saglamak.
- Dahili plugin olmayan dillerde kullaniciya gercek kurulum/uyumluluk nedeni gostermek.
- Her dil icin ayni fallback standardini uygulamak: proje yerel paket, mdaiAgent LSP klasoru, sistem PATH'i ve son olarak kılavuz.
- Bir dilin LSP'si basarisiz oldugunda diger dillerin session'larini etkilememesini saglamak.
- Kullaniciya iki destek katmanini tum dillerde acik gostermek:
   - Dahili kod denetimi varsa temel kontrol
   - Harici LSP varsa gelismis analiz, tamamlama ve gezinme

### Kabul kriterleri

- Her desteklenen dil icin LSP durumu `Kurulu`, `Hazir` veya `Baslatilamadi` olarak dogru raporlanir.
- Her dilde proje yerel server'i global server'dan once tercih edilir.
- Her dilde server baslatilamiyorsa kullaniciya "eklenti eksik" gibi genel bir mesaj yerine gercek neden gosterilir.
- HTML, CSS, JS, TS, C#, Python, Java, C/C++, Rust, Go, Dart, Kotlin, PHP, Ruby ve SQL icin en az bir gercek dosya ile temel acma/diagnostics senaryosu calisir.
- Capability destekleyen server'larda completion veya gezinme senaryolarindan en az biri dogrulanir.
- Bir dildeki baslatma hatasi baska bir dilin aktif session'ini bozmaz.
- Dahili diagnostic plugin bulunan dillerde plugin, LSP'den bagimsiz calisir.
- Dahili plugin olmayan dillerde kurulum ve uyumluluk sinirlari acikca raporlanir.
- HTML/CSS/JS ilk pilot olarak tamamlanir; ancak Faz 5 ancak dil matrisi kabul kriterlerini sagladiginda kapanir.

### Uygulanan ilk dilim

- TSX ve JSX dogru TypeScript/JavaScript `languageId` degerlerine normalize ediliyor.
- SCSS, Sass ve LESS CSS LSP akisina baglaniyor; HTM HTML olarak normalize ediliyor.
- Java registry executable adi runtime'daki `jdtls` ile hizalandi.
- PHP icin `intelephense` ve `phpactor` fallback'leri ortak cozumlemeye alindi.
- Go, Rust, Kotlin, PHP, Ruby, SQL ve Swift proje LSP taramasina eklendi.
- LSP diagnostics ile dahili plugin diagnostics ayristirildi; tekrar eden plugin diagnostic birikimi engellendi.
- `LspLanguageRegistry` eklendi; runtime ve editor uzanti/languageId eslestirmeleri ortak kaynaga alindi.
- TSX, JSX, SCSS, Sass, LESS, HTM, C/C++ header ve KTS alias'lari registry testleriyle korundu.
- Registry tanimlarinin benzersizligi ve PHP executable fallback'leri test edildi.
- LSP `Content-Length` mesaj govdesi UTF-8 byte sayisina gore okunacak sekilde duzeltildi; Turkce ve diger Unicode metinlerde mesaj kaymasi riski azaltildi.
- LSP `initialize` el sikismasi 10 saniye ile sinirlandi; cevap vermeyen process UI'yi/testi kilitleyemiyor.
- TypeScript baslatma oncesi artik yalnizca `tsc` degil, gercek `tsserver.js` kurulumu araniyor.
- Gercek process testleri eklendi; modern HTML LSP veya uyumlu `tsserver.js` yoksa test bunu acikca raporlayarak atliyor.
- Mevcut makinedeki eski `html-languageserver` paketi `messageReader.onClose is not a function` ile uyumsuz oldugu icin `Hazır` kabul edilmiyor.
- Mevcut global TypeScript 7 kurulumunda `tsserver.js` bulunmadigi icin TypeScript server'i kuruluma hazir degil olarak raporlanacak.
- Modern `vscode-html-language-server` ve `vscode-css-language-server` komutlari legacy alias'larin onune alindi; eski paketler yalnizca fallback olarak kaldi.
- Kurulu ama health check basarisiz LSP'lerde `Kur`/onar butonu yeniden gorunur hale getirildi.
- HTML/CSS eksik veya uyumsuzsa otomatik kurulum akisi `vscode-langservers-extracted` paketine yonlendiriliyor.
- Uygulama build: 0 uyari, 0 hata.
- LSP resolver, language registry ve entegrasyon testleri: 14/14 basarili.

## Faz 6 - Kurulum ve tekrar deneme akisi

Durum: Tamamlandi

### Yapilacaklar

- Kurulum sonrasi `RefreshInstallStatus()` ile ortak resolver tekrar calistirilacak.
- Kurulum basariliysa otomatik health check yapilacak.
- Gerekirse ilgili LSP session yeniden baslatilacak.
- Kullaniciya "kuruldu" ile "calismaya hazir" ayrimi gosterilecek.
- Otomatik npm/pip kurulumu proje ve global kapsam bilgisiyle raporlanacak.
- PATH degisikligi sonrasi uygulamanin eski process ortam degiskenlerine bagli kalmasi ele alinacak.

### Kapanis kaniti

- HTML server kurulumu sonrasi uygulama yeniden baslatma gereksinimi acikca bildirilir veya yeni PATH kontrollu sekilde okunur.
- Kurulum basarisizsa kismi kurulum temizlenir ya da tekrar denenebilir.
- Kurulumdan sonra kullanici tekrar manuel sekme degistirmek zorunda kalmaz.

### Uygulanan ilk dilim

- LSP kurulumu tamamlandiginda `Durumu Yenile` icin kullaniciya manuel talimat verilmiyor.
- Kurulum sonrasi resolver ve gercek `initialize` health check otomatik calistiriliyor.
- Sonuc `Hazır` ise basari, kurulu ama baslatilamiyorsa uyari ve son hata gosteriliyor.
- Windows kullanici npm klasoru resolver'a eklendi; global kurulum sonrasi eski process PATH'i nedeniyle yeniden baslatma zorunlulugu azaltildi.
- GitHub arşivi indirme/çıkartma/doğrulama hatalarında geçici arşiv temizliği eklendi.
- Uygulama build: 0 uyari, 0 hata.
- LSP odakli testler: 14/14 basarili.

## Faz 7 - Marketplace ve eklenti yasam dongusu

Durum: Devam ediyor (sahte katalog temizligi)

### Yapilacaklar

- Gercek imzali katalog yapisi tamamlanana kadar indirilebilir sekmede sahte veri gosterilmemesi.
- Dahili plugin, harici plugin ve LSP server icin ayri manifest modelleri kullanilmasi.
- Dahili plugin'lerde guncelleme butonunun kaldirilmasi.
- Harici plugin indirme, SHA-256 ve manifest dogrulama akisinin UI'da acik gosterilmesi.
- LSP kurulumlarinin marketplace plugin indirmesiyle karistirilmamasi.

### Uygulanan ilk dilim

- Placeholder Kotlin/Swift kayitlari kaldirildi.
- Guvenilir katalog hazir degilken `GetAvailablePlugins()` bos liste donuyor.
- Boylece indirilebilir eklentiler sekmesi sahte paketleri gercek kurulum secenegi gibi sunmuyor.
- Opsiyonel imzali katalog yukleyici eklendi; `MDAI_PLUGIN_CATALOG_URL`, `MDAI_PLUGIN_CATALOG_SIGNATURE_URL` ve `MDAI_PLUGIN_CATALOG_PUBLIC_KEY` birlikte verilmeden uzak katalog etkinlesmiyor.
- Katalog ve imza adresleri yalnizca HTTPS ve guvenilir GitHub alan adlarinda kabul ediliyor.
- Katalog imzasi ve her plugin manifesti indirme oncesi dogrulaniyor.
- Eklentiler penceresi katalogu asenkron yukluyor ve yapilandirilmamis durumu kullaniciya acikca bildiriyor.
- Uygulama build: 0 uyari, 0 hata.
- Marketplace guvenlik ve LSP odakli testler: 19/19 basarili.

### Kapanis kaniti

- Her sekmedeki veri kaynagi ve varlik tipi nettir.
- Bos katalog gercekten bos ise kullaniciya neden gosterilir.
- Guncelleme butonu yalnizca guncellenebilir paketlerde vardir.

## Faz 8 - Test, telemetry ve manuel kabul

Durum: Devam ediyor (otomatik testler ve regresyon kaniti)

### Otomatik testler

Yeni test siniflari onerisi:

- `LspExecutableResolverTests`
- `LspServerRegistryTests`
- `LspStatusTests`
- `LanguageServerSessionTests`
- `PluginCatalogPresentationTests`

Test senaryolari:

- PATH'te `.cmd` bulunan Windows senaryosu.
- Sadece `.ps1` bulunan ve baslatilamamasi gereken senaryo.
- Proje `node_modules/.bin` kaynaginin global PATH'ten once gelmesi.
- HTML ve CSS'in dogru server kaydina baglanmasi.
- HTML + C# projesinde ayri session kullanilmasi.
- Initialize basarisizliginin `Failed` olarak raporlanmasi.
- Dahili plugin'in LSP olarak listelenmemesi.
- Dahili plugin icin guncelleme butonu gosterilmemesi.

### Manuel kabul testi

1. Bos bir HTML projesi ac.
2. `index.html` dosyasini ac.
3. HTML LSP statusunu kontrol et.
4. HTML dosyasinda bilerek hata olustur.
5. Diagnostics akisinin calistigini kontrol et.
6. CSS dosyasi ac ve CSS server durumunu kontrol et.
7. C# + HTML iceren karma proje ac.
8. C# ve HTML dosyalarinda sirayla tamamlama/diagnostics dene.
9. Eklentiler penceresini ac ve uc sekmede kayitlarin dogru kavrami gosterdigini kontrol et.
10. Bir LSP'yi kapat veya PATH'ten kaldir, status refresh yap ve gercek hata metnini kontrol et.

### Kapanis kaniti

- `dotnet build BasucuIDE/mdaiAgent.csproj --nologo -v:minimal`
- `dotnet test tests/mdaiAgent.Tests/mdaiAgent.Tests.csproj --nologo -v:minimal`
- Gercek HTML projesi ile manuel kabul testi.
- Calisan LSP process'lerinin kapatma sonrasi kalmadiginin kontrolu.

### Guncel dogrulama

- LSP resolver, language registry, gercek process ve marketplace guvenlik testleri: 19/19 basarili.
- Tam test projesi: 201/201 basarili.
- Dogrudan belirtilen `.txt` ve `.md` gorev dosyalarinin icerigi context markdown'a eklenerek kalan context discovery regresyonu giderildi.
- Test projesinde mevcut `NU1603` ve `VSTHRD200` uyarilari bulunuyor.

### Otomatik test kapanisi

- LSP, plugin guvenligi, signed catalog, resolver, language registry ve tum mevcut regresyon testleri birlikte basarili.
- Release uygulama build'i daha once 0 uyari, 0 hata ile dogrulandi.

## 5. Degisiklik Sinirlari

- Ilk fazlarda ChatFlowService, ToolExecutor ve AI router'a dokunulmayacak.
- LSP ve plugin degisiklikleri kendi sinirlari icinde tutulacak.
- Buyuk refactor tek seferde yapilmayacak; her faz build ve test ile kapanacak.
- UI metni, durum modeli ve runtime davranisi ayni kavramlari gostermeli.
- Bir madde, sadece kod eklendi diye tamamlanmis sayilmayacak; kabul kriteri ve kanit gerekecek.

## 6. Ilk Uygulama Adimi

Ilk kod degisikligi olarak Faz 0 ve Faz 1 birlikte ele alinacak:

1. Mevcut LSP ve plugin modelleri icin test edilebilir sinirleri belirle.
2. LSP sekmesi ile kod denetleyicileri sekmesinin veri kaynaklarini ayir.
3. UI'da `Yüklü eklentiler` adini `Kod denetleyicileri` olarak duzelt.
4. Dahili plugin'lerde guncelleme butonunu kaldir.
5. Build ve hedef testleri calistir.

Bundan sonra Faz 2'de ortak executable resolver yazilacak. Resolver tamamlanmadan LSP status tiklerinin yeniden tasarlanmasi kalici cozum sayilmayacak.

## 7. Durum Ozeti

- [x] Sorun alani analiz edildi.
- [x] LSP ile diagnostic plugin ayrimi dogrulandi.
- [x] HTML executable cozumleme uyumsuzlugu dogrulandi.
- [x] Tek process/cok dil riski dogrulandi.
- [x] Marketplace ve guncelleme akisinin eksikligi dogrulandi.
- [x] Faz 0 - Sozlesme ve regresyon zemini
- [x] Faz 1 - LSP ve plugin arayuzlerini ayirmak (ilk UI dilimi)
- [x] Faz 2 - Ortak executable resolver
- [~] Faz 3 - LSP health check ve gercek durum (runtime durum dilimi)
- [x] Faz 4 - Dil bazli LSP session yonetimi
- [~] Faz 5 - Tum desteklenen diller icin gercek dunya LSP akisi
- [x] Faz 6 - Kurulum ve tekrar deneme
- [~] Faz 7 - Marketplace ve eklenti yasam dongusu (sahte katalog temizligi)
- [~] Faz 8 - Test, telemetry ve manuel kabul (otomatik testler tamamlandi)
