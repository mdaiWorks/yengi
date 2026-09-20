using System;
using System.Linq;
using System.Windows.Threading;
using Xunit;

namespace mdaiAgent.Tests;

public class ToastServiceTests
{
    [Fact]
    public void AddToast_InsertsNotificationIntoCollection()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var service = new ToastService(dispatcher);

        service.AddToast("Test mesajı", NotificationSeverity.Info);

        Assert.Single(service.Notifications);
        Assert.Equal("Test mesajı", service.Notifications.First().Message);
        Assert.Equal("ℹ️", service.Notifications.First().Icon);
    }

    [Fact]
    public void RemoveToast_RemovesNotificationFromCollection()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var service = new ToastService(dispatcher);
        service.AddToast("Silinecek mesaj", NotificationSeverity.Warning);

        var toast = service.Notifications.First();
        service.RemoveToast(toast.Id);

        Assert.Empty(service.Notifications);
    }
}
