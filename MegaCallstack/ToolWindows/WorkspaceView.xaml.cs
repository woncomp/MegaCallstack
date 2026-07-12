using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MegaCallstack.Models;
using MegaCallstack.Services;
using MegaCallstack.ViewModels;

namespace MegaCallstack.ToolWindows
{
    public partial class WorkspaceView : UserControl
    {
        private SessionViewModel _viewModel;

        public WorkspaceView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public event Action<string, int> NavigateToFile
        {
            add
            {
                if (DataContext is SessionViewModel vm)
                    vm.NavigateToFile += value;
                else
                    _navigateToFile += value;
            }
            remove
            {
                if (DataContext is SessionViewModel vm)
                    vm.NavigateToFile -= value;
                _navigateToFile -= value;
            }
        }

        private event Action<string, int> _navigateToFile;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SessionViewModel oldVm)
            {
                oldVm.NavigateToFile -= OnNavigateToFile;
                oldVm.TreeUpdated -= OnTreeUpdated;
            }

            _viewModel = e.NewValue as SessionViewModel;
            if (_viewModel != null)
            {
                _viewModel.NavigateToFile += OnNavigateToFile;
                _viewModel.TreeUpdated += OnTreeUpdated;

                if (_navigateToFile != null)
                {
                    foreach (var handler in _navigateToFile.GetInvocationList())
                    {
                        _viewModel.NavigateToFile += (Action<string, int>)handler;
                    }
                }
            }
        }

        private void OnNavigateToFile(string fileName, int lineNumber)
        {
            _navigateToFile?.Invoke(fileName, lineNumber);
        }

        private void OnTreeUpdated()
        {
            MainTreeView.ItemsSource = _viewModel?.DisplayTreeNodes;
        }

        private void TreeView_Selected(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeViewNode node)
            {
                if (_viewModel != null)
                    _viewModel.SelectedNode = node;
            }
        }

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TreeViewNode node)
            {
                if (_viewModel != null)
                    _viewModel.SelectedNode = node;
            }
            else if (sender is TreeViewItem tvi && tvi.DataContext is TreeViewNode node2)
            {
                if (_viewModel != null)
                    _viewModel.SelectedNode = node2;
            }
        }

        private void TreeViewItem_DoubleClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.DoubleClickNodeCommand.Execute(null);
        }

        private void TreeViewItem_ExpandedCollapsed(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi && tvi.DataContext is TreeViewNode node && _viewModel != null)
            {
                if (node.Frame != null && _viewModel.ActiveSession != null)
                {
                    _viewModel.TreeBuilder.SaveExpansionState(_viewModel.ActiveSession, node.MergeId, tvi.IsExpanded);
                }
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
                textBox.SelectAll();
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _viewModel?.ConfirmRenameCommand.Execute(null);
            else if (e.Key == Key.Escape)
                _viewModel?.CancelRenameCommand.Execute(null);
        }

        private void DeleteSelectedSession_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.SelectedSession == null)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete session '{_viewModel.SelectedSession.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                _viewModel.DeleteSelectedSessionCommand.Execute(null);
        }

        private void SessionViewContent_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel?.SelectedSession != null)
                _viewModel.ActivateSessionCommand.Execute(_viewModel.SelectedSession);
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v != Visibility.Visible;
            return false;
        }
    }

    public class BoolToBoldConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return FontWeights.Bold;
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FontWeight fw)
                return fw == FontWeights.Bold;
            return false;
        }
    }
}
