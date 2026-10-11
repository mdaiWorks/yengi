using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace mdaiAgent.Services;

/// <summary>
/// Otonom Tarayıcı Ajanı Servisi.
/// WebView2 kullanarak AI'nın gezinme, tıklama, form doldurma,
/// ekran görüntüsü alma ve DOM okuma işlemlerini yapmasını sağlar.
/// Tarayıcı ayrı pencere yerine editör sekmesi olarak açılır.
/// </summary>
public class BrowserAgentService : IDisposable
{
    // ─────────────────────────────────────────────
    // Singleton
    // ─────────────────────────────────────────────
    private static BrowserAgentService? _instance;
    public static BrowserAgentService Instance => _instance ??= new BrowserAgentService();

    // ─────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────
    private BrowserAgentPanel? _panel;
    private TabItem?           _tab;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private Action<string> _terminalLog = msg => { };

    // MainWindow tarafından set edilir — tcEditor'a TabItem eklemek için
    private Func<BrowserAgentPanel, TabItem>? _tabOpener;

    private BrowserAgentService() { }

    public void SetLogger(Action<string> log) => _terminalLog = log;

    /// <summary>
    /// MainWindow bu metodu çağırarak tcEditor'a sekme ekleme yetkisi verir.
    /// </summary>
    public void SetTabOpener(Func<BrowserAgentPanel, TabItem> opener) => _tabOpener = opener;

    // ─────────────────────────────────────────────
    // Init
    // ─────────────────────────────────────────────

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _panel = new BrowserAgentPanel();

                if (_tabOpener != null)
                {
                    // Editör sekmesi olarak aç (tercih edilen yol)
                    _tab = _tabOpener(_panel);
                }
                else
                {
                    // Fallback: ayrı pencere (SetTabOpener çağrılmamışsa)
                    var win = new Window
                    {
                        Title      = "🌐 Yengi — Tarayıcı Ajanı",
                        Content    = _panel,
                        Width      = 1100,
                        Height     = 720,
                        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e))
                    };
                    win.Show();
                }
            });

            // Loaded event'indeki WebView2 init'in bitmesini bekle
            await _panel!.WaitForInitAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ─────────────────────────────────────────────
    // Browser Operations
    // ─────────────────────────────────────────────

    public async Task<ToolResult> NavigateAsync(JsonElement args, CancellationToken ct)
    {
        var url = args.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
            return Fail("'url' parametresi zorunludur.");

        if (!url!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        await EnsureInitializedAsync();
        _terminalLog($"🌐 [Browser] Geziniliyor: {url}");

        // Sekme varsa ön plana getir
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_tab != null)
            {
                var tc = ItemsControl.ItemsControlFromItemContainer(_tab) as TabControl;
                tc?.Dispatcher.Invoke(() => tc.SelectedItem = _tab);
            }
        });

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _ = _panel!.NavigateAsync(url, tcs);
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(20_000, ct)) == tcs.Task;
        if (!completed) return Fail("Sayfa yükleme zaman aşıldı (20s).");

        _terminalLog($"✅ [Browser] Sayfa yüklendi: {url}");
        return Ok("Sayfa yüklendi: " + url);
    }

    public async Task<ToolResult> ClickAsync(JsonElement args, CancellationToken ct)
    {
        var selector = args.TryGetProperty("selector", out var s) ? s.GetString() : null;
        var text     = args.TryGetProperty("text",     out var t) ? t.GetString() : null;

        if (string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(text))
            return Fail("'selector' veya 'text' parametrelerinden biri zorunludur.");

        await EnsureInitializedAsync();
        _terminalLog($"🖱️ [Browser] Tıklanıyor: {selector ?? text}");

        string script;
        if (!string.IsNullOrWhiteSpace(selector))
        {
            var safe = selector!.Replace("'", "\\'");
            script = "(function(){" +
                     "var el=document.querySelector('" + safe + "');" +
                     "if(!el)return 'NOT_FOUND';" +
                     "el.scrollIntoView({behavior:'instant',block:'center'});" +
                     "el.click();" +
                     "return 'CLICKED';})()";
        }
        else
        {
            var lower = text!.ToLower().Replace("'", "\\'");
            script = "(function(){" +
                     "var all=document.querySelectorAll('a,button,input,label,[role=\"button\"],[role=\"link\"]');" +
                     "for(var i=0;i<all.length;i++){" +
                     "  var tx=(all[i].textContent||'').trim().toLowerCase();" +
                     "  var vl=(all[i].value||'').toLowerCase();" +
                     "  var ph=(all[i].placeholder||'').toLowerCase();" +
                     "  if(tx.includes('" + lower + "')||vl.includes('" + lower + "')||ph.includes('" + lower + "')){" +
                     "    all[i].scrollIntoView({behavior:'instant',block:'center'});" +
                     "    all[i].click();" +
                     "    return 'CLICKED:'+all[i].tagName;" +
                     "  }" +
                     "}" +
                     "return 'NOT_FOUND';})()";
        }

        var result = await ExecuteScriptAsync(script, ct);
        if (result.Contains("NOT_FOUND"))
            return Fail($"Element bulunamadı: {selector ?? text}");

        await Task.Delay(500, ct);
        return Ok("Tıklandı: " + result);
    }

    public async Task<ToolResult> FillAsync(JsonElement args, CancellationToken ct)
    {
        var selector   = args.TryGetProperty("selector",   out var s)  ? s.GetString()   : null;
        var value      = args.TryGetProperty("value",      out var v)  ? v.GetString()   : "";
        var pressEnter = args.TryGetProperty("pressEnter", out var pe) && pe.GetBoolean();

        if (string.IsNullOrWhiteSpace(selector))
            return Fail("'selector' parametresi zorunludur.");

        await EnsureInitializedAsync();
        _terminalLog($"⌨️ [Browser] Form dolduruluyor: {selector}");

        var safeSelector = selector!.Replace("'", "\\'");
        var safeValue    = (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        var enterJs      = pressEnter
            ? "el.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',keyCode:13,bubbles:true}));"
            : "";

        var script = "(function(){" +
                     "var el=document.querySelector('" + safeSelector + "');" +
                     "if(!el)return 'NOT_FOUND';" +
                     "el.focus();" +
                     "el.value='" + safeValue + "';" +
                     "el.dispatchEvent(new Event('input',{bubbles:true}));" +
                     "el.dispatchEvent(new Event('change',{bubbles:true}));" +
                     enterJs +
                     "return 'FILLED';})()";

        var result = await ExecuteScriptAsync(script, ct);
        if (result.Contains("NOT_FOUND"))
            return Fail("Input elementi bulunamadı: " + selector);

        return Ok("Form alanı dolduruldu: " + selector);
    }

    public async Task<ToolResult> ScrollAsync(JsonElement args, CancellationToken ct)
    {
        var direction = args.TryGetProperty("direction", out var d) ? d.GetString() ?? "down" : "down";
        var amount    = args.TryGetProperty("amount",    out var a) ? a.GetInt32() : 500;

        await EnsureInitializedAsync();
        _terminalLog($"📜 [Browser] Kaydırılıyor: {direction} {amount}px");

        var pixels = direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? -amount : amount;
        await ExecuteScriptAsync($"window.scrollBy(0,{pixels});'SCROLLED'", ct);
        await Task.Delay(300, ct);
        return Ok($"Sayfa {direction} yönünde {amount}px kaydırıldı.");
    }

    public async Task<ToolResult> GetDomAsync(JsonElement args, CancellationToken ct)
    {
        var mode = args.TryGetProperty("mode", out var m) ? m.GetString() ?? "summary" : "summary";
        await EnsureInitializedAsync();
        _terminalLog("🔎 [Browser] DOM okunuyor...");

        string script;
        if (mode == "full")
        {
            script = "document.documentElement.outerHTML.substring(0,15000)";
        }
        else
        {
            script = "(function(){" +
                     "var ti=document.title;" +
                     "var ur=location.href;" +
                     "var tx=(document.body?document.body.innerText:'').replace(/\\s+/g,' ').trim().substring(0,3000);" +
                     "var li=[];" +
                     "document.querySelectorAll('a[href]').forEach(function(a){" +
                     "  var h=a.href,t=(a.textContent||'').trim();" +
                     "  if(h&&t&&li.length<30)li.push(t+' -> '+h);" +
                     "});" +
                     "var inp=[];" +
                     "document.querySelectorAll('input,select,textarea,button').forEach(function(el){" +
                     "  inp.push((el.type||el.tagName)+'['+el.name+']: '+(el.placeholder||el.value||''));" +
                     "});" +
                     "return JSON.stringify({title:ti,url:ur,text:tx,links:li.slice(0,20),inputs:inp.slice(0,15)});})()";
        }

        var raw = await ExecuteScriptAsync(script, ct);
        if (raw.StartsWith("\"") && raw.EndsWith("\""))
            raw = JsonSerializer.Deserialize<string>(raw) ?? raw;

        return Ok(raw);
    }

    public async Task<ToolResult> UploadFileAsync(JsonElement args, CancellationToken ct)
    {
        var selector = args.TryGetProperty("selector", out var s) ? s.GetString() : null;
        var filePath = args.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;

        if (string.IsNullOrWhiteSpace(selector) || string.IsNullOrWhiteSpace(filePath))
            return Fail("'selector' ve 'filePath' parametreleri zorunludur.");

        if (!File.Exists(filePath))
            return Fail($"Yüklenecek dosya bulunamadı: {filePath}");

        await EnsureInitializedAsync();
        _terminalLog($"📤 [Browser] Dosya yükleniyor: {Path.GetFileName(filePath)} -> {selector}");

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var base64 = Convert.ToBase64String(bytes);
            var fileName = Path.GetFileName(filePath).Replace("'", "\\'");
            var safeSelector = selector!.Replace("'", "\\'");

            // MIME type tahmini
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".mp4" => "video/mp4",
                ".zip" => "application/zip",
                ".aab" or ".apk" => "application/octet-stream",
                ".json" => "application/json",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            // JS ile DataTransfer ve File oluşturup input elementine aktar ve change/input eventlerini tetikle
            var script = "(function(){" +
                         "var el = document.querySelector('" + safeSelector + "');" +
                         "if (!el) return 'NOT_FOUND';" +
                         "try {" +
                         "  var b64 = '" + base64 + "';" +
                         "  var bin = atob(b64);" +
                         "  var len = bin.length;" +
                         "  var u8 = new Uint8Array(len);" +
                         "  for (var i=0; i<len; i++) u8[i] = bin.charCodeAt(i);" +
                         "  var file = new File([u8], '" + fileName + "', { type: '" + mime + "' });" +
                         "  var dt = new DataTransfer();" +
                         "  dt.items.add(file);" +
                         "  el.files = dt.files;" +
                         "  el.dispatchEvent(new Event('input', { bubbles: true }));" +
                         "  el.dispatchEvent(new Event('change', { bubbles: true }));" +
                         "  return 'UPLOADED:' + file.name + ' (' + file.size + ' bytes)';" +
                         "} catch(err) {" +
                         "  return 'ERROR:' + err.message;" +
                         "}" +
                         "})()";

            var res = await ExecuteScriptAsync(script, ct);
            if (res.Contains("NOT_FOUND"))
                return Fail($"Dosya yükleme inputu bulunamadı: {selector}");
            if (res.Contains("ERROR:"))
                return Fail($"Dosya yükleme JS hatası: {res}");

            _terminalLog($"✅ [Browser] Dosya yüklendi: {Path.GetFileName(filePath)}");
            return Ok($"Dosya başarıyla yüklendi: {res}");
        }
        catch (Exception ex)
        {
            return Fail($"Dosya yükleme hatası: {ex.Message}");
        }
    }

    public async Task<ToolResult> CaptureScreenshotAsync(JsonElement args, CancellationToken ct)
    {
        await EnsureInitializedAsync();
        _terminalLog("📸 [Browser] Ekran görüntüsü alınıyor...");

        var saveAsFile = args.TryGetProperty("saveAsFile", out var sf) && sf.GetBoolean();

        try
        {
            byte[] pngBytes = await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                using var ms = new System.IO.MemoryStream();
                await _panel!.WebView.CoreWebView2.CapturePreviewAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms);
                return ms.ToArray();
            }).Task.Unwrap();

            if (saveAsFile)
            {
                var path = Path.Combine(Path.GetTempPath(),
                    $"yengi_browser_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                await File.WriteAllBytesAsync(path, pngBytes, ct);
                _terminalLog($"📸 [Browser] Kaydedildi: {path}");
                return Ok("[SCREENSHOT_PATH:" + path + "]");
            }

            var b64 = Convert.ToBase64String(pngBytes);
            _terminalLog("📸 [Browser] Ekran görüntüsü hazır (base64).");
            return new ToolResult { Success = true, Output = "[SCREENSHOT_BASE64:" + b64 + "]" };
        }
        catch (Exception ex)
        {
            return Fail("Ekran görüntüsü alınamadı: " + ex.Message);
        }
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private async Task<string> ExecuteScriptAsync(string script, CancellationToken ct)
    {
        return await Application.Current.Dispatcher.InvokeAsync(async () =>
            await _panel!.WebView.ExecuteScriptAsync(script)).Task.Unwrap();
    }

    private static ToolResult Ok(string output)  => new() { Success = true,  Output = output };
    private static ToolResult Fail(string error)  => new() { Success = false, Error  = error  };

    public void Dispose()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_tab != null)
            {
                var tc = ItemsControl.ItemsControlFromItemContainer(_tab) as TabControl;
                tc?.Items.Remove(_tab);
            }
        });
        _panel = null;
        _tab   = null;
        _initialized = false;
    }
}
