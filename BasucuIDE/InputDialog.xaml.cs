using System.Windows;
using System.Windows.Input;

namespace mdaiAgent
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = "";

        public InputDialog(string prompt, string title = "Giriş", string defaultResponse = "")
        {
            InitializeComponent();
            ApplyLanguage(title, prompt);
            txtInput.Text = defaultResponse;
            txtInput.Focus();
            txtInput.SelectAll();
        }

        private void ApplyLanguage(string title, string prompt)
        {
            bool isEnglish = Localization.IsEnglish();

            Title = isEnglish ? (string.Equals(title, LocalizationManager.Instance.GetString("Giris"), StringComparison.OrdinalIgnoreCase) ? "Input" : title) : title;
            lblPrompt.Content = prompt;
            btnCancel.Content = isEnglish ? "Cancel" : "İptal";
            btnOk.Content = isEnglish ? LocalizationManager.Instance.GetString("OK") : LocalizationManager.Instance.GetString("OK");
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            InputText = txtInput.Text;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                InputText = txtInput.Text;
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
            }
        }
    }
}
