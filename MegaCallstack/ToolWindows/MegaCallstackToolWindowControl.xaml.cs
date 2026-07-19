using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MegaCallstack.Models;
using MegaCallstack.Dialogs;
using MegaCallstack.Services;
using MegaCallstack.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace MegaCallstack.ToolWindows
{
    public partial class MegaCallstackToolWindowControl : UserControl
    {
        private ISolutionInfoProvider _solutionInfoProvider;
        private SolutionWorkspace _workspace;

        public MegaCallstackToolWindowControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_solutionInfoProvider != null)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
            Logger.Log("ToolWindow: Initializing");

            _solutionInfoProvider = new SolutionInfoProvider(dte);
            _solutionInfoProvider.CurrentChanged += OnSolutionInfoChanged;

            ApplySolutionInfo(_solutionInfoProvider.Current);
        }

        private void OnSolutionInfoChanged(object sender, EventArgs e)
        {
            ApplySolutionInfo(_solutionInfoProvider.Current);
        }

        private async void ApplySolutionInfo(SolutionInfo info)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _workspace?.Dispose();
            _workspace = null;

            if (info == null)
            {
                Logger.Log("ToolWindow: No solution loaded, showing empty workspace");
                Content = new EmptyWorkspaceView();
                return;
            }

            Logger.Log($"ToolWindow: Creating workspace for {info.FullPath}");

            var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
            string diagnosticsDirectory = Path.Combine(info.DataDirectory, Constants.DiagnosticsFolderName);
            var diagnostics = new FuzzyBookmarkFileDiagnostics(diagnosticsDirectory);
            var bookmarkEngine = new FuzzyBookmarkEngine(diagnostics);
            var bookmarkResolver = new BookmarkResolver(bookmarkEngine);
            var captureService = new CallstackCaptureService(dte, info.UserCodeRoots, bookmarkEngine, bookmarkResolver);
            var treeBuilder = new CallstackTreeBuilder();
            var repository = new SessionRepository(info);
            var window = Window.GetWindow(this);

            _workspace = new SolutionWorkspace(
                info,
                repository,
                captureService,
                bookmarkResolver,
                treeBuilder,
                new WpfColorPickerService(window),
                new WpfNoteEditorService(window),
                window);

            var workspaceView = new WorkspaceView();
            workspaceView.NavigateToFile += OnNavigateToFile;
            Content = workspaceView;

            await _workspace.InitializeAsync();
            workspaceView.DataContext = _workspace.ViewModel;

            GotFocus += OnToolWindowGotFocus;
        }

        private DateTime _lastFocusCheck = DateTime.MinValue;

        private async void OnToolWindowGotFocus(object sender, RoutedEventArgs e)
        {
            if (_workspace?.ViewModel == null)
                return;

            var now = DateTime.Now;
            if ((now - _lastFocusCheck).TotalSeconds < 1.0)
                return;
            _lastFocusCheck = now;

            try
            {
                await _workspace.ViewModel.CheckFilesAndResolveAsync();
            }
            catch (Exception ex)
            {
                Logger.Log($"ToolWindow: Focus resolution failed: {ex.Message}");
            }
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
    }
}
