using System.Windows;

namespace mdaiAgent;

public partial class LanguageSelectionWindow : Window
{
    public string SelectedLanguage { get; private set; } = "tr";

    public LanguageSelectionWindow()
    {
        InitializeComponent();
        rbTurkish.IsChecked = LocalizationManager.Instance.IsTurkish;
        rbEnglish.IsChecked = LocalizationManager.Instance.IsEnglish;
        rbChinese.IsChecked = LocalizationManager.Instance.IsChinese;
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        SelectedLanguage = rbEnglish.IsChecked == true
            ? "en"
            : rbChinese.IsChecked == true ? "zh" : "tr";
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedLanguage = LocalizationManager.Instance.CurrentLanguageCode;
        DialogResult = false;
        Close();
    }
}
