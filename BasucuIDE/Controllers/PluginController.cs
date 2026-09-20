using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Plugin yöneticisi, LSP istemcisi, dil pluginleri, harici DLL yükleme için controller.
/// Kural: Yeni plugin/LSP özellikleri buraya yazılsın, MainWindow sadece çağrı yapsın.
/// </summary>
public class PluginController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public PluginController(MainWindow window)
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
