using System;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace mdaiAgent;

public partial class PreviewWindow : Window
{
    private PreviewService? _previewService;
    private string? _currentUrl;
    
    public PreviewWindow()
    {
        InitializeComponent();
        Loaded += PreviewWindow_Loaded;
        Closing += PreviewWindow_Closing;
    }
    
    public void SetPreviewService(PreviewService service)
    {
        _previewService = service;
        if (_previewService != null)
        {
            _previewService.PreviewUrlChanged += OnPreviewUrlChanged;
            _previewService.PreviewStatusChanged += OnPreviewStatusChanged;
            _previewService.PreviewError += OnPreviewError;
        }
    }

    private void PreviewWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (webView2 != null && webView2.CoreWebView2 != null)
            {
                webView2.CoreWebView2.Stop();
            }
        }
        catch { }
    }
    
#pragma warning disable VSTHRD100 // Avoid async void methods - event handler
    private async void PreviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // E_ACCESSDENIED hatasını önlemek için özel bir userDataFolder belirle.
            // Varsayılan klasör bazen yönetici izin çakışmalarına neden olur.
            var userDataFolder = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Yengi", "WebView2Cache"
            );
            System.IO.Directory.CreateDirectory(userDataFolder);
            
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder
            );
            await webView2.EnsureCoreWebView2Async(env);
            
            if (webView2.CoreWebView2 != null)
            {
                var settings = webView2.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = true;
                settings.AreDefaultContextMenusEnabled = true;
                settings.IsStatusBarEnabled = false;
                settings.IsScriptEnabled = true;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.IsWebMessageEnabled = true;
                
                // Güncel mobil user-agent (iPhone)
                settings.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1";
                
                // Handle navigation events to make sure links/buttons work
                webView2.CoreWebView2.NavigationStarting += (s, args) =>
                {
                    // Allow all navigation
                };
                webView2.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                webView2.CoreWebView2.NewWindowRequested += (s, args) =>
                {
                    // Open new windows in the same WebView2
                    args.Handled = true;
                    webView2.CoreWebView2.Navigate(args.Uri);
                };

                // Eğer zaten bir URL varsa hemen yükle!
                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    tbUrl.Text = _currentUrl;
                    webView2.CoreWebView2.Navigate(_currentUrl);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Web önizleme başlatılamadı: {ex.Message}\n\n" +
                "Olası çözüm: Uygulamayı Yönetici olarak çalıştırmayın.",
                "WebView2 Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
#pragma warning restore VSTHRD100

#pragma warning disable VSTHRD100 // Avoid async void methods - event handler
    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            // Basit CSS ile mobil kaydırma ve dokunma hissini iyileştirelim
            await webView2.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    const style = document.createElement('style');
    style.textContent = `
        /* Mobil tarayıcıda kaydırmayı ve dokunmayı iyileştir */
        html, body {
            overscroll-behavior: none;
            -webkit-overflow-scrolling: touch;
        }
        * {
            touch-action: pan-y;
            -webkit-tap-highlight-color: rgba(0,0,0,0);
        }
    `;
    document.head.appendChild(style);
})();
");
        }
        catch { }
    }
#pragma warning restore VSTHRD100
    
    private void OnPreviewUrlChanged(object? sender, string url)
    {
        _currentUrl = url;
        Dispatcher.Invoke(() =>
        {
            tbUrl.Text = url;
            // WebView2'nin CoreWebView2'si hazır olunca yönlendir!
            if (webView2.CoreWebView2 != null)
            {
                webView2.CoreWebView2.Navigate(url);
            }
        });
    }
    
    private void OnPreviewStatusChanged(object? sender, string status)
    {
        // Can add status updates here
    }
    
    private void OnPreviewError(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show($"Preview Error: {error}", LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }
    
    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentUrl) && webView2.CoreWebView2 != null)
        {
            webView2.CoreWebView2.Reload();
        }
    }
    
    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _previewService?.StopPreview();
        _currentUrl = null;
        tbUrl.Text = "";
        // Optional: Clear WebView2 content
        if (webView2.CoreWebView2 != null)
        {
            webView2.CoreWebView2.NavigateToString("<html></html>");
        }
    }
}
