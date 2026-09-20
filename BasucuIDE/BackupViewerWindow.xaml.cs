using System.Collections.ObjectModel;


using System.Diagnostics;


using System.IO;


using System.Linq;


using System.Windows;





namespace mdaiAgent;





public partial class BackupViewerWindow : Window


{


    private readonly string _projectFolder;


    private readonly string _backupRoot;


    private readonly ObservableCollection<BackupEntry> _backupEntries = new();





    public BackupViewerWindow(string projectFolder)


    {


        InitializeComponent();


        _projectFolder = projectFolder;


        _backupRoot = Path.Combine(_projectFolder, ".mdai", "backup");


        lstBackups.ItemsSource = _backupEntries;


        ApplyLanguage();


        RefreshBackups();


    }





    private void ApplyLanguage()


    {


        bool isEnglish = Localization.IsEnglish();





            Title = isEnglish ? "Project Timeline (Backups) - Yengi" : "Proje Zaman Makinesi (Yedekler) - Yengi";


        txtHeaderTitle.Text = isEnglish ? "Project Timeline (Backups)" : "Proje Zaman Makinesi (Yedekler)";


        btnRefreshBackups.Content = isEnglish ? "🔄 Refresh" : "🔄 Yenile";


        btnShowInExplorer.Content = isEnglish ? "Show in Explorer" : "Gezginde Göster";


        btnOpenBackup.Content = isEnglish ? LocalizationManager.Instance.GetString("InspectFile") : "Dosyayı İncele";


        btnRestoreBackup.Content = isEnglish ? "✨ Restore This Backup" : "✨ Bu Yedeğe Dön";





        if (_backupEntries.Count == 0)


            tbBackupInfo.Text = isEnglish ? "No backup selected." : "Herhangi bir yedek seçilmedi.";


    }





    private void RefreshBackups()


    {


        _backupEntries.Clear();


        if (!Directory.Exists(_backupRoot))


        {


            tbBackupInfo.Text = Localization.IsEnglish() ? LocalizationManager.Instance.GetString("YedekKlasoruBulunamadi") : LocalizationManager.Instance.GetString("YedekKlasoruBulunamadi");


            return;


        }





        var files = Directory.GetFiles(_backupRoot, "*.*", SearchOption.AllDirectories)


            .OrderByDescending(File.GetLastWriteTime);





        foreach (var file in files)


        {


            var relative = Path.GetRelativePath(_backupRoot, file);


            var entry = new BackupEntry(_projectFolder, file, relative, GetBackupTimestamp(file));


            _backupEntries.Add(entry);


        }





        tbBackupInfo.Text = _backupEntries.Count == 0


            ? (Localization.IsEnglish() ? "No backups created yet." : "Henüz yedek bulunmuyor.")


            : (Localization.IsEnglish() ? $"{_backupEntries.Count} backups found." : $"{_backupEntries.Count} yedek bulundu.");


    }





    private string GetBackupTimestamp(string fullPath)


    {


        var date = File.GetLastWriteTime(fullPath);


        return date.ToString("yyyy-MM-dd HH:mm:ss");


    }





    private void LstBackups_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)


    {


        if (lstBackups.SelectedItem is BackupEntry selected)


        {


            tbBackupInfo.Text = Localization.IsEnglish() ? LocalizationManager.Instance.GetString("SeciliSelectedDisplayPath").Replace("{selected.DisplayPath}", selected.DisplayPath) : LocalizationManager.Instance.GetString("SeciliSelectedDisplayPath").Replace("{selected.DisplayPath}", selected.DisplayPath);


            btnOpenBackup.IsEnabled = true;


            btnRestoreBackup.IsEnabled = true;


            btnShowInExplorer.IsEnabled = true;


        }


        else


        {


            tbBackupInfo.Text = _backupEntries.Count == 0


                ? (Localization.IsEnglish() ? "No backups created yet." : "Henüz yedek bulunmuyor.")


                : (Localization.IsEnglish() ? "No backup selected." : "Seçili yedek yok.");


            btnOpenBackup.IsEnabled = false;


            btnRestoreBackup.IsEnabled = false;


            btnShowInExplorer.IsEnabled = false;


        }


    }





    private void BtnRefreshBackups_Click(object sender, RoutedEventArgs e)


    {


        RefreshBackups();


    }





    private void BtnOpenBackup_Click(object sender, RoutedEventArgs e)


    {


        if (lstBackups.SelectedItem is BackupEntry entry)


        {


            try


            {


                Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });


            }


            catch (Exception ex)


            {


                var dialog = new MessageDialog(


                    Localization.IsEnglish() ? $"Backup file could not be opened:\n{ex.Message}" : $"Yedek dosya açılamadı:\n{ex.Message}",


                    Localization.IsEnglish() ? "Error" : "Hata") { Owner = this };


                dialog.ShowDialog();


            }


        }


    }





    private void BtnShowInExplorer_Click(object sender, RoutedEventArgs e)


    {


        if (lstBackups.SelectedItem is BackupEntry entry)


        {


            try


            {


                var argument = $"/select,\"{entry.FullPath}\"";


                Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });


            }


            catch (Exception ex)


            {


                var dialog = new MessageDialog(


                    Localization.IsEnglish() ? $"Could not show in Explorer:\n{ex.Message}" : $"Gezginde gösterilemedi:\n{ex.Message}",


                    Localization.IsEnglish() ? "Error" : "Hata") { Owner = this };


                dialog.ShowDialog();


            }


        }


    }





    private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)


    {


        if (lstBackups.SelectedItem is BackupEntry entry)


        {


            var originalFilePath = entry.OriginalFilePath;


            


            var dialog = new MessageDialog(


                Localization.IsEnglish()


                    ? $"Are you sure you want to restore this backup?\n\nBackup: {entry.DisplayPath}\n\nWarning: The current file will be overwritten."


                    : $"Yedeği geri yüklemek istediğinize emin misiniz?\n\nYedek: {entry.DisplayPath}\n\nUyarı: Mevcut dosya üzerine yazılacaktır.",


                Localization.IsEnglish() ? "Restore Backup" : "Yedeği Geri Yükle") { Owner = this };


                


            if (dialog.ShowDialog() != true)


                return;





            try


            {


                var originalDirectory = Path.GetDirectoryName(originalFilePath);


                if (!string.IsNullOrEmpty(originalDirectory) && !Directory.Exists(originalDirectory))


                {


                    Directory.CreateDirectory(originalDirectory);


                }





                File.Copy(entry.FullPath, originalFilePath, overwrite: true);


                


                var successDialog = new MessageDialog(


                    Localization.IsEnglish() ? $"Backup restored successfully.\n{originalFilePath}" : $"Yedek başarıyla geri yüklendi.\n{originalFilePath}",


                    Localization.IsEnglish() ? "Success" : "Başarılı") { Owner = this };


                successDialog.ShowDialog();


                


                RefreshBackups();


            }


            catch (Exception ex)


            {


                var errDialog = new MessageDialog(


                    Localization.IsEnglish() ? $"Backup could not be restored:\n{ex.Message}" : $"Yedek geri yüklenemedi:\n{ex.Message}",


                    Localization.IsEnglish() ? "Error" : "Hata") { Owner = this };


                errDialog.ShowDialog();


            }


        }


    }





    private sealed class BackupEntry


    {


        private readonly string _projectFolder;





        public BackupEntry(string projectFolder, string fullPath, string relativePath, string timestamp)


        {


            _projectFolder = projectFolder;


            FullPath = fullPath;


            BackupPath = relativePath;


            DisplayPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');


            BackupFileName = Path.GetFileName(fullPath);


            BackupDateText = timestamp;


            OriginalFilePath = ComputeOriginalFilePath(relativePath);


        }





        public string FullPath { get; }


        public string BackupPath { get; }


        public string DisplayPath { get; }


        public string BackupFileName { get; }


        public string BackupDateText { get; }


        public string OriginalFilePath { get; }





        private string ComputeOriginalFilePath(string relativeBackupPath)


        {


            var originalDirectory = Path.GetDirectoryName(relativeBackupPath) ?? string.Empty;


            var fileName = Path.GetFileNameWithoutExtension(relativeBackupPath);


            var extension = Path.GetExtension(relativeBackupPath);


            var index = fileName.LastIndexOf('_');


            var originalBaseName = index > 0 ? fileName.Substring(0, index) : fileName;


            var originalRelative = string.IsNullOrEmpty(originalDirectory)


                ? originalBaseName + extension


                : Path.Combine(originalDirectory, originalBaseName + extension);





            return Path.GetFullPath(Path.Combine(_projectFolder, originalRelative));


        }


    }


}


