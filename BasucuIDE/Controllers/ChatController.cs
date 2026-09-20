using System;
using System.Windows;

namespace mdaiAgent.Controllers;

/// <summary>
/// Chat panel olayları ve akışı için controller.
/// Kural: Yeni chat özellikleri buraya yazılsın, MainWindow.ChatPanel sadece çağrı yapsın.
/// </summary>
public class ChatController
{
    private readonly MainWindow _window;
    private bool _isInitialized;

    public ChatController(MainWindow window)
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
