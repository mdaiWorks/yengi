using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace mdaiAgent
{
    public partial class DiffWindow : Window
    {
        public DiffWindow(string filePath, string oldContent, string newContent)
        {
            InitializeComponent();

            // Dosya yolunu göster
            tbFilePath.Text = $"📄 Dosya: {Path.GetFileName(filePath)} ({filePath})";

            // Editör içeriklerini yükle
            editorOld.Text = oldContent;
            editorNew.Text = newContent;

            // Dil renklendirmesini (Highlighting) ayarla
            var ext = Path.GetExtension(filePath);
            IHighlightingDefinition? syntaxMode = HighlightingManager.Instance.GetDefinitionByExtension(ext);
            if (syntaxMode != null)
            {
                editorOld.SyntaxHighlighting = syntaxMode;
                editorNew.SyntaxHighlighting = syntaxMode;
            }

            // Dark Tema İnce Ayarları
            ConfigureEditorColors(editorOld);
            ConfigureEditorColors(editorNew);

            // Compute simple line-based diff stats (+added -removed)
            try
            {
                var oldLines = oldContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var newLines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var oldSet = new System.Collections.Generic.HashSet<string>(oldLines);
                var newSet = new System.Collections.Generic.HashSet<string>(newLines);
                int added = newSet.Except(oldSet).Count();
                int removed = oldSet.Except(newSet).Count();
                tbAddedStats.Text = added.ToString();
                tbRemovedStats.Text = removed.ToString();
            }
            catch { }
        }

        private void ConfigureEditorColors(TextEditor editor)
        {
            editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
            editor.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
