# 🗺️ Yengi — Ana Yol Haritası (Master Roadmap)

> Bu dosya Yengi'nin tüm gelecek güncelleme planlarını tek bir yerden takip eder.
> Her büyük özellik kendi bölümünde detaylıca açıklanmıştır.

---

## 📋 Özet Tablo

| # | Özellik | Versiyon | Durum |
|---|---------|----------|-------|
| **1** | 🎨 Seed Kontrolü & Sohbetli Görsel Düzenleme | v1.16 | ✅ Tamamlandı |
| **2** | 🌐 Otonom Tarayıcı & Görsel Ajanı | v2.x | 📌 Planlandı |
| **3** | 🎮 ControlNet & Oyun Sprite Üretimi | v1.17–v1.20 | 📌 Planlandı |

---

---

# 🎨 Plan 1 — Seed Kontrolü & Sohbetli Görsel Düzenleme (v1.16)

## ✅ Durum: Tamamlandı

### Ne Yapıldı?

**Seed (Tohum) Kontrol Sistemi:**
- `AppSettings` sınıfına `ImageStudioSeed` eklendi → `-1` = rastgele, pozitif sayı = sabit
- Görsel Stüdyosu Ayarlarına Seed kutusu eklendi
- Üretilen her görselin altında `🌱 Seed (Tohum): xxxxxxxx` etiketi gösteriliyor
- `/v1/images/generations` isteğine `seed` parametresi eklendi

**Sohbetli Görsel Düzenleme:**
- `IsImageEditIntent()` → keyword tabanlı niyet algılama ("now", "şimdi", "change", "make" vb.)
- `TryExtractPreviousPrompt()` → sohbet geçmişinden önceki kullanıcı prompt'unu bulur
- Edit niyeti algılandığında önceki prompt + yeni prompt birleştirilir
- Örnek: `"A cute robot on a desk"` → `"now make it hold a red cup"` → `"A cute robot on a desk, now make it hold a red cup"`

> [!NOTE]
> `/v1/images/edits` endpoint'i rapid-mlx'te `size` ve `steps` parametrelerini görmezden geliyor.
> Bu nedenle her zaman `/v1/images/generations` kullanılıyor.

---

---

# 🌐 Plan 2 — Otonom Tarayıcı & Görsel Ajanı (v2.x)

## 📌 Durum: Planlandı

### Vizyon

Yengi'yi sadece bir Kod IDE'si olmaktan çıkarıp, `WebView2` ve `Multimodal Vision` kullanarak web sitelerinde otonom işlem yapabilen bir **Tarayıcı Ajanı** haline getirmek.

### Aşamalar

**Aşama 1 — WebView2 Entegrasyonu:**
- `Microsoft.Web.WebView2` NuGet paketi eklenir
- Sağ panelde canlı Chromium tarayıcı penceresi
- Adres çubuğu, Yenile, Geri/İleri butonları

**Aşama 2 — Tarayıcı Araç Tanımları:**
- `OpenBrowserUrl(url)`, `ClickBrowserElement()`, `TypeBrowserInput()`
- `CaptureBrowserScreenshot()`, `ExtractBrowserDOM()`, `ScrollBrowser()`

**Aşama 3 — Vision & DOM Analiz Döngüsü:**
- Ekran görüntüsü → Gemini Vision / Claude → koordinat tespiti
- DOM ağacı + görsel hibrit analiz

**Aşama 4 — Human-in-the-Loop Güvenlik Kapısı:**
- Şifre / 2FA alanı tespit edilince otonom mod duraklar
- Hassas veriler AI geçmişine kaydedilmez

**Aşama 5 — Otonom Senaryo Kütüphanesi:**
- App Store Connect & Google Play Console form asistanı
- Vercel / Firebase otomatik dağıtım
- Canlı E2E web testi

---

---

# 🎮 Plan 3 — ControlNet & Oyun Sprite Üretimi (v1.17–v1.20)

## 📌 Durum: Planlandı

### Vizyon

2D oyun geliştirme iş akışı için aynı karakteri farklı pozlarda tutarlı şekilde üretmek.

> [!NOTE]
> Seed "aynı prompt + aynı seed = aynı görsel" garantisi verir ama prompt değişince karakter değişir.
> Gerçek karakter tutarlılığı için modele **referans görsel** verilmesi şart → ControlNet / IP-Adapter.

### Karşılaştırma

| Yöntem | Karakter Tutarlılığı | Poz Kontrolü |
|--------|---------------------|--------------|
| Seed (mevcut) | ❌ Prompt değişince karakter değişir | ❌ |
| img2img | ~%60 | ❌ |
| ControlNet | ~%75 | ✅ |
| ControlNet + IP-Adapter | ~%90+ | ✅ |

### Versiyon Planı

**v1.17 — ComfyUI Bağlantısı:**
- Mac'te ComfyUI kurulumu (bir kez): `python main.py --listen 0.0.0.0 --port 8188`
- Yengi'ye `ControlNetBaseUrl` ayarı eklenir
- Basit img2img: önceki görsel → yeni prompt → ~%60 tutarlı karakter

**v1.18 — ControlNet Pose Kontrolü:**
- Hazır poz iskelet kütüphanesi: `t-pose`, `idle`, `walk`, `run`, `attack`, `jump`
- Niyet algılama: `"yürürken"` → `walk.json`, `"koşarken"` → `run.json`
- Tutarlılık: ~%75

**v1.19 — Sprite Sheet Üretimi:**
- Tek komut: `"Bu karakter için sprite sheet üret"`
- idle + walk (4 kare) + run (4 kare) + attack (3 kare) → tek `sprite_sheet.png`
- Unity Sprite Editor'a doğrudan yüklenebilir format + JSON metadata

**v1.20 — IP-Adapter Karakter Tutarlılığı:**
- Referans görseldeki yüz, renk, stil → yeni görüntüye uygulanır
- ControlNet (poz) + IP-Adapter (görünüm) = ~%90+ tutarlılık ✅

---

## 🔗 İlgili Kod Dosyaları

- [`ChatFlowService.cs`](file:///e:/andoridGames/mdaiAgent/BasucuIDE/ChatFlowService.cs) — Görsel üretim ana akışı
- [`WorkspaceSettingsWindow.xaml`](file:///e:/andoridGames/mdaiAgent/BasucuIDE/WorkspaceSettingsWindow.xaml) — Görsel Stüdyosu UI
- [`SettingsWindow.xaml.cs`](file:///e:/andoridGames/mdaiAgent/BasucuIDE/SettingsWindow.xaml.cs) — AppSettings sınıfı
