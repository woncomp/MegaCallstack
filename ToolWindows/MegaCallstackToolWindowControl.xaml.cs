using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MegaCallstack.Models;
using MegaCallstack.Services;
using MegaCallstack.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace MegaCallstack.ToolWindows
{
    public partial class MegaCallstackToolWindowControl : UserControl
    {
        private MegaCallstackViewModel _viewModel;
        private CallstackManager _manager;
        // EnvDTE event objects are kept alive by this reference; without it the
        // GC can collect them and the Opened event stops firing.
        private EnvDTE.Events _dteEvents;
        private EnvDTE.SolutionEvents _solutionEvents;

        public MegaCallstackToolWindowControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
            string solutionName = "none";
            try { solutionName = dte?.Solution?.FullName ?? "none"; } catch { }
            Logger.Log($"ToolWindow: Initializing, solution={solutionName}");

            _manager = new CallstackManager(dte);
            await _manager.LoadDataAsync();

            // Compute user-code roots before any capture so trimming has an
            // accurate picture of where the solution's code actually lives
            // (which may be outside the .sln directory).
            await _manager.ComputeSolutionRootsAsync();
            HookSolutionEvents(dte);
            Logger.Log("ToolWindow: LoadData and root detection complete");

            _viewModel = new MegaCallstackViewModel(_manager);
            _viewModel.NavigateToFile += OnNavigateToFile;
            _viewModel.TreeUpdated += OnTreeUpdated;
            DataContext = _viewModel;

            _viewModel.LoadData();
        }

        /// <summary>
        /// Subscribes to solution-open events so the user-code roots are
        /// recomputed when the user switches solutions after the window is
        /// already open. Holds the events object references to prevent GC.
        /// </summary>
        private void HookSolutionEvents(EnvDTE.DTE dte)
        {
            if (dte == null)
            {
                Logger.Log("ToolWindow: No DTE, skipping solution-event subscription");
                return;
            }

            try
            {
                _dteEvents = dte.Events;
                _solutionEvents = _dteEvents.SolutionEvents;
                _solutionEvents.Opened += OnSolutionOpened;
                Logger.Log("ToolWindow: Subscribed to solution-open events");
            }
            catch (Exception ex)
            {
                Logger.Error("ToolWindow: Failed to subscribe to solution events", ex);
            }
        }

        private void OnSolutionOpened()
        {
            Logger.Log("ToolWindow: Solution reopened, recomputing user-code roots");
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_manager != null)
                {
                    await _manager.ComputeSolutionRootsAsync();
                }
            });
        }

        private void OnNavigateToFile(string fileName, int lineNumber)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
                    if (dte != null && !string.IsNullOrEmpty(fileName))
                    {
                        var window = dte.ItemOperations.OpenFile(fileName);
                        if (window != null)
                        {
                            var selection = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
                            selection?.GotoLine(lineNumber, true);
                        }
                    }
                }
                catch
                {
                }
            });
        }

        private void OnTreeUpdated()
        {
            MainTreeView.ItemsSource = _viewModel?.TreeNodes;
        }

        private void TreeView_Selected(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeViewNode node)
            {
                if (_viewModel != null)
                    _viewModel.SelectedNode = node;
            }
        }

        private void TreeViewItem_DoubleClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.DoubleClickNodeCommand.Execute(null);
            }
        }

        private void TreeViewItem_ExpandedCollapsed(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi && tvi.DataContext is TreeViewNode node && _viewModel != null)
            {
                if (node.Frame != null && _viewModel.ActiveSession != null)
                {
                    _viewModel.Manager.SaveExpansionState(_viewModel.ActiveSession, node.MergeId, tvi.IsExpanded);
                }
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _viewModel?.ConfirmRenameCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                _viewModel?.CancelRenameCommand.Execute(null);
            }
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
            {
                _viewModel.DeleteSelectedSessionCommand.Execute(null);
            }
        }

        private void SessionViewContent_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel?.SelectedSession != null)
            {
                _viewModel.ActivateSessionCommand.Execute(_viewModel.SelectedSession);
            }
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
