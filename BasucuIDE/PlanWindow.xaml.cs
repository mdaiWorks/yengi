using System;

using System.Collections.Generic;

using System.Windows;

using System.Windows.Threading;

namespace mdaiAgent;

public sealed class PlanOption

{

    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

}

public partial class PlanWindow : Window

{

    public string? SelectedOptionId { get; private set; }

    private readonly PlanModeService _planModeService;

    private readonly string _selectedFilePath;

    private readonly string _currentFileContent;

    private readonly string _selectedFolder;

    private readonly string _systemPrompt;

    private CancellationTokenSource _cts = new CancellationTokenSource();

    public PlanModeResult? PlanResult { get; private set; }

    public PlanWindow(IPlanModeService planModeService, string filePath, string content, string folder, string systemPrompt, PlanModeResult? cachedResult = null)

    {

        InitializeComponent();

        _planModeService = (PlanModeService)planModeService;

        _selectedFilePath = filePath;

        _currentFileContent = content;

        _selectedFolder = folder;

        _systemPrompt = systemPrompt;

        if (cachedResult != null && cachedResult.Success)

        {

            // Cache'den yükle — hemen göster, yükleme ekranı yok

            PlanResult = cachedResult;

            Loaded += (s, e) => ShowPlanResult(cachedResult);

        }

        else

        {

            Loaded += PlanWindow_Loaded;

        }

        Closing += (s, e) => _cts.Cancel();

    }

    private void ShowPlanResult(PlanModeResult result)

    {

        loadingGrid.Visibility = Visibility.Collapsed;

        mainContentGrid.Visibility = Visibility.Visible;

        lbPlanOptions.ItemsSource = result.Options;

        txtFullPlan.Text = result.FullPlanText;

        txtPlanOptionsSummary.Text = result.Options.Count == 1 ? LocalizationManager.Instance.GetString("_1SecenekYuklendi") : $"{result.Options.Count} seçenek yüklendi.";

        if (result.Options.Count > 0)

            lbPlanOptions.SelectedIndex = 0;

    }

#pragma warning disable VSTHRD100 // Avoid async void methods (WPF event handlers need this)

    private async void RegeneratePlan_Click(object sender, RoutedEventArgs e)

    {

        // Yeni plan yap — loading ekranına dön

        _cts.Cancel();

        _cts = new CancellationTokenSource();

        mainContentGrid.Visibility = Visibility.Collapsed;

        loadingGrid.Visibility = Visibility.Visible;

        txtLoadingStatus.Text = LocalizationManager.Instance.GetString("YeniPlanOlusturuluyor");

        await RunPlanGenerationAsync();

    }

    private async void PlanWindow_Loaded(object sender, RoutedEventArgs e)

    {

        loadingGrid.Visibility = Visibility.Visible;

        mainContentGrid.Visibility = Visibility.Collapsed;

        await RunPlanGenerationAsync();

    }

    private async Task RunPlanGenerationAsync()

    {

        var texts = new[] { "Proje analiz ediliyor...", "Eksik özellikler aranıyor...", "Mimari inceleniyor...", "Yol haritası oluşturuluyor..." };

        int textIndex = 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        timer.Tick += (s, ev) =>

        {

            textIndex = (textIndex + 1) % texts.Length;

            txtLoadingStatus.Text = texts[textIndex];

        };

        timer.Start();

        try

        {

            PlanResult = await _planModeService.CreatePlanAsync(_selectedFilePath, _currentFileContent, _selectedFolder, _systemPrompt, _cts.Token);

            timer.Stop();

            if (PlanResult != null && PlanResult.Success)

            {

                ShowPlanResult(PlanResult);

            }

            else

            {

                MessageBox.Show(PlanResult?.ErrorMessage ?? LocalizationManager.Instance.GetString("BilinmeyenBirHataOlustu"), LocalizationManager.Instance.GetString("PlanHatasi"), MessageBoxButton.OK, MessageBoxImage.Error);

                DialogResult = false;

                Close();

            }

        }

        catch (OperationCanceledException)

        {

            timer.Stop();

        }

        catch (Exception ex)

        {

            timer.Stop();

            MessageBox.Show(LocalizationManager.Instance.GetString("PlanOlusturulurkenHataOlustuExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

            DialogResult = false;

            Close();

        }

    }

    private void LbPlanOptions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)

    {

        if (lbPlanOptions.SelectedItem is PlanOption selected)

        {

            txtPlanDetail.Text = selected.Title;

            txtSelectedPlanInfo.Text = LocalizationManager.Instance.GetString("SeciliPlanSelectedId").Replace("{selected.Id}", selected.Id.ToString());

        }

        else

        {

            txtPlanDetail.Text = string.Empty;

            txtSelectedPlanInfo.Text = "Detay için bir adım seçin.";

        }

    }

    private void BtnCopyFullPlan_Click(object sender, RoutedEventArgs e)

    {

        Clipboard.SetText(txtFullPlan.Text);

        MessageBox.Show(LocalizationManager.Instance.GetString("PlanMetniPanoyaKopyalandi"), LocalizationManager.Instance.GetString("Kopyalandi"), MessageBoxButton.OK, MessageBoxImage.Information);

    }

    private void BtnApprove_Click(object sender, RoutedEventArgs e)

    {

        if (lbPlanOptions.SelectedItem is PlanOption selected)

        {

            SelectedOptionId = selected.Id;

            DialogResult = true;

            Close();

        }

        else

        {

            MessageBox.Show(LocalizationManager.Instance.GetString("LutfenOnceBirSecenekSecin"), LocalizationManager.Instance.GetString("SecimGerekli"), MessageBoxButton.OK, MessageBoxImage.Warning);

        }

    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)

    {

        DialogResult = false;

        Close();

    }

}
