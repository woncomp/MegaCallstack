using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.ViewModels
{
    public class MegaCallstackViewModel : INotifyPropertyChanged
    {
        private readonly CallstackManager _manager;
        private readonly IColorPickerService _colorPickerService;
        private readonly INoteEditorService _noteEditorService;

        private ObservableCollection<TreeViewNode> _treeNodes = new ObservableCollection<TreeViewNode>();
        private ObservableCollection<TreeViewNode> _displayTreeNodes = new ObservableCollection<TreeViewNode>();
        private TreeViewNode _selectedNode;
        private string _searchText;
        private List<TreeViewNode> _searchMatches = new List<TreeViewNode>();
        private int _currentMatchIndex = -1;
        private bool _isTreeViewMode = true;
        private string _activeSessionName;
        private bool _isRenaming;
        private string _renameText;
        private CallstackSession _activeSession;
        private CallstackSession _selectedSession;
        private string _sessionFilterText;

        public CallstackManager Manager => _manager;
        public CallstackSession ActiveSession => _activeSession;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string, int> NavigateToFile;
        public event Action TreeUpdated;

        public MegaCallstackViewModel(CallstackManager manager, IColorPickerService colorPickerService, INoteEditorService noteEditorService)
        {
            _manager = manager;
            _colorPickerService = colorPickerService;
            _noteEditorService = noteEditorService;

            CaptureCommand = new RelayCommand(ExecuteCapture, CanCapture);
            SearchCommand = new RelayCommand(ExecuteSearch);
            PrevMatchCommand = new RelayCommand(ExecutePrevMatch, CanNavigateMatches);
            NextMatchCommand = new RelayCommand(ExecuteNextMatch, CanNavigateMatches);
            DoubleClickNodeCommand = new RelayCommand(ExecuteDoubleClickNode);
            JumpToCallerCommand = new RelayCommand(ExecuteJumpToCaller, CanJumpToCaller);
            JumpToFrameCommand = new RelayCommand(ExecuteJumpToFrame, CanJumpToFrame);
            SetColorCommand = new RelayCommand(ExecuteSetColor);
            ClearColorCommand = new RelayCommand(ExecuteClearColor);
            AddNoteCommand = new RelayCommand(ExecuteAddNote);
            EditNoteCommand = new RelayCommand<NodeNote>(ExecuteEditNote);
            ToggleAncestorsCommand = new RelayCommand(ExecuteToggleAncestors, CanToggleAncestors);
            SwitchToSessionViewCommand = new RelayCommand(ExecuteSwitchToSessionView);
            SwitchToTreeViewCommand = new RelayCommand(ExecuteSwitchToTreeView);
            CreateSessionCommand = new RelayCommand(ExecuteCreateSession);
            ActivateSessionCommand = new RelayCommand<CallstackSession>(ExecuteActivateSession);
            StartRenameCommand = new RelayCommand(ExecuteStartRename);
            ConfirmRenameCommand = new RelayCommand(ExecuteConfirmRename);
            CancelRenameCommand = new RelayCommand(ExecuteCancelRename);
            DeleteSessionCommand = new RelayCommand<CallstackSession>(ExecuteDeleteSession);
            DeleteSelectedSessionCommand = new RelayCommand(ExecuteDeleteSelectedSession, CanDeleteSelectedSession);
        }

        public ObservableCollection<TreeViewNode> TreeNodes
        {
            get => _treeNodes;
            set { _treeNodes = value; OnPropertyChanged(); }
        }

       public ObservableCollection<TreeViewNode> DisplayTreeNodes
       {
           get => _displayTreeNodes;
           set { _displayTreeNodes = value; OnPropertyChanged(); }
       }

        public bool IsSelectedNodeDisplayRoot => SelectedNode?.IsDisplayRoot ?? false;

       public TreeViewNode SelectedNode
       {
            get => _selectedNode;
            set
            {
                if (_selectedNode != value)
                {
                    if (_selectedNode != null)
                        _selectedNode.IsSelected = false;

                    _selectedNode = value;

                   if (_selectedNode != null)
                       _selectedNode.IsSelected = true;

                   UpdatePathBolding();
                   OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSelectedNodeDisplayRoot));
               }
           }
       }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); }
        }

        public bool IsTreeViewMode
        {
            get => _isTreeViewMode;
            set
            {
                _isTreeViewMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSessionViewMode));
                OnPropertyChanged(nameof(IsHomePageVisible));
                OnPropertyChanged(nameof(IsTreeViewContentVisible));
            }
        }

        public bool IsSessionViewMode => !IsTreeViewMode;

        public string ActiveSessionName
        {
            get => _activeSessionName;
            set { _activeSessionName = value; OnPropertyChanged(); }
        }

        public bool HasActiveSession => _activeSession != null;

        public CallstackSession PreviousSession
        {
            get => _manager.GetLastActiveSession();
        }

        public bool CanResumePreviousSession => PreviousSession != null;

        public string PreviousSessionName => string.IsNullOrWhiteSpace(PreviousSession?.Name) ? "Untitled Session" : PreviousSession.Name;

        public bool HasAnySessions => _manager.HasAnySessions;

        public bool IsHomePageVisible => IsTreeViewMode && !HasActiveSession;

        public bool IsTreeViewContentVisible => IsTreeViewMode && HasActiveSession;

        public string CaptureButtonTooltip
        {
            get
            {
                if (CanCapture())
                    return "Click to capture the current callstack.";
                return "Stop at a breakpoint to capture a callstack.";
            }
        }

        public CallstackSession SelectedSession
        {
            get => _selectedSession;
            set { _selectedSession = value; OnPropertyChanged(); }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            set { _isRenaming = value; OnPropertyChanged(); }
        }

        public string RenameText
        {
            get => _renameText;
            set { _renameText = value; OnPropertyChanged(); }
        }

        public string SessionFilterText
        {
            get => _sessionFilterText;
            set
            {
                _sessionFilterText = value;
                OnPropertyChanged();
                ApplySessionFilter();
            }
        }

        public ObservableCollection<CallstackSession> Sessions { get; } = new ObservableCollection<CallstackSession>();
        public ObservableCollection<CallstackSession> FilteredSessions { get; } = new ObservableCollection<CallstackSession>();

        public ICommand CaptureCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand PrevMatchCommand { get; }
        public ICommand NextMatchCommand { get; }
        public ICommand DoubleClickNodeCommand { get; }
        public ICommand JumpToCallerCommand { get; }
        public ICommand JumpToFrameCommand { get; }
        public ICommand SetColorCommand { get; }
        public ICommand ClearColorCommand { get; }
        public ICommand AddNoteCommand { get; }
        public ICommand EditNoteCommand { get; }
        public ICommand ToggleAncestorsCommand { get; }
        public ICommand SwitchToSessionViewCommand { get; }
        public ICommand SwitchToTreeViewCommand { get; }
        public ICommand CreateSessionCommand { get; }
        public ICommand ActivateSessionCommand { get; }
        public ICommand StartRenameCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand DeleteSessionCommand { get; }
        public ICommand DeleteSelectedSessionCommand { get; }

        public Task LoadDataAsync()
        {
            _activeSession = null;
            ActiveSessionName = string.Empty;

            NotifyHomePageProperties();
            RefreshTreeNodes();
            RefreshSessionsList();
            return Task.CompletedTask;
        }

        private void NotifyHomePageProperties()
        {
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(PreviousSession));
            OnPropertyChanged(nameof(PreviousSessionName));
            OnPropertyChanged(nameof(CanResumePreviousSession));
            OnPropertyChanged(nameof(HasAnySessions));
            OnPropertyChanged(nameof(IsHomePageVisible));
            OnPropertyChanged(nameof(IsTreeViewContentVisible));
            OnPropertyChanged(nameof(CaptureButtonTooltip));
        }

        private void RefreshTreeNodes()
        {
            var nodes = _manager.BuildTreeNodes(_activeSession);
            TreeNodes = new ObservableCollection<TreeViewNode>(nodes);

            var displayNodes = _manager.BuildDisplayTreeNodes(_activeSession, nodes);
            DisplayTreeNodes = new ObservableCollection<TreeViewNode>(displayNodes);

            TreeUpdated?.Invoke();
        }

        private void RefreshSessionsList()
        {
            Sessions.Clear();
            foreach (var session in _manager.SessionData.Sessions)
            {
                Sessions.Add(session);
            }
            OnPropertyChanged(nameof(HasAnySessions));
            OnPropertyChanged(nameof(PreviousSession));
            OnPropertyChanged(nameof(PreviousSessionName));
            OnPropertyChanged(nameof(CanResumePreviousSession));
            ApplySessionFilter();
        }

        private async Task EnsureSessionLoadedAsync(CallstackSession session)
        {
            if (session != null && !session.IsLoaded)
            {
                await _manager.LoadSessionDetailsAsync(session);
            }
        }

        private void ApplySessionFilter()
        {
            FilteredSessions.Clear();
            var filter = _sessionFilterText?.ToLowerInvariant() ?? string.Empty;
            foreach (var session in Sessions)
            {
                if (string.IsNullOrEmpty(filter) ||
                    (session.Name != null && session.Name.ToLowerInvariant().Contains(filter)))
                {
                    FilteredSessions.Add(session);
                }
            }
        }

        private async void ExecuteCapture()
        {
            var callstack = await _manager.CaptureCurrentCallstackAsync();
            if (callstack == null)
                return;

            if (_activeSession == null)
            {
                var leafFrame = callstack.Frames.LastOrDefault();
                var sessionName = !string.IsNullOrWhiteSpace(leafFrame?.FunctionName) ? leafFrame.FunctionName : "New Session";
                _activeSession = _manager.CreateSession(sessionName);
                _manager.SetActiveSession(_activeSession.Id);
                ActiveSessionName = _activeSession.Name;
                NotifyHomePageProperties();

                await EnsureSessionLoadedAsync(_activeSession);
            }

            _manager.AddOrUpdateCallstack(_activeSession, callstack);
            await _manager.SaveSessionMetadataAsync(_activeSession);
            await _manager.SaveCallstacksAsync(_activeSession);

            RefreshTreeNodes();
            RefreshSessionsList();
        }

        private bool CanCapture()
        {
            return _manager.IsDebuggerInBreakMode;
        }

        private void ExecuteSearch()
        {
            _searchMatches.Clear();
            _currentMatchIndex = -1;

            if (string.IsNullOrWhiteSpace(SearchText))
                return;

            var searchLower = SearchText.ToLower();
            foreach (var rootNode in TreeNodes)
            {
                CollectMatchingNodes(rootNode, searchLower, _searchMatches);
            }

            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                NavigateToMatch(_searchMatches[0]);
            }
        }

        private void CollectMatchingNodes(TreeViewNode node, string searchText, List<TreeViewNode> matches)
        {
            if (node.DisplayText != null && node.DisplayText.ToLower().Contains(searchText))
            {
                matches.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectMatchingNodes(child, searchText, matches);
            }
        }

        private bool CanNavigateMatches()
        {
            return _searchMatches.Count > 0;
        }

        private void ExecutePrevMatch()
        {
            if (_searchMatches.Count == 0)
                return;

            _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            NavigateToMatch(_searchMatches[_currentMatchIndex]);
        }

        private void ExecuteNextMatch()
        {
            if (_searchMatches.Count == 0)
                return;

            _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
            NavigateToMatch(_searchMatches[_currentMatchIndex]);
        }

        private void NavigateToMatch(TreeViewNode node)
        {
            ExpandAncestors(node);
            SelectedNode = node;
        }

        private void ExpandAncestors(TreeViewNode node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                parent.IsExpanded = true;
                parent = parent.Parent;
            }
        }

        private void ExecuteDoubleClickNode()
        {
            if (CanJumpToCaller())
                ExecuteJumpToCaller();
        }

        private bool CanJumpToCaller()
        {
            return SelectedNode?.Parent?.Frame != null;
        }

        private async void ExecuteJumpToCaller()
        {
            var parent = SelectedNode?.Parent;
            if (parent?.Frame != null)
            {
                int line = await _manager.ResolveFrameLineNumberAsync(parent.Frame);
                NavigateToFile?.Invoke(parent.Frame.FileName, line);
            }
        }

        private bool CanJumpToFrame()
        {
            return SelectedNode?.Frame != null && !SelectedNode.IsLeaf;
        }

        private async void ExecuteJumpToFrame()
        {
            if (SelectedNode?.Frame != null)
            {
                int line = await _manager.ResolveFrameLineNumberAsync(SelectedNode.Frame);
                NavigateToFile?.Invoke(SelectedNode.Frame.FileName, line);
            }
        }

        private void ExecuteSetColor()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            Color? initialColor = null;
            if (SelectedNode.DisplayBackground is SolidColorBrush solidBrush)
            {
                initialColor = solidBrush.Color;
            }

            var result = _colorPickerService.PickColor(initialColor);

            if (result.Result == ColorPickerResult.Cancel)
                return;

            if (result.Result == ColorPickerResult.Clear)
            {
                ExecuteClearColor();
                return;
            }

            if (!result.Color.HasValue)
                return;

            var color = result.Color.Value;
            var hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            var brush = new SolidColorBrush(color);

            SelectedNode.SetColorAndPropagate(brush);

            if (SelectedNode.Frame != null)
            {
                _activeSession.NodeColors[SelectedNode.MergeId] = hexColor;
                SaveStateAsync();
            }
        }

        private void ExecuteClearColor()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            if (SelectedNode.Frame != null)
            {
                _activeSession.NodeColors.Remove(SelectedNode.MergeId);
                SaveStateAsync();
            }

            SelectedNode.ClearColorAndPropagate();
        }

        private void ExecuteAddNote()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            var result = _noteEditorService.EditNote(null);
            if (!result.IsConfirmed || result.Note == null)
                return;

            SelectedNode.Notes.Add(result.Note);

            var key = SelectedNode.NodeKey;
            if (!_activeSession.NodeNotes.ContainsKey(key))
                _activeSession.NodeNotes[key] = new System.Collections.Generic.List<NodeNote>();
            _activeSession.NodeNotes[key].Add(result.Note);

            SaveNotesAsync();
        }

        private void ExecuteEditNote(NodeNote note)
        {
            if (note == null || SelectedNode == null || _activeSession == null)
                return;

            var result = _noteEditorService.EditNote(note);
            if (!result.IsConfirmed)
                return;

            var key = SelectedNode.NodeKey;
            var sessionNotes = _activeSession.NodeNotes.ContainsKey(key)
                ? _activeSession.NodeNotes[key]
                : null;

            if (result.IsDeleted)
            {
                SelectedNode.Notes.Remove(note);
                sessionNotes?.Remove(note);
            }
            else if (result.Note != null)
            {
                note.Emoji = result.Note.Emoji;
                note.Text = result.Note.Text;
            }

            SaveNotesAsync();
        }

        private async void SaveNotesAsync()
        {
            await _manager.SaveNotesAsync(_activeSession);
        }

       private bool CanToggleAncestors()
       {
           if (SelectedNode == null || _activeSession == null)
               return false;

            if (SelectedNode.IsDisplayRoot)
                return true;

           return _manager.CanHideAncestors(SelectedNode);
       }

       private async void ExecuteToggleAncestors()
       {
           if (SelectedNode == null || _activeSession == null)
               return;

            if (SelectedNode.IsDisplayRoot)
           {
               _manager.ClearHiddenAncestorsForPath(_activeSession, SelectedNode);
           }
           else
           {
               _manager.SetHiddenAncestors(_activeSession, SelectedNode);
           }

            await _manager.SaveStateAsync(_activeSession);
            RefreshTreeNodes();
        }

        private async void SaveStateAsync()
        {
            await _manager.SaveStateAsync(_activeSession);
        }

        private void UpdatePathBolding()
        {
            foreach (var rootNode in TreeNodes)
            {
                ClearBoldRecursive(rootNode);
            }

            if (_selectedNode != null)
            {
                _selectedNode.SetPathBold(true);
            }
        }

        private void ClearBoldRecursive(TreeViewNode node)
        {
            node.IsBold = false;
            foreach (var child in node.Children)
            {
                ClearBoldRecursive(child);
            }
        }

        private void ExecuteSwitchToSessionView()
        {
            IsTreeViewMode = false;
            RefreshSessionsList();
        }

        private void ExecuteSwitchToTreeView()
        {
            IsTreeViewMode = true;
        }

        private async void ExecuteCreateSession()
        {
            var callstack = await _manager.CaptureCurrentCallstackAsync();
            if (callstack == null)
                return;

            var leafFrame = callstack.Frames.LastOrDefault();
            var sessionName = !string.IsNullOrWhiteSpace(leafFrame?.FunctionName) ? leafFrame.FunctionName : "New Session";
            var session = _manager.CreateSession(sessionName);
            _manager.SetActiveSession(session.Id);
            _activeSession = session;
            ActiveSessionName = session.Name;
            NotifyHomePageProperties();

            await EnsureSessionLoadedAsync(session);

            _manager.AddOrUpdateCallstack(session, callstack);
            await _manager.SaveSessionMetadataAsync(session);
            await _manager.SaveCallstacksAsync(session);

            RefreshTreeNodes();
            RefreshSessionsList();

            IsTreeViewMode = true;
        }

        private async void ExecuteActivateSession(CallstackSession session)
        {
            if (session == null)
                return;

            _manager.SetActiveSession(session.Id);
            _activeSession = session;
            ActiveSessionName = session.Name;
            NotifyHomePageProperties();

            await EnsureSessionLoadedAsync(session);

            RefreshTreeNodes();
            RefreshSessionsList();

            IsTreeViewMode = true;
        }

        private void ExecuteStartRename()
        {
            RenameText = ActiveSessionName;
            IsRenaming = true;
        }

        private async void ExecuteConfirmRename()
        {
            if (_activeSession != null && !string.IsNullOrWhiteSpace(RenameText))
            {
                _activeSession.Name = RenameText;
                ActiveSessionName = RenameText;
                await _manager.SaveSessionMetadataAsync(_activeSession);
                RefreshSessionsList();
            }
            IsRenaming = false;
        }

        private void ExecuteCancelRename()
        {
            IsRenaming = false;
        }

        private void ExecuteDeleteSession(CallstackSession session)
        {
            if (session == null)
                return;

            _manager.DeleteSession(session);

            if (_activeSession == session)
            {
            _activeSession = null;
            ActiveSessionName = string.Empty;
            NotifyHomePageProperties();
            RefreshTreeNodes();
        }

        RefreshSessionsList();
    }

    private bool CanDeleteSelectedSession()
    {
        return _selectedSession != null;
    }

        private void ExecuteDeleteSelectedSession()
        {
            if (_selectedSession == null)
                return;

            ExecuteDeleteSession(_selectedSession);
            _selectedSession = null;
            OnPropertyChanged(nameof(SelectedSession));
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute?.Invoke();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;
        public void Execute(object parameter) => _execute?.Invoke((T)parameter);
    }
}
