using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Agent workflow, Plan Mode, delegation ve verification koordinasyonu için controller.
/// Kural: Yeni agent özellikleri buraya yazılsın, MainWindow sadece çağrı yapsın.
/// </summary>
public class AgentController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public AgentController(MainWindow window)
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
