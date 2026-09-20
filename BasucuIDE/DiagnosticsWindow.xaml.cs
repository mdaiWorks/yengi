using System.Windows;
using System.Windows.Controls;

namespace mdaiAgent;

public partial class DiagnosticsWindow : Window
{
    private readonly TabItem _diagnosticsTab;

    public DiagnosticsWindow(TabItem diagnosticsTab)
    {
        InitializeComponent();
        _diagnosticsTab = diagnosticsTab;
        var content = diagnosticsTab.Content as UIElement ?? new TextBlock { Text = "Tanılama içeriği yüklenemedi." };
        diagnosticsTab.Content = null;
        hostGrid.Children.Add(content);
    }

    public UIElement? DetachContent()
    {
        if (hostGrid.Children.Count == 0)
            return null;

        var content = hostGrid.Children[0];
        hostGrid.Children.RemoveAt(0);
        return content;
    }

    protected override void OnClosed(EventArgs e)
    {
        var content = DetachContent();
        _diagnosticsTab.Content = content;
        base.OnClosed(e);
    }
}
