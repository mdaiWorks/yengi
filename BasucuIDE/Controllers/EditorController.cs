using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Kod editörü, dosya aç/kaydet, LSP navigasyonu, inline edit, diff için controller.
/// Kural: Yeni editör özellikleri buraya yazılsın, MainWindow sadece çağrı yapsın.
/// </summary>
public class EditorController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public EditorController(MainWindow window)
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
