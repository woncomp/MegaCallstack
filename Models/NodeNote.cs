using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MegaCallstack.Models
{
    public class NodeNote : INotifyPropertyChanged
    {
        private string _emoji;
        private string _text;

        public string Emoji
        {
            get => _emoji;
            set { _emoji = value; OnPropertyChanged(); }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
