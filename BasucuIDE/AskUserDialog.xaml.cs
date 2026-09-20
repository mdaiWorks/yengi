using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;

namespace mdaiAgent;

public partial class AskUserDialog : Window
{
    public string SelectedAnswer { get; private set; } = string.Empty;

    private readonly List<RadioButton> _radioButtons = new();
    private readonly List<CheckBox> _checkBoxes = new();
    private readonly bool _allowMultiple;

    public AskUserDialog(string question, List<string> options, bool allowMultiple = false)
    {
        InitializeComponent();
        _allowMultiple = allowMultiple;
        ApplyLanguage();

        tbQuestion.Text = question;

        // Seçenekleri dinamik olarak oluştur
        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i];
            if (allowMultiple)
            {
                var checkBox = new CheckBox
                {
                    Content = new TextBlock { Text = $"{i + 1}. {option}", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = System.Windows.Media.Brushes.LightGray },
                    Style = (Style)Resources["MultiSelectOptionStyle"],
                    Tag = option
                };
                checkBox.Checked += (_, _) => txtCustomAnswer.Clear();

                _checkBoxes.Add(checkBox);
                panelOptions.Children.Add(checkBox);
            }
            else
            {
                var radio = new RadioButton
                {
                    Content = new TextBlock { Text = $"{i + 1}. {option}", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = System.Windows.Media.Brushes.LightGray },
                    Style = (Style)Resources["OptionButtonStyle"],
                    Tag = option
                };
                radio.Checked += (_, _) => txtCustomAnswer.Clear();
                _radioButtons.Add(radio);
                panelOptions.Children.Add(radio);
            }
        }

        // İlk seçeneği varsayılan olarak seç
        if (!_allowMultiple && _radioButtons.Count > 0)
            _radioButtons[0].IsChecked = true;
    }

    private void ApplyLanguage()
    {
        Title = LocalizationManager.Instance.GetString("AiAsksYou");
        tbHeaderTitle.Text = LocalizationManager.Instance.GetString("AISanaBirSeySoruyor");
        tbSelectionMode.Text = _allowMultiple
            ? LocalizationManager.Instance.GetString("BirdenFazlaSecenekSecebilirsiniz")
            : LocalizationManager.Instance.GetString("BirSecenekSecin");
        tbCustomAnswerHint.Text = LocalizationManager.Instance.GetString("VeyaKendiCevabiniYaz");
        btnCancel.Content = LocalizationManager.Instance.GetString("Iptal");
        btnSubmit.Content = LocalizationManager.Instance.GetString(LocalizationManager.Instance.GetString("Confirm"));
    }

    private void TxtCustomAnswer_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Özel metin girince radio seçimlerini kaldır
        if (!string.IsNullOrEmpty(txtCustomAnswer.Text))
        {
            foreach (var rb in _radioButtons)
                rb.IsChecked = false;
            foreach (var cb in _checkBoxes)
                cb.IsChecked = false;
        }
    }

    private void TxtCustomAnswer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(txtCustomAnswer.Text))
        {
            Submit();
            e.Handled = true;
        }
    }

    private void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedAnswer = Localization.IsEnglish() ? "Cancelled." : "İptal edildi.";
        DialogResult = false;
        Close();
    }

    private void Submit()
    {
        // Önce özel metin kutusunu kontrol et
        if (!string.IsNullOrWhiteSpace(txtCustomAnswer.Text))
        {
            SelectedAnswer = txtCustomAnswer.Text.Trim();
        }
        else
        {
            if (_allowMultiple)
            {
                var selected = _checkBoxes.Where(cb => cb.IsChecked == true).Select(cb => cb.Tag?.ToString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                SelectedAnswer = selected.Count > 0
                    ? string.Join(", ", selected)
                    : (Localization.IsEnglish() ? "No selection made." : "Seçim yapılmadı.");
            }
            else
            {
                var selected = _radioButtons.FirstOrDefault(rb => rb.IsChecked == true);
                SelectedAnswer = selected?.Tag?.ToString() ?? (Localization.IsEnglish() ? "No selection made." : "Seçim yapılmadı.");
            }
        }

        DialogResult = true;
        Close();
    }
}
