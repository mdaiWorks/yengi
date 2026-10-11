using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace mdaiAgent.Services;

/// <summary>
/// Google Play Console ve Apple App Store için otonom mağaza yayınlama servisi.
/// Asset'leri (ikon, ekran görüntüsü, feature graphic) mağaza standartlarına göre
/// otomatik boyutlandırır ve yayınlama iş akışını yönetir.
/// </summary>
public class StoreDeploymentService
{
    private static StoreDeploymentService? _instance;
    public static StoreDeploymentService Instance => _instance ??= new StoreDeploymentService();

    private Action<string> _terminalLog = msg => { };

    private StoreDeploymentService() { }

    public void SetLogger(Action<string> log) => _terminalLog = log;

    /// <summary>
    /// Verilen görseli Google Play veya App Store standartlarına göre ölçeklendirip kaydeder.
    /// </summary>
    public async Task<ToolResult> PrepareStoreAssetsAsync(JsonElement args, string? projectFolder, CancellationToken ct)
    {
        var inputPath = args.TryGetProperty("inputPath", out var ip) ? ip.GetString() : null;
        var store     = args.TryGetProperty("store",     out var st) ? st.GetString()?.ToLowerInvariant() : "play_store";
        var assetType = args.TryGetProperty("assetType", out var at) ? at.GetString()?.ToLowerInvariant() : "icon";
        var outputDir = args.TryGetProperty("outputDir", out var od) ? od.GetString() : null;

        if (string.IsNullOrWhiteSpace(inputPath))
            return Fail("'inputPath' parametresi zorunludur.");

        // Göreli yol verilmişse aktif proje klasörüyle birleştir
        if (!Path.IsPathRooted(inputPath) && !string.IsNullOrWhiteSpace(projectFolder))
        {
            var combined = Path.Combine(projectFolder, inputPath);
            if (File.Exists(combined)) inputPath = combined;
        }

        if (!File.Exists(inputPath))
            return Fail($"Kaynak görsel dosyası bulunamadı: {inputPath}");

        // Hedef klasör: Eğer belirtilmemişse önce aktif proje klasörünü, yoksa kaynak dosyanın yanını seç
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            var baseDir = (!string.IsNullOrWhiteSpace(projectFolder) && Directory.Exists(projectFolder))
                ? projectFolder
                : (Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".");

            outputDir = Path.Combine(baseDir, "store_assets", store ?? "play_store");
        }

        Directory.CreateDirectory(outputDir);

        _terminalLog($"🎨 [Store Deployment] Asset hazırlanıyor: {Path.GetFileName(inputPath)} ({store} -> {assetType})");

        try
        {
            var results = new System.Collections.Generic.List<string>();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var uri = new Uri(Path.GetFullPath(inputPath));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                if (store == "play_store")
                {
                    switch (assetType)
                    {
                        case "icon":
                            // Google Play Icon: 512x512 PNG, max 1024KB, 32-bit
                            var iconPath = Path.Combine(outputDir, "play_store_icon_512x512.png");
                            SaveResizedBitmap(bitmap, 512, 512, iconPath);
                            results.Add($"Google Play İkonu (512x512): {iconPath}");
                            break;

                        case "feature_graphic":
                            // Google Play Feature Graphic: 1024x500 PNG/JPEG
                            var featPath = Path.Combine(outputDir, "play_store_feature_1024x500.png");
                            SaveResizedBitmap(bitmap, 1024, 500, featPath);
                            results.Add($"Google Play Feature Graphic (1024x500): {featPath}");
                            break;

                        case "screenshot":
                            // Google Play Phone Screenshot: 1080x1920 (Portrait) veya 1920x1080 (Landscape)
                            bool isPortrait = bitmap.PixelHeight >= bitmap.PixelWidth;
                            int targetW = isPortrait ? 1080 : 1920;
                            int targetH = isPortrait ? 1920 : 1080;
                            var sName = $"play_store_screenshot_{targetW}x{targetH}_{DateTime.Now:HHmmss}.png";
                            var scPath = Path.Combine(outputDir, sName);
                            SaveResizedBitmap(bitmap, targetW, targetH, scPath);
                            results.Add($"Google Play Ekran Görüntüsü ({targetW}x{targetH}): {scPath}");
                            break;

                        case "all":
                        default:
                            var iPath = Path.Combine(outputDir, "play_store_icon_512x512.png");
                            SaveResizedBitmap(bitmap, 512, 512, iPath);
                            results.Add($"İkon (512x512): {iPath}");

                            var fPath = Path.Combine(outputDir, "play_store_feature_1024x500.png");
                            SaveResizedBitmap(bitmap, 1024, 500, fPath);
                            results.Add($"Feature Graphic (1024x500): {fPath}");
                            break;
                    }
                }
                else // app_store
                {
                    switch (assetType)
                    {
                        case "icon":
                            // App Store Icon: 1024x1024 PNG (No transparency)
                            var appIconPath = Path.Combine(outputDir, "app_store_icon_1024x1024.png");
                            SaveResizedBitmap(bitmap, 1024, 1024, appIconPath);
                            results.Add($"App Store İkonu (1024x1024): {appIconPath}");
                            break;

                        case "screenshot":
                        case "all":
                        default:
                            // 6.7" iPhone Display (1290x2796)
                            var iphPath = Path.Combine(outputDir, "app_store_iphone_6.7_1290x2796.png");
                            SaveResizedBitmap(bitmap, 1290, 2796, iphPath);
                            results.Add($"iPhone 6.7\" Ekran Görüntüsü (1290x2796): {iphPath}");

                            // 12.9" iPad Display (2048x2732)
                            var ipadPath = Path.Combine(outputDir, "app_store_ipad_12.9_2048x2732.png");
                            SaveResizedBitmap(bitmap, 2048, 2732, ipadPath);
                            results.Add($"iPad 12.9\" Ekran Görüntüsü (2048x2732): {ipadPath}");
                            break;
                    }
                }
            });

            _terminalLog($"✅ [Store Deployment] Assetler hazırlandı ({results.Count} dosya).");
            return Ok("Mağaza görselleri başarıyla oluşturuldu:\n" + string.Join("\n", results));
        }
        catch (Exception ex)
        {
            return Fail($"Görsel ölçeklendirme hatası: {ex.Message}");
        }
    }

    private static void SaveResizedBitmap(BitmapSource source, int targetWidth, int targetHeight, string targetPath)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);

        using (var dc = group.Open())
        {
            dc.DrawImage(source, new Rect(0, 0, targetWidth, targetHeight));
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawDrawing(group);
        }

        var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    private static ToolResult Ok(string output) => new() { Success = true, Output = output };
    private static ToolResult Fail(string error) => new() { Success = false, Error = error };
}
