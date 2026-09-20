using System.Collections.Generic;
using System.Windows;

namespace mdaiAgent
{
    public class KeyboardShortcut
    {
        public string Shortcut { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public partial class KeyboardShortcutsWindow : Window
    {
        public KeyboardShortcutsWindow()
        {
            InitializeComponent();
            ApplyLanguage();
            LoadShortcuts();
        }

        private void ApplyLanguage()
        {
            bool isEnglish = Localization.IsEnglish();

            Title = isEnglish ? LocalizationManager.Instance.GetString("KisayollarVeKomutlarMdaiAgent") : LocalizationManager.Instance.GetString("KisayollarVeKomutlarMdaiAgent");
            txtWindowTitle.Text = isEnglish ? "⚡ Shortcuts and Commands" : "⚡ Kısayollar ve Komutlar";
            txtWindowSubtitle.Text = isEnglish
                ? "Use keyboard shortcuts and AI commands more efficiently."
                : "Uygulamayı daha verimli kullanmak için klavye kısayolları ve AI komutları";

            tabShortcuts.Header = isEnglish ? "⌨️ Keyboard Shortcuts" : "⌨️ Klavye Kısayolları";
            tabSlashCommands.Header = isEnglish ? "🤖 Slash Commands (/)" : "🤖 Slash Komutları (/)";
            colShortcut.Header = isEnglish ? "Shortcut" : "Kısayol";
            colShortcutDesc.Header = isEnglish ? "Description" : "Açıklama";
            colSlashCommand.Header = isEnglish ? "Command" : "Komut";
            colSlashCommandDesc.Header = isEnglish ? "Description" : "Açıklama";
            btnClose.Content = isEnglish ? LocalizationManager.Instance.GetString("Close") : LocalizationManager.Instance.GetString("Close");
        }

        private void LoadShortcuts()
        {
            var shortcuts = new List<KeyboardShortcut>
            {
                new() { Shortcut = "Ctrl + P", Description = Localization.Get("Komut Paletini Aç", "Open command palette") },
                new() { Shortcut = "Ctrl + S", Description = Localization.Get("Aktif Dosyayı Kaydet", "Save active file") },
                new() { Shortcut = "Ctrl + Shift + S", Description = Localization.Get("Tüm Dosyaları Kaydet", "Save all files") },
                new() { Shortcut = "Ctrl + L", Description = Localization.Get("Terminal Çıktısını Temizle", "Clear terminal output") },
                new() { Shortcut = "Ctrl + Alt + T", Description = Localization.Get("Terminal Girişine Odaklan", "Focus terminal input") },
                new() { Shortcut = "Ctrl + F", Description = Localization.Get("Metin Editöründe Arama", "Search in text editor") },
                new() { Shortcut = "F12", Description = LocalizationManager.Instance.GetString("GoToDefinitionShortcut") },
                new() { Shortcut = "Shift + F12", Description = LocalizationManager.Instance.GetString("FindReferencesShortcut") },
                new() { Shortcut = "Ctrl + T", Description = LocalizationManager.Instance.GetString("ShowDocumentSymbolsShortcut") },
                new() { Shortcut = "Enter", Description = Localization.Get("Komut Paletinde Seçili Komutu Çalıştır / Sohbet Mesajı Gönder", "Run selected command in palette / send chat message") },
                new() { Shortcut = "Shift + Enter", Description = Localization.Get("Sohbet Mesajında Yeni Satır", "New line in chat message") },
                new() { Shortcut = "↑ / ↓", Description = Localization.Get("Komut Paletinde Gezin / Terminal Geçmişini Gezin", "Navigate in command palette / terminal history") },
                new() { Shortcut = "Escape", Description = Localization.Get("Komut Paletini Kapat / Pencereleri Kapat", "Close command palette / windows") }
            };

            var slashCommands = new List<KeyboardShortcut>
            {
                new() { Shortcut = "/plan", Description = Localization.Get("İstediğiniz projenin teknik mimari planını çizer (implementation_plan.md)", "Generates the technical architecture plan for your project (implementation_plan.md)") },
                new() { Shortcut = "/tasks", Description = Localization.Get("Çizilen planı uygulanabilir görev kutucuklarına (checklist) böler (task.md)", "Breaks the plan into actionable task checklist items (task.md)") },
                new() { Shortcut = "/spec", Description = Localization.Get("Projeniz için gereksinimler dosyası oluşturur (spec.md)", "Creates a requirements document for your project (spec.md)") },
                new() { Shortcut = "/implement", Description = Localization.Get("Belirlenen plana göre kodlamaya ve uygulamaya başlar", "Starts coding and implementation according to the selected plan") },
                new() { Shortcut = "/constitution", Description = Localization.Get("Projenin temel kurallarını ve yapı taşlarını oluşturur", "Creates the basic rules and building blocks for the project") }
            };

            dgShortcuts.ItemsSource = shortcuts;
            dgSlashCommands.ItemsSource = slashCommands;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
