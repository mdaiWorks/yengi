using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mdaiAgent
{
    public class ChatSession : INotifyPropertyChanged
    {
        private string _name = Localization.Get("Yeni Sohbet", "New Chat");
        private bool _isSelected;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<ExtendedChatMessage> History { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsHidden { get; set; } = false;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
