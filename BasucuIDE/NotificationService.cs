using System;

namespace mdaiAgent;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public class NotificationEventArgs : EventArgs
{
    public string Message { get; }
    public NotificationSeverity Severity { get; }

    public NotificationEventArgs(string message, NotificationSeverity severity)
    {
        Message = message;
        Severity = severity;
    }
}

public class NotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationRaised;

    public void Notify(string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        NotificationRaised?.Invoke(this, new NotificationEventArgs(message, severity));
    }
}
