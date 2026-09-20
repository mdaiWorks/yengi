using System;



using System.Globalization;



using System.IO;



using System.Linq;



using System.Windows;



using System.Windows.Controls;



using System.Windows.Data;



using System.Windows.Media;



using Microsoft.Win32;







namespace mdaiAgent



{



    public class StringJoinConverter : IValueConverter


    {



        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)



        {



            if (value is string[] strings)



            {



                return string.Join(", ", strings);



            }



            return "";



        }







        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)



        {



            throw new NotImplementedException();



        }



    }







    public class InstalledToIconConverter : IValueConverter



    {



        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)



        {



            if (value is bool isInstalled && isInstalled)



            {



                return "\u2714";



            }



            return "\u274C";



        }







        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)



        {



            throw new NotImplementedException();



        }



    }







    public class InverseBoolToVisibilityConverter : IValueConverter



    {



        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)



        {



            if (value is bool isInstalled)



            {



                return isInstalled ? Visibility.Collapsed : Visibility.Visible;



            }



            return Visibility.Visible;



        }







        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)



        {



            throw new NotImplementedException();



        }



    }

    public class LspInstallVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PluginManager.LspServerInfo server)
            {
                return !server.IsInstalled || !server.IsReady
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }







    public partial class PluginManagerWindow : Window



    {



        private void RunBackgroundLocal(Task t, string? ctx = null)



        {



            if (t == null) return;



            var _ = t.ContinueWith(tt =>



            {



                try



                {



                    var ex = tt.Exception?.Flatten();



                    if (ex != null)



                    {



                        Dispatcher.Invoke(() => MessageBox.Show($"Arka plan gÃ¶rev hatasÄ±{(ctx != null ? $" ({ctx})" : string.Empty)}: {ex.Message}", LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                    }



                }



                catch { }



            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);



        }







        public PluginManagerWindow()



        {



            InitializeComponent();

            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            Closed += (_, _) => LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;



            LoadInstalledPlugins();



            LoadAvailablePlugins();



            LoadLspServers();



        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LoadLspServers();
                txtUpdateStatus.Text = LocalizationManager.Instance.GetString("LspStatusRefreshed");
            });
        }







        private void LoadInstalledPlugins()



        {



            try



            {



                dgInstalledPlugins.ItemsSource = PluginManager.GetAllPluginManifests();



            }



            catch (Exception ex)



            {



                MessageBox.Show(LocalizationManager.Instance.GetString("EklentilerYuklenirkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



            }



        }







        private void LoadAvailablePlugins()
        {
            RunBackgroundLocal(LoadAvailablePluginsAsync(), "Plugin_LoadCatalog");
        }

        private async Task LoadAvailablePluginsAsync()
        {
            try
            {
                var plugins = await PluginManager.GetAvailablePluginsFromGitHubAsync();
                Dispatcher.Invoke(() =>
                {
                    dgAvailablePlugins.ItemsSource = plugins;
                    txtUpdateStatus.Text = plugins.Count == 0
                        ? "Güvenilir imzalı eklenti kataloğu yapılandırılmadı."
                        : $"{plugins.Count} eklenti bulundu.";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show(
                    LocalizationManager.Instance.GetString("MevcutEklentilerYuklenirkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message),
                    LocalizationManager.Instance.GetString("Hata"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
            }
        }







        private void BtnRefresh_Click(object sender, RoutedEventArgs e)



        {



            LoadInstalledPlugins();



            LoadAvailablePlugins();



        }







        private void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)



        {



            RunBackgroundLocal(Task.Run(async () =>



            {



                try



                {



                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("GuncellemeleriDenetleniyor"));







                    var updates = await PluginManager.CheckForUpdatesAsync();







                    if (updates.Count > 0)



                    {



                        var updateList = string.Join("\n", updates.Select(u =>



                            $"{u.local.Name}: {u.local.Version} â†’ {u.remote?.Version ?? "Bilinmiyor"}"));







                        Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("UpdatesCountGuncellemeMevcut").Replace("{updates.Count}", updates.Count.ToString()));







                        var result = MessageBoxResult.None;



                        Dispatcher.Invoke(() =>



                        {



                            result = MessageBox.Show(LocalizationManager.Instance.GetString("YeniGuncellemelerMevcutUpdateListGuncellemekIsterMisiniz").Replace("{updateList}", updateList), LocalizationManager.Instance.GetString("GuncellemelerMevcut"), MessageBoxButton.YesNo, MessageBoxImage.Information);



                        });







                        if (result == MessageBoxResult.Yes)



                        {



                            foreach (var update in updates)



                            {



                                if (update.remote != null)



                                {



                                    await PluginManager.DownloadAndInstallPluginAsync(update.remote);



                                }



                            }







                            Dispatcher.Invoke(() =>



                            {



                                LoadInstalledPlugins();



                                txtUpdateStatus.Text = LocalizationManager.Instance.GetString("TumGuncellemelerYuklendi");



                            });



                        }



                    }



                    else



                    {



                        Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("TumEklentilerGuncel"));



                    }



                }



                catch (Exception ex)



                {



                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("GuncellemeDenetimiBasarisiz"));



                    Dispatcher.Invoke(() => MessageBox.Show($"{LocalizationManager.Instance.GetString("GuncellemeleriDenetlerkenHataOlustuExMessage")}".Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                }



            }), "Plugin_CheckForUpdates");



        }







        private void BtnDownloadPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button dlBtn || dlBtn.Tag is not PluginManifest manifest) return;

            RunBackgroundLocal(Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("IndiriliyorManifestName").Replace("{manifest.Name}", manifest.Name));







                        bool success = await PluginManager.DownloadAndInstallPluginAsync(manifest);







                        if (success)



                        {



                            Dispatcher.Invoke(() =>



                            {



                                txtUpdateStatus.Text = LocalizationManager.Instance.GetString("ManifestNameBasariylaYuklendi").Replace("{manifest.Name}", manifest.Name);



                                LoadInstalledPlugins();



                                MessageBox.Show(LocalizationManager.Instance.GetString("ManifestNameEklentisiBasariylaYuklendi").Replace("{manifest.Name}", manifest.Name.ToString()), LocalizationManager.Instance.GetString("Basarili"), MessageBoxButton.OK, MessageBoxImage.Information);



                            });



                        }



                        else



                        {



                            Dispatcher.Invoke(() =>



                            {



                                txtUpdateStatus.Text = LocalizationManager.Instance.GetString("IndirmeBasarisizOldu");



                                MessageBox.Show(LocalizationManager.Instance.GetString("EklentiIndirilirkenBirHataOlustu"), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



                            });



                        }

                }

                catch (Exception ex)



                {



                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("IndirmeBasarisizOldu"));



                    Dispatcher.Invoke(() => MessageBox.Show($"{LocalizationManager.Instance.GetString("BilinmeyenBirHataOlustu")}: {ex.Message}", LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                }



            }), "Plugin_Download");



        }







        private void BtnUpdatePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button upBtn || upBtn.Tag is not PluginManifest localManifest) return;

            RunBackgroundLocal(Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("GuncelleniyorLocalManifestName").Replace("{localManifest.Name}", localManifest.Name));







                        var availablePlugins = PluginManager.GetAvailablePlugins();



                        var remoteManifest = availablePlugins.FirstOrDefault(p => p.Id == localManifest.Id);







                        if (remoteManifest != null)



                        {



                            bool success = await PluginManager.DownloadAndInstallPluginAsync(remoteManifest);







                            if (success)



                            {



                                Dispatcher.Invoke(() =>



                                {



                                    txtUpdateStatus.Text = LocalizationManager.Instance.GetString("LocalManifestNameBasariylaGuncellendi").Replace("{localManifest.Name}", localManifest.Name);



                                    LoadInstalledPlugins();



                                    MessageBox.Show(LocalizationManager.Instance.GetString("LocalManifestNameEklentisiBasariylaGuncellendi").Replace("{localManifest.Name}", localManifest.Name.ToString()), LocalizationManager.Instance.GetString("Basarili"), MessageBoxButton.OK, MessageBoxImage.Information);



                                });



                            }



                            else



                            {



                                Dispatcher.Invoke(() =>



                                {



                                    txtUpdateStatus.Text = LocalizationManager.Instance.GetString("GuncellemeBasarisizOldu");



                                    MessageBox.Show(LocalizationManager.Instance.GetString("EklentiGuncellenirkenBirHataOlustu"), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



                                });



                            }



                        }



                        else



                        {



                            Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("BuEklentiIcinGuncellemeBulunamadi"));



                        }

                }

                catch (Exception ex)



                {



                    Dispatcher.Invoke(() => txtUpdateStatus.Text = LocalizationManager.Instance.GetString("GuncellemeBasarisizOldu"));



                    Dispatcher.Invoke(() => MessageBox.Show($"{LocalizationManager.Instance.GetString("BilinmeyenBirHataOlustu")}: {ex.Message}", LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                }



            }), "Plugin_Update");



        }







        private void BtnClose_Click(object sender, RoutedEventArgs e)



        {



            Close();



        }







        private void BtnLoadExternal_Click(object sender, RoutedEventArgs e)



        {



            try



            {



                var openFileDialog = new OpenFileDialog



                {



                    Title = LocalizationManager.Instance.GetString("EklentiDLLDosyasiniSec"),



                    Filter = "DLL DosyalarÄ± (*.dll)|*.dll|TÃ¼m Dosyalar (*.*)|*.*",



                    Multiselect = true



                };







                if (openFileDialog.ShowDialog() == true)



                {



                    var pluginsDir = PluginManager.PluginsFolder;



                    if (!Directory.Exists(pluginsDir))



                    {



                        Directory.CreateDirectory(pluginsDir);



                    }







                    foreach (var filePath in openFileDialog.FileNames)



                    {



                        var fileName = Path.GetFileName(filePath);



                        var destPath = Path.Combine(pluginsDir, fileName);



                        



                        File.Copy(filePath, destPath, true);



                    }







                    MessageBox.Show(



                        $"Eklenti{(openFileDialog.FileNames.Length > 1 ? "ler" : "")} baÅŸarÄ±yla yÃ¼klendi! DeÄŸiÅŸikliklerin etkili olmasÄ± iÃ§in lÃ¼tfen programÄ± yeniden baÅŸlatÄ±n.", 



                        "BaÅŸarÄ±lÄ±", 



                        MessageBoxButton.OK, 



                        MessageBoxImage.Information);



                    



                    LoadInstalledPlugins();



                }



            }



            catch (Exception ex)



            {



                MessageBox.Show(LocalizationManager.Instance.GetString("EklentiYuklenirkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



            }



        }







        private void BtnOpenPluginsFolder_Click(object sender, RoutedEventArgs e)



        {



            try



            {



                var pluginsDir = PluginManager.PluginsFolder;



                if (!Directory.Exists(pluginsDir))



                {



                    Directory.CreateDirectory(pluginsDir);



                }



                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo



                {



                    FileName = pluginsDir,



                    UseShellExecute = true



                });



            }



            catch (Exception ex)



            {



                MessageBox.Show(LocalizationManager.Instance.GetString("EklentilerKlasoruAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



            }



        }







        private void LoadLspServers()



        {



            try



            {



                dgLspServers.ItemsSource = PluginManager.LspServerManager.GetAllLspServers();



                txtLspStatus.Text = LocalizationManager.Instance.GetString("LSPSunuculariYuklendi");



            }



            catch (Exception ex)



            {



                MessageBox.Show(LocalizationManager.Instance.GetString("LSPSunuculariYuklenirkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



            }



        }







        private void BtnRefreshLspStatus_Click(object sender, RoutedEventArgs e)
        {
            RunBackgroundLocal(RefreshLspStatusAsync(), "LSP_StatusRefresh");
        }

        private async Task RefreshLspStatusAsync()
        {
            Dispatcher.Invoke(() => txtLspStatus.Text = LocalizationManager.Instance.GetString("LspStatusChecking"));

            await PluginManager.LspServerManager.RefreshRuntimeStatusAsync();

            Dispatcher.Invoke(() =>
            {
                LoadLspServers();
                txtLspStatus.Text = LocalizationManager.Instance.GetString("LspStatusRefreshed");
            });
        }







        private void BtnInstallLsp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button __lspBtn || __lspBtn.Tag is not PluginManager.LspServerInfo server) return;

            RunBackgroundLocal(Task.Run(async () =>
            {
                try
                {
                    ProgressWindow? progressWindow = null;
                    Dispatcher.Invoke(() =>



                        {



                            progressWindow = new ProgressWindow



                            {



                                Owner = this,



                                Title = $"{server.Name} Kuruluyor"



                            };



                            progressWindow.Show();



                        });







                        IProgress<(double, string)> progress = new Progress<(double, string)>(update =>



                        {



                            if (progressWindow != null)



                            {



                                Dispatcher.Invoke(() => progressWindow.UpdateProgress(update.Item1, update.Item2));



                            }



                        });







                        // If the LSP corresponds to a language we can auto-install locally, offer that first



                        string? error = null;



                        try



                        {



                            if (server.Id == "typescript" || server.Id == "pyright")



                            {



                                // Ask user to pick a project folder to install into



                                MessageBoxResult doInstall = MessageBoxResult.No; Dispatcher.Invoke(() => { doInstall = MessageBox.Show(this, LocalizationManager.Instance.GetString("ServerNameIcinProjeIcineOtomatikYuklemeYapmakIsterMisinizOnerilen").Replace("{server.Name}", server.Name), "Otomatik Kurulum", MessageBoxButton.YesNo, MessageBoxImage.Question); });



                                if (doInstall == MessageBoxResult.Yes)



                                {



                                    // Let user choose folder



                                    string? folder = null;



                                    Dispatcher.Invoke(() =>



                                    {



                                        using var dlg = new System.Windows.Forms.FolderBrowserDialog();



                                        dlg.Description = "LSP paketlerini yÃ¼klemek istediÄŸiniz proje klasÃ¶rÃ¼nÃ¼ seÃ§in";



                                        dlg.ShowNewFolderButton = false;



                                        var res = dlg.ShowDialog();



                                        if (res == System.Windows.Forms.DialogResult.OK)



                                            folder = dlg.SelectedPath;



                                    });







                                    if (!string.IsNullOrEmpty(folder))



                                    {



                                        error = await DependencyInstaller.InstallForLanguageAsync(folder, server.Id, progress);



                                    }



                                    else



                                    {



                                        progress.Report((0, "Proje klasÃ¶rÃ¼ seÃ§ilmedi; manuel kurulum kÄ±lavuzuna yÃ¶nlendiriliyor."));



                                    }



                                }



                            }







                            if (string.IsNullOrEmpty(error))



                            {



                                // Fallback to default download/install guidance



                                error = await PluginManager.LspServerManager.DownloadAndInstallAsync(server, progress);



                            }



                        }



                        catch (Exception ex)



                        {



                            error = ex.Message;



                        }







                        if (progressWindow != null)



                        {



                            Dispatcher.Invoke(() => progressWindow.Close());



                        }







                        if (string.IsNullOrEmpty(error))
                        {
                            await PluginManager.LspServerManager.RefreshRuntimeStatusAsync();
                            Dispatcher.Invoke(() =>
                            {
                                LoadLspServers();
                                var status = PluginManager.LspServerManager.GetAllLspServers()
                                    .FirstOrDefault(item => item.Id.Equals(server.Id, StringComparison.OrdinalIgnoreCase));

                                if (status?.IsReady == true)
                                {
                                    MessageBox.Show($"{server.Name} kuruldu ve çalışmaya hazır.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    MessageBox.Show(
                                        $"{server.Name} kuruldu ancak başlatılamadı.\n\n{status?.LastError ?? "Durum doğrulanamadı."}",
                                        "Kurulum doğrulaması",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                                }
                            });
                        }



                        else



                        {



                            Dispatcher.Invoke(() => MessageBox.Show(LocalizationManager.Instance.GetString("KurulumHatasiError").Replace("{error}", error), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                        }

                }
                catch (Exception ex)



                {



                    Dispatcher.Invoke(() => MessageBox.Show(LocalizationManager.Instance.GetString("KurulumHatasiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error));



                }



            }), "Plugin_InstallLsp");



        }







        private void BtnGuideLsp_Click(object sender, RoutedEventArgs e)



        {



            try



            {



                if (sender is not Button __lspBtn || __lspBtn.Tag is not PluginManager.LspServerInfo server) return; // 



                {



                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo



                    {



                        FileName = server.InstallationGuideUrl,



                        UseShellExecute = true



                    });



                }



            }



            catch (Exception ex)



            {



                MessageBox.Show(LocalizationManager.Instance.GetString("KilavuzAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);



            }



        }



    }







    public partial class ProgressWindow : Window



    {



        private TextBlock? txtMessage;



        private ProgressBar? progressBar;



        private TextBlock? txtPercentage;







        public ProgressWindow()



        {



            Title = LocalizationManager.Instance.GetString("IslemYapiliyor");



            Width = 400;



            Height = 150;



            WindowStartupLocation = WindowStartupLocation.CenterOwner;



            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));



            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));



            ResizeMode = ResizeMode.NoResize;







            var grid = new Grid();



            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });



            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });



            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });



            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });



            grid.Margin = new Thickness(20);







            txtMessage = new TextBlock



            {



                Text = LocalizationManager.Instance.GetString("IslemHazirlaniyor"),



                TextWrapping = TextWrapping.Wrap,



                Margin = new Thickness(0, 0, 0, 10)



            };



            Grid.SetRow(txtMessage, 1);







            var progressPanel = new StackPanel { Orientation = Orientation.Horizontal };



            progressBar = new ProgressBar



            {



                Height = 20,



                Minimum = 0,



                Maximum = 100,



                Value = 0,



                Margin = new Thickness(0, 0, 10, 0),



                VerticalAlignment = VerticalAlignment.Center,



                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),



                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204))



            };



            txtPercentage = new TextBlock



            {



                Text = "0%",



                Width = 40,



                VerticalAlignment = VerticalAlignment.Center,



                TextAlignment = TextAlignment.Right



            };



            progressPanel.Children.Add(progressBar);



            progressPanel.Children.Add(txtPercentage);



            Grid.SetRow(progressPanel, 2);







            grid.Children.Add(txtMessage);



            grid.Children.Add(progressPanel);



            this.Content = grid;



        }







        public void UpdateProgress(double percentage, string message)



        {



            if (progressBar != null)



            {



                progressBar.Value = percentage;



            }



            if (txtPercentage != null)



            {



                txtPercentage.Text = $"{percentage:F0}%";



            }



            if (txtMessage != null)



            {



                txtMessage.Text = message;



            }



        }



    }



}




