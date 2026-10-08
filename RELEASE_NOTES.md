# Yengi v1.15 Release Notes

### 🚀 Yenilikler / New Features
- **Yerel Görsel Modeli Ayarları (Çözünürlük & Step):** Görsel Stüdyosu Ayarları (⚙️) menüsüne Çözünürlük (Resolution: 512x512, 768x768, 1024x1024 vb.) ve Adım Sayısı (Step: 15, 20, 30 vb.) ayarları eklendi.
- **Mac / Apple Silicon MLX Qwen-Image Desteği:** Yerel Qwen-Image / Flux modellerinde 512x512 ve 15-20 step seçenekleriyle 10-15 dakikalık üretim süreleri **1.4 dakikaya (~87 saniye)** düşürüldü.
- **Modern Dark ComboBox Tasarımı:** Windows XP varsayılan beyaz açılır kutu teması VS Code uyumlu modern karanlık tema (`#1e1e1e` background, `#007acc` hover, `#00ffb7` selected) ile yenilendi.

### 🐛 Düzeltmeler / Bug Fixes
- **WPF ComboBoxItem Metin Ayrıştırma:** ComboBoxItem nesne öneki (`System.Windows.Controls.ComboBoxItem`) temizlenerek seçilen çözünürlük ve step değerlerinin yerel sunucuya (`rapid-mlx` / `mflux`) eksiksiz iletilmesi sağlandı (`a8702b0`).
- **HttpClient Timeout Uzatıldı:** Yerel MLX görsel üretimi sırasında oluşan zamanaşımı (timeout) hatasını engellemek için HTTP istek süresi 10 dakikaya çıkarıldı (`7b9557a`).
- **Pollinations URL Çözünürlük Parametresi:** Ücretsiz gateway kullanımında seçilen çözünürlük URL parametresine (`width` & `height`) aktarıldı (`39011b1`).

### 🔧 Sürüm & Altyapı / Maintenance
- Dotnet publish (win-x64 self-contained) ve Inno Setup installer `dist/Yengi_Setup_v1.15.exe` derlendi.
