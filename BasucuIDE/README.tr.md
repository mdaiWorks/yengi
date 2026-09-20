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
| **Router** | Küçük/ucuz bir modelle hangi araçların gerektiğine karar verme | `Services/Router/*` |
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
