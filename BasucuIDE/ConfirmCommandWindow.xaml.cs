using System.Windows;

namespace mdaiAgent;

public partial class ConfirmCommandWindow : Window
{
    public ToolExecutor.ConfirmResult Result { get; private set; } = ToolExecutor.ConfirmResult.Cancel;

    public ConfirmCommandWindow(string commandText)
    {
        InitializeComponent();
        ApplyLanguage();
        tbCommand.Text = commandText;
    }

    private void ApplyLanguage()
    {
        bool isEnglish = Localization.IsEnglish();

        Title = isEnglish ? LocalizationManager.Instance.GetString("KomutOnayi") : LocalizationManager.Instance.GetString("KomutOnayi");
        tbConfirmInstruction.Text = isEnglish ? "AI wants to run the following command:" : "AI aşağıdaki komutu çalıştırmak istiyor:";
        btnDryRun.Content = isEnglish ? LocalizationManager.Instance.GetString("KuruCalistir") : LocalizationManager.Instance.GetString("KuruCalistir");
        btnSkip.Content = isEnglish ? "Skip" : "Atla";
        btnAllow.Content = isEnglish ? LocalizationManager.Instance.GetString("IzinVer") : LocalizationManager.Instance.GetString("IzinVer");
        btnCancel.Content = isEnglish ? "Cancel" : "İptal";
    }

    private void BtnAllow_Click(object sender, RoutedEventArgs e)
    {
        Result = ToolExecutor.ConfirmResult.Allow;
        DialogResult = true;
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        Result = ToolExecutor.ConfirmResult.Skip;
        DialogResult = false;
        Close();
    }

    private void BtnDryRun_Click(object sender, RoutedEventArgs e)
    {
        Result = ToolExecutor.ConfirmResult.DryRun;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = ToolExecutor.ConfirmResult.Cancel;
        DialogResult = false;
        Close();
    }
}
