using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Terminal paneli, komut çalıştırma, build/test, process yönetimi için controller.
/// Kural: Yeni terminal özellikleri buraya yazılsın, MainWindow.Terminal sadece çağrı yapsın.
/// </summary>
public class TerminalController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public TerminalController(MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
    }

    public void Shutdown()
    {
        _isInitialized = false;
    }
}
