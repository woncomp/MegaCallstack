using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MegaCallstack.Models
{
    public class TreeViewNode : INotifyPropertyChanged
    {
        private string _displayText;
        private TreeViewNode _parent;
        private Brush _displayBackground;
        private bool _isBold;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isColorExplicitlySet;

        public string DisplayText
        {
            get => _displayText;
            set { _displayText = value; OnPropertyChanged(); }
        }

        public CallstackFrame Frame { get; set; }
        public int MergeId { get; set; }
        public bool IsLeaf { get; set; }

        public string TooltipText
        {
            get
            {
                if (Frame != null)
                    return Frame.BuildTooltipText();
                return DisplayText;
            }
        }

        public TreeViewNode Parent
        {
            get => _parent;
            set
            {
                if (_parent != value)
                {
                    _parent = value;
                    ResolveColor();
                }
            }
        }

        public Brush DisplayBackground
        {
            get => _displayBackground;
            set { _displayBackground = value; OnPropertyChanged(); }
        }

        public bool IsBold
        {
            get => _isBold;
            set { _isBold = value; OnPropertyChanged(); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TreeViewNode> Children { get; } = new ObservableCollection<TreeViewNode>();

        public TreeViewNode()
        {
            Children.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (TreeViewNode child in e.NewItems)
                    {
                        child.Parent = this;
                    }
                }
            };
        }

        public void SetColorAndPropagate(Brush color)
        {
            DisplayBackground = color;
            _isColorExplicitlySet = true;
            PropagateColorToAncestors();
        }

        public void ClearColorAndPropagate()
        {
            DisplayBackground = null;
            _isColorExplicitlySet = false;
            ResolveColor();
            PropagateColorToAncestors();
        }

        public void PropagateColorToAncestors()
        {
            var node = Parent;
            while (node != null)
            {
                node.ResolveColor();
                node = node.Parent;
            }
        }

        public void ResolveColor()
        {
            if (_isColorExplicitlySet)
                return;

            foreach (var child in Children)
            {
                var childColor = child.GetEffectiveColor();
                if (childColor != null)
                {
                    DisplayBackground = childColor;
                    return;
                }
            }

            DisplayBackground = null;
        }

        public Brush GetEffectiveColor()
        {
            if (DisplayBackground != null)
                return DisplayBackground;

            foreach (var child in Children)
            {
                var color = child.GetEffectiveColor();
                if (color != null)
                    return color;
            }

            return null;
        }

        public TreeViewNode FindNodeByHash(int hashCode)
        {
            if (Frame != null && Frame.HashCode == hashCode)
                return this;

            foreach (var child in Children)
            {
                var found = child.FindNodeByHash(hashCode);
                if (found != null)
                    return found;
            }

            return null;
        }

        public void SetPathBold(bool bold)
        {
            IsBold = bold;
            Parent?.SetPathBold(bold);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
