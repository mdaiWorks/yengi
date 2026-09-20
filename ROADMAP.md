# 🚀 Yengi IDE - Geliştirme Yol Haritası (Roadmap)

> **Vizyon:** Dünyanın İlk AI-Powered Oyun, 3D ve Medya Destekli Gelişmiş Kod Editörü.

---

## 🟢 Faz 1: Çekirdek IDE & Temel Stabilizasyon (TAMAMLANDI ✅)
- [x] **WPF .NET 8 Mimarisi:** Yüksek performanslı arayüz ve sekme yönetimi.
- [x] **LSP Entegrasyonu:** Pyright, OmniSharp, TypeScript vb. dili içi hata kontrolü.
- [x] **Trae.ai Stili Auto-Update:** GitHub tabanlı arka planda sessiz güncelleme sistemi.
- [x] **Yengi Router (Lokal Model):** 1.5B parametreli fine-tuned araç seçim ajanı.
- [x] **AppData & Lisanslama:** AGPL-3.0 lisansı ve `%APPDATA%\Yengi` mimarisi.
- [x] **Çoklu Dil Desteği:** Türkçe, İngilizce ve Çince tam arayüz entegrasyonu.

---

## 🎨 Faz 2: AI Görsel & Medya Üretim Stüdyosu (Image & Media Studio)
*Hedef: Kullanıcının kod yazarken proje klasörüne doğrudan AI ile görsel/asset üretmesini sağlamak.*

- [ ] **`GenerateImage` Ajan Aracı:** OpenAI DALL-E 3, Flux (Replicate/OpenRouter) ve Yerel ComfyUI/Automatic1111 entegrasyonu.
- [ ] **Proje Otomatik Asset Kaydı:** Üretilen görsellerin otomatik `assets/` veya belirtilen dizine PNG/JPG olarak kaydedilmesi.
- [ ] **Görsel Önizleme Paneli:** Üretilen görsellerin IDE içerisinde anında görüntülenmesi.

---

## 🧊 Faz 3: Blender 3D AI Bridge (3D Nesne & Animasyon Asistanı)
*Hedef: Yengi üzerinden Blender'a canlı bağlanarak 3D model ve materyal ürettirmek.*

- [ ] **Blender Python Socket Köprüsü:** Yengi ile açık olan Blender arasında arka plan haberleşmesi.
- [ ] **`ExecuteBlenderScript` Ajan Aracı:** Yapay zekanın ürettiği `bpy` (Blender Python) kodlarının canlı çalıştırılması.
- [ ] **Otomatik FBX/OBJ Dışa Aktarım:** Üretilen 3D modellerin doğrudan projenin modeller klasörüne aktarılması.

---

## 🎮 Faz 4: Unity Game Engine Copilot (Canlı Oyun Motoru Asistanı)
*Hedef: Yengi'yi Unity Editor ile canlı konuşturarak oyun geliştirme süreçlerini otomatize etmek.*

- [ ] **Unity Editor C# Bridge (IPC/WebSocket):** Yengi'nin Unity Editor ile canlı haberleşmesini sağlayan hafif Unity paketi.
- [ ] **`UnityEditorAction` Ajan Aracı:** Sahneye GameObject ekleme, bileşen (Component) bağlama ve değer değiştirme emri.
- [ ] **Canlı Unity Log & Hata Ayıklayıcı:** Unity konsol hatalarının anında Yengi Chat'e akması ve AI tarafından otomatik düzeltilmesi.

---

## 🏁 Faz 5: Büyük Lansman & GitHub Yayınlanması
- [ ] **Entegrasyon ve Stres Testleri:** Tüm modüllerin bir arada kusursuz çalışmasının doğrulanması.
- [ ] **Demo Video & GIF Hazırlığı:** Unity, Blender ve Görsel üretimini gösteren etkileyici tanıtım materyalleri.
- [ ] **GitHub Release & Yayın:** v1.0.0 "Ultimate Edition" lansmanı.
