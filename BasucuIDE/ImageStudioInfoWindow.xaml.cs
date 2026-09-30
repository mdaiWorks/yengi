using System.Windows;
using System.Windows.Input;

namespace mdaiAgent;

/// <summary>
/// Interaction logic for ImageStudioInfoWindow.xaml
/// </summary>
public partial class ImageStudioInfoWindow : Window
{
    public bool DontShowAgain => chkDontShow.IsChecked == true;

    public ImageStudioInfoWindow()
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
