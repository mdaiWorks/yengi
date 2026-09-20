using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace mdaiAgent
{
    public partial class RouterBlacklistWindow : Window
    {
        public List<BlacklistItem> ToolItems { get; set; } = new();

        public RouterBlacklistWindow(List<string> currentBlacklist)
        {
            InitializeComponent();
            ApplyLanguage();

            var allTools = ToolRegistry.GetTools();
            foreach (var tool in allTools)
            {
                if (tool.Function?.Name != null)
                {
                    ToolItems.Add(new BlacklistItem
                    {
                        Name = tool.Function.Name,
                        Description = tool.Function.Description,
                        IsBlacklisted = currentBlacklist.Contains(tool.Function.Name)
                    });
                }
            }

            icTools.ItemsSource = ToolItems;
        }

        private void ApplyLanguage()
        {
            bool isEnglish = Localization.IsEnglish();

            Title = isEnglish ? "Router Blacklist" : "Router Kara Listesi";
            txtTitle.Text = isEnglish ? "Router Blacklist" : "Router Kara Listesi";
            txtSubtitle.Text = isEnglish
                ? "Selected tools will be hidden from the router and cannot be used by it."
                : "Seçilen araçlar Router'dan GİZLENECEKTİR. Router bu araçları kullanamaz.";
            btnSave.Content = isEnglish ? "Save and Close" : "Kaydet ve Kapat";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }

    public class BlacklistItem
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsBlacklisted { get; set; }
    }
}
