using System.Windows;
using System.Windows.Input;

namespace mdaiAgent;

/// <summary>
/// Interaction logic for ResearchModeInfoWindow.xaml
/// </summary>
public partial class ResearchModeInfoWindow : Window
{
    public bool DontShowAgain => chkDontShow.IsChecked == true;

    public ResearchModeInfoWindow()
    {
        InitializeComponent();
        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
    }

    private void BtnGotIt_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
