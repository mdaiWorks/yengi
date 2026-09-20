using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;

namespace mdaiAgent;

public partial class MainWindow
{
    public List<string> GetOpenFilePaths()
    {
        var filePaths = new List<string>();
        if (tcEditor.Items.Count == 0) return filePaths;

        foreach (TabItem tab in tcEditor.Items)
        {
            if (tab.Header is StackPanel sp && sp.Children.Count > 1)
            {
                if (sp.Children[1] is TextBlock tb)
                {
                    var filename = tb.Text?.TrimEnd('*');
                    if (!string.IsNullOrEmpty(filename))
                    {
                        var fullPath = Path.Combine(_selectedFolder ?? "", filename);
                        if (File.Exists(fullPath))
                        {
                            filePaths.Add(fullPath);
                        }
                    }
                }
            }
        }
        return filePaths;
    }

    public void RestoreOpenFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                OpenFile(filePath);
            }
            catch { }
        }
    }
}
