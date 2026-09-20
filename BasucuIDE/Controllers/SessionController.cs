using System;

namespace mdaiAgent.Controllers;

/// <summary>
/// Chat oturum yönetimi, yeni sohbet, isimlendirme, silme, arşivleme için controller.
/// Kural: Yeni session özellikleri buraya yazılsın, MainWindow.SessionRecovery sadece çağrı yapsın.
/// </summary>
public class SessionController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public SessionController(MainWindow window)
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
