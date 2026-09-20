
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mdaiAgent
{
    // Event Types
    public enum TimelineEventType
    {
        Info,
        Thinking,
        ReadingFile,
        EditingFile,
        CreatingFile,
        DeletingFile,
        RunningTerminal,
        Building,
        Testing,
        FixingError,
        Completed,
        Failed
    }

    public enum TodoTaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted
    }

    // Data Models (for internal use)
    public class TimelineEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public TimelineEventType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool IsExpanded { get; set; } = true;
        public bool IsCompleted { get; set; } = false;
        public bool IsFailed { get; set; } = false;
    }

    public class TodoTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Description { get; set; } = string.Empty;
        public TodoTaskStatus Status { get; set; } = TodoTaskStatus.Pending;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public class ToolCallEvent
    {
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
        public string ToolName { get; set; } = string.Empty;
        public string? Parameters { get; set; }
        public string? Output { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsExpanded { get; set; } = true;
    }

    public class FileChangeEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public FileChangeType Type { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? OldContent { get; set; }
        public string? NewContent { get; set; }
        public string? DiffPreview { get; set; }
    }

    public class SubAgentRequestEvent
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString();
        public string Role { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Context { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SubAgentCompletedEvent
    {
        public string TaskId { get; set; } = "";
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    // Simple Event Bus
    public static class EventBus
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Collections.Concurrent.ConcurrentBag<Delegate>> _handlers
            = new System.Collections.Concurrent.ConcurrentDictionary<Type, System.Collections.Concurrent.ConcurrentBag<Delegate>>();

        public static void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var bag))
            {
                bag = new System.Collections.Concurrent.ConcurrentBag<Delegate>();
                _handlers[type] = bag;
            }
            bag.Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var bag))
            {
                return;
            }

            var snapshot = new List<Delegate>();
            while (bag.TryTake(out var item))
            {
                snapshot.Add(item);
            }

            foreach (var item in snapshot)
            {
                if (!ReferenceEquals(item, handler) && item != null)
                {
                    bag.Add(item);
                }
            }
        }

        public static void Publish<T>(T eventData)
        {
            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var bag))
            {
                foreach (var handler in bag)
                {
                    if (handler is Action<T> typedHandler)
                    {
                        try
                        {
                            typedHandler(eventData);
                        }
                        catch
                        {
                            // Ignore errors for now
                        }
                    }
                }
            }
        }
    }

    // View Models for UI
    public class TodoItemViewModel : INotifyPropertyChanged
    {
        private string _description = string.Empty;
        private TodoTaskStatus _status = TodoTaskStatus.Pending;

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        public TodoTaskStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StatusIcon => Status switch
        {
            TodoTaskStatus.Pending => "☐",
            TodoTaskStatus.Running => "🟡",
            TodoTaskStatus.Completed => "✅",
            TodoTaskStatus.Failed => "❌",
            TodoTaskStatus.Skipped => "➡️",
            _ => "☐"
        };

        public string StatusColor => Status switch
        {
            TodoTaskStatus.Pending => "#909090",
            TodoTaskStatus.Running => "#FFD700",
            TodoTaskStatus.Completed => "#4CAF50",
            TodoTaskStatus.Failed => "#FF5252",
            TodoTaskStatus.Skipped => "#9E9E9E",
            _ => "#909090"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TimelineItemViewModel : INotifyPropertyChanged
    {
        private string _icon = "🔄";
        private string _message = string.Empty;
        private string? _details;
        private bool _isExpanded = false;
        private bool _isCompleted = false;
        private bool _isFailed = false;

        public string Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged();
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged();
            }
        }

        public string? Details
        {
            get => _details;
            set
            {
                _details = value;
                OnPropertyChanged();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                _isCompleted = value;
                OnPropertyChanged();
            }
        }

        public bool IsFailed
        {
            get => _isFailed;
            set
            {
                _isFailed = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FileChangeItemViewModel : INotifyPropertyChanged
    {
        private string _icon = "📄";
        private string _fileName = string.Empty;
        private string _statusColor = "#d0d0d0";
        private FileChangeType _type;

        public string Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged();
            }
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }

        public string StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                OnPropertyChanged();
            }
        }

        public FileChangeType Type
        {
            get => _type;
            set
            {
                _type = value;
                Icon = value switch
                {
                    FileChangeType.Created => "➕",
                    FileChangeType.Modified => "✏️",
                    FileChangeType.Deleted => "🗑️",
                    _ => "📄"
                };
                StatusColor = value switch
                {
                    FileChangeType.Created => "#4CAF50",
                    FileChangeType.Modified => "#2196F3",
                    FileChangeType.Deleted => "#FF5252",
                    _ => "#d0d0d0"
                };
                OnPropertyChanged();
            }
        }

        public string? FilePath { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class AgentWorkflowHelper
    {
        public static IReadOnlyList<TodoItemViewModel> CreateTodoTasks(string request)
        {
            var normalized = (request ?? string.Empty).Trim();
            var tasks = new List<TodoItemViewModel>
            {
                new TodoItemViewModel { Description = "İsteği anlamlandır ve planı çıkar" },
                new TodoItemViewModel { Description = "İlgili dosyaları incele" },
                new TodoItemViewModel { Description = "Gerekli değişiklikleri uygula" },
                new TodoItemViewModel { Description = "Derleme/test kontrolü yap" }
            };

            if (normalized.Contains("dosya", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("düzenle", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("oluştur", StringComparison.OrdinalIgnoreCase))
            {
                tasks.Insert(2, new TodoItemViewModel { Description = "İlgili dosyaları güncelle" });
            }

            if (normalized.Contains("hata", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("hatalı", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("debug", StringComparison.OrdinalIgnoreCase))
            {
                tasks.Add(new TodoItemViewModel { Description = "Hata varsa düzelt ve tekrar kontrol et" });
            }

            return tasks;
        }
    }

    public class MentionedFileViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string FullPath { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Icon { get; set; } = "📄";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ChatPanelViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TodoItemViewModel> TodoItems { get; set; } = new ObservableCollection<TodoItemViewModel>();
        public ObservableCollection<TimelineItemViewModel> TimelineItems { get; set; } = new ObservableCollection<TimelineItemViewModel>();
        public ObservableCollection<FileChangeItemViewModel> FileChangeItems { get; set; } = new ObservableCollection<FileChangeItemViewModel>();
        public ObservableCollection<MentionedFileViewModel> MentionedFiles { get; set; } = new ObservableCollection<MentionedFileViewModel>();

        private bool _todoPanelVisible = false;
        private bool _timelinePanelVisible = false;
        private bool _fileChangesPanelVisible = false;
        private string _aiStatusText = LocalizationManager.Instance.GetString("Hazir");
        private string _aiStatusColor = "#4CAF50";

        public bool TodoPanelVisible
        {
            get => _todoPanelVisible;
            set
            {
                _todoPanelVisible = value;
                OnPropertyChanged();
            }
        }

        public bool TimelinePanelVisible
        {
            get => _timelinePanelVisible;
            set
            {
                _timelinePanelVisible = value;
                OnPropertyChanged();
            }
        }

        public bool FileChangesPanelVisible
        {
            get => _fileChangesPanelVisible;
            set
            {
                _fileChangesPanelVisible = value;
                OnPropertyChanged();
            }
        }

        public string AiStatusText
        {
            get => _aiStatusText;
            set
            {
                _aiStatusText = value;
                OnPropertyChanged();
            }
        }

        public string AiStatusColor
        {
            get => _aiStatusColor;
            set
            {
                _aiStatusColor = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Clear()
        {
            TodoItems.Clear();
            TimelineItems.Clear();
            FileChangeItems.Clear();
            MentionedFiles.Clear();
            TodoPanelVisible = false;
            TimelinePanelVisible = false;
            FileChangesPanelVisible = false;
            AiStatusText = LocalizationManager.Instance.GetString("Hazir");
            AiStatusColor = "#4CAF50";
        }
    }
}
