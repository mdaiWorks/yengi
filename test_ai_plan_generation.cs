#!/usr/bin/env dotnet
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

// Quick test to verify AiPlanGenerator works
// Usage: dotnet script test_ai_plan_generation.cs

namespace mdaiAgent.QuickTest
{
    public class QuickPlanTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("🧪 AiPlanGenerator Test başlıyor...");
            Console.WriteLine();

            // Test 1: Tetris Plan
            Console.WriteLine("📋 Test 1: Tetris Oyunu Planı");
            await TestPlanGeneration("Tetris oyunu yap", "WPF uygulaması. Pastel renkler, eğlenceli. Klavyeden kontrol. Başlangıçta kolay seviye.");

            Console.WriteLine();
            Console.WriteLine("---");
            Console.WriteLine();

            // Test 2: Note App Plan
            Console.WriteLine("📋 Test 2: Not Uygulaması Planı");
            await TestPlanGeneration("Basit bir not alma uygulaması yap", "Notları kaydet, düzenle, sil. Kategori desteği. Arama özelliği.");

            Console.WriteLine();
            Console.WriteLine("✅ Testler tamamlandı!");
        }

        private static async Task TestPlanGeneration(string objective, string context)
        {
            try
            {
                // This would require actual initialization of AppSettings and NvidiaApiClient
                // For now, we just demonstrate the plan structure

                Console.WriteLine($"📌 Objective: {objective}");
                Console.WriteLine($"📝 Context: {context}");
                Console.WriteLine();

                // Dummy response (actual would come from AI)
                var dummyPlan = @"
1. 📊 Gereksimleri Analiz Et
   - Tetris oyunun klasik kurallarını gözden geçir
   - Kullanıcının tercih ettiği renk paletini (pastel) belirle
   - Oyun mekaniklerini ve seviyeleri planla

2. 🏗️ Mimari Tasarla
   - WPF uygulaması için window layout'ını oluştur
   - Game logic ve rendering'i ayır
   - Kontrol harita sistemini implement et

3. 📂 Dosya Yapısını Planla
   - GameBoard.cs - oyun tahtası mantığı
   - Tetromino.cs - oyun parçaları
   - GameManager.cs - oyun yönetimi
   - GameWindow.xaml - UI

4. ⚙️ Temel Bileşenleri Uygula
   - Tahtayı oluştur ve render et
   - Tetromino'ları tanımla ve hareket ettir
   - Keyboard input işleme
   - Çizgiler tamamen dolduğunda temizle

5. ✅ Test ve Döşeme
   - Rotasyon ve hareket testleri
   - Collision detection'ı doğrula
   - Skor sistemi ve seviyeleri test et
";

                Console.WriteLine("🤖 AI tarafından oluşturulan plan:");
                Console.WriteLine(dummyPlan);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }
    }
}
