# BAŞUCU DANIŞMA DOSYASI: ÖZGÜR VE OTONOM AI IDE (TRAE-KILLER)

## 1. PROJE ÖZETİ VE VİZYON
Bu projenin amacı; ticari AI editörlerinin (Trae, Cursor, Windsurf vb.) getirdiği yapay zeka limitlerini tamamen ortadan kaldırmaktır. Sistem, kullanıcının kendi Nvidia API anahtarını (Nvidia NIM) kullanarak çalışacaktır. Yapay zeka sadece metin üretmekle kalmayacak; dosyaları okuyabilecek, yeni kod dosyaları oluşturup düzenleyebilecek ve terminalde komut çalıştırabilecektir (Otonom Ajan / Solo Modu).

## 2. TEKNOLOJİK ALTYAPI (WINDOWS NATIVE)
- **Dil ve Mimari:** C# / .NET 8.0 (Windows Forms veya WPF - Tamamen bağımsız tek bir .exe çıktısı üretmek için)
- **AI Entegrasyonu:** `Betalgo.OpenAI` NuGet paketi veya standart `HttpClient` (Nvidia API, OpenAI standartlarıyla %100 uyumludur)
- **Zeka Motoru:** Nvidia Inference API (Örn: `mistralai/codestral-22b` veya `meta/llama-3.1-70b-instruct`)

---

## 3. ADIM ADIM İŞ PLANI VE YAPILACAKLAR LİSTESİ

### 🟩 ADIM 1: Arayüzün Kurulması (Trae.ai Tasarımı)
Yapay zeka asistanı, Windows üzerinde şık bir karanlık tema (Dark Mode) kullanarak 3 ana dikey bölmeden oluşan bir masaüstü penceresi tasarlamalıdır:
1. **Sol Panel (Dosya Ağacı):** Kullanıcının seçtiği bir proje klasöründeki tüm dosyaları (`.py`, `.dart`, `.txt` vb.) listeleyen bir `TreeView` yapısı.
2. **Orta Panel (Kod Editörü ve Terminal):**
   - Üstte, sol taraftan tıklanan dosyanın içeriğini gösterecek ve manuel düzenlemeye izin verecek bir metin editörü alanı.
   - Altta, projeyi test etmek için çalıştırılan komutların çıktılarını canlı gösteren bir Terminal/Konsol simülasyon penceresi.
3. **Sağ Panel (AI Agent Chat):** Altında bir metin giriş kutusu olan, yapay zekanın kullanıcıyla ve kendi kendine konuşmalarını loglayacağı Sohbet Penceresi.

### 🟩 ADIM 2: Nvidia API Bağlantısının Kurulması (Beyin)
1. C# projesine gerekli HTTP kütüphaneleri eklenmelidir.
2. Kullanıcının arayüzden gireceği Nvidia API anahtarını okuyan bir yapı kurulmalıdır.
3. API İstek Ayarları:
   - **Base URL:** `https://integrate.api.nvidia.com/v1`
   - **Model:** `mistralai/codestral-22b` (Kodlama için optimize)
4. Chat kutusundan bir şey yazıldığında, tüm projedeki açık dosyanın içeriği "Context" olarak Nvidia API'ye gönderilmeli ve gelen yanıt sağ panele yazdırılmalıdır.

### 🟩 ADIM 3: "Otonom Ajan" (Solo) Yeteneklerinin Eklenmesi (Eller ve Kollar)
Programın kendi kendine kod yazıp dosya oluşturabilmesi için "Function Calling" (Fonksiyon Çağırma) mantığı C# tarafında kurulmalıdır. Nvidia API'ye sistem promptu (System Prompt) ile şu 3 aracı (Tool) tetikleyebileceği öğretilmelidir:

1. **`CreateOrUpdateFile(string filePath, string content)`:** Yapay zeka yeni bir dosya oluşturmak veya var olan kodu güncellemek istediğinde C# koduna bu JSON emrini gönderir. C# bu emri yakalayıp Windows diskinde o dosyayı yazar.
2. **`ReadFile(string filePath)`:** Yapay zeka başka bir dosyanın içeriğine bakmak istediğinde bu fonksiyon tetiklenir ve C# dosya içeriğini okuyup AI'ye besler.
3. **`ExecuteTerminalCommand(string command)`:** Yapay zeka yazdığı kodu test etmek istediğinde (Örn: `python bot.py`) C# arka planda bir `Process` (cmd veya powershell) başlatır, komutu çalıştırır ve terminal çıktılarını hem orta panele basar hem de AI'ye "Kod başarıyla çalıştı" veya "Şu hata alındı" diye geri bildirir.

### 🟩 ADIM 4: Otomatik Güvenlik ve Yedekleme Sistemi
Yapay zeka otonom çalışırken (Solo Modu) mevcut çalışan kodları bozmaması için şu emniyet sübapları C# koduna gömülmelidir:
1. Yapay zeka `CreateOrUpdateFile` fonksiyonunu tetiklediği an, C# o dosyanın orijinal halini otomatik olarak projenin içindeki bir `backup/` klasörüne o günün tarihiyle yedeklemelidir.
2. `ExecuteTerminalCommand` fonksiyonu çalıştırılmadan önce, arayüzde kullanıcıya "AI bu komutu çalıştırmak istiyor: [Komut]. Onaylıyor musunuz?" diye soran bir Onay/Red butonu çıkarılmalıdır.

---

## 4. YAPAY ZEKA AJANINA TALİMAT (PROJEYİ BAŞLATIRKEN OKUTULACAK)
"Yukarıdaki planı eksiksiz okuduysan; bana C# (.NET) kullanarak bu otonom yapay zeka editörünü adım adım yazmaya başla. Her adım bittiğinde kodu bana sun ve bir sonraki adıma geçmek için benden onay iste. İlk olarak ADIM 1'deki arayüz tasarım kodlarıyla başla."