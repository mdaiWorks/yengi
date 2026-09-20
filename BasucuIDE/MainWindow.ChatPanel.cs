using System;

using System.Collections.Generic;

using System.Diagnostics;

using System.IO;

using System.Linq;

using System.Text.RegularExpressions;

using System.Threading;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Documents;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Media.Effects;

using System.Windows.Threading;

using Ellipse = System.Windows.Shapes.Ellipse;

using ICSharpCode.AvalonEdit;

using Microsoft.Win32;

namespace mdaiAgent;

public partial class MainWindow

{

    private CancellationTokenSource? _chatCts;

    private List<string> _mentionedFiles = new();

    private readonly List<StackPanel> _assistantActionBars = new();

    private readonly List<StackPanel> _currentTurnActionBars = new();

    private ListBoxItem? _activeProgressItem;

    private sealed class ChatMessageUiContext

    {

        public string DisplayText { get; init; } = string.Empty;

        public int? HistoryIndex { get; init; }

        public ExtendedChatMessage? Message { get; init; }

        public List<Attachment>? Attachments { get; init; }

    }

    private void AddTerminalMessage(string message)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => AddTerminalMessage(message)));

            return;

        }

        if (string.IsNullOrWhiteSpace(message))

            return;

        if (_terminalService != null)

        {

            _terminalService.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");

        }

        else

        {

            txtTerminal.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

            txtTerminal.CaretIndex = txtTerminal.Text.Length;

            txtTerminal.ScrollToEnd();

        }

        if (message.Contains("başladı", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("başarılı", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("hata", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("started", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("completed", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("success", StringComparison.OrdinalIgnoreCase) ||

            message.Contains("error", StringComparison.OrdinalIgnoreCase))

        {

            UpdateProcessStatus(message);

        }

    }

    private void RefreshChatSessionsList()

    {

        foreach (var session in _chatFlowService.ChatSessions)

        {

            session.IsSelected = session == _chatFlowService.ActiveSession;

        }

        icChatSessions.ItemsSource = null;

        icChatSessions.ItemsSource = _chatFlowService.ChatSessions;

    }

    private void ChatSession_Click(object sender, MouseButtonEventArgs e)

    {

        if (sender is Border border && border.DataContext is ChatSession session)

        {

            SelectChatSession(session);

        }

    }

    private void SelectChatSession(ChatSession session)

    {

        if (session != null)

        {

            _chatFlowService.SetActiveSession(session);

            RefreshChatSessionsList();

            UpdateChatHistory();

            ShowChatView();

        }

    }

    private void ShowChatView()

        {

            chatListView.Visibility = Visibility.Collapsed;

            chatView.Visibility = Visibility.Visible;

            // DataContext'i ayarlayalim

            if (icTimeline != null) icTimeline.DataContext = _chatPanelViewModel;

            if (icTodoList != null) icTodoList.DataContext = _chatPanelViewModel;

            if (icFileChanges != null) icFileChanges.DataContext = _chatPanelViewModel;

            if (icMentionedChips != null) icMentionedChips.DataContext = _chatPanelViewModel;

            if (_chatFlowService.ActiveSession != null)

            {

                txtCurrentChatName.Text = _chatFlowService.ActiveSession.Name;

            }

            _ = Dispatcher.BeginInvoke(() => txtChatInput?.Focus(), DispatcherPriority.Background);

            // Varsayilan olarak sohbet geçmişini gösterelim (canlı ve kaydedilmiş mesajlar görünür olsun)

            BtnShowChatHistory_Click(null!, null!);

        }

    private void ShowChatListView()

    {

        chatListView.Visibility = Visibility.Visible;

        chatView.Visibility = Visibility.Collapsed;

    }

    private void BtnBackToList_Click(object sender, RoutedEventArgs e)

    {

        ShowChatListView();

    }

    private void BtnRenameCurrentChat_Click(object sender, RoutedEventArgs e)

    {

        if (_chatFlowService.ActiveSession == null) return;

        var session = _chatFlowService.ActiveSession;

        var dialog = new Window

        {

            Title = Localization.Get("Sohbet Adını Değiştir", "Rename Chat"),

            Width = 400,

            Height = 180,

            WindowStartupLocation = WindowStartupLocation.CenterOwner,

            Owner = this,

            ResizeMode = ResizeMode.NoResize,

            Background = new SolidColorBrush(Color.FromRgb(30, 30, 50)),

            Foreground = Brushes.White

        };

        var grid = new Grid();

        var stackPanel = new StackPanel { Margin = new Thickness(20) };

        var label = new TextBlock

        {

            Text = Localization.Get("Yeni sohbet adını girin:", LocalizationManager.Instance.GetString("EnterNewChatName")),

            Foreground = Brushes.LightGray,

            Margin = new Thickness(0, 0, 0, 10)

        };

        var textBox = new TextBox

        {

            Text = session.Name,

            FontSize = 14,

            Padding = new Thickness(8),

            Background = new SolidColorBrush(Color.FromRgb(20, 20, 40)),

            BorderBrush = Brushes.Gray,

            Foreground = Brushes.White

        };

        var buttonPanel = new StackPanel

        {

            Orientation = Orientation.Horizontal,

            HorizontalAlignment = HorizontalAlignment.Right,

            Margin = new Thickness(0, 20, 0, 0)

        };

        var cancelButton = new Button

        {

            Content = Localization.Get("İptal", "Cancel"),

            Padding = new Thickness(20, 8, 20, 8),

            Margin = new Thickness(0, 0, 10, 0),

            Background = Brushes.Transparent,

            BorderBrush = Brushes.Gray,

            Foreground = Brushes.White

        };

        var saveButton = new Button

        {

            Content = Localization.Get("Kaydet", "Save"),

            Padding = new Thickness(20, 8, 20, 8),

            Background = new SolidColorBrush(Color.FromRgb(100, 100, 200)),

            BorderThickness = new Thickness(0),

            Foreground = Brushes.White

        };

        cancelButton.Click += (s, args) => dialog.Close();

        saveButton.Click += (s, args) =>

        {

            if (!string.IsNullOrWhiteSpace(textBox.Text))

            {

                session.Name = textBox.Text;

                _chatFlowService.SaveSessions();

                txtCurrentChatName.Text = session.Name;

                RefreshChatSessionsList();

                Notify(Localization.Get("Sohbet adı güncellendi.", "Chat name updated."), NotificationSeverity.Success);

            }

            dialog.Close();

        };

        buttonPanel.Children.Add(cancelButton);

        buttonPanel.Children.Add(saveButton);

        stackPanel.Children.Add(label);

        stackPanel.Children.Add(textBox);

        stackPanel.Children.Add(buttonPanel);

        grid.Children.Add(stackPanel);

        dialog.Content = grid;

        dialog.ShowDialog();

    }

    private void ChatSessionRename_Click(object sender, RoutedEventArgs e)

    {

        e.Handled = true;

        if (sender is Button button && button.Tag is ChatSession session)

        {

            var dialog = new Window

            {

                Title = Localization.Get("Sohbet Adını Değiştir", "Rename Chat"),

                Width = 400,

                Height = 180,

                WindowStartupLocation = WindowStartupLocation.CenterOwner,

                Owner = this,

                ResizeMode = ResizeMode.NoResize,

                Background = new SolidColorBrush(Color.FromRgb(30, 30, 50)),

                Foreground = Brushes.White

            };

            var grid = new Grid();

            var stackPanel = new StackPanel { Margin = new Thickness(20) };

            var label = new TextBlock

            {

                Text = Localization.Get("Yeni sohbet adını girin:", LocalizationManager.Instance.GetString("EnterNewChatName")),

                Foreground = Brushes.LightGray,

                Margin = new Thickness(0, 0, 0, 10)

            };

            var textBox = new TextBox

            {

                Text = session.Name,

                FontSize = 14,

                Padding = new Thickness(8),

                Background = new SolidColorBrush(Color.FromRgb(20, 20, 40)),

                BorderBrush = Brushes.Gray,

                Foreground = Brushes.White

            };

            var buttonPanel = new StackPanel

            {

                Orientation = Orientation.Horizontal,

                HorizontalAlignment = HorizontalAlignment.Right,

                Margin = new Thickness(0, 20, 0, 0)

            };

            var cancelButton = new Button

            {

                Content = Localization.Get("İptal", "Cancel"),

                Padding = new Thickness(20, 8, 20, 8),

                Margin = new Thickness(0, 0, 10, 0),

                Background = Brushes.Transparent,

                BorderBrush = Brushes.Gray,

                Foreground = Brushes.White

            };

            var saveButton = new Button

            {

                Content = Localization.Get("Kaydet", "Save"),

                Padding = new Thickness(20, 8, 20, 8),

                Background = new SolidColorBrush(Color.FromRgb(100, 100, 200)),

                BorderThickness = new Thickness(0),

                Foreground = Brushes.White

            };

            cancelButton.Click += (s, args) => dialog.Close();

            saveButton.Click += (s, args) =>

            {

                if (!string.IsNullOrWhiteSpace(textBox.Text))

                {

                    session.Name = textBox.Text;

                    _chatFlowService.SaveSessions();

                    RefreshChatSessionsList();

                    Notify(Localization.Get("Sohbet adı güncellendi.", "Chat name updated."), NotificationSeverity.Success);

                }

                dialog.Close();

            };

            buttonPanel.Children.Add(cancelButton);

            buttonPanel.Children.Add(saveButton);

            stackPanel.Children.Add(label);

            stackPanel.Children.Add(textBox);

            stackPanel.Children.Add(buttonPanel);

            grid.Children.Add(stackPanel);

            dialog.Content = grid;

            dialog.ShowDialog();

        }

    }

    private void ChatSessionDelete_Click(object sender, RoutedEventArgs e)

    {

        e.Handled = true;

        if (sender is Button button && button.Tag is ChatSession session)

        {

            if (_chatFlowService.ChatSessions.Count > 1)

            {

                var result = MessageBox.Show(LocalizationManager.Instance.GetString("SessionNameSohbetiniSilmekIstediginizeEminMisiniz").Replace("{session.Name}", session.Name), "Sohbeti Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)

                {

                    if (session != _chatFlowService.ActiveSession)

                    {

                        _chatFlowService.SetActiveSession(session);

                    }

                    _chatFlowService.DeleteActiveSession();

                    RefreshChatSessionsList();

                    Notify("Sohbet silindi.", NotificationSeverity.Success);

                    ShowChatListView();

                }

            }

        }

    }

    private void UpdateChatHistory()

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(UpdateChatHistory);

            return;

        }

        lstChatHistory.Items.Clear();

        _assistantActionBars.Clear();

        _loadMoreButton = null;

        var activeSession = _chatFlowService.ActiveSession;

        if (activeSession == null)

        {

            AddChatMessage(LocalizationManager.Instance.GetString("System"), LocalizationManager.Instance.GetString("ChatWelcome"));

            return;

        }

        _loadedMessageCount = Math.Min(activeSession.History.Count, MaxChatMessagesPerLoad);

        for (int i = activeSession.History.Count - _loadedMessageCount; i < activeSession.History.Count; i++)

        {

            var msg = activeSession.History[i];

            if (msg.Role == "user")

            {

                var context = new ChatMessageUiContext

                {

                    DisplayText = msg.Content ?? string.Empty,

                    HistoryIndex = i,

                    Message = msg,

                    Attachments = msg.Attachments

                };

                AddChatMessage("Sen", msg.Content ?? "", msg.Attachments, context);

            }

            else if (msg.Role == "assistant")

            {

                if (!string.IsNullOrEmpty(msg.Content))

                {

                    AddChatMessage("AI Asistan", msg.Content, msg.Attachments);

                }

            }

        }

        if (_loadedMessageCount < activeSession.History.Count)

        {

            _loadMoreButton = new Button

            {

                Content = LocalizationManager.Instance.GetString("DahaFazlaYukle"),

                Padding = new Thickness(10, 5, 10, 5),

                Margin = new Thickness(0, 5, 0, 5),

                HorizontalAlignment = HorizontalAlignment.Center,

                Tag = "LOAD_MORE"

            };

            _loadMoreButton.Click += BtnLoadMore_Click;

            lstChatHistory.Items.Insert(0, _loadMoreButton);

        }

        if (activeSession.History.Count == 0)

        {

            AddChatMessage(LocalizationManager.Instance.GetString("System"), LocalizationManager.Instance.GetString("ChatWelcome"));

        }

    }

    private void BtnLoadMore_Click(object sender, RoutedEventArgs e)

    {

        var activeSession = _chatFlowService.ActiveSession;

        if (activeSession == null)

            return;

        int newLoadedCount = Math.Min(activeSession.History.Count, _loadedMessageCount + MaxChatMessagesPerLoad);

        if (newLoadedCount <= _loadedMessageCount)

            return;

        if (_loadMoreButton != null && lstChatHistory.Items.Contains(_loadMoreButton))

        {

            lstChatHistory.Items.Remove(_loadMoreButton);

        }

        int messagesToAdd = newLoadedCount - _loadedMessageCount;

        int startIndex = activeSession.History.Count - newLoadedCount;

        for (int i = 0; i < messagesToAdd; i++)

        {

            var historyIndex = startIndex + i;

            var msg = activeSession.History[historyIndex];

            if (msg.Role == "user")

            {

                var context = new ChatMessageUiContext

                {

                    DisplayText = msg.Content ?? string.Empty,

                    HistoryIndex = historyIndex,

                    Message = msg,

                    Attachments = msg.Attachments

                };

                InsertChatMessageAt(0, "Sen", msg.Content ?? "", context);

            }

            else if (msg.Role == "assistant")

            {

                if (!string.IsNullOrEmpty(msg.Content))

                {

                    InsertChatMessageAt(0, "AI Asistan", msg.Content);

                }

            }

        }

        _loadedMessageCount = newLoadedCount;

        if (_loadedMessageCount < activeSession.History.Count)

        {

            _loadMoreButton = new Button

            {

                Content = LocalizationManager.Instance.GetString("DahaFazlaYukle"),

                Padding = new Thickness(10, 5, 10, 5),

                Margin = new Thickness(0, 5, 0, 5),

                HorizontalAlignment = HorizontalAlignment.Center,

                Tag = "LOAD_MORE"

            };

            _loadMoreButton.Click += BtnLoadMore_Click;

            lstChatHistory.Items.Insert(0, _loadMoreButton);

        }

        else

        {

            _loadMoreButton = null;

        }

    }

    private void InsertChatMessageAt(int index, string sender, string message, ChatMessageUiContext? messageContext = null)

    {

        bool isUser = sender == "Sen";

        var outerPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 7, 0, 7) };

        if (isUser)

        {

            var messageBorder = new Border

            {

                CornerRadius = new CornerRadius(10, 10, 3, 10),

                Padding = new Thickness(14, 9, 14, 9),

                MaxWidth = 480,

                Margin = new Thickness(25, 0, 4, 0),

                HorizontalAlignment = HorizontalAlignment.Right,

                Background = TryFindResource("ChatUserBubbleBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 120, 210)),

                BorderBrush = TryFindResource("ChatUserBubbleBorderBrush") as Brush,

                BorderThickness = new Thickness(1)

            };

            var innerGrid = new Grid();

            var contentStack = new StackPanel

            {

                Orientation = Orientation.Vertical,

                Margin = new Thickness(0, 0, 30, 0)

            };

            var contentElement = CreateChatMessageContent(message);

            if (contentElement is FrameworkElement fe)

            {

                fe.HorizontalAlignment = HorizontalAlignment.Stretch;

            }

            contentStack.Children.Add(contentElement);

            innerGrid.Children.Add(contentStack);

            if (messageContext != null)

            {

                var editButton = new Button

                {

                    Content = "✏️",

                    Padding = new Thickness(4, 2, 4, 2),

                    FontSize = 11,

                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),

                    Foreground = Brushes.White,

                    BorderThickness = new Thickness(0),

                    Tag = messageContext,

                    Cursor = Cursors.Hand,

                    Height = 20,

                    Width = 20,

                    HorizontalAlignment = HorizontalAlignment.Right,

                    VerticalAlignment = VerticalAlignment.Top,

                    HorizontalContentAlignment = HorizontalAlignment.Center,

                    VerticalContentAlignment = VerticalAlignment.Center,

                    ToolTip = "Mesajı düzenle (geçmişi bu noktadan itibaren sıfırlar)"

                };

                editButton.Click += EditUserMessage_Click;

                innerGrid.Children.Add(editButton);

            }

            messageBorder.Child = innerGrid;

            DockPanel.SetDock(messageBorder, Dock.Right);

            outerPanel.Children.Add(messageBorder);

        }

        else

        {

            var assistantBorder = new Border

            {

                Background = Brushes.Transparent,

                BorderThickness = new Thickness(0),

                Padding = new Thickness(0),

                Margin = new Thickness(0, 0, 10, 0),

                HorizontalAlignment = HorizontalAlignment.Left

            };

            var assistantDock = new DockPanel { LastChildFill = true };

            var accentBar = new Border

            {

                Background = TryFindResource("ChatAssistantAccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(88, 166, 255)),

                Width = 2,

                HorizontalAlignment = HorizontalAlignment.Stretch,

                VerticalAlignment = VerticalAlignment.Stretch

            };

            assistantDock.Children.Add(accentBar);

            DockPanel.SetDock(accentBar, Dock.Left);

            var contentStack = new StackPanel

            {

                Orientation = Orientation.Vertical,

                Margin = new Thickness(10, 5, 16, 5),

                HorizontalAlignment = HorizontalAlignment.Stretch

            };

            contentStack.Children.Add(CreateChatMessageContent(message));

            var actionBar = CreateAssistantMessageActions(message);

            contentStack.Children.Add(actionBar);

            assistantBorder.MouseEnter += (_, _) => actionBar.Opacity = 1;

            assistantBorder.MouseLeave += (_, _) => actionBar.Opacity = 0.42;

            assistantDock.Children.Add(contentStack);

            assistantBorder.Child = assistantDock;

            outerPanel.Children.Add(assistantBorder);

        }

        var item = new ListBoxItem

        {

            Content = outerPanel,

            HorizontalContentAlignment = HorizontalAlignment.Stretch,

            Padding = new Thickness(2)

        };

        item.Tag = messageContext != null ? (object)messageContext : sender;

        lstChatHistory.Items.Insert(index, item);

    }

    private void EditUserMessage_Click(object sender, RoutedEventArgs e)

    {

        if (sender is not Button button || button.Tag is not ChatMessageUiContext context)

        {

            return;

        }

        var currentText = context.DisplayText;

        var dialog = new InputDialog("Mesajı düzenleyin:", "Mesajı Düzenle", currentText);

        if (dialog.ShowDialog() != true)

        {

            return;

        }

        var updatedText = dialog.InputText?.Trim();

        if (string.IsNullOrWhiteSpace(updatedText))

        {

            return;

        }

        var activeSession = _chatFlowService.ActiveSession;

        if (activeSession == null)

        {

            return;

        }

        var targetIndex = context.HistoryIndex ?? -1;

        if (targetIndex < 0 || targetIndex >= activeSession.History.Count)

        {

            return;

        }

        var trimmedHistory = ChatFlowService.TrimHistoryForEdit(activeSession.History, targetIndex);

        activeSession.History.Clear();

        activeSession.History.AddRange(trimmedHistory);

        activeSession.History.Add(new ExtendedChatMessage

        {

            Role = "user",

            Content = updatedText,

            Attachments = context.Attachments ?? new List<Attachment>()

        });

        _chatFlowService.SaveSessions();

        UpdateChatHistory();

        RunBackground(SendMessageAsync(updatedText, false, context.Attachments), "SendMessage");

    }

    private void BtnNewChat_Click(object sender, RoutedEventArgs e)

        {

            _chatFlowService.CreateNewSession(Localization.Get("Yeni Sohbet", "New Chat"));

            RefreshChatSessionsList();

            Notify(Localization.Get("Yeni sohbet oluşturuldu.", "New chat created."), NotificationSeverity.Success);

            ShowChatView();

        }

    private void BtnPlanMode_Click(object sender, RoutedEventArgs e)

    {

        if (_isPlanModeRunning)

        {

            Notify(Localization.Get("Plan modu zaten hazırlanıyor. Lütfen bekleyin.", "Plan mode is already being prepared. Please wait."), NotificationSeverity.Info);

            return;

        }

        RunBackground(RunAiPlanModeAsync(), "AiPlanMode");

    }

    private void BtnDeleteChat_Click(object sender, RoutedEventArgs e)

    {

        if (_chatFlowService.ActiveSession != null && _chatFlowService.ChatSessions.Count > 1)

        {

            var result = MessageBox.Show(

                Localization.Get($"\"{_chatFlowService.ActiveSession.Name}\" sohbetini silmek istediğinize emin misiniz?", $"Are you sure you want to delete the chat \"{_chatFlowService.ActiveSession.Name}\"?"),

                Localization.Get("Sohbeti Sil", "Delete Chat"),

                MessageBoxButton.YesNo,

                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)

            {

                var deletedName = _chatFlowService.ActiveSession.Name;

                _chatFlowService.DeleteActiveSession();

                RefreshChatSessionsList();

                UpdateChatHistory();

                Notify(Localization.Get($"{deletedName} sohbeti silindi.", $"Chat {deletedName} was deleted."), NotificationSeverity.Success);

            }

        }

        else if (_chatFlowService.ActiveSession != null)

        {

            var result = MessageBox.Show(

                Localization.Get("Sohbet geçmişini temizlemek ister misiniz?", "Do you want to clear the chat history?"),

                Localization.Get("Temizle", "Clear"),

                MessageBoxButton.YesNo,

                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)

            {

                _chatFlowService.ClearActiveSessionHistory();

                RefreshChatSessionsList();

                UpdateChatHistory();

                Notify(Localization.Get("Sohbet geçmişi temizlendi.", "Chat history cleared."), NotificationSeverity.Success);

            }

        }

    }

    private void BtnKeyboardShortcuts_Click(object sender, RoutedEventArgs e)

    {

        var shortcutsWindow = new KeyboardShortcutsWindow

        {

            Owner = this

        };

        shortcutsWindow.ShowDialog();

    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)

    {

        var settingsWindow = new SettingsWindow

        {

            Owner = this

        };

        if (settingsWindow.ShowDialog() == true)

        {

            _settings = settingsWindow.Settings;

            InitializeApiClient();

            _chatFlowService.UpdateClients(_apiClient, _toolExecutor);

            UpdateAutoSaveTimer();

            AddTerminalMessage(LocalizationManager.Instance.GetString("AyarlarKaydedildi"));

            Notify(LocalizationManager.Instance.GetString("AyarlarKaydedildi"), NotificationSeverity.Success);

        }

    }

    private void UpdateAutoSaveTimer()

    {

        if (_autoSaveTimer != null)

        {

            _autoSaveTimer.Stop();

            _autoSaveTimer.Dispose();

        }

        if (_settings.AutoSaveEnabled)

        {

            _autoSaveTimer = new System.Timers.Timer(_settings.AutoSaveIntervalSeconds * 1000);

            _autoSaveTimer.Elapsed += (s, e) => AutoSaveModifiedFiles();

            _autoSaveTimer.Start();

        }

    }

    private void TxtChatInput_TextChanged(object sender, TextChangedEventArgs e)

    {

        if (sender is TextBox tb)

        {

            try

            {

                tb.VerticalAlignment = VerticalAlignment.Stretch;

                tb.VerticalContentAlignment = VerticalAlignment.Top;

                tb.Padding = new Thickness(10, 8, 10, 8);

                txtChatInputPlaceholder.Visibility = string.IsNullOrEmpty(tb.Text)

                    ? Visibility.Visible

                    : Visibility.Collapsed;

                // @ algılama mantığı

                var caretPos = tb.CaretIndex;

                var text = tb.Text;

                // Caret'in solundaki son @ işaretini bul

                var atIndex = text.LastIndexOf('@', caretPos > 0 ? caretPos - 1 : 0);

                if (atIndex >= 0)

                {

                    // @ ile caret arasında boşluk varsa bu bir mention değildir

                    var query = text.Substring(atIndex + 1, caretPos - atIndex - 1);

                    if (!query.Contains(' ') && !query.Contains('\n'))

                    {

                        ShowAtMentionPopup(query);

                        return;

                    }

                }

                popupAtMention.IsOpen = false;

            }

            catch

            {

            }

        }

    }

    private void TxtChatInput_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (popupAtMention.IsOpen)

        {

            if (e.Key == Key.Down)

            {

                if (lstAtMentions.SelectedIndex < lstAtMentions.Items.Count - 1)

                    lstAtMentions.SelectedIndex++;

                e.Handled = true;

                return;

            }

            else if (e.Key == Key.Up)

            {

                if (lstAtMentions.SelectedIndex > 0)

                    lstAtMentions.SelectedIndex--;

                e.Handled = true;

                return;

            }

            else if (e.Key == Key.Enter || e.Key == Key.Tab)

            {

                if (lstAtMentions.SelectedItem != null)

                {

                    LstAtMentions_SelectionChanged(null, null);

                    e.Handled = true;

                    return;

                }

            }

            else if (e.Key == Key.Escape)

            {

                popupAtMention.IsOpen = false;

                e.Handled = true;

                return;

            }

        }

        if (e.Key == Key.Enter)

        {

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)

            {

                e.Handled = false;

            }

            else

            {

                RunBackground(SendMessageAsync(), "SendMessage");

                e.Handled = true;

            }

        }

    }

    private void ShowAtMentionPopup(string query)

    {

        if (string.IsNullOrEmpty(_selectedFolder)) return;

        try

        {

            var allFiles = Directory.EnumerateFiles(_selectedFolder, "*.*", SearchOption.AllDirectories)

                .Where(f => !IsIgnoredForMention(f))

                .Select(f => Path.GetRelativePath(_selectedFolder, f).Replace('\\', '/'))

                .Where(f => string.IsNullOrEmpty(query) || f.Contains(query, StringComparison.OrdinalIgnoreCase))

                .OrderBy(f => f.Length)

                .Take(15)

                .ToList();

            if (allFiles.Count > 0)

            {

                lstAtMentions.ItemsSource = allFiles;

                lstAtMentions.SelectedIndex = 0;

                popupAtMention.IsOpen = true;

            }

            else

            {

                popupAtMention.IsOpen = false;

            }

        }

        catch

        {

            popupAtMention.IsOpen = false;

        }

    }

    private void LstAtMentions_SelectionChanged(object? sender, SelectionChangedEventArgs? e)

    {

        if (lstAtMentions.SelectedItem is string selectedFile)

        {

            var text = txtChatInput.Text;

            var caretPos = txtChatInput.CaretIndex;

            var atIndex = text.LastIndexOf('@', caretPos > 0 ? caretPos - 1 : 0);

            if (atIndex >= 0)

            {

                var beforeAt = text.Substring(0, atIndex + 1);

                var afterCaret = text.Substring(caretPos);

                txtChatInput.Text = beforeAt + selectedFile + " " + afterCaret;

                txtChatInput.CaretIndex = beforeAt.Length + selectedFile.Length + 1;

                var fullPath = Path.Combine(_selectedFolder ?? "", selectedFile);

                if (!_mentionedFiles.Contains(fullPath))

                {

                    _mentionedFiles.Add(fullPath);
                    _chatPanelViewModel.MentionedFiles.Add(new MentionedFileViewModel
                    {
                        FullPath = fullPath,
                        DisplayName = selectedFile,
                        Icon = GetIconForExtension(Path.GetExtension(selectedFile))
                    });
                }

                RefreshMentionedChipsVisibility();
            }

            popupAtMention.IsOpen = false;

        }

    }

    private void LstAtMentions_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Enter || e.Key == Key.Tab)

        {

            LstAtMentions_SelectionChanged(null, null);

            e.Handled = true;

        }

    }

    private void RefreshMentionedChipsVisibility()
    {
        if (icMentionedChips != null)
            icMentionedChips.Visibility = _chatPanelViewModel.MentionedFiles.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private bool _todoProgressExpanded = true;
    private void TodoProgressToggle_Click(object sender, RoutedEventArgs e)
    {
        _todoProgressExpanded = !_todoProgressExpanded;
        if (txtTodoProgressChevron != null)
            txtTodoProgressChevron.Text = _todoProgressExpanded ? " ▾" : " ▸";
        if (btnTodoCollapse != null)
            btnTodoCollapse.Content = _todoProgressExpanded ? "🔽" : "▶️";
    }

    private static string GetIconForExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return "📄";
        return ext.ToLowerInvariant() switch
        {
            ".cs" or ".java" or ".kt" or ".swift" or ".py" or ".cpp" or ".c" or ".h" => "📝",
            ".xaml" or ".xml" or ".html" or ".htm" or ".json" or ".yaml" or ".yml" or ".js" or ".ts" => "🏷️",
            ".md" or ".txt" => "📃",
            ".sln" or ".csproj" or ".fsproj" or ".vbproj" => "⚙️",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" => "🖼️",
            ".pdf" => "📕",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "🗜️",
            ".mp3" or ".wav" or ".flac" => "🎵",
            ".mp4" or ".avi" or ".mov" or ".webm" => "🎬",
            _ => "📄"
        };
    }

    private void MentionedChipRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fullPath) return;

        _mentionedFiles.Remove(fullPath);
        var vm = _chatPanelViewModel.MentionedFiles.FirstOrDefault(m => m.FullPath == fullPath);
        if (vm != null) _chatPanelViewModel.MentionedFiles.Remove(vm);

        if (txtChatInput != null)
        {
            var name = Path.GetFileName(fullPath);
            var text = txtChatInput.Text;
            var idx = text.IndexOf("@" + name, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var endIdx = idx + ("@" + name).Length;
                if (endIdx < text.Length && text[endIdx] == ' ') endIdx++;
                txtChatInput.Text = text.Remove(idx, endIdx - idx);
                txtChatInput.CaretIndex = Math.Max(0, idx);
            }
        }

        RefreshMentionedChipsVisibility();
    }

    private bool IsIgnoredForMention(string path)

    {

        var ignoredDirs = new[] { ".git", "node_modules", "build", ".dart_tool", "dist", "bin", "obj", ".vs", "packages" };

        var ignoredExtensions = new[] { ".png", ".jpg", ".jpeg", ".pdf", ".dll", ".exe", ".bin", ".zip", ".tar", ".gz" };

        foreach (var dir in ignoredDirs)

        {

            if (path.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||

                path.EndsWith($"{Path.DirectorySeparatorChar}{dir}", StringComparison.OrdinalIgnoreCase))

            {

                return true;

            }

        }

        var ext = Path.GetExtension(path);

        if (!string.IsNullOrEmpty(ext) && ignoredExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))

        {

            return true;

        }

        return false;

    }

    private void BtnAttach_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new OpenFileDialog

        {

            Title = "Dosya Ekle",

            CheckFileExists = true,

            Multiselect = true,

            Filter = "Tüm Dosyalar|*.*|Resimler|*.png;*.jpg;*.jpeg;*.gif|Metin|*.txt;*.md|PDF|*.pdf"

        };

        if (dialog.ShowDialog(this) == true)

        {

            foreach (var file in dialog.FileNames)

            {

                try

                {

                    var bytes = File.ReadAllBytes(file);

                    var b64 = Convert.ToBase64String(bytes);

                    var att = new Attachment

                    {

                        FileName = Path.GetFileName(file),

                        MimeType = GetMimeTypeFromExtension(Path.GetExtension(file)),

                        Base64Content = b64,

                        Size = bytes.LongLength,

                        LocalPath = file

                    };

                    _pendingAttachments.Add(att);

                }

                catch (Exception ex)

                {

                    AddTerminalMessage($"Eklenirken hata: {ex.Message}");

                }

            }

            UpdateAttachmentPreview();

        }

    }

    private void UpdateAttachmentPreview()

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(UpdateAttachmentPreview);

            return;

        }

        if (_pendingAttachments.Count == 0)

        {

            txtAttachmentList.Visibility = Visibility.Collapsed;

            txtAttachmentList.Text = string.Empty;

        }

        else

        {

            txtAttachmentList.Visibility = Visibility.Visible;

            txtAttachmentList.Text = string.Join(", ", _pendingAttachments.Select(a => a.FileName));

        }

    }

    private static string? GetMimeTypeFromExtension(string? ext)

    {

        if (string.IsNullOrEmpty(ext)) return null;

        ext = ext.ToLowerInvariant().TrimStart('.');

        return ext switch

        {

            "png" => "image/png",

            "jpg" => "image/jpeg",

            "jpeg" => "image/jpeg",

            "gif" => "image/gif",

            "txt" => "text/plain",

            "md" => "text/markdown",

            "pdf" => "application/pdf",

            "json" => "application/json",

            _ => null

        };

    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)

    {

        _chatCts?.Cancel();

    }

    private void BtnVoice_PreviewMouseDown(object sender, MouseButtonEventArgs e)

    {

        // SAĞ TIKLAMA: Her zaman ayarları aç

        if (e.RightButton == MouseButtonState.Pressed)

        {

            e.Handled = true;

            OpenVoiceSettings();

            return;

        }

        // SOL TIKLAMA: API ayarı yoksa önce kurulum yap

        if (string.IsNullOrEmpty(_settings?.SttApiKey))

        {

            e.Handled = true;

            var result = OpenVoiceSettings(isFirstTime: true);

            return;

        }

        if (_voiceCommandService == null)

        {

            Notify("Sesli Komut servisi başlatılamadı", NotificationSeverity.Error);

            return;

        }

        try

        {

            _voiceCommandService.StartRecording();

            btnVoice.Content = "🎙️";

            btnVoice.Background = new SolidColorBrush(Color.FromRgb(180, 60, 60));

            AddTerminalMessage(LocalizationManager.Instance.GetString("DinleniyorKonusmayiBitirinceButonuBirakin"));

        }

        catch (Exception ex)

        {

            Notify($"Kayıt başlatılamadı: {ex.Message}", NotificationSeverity.Error);

            AddTerminalMessage($"Sesli Komut hatası: {ex.Message}");

        }

    }

#pragma warning disable VSTHRD100

    private async void BtnVoice_PreviewMouseUp(object sender, MouseButtonEventArgs e)

    {

        // Eğer API yoksa veya servis yoksa çık

        if (_voiceCommandService == null || string.IsNullOrEmpty(_settings?.SttApiKey))

            return;

        try

        {

            btnVoice.Content = "🎤";

            btnVoice.Background = null;

            _voiceCommandService.StopRecording();

            AddTerminalMessage(Localization.Get("⏳ Sesten metne çevriliyor, lütfen bekleyin...", "⏳ Transcribing voice to text, please wait..."));

            var transcribedText = await _voiceCommandService.TranscribeRecordingAsync();

            if (!string.IsNullOrEmpty(transcribedText))

            {

                AddTerminalMessage(Localization.Get($"✅ Çeviri başarılı: {transcribedText}", $"✅ Transcription successful: {transcribedText}"));

                txtChatInput.Text += (txtChatInput.Text.Length > 0 ? " " : "") + transcribedText;

                txtChatInput.Focus();

                txtChatInput.CaretIndex = txtChatInput.Text.Length;

            }

            else

            {

                AddTerminalMessage(Localization.Get("⚠️ Ses algılanamadı veya çeviri boş döndü.", "⚠️ No speech was detected or the transcription was empty."));

            }

        }

        catch (Exception ex)

        {

            Notify(Localization.Get($"Transkripsiyon hatası: {ex.Message}", $"Transcription error: {ex.Message}"), NotificationSeverity.Error);

            AddTerminalMessage(Localization.Get($"Sesli Komut hatası: {ex.Message}", $"Voice command error: {ex.Message}"));

        }

    }

#pragma warning restore VSTHRD100

    /// <summary>

    /// Sesli Komut ayarlar penceresini açar.

    /// isFirstTime=true ise başlık "İlk Kurulum" olarak gösterilir.

    /// Kaydedilirse ayarları diske yazar.

    /// </summary>

    private bool OpenVoiceSettings(bool isFirstTime = false)

    {

        var win = new VoiceSettingsWindow(_settings!)

        {

            Owner = this

        };

        if (isFirstTime)

                win.Title = Localization.Get(LocalizationManager.Instance.GetString("SesliKomutIlkKurulum"), "Voice Command — First Setup");

        var result = win.ShowDialog() == true;

        if (result)

        {

            // Ayarları kaydet

            SettingsWindow.SaveSettings(_settings!);

            AddTerminalMessage(LocalizationManager.Instance.GetString("SesliKomutAyarlariKaydedildiArtikMikrofonaBasarakKonusabilirsiniz"));

            Notify("Sesli Komut kurulumu tamamlandı!", NotificationSeverity.Success);

        }

        return result;

    }

    // ─── RAG Sekme Event Handler'ları ───────────────────────────────────────

    private void BtnRagSettings_Click(object sender, System.Windows.RoutedEventArgs e)

        => OpenRagSettings();

#pragma warning disable VSTHRD100

    private async void BtnRagReindex_Click(object sender, System.Windows.RoutedEventArgs e)

    {

        if (_ragService == null || !_settings.RagEnabled) return;

        btnRagReindex.IsEnabled = false;

        txtRagStatusTitle.Text  = Localization.Get("🔄 Yeniden indeksleniyor...", "🔄 Reindexing...");

        lstRagIndex.Items.Clear();

        try

        {

            await _ragService.IndexProjectAsync(_selectedFolder ?? "");

            UpdateRagTabUI();

            AddTerminalMessage(Localization.Get("✅ RAG yeniden indeksleme tamamlandı.", "✅ RAG reindexing completed."));

        }

        catch (Exception ex)

        {

            AddTerminalMessage(Localization.Get($"❌ RAG indeksleme hatası: {ex.Message}", $"❌ RAG indexing error: {ex.Message}"));

        }

        finally

        {

            btnRagReindex.IsEnabled = _settings.RagEnabled;

        }

    }

#pragma warning restore VSTHRD100

    private void OpenRagSettings()

    {

        var win = new RagSettingsWindow(_settings!) { Owner = this };

        var result = win.ShowDialog() == true;

        if (!result) return;

        SettingsWindow.SaveSettings(_settings!);

        if (win.DisabledRequested)

        {

            UpdateRagTabUI(forceDisabled: true);

            AddTerminalMessage(Localization.Get("⚪ RAG devre dışı bırakıldı.", "⚪ RAG has been disabled."));

            Notify("RAG kapatıldı.", NotificationSeverity.Info);

        }

        else

        {

            // RAG servisini yeni ayarlarla yeniden başlat

            _ = RestartRagServiceAsync();

        }

    }

    private async Task RestartRagServiceAsync()

    {

        try

        {

            txtRagStatusTitle.Text  = "⏳ RAG başlatılıyor...";

            txtRagStatusDetail.Text = LocalizationManager.Instance.GetString("SettingsRagModelModeliIleBaglaniyor").Replace("{_settings.RagModel}", _settings.RagModel);

            lstRagIndex.Items.Clear();

            // RagService'e yeni ayarları geçmemiz için servis yeniden oluşturuluyor

            _ragService?.Dispose();

            _ragService = new mdaiAgent.Services.RagService(_selectedFolder ?? "", _settings);

            await _ragService.InitializeAsync();

            if (!string.IsNullOrEmpty(_selectedFolder))

                await _ragService.IndexProjectAsync(_selectedFolder);

            UpdateRagTabUI();

            AddTerminalMessage(Localization.Get("✅ RAG aktif edildi ve indeksleme tamamlandı!", "✅ RAG enabled and indexing completed!"));

            Notify("RAG aktif! Proje bağlamı AI'a enjekte ediliyor.", NotificationSeverity.Success);

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"❌ RAG başlatma hatası: {ex.Message}");

            UpdateRagTabUI(forceDisabled: true);

        }

    }

    /// <summary>RAG sekmesindeki ikonu, başlığı ve sayacı günceller.</summary>

    internal void UpdateRagTabUI(bool forceDisabled = false)

    {

        bool active = _settings.RagEnabled && !forceDisabled && _ragService != null;

        // Tab ikonu rengi

        txtRagTabIcon.Text       = active ? "🟢" : "⚪";

        txtRagStatusIcon.Text    = active ? "🟢" : "⚪";

        if (active)

        {

            txtRagStatusTitle.Text  = Localization.Get("🧠 RAG Aktif", "🧠 RAG Active");

            txtRagStatusDetail.Text = $"{_settings.RagModel}  |  {_settings.RagBaseUrl}";

            txtRagStatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(

                System.Windows.Media.Color.FromRgb(63, 185, 80));

            btnRagReindex.IsEnabled = true;

            // İndeks özetini listele

            lstRagIndex.Items.Clear();

            lstRagIndex.Items.Add(Localization.Get("📁 İndekslenen dosyalar:", "📁 Indexed files:"));

            var stats = _ragService?.GetIndexStats();

            if (stats != null)

            {

                foreach (var s in stats)

                    lstRagIndex.Items.Add($"   {s}");

                txtRagChunkCount.Text = Localization.Get($"{_ragService?.ChunkCount ?? 0} kod bloğu indekslendi", $"{_ragService?.ChunkCount ?? 0} code blocks indexed");

            }

        }

        else

        {

            txtRagStatusTitle.Text  = Localization.Get("RAG Devre Dışı", "RAG Disabled");

            txtRagStatusDetail.Text = Localization.Get(LocalizationManager.Instance.GetString("AyarlarButonunaTiklayarakAPIGirin"), LocalizationManager.Instance.GetString("AyarlarButonunaTiklayarakAPIGirin"));

            txtRagStatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(

                System.Windows.Media.Color.FromRgb(139, 148, 158));

            txtRagChunkCount.Text   = Localization.Get("0 kod bloğu indekslendi", "0 code blocks indexed");

            btnRagReindex.IsEnabled = false;

        }

    }

    private void VoiceCommandService_RecordingStarted(object? sender, RecordingStartedEventArgs e)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => VoiceCommandService_RecordingStarted(sender, e)));

            return;

        }

        AddTerminalMessage(Localization.Get("🎤 Sesli kayıt başladı", "🎤 Voice recording started"));

        UpdateProcessStatus(Localization.Get("🎤 Konuşun...", "🎤 Speak..."));

    }

    private void VoiceCommandService_RecordingStopped(object? sender, RecordingStoppedEventArgs e)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => VoiceCommandService_RecordingStopped(sender, e)));

            return;

        }

        AddTerminalMessage(Localization.Get("🎤 Sesli kayıt durduruldu", "🎤 Voice recording stopped"));

        UpdateProcessStatus(Localization.Get("🔄 Transkripsiyon işleniyor...", "🔄 Processing transcription..."));

    }

    private void VoiceCommandService_TranscriptionComplete(object? sender, TranscriptionCompleteEventArgs e)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => VoiceCommandService_TranscriptionComplete(sender, e)));

            return;

        }

        AddTerminalMessage($"✅ Transkripsiyon tamamlandı: {e.Text.Substring(0, Math.Min(50, e.Text.Length))}");

        UpdateProcessStatus(Localization.Get("✅ Transkripsiyon tamamlandı", "✅ Transcription completed"));

    }

    private void VoiceCommandService_TranscriptionError(object? sender, TranscriptionErrorEventArgs e)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(new Action(() => VoiceCommandService_TranscriptionError(sender, e)));

            return;

        }

        AddTerminalMessage($"❌ Transkripsiyon hatası: {e.Message}");

        Notify($"Transkripsiyon hatası: {e.Message}", NotificationSeverity.Error);

        UpdateProcessStatus("❌ Transkripsiyon hatası");

    }

    private void VoiceCommandService_VolumeChanged(object? sender, VolumeChangedEventArgs e)

    {

        // Volume level is available here for UI updates (e.g., waveform visualization)

        // For now, just track it for future enhancements

    }

    private void CmbComposerModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || cmbComposerModel == null) return;
        switch (cmbComposerModel.SelectedIndex)
        {
            case 0: // 🤖 Otomatik — router karar verir
                _settings.RouterEnabled = true;
                break;
            case 1: // 🏠 Yerel Model
                _settings.RouterEnabled = false;
                _settings.ActiveProvider = ProviderType.LocalModel;
                break;
            case 2: // ⚡ Api Servisi
                _settings.RouterEnabled = false;
                _settings.ActiveProvider = ProviderType.ApiService;
                break;
            case 3: // ☁️ Bulut Model (varsayılan: Anthropic)
                _settings.RouterEnabled = false;
                _settings.ActiveProvider = ProviderType.Anthropic;
                break;
        }

    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)

    {

        if (_chatCts != null)

        {

            _chatCts.Cancel();

        }

        else

        {

            RunBackground(SendMessageAsync(), "SendMessage");

        }

    }

    private async Task SendMessageAsync(string? explicitText = null, bool addUserBubble = true, List<Attachment>? attachmentsOverride = null)

    {

        _currentTurnActionBars.Clear();

        var message = (explicitText ?? txtChatInput.Text).Trim();

        if (string.IsNullOrEmpty(message))

            return;

        var apiMessage = message;

        var displayMessage = message;

        var contextMode = cmbContextMode?.SelectedIndex switch
        {
            1 => ChatContextMode.ActiveFile,
            2 => ChatContextMode.Selection,
            3 => ChatContextMode.Project,
            4 => ChatContextMode.Rag,
            _ => ChatContextMode.Auto
        };

        if (cmbContextMode != null && cmbContextMode.SelectedIndex > 0)
        {
            if (tcEditor?.SelectedItem is TabItem ctxTab && ctxTab.Tag is TabInfo ctxTabInfo && ctxTab.Content is TextEditor ctxEditor)
            {
                switch (cmbContextMode.SelectedIndex)
                {
                    case 1:
                        if (!string.IsNullOrEmpty(ctxTabInfo.FilePath) && File.Exists(ctxTabInfo.FilePath))
                        {
                            var caretLine = ctxEditor.TextArea.Caret.Line;
                            var contextText = GetCursorWindowContent(ctxEditor.Text, caretLine, windowLines: 80);
                            apiMessage += $"\n\n[BAĞLAM: Aktif Dosya ({Path.GetFileName(ctxTabInfo.FilePath)})]\n{contextText}";
                        }
                        break;
                    case 2:
                        if (!string.IsNullOrEmpty(ctxEditor.SelectedText))
                        {
                            apiMessage += $"\n\n[BAĞLAM: Seçili Kod]\n{ctxEditor.SelectedText}";
                        }
                        break;
                }
            }

            if (cmbContextMode.SelectedIndex == 3)
            {
                apiMessage += "\n\n[BAĞLAM TALİMATI: Cevabı üretirken proje yapısını ve mimarisini dikkate al.]";
            }
        }

        if (_mentionedFiles.Count > 0)

        {

            var mentionsContext = "\n\n=== EKLENEN DOSYALAR (@) ===\n";

            var fileNames = new List<string>();

            foreach (var file in _mentionedFiles.Distinct())

            {

                if (File.Exists(file))

                {

                    var lines = File.ReadAllLines(file);

                    var limit = lines.Length > 2000 ? 2000 : lines.Length; // 2000 satır limiti

                    var relPath = Path.GetRelativePath(_selectedFolder ?? "", file);

                    fileNames.Add(relPath);

                    mentionsContext += $"\n[Dosya: {relPath}]\n";

                    mentionsContext += string.Join("\n", lines.Take(limit));

                    if (lines.Length > 2000) mentionsContext += "\n... (kırpıldı) ...\n";

                }

            }

            apiMessage += mentionsContext;

            _mentionedFiles.Clear();
            _chatPanelViewModel.MentionedFiles.Clear();
            RefreshMentionedChipsVisibility();

        }

        // Clear previous items ve yeni workflow başlat

                _chatPanelViewModel.Clear();

                InitializeAgentWorkflow(apiMessage); // TODO listesi ve timeline başlat

                // Yeni chatView'in görünür olduğundan emin ol (eski chatListView yerine)

                ShowChatView();

                // Gönderim sırasında sohbet görünümünde kal; workflow panelleri sekmelerden açılabilir.

                BtnShowChatHistory_Click(null!, null!);

                if (_chatFlowService.ActiveSession == null)

        {

            _chatFlowService.CreateNewSession(Localization.Get("Yeni Sohbet", "New Chat"));

            RefreshChatSessionsList();

        }

        if (addUserBubble)

        {

            txtChatInput.Clear();

        }

        txtChatInput.IsEnabled = false;

        btnSend.IsEnabled = true;

        btnSend.Content = "Durdur";

        btnStop.Visibility = Visibility.Collapsed;

        SetAssistantActionsEnabled(false);

        _chatCts = new CancellationTokenSource();

        var activeSession = _chatFlowService?.ActiveSession;
        if (activeSession != null && IsUntitledChatName(activeSession.Name))
        {
            GenerateAutoTitle(displayMessage);
        }

        string? currentFilePath = null;

        string? currentFileContent = null;

        if (tcEditor?.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo activeTabInfo && activeTab.Content is TextEditor activeEditor)

        {

            currentFilePath = activeTabInfo.FilePath;

            // VSCode/Cursor tarzı: tüm dosya yerine imleç etrafındaki ±80 satır penceresi

            var caretLine = activeEditor.TextArea.Caret.Line; // 1-indexed

            currentFileContent = GetCursorWindowContent(activeEditor.Text, caretLine, windowLines: 80);

        }

        ListBoxItem? currentStreamedItem = null;

        TextBox? currentStreamTextBox = null;

        string? currentStreamId = null;

        // ── Thinking (Sesli Düşünme) streaming state ──────────────────────

        var thinkBuffer = new System.Text.StringBuilder();

        bool isInThinkBlock = false;

        var tokenCarryover = string.Empty;  // <think> / </think> tagları bölünmüş gelebilir

        ListBoxItem? thinkStreamItem = null;

        TextBox? thinkStreamTextBox = null;

        Expander? thinkExpander = null;

        DateTime thinkStartTime = DateTime.Now;

        DateTime lastScrollTime = DateTime.MinValue;

        // ──────────────────────────────────────────────────────────────────

        Action<string> onTokenReceived = (token) =>

        {

            _ = Dispatcher.BeginInvoke(new Action(() =>

            {

                // Tokenı önceki taşınan parçayla birleştir

                var combined = tokenCarryover + (token ?? string.Empty);

                tokenCarryover = string.Empty;

                // ── <think> ve </think> etiketlerini gerçek zamanlı ayıkla ──

                while (combined.Length > 0)

                {

                    if (!isInThinkBlock)

                    {

                        // <think> açılış etiketi var mı?

                        int openIdx = combined.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);

                        if (openIdx >= 0)

                        {

                            // Etiketten önceki metin normal akışa yaz

                            var before = combined[..openIdx];

                            if (before.Length > 0)

                            {

                                if (currentStreamedItem == null)

                                {

                                    currentStreamId = "STREAM_" + Guid.NewGuid().ToString("N");

                                    currentStreamedItem = AddChatMessage("AI Asistan", "");

                                    currentStreamTextBox = FindFirstTextBox(currentStreamedItem.Content as DependencyObject);

                                    try { currentStreamedItem.Tag = currentStreamId; } catch { }

                                }

                                if (currentStreamTextBox != null)

                                {

                                    currentStreamTextBox.Text += before;

                                    if ((DateTime.Now - lastScrollTime).TotalMilliseconds > 150)

                                    {

                                        lastScrollTime = DateTime.Now;

                                        lstChatHistory.ScrollIntoView(currentStreamedItem);

                                    }

                                }

                            }

                            // Think Expander'ı oluştur (henüz yoksa)

                            if (thinkStreamItem == null)

                            {

                                thinkStartTime = DateTime.Now;

                                thinkBuffer.Clear();

                                isInThinkBlock = true;

                                thinkExpander = new Expander

                                {

                                    Header = "🧠 Düşünüyor...",

                                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),

                                    Background = new SolidColorBrush(Color.FromArgb(20, 167, 139, 250)),

                                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 167, 139, 250)),

                                    BorderThickness = new Thickness(1),

                                    Margin = new Thickness(0, 4, 0, 6),

                                    Padding = new Thickness(8),

                                    IsExpanded = true

                                };

                                var thinkTb = new TextBox

                                {

                                    TextWrapping = TextWrapping.Wrap,

                                    Background = Brushes.Transparent,

                                    BorderThickness = new Thickness(0),

                                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)),

                                    FontSize = 11.5,

                                    IsReadOnly = true,

                                    FontStyle = FontStyles.Italic,

                                    Margin = new Thickness(4)

                                };

                                thinkExpander.Content = thinkTb;

                                thinkStreamTextBox = thinkTb;

                                var expanderItem = new ListBoxItem

                                {

                                    Content = thinkExpander,

                                    Background = Brushes.Transparent,

                                    BorderThickness = new Thickness(0),

                                    Padding = new Thickness(0),

                                    HorizontalContentAlignment = HorizontalAlignment.Stretch,

                                    Focusable = false

                                };

                                thinkStreamItem = expanderItem;

                                lstChatHistory.Items.Add(expanderItem);

                                lstChatHistory.ScrollIntoView(expanderItem);

                            }

                            combined = combined[(openIdx + "<think>".Length)..];

                        }

                        else

                        {

                            // Etiket yok — tamamını normal metin olarak yaz

                            // Ama token "<th" gibi yarım kalmış olabilir, onu taşı

                            if (combined.EndsWith("<") || combined.EndsWith("<t") || combined.EndsWith("<th") ||

                                combined.EndsWith("<thi") || combined.EndsWith("<thin") || combined.EndsWith("<think"))

                            {

                                tokenCarryover = combined;

                                combined = string.Empty;

                                break;

                            }

                            if (currentStreamedItem == null)

                            {

                                currentStreamId = "STREAM_" + Guid.NewGuid().ToString("N");

                                currentStreamedItem = AddChatMessage("AI Asistan", "");

                                currentStreamTextBox = FindFirstTextBox(currentStreamedItem.Content as DependencyObject);

                                try { currentStreamedItem.Tag = currentStreamId; } catch { }

                            }

                            if (currentStreamTextBox != null)

                            {

                                currentStreamTextBox.Text += combined;

                                if ((DateTime.Now - lastScrollTime).TotalMilliseconds > 150)

                                {

                                    lastScrollTime = DateTime.Now;

                                    lstChatHistory.ScrollIntoView(currentStreamedItem);

                                }

                            }

                            combined = string.Empty;

                        }

                    }

                    else

                    {

                        // Think bloğu içindeyiz — </think> kapanışı var mı?

                        int closeIdx = combined.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);

                        if (closeIdx >= 0)

                        {

                            // Think içeriğini yaz

                            var thinkPart = combined[..closeIdx];

                            thinkBuffer.Append(thinkPart);

                            if (thinkStreamTextBox != null) thinkStreamTextBox.Text += thinkPart;

                            // Expander'ı güncelle ve kapat

                            isInThinkBlock = false;

                            var elapsed = (int)(DateTime.Now - thinkStartTime).TotalSeconds;

                            if (thinkExpander != null)

                            {

                                thinkExpander.Header = $"💡 Thought for {elapsed}s  ›";

                                thinkExpander.IsExpanded = false;

                            }

                            thinkStreamItem = null;

                            thinkStreamTextBox = null;

                            combined = combined[(closeIdx + "</think>".Length)..];

                        }

                        else

                        {

                            // Kapanış yok — hepsini think buffer'a yaz, ama yarım tag kontrolü

                            if (combined.EndsWith("<") || combined.EndsWith("</") || combined.EndsWith("</t") ||

                                combined.EndsWith("</th") || combined.EndsWith("</thi") || combined.EndsWith("</thin") || combined.EndsWith("</think"))

                            {

                                var safe = combined[..^combined.Length]; // boş string = hiç yazma

                                // Yarım kapanış için taşı

                                var lastAngle = combined.LastIndexOf('<');

                                safe = combined[..lastAngle];

                                tokenCarryover = combined[lastAngle..];

                                combined = string.Empty;

                                if (safe.Length > 0)

                                {

                                    thinkBuffer.Append(safe);

                                    if (thinkStreamTextBox != null) thinkStreamTextBox.Text += safe;

                                }

                            }

                            else

                            {

                                thinkBuffer.Append(combined);

                                if (thinkStreamTextBox != null) thinkStreamTextBox.Text += combined;

                                combined = string.Empty;

                            }

                            // ⚡ RUNAWAY THINKING KORUMASI & PERFORMANCE THROTTLING

                            if (thinkExpander != null && thinkExpander.IsExpanded && thinkBuffer.Length > 1800)

                            {

                                var elapsedSec = (int)(DateTime.Now - thinkStartTime).TotalSeconds;

                                thinkExpander.Header = $"💡 Düşünce Süreci ({elapsedSec}s - Uzun içerik otomatik daraltıldı 📦)";

                                thinkExpander.IsExpanded = false;

                            }

                            if ((DateTime.Now - lastScrollTime).TotalMilliseconds > 150 && thinkStreamItem != null)

                            {

                                lastScrollTime = DateTime.Now;

                                lstChatHistory.ScrollIntoView(thinkStreamItem);

                            }

                        }

                    }

                }

                // ── Think state machine sonu ──────────────────────────────

            }));

        };

        Action<ChatFlowMessage> onMessageAdded = (msg) =>

        {

            _ = Dispatcher.BeginInvoke(new Action(() =>

            {

                // Diagnostic: log arrival of finalized message into UI thread

                //try { AddTerminalMessage($"[UI] onMessageAdded Sender={msg.Sender} contentLen={(msg.Content?.Length ?? 0)} currentStreamId={currentStreamId}"); } catch { }

                ListBoxItem? locateStreamItem = null;

                if (!string.IsNullOrEmpty(currentStreamId))

                {

                    locateStreamItem = lstChatHistory.Items.Cast<object>().OfType<ListBoxItem>().Reverse().FirstOrDefault(it => (it.Tag as string) == currentStreamId);

                }

                if (locateStreamItem == null && currentStreamedItem != null)

                {

                    locateStreamItem = currentStreamedItem;

                }

                if (msg.Sender == "Sistem")

                {

                    AddChatMessage(msg.Sender, msg.Content ?? string.Empty);

                }

                else if (locateStreamItem != null && msg.Sender == "AI Asistan" && string.IsNullOrEmpty(msg.Content))

                {

                    lstChatHistory.Items.Remove(locateStreamItem);

                    currentStreamedItem = null;

                    currentStreamTextBox = null;

                    currentStreamId = null;

                }

                else if (locateStreamItem != null)

                {

                    try

                    {

                        // Streaming sırasında oluşan canlı think Expander'ı kaldır

                        // Markdown renderer zaten <think> bloğunu nihai item içine Expander olarak işleyecek

                        if (thinkStreamItem != null && lstChatHistory.Items.Contains(thinkStreamItem))

                        {

                            lstChatHistory.Items.Remove(thinkStreamItem);

                            thinkStreamItem = null;

                            thinkStreamTextBox = null;

                            thinkExpander = null;

                        }

                        ReplaceStreamedItemContent(locateStreamItem, msg.Content ?? string.Empty);

                        lstChatHistory.ScrollIntoView(locateStreamItem);

                    }

                    catch

                    {

                        AddChatMessage(msg.Sender, msg.Content ?? string.Empty);

                    }

                    currentStreamedItem = null;

                    currentStreamTextBox = null;

                    currentStreamId = null;

                }

                else

                {

                    AddChatMessage(msg.Sender, msg.Content ?? string.Empty);

                }

                if (msg.ShouldRefreshFileTree && !string.IsNullOrEmpty(_selectedFolder))

                {

                    LoadFileTree(_selectedFolder);

                }

            }));

        };

        var chatFlowService = _chatFlowService;

        if (chatFlowService == null)

        {

            return;

        }

        var session = chatFlowService.ActiveSession;

        if (session == null)

        {

            return;

        }

        var historyEntry = new ExtendedChatMessage

        {

            Role = "user",

            Content = apiMessage,

            Attachments = attachmentsOverride ?? (_pendingAttachments.Count > 0 ? new List<Attachment>(_pendingAttachments) : null)

        };

        session.History.Add(historyEntry);

        chatFlowService.SaveSessions();

        if (addUserBubble)

        {

            var userContext = new ChatMessageUiContext

            {

                DisplayText = displayMessage,

                HistoryIndex = session.History.Count - 1,

                Message = historyEntry,

                Attachments = historyEntry.Attachments

            };

            AddChatMessage("Sen", displayMessage, historyEntry.Attachments, userContext);

        }

        _activeProgressItem = AddProgressStatus("🧠 AI isteği analiz ediyor...");

        try

        {

            var result = await Task.Run(() => chatFlowService.SendMessageAsync(

                apiMessage,

                currentFilePath,

                currentFileContent,

                _selectedFolder,

                _settings?.SystemPrompt ?? string.Empty,

                _settings?.QaAgentEnabled ?? false,

                _settings?.UiAgentEnabled ?? false,

                historyEntry.Attachments,

                _chatCts.Token,

                onTokenReceived,

                onMessageAdded,

                appendUserMessageToHistory: false,

                targetSession: session,

                contextMode: contextMode), _chatCts.Token);

            RemoveActiveProgressStatus();

            if (!result.Success)

                        {

                            Notify($"AI işlemi sırasında hata oluştu: {result.ErrorMessage}", NotificationSeverity.Error);

                            // Hata olsa bile yapılan işlemlerin özetini göster

                            GenerateFinalSummary(result);

                        }

                        else

                        {

                            // Final Summary - işlem başarılı bitti

                            GenerateFinalSummary(result);

                        }

                        RefreshChatSessionsList();

            _pendingAttachments.Clear();

            UpdateAttachmentPreview();

        }

        catch (OperationCanceledException)

        {

            RemoveActiveProgressStatus();

            AddChatMessage("Sistem", "İşlem kullanıcı tarafından iptal edildi.");

            UpdateOperationStep("İşlem iptal edildi.");

        }

        catch (Exception ex)

        {

            RemoveActiveProgressStatus();

            AddChatMessage("AI Asistan", $"Hata: {ex.Message}");

            AddTerminalMessage($"AI Hatası: {ex}");

            Notify($"AI işlemi sırasında hata oluştu: {ex.Message}", NotificationSeverity.Error);

        }

        finally

        {

            try

            {

                txtChatInput.IsEnabled = true;

                btnSend.IsEnabled = true;

                btnSend.Content = LocalizationManager.Instance.GetString("Gonder");

                btnStop.Visibility = Visibility.Collapsed;

                SetAssistantActionsEnabled(true);

                // Kullanıcıya AI'ın bittiğini bildir

                AddTerminalMessage("─────────────────────────────");

                AddTerminalMessage(LocalizationManager.Instance.GetString("AIYanitiTamamlandiSiraSizde"));

                AddTerminalMessage("─────────────────────────────");

                CompleteAllRemainingTodos();

                if (!string.IsNullOrEmpty(_selectedFolder)) LoadFileTree(_selectedFolder);

                // Reset terminal busy state if active

                if (_terminalService != null)

                {

                    _terminalService.ResetBusyState();

                }

                UpdateOperationStep(""); // Clear operation step indicator

                _chatCts?.Dispose();

                _chatCts = null;

            }

            catch { }

        }

    }

    private ListBoxItem AddProgressStatus(string status)

    {

        var statusText = new TextBlock

        {

            Text = status,

            Foreground = new SolidColorBrush(Color.FromRgb(255, 231, 92)),

            FontSize = 12,

            FontStyle = FontStyles.Italic,

            TextWrapping = TextWrapping.Wrap,

            VerticalAlignment = VerticalAlignment.Center

        };

        var statusIcon = new Ellipse

        {

            Width = 8,

            Height = 8,

            Fill = new SolidColorBrush(Color.FromRgb(255, 204, 0)),

            Margin = new Thickness(0, 0, 10, 0),

            VerticalAlignment = VerticalAlignment.Center

        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        content.Children.Add(statusIcon);

        content.Children.Add(statusText);

        var card = new Border

        {

            Background = new SolidColorBrush(Color.FromRgb(47, 43, 20)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(217, 180, 0)),

            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(6),

            Padding = new Thickness(12, 9, 12, 9),

            Margin = new Thickness(8, 2, 36, 5),

            Child = content

        };

        var item = new ListBoxItem

        {

            Content = card,

            HorizontalContentAlignment = HorizontalAlignment.Stretch,

            Padding = new Thickness(0),

            Tag = "ACTIVE_PROGRESS"

        };

        lstChatHistory.Items.Add(item);

        lstChatHistory.ScrollIntoView(item);

        return item;

    }

    private void UpdateActiveProgressStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return;

        if (_activeProgressItem == null || !lstChatHistory.Items.Contains(_activeProgressItem))
        {
            _activeProgressItem = AddProgressStatus(status);
            return;
        }

        if (_activeProgressItem.Content is Border card && card.Child is StackPanel content)
        {
            var textBlock = content.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock != null)
            {
                textBlock.Text = status;
            }
        }

        // Kartın sohbet geçmişinde her zaman EN ALTTASINDA (en son eleman) olmasını sağla
        int count = lstChatHistory.Items.Count;
        if (count > 0 && lstChatHistory.Items[count - 1] != _activeProgressItem)
        {
            lstChatHistory.Items.Remove(_activeProgressItem);
            lstChatHistory.Items.Add(_activeProgressItem);
        }

        lstChatHistory.ScrollIntoView(_activeProgressItem);
    }

    private void RemoveActiveProgressStatus()
    {
        if (_activeProgressItem != null && lstChatHistory.Items.Contains(_activeProgressItem))
        {
            lstChatHistory.Items.Remove(_activeProgressItem);
        }

        // Temizlik garantisi: Listedeki tüm ACTIVE_PROGRESS etiketli artıkları kaldır
        var legacyItems = lstChatHistory.Items.Cast<object>()
            .OfType<ListBoxItem>()
            .Where(it => (it.Tag as string) == "ACTIVE_PROGRESS")
            .ToList();

        foreach (var item in legacyItems)
        {
            lstChatHistory.Items.Remove(item);
        }

        _activeProgressItem = null;
    }

    private static string FormatActivityLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "○ İşlem bekliyor";
        }

        var cleaned = message.Trim();
        cleaned = Regex.Replace(cleaned, @"^[\W_]+", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "○ İşlem bekliyor";
        }

        var lower = cleaned.ToLowerInvariant();

        if (lower.Contains("error") || lower.Contains("hata") || lower.Contains("failed") || lower.Contains("başarısız") || lower.Contains("fail") || lower.Contains("exception"))
        {
            return "❌ İşlem sırasında hata oluştu";
        }

        if (lower.Contains("success") || lower.Contains("tamamlandı") || lower.Contains("completed") || lower.Contains("done") || lower.Contains("başarılı"))
        {
            return "✓ İşlem tamamlandı";
        }

        if (lower.Contains("working") || lower.Contains("çalışıyor") || lower.Contains("running") || lower.Contains("başlıyor") || lower.Contains("başladı") || lower.Contains("analyzing") || lower.Contains("analiz") || lower.Contains("processing") || lower.Contains("işleniyor"))
        {
            return "● İşlem devam ediyor";
        }

        if (lower.Contains("waiting") || lower.Contains("bekliyor") || lower.Contains("pending") || lower.Contains("sırada"))
        {
            return "○ İşlem bekliyor";
        }

        if (lower.Contains("readfile:") || lower.Contains("read file:") || lower.Contains("readfile") || lower.Contains("read file"))
        {
            var filePart = cleaned.Contains(':') ? cleaned.Split(':', 2)[1].Trim() : cleaned;
            var normalized = Regex.Replace(filePart, @"^(?:readfile|read file)\s*[:\-]?\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "✓ Dosya incelendi" : $"✓ {normalized} incelendi";
        }

        if (lower.Contains("writefile:") || lower.Contains("write file:") || lower.Contains("writefile") || lower.Contains("write file"))
        {
            var filePart = cleaned.Contains(':') ? cleaned.Split(':', 2)[1].Trim() : cleaned;
            var normalized = Regex.Replace(filePart, @"^(?:writefile|write file)\s*[:\-]?\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "✓ Dosya güncellendi" : $"✓ {normalized} güncellendi";
        }

        if (lower.Contains("search") || lower.Contains("ara") || lower.Contains("grep"))
        {
            return "✓ Arama tamamlandı";
        }

        if (lower.Contains("build") || lower.Contains("compile") || lower.Contains("derleme") || lower.Contains("compile") || lower.Contains("dotnet build"))
        {
            return "✓ Derleme tamamlandı";
        }

        if (lower.Contains("test") || lower.Contains("testler") || lower.Contains("pytest") || lower.Contains("dotnet test"))
        {
            return "✓ Testler tamamlandı";
        }

        if (lower.Contains("install") || lower.Contains("yüklen") || lower.Contains("download") || lower.Contains("indiril"))
        {
            return "✓ Kurulum tamamlandı";
        }

        return "✓ " + cleaned;
    }

    private ListBoxItem AddChatMessage(string sender, string? message, List<Attachment>? attachments = null, ChatMessageUiContext? messageContext = null)

    {

        if (!Dispatcher.CheckAccess())

        {

            return Dispatcher.Invoke(() => AddChatMessage(sender, message, attachments));

        }

        message ??= string.Empty;

        bool isUser = sender == "Sen";

        bool isSystem = sender == "Sistem";

        if (isSystem)
        {
            var stepPrefixes = new[] { "🔧", "⚙️", "🔍", "✅", "🎨", "⚠️", "📖", "🔄", "❌", "❓" };
            var trimmed = (message ?? string.Empty).TrimStart();
            if (stepPrefixes.Any(p => trimmed.StartsWith(p)))
            {
                try
                {
                    var lastAiItem = lstChatHistory.Items.Cast<object>().OfType<ListBoxItem>().Reverse().FirstOrDefault(it => (it.Tag as string) == "AI Asistan");
                    if (lastAiItem != null)
                    {
                        Panel? panel = null;
                        if (lastAiItem.Content is Panel p) panel = p;
                        else if (lastAiItem.Content is ContentControl cc && cc.Content is Panel p2) panel = p2;

                        if (panel != null)
                        {
                            var stack = panel.Children.OfType<StackPanel>().FirstOrDefault();
                            if (stack != null)
                            {
                                // Activity Card (🧠 Working kartı) ara veya oluştur
                                var expander = stack.Children.OfType<Expander>().FirstOrDefault(e => (e.Tag as string) == "ActivityCard");
                                StackPanel activityStack;

                                if (expander == null)
                                {
                                    activityStack = new StackPanel();
                                    expander = new Expander
                                    {
                                        Tag = "ActivityCard",
                                        Header = new TextBlock
                                        {
                                            Text = "🧠 Working (1 işlem)",
                                            Foreground = new SolidColorBrush(Color.FromRgb(160, 200, 240)),
                                            FontSize = 11.5,
                                            FontWeight = FontWeights.SemiBold
                                        },
                                        Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                                        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                                        BorderThickness = new Thickness(1),
                                        Margin = new Thickness(0, 6, 0, 6),
                                        Padding = new Thickness(4, 2, 4, 2),
                                        IsExpanded = true,
                                        Content = activityStack
                                    };
                                    stack.Children.Insert(0, expander); // Aktivite kartını AI balonunun üstüne yerleştir
                                }
                                else
                                {
                                    activityStack = expander.Content as StackPanel ?? new StackPanel();
                                }

                                var statusBlock = new TextBlock
                                {
                                    Text = FormatActivityLine(message),
                                    TextWrapping = TextWrapping.Wrap,
                                    Foreground = new SolidColorBrush(Color.FromArgb(220, 200, 215, 230)),
                                    FontSize = 11.5,
                                    Margin = new Thickness(4, 2, 4, 2)
                                };
                                activityStack.Children.Add(statusBlock);

                                if (expander.Header is TextBlock headerTb)
                                {
                                    headerTb.Text = $"🧠 Working ({activityStack.Children.Count} işlem)";
                                }

                                lstChatHistory.ScrollIntoView(lastAiItem);
                                return lastAiItem;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        var outerPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 7, 0, 7) };

        if (isUser)

        {

            var messageBorder = new Border

            {

                CornerRadius = new CornerRadius(10, 10, 3, 10),

                Padding = new Thickness(14, 9, 14, 9),

                MaxWidth = 480,

                Margin = new Thickness(25, 0, 4, 0),

                HorizontalAlignment = HorizontalAlignment.Right,

                Background = TryFindResource("ChatUserBubbleBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 120, 210)),

                BorderBrush = TryFindResource("ChatUserBubbleBorderBrush") as Brush,

                BorderThickness = new Thickness(1)

            };

            var innerGrid = new Grid();

            var contentStack = new StackPanel

            {

                Orientation = Orientation.Vertical,

                Margin = new Thickness(0, 0, 36, 0)

            };

            var contentElement = CreateChatMessageContent(message);

            if (contentElement is FrameworkElement fe)

            {

                fe.HorizontalAlignment = HorizontalAlignment.Stretch;

            }

            contentStack.Children.Add(contentElement);

            innerGrid.Children.Add(contentStack);

            if (messageContext != null)

            {

                var editButton = new Button

                {

                    Content = "✏️",

                    Padding = new Thickness(4, 2, 4, 2),

                    FontSize = 11,

                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),

                    Foreground = Brushes.White,

                    BorderThickness = new Thickness(0),

                    Tag = messageContext,

                    Cursor = Cursors.Hand,

                    Height = 22,

                    Width = 22,

                    HorizontalAlignment = HorizontalAlignment.Right,

                    VerticalAlignment = VerticalAlignment.Top,

                    Margin = new Thickness(0, 0, 0, 0),

                    HorizontalContentAlignment = HorizontalAlignment.Center,

                    VerticalContentAlignment = VerticalAlignment.Center,

                    ToolTip = "Mesajı düzenle (geçmişi bu noktadan itibaren sıfırlar)"

                };

                editButton.Click += EditUserMessage_Click;

                innerGrid.Children.Add(editButton);

            }

            messageBorder.Child = innerGrid;

            DockPanel.SetDock(messageBorder, Dock.Right);

            outerPanel.Children.Add(messageBorder);

        }

        else

        {

            var assistantBorder = new Border

            {

                Background = Brushes.Transparent,

                BorderThickness = new Thickness(0),

                Padding = new Thickness(0),

                Margin = new Thickness(0, 0, 10, 0),

                HorizontalAlignment = HorizontalAlignment.Left

            };

            var contentStack = new StackPanel

            {

                Orientation = Orientation.Vertical,

                Margin = new Thickness(10, 5, 16, 5),

                HorizontalAlignment = HorizontalAlignment.Left

            };

            if (message == "Düşünüyorum...")

            {

                var textBox = CreateSelectableTextBox("Düşünüyorum...");

                textBox.Foreground = Brushes.Gray;

                textBox.FontStyle = FontStyles.Italic;

                textBox.FontSize = 11;

                contentStack.Children.Add(textBox);

            }

            else if (string.IsNullOrEmpty(message))

            {

                var textBox = CreateSelectableTextBox(string.Empty);

                textBox.Foreground = TryFindResource("ChatMessageForeground") as Brush ?? Brushes.White;

                contentStack.Children.Add(textBox);

            }

            else

            {

                var contentElement = CreateChatMessageContent(message);

                if (contentElement is FrameworkElement fe)

                {

                    fe.HorizontalAlignment = HorizontalAlignment.Left;

                }

                contentStack.Children.Add(contentElement);

            }

            var actionBar = CreateAssistantMessageActions(message);

            contentStack.Children.Add(actionBar);

            assistantBorder.MouseEnter += (_, _) => actionBar.Opacity = 1;

            assistantBorder.MouseLeave += (_, _) => actionBar.Opacity = 0.42;

            assistantBorder.Child = contentStack;

            outerPanel.Children.Add(assistantBorder);

        }

        StackPanel? targetStack = null;

        targetStack = outerPanel.Children.OfType<Border>().Select(b => b.Child).OfType<StackPanel>().FirstOrDefault()

                      ?? outerPanel.Children.OfType<Border>().Select(b => b.Child).OfType<Grid>().SelectMany(g => g.Children.OfType<StackPanel>()).FirstOrDefault()

                      ?? outerPanel.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<StackPanel>()).FirstOrDefault();

        if (attachments != null && attachments.Count > 0 && targetStack != null)

        {

            var attachPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            foreach (var att in attachments)

            {

                var btn = new Button

                {

                    Content = $"📎 {att.FileName}",

                    Margin = new Thickness(2, 0, 2, 0),

                    Padding = new Thickness(6, 4, 6, 4),

                    Tag = att

                };

                btn.Click += (s, e) =>

                {

                    try

                    {

                        var a = ((Button)s).Tag as Attachment;

                        if (a == null) return;

                        string targetPath;

                        if (!string.IsNullOrEmpty(a.LocalPath) && File.Exists(a.LocalPath))

                        {

                            targetPath = a.LocalPath;

                        }

                        else if (!string.IsNullOrEmpty(a.Base64Content))

                        {

                            var tmp = Path.Combine(Path.GetTempPath(), a.FileName);

                            File.WriteAllBytes(tmp, Convert.FromBase64String(a.Base64Content));

                            targetPath = tmp;

                        }

                        else

                        {

                            AddTerminalMessage(LocalizationManager.Instance.GetString("EkDosyaAcmaBasarisizIcerikYok"));

                            return;

                        }

                        var psi = new ProcessStartInfo(targetPath) { UseShellExecute = true };

                        Process.Start(psi);

                    }

                    catch (Exception ex)

                    {

                        AddTerminalMessage($"Ek dosya açılırken hata: {ex.Message}");

                        MessageBox.Show(LocalizationManager.Instance.GetString("EkDosyaAcilamadiExMessage").Replace("{ex.Message}", ex.Message), LocalizationManager.Instance.GetString("Hata"), MessageBoxButton.OK, MessageBoxImage.Error);

                    }

                };

                attachPanel.Children.Add(btn);

            }

            targetStack.Children.Add(attachPanel);

        }

        var item = new ListBoxItem

        {

            Content = outerPanel,

            HorizontalContentAlignment = HorizontalAlignment.Stretch,

            Padding = new Thickness(2)

        };

        item.Tag = messageContext != null ? (object)messageContext : sender;

        lstChatHistory.Items.Add(item);

        lstChatHistory.ScrollIntoView(item);

        return item;

    }

    private static FrameworkElement CreateChatMessageContent(string? message)

    {

        if (string.IsNullOrEmpty(message))

            return CreateSelectableTextBox(string.Empty);

        return CreateMarkdownRichTextBox(message);

    }

    private static RichTextBox CreateMarkdownRichTextBox(string? message)

    {

        var text = message ?? string.Empty;

        var foregroundBrush = (Application.Current?.TryFindResource("ChatMessageForeground") as Brush) ?? Brushes.White;

        var richTextBox = new RichTextBox

        {

            Background = Brushes.Transparent,

            BorderThickness = new Thickness(0),

            IsReadOnly = true,

            IsDocumentEnabled = true,

            IsReadOnlyCaretVisible = true,

            IsTabStop = true,

            Focusable = true,

            Foreground = foregroundBrush,

                FontFamily = (Application.Current?.TryFindResource("ChatTextFont") as FontFamily) ?? new FontFamily("Segoe UI"),

                FontSize = 14,

            VerticalAlignment = VerticalAlignment.Top,

            HorizontalAlignment = HorizontalAlignment.Stretch,

            Margin = new Thickness(0),

            Padding = new Thickness(0),

            Document = ConvertMarkdownToFlowDocument(text, foregroundBrush)

        };

        if (richTextBox.Document != null)

        {

            richTextBox.Document.Foreground = foregroundBrush;

            richTextBox.Document.PagePadding = new Thickness(0);

        }

        richTextBox.ContextMenu = CreateCopyContextMenu(richTextBox);

        return richTextBox;

    }

    private static TextBox CreateSelectableTextBox(string text)

    {

        var foregroundBrush = (Application.Current?.TryFindResource("ChatMessageForeground") as Brush) ?? Brushes.White;

        var textBox = new TextBox

        {

            Text = text ?? string.Empty,

            TextWrapping = TextWrapping.Wrap,

            Background = Brushes.Transparent,

            Foreground = foregroundBrush,

            BorderThickness = new Thickness(0),

            IsReadOnly = true,

            IsReadOnlyCaretVisible = true,

            IsTabStop = true,

            Focusable = true,

            FontFamily = (Application.Current?.TryFindResource("ChatTextFont") as FontFamily) ?? new FontFamily("Segoe UI"),

            FontSize = 14,

            VerticalAlignment = VerticalAlignment.Top,

            HorizontalAlignment = HorizontalAlignment.Stretch,

            Margin = new Thickness(0),

            Padding = new Thickness(0)

        };

        textBox.ContextMenu = CreateCopyContextMenu(textBox);

        return textBox;

    }

    private static FlowDocument ConvertMarkdownToFlowDocument(string? markdown, Brush foregroundBrush)

    {

        var document = new FlowDocument { Foreground = foregroundBrush };

        if (string.IsNullOrWhiteSpace(markdown))

            return document;

        var lines = markdown.Replace("<think>", "\n<think>\n").Replace("</think>", "\n</think>\n").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool inCodeBlock = false;

        string? codeBlockLanguage = null;

        System.Text.StringBuilder? codeBlockContent = null;

        bool inThinkBlock = false;

        System.Text.StringBuilder? thinkBlockContent = null;

        bool previousLineWasBlank = false;

        List<List> currentLists = new();

        void FlushLists()

        {

            foreach (var list in currentLists)

            {

                document.Blocks.Add(list);

            }

            currentLists.Clear();

        }

        foreach (var rawLine in lines)

        {

            var line = rawLine.TrimEnd();

            var trimmedLine = line.Trim();

            if (trimmedLine == "<think>")

            {

                if (!inThinkBlock)

                {

                    inThinkBlock = true;

                    thinkBlockContent = new System.Text.StringBuilder();

                }

                continue;

            }

            if (trimmedLine == "</think>")

            {

                if (inThinkBlock)

                {

                    inThinkBlock = false;

                    FlushLists();

                    document.Blocks.Add(CreateThoughtProcessBlock(thinkBlockContent?.ToString().TrimEnd() ?? string.Empty));

                }

                continue;

            }

            if (inThinkBlock)

            {

                thinkBlockContent?.AppendLine(line);

                continue;

            }

            if (line.StartsWith("```"))

            {

                if (!inCodeBlock)

                {

                    inCodeBlock = true;

                    codeBlockLanguage = line.Length > 3 ? line[3..].Trim() : null;

                    codeBlockContent = new System.Text.StringBuilder();

                }

                else

                {

                    inCodeBlock = false;

                    document.Blocks.Add(CreateCodeBlockParagraph(codeBlockContent?.ToString().TrimEnd() ?? string.Empty, codeBlockLanguage));

                    codeBlockLanguage = null;

                }

                continue;

            }

            if (inCodeBlock)

            {

                codeBlockContent?.AppendLine(line);

                continue;

            }

            if (string.IsNullOrWhiteSpace(line))

            {

                if (previousLineWasBlank)

                {

                    continue;

                }

                FlushLists();

                document.Blocks.Add(new Paragraph(new Run(" ")) { Margin = new Thickness(0, 3, 0, 3) });

                previousLineWasBlank = true;

                continue;

            }

            previousLineWasBlank = false;

            if (line.StartsWith("#"))

            {

                FlushLists();

                int headerLevel = line.TakeWhile(c => c == '#').Count();

                string headerText = line.Substring(headerLevel).Trim();

                var paragraph = new Paragraph(new Run(headerText))

                {

                    FontWeight = FontWeights.SemiBold,

                    Foreground = foregroundBrush,

                    Margin = new Thickness(0, 8, 0, 4)

                };

                switch (headerLevel)

                {

                    case 1:

                        paragraph.FontSize = 18;

                        break;

                    case 2:

                        paragraph.FontSize = 16;

                        break;

                    default:

                        paragraph.FontSize = 14;

                        break;

                }

                document.Blocks.Add(paragraph);

                continue;

            }

            if (line.StartsWith("Başlık:", StringComparison.OrdinalIgnoreCase) ||

                line.StartsWith("Açıklama:", StringComparison.OrdinalIgnoreCase))

            {

                FlushLists();

                var separatorIndex = line.IndexOf(':');

                var label = line[..(separatorIndex + 1)];

                var value = line[(separatorIndex + 1)..].Trim();

                var labeledParagraph = new Paragraph

                {

                    Foreground = foregroundBrush,

                    Margin = new Thickness(0, 4, 0, 3)

                };

                labeledParagraph.Inlines.Add(new Bold(new Run(label)

                {

                    Foreground = Application.Current?.TryFindResource("ChatAssistantAccentBrush") as Brush ?? foregroundBrush

                }));

                if (!string.IsNullOrEmpty(value))

                {

                    labeledParagraph.Inlines.Add(new Run(" " + value));

                }

                document.Blocks.Add(labeledParagraph);

                continue;

            }

            var unorderedMatch = Regex.Match(line, "^(?:[-*+] )(.+)$");

            var orderedMatch = Regex.Match(line, "^(?:\\d+\\.) (.+)$");

            if (unorderedMatch.Success || orderedMatch.Success)

            {

                var list = new List { MarkerStyle = unorderedMatch.Success ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal };

                var listParagraph = new Paragraph { Foreground = foregroundBrush };

                foreach (var inline in CreateInlineElements(unorderedMatch.Success ? unorderedMatch.Groups[1].Value : orderedMatch.Groups[1].Value))

                {

                    listParagraph.Inlines.Add(inline);

                }

                list.ListItems.Add(new ListItem(listParagraph));

                currentLists.Add(list);

                continue;

            }

            if (currentLists.Count > 0)

            {

                var prev = currentLists.Last();

                if (unorderedMatch.Success)

                {

                    var newParagraph = new Paragraph { Foreground = foregroundBrush };

                    foreach (var inline in CreateInlineElements(unorderedMatch.Groups[1].Value))

                    {

                        newParagraph.Inlines.Add(inline);

                    }

                    prev.ListItems.Add(new ListItem(newParagraph));

                    continue;

                }

                if (orderedMatch.Success)

                {

                    var newParagraph = new Paragraph { Foreground = foregroundBrush };

                    foreach (var inline in CreateInlineElements(orderedMatch.Groups[1].Value))

                    {

                        newParagraph.Inlines.Add(inline);

                    }

                    prev.ListItems.Add(new ListItem(newParagraph));

                    continue;

                }

                FlushLists();

            }

            var bodyParagraph = new Paragraph { Foreground = foregroundBrush };

            foreach (var inline in CreateInlineElements(line))

            {

                bodyParagraph.Inlines.Add(inline);

            }

            bodyParagraph.Margin = new Thickness(0, 3, 0, 3);

            document.Blocks.Add(bodyParagraph);

        }

        FlushLists();

        if (inCodeBlock)

        {

            document.Blocks.Add(CreateCodeBlockParagraph(codeBlockContent?.ToString().TrimEnd() ?? string.Empty, codeBlockLanguage));

        }

        if (inThinkBlock)

        {

            document.Blocks.Add(CreateThoughtProcessBlock(thinkBlockContent?.ToString().TrimEnd() ?? string.Empty));

        }

        return document;

    }

    private static BlockUIContainer CreateThoughtProcessBlock(string content)

    {

        var expander = new Expander

        {

            Header = "💡 Düşünce Süreci (Thought Process)",

            Foreground = Brushes.Gray,

            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),

            Margin = new Thickness(0, 4, 0, 8),

            Padding = new Thickness(6),

            BorderThickness = new Thickness(1),

            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),

            IsExpanded = false

        };

        var tb = new TextBlock

        {

            Text = content,

            TextWrapping = TextWrapping.Wrap,

            Foreground = Brushes.DarkGray,

            FontSize = 11.5,

            Margin = new Thickness(4)

        };

        expander.Content = tb;

        return new BlockUIContainer(expander);

    }

    private static BlockUIContainer CreateCodeBlockParagraph(string content, string? language)

    {

        var codeEditor = new TextEditor

        {

            Text = content,

            IsReadOnly = true,

            ShowLineNumbers = true,

            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,

            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

            Background = Brushes.Transparent,

            Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 238)),

            BorderThickness = new Thickness(0),

            FontFamily = (Application.Current?.TryFindResource("ChatCodeFont") as FontFamily) ?? new FontFamily("Consolas"),

            FontSize = 12.5,

            Padding = new Thickness(8, 4, 8, 10),

            MinHeight = 44,

            MaxHeight = 420,

            Height = Math.Min(420, Math.Max(44, (content.Count(c => c == '\n') + 1) * 18 + 28))

        };

        var codeExtension = ResolveCodeExtension(language);

        if (codeExtension != null)

        {

            codeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(codeExtension);

        }

        var copyButton = new Button

        {

            Content = "Kopyala",

            Padding = new Thickness(8, 3, 8, 3),

            Margin = new Thickness(0, 0, 8, 0),

            Background = Brushes.Transparent,

            Foreground = new SolidColorBrush(Color.FromRgb(166, 182, 202)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 70, 88)),

            BorderThickness = new Thickness(1),

            Cursor = Cursors.Hand,

            ToolTip = "Kod bloğunu kopyala"

        };

        copyButton.Click += (_, _) => Clipboard.SetText(content);

        var openButton = new Button

        {

            Content = LocalizationManager.Instance.GetString("EditordeAc"),

            Padding = new Thickness(8, 3, 8, 3),

            Margin = new Thickness(0, 0, 8, 0),

            Background = Brushes.Transparent,

            Foreground = new SolidColorBrush(Color.FromRgb(166, 182, 202)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 70, 88)),

            BorderThickness = new Thickness(1),

            Cursor = Cursors.Hand,

            ToolTip = "Kodu yeni bir taslak sekmesinde aç"

        };

        openButton.Click += (_, _) =>

        {

            if (Application.Current?.MainWindow is MainWindow mainWindow)

            {

                mainWindow.OpenCodeSnippetInEditor(content, language);

            }

        };

        var applyButton = new Button
        {
            Content = "Uygula ↓",
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 210, 150)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(40, 120, 80)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "Bu kodu aktif dosyaya uygula (Diff önizlemesi ile)"
        };

        applyButton.Click += (_, _) =>
        {
            if (Application.Current?.MainWindow is MainWindow mainWindow)
            {
                _ = mainWindow.ApplyCodeSnippetToActiveFileAsync(content);
            }
        };

        var header = new Grid { Margin = new Thickness(12, 8, 0, 0) };

        header.ColumnDefinitions.Add(new ColumnDefinition());

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var languageText = new TextBlock

        {

            Text = string.IsNullOrWhiteSpace(language) ? "CODE" : language.ToUpperInvariant(),

            Foreground = new SolidColorBrush(Color.FromRgb(126, 160, 194)),

            FontSize = 10,

            FontWeight = FontWeights.SemiBold,

            VerticalAlignment = VerticalAlignment.Center

        };

        Grid.SetColumn(applyButton, 1);

        Grid.SetColumn(copyButton, 2);

        Grid.SetColumn(openButton, 3);

        header.Children.Add(languageText);

        header.Children.Add(applyButton);

        header.Children.Add(copyButton);

        header.Children.Add(openButton);

        var contentPanel = new StackPanel();

        contentPanel.Children.Add(header);

        contentPanel.Children.Add(codeEditor);

        var codeBorder = new Border

        {

            Background = new SolidColorBrush(Color.FromRgb(11, 16, 22)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 50, 65)),

            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(8),

            Margin = new Thickness(0, 8, 0, 10),

            Child = contentPanel

        };

        return new BlockUIContainer(codeBorder);

    }

    public async Task ApplyCodeSnippetToActiveFileAsync(string codeSnippet)
    {
        if (tcEditor?.SelectedItem is TabItem activeTab && activeTab.Tag is TabInfo activeTabInfo && activeTab.Content is TextEditor activeEditor && !string.IsNullOrEmpty(activeTabInfo.FilePath) && File.Exists(activeTabInfo.FilePath))
        {
            string filePath = activeTabInfo.FilePath;
            string oldContent = File.ReadAllText(filePath);
            bool approved = await ShowDiffWindowConfirmAsync(filePath, oldContent, codeSnippet);
            if (approved)
            {
                File.WriteAllText(filePath, codeSnippet);
                activeEditor.Text = codeSnippet;
                Notify("Değişiklik aktif dosyaya uygulandı.", NotificationSeverity.Info);
            }
        }
        else
        {
            Notify("Aktif açık bir dosya bulunamadı.", NotificationSeverity.Warning);
        }
    }

    private void GenerateAutoTitle(string userMessage)
    {
        var chatFlowService = _chatFlowService;
        var activeSession = chatFlowService?.ActiveSession;
        if (chatFlowService == null || activeSession == null || string.IsNullOrWhiteSpace(userMessage)) return;

        var normalizedMessage = userMessage.Trim();
        var commandParts = normalizedMessage.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (commandParts.Length > 1 && commandParts[0].StartsWith('/'))
        {
            normalizedMessage = commandParts[1];
        }

        var words = normalizedMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(" ", words.Take(5));
        if (title.Length > 35) title = title.Substring(0, 35) + "...";

        activeSession.Name = title;
        if (txtCurrentChatName != null)
        {
            txtCurrentChatName.Text = title;
        }

        chatFlowService.SaveSessions();
        RefreshChatSessionsList();
    }

    private static bool IsUntitledChatName(string? name)
    {
        var defaultName = Localization.Get("Yeni Sohbet", "New Chat");
        return string.IsNullOrWhiteSpace(name)
            || name.Equals(defaultName, StringComparison.OrdinalIgnoreCase)
            || name.Equals("Yeni Sohbet", StringComparison.OrdinalIgnoreCase)
            || name.Equals("New Chat", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenCodeSnippetInEditor(string content, string? language)

    {

        var extension = ResolveCodeExtension(language) ?? ".txt";

        var displayName = $"AI Snippet ({(string.IsNullOrWhiteSpace(language) ? "code" : language)})";

        var tabItem = new TabItem();

        var tabInfo = new TabInfo

        {

            FilePath = string.Empty,

            DisplayName = displayName,

            IsUntitled = true,

            IsModified = true

        };

        tabItem.Tag = tabInfo;

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

        var iconText = new TextBlock

        {

            Text = "✦",

            VerticalAlignment = VerticalAlignment.Center,

            Margin = new Thickness(0, 0, 6, 0),

            Foreground = new SolidColorBrush(Color.FromRgb(105, 183, 255))

        };

        var headerText = new TextBlock

        {

            Text = displayName + " *",

            VerticalAlignment = VerticalAlignment.Center,

            Foreground = Brushes.Orange

        };

        var closeButton = new Button

        {

            Content = "×",

            Margin = new Thickness(6, 0, 0, 0),

            Padding = new Thickness(4, 0, 4, 0),

            Background = Brushes.Transparent,

            Foreground = Brushes.Gray,

            BorderThickness = new Thickness(0),

            Cursor = Cursors.Hand,

            Tag = tabItem

        };

        closeButton.Click += BtnCloseTab_Click;

        headerStack.Children.Add(iconText);

        headerStack.Children.Add(headerText);

        headerStack.Children.Add(closeButton);

        tabItem.Header = headerStack;

        var editor = new TextEditor

        {

            Text = content,

            FontFamily = (Application.Current?.TryFindResource("ChatCodeFont") as FontFamily) ?? new FontFamily("Consolas"),

            FontSize = 14,

            ShowLineNumbers = true,

            Background = (SolidColorBrush)FindResource("SurfaceBrush"),

            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),

            LineNumbersForeground = (SolidColorBrush)FindResource("TextSecondaryBrush"),

            WordWrap = false,

            Tag = tabItem

        };

        editor.Options.ConvertTabsToSpaces = false;

        editor.Options.EnableHyperlinks = false;

        editor.Options.HighlightCurrentLine = true;

        editor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(extension);

        editor.TextChanged += (_, _) => tabInfo.IsModified = true;

        tabItem.Content = editor;

        tcEditor.Items.Add(tabItem);

        tcEditor.SelectedItem = tabItem;

        _currentOpenFile = null;

        tbCurrentFile.Text = displayName + " *";

        AddTerminalMessage($"Kod taslak sekmesinde açıldı: {displayName}");

    }

    private static string? ResolveCodeExtension(string? language)

    {

        if (string.IsNullOrWhiteSpace(language)) return null;

        return language.Trim().ToLowerInvariant() switch

        {

            "c#" or "csharp" or "cs" => ".cs",

            "javascript" or "js" or "jsx" or "typescript" or "ts" or "tsx" => ".js",

            "python" or "py" => ".py",

            "html" or "htm" => ".html",

            "css" or "scss" => ".css",

            "dart" => ".dart",

            "json" => ".json",

            "xml" or "xaml" => ".xml",

            "yaml" or "yml" => ".yaml",

            "shell" or "bash" or "sh" => ".sh",

            _ => language.StartsWith('.') ? language : "." + language

        };

    }

    private StackPanel CreateAssistantMessageActions(string message)

    {

        var actions = new StackPanel

        {

            Orientation = Orientation.Horizontal,

            HorizontalAlignment = HorizontalAlignment.Left,

            Margin = new Thickness(0, 1, 0, 0),

            Opacity = 0.42,

            Tag = message // İçeriği Tag'da tutuyoruz, stream güncelledikçe değişecek

        };

        _assistantActionBars.Add(actions);

        _currentTurnActionBars.Add(actions);

        // AI çalışırken veya boşken tüm action barlar gizlenir

        if (_chatCts != null || string.IsNullOrWhiteSpace(message) || message == "Düşünüyorum...")

        {

            actions.IsEnabled = false;

            actions.Visibility = Visibility.Collapsed;

        }

        var copyButton = CreateMessageActionButton(
            LocalizationManager.Instance.GetString("Copy"),
            LocalizationManager.Instance.GetString("YanitiPanoyaKopyala"));

        copyButton.Click += (_, _) => Clipboard.SetText((actions.Tag as string) ?? string.Empty);

        var retryButton = CreateMessageActionButton(
            LocalizationManager.Instance.GetString("Refresh"),
            LocalizationManager.Instance.GetString("BuYanitiYenidenUret"));

        retryButton.Click += (_, _) => RetryAssistantMessage((actions.Tag as string) ?? string.Empty);

        var continueButton = CreateMessageActionButton(
            LocalizationManager.Instance.GetString("Continue"),
            LocalizationManager.Instance.GetString("GirisAlaninaGec"));

        continueButton.Click += (_, _) =>

        {

            txtChatInput.Focus();

            txtChatInput.CaretIndex = txtChatInput.Text.Length;

        };

        actions.Children.Add(copyButton);

        actions.Children.Add(retryButton);

        actions.Children.Add(continueButton);

        return actions;

    }

    private void SetAssistantActionsEnabled(bool enabled)

    {

        if (!enabled)

        {

            // AI çalışmaya başladı: tüm barları devre dışı bırak

            foreach (var actionBar in _assistantActionBars.ToList())

            {

                actionBar.IsEnabled = false;

                actionBar.Visibility = Visibility.Collapsed;

                actionBar.Opacity = 0;

            }

        }

        else

        {

            // AI bitti: Bu turdaki barları temizle, sadece en sonuncusunu göster

            if (_currentTurnActionBars.Count > 0)

            {

                var finalBar = _currentTurnActionBars.Last();

                // Bu turun ara adımlarını (sonuncu hariç) kalıcı olarak listelerden sil

                foreach (var bar in _currentTurnActionBars.Take(_currentTurnActionBars.Count - 1))

                {

                    bar.Visibility = Visibility.Collapsed;

                    _assistantActionBars.Remove(bar);

                }

                // Sadece son cevabın action bar'ını göster

                finalBar.IsEnabled = true;

                finalBar.Visibility = Visibility.Visible;

                finalBar.Opacity = 0.42;

            }

            // Önceki turlardaki barlar (bu turdakiler temizlendikten sonra kalan) — tekrar aktif et

            foreach (var actionBar in _assistantActionBars.Except(_currentTurnActionBars).ToList())

            {

                actionBar.IsEnabled = true;

                actionBar.Visibility = Visibility.Visible;

                actionBar.Opacity = 0.42;

            }

            _currentTurnActionBars.Clear();

        }

    }

    private void RetryAssistantMessage(string assistantMessage)

    {

        if (_chatCts != null)

        {

            Notify("Önce mevcut AI işleminin tamamlanmasını bekleyin.", NotificationSeverity.Warning);

            return;

        }

        var session = _chatFlowService.ActiveSession;

        if (session == null)

        {

            return;

        }

        var assistantIndex = session.History.FindLastIndex(message =>

            message.Role == "assistant" && string.Equals(message.Content, assistantMessage, StringComparison.Ordinal));

        if (assistantIndex <= 0)

        {

            Notify("Bu yanıt için yeniden üretilecek kullanıcı mesajı bulunamadı.", NotificationSeverity.Warning);

            return;

        }

        var userIndex = assistantIndex - 1;

        while (userIndex >= 0 && session.History[userIndex].Role != "user")

        {

            userIndex--;

        }

        if (userIndex < 0)

        {

            Notify("Bu yanıt için yeniden üretilecek kullanıcı mesajı bulunamadı.", NotificationSeverity.Warning);

            return;

        }

        var originalMessage = session.History[userIndex];

        var originalText = originalMessage.Content ?? string.Empty;

        var originalAttachments = originalMessage.Attachments;

        session.History.RemoveRange(userIndex, session.History.Count - userIndex);

        _chatFlowService.SaveSessions();

        UpdateChatHistory();

        ShowChatView();

        RunBackground(SendMessageAsync(originalText, addUserBubble: true, attachmentsOverride: originalAttachments), "RetryAssistantMessage");

    }

    private static Button CreateMessageActionButton(string content, string toolTip)

    {

        return new Button

        {

            Content = content,

            FontSize = 10,

            Padding = new Thickness(6, 1, 6, 1),

            Margin = new Thickness(0, 0, 4, 0),

            Background = Brushes.Transparent,

            Foreground = new SolidColorBrush(Color.FromRgb(145, 164, 185)),

            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 63, 80)),

            BorderThickness = new Thickness(1),

            Cursor = Cursors.Hand,

            ToolTip = toolTip

        };

    }

    private static IEnumerable<Inline> CreateInlineElements(string text)

    {

        var inlines = new List<Inline>();

        int index = 0;

        while (index < text.Length)

        {

            if (text[index] == '`')

            {

                int closing = text.IndexOf('`', index + 1);

                if (closing > index + 1)

                {

                    var codeText = text.Substring(index + 1, closing - index - 1);

                    inlines.Add(new Run(codeText) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(60, 63, 70)), Foreground = Brushes.LightGreen });

                    index = closing + 1;

                    continue;

                }

            }

            if (index + 1 < text.Length && text[index] == '*' && text[index + 1] == '*')

            {

                int closing = text.IndexOf("**", index + 2, StringComparison.Ordinal);

                if (closing > index + 2)

                {

                    var boldText = text.Substring(index + 2, closing - index - 2);

                    inlines.Add(new Bold(new Run(boldText)));

                    index = closing + 2;

                    continue;

                }

            }

            if (index + 1 < text.Length && text[index] == '_' && text[index + 1] == '_')

            {

                int closing = text.IndexOf("__", index + 2, StringComparison.Ordinal);

                if (closing > index + 2)

                {

                    var boldText = text.Substring(index + 2, closing - index - 2);

                    inlines.Add(new Bold(new Run(boldText)));

                    index = closing + 2;

                    continue;

                }

            }

            if (text[index] == '*' || text[index] == '_')

            {

                char marker = text[index];

                int closing = text.IndexOf(marker, index + 1);

                if (closing > index + 1)

                {

                    var italicText = text.Substring(index + 1, closing - index - 1);

                    inlines.Add(new Italic(new Run(italicText)));

                    index = closing + 1;

                    continue;

                }

            }

            int nextSpecial = text.IndexOfAny(new[] { '`', '*', '_' }, index);

            if (nextSpecial < 0)

            {

                inlines.Add(new Run(text.Substring(index)));

                break;

            }

            if (nextSpecial > index)

            {

                inlines.Add(new Run(text.Substring(index, nextSpecial - index)));

                index = nextSpecial;

                continue;

            }

            inlines.Add(new Run(text[index].ToString()));

            index += 1;

        }

        return inlines;

    }

    private static ContextMenu CreateCopyContextMenu(FrameworkElement target)

    {

        var menu = new ContextMenu();

        var copyItem = new MenuItem { Header = "Kopyala" };

        copyItem.Click += (_, _) =>

        {

            string textToCopy;

            if (target is TextBox textBox)

            {

                textToCopy = string.IsNullOrEmpty(textBox.SelectedText) ? textBox.Text : textBox.SelectedText;

            }

            else if (target is RichTextBox richTextBox)

            {

                textToCopy = richTextBox.Selection.IsEmpty

                    ? new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text.TrimEnd('\r', '\n')

                    : richTextBox.Selection.Text;

            }

            else

            {

                textToCopy = string.Empty;

            }

            if (!string.IsNullOrEmpty(textToCopy))

            {

                Clipboard.SetText(textToCopy);

            }

        };

        menu.Items.Add(copyItem);

        return menu;

    }

    private void UpdateChatMessage(ListBoxItem item, string? appendText)

    {

        if (item == null || string.IsNullOrEmpty(appendText))

            return;

        try

        {

            var target = FindFirstTextElement(item.Content as DependencyObject);

            if (target is TextBox tb)

            {

                tb.Text += appendText;

                lstChatHistory.ScrollIntoView(item);

            }

            else if (target is RichTextBox rtb)

            {

                var range = new TextRange(rtb.Document.ContentEnd, rtb.Document.ContentEnd);

                range.Text = appendText;

                lstChatHistory.ScrollIntoView(item);

            }

        }

        catch { }

    }

    private void ReplaceStreamedItemContent(ListBoxItem locateStreamItem, string content)

    {

        try

        {

            // Update the Tag of the action bar so copy/retry buttons have the latest content

            if (locateStreamItem.Content is Panel outerPanel && 

                outerPanel.Children.Count > 0 && 

                outerPanel.Children[0] is Border b && 

                b.Child is Panel contentStack)

            {

                var actionBar = contentStack.Children.OfType<StackPanel>().FirstOrDefault(s => s.Orientation == Orientation.Horizontal);

                if (actionBar != null)

                {

                    actionBar.Tag = content;

                }

            }

            var target = FindFirstTextElement(locateStreamItem.Content as DependencyObject);

            // Eğer hedef bir TextBox ise ve metin Markdown / <think> içeriyorsa 

            // Onu RichTextBox ile değiştirip Markdown render'ını çalıştırmamız lazım!

            if (target is TextBox tb)

            {

                if (content.Contains("<think>") || content.Contains("```") || content.Contains("**") || content.Contains("#"))

                {

                    var parent = tb.Parent as Panel;

                    if (parent != null)

                    {

                        var index = parent.Children.IndexOf(tb);

                        parent.Children.RemoveAt(index);

                        var foregroundBrush = (Application.Current?.TryFindResource("ChatMessageForeground") as Brush) ?? Brushes.White;

                        var rtb = new RichTextBox

                        {

                            Background = Brushes.Transparent,

                            BorderThickness = new Thickness(0),

                            IsReadOnly = true,

                            IsDocumentEnabled = true,

                            Document = ConvertMarkdownToFlowDocument(content, foregroundBrush),

                            HorizontalAlignment = HorizontalAlignment.Stretch,

                            Margin = new Thickness(0),

                            Padding = new Thickness(0)

                        };

                        rtb.ContextMenu = CreateCopyContextMenu(rtb);

                        parent.Children.Insert(index, rtb);

                        locateStreamItem.InvalidateVisual();

                        return;

                    }

                }

                tb.Text = content;

                tb.IsReadOnly = false;

                tb.IsReadOnly = true;

                locateStreamItem.InvalidateVisual();

                return;

            }

            if (target is RichTextBox rtbExisting)

            {

                var foregroundBrush = (Application.Current?.TryFindResource("ChatMessageForeground") as Brush) ?? Brushes.White;

                rtbExisting.Document = ConvertMarkdownToFlowDocument(content, foregroundBrush);

                locateStreamItem.InvalidateVisual();

                return;

            }

            var panel = locateStreamItem.Content as Panel;

            if (panel == null && locateStreamItem.Content is ContentControl cc && cc.Content is Panel p2)

                panel = p2;

            if (panel != null)

            {

                var emptyStack = panel.Children.OfType<StackPanel>().FirstOrDefault();

                if (emptyStack != null)

                {

                    emptyStack.Children.Clear();

                    emptyStack.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });

                    locateStreamItem.InvalidateVisual();

                    return;

                }

                panel.Children.Clear();

                panel.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });

                locateStreamItem.InvalidateVisual();

            }

        }

        catch (Exception ex)

        {

            AddTerminalMessage($"ReplaceStreamedItemContent hatası: {ex.Message}");

            AddChatMessage("AI Asistan", content);

        }

    }

    private static FrameworkElement? FindFirstTextElement(DependencyObject? root)

    {

        if (root == null)

            return null;

        if (root is TextBox || root is TextBlock || root is RichTextBox)

            return (FrameworkElement)root;

        if (root is Panel panel)

        {

            foreach (UIElement child in panel.Children)

            {

                var found = FindFirstTextElement(child);

                if (found != null)

                    return found;

            }

        }

        if (root is ContentControl contentControl && contentControl.Content is DependencyObject content)

        {

            var found = FindFirstTextElement(content);

            if (found != null)

                return found;

        }

        if (root is Decorator decorator && decorator.Child is DependencyObject decoratorChild)

        {

            var found = FindFirstTextElement(decoratorChild);

            if (found != null)

                return found;

        }

        if (root is ItemsControl itemsControl)

        {

            foreach (var item in itemsControl.Items)

            {

                if (item is DependencyObject child)

                {

                    var found = FindFirstTextElement(child);

                    if (found != null)

                        return found;

                }

            }

        }

        return null;

    }

    private static TextBox? FindFirstTextBox(DependencyObject? root)

    {

        if (root == null)

            return null;

        if (root is TextBox tb)

            return tb;

        if (root is Panel panel)

        {

            foreach (UIElement child in panel.Children)

            {

                var found = FindFirstTextBox(child);

                if (found != null)

                    return found;

            }

        }

        if (root is ContentControl contentControl && contentControl.Content is DependencyObject content)

        {

            var found = FindFirstTextBox(content);

            if (found != null)

                return found;

        }

        if (root is Decorator decorator && decorator.Child is DependencyObject decoratorChild)

        {

            var found = FindFirstTextBox(decoratorChild);

            if (found != null)

                return found;

        }

        if (root is ItemsControl itemsControl)

        {

            foreach (var item in itemsControl.Items)

            {

                if (item is DependencyObject child)

                {

                    var found = FindFirstTextBox(child);

                    if (found != null)

                        return found;

                }

            }

        }

        return null;

    }

    // Chat Panel ViewModel and EventBus initialization

    private void InitializeChatPanelViewModel()

        {

            _chatPanelViewModel = new ChatPanelViewModel();

        // EventBus aboneliklerini ayarlayalim

        EventBus.Subscribe<TimelineEvent>(OnTimelineEvent);

        EventBus.Subscribe<ToolCallEvent>(OnToolCallEvent);

        EventBus.Subscribe<FileChangeEvent>(OnFileChangeEvent);

        EventBus.Subscribe<SubAgentRequestEvent>(OnSubAgentRequestEvent);

    }

    private void OnSubAgentRequestEvent(SubAgentRequestEvent ev)

    {

        // UI iş parçacığından Timeline güncellemesini yap, sonra arka plan işini başlat

        _ = Dispatcher.BeginInvoke(() =>

        {

            try

            {

                // Timeline'a alt ajan başlangıç bilgisi ekle

                EventBus.Publish(new TimelineEvent

                {

                    Type = TimelineEventType.Info,

                    Message = $"Alt Ajan ({ev.Role}) arka planda görevlendirildi: {ev.Prompt}",

                    Details = ev.Context

                });

                // Yeni gizli bir sohbet oturumu oluştur

                var subSession = new ChatSession { Name = $"SubAgent - {ev.Role}", IsHidden = true };

                // Arka plan görevi olarak çalıştır (ui'yi bloke etmez)

                _ = Task.Run(async () =>

                {

                    try

                    {

                        var systemPrompt = $"Sen bir alt-ajansın. Rolün: {ev.Role}. Görevin: {ev.Prompt}. Sadece hedefini gerçekleştir ve araçları kullan. Kullanıcı sana cevap veremez.";

                        var result = await _chatFlowService.SendMessageInternalAsync(

                            message: ev.Context + "\n" + ev.Prompt,

                            currentFilePath: null,

                            currentFileContent: null,

                            selectedFolder: _selectedFolder,

                            systemPrompt: systemPrompt,

                            qaAgentEnabled: false,

                            uiAgentEnabled: false,

                            attachments: null,

                            cancellationToken: default,

                            onTokenReceived: null,

                            onMessageAdded: null,

                            includeSystemPrompt: true,

                            targetSession: subSession);

                        var status = result.Success ? "Başarı" : "Hata";

                        var finalMsg = result.Messages.LastOrDefault(m => m.Sender == "AI Asistan")?.Content ?? result.ErrorMessage;

                        EventBus.Publish(new TimelineEvent

                        {

                            Type = result.Success ? TimelineEventType.Completed : TimelineEventType.Failed,

                            Message = $"Alt Ajan ({ev.Role}) görevini bitirdi. ({status})",

                            Details = finalMsg

                        });

                        if (result.Success)

                        {

                            _toolExecutor?.SubAgentCoordinator.CompleteTask(ev.TaskId, finalMsg);

                        }

                        else

                        {

                            _toolExecutor?.SubAgentCoordinator.FailTask(ev.TaskId, finalMsg);

                        }

                    }

                    catch (Exception ex)

                    {

                        EventBus.Publish(new TimelineEvent

                        {

                            Type = TimelineEventType.Failed,

                            Message = $"Alt Ajan ({ev.Role}) kritik hata aldı.",

                            Details = ex.Message

                        });

                        _toolExecutor?.SubAgentCoordinator.FailTask(ev.TaskId, ex.Message);

                    }

                });

            }

            catch (Exception ex)

            {

                Notify($"SubAgent başlatılamadı: {ex.Message}", NotificationSeverity.Error);

            }

        });

    }

    private void InitializeAgentWorkflow(string userRequest)

    {

        if (_chatPanelViewModel == null)

        {

            InitializeChatPanelViewModel();

        }

        _chatPanelViewModel!.Clear();

        _chatPanelViewModel.TodoItems.Clear();

        foreach (var todo in AgentWorkflowHelper.CreateTodoTasks(userRequest))

        {

            _chatPanelViewModel.TodoItems.Add(todo);

        }

        _chatPanelViewModel.TodoPanelVisible = true;

        _chatPanelViewModel.TimelinePanelVisible = true;

        _chatPanelViewModel.FileChangesPanelVisible = true;

        if (_chatPanelViewModel.TodoItems.Count > 0)

        {

            _chatPanelViewModel.TodoItems[0].Status = TodoTaskStatus.Running;

        }

        EventBus.Publish(new TimelineEvent

        {

            Type = TimelineEventType.Thinking,

            Message = "İsteği analiz ediyorum...",

            Details = userRequest,

            IsExpanded = true,

            IsCompleted = false

        });

        EventBus.Publish(new TimelineEvent

        {

            Type = TimelineEventType.Info,

            Message = "Ajan akışı başlatıldı",

            Details = "TODO listesi ve canlı durum güncellemeleri aktif.",

            IsExpanded = true,

            IsCompleted = false

        });

    }

    private void AdvanceTodoProgress(TodoTaskStatus status, string? message = null)

    {

        if (_chatPanelViewModel == null || _chatPanelViewModel.TodoItems.Count == 0)

        {

            return;

        }

        var runningIndex = _chatPanelViewModel.TodoItems.ToList().FindIndex(x => x.Status == TodoTaskStatus.Running);

        if (runningIndex >= 0)

        {

            _chatPanelViewModel.TodoItems[runningIndex].Status = status;

        }

        if (status == TodoTaskStatus.Completed && runningIndex + 1 < _chatPanelViewModel.TodoItems.Count)

        {

            _chatPanelViewModel.TodoItems[runningIndex + 1].Status = TodoTaskStatus.Running;

        }

        if (status == TodoTaskStatus.Failed && runningIndex >= 0)

        {

            _chatPanelViewModel.TodoItems[runningIndex].Status = TodoTaskStatus.Failed;

        }

    }

    private void CompleteAllRemainingTodos()

    {

        if (_chatPanelViewModel == null || _chatPanelViewModel.TodoItems.Count == 0)

            return;

        foreach (var item in _chatPanelViewModel.TodoItems)

        {

            if (item.Status == TodoTaskStatus.Pending || item.Status == TodoTaskStatus.Running)

            {

                item.Status = TodoTaskStatus.Completed;

            }

        }

    }

    private void BtnShowChatHistory_Click(object sender, RoutedEventArgs e)

    {

        if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Visible;

        if (timelinePanel != null) timelinePanel.Visibility = Visibility.Collapsed;

        if (todoPanel != null) todoPanel.Visibility = Visibility.Collapsed;

        if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Collapsed;

        // Buton stillerini guncelle

        if (btnShowChatHistory != null)

        {

            btnShowChatHistory.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

            btnShowChatHistory.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        }

        if (btnShowTimeline != null)

        {

            btnShowTimeline.Background = Brushes.Transparent;

            btnShowTimeline.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowTodoList != null)

        {

            btnShowTodoList.Background = Brushes.Transparent;

            btnShowTodoList.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowFileChanges != null)

        {

            btnShowFileChanges.Background = Brushes.Transparent;

            btnShowFileChanges.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

    }

    // Panel sekmeleri buton tiklama olaylari

    private void BtnShowTimeline_Click(object sender, RoutedEventArgs e)

        {

            if (timelinePanel != null) timelinePanel.Visibility = Visibility.Visible;

            if (todoPanel != null) todoPanel.Visibility = Visibility.Collapsed;

            if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Collapsed;

            if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Collapsed;

        // Buton stillerini guncelle

        if (btnShowTimeline != null)

        {

            btnShowTimeline.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

            btnShowTimeline.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        }

        if (btnShowTodoList != null)

        {

            btnShowTodoList.Background = Brushes.Transparent;

            btnShowTodoList.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowFileChanges != null)

        {

            btnShowFileChanges.Background = Brushes.Transparent;

            btnShowFileChanges.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowChatHistory != null)

        {

            btnShowChatHistory.Background = Brushes.Transparent;

            btnShowChatHistory.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

    }

    private void BtnShowTodoList_Click(object sender, RoutedEventArgs e)

        {

            if (timelinePanel != null) timelinePanel.Visibility = Visibility.Collapsed;

            if (todoPanel != null) todoPanel.Visibility = Visibility.Visible;

            if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Collapsed;

            if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Collapsed;

        // Buton stillerini guncelle

        if (btnShowTodoList != null)

        {

            btnShowTodoList.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

            btnShowTodoList.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        }

        if (btnShowTimeline != null)

        {

            btnShowTimeline.Background = Brushes.Transparent;

            btnShowTimeline.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowFileChanges != null)

        {

            btnShowFileChanges.Background = Brushes.Transparent;

            btnShowFileChanges.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowChatHistory != null)

        {

            btnShowChatHistory.Background = Brushes.Transparent;

            btnShowChatHistory.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

    }

    private void FileChangeItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)

    {

        if (sender is FrameworkElement element && element.DataContext is FileChangeItemViewModel item)

        {

            if (!string.IsNullOrEmpty(item.FilePath))

            {

                OpenFile(item.FilePath);

            }

        }

    }

    private void BtnShowFileChanges_Click(object sender, RoutedEventArgs e)

        {

            if (timelinePanel != null) timelinePanel.Visibility = Visibility.Collapsed;

            if (todoPanel != null) todoPanel.Visibility = Visibility.Collapsed;

            if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Visible;

            if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Collapsed;

        // Buton stillerini guncelle

        if (btnShowFileChanges != null)

        {

            btnShowFileChanges.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

            btnShowFileChanges.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        }

        if (btnShowTimeline != null)

        {

            btnShowTimeline.Background = Brushes.Transparent;

            btnShowTimeline.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowTodoList != null)

        {

            btnShowTodoList.Background = Brushes.Transparent;

            btnShowTodoList.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

        if (btnShowChatHistory != null)

        {

            btnShowChatHistory.Background = Brushes.Transparent;

            btnShowChatHistory.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        }

    }

    public void UpdateMultiAgentButtonUI()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateMultiAgentButtonUI);
            return;
        }

        if (btnMultiAgentToggle == null) return;

        var settings = SettingsWindow.GetSettings();
        bool isActive = settings.EnableMultiAgentImageGeneration;

        if (isActive)
        {
            btnMultiAgentToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1b382b"));
            btnMultiAgentToggle.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00ffb7"));
            btnMultiAgentToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00ffb7"));
        }
        else
        {
            btnMultiAgentToggle.Background = Brushes.Transparent;
            btnMultiAgentToggle.BorderBrush = TryFindResource("DividerBrush") as Brush ?? Brushes.Gray;
            btnMultiAgentToggle.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        }
    }

    private void BtnMultiAgentToggle_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsWindow.GetSettings();
        settings.EnableMultiAgentImageGeneration = !settings.EnableMultiAgentImageGeneration;
        SettingsWindow.SaveSettings(settings);

        UpdateMultiAgentButtonUI();

        string msgKey = settings.EnableMultiAgentImageGeneration ? "MultiAgentActiveToast" : "MultiAgentInactiveToast";
        string localizedMsg = LocalizationManager.Instance[msgKey];
        if (string.IsNullOrEmpty(localizedMsg))
        {
            localizedMsg = settings.EnableMultiAgentImageGeneration
                ? "🎨 AI Görsel Desteği AKTİF: AI kod yazarken ihtiyaç duyduğunda görsel üretebilecek."
                : "🎨 AI Görsel Desteği PASİF: AI sadece varsayılan kod üretim modunda çalışacak.";
        }
        UpdateProcessStatus(localizedMsg);
    }

    private void BtnMultiAgentToggle_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        var settingsWin = new WorkspaceSettingsWindow(AgentWorkspaceMode.ImageStudio);
        settingsWin.Owner = this;
        settingsWin.ShowDialog();

        _settings = SettingsWindow.GetSettings();
        UpdateMultiAgentButtonUI();
    }

        // AI çalışmaya başladığında panelleri otomatik göster

        private void ShowAgentPanels()

                {

                    if (!Dispatcher.CheckAccess())

                    {

                        _ = Dispatcher.BeginInvoke(ShowAgentPanels);

                        return;

                    }

                    try

            {

                // Tüm yeni panelleri görünür yap

                if (timelinePanel != null) timelinePanel.Visibility = Visibility.Visible;

                if (todoPanel != null) todoPanel.Visibility = Visibility.Visible;

                if (fileChangesPanel != null) fileChangesPanel.Visibility = Visibility.Collapsed;

                if (chatHistoryPanel != null) chatHistoryPanel.Visibility = Visibility.Visible;

                // Timeline butonunu aktif yap

                if (btnShowTimeline != null)

                {

                    btnShowTimeline.Background = TryFindResource("SurfaceAlt2Brush") as Brush ?? new SolidColorBrush(Color.FromRgb(60, 60, 80));

                    btnShowTimeline.Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

                }

                if (btnShowTodoList != null)

                {

                    btnShowTodoList.Background = Brushes.Transparent;

                    btnShowTodoList.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

                }

                if (btnShowFileChanges != null)

                {

                    btnShowFileChanges.Background = Brushes.Transparent;

                    btnShowFileChanges.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

                }

                // AI durumunu güncelle

                if (txtAiStatus != null) txtAiStatus.Text = LocalizationManager.Instance.GetString("Calisiyor");

                if (aiStatusEllipse != null) aiStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7));

            }

            catch { }

        }

        // İşlem sonu özeti oluştur (başarılı veya başarısız)

            private void GenerateFinalSummary(ChatFlowResult result)

            {

                if (!Dispatcher.CheckAccess())

                {

                    _ = Dispatcher.BeginInvoke(() => GenerateFinalSummary(result));

                    return;

                }

                // Hata mesajını belirle

                bool hasError = !result.Success || !string.IsNullOrEmpty(result.ErrorMessage);

                string statusEmoji = hasError ? "❌" : "✅";

                string statusText = hasError ? LocalizationManager.Instance.GetString("HataIleTamamlandi") : LocalizationManager.Instance.GetString("IslemBasariylaTamamlandi");

                // Tamamlandı event'i yayınla

                EventBus.Publish(new TimelineEvent

                {

                    Type = hasError ? TimelineEventType.Failed : TimelineEventType.Completed,

                    Message = $"{statusEmoji} {statusText}",

                    Details = BuildSummaryDetails(result),

                    IsCompleted = !hasError,

                    IsFailed = hasError,

                    IsExpanded = true

                });

                // TODO listesini tamamla

                AdvanceTodoProgress(hasError ? TodoTaskStatus.Failed : TodoTaskStatus.Completed);

                // Status bar'ı güncelle

                if (txtAiStatus != null) txtAiStatus.Text = hasError ? LocalizationManager.Instance.GetString("Hata") : LocalizationManager.Instance.GetString("Hazir2");

                if (aiStatusEllipse != null) aiStatusEllipse.Fill = new SolidColorBrush(hasError ? Color.FromRgb(244, 67, 54) : Color.FromRgb(76, 175, 80));

            }

        private string BuildSummaryDetails(ChatFlowResult result)

        {

            var details = new System.Text.StringBuilder();

            var modifiedFiles = _chatPanelViewModel?.FileChangeItems

                .Where(f => f.Type == FileChangeType.Modified)

                .Select(f => f.FileName)

                .ToList();

            var createdFiles = _chatPanelViewModel?.FileChangeItems

                .Where(f => f.Type == FileChangeType.Created)

                .Select(f => f.FileName)

                .ToList();

            if (modifiedFiles != null && modifiedFiles.Count > 0)

                details.AppendLine($"📝 Değiştirilen dosyalar ({modifiedFiles.Count}): {string.Join(", ", modifiedFiles)}");

            if (createdFiles != null && createdFiles.Count > 0)

                details.AppendLine($"✨ Oluşturulan dosyalar ({createdFiles.Count}): {string.Join(", ", createdFiles)}");

            if (result.UpdatedFilePaths.Count > 0)

                details.AppendLine($"📂 Güncellenen dosya yolları: {string.Join(", ", result.UpdatedFilePaths.Select(Path.GetFileName))}");

            details.AppendLine($"⏱️ Bitiş: {DateTime.Now:HH:mm:ss}");

            return details.ToString().Trim();

        }

        // EventBus olay isleyicileri

        private void OnTimelineEvent(TimelineEvent evt)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(() => OnTimelineEvent(evt));

            return;

        }

        var timelineItem = new TimelineItemViewModel

        {

            Icon = GetTimelineEventIcon(evt.Type),

            Message = evt.Message,

            Details = evt.Details,

            IsCompleted = evt.IsCompleted,

            IsFailed = evt.IsFailed,

            IsExpanded = evt.IsExpanded

        };

        if (evt.Type == TimelineEventType.Thinking)

        {

            AdvanceTodoProgress(TodoTaskStatus.Running);

        }

        else if (evt.Type == TimelineEventType.Completed)

        {

            AdvanceTodoProgress(TodoTaskStatus.Completed);

        }

        else if (evt.Type == TimelineEventType.Failed)

        {

            AdvanceTodoProgress(TodoTaskStatus.Failed);

        }

        else if (evt.Type == TimelineEventType.ReadingFile || evt.Type == TimelineEventType.RunningTerminal)

        {

            AdvanceTodoProgress(TodoTaskStatus.Completed);

        }

        _chatPanelViewModel.TimelineItems.Add(timelineItem);

        // AI durumunu guncelle

        UpdateAiStatus(evt);

    }

    private void OnToolCallEvent(ToolCallEvent evt)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(() => OnToolCallEvent(evt));

            return;

        }

        // Eger uretilen plan ise, TODO listesini bu yeni planla guncelle

        if (evt.ToolName == "CreatePlan" && evt.IsSuccess && !string.IsNullOrWhiteSpace(evt.Output))

        {

            try

            {

                // PlanOutput.AppendLine ile olusturulan metinden "=== PLAN DETAYLARI ===" veya "Uretilen Plan:" sonrasini alabiliriz.

                // Veya dogrudan tum ciktidan liste parse etmeye calisabiliriz. PlanModeHelper.ParseTodoItemsFromPlanText listeyi bulur.

                var todoItems = PlanModeHelper.ParseTodoItemsFromPlanText(evt.Output);

                if (todoItems.Count > 0)

                {

                    _chatPanelViewModel.TodoItems.Clear();

                    foreach (var todo in todoItems)

                    {

                        _chatPanelViewModel.TodoItems.Add(todo);

                    }

                    if (_chatPanelViewModel.TodoItems.Count > 0)

                    {

                        _chatPanelViewModel.TodoItems[0].Status = TodoTaskStatus.Running;

                    }

                }

            }

            catch (Exception ex)

            {

                // Hata durumunda yutabiliriz, boylece akis bozulmaz

                System.Diagnostics.Debug.WriteLine($"TODO listesi guncelleme hatasi: {ex.Message}");

            }

        }

        // Tool call olayini timeline'a da ekleyelim

        // Uzun dosya içeriklerini timeline'da kısalt (max 150 karakter)

        const int MaxDetailsLength = 150;

        string? details = evt.Output;

        if (details != null && details.Length > MaxDetailsLength)

        {

            details = details.Substring(0, MaxDetailsLength).TrimEnd() + "...";

        }

        var timelineItem = new TimelineItemViewModel

        {

            Icon = "🔧",

            Message = $"Arac cagrisi: {evt.ToolName}",

            Details = details,

            IsCompleted = evt.IsSuccess,

            IsFailed = !evt.IsSuccess && evt.EndTime.HasValue

        };

        _chatPanelViewModel.TimelineItems.Add(timelineItem);

    }

    private void OnFileChangeEvent(FileChangeEvent evt)

    {

        if (!Dispatcher.CheckAccess())

        {

            _ = Dispatcher.BeginInvoke(() => OnFileChangeEvent(evt));

            return;

        }

        var fileChangeItem = new FileChangeItemViewModel

        {

            FileName = System.IO.Path.GetFileName(evt.FilePath),

            FilePath = evt.FilePath,

            Type = evt.Type

        };

        _chatPanelViewModel.FileChangeItems.Add(fileChangeItem);

        AdvanceTodoProgress(TodoTaskStatus.Completed);

        // Timeline'a da ekleyelim

        var timelineItem = new TimelineItemViewModel

        {

            Icon = fileChangeItem.Icon,

            Message = $"{(evt.Type == FileChangeType.Created ? "Olusturuldu" : evt.Type == FileChangeType.Modified ? "Guncellendi" : "Silindi")}: {fileChangeItem.FileName}",

            IsCompleted = true

        };

        _chatPanelViewModel.TimelineItems.Add(timelineItem);

        // Dosya ağacını otomatik yenile

        if (!string.IsNullOrEmpty(_selectedFolder))

        {

            LoadFileTree(_selectedFolder);

        }

    }

    private string GetTimelineEventIcon(TimelineEventType type)

    {

        return type switch

        {

            TimelineEventType.Thinking => "🧠",

            TimelineEventType.ReadingFile => "📖",

            TimelineEventType.EditingFile => "✏️",

            TimelineEventType.CreatingFile => "➕",

            TimelineEventType.DeletingFile => "🗑️",

            TimelineEventType.RunningTerminal => "💻",

            TimelineEventType.Building => "🔨",

            TimelineEventType.Testing => "🧪",

            TimelineEventType.FixingError => "🔧",

            TimelineEventType.Completed => "✅",

            TimelineEventType.Failed => "❌",

            _ => "ℹ️"

        };

    }

    private void UpdateAiStatus(TimelineEvent evt)

    {

        string statusText;

        Color statusColor;

        if (_chatPanelViewModel != null)

        {

            _chatPanelViewModel.TodoPanelVisible = true;

            _chatPanelViewModel.TimelinePanelVisible = true;

            _chatPanelViewModel.FileChangesPanelVisible = true;

        }

        switch (evt.Type)

        {

            // Eğer AI işi bitmişse (_chatCts == null), geç gelen ara araç eventleri

            // göstergeyi kirletmesin. Sadece Completed/Failed eventleri geçsin.

            case TimelineEventType.ReadingFile:

            case TimelineEventType.EditingFile:

            case TimelineEventType.CreatingFile:

            case TimelineEventType.DeletingFile:

            case TimelineEventType.RunningTerminal:

            case TimelineEventType.Building:

            case TimelineEventType.Testing:

            case TimelineEventType.FixingError:

            case TimelineEventType.Thinking:

                if (_chatCts == null)

                    return; // İş bitti, geç gelen eventleri yoksay

                break;

            default:

                break;

        }

        switch (evt.Type)

        {

            case TimelineEventType.Thinking:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("Dusunuyor"), LocalizationManager.Instance.GetString("Dusunuyor"));

                statusColor = Color.FromRgb(255, 193, 7); // Yellow

                break;

            case TimelineEventType.ReadingFile:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("DosyaOkuyor"), LocalizationManager.Instance.GetString("DosyaOkuyor"));

                statusColor = Color.FromRgb(33, 150, 243); // Blue

                break;

            case TimelineEventType.EditingFile:

            case TimelineEventType.CreatingFile:

            case TimelineEventType.DeletingFile:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("DosyalariDuzenliyor"), LocalizationManager.Instance.GetString("DosyalariDuzenliyor"));

                statusColor = Color.FromRgb(156, 39, 176); // Purple

                break;

            case TimelineEventType.RunningTerminal:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("TerminalKomutuCalisiyor"), LocalizationManager.Instance.GetString("TerminalKomutuCalisiyor"));

                statusColor = Color.FromRgb(255, 152, 0); // Orange

                break;

            case TimelineEventType.Building:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("ProjeDerleniyor"), LocalizationManager.Instance.GetString("ProjeDerleniyor"));

                statusColor = Color.FromRgb(255, 152, 0); // Orange

                break;

            case TimelineEventType.Testing:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("TestlerCalisiyor"), "🧪 Running tests...");

                statusColor = Color.FromRgb(255, 193, 7); // Yellow

                break;

            case TimelineEventType.FixingError:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("HatalariDuzeltiyor"), LocalizationManager.Instance.GetString("HatalariDuzeltiyor"));

                statusColor = Color.FromRgb(244, 67, 54); // Red

                break;

            case TimelineEventType.Completed:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("Hazir2"), LocalizationManager.Instance.GetString("Hazir2"));

                statusColor = Color.FromRgb(76, 175, 80); // Green

                break;

            case TimelineEventType.Failed:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("Hata"), LocalizationManager.Instance.GetString("Hata"));

                statusColor = Color.FromRgb(244, 67, 54); // Red

                break;

            default:

                statusText = Localization.Get(LocalizationManager.Instance.GetString("Hazir2"), LocalizationManager.Instance.GetString("Hazir2"));

                statusColor = Color.FromRgb(76, 175, 80); // Green

                break;

        }

        if (txtAiStatus != null)

        {

            txtAiStatus.Text = statusText;

        }

        if (aiStatusEllipse != null)

        {

            aiStatusEllipse.Fill = new SolidColorBrush(statusColor);

        }

    }

    /// <summary>

    /// VSCode/Cursor tarzı cursor-aware bağlam: Tüm dosya yerine imleç etrafındaki

    /// ±windowLines satırlık pencereyi döndürür. Küçük dosyalarda tüm içeriği verir.

    /// AI'ya dosyanın gerçek boyutunu ve hangi bölgeyi gördüğünü bildirir.

    /// </summary>

    private static string GetCursorWindowContent(string fullText, int caretLine, int windowLines = 80)

    {

        if (string.IsNullOrEmpty(fullText))

            return string.Empty;

        var lines = fullText.Split('\n');

        int totalLines = lines.Length;

        // Küçük dosyalarda tümünü gönder

        if (totalLines <= windowLines * 2)

        {

            return $"[Dosya: {totalLines} satır — tam içerik]\n" + fullText;

        }

        // Pencere sınırlarını hesapla (1-indexed → 0-indexed)

        int center = Math.Max(0, caretLine - 1);

        int startLine = Math.Max(0, center - windowLines);

        int endLine = Math.Min(totalLines - 1, center + windowLines);

        var windowLines_ = lines.Skip(startLine).Take(endLine - startLine + 1);

        var windowContent = string.Join("\n", windowLines_);

        // Bağlam başlığı: AI'ya dosya büyüklüğünü ve hangi bölgeyi gördüğünü bildir

        var header = $"[Dosya: {totalLines} satır — gösterilen bölge: satır {startLine + 1}–{endLine + 1} (imleç: {caretLine})]";

        var prefix = startLine > 0 ? $"... ({startLine} satır gizlendi) ...\n" : "";

        var suffix = endLine < totalLines - 1 ? $"\n... ({totalLines - endLine - 1} satır gizlendi) ..." : "";

        return $"{header}\n{prefix}{windowContent}{suffix}";

    }

}
