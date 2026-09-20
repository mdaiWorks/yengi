using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace mdaiAgent;

public class ToastNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Message { get; set; } = string.Empty;
    public string Icon { get; set; } = "ℹ️";
    public Brush Background { get; set; } = new SolidColorBrush(Color.FromRgb(45, 45, 48));
}

public class ToastService
{
    private readonly Dictionary<Guid, DispatcherTimer> _toastTimers = new();
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<ToastNotification> Notifications { get; } = new();

    public ToastService(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void AddToast(string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var toast = new ToastNotification
        {
            Message = message,
            Icon = severity switch
            {
                NotificationSeverity.Error => "❌",
                NotificationSeverity.Warning => "⚠️",
                NotificationSeverity.Success => "✅",
                _ => "ℹ️"
            },
            Background = new SolidColorBrush(severity switch
            {
                NotificationSeverity.Error => Color.FromRgb(90, 25, 25),
                NotificationSeverity.Warning => Color.FromRgb(90, 70, 25),
                NotificationSeverity.Success => Color.FromRgb(25, 90, 45),
                _ => Color.FromRgb(45, 45, 48)
            })
        };

        _dispatcher.Invoke(() =>
        {
            Notifications.Insert(0, toast);
            var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RemoveToastInternal(toast.Id);
            };

            _toastTimers[toast.Id] = timer;
            timer.Start();
        });
    }

    public void RemoveToast(Guid id)
    {
        if (_dispatcher.CheckAccess())
        {
            RemoveToastInternal(id);
            return;
        }

        _dispatcher.Invoke(() => RemoveToastInternal(id));
    }

    private void RemoveToastInternal(Guid id)
    {
        if (_toastTimers.TryGetValue(id, out var timer))
        {
            timer.Stop();
            _toastTimers.Remove(id);
        }

        var toast = Notifications.FirstOrDefault(t => t.Id == id);
        if (toast != null)
        {
            Notifications.Remove(toast);
        }
    }
}
