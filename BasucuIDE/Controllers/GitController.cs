using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Git entegrasyonu, stage/commit/push, diff, dosya ağacı durum göstergesi için controller.
/// Kural: Yeni Git özellikleri buraya yazılsın, MainWindow sadece çağrı yapsın.
/// </summary>
public class GitController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public GitController(MainWindow window)
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
