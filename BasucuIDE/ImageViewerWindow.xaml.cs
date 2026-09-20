using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace mdaiAgent
{
    public partial class ImageViewerWindow : Window
    {
        private readonly string _filePath;

        public ImageViewerWindow(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;

            LoadImage();
        }

        private void LoadImage()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    txtFileName.Text = "Dosya bulunamadı!";
                    return;
                }

                var fileInfo = new FileInfo(_filePath);
                txtFileName.Text = fileInfo.Name;
                txtFilePath.Text = _filePath;

                // Dosya boyutunu biçimlendir
                string fileSizeStr = FormatFileSize(fileInfo.Length);

                // BitmapImage yükle (File lock olmadan)
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_filePath);
                bitmap.EndInit();

                imgPreview.Source = bitmap;

                txtImageDetails.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px  •  {fileSizeStr}";
                Title = $"Görsel Önizleme - {fileInfo.Name}";
            }
            catch (Exception ex)
            {
                txtFileName.Text = "Görsel yükleme hatası";
                txtImageDetails.Text = ex.Message;
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Görsel açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_filePath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Klasör açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
