Dili Değiştir: 🇹🇷 Türkçe | [🇺🇸 Read in English](README.md)

---

<p align="center">
  <img src="assets/banner.png" alt="Yengi Banner" width="100%"/>
</p>

# 🚀 Yengi AI IDE

> **Y**our **E**ngineering **N**exus, **G**enerative **I**ntelligence — *Mühendisliğinin Merkezi, Üretken Zekâ.*  
> *"Yengi: Uğraşlarının sonunda ulaştığın o mutlu ve başarılı sonucun adı."*

[![Build & Test](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml)
[![Sürüm: v1.15](https://img.shields.io/badge/S%C3%BCr%C3%BCm-v1.15-blue.svg)](https://github.com/mdaiWorks/yengi/releases)
[![Altyapı: .NET 10 LTS](https://img.shields.io/badge/Altyap%C4%B1-.NET%2010%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6.svg)](https://microsoft.com/windows)
[![Lisans: AGPL-3.0](https://img.shields.io/badge/Lisans-AGPL--3.0-green.svg)](LICENSE)
[![HuggingFace: Yengi Router](https://img.shields.io/badge/HuggingFace-yengi--router%3A1.5b-FFD21E.svg)](https://huggingface.co/)

**Yengi; yerel ve bulut AI modelleriyle çalışan, proje dosyalarını okuyup değiştiren, terminal/derleme/test adımlarını yürüten ve kendi kendini düzelten döngüye sahip .NET 10 (LTS) altyapılı açık kaynaklı Windows AI IDE'sidir.**

---

## 🎬 Görsel Vitrin

| 🌐 Web Uygulaması & Görsel Üretimi | 🧊 Canlı 3D Blender Copilot |
| :---: | :---: |
| ![Web App & Görsel Üretimi](assets/gorselUretimvetetris.gif) | ![Blender Copilot](assets/Blender.gif) |

| 🎮 Unity Oyun Motoru Copilot | 🖥️ Modern IDE Çalışma Alanı |
| :---: | :---: |
| ![Unity Copilot](assets/unity.gif) | ![Main Workspace](assets/arayuz.png) |

▶️ **[YouTube Canlı İnceleme ve Tetris Oyunu Yapımı Videosunu İzle](https://youtu.be/l0tdhvCmwNI)**

---

## 🧠 Otonom Agent Mimari Akışı

Yengi basit bir "AI sohbet kutusu" değildir. Kod projeniz üzerinde **Plan → Araç Çağrısı → Kod Değişikliği → Doğrulama → Otomatik Düzeltme** döngüsünü otonom olarak çalıştırır.

```mermaid
flowchart TD
    A["👤 Kullanıcı İsteği / Hedef"] --> B["📝 Plan Modu & Görev Bölümleme"]
    B --> C["🤖 Agent Yöneticisi & Araç Seçimi"]
    C --> D{"⚡ Araç Çalıştırma"}
    D -->|"Kod Değişikliği"| E["📝 Dosya Oluştur / Düzenle"]
    D -->|"Terminal / Derleme"| F["⚙️ Build & Test Komutları"]
    D -->|"Copilot Entegrasyonu"| G["🧊 Blender / Unity Otomasyonu"]
    E --> H{"🔍 Doğrulama Döngüsü"}
    F --> H
    H -->|"✅ Başarılı"| I["🎉 Tamamlandı & Raporla"]
    H -->|"❌ Derleme / Test Hatası"| J["🩹 Kendi Kendini İyileştiren Agent"]
    J --> C
```

---

## 🎨 Çift Aşamalı 3D & Oyun Motoru Architect Mimarisi

Yengi, Blender 3D ve Unity Engine entegrasyonlarında ilkel basit şekiller üretmek yerine **2 Aşamalı Otonom AI Pipeline** çalıştırır. Tasarım kararlarını kod sentezinden ayırarak yüksek kaliteli 3D varlıklar ve oyun bileşenleri oluşturur:

```mermaid
flowchart TD
    A["👤 Kullanıcı İsteği<br/>('Low poly meşe ağacı yap' / 'Kamera takip sistemi ekle')"] --> B{"⚙️ 3D Prompt Mühendisi Açık mı?"}
    B -- "EVET (Varsayılan - Ayarlanabilir)" --> C["🎨 1. Aşama: 3D Asset/Component Architect"]
    C --> D["📝 Detaylı 3D Şartname (Blueprint)<br/>(Metrik Ölçüler, Renk Paleti, Poligon Hedefi, Topoloji)"]
    D --> E["⚡ 2. Aşama: Blender (bpy) / Unity (C#) Kod Üretici"]
    B -- "HAYIR (Doğrudan İstem)" --> E
    E --> F["🚀 Canlı TCP Socket İletimi & Otomatik Undo Noktası"]
    F --> G["🧊 Blender 3D Viewport / 🎮 Unity Sahne Güncellemesi"]
    G --> H{"🔍 Derleme & Çalıştırma Kontrolü"}
    H -- "✅ Başarılı" --> I["📸 Viewport Ekran Görüntüsü & Sonucu Sun"]
    H -- "❌ Hata / Derleme Uyarısı" --> J["🩹 Kendi Kendini Düzelten Self-Healing Döngüsü"]
    J --> E
```

- **1. Aşama (Sanatçı Rolü)**: Kullanıcının fikrini parçalara ayırır, metre bazında ölçüler, renk paletleri ve materyal özellikleri belirler.
- **2. Aşama (Yazılımcı Rolü)**: Şartnameye ve canlı sahne RAG verisine bakarak hatasız `bpy` Python veya Unity C# Editor kodunu üretir.
- **Esnek Kontrol**: **Çalışma Alanı Ayarları** (`⚙️`) menüsünden tek tıkla açılıp kapatılabilir.

---

## 👁️ Canlı Viewport Vizyonu — AI Gözleri 3D Sahnenizde

**Çok modlu (multimodal) uyumlu bir model** (GPT-4o, Claude 3.5 Sonnet, Gemini 1.5/2.0) kullanıldığında Yengi, Blender 3D viewport'unu gerçek zamanlı olarak **görebilir** ve görsel değerlendirme ile düzeltme yapabilir:

```mermaid
flowchart LR
    A["🧊 Blender OpenGL Viewport"] -->|"OpenGL Anlık Görüntü"| B["📸 yengi_viewport_preview.png<br/>(AppData/Local/Temp)"]
    B -->|"Base64 + TCP Socket"| C["🖥️ Yengi AI Çekirdeği"]
    C -->|"Vision API Çağrısı"| D["👁️ Çok Modlu Model<br/>(GPT-4o / Claude 3.5 / Gemini)"]
    D -->|"Görsel Analiz"| E{"🔍 Sahne OK mi?"}
    E -- "✅ Doğru görünüyor" --> F["🎉 Sonucu Kullanıcıya Sun"]
    E -- "❌ Geometri / Renk Yanlış" --> G["🩹 Düzeltici bpy Kodu Üret"]
    G -->|"Otomatik Çalıştır"| A
```

1. **Yakala** — Blender, OpenGL viewport anlık görüntüsünü `yengi_viewport_preview.png` olarak kaydeder
2. **İlet** — Görüntü Base64 ile kodlanır ve TCP soketi (port 8181) üzerinden Yengi'ye gönderilir
3. **İncele** — Çok modlu AI modeli render edilmiş 3D sahneyi görsel olarak analiz eder
4. **Düzelt** — Geometri, materyal veya oranlar yanlışsa AI otomatik `bpy` kodu üretip çalıştırır

> [!TIP]
> Blender'ın N-Panel'indeki (viewport'ta `N` tuşu) **"Viewport Resmi Al"** butonuyla istediğiniz zaman anlık görüntü alabilirsiniz; Çift Aşamalı Pipeline ise her işlem sonrası bunu otomatik yapar.

---

## 💡 Neden Yengi?

Yengi yalın bir fikir etrafında doğdu: **Yapay zeka geliştirme ortamınız tek bir sağlayıcıya, modele veya kullanım kotasına bağımlı kalmamalıdır.**

Ollama üzerinden yerel modelleri çalıştırın, kendi API anahtarlarınızı (Claude, OpenAI, Gemini) bağlayın veya Yengi'nin özel fine-tune edilmiş yerel yönlendiricisini (Router) kullanın. Araçlarınız ve verileriniz üzerinde tam kontrol sahibiyken otonom agent iş akışlarıyla geliştirme yapın.

---

## ✨ Öne Çıkan Özellikler

- 🧠 **Özel 1.5B Yerel AI Router**: Doğru AI iş akışını ve araç bağlamını otonom olarak seçmek üzere özel eğitilmiş 1.5B yönlendirme modeli.
- 🎛️ **4 Uzmanlaşmış Geliştirme Modu**: **Code IDE**, **Görsel Üretim Stüdyosu**, **Blender 3D Copilot** ve **Unity Oyun Motoru Copilot**.
- 🔒 **Yerel Odaklı & Model Bağımsız**: **Ollama (Qwen 35B, DeepSeek-R1)**, Claude 3.5, OpenAI veya Gemini ile çalışır. Yerel sağlayıcı seçildiğinde kod verileriniz bilgisayarınızda kalır.
- 🛡️ **Kendi Kendini İyileştiren Doğrulama Döngüsü**: Derleme ve test hatalarını (`dotnet build`, `npm test`, Python linters) otomatik tespit eder ve insan müdahalesi olmadan düzeltir.
- 🔄 **Tek Tıkla Otomatik Güncelleme**: Entegre GitHub Releases API kontrolcüsü ile yeni sürümleri otomatik algılar ve günceller.
- 🧊 **Blender & Unity Copilot Entegrasyonu**: Canlı 2 yönlü JSON-RPC iletişimi, Sahne RAG verisi, Çift Aşamalı Architect Mimarisi, self-healing döngüsü ve otomatik Undo noktaları.
- 👁️ **Canlı Viewport Vizyonu**: Çok modlu (multimodal) AI modelleri (GPT-4o, Claude 3.5, Gemini) OpenGL viewport görüntülerini inceleyerek 3D sahneyi **görür** ve kapalı döngüde geometri/renk düzeltmeleri yapar.
- 🔎 **Araştırma & Sohbet Modu**: Canlı web araması (Tavily API ve ücretsiz DuckDuckGo yedekleme), fikir geliştirme ve AI görselleri gömülü otomatik Markdown (`.md`) rapor üretimi.

- ⚡ **.NET 10 (LTS) Gücü**: **200+ birim testi** ile doğruluk ve yüksek performans garantisi.
- 🧰 **30+ Yerel Agent Aracı**: Dosya arama, Git yönetimi, terminal çalıştırma, RAG indeksleme ve canlı web önizleme.

---

## 🗺️ Gelecek Güncellemeler & Yol Haritası (Upcoming Roadmap)

Yengi yeniliklerle gelişmeye devam ediyor! Yakın gelecekte gelmesi planlanan büyük güncellemeler:

- 🌐 **Otonom Tarayıcı & Görsel Ajanı (Autonomous Browser & Vision Agent):** Bilgisayar ekranını ve web tarayıcısını doğrudan okuyarak (Vision AI) AppStore işlemleri, web formları ve karmaşık tarayıcı görevlerini otonom tamamlama.
- 🎨 **İnteraktif Sohbetli Görsel Düzenleme & Seed Kilitleme:** Karakter tutarlılığı için `🌱 Seed` sabitleme ve sohbet ekranından *"şimdi sağ kolunu kaldır"*, *"bisikleti mavi yap"* gibi talimatlarla görselleri sohbet içinde canlı düzenleme (`qwen-image-edit` & `/v1/images/edits`).

---

## 📖 Detaylı Dokümantasyon & Tüm Araçlar Rehberi

30'dan fazla **AI Agent Aracı**, Kendi Kendini İyileştiren Doğrulama Döngüsü detayları, RAG mimarisi ve arayüz buton kılavuzu için **[Detaylı Dokümantasyon Dosyasına (DOCS_FULL.tr.md)](DOCS_FULL.tr.md)** göz atabilirsiniz | **[Read Full English Docs (DOCS_FULL.md)](DOCS_FULL.md)**.

---

## 🚀 Hızlı Başlangıç

### Yöntem A: Kurulum Exe'si (Önerilen)
1. En son `Yengi_Setup.exe` kurulum dosyasını [GitHub Releases](https://github.com/mdaiWorks/yengi/releases/latest) sayfasından indirin.
2. Kurulumu tamamlayın ve Yengi'yi başlatın.
3. **Ayarlar** sayfasından istediğiniz modeli (Ollama, Anthropic, OpenAI veya Gemini) seçip kodlamaya başlayın!

### Yöntem B: Kaynak Koddan Çalıştırma (.NET 10 SDK)
```bash
# Depoyu klonla
git clone https://github.com/mdaiWorks/yengi.git
cd yengi

# .NET 10 ile derle ve çalıştır
dotnet run --project BasucuIDE/mdaiAgent.csproj
```

---

## 👤 Geliştiriciden Not

> *"Ben bir öğretmenim, profesyonel bir şirket yazılımcısı değilim."*

Yengi'yi bir **Mimarlık ve AI Orchestrator (Yapay Zeka Yöneticisi)** rolü üstlenerek inşa ettim. Kodu üretmek için AI asistanlarından yararlanırken mimariyi, doğrulama döngülerini ve güvenlik sınırlarını kendim kurguladım.

Yengi **%100 Ücretsiz, Açık Kaynaklı ve Telemetrisizdir**. Eğer Yengi projesi size ilham verdiyse GitHub'da ⭐ **Star vererek** destek olabilirsiniz!

---

## 📄 Lisans

[AGPL-3.0 Lisansı](LICENSE) altında dağıtılmaktadır.
