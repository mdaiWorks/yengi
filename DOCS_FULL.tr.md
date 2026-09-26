# 📖 Yengi AI IDE — Detaylı Teknik Dokümantasyon ve Araç Rehberi

Dili Değiştir: 🇹🇷 Türkçe | [🇺🇸 Read in English](DOCS_FULL.md)

---

**Yengi AI IDE** için hazırlanan kapsamlı teknik referans dokümanına hoş geldiniz. Bu doküman; mimari detayları, 30'dan fazla araç tanımını, doğrulama döngüsü mekanizmasını, RAG indeksleme yapısını ve gelişmiş kullanıcılar/katkıda bulunanlar için bileşen kılavuzlarını içerir.

---

## İçindekiler

- [1. Genel Mimari Bakış](#1-genel-mimari-bak%C4%B1%C5%9F)
- [2. Tüm Agent Araçları (30+ Araç)](#2-t%C3%BCm-agent-ara%C3%A7lar%C4%B1-30-ara%C3%A7)
- [3. Kendi Kendini İyileştiren Doğrulama Döngüsü](#3-kendi-kendini-i%CC%87yile%C5%9Ftiren-do%C4%9Frulama-d%C3%B6ng%C3%BCs%C3%BC)
- [4. RAG ve Kod Arama Motoru](#4-rag-ve-kod-arama-motoru)
- [5. Sub-Agent ve Görev Devretme Protokolü](#5-sub-agent-ve-g%C3%B6rev-devretme-protokol%C3%BC)
- [6. Yerel Yapay Zeka Router Modeli (1.5B)](#6-yerel-yapay-zeka-router-modeli-15b)
- [7. Güvenlik ve Yol İhlali Korumaları](#7-g%C3%BCvenlik-ve-yol-i%CC%87hlali-korumalar%C4%B1)

---

## 1. Genel Mimari Bakış

Yengi, **.NET 10 (LTS)** ve WPF üzerinde geliştirilmiştir. **Olay Güdümlü Agentik Çalıştırma Hattı (Event-Driven Agentic Pipeline)** izler:

1. **İstek Alımı**: Kullanıcı hedefini veya dosya görevini girer.
2. **Planlama Modu**: Karmaşık hedef adım adım mantıksal alt görevlere bölünür.
3. **Araç Çalıştırıcı (Tool Executor)**: Dosya, terminal veya copilot komutları otonom yürütülür.
4. **Doğrulama Motoru**: Derleyici çıktıları, test sonuçları veya çalışma zamanı logları taranır.
5. **Otomatik Düzeltme Döngüsü**: Hata izleri yapay zekaya beslenerek kod otomatik onarılır.

---

## 2. Tüm Agent Araçları (30+ Araç)

| Araç Adı | Risk Seviyesi | Açıklama |
| :--- | :---: | :--- |
| `ReadFile` | Düşük | Proje sınırları içindeki bir dosyanın içeriğini okur. |
| `CreateOrUpdateFile` | Orta | Yeni dosya oluşturur veya mevcut dosyayı yedek alarak günceller. |
| `ReplaceInFile` | Orta | Dosya içinde belirli satır/blok değiştirmesi yapar. |
| `DeleteFile` | Yüksek | Kullanıcı onayıyla bir dosyayı güvenli bir şekilde siler. |
| `ListDirectory` | Düşük | Dizin içindeki dosya ve alt klasörleri listeler. |
| `SearchProject` | Düşük | Kod tabanı içinde hızlı regex veya metin araması yapar. |
| `ExecuteTerminalCommand` | Yüksek | Terminal süreçlerini 30 saniye zaman aşımı korumasıyla çalıştırır. |
| `BuildProject` | Orta | Proje türünü (C#, Node.js, Python, Flutter) tespit edip derler. |
| `RunTests` | Orta | Otomatik test paketlerini (`dotnet test`, `npm test`, `pytest`) çalıştırır. |
| `Generatelmage` | Düşük | AI görselleri üreterek `generated_images/` klasörüne kaydeder. |
| `GitStatus` | Düşük | Mevcut Git durumunu ve değişen dosyaları getirir. |
| `GitCommit` | Orta | Değişiklikleri Git deposuna commit eder. |
| `GitPush` | Yüksek | Commit edilmiş kodları uzak GitHub deposuna yükler. |
| `AskUserQuestion` | Düşük | Belirsiz gereksinimleri açıklığa kavuşturmak için arayüzde soru sorar. |

---

## 3. Kendi Kendini İyileştiren Doğrulama Döngüsü

`BuildProject` veya `RunTests` başarısız olduğunda:
1. Hata logu tam ve kesintisiz olarak yakalanır.
2. **Onarım Agent'ı (Repair Agent)** hatanın tam iziyle başlatılır.
3. Bozulan testleri veya istisnaları (NullReference vb.) düzelten kod değişiklikleri önerilir.
4. `0 hata` alana kadar derleme ve test tekrar yürütülür.

---

## 4. RAG ve Kod Arama Motoru

- **İndeksleme**: AST düğümlerini, sınıf tanımlarını, fonksiyon imzalarını ve Markdown dokümanlarını ayrıştırır.
- **Arama**: Hibrit semantik ve anahtar kelime indekslemesi binlerce dosyalı projelerde anında sembol araması sağlar.

---

## 5. Yerel Yapay Zeka Router Modeli (1.5B)

Yengi, özel olarak eğitilmiş 1.5B parametreli yerel bir modele (`yengi-router:1.5b`) sahiptir.
- **Amaç**: Kullanıcı isteklerini analiz eder ve büyük modellere gitmeden önce %95+ güvenle en doğru araç sırasını seçer.
- **Fayda**: Basit görevleri yerelde çözerek jeton (token) maliyetlerini ve gecikmeyi düşürür.

---

## 📄 Lisans

[AGPL-3.0 Lisansı](LICENSE) altında dağıtılmaktadır.
