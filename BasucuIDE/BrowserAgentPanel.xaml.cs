using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace mdaiAgent;

/// <summary>
/// Editör sekmesi olarak gömülen Tarayıcı Ajanı paneli.
/// BrowserAgentService tarafından tcEditor TabControl'e TabItem olarak eklenir.
/// </summary>
public partial class BrowserAgentPanel : UserControl
{
    public WebView2 WebView => webView;

    private TaskCompletionSource<bool>? _navTcs;

    // Loaded event'i WebView2 init bittiğinde çözümlenir.
#pragma warning disable VSTHRD003
    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitForInitAsync() => _initTcs.Task;
#pragma warning restore VSTHRD003

    public BrowserAgentPanel()
    {
        InitializeComponent();
        // WebView2 init'i LOADED eventinde yap — pencere/sekme görünür olunca
        Loaded += BrowserAgentPanel_Loaded;
    }

    // ─────────────────────────────────────────────
    // Init (Loaded event'inde çalışır — UserControl görünür olunca)
    // ─────────────────────────────────────────────

#pragma warning disable VSTHRD100
    private async void BrowserAgentPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Tekrar tetiklenmeyi engelle (tab switch'lerinde Loaded yeniden gelmeyebilir ama önlem)
        Loaded -= BrowserAgentPanel_Loaded;

        if (_initTcs.Task.IsCompleted) return;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yengi", "BrowserAgentCache");
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.NewWindowRequested  += (s, ev) =>
            {
                ev.Handled = true;
                webView.CoreWebView2.Navigate(ev.Uri);
            };

            // Desktop User-Agent — bazı siteler headless tarayıcıları engeller
            webView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsScriptEnabled    = true;

            SetStatus("Hazır ✓", "#4caf50");
            _initTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            SetStatus("Init Hatası ✗", "#f44336");
            _initTcs.TrySetException(ex);
        }
    }
#pragma warning restore VSTHRD100

    // ─────────────────────────────────────────────
    // Navigation helpers
    // ─────────────────────────────────────────────

    public Task NavigateAsync(string url, TaskCompletionSource<bool> tcs)
    {
        _navTcs = tcs;
        SetStatus("Geziniliyor...", "#ff9800");
        tbCurrentUrl.Text = url;
        webView.CoreWebView2.Navigate(url);
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            tbCurrentUrl.Text = e.Uri;
            SetStatus("Yükleniyor...", "#ff9800");
        });
    }

#pragma warning disable VSTHRD100
    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            await Task.Delay(800);

            Dispatcher.Invoke(() =>
            {
                SetStatus(e.IsSuccess ? "Yüklendi ✓" : "Hata ✗", e.IsSuccess ? "#4caf50" : "#f44336");
                tbActionLog.Text = $"Son işlem: {webView.CoreWebView2.DocumentTitle}";
            });

            _navTcs?.TrySetResult(e.IsSuccess);
        }
        catch (Exception ex)
        {
            _navTcs?.TrySetException(ex);
        }
    }
#pragma warning restore VSTHRD100

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void SetStatus(string text, string colorHex)
    {
        tbStatus.Text = text;
        ellStatus.Fill = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
    }

    public void UpdateActionLog(string message)
    {
        Dispatcher.Invoke(() => tbActionLog.Text = message);
    }
}
