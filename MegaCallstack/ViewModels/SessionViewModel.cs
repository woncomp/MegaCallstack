using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.ViewModels
{
    public class SessionViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SolutionInfo _solutionInfo;
        private readonly SolutionSessionData _sessionData;
        private readonly ISessionRepository _repository;
        private readonly ICallstackCaptureService _captureService;
        private readonly IBookmarkResolver _bookmarkResolver;
        private readonly ICallstackTreeBuilder _treeBuilder;
        private readonly IColorPickerService _colorPickerService;
        private readonly INoteEditorService _noteEditorService;
        private readonly Window _window;

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

        public SolutionInfo SolutionInfo => _solutionInfo;
        public CallstackSession ActiveSession => _activeSession;
        public ISessionRepository Repository => _repository;
        public ICallstackTreeBuilder TreeBuilder => _treeBuilder;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string, int> NavigateToFile;
        public event Action TreeUpdated;

        public SessionViewModel(
            SolutionInfo solutionInfo,
            SolutionSessionData sessionData,
            ISessionRepository repository,
            ICallstackCaptureService captureService,
            IBookmarkResolver bookmarkResolver,
            ICallstackTreeBuilder treeBuilder,
            IColorPickerService colorPickerService,
            INoteEditorService noteEditorService,
            Window window)
        {
            _solutionInfo = solutionInfo;
            _sessionData = sessionData;
            _repository = repository;
            _captureService = captureService;
            _bookmarkResolver = bookmarkResolver;
            _treeBuilder = treeBuilder;
            _colorPickerService = colorPickerService;
            _noteEditorService = noteEditorService;
            _window = window;

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
            ResetActiveSessionCommand = new RelayCommand(ExecuteResetActiveSession);
            ActivateSessionCommand = new RelayCommand<CallstackSession>(ExecuteActivateSession);
            StartRenameCommand = new RelayCommand(ExecuteStartRename);
            ConfirmRenameCommand = new RelayCommand(ExecuteConfirmRename);
            CancelRenameCommand = new RelayCommand(ExecuteCancelRename);
            DeleteSessionCommand = new RelayCommand<CallstackSession>(ExecuteDeleteSession);
            DeleteSelectedSessionCommand = new RelayCommand(ExecuteDeleteSelectedSession, CanDeleteSelectedSession);

            _captureService.DebuggerStateChanged += OnDebuggerStateChanged;
        }

        public void Dispose()
        {
            _captureService.DebuggerStateChanged -= OnDebuggerStateChanged;
        }

        private void OnDebuggerStateChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(CaptureButtonTooltip));
            CommandManager.InvalidateRequerySuggested();
        }

        public Task LoadDataAsync()
        {
            _activeSession = null;
            ActiveSessionName = string.Empty;

            NotifyHomePageProperties();
            RefreshTreeNodes();
            RefreshSessionsList();
            return Task.CompletedTask;
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
            get => _sessionData.PreviousSessionId != null
                ? _sessionData.Sessions.FirstOrDefault(s => s.Id == _sessionData.PreviousSessionId)
                : null;
        }

        public bool CanResumePreviousSession => PreviousSession != null;

        public string PreviousSessionName => string.IsNullOrWhiteSpace(PreviousSession?.Name) ? "Untitled Session" : PreviousSession.Name;

        public bool HasAnySessions => _sessionData.Sessions.Count > 0;

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
        public ICommand ResetActiveSessionCommand { get; }
        public ICommand ActivateSessionCommand { get; }
        public ICommand StartRenameCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand DeleteSessionCommand { get; }
        public ICommand DeleteSelectedSessionCommand { get; }

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

        public void RefreshTreeNodes()
        {
            var nodes = _treeBuilder.BuildTreeNodes(_activeSession);
            TreeNodes = new ObservableCollection<TreeViewNode>(nodes);

            var displayNodes = _treeBuilder.BuildDisplayTreeNodes(_activeSession, nodes);
            DisplayTreeNodes = new ObservableCollection<TreeViewNode>(displayNodes);

            TreeUpdated?.Invoke();
        }

        private void RefreshSessionsList()
        {
            Sessions.Clear();
            foreach (var session in _sessionData.Sessions)
                Sessions.Add(session);

            OnPropertyChanged(nameof(HasAnySessions));
            OnPropertyChanged(nameof(PreviousSession));
            OnPropertyChanged(nameof(PreviousSessionName));
            OnPropertyChanged(nameof(CanResumePreviousSession));
            ApplySessionFilter();
        }

        private async Task EnsureSessionLoadedAsync(CallstackSession session)
        {
            if (session != null && !session.IsLoaded)
                await _repository.LoadSessionDetailsAsync(session);
        }

        public async Task CheckFilesAndResolveAsync()
        {
            if (_activeSession == null || _bookmarkResolver == null)
                return;

            var files = _activeSession.Callstacks
                .SelectMany(c => c.Frames)
                .Where(f => f.Bookmark != null && !string.IsNullOrEmpty(f.FileName))
                .Select(f => f.FileName)
                .Distinct()
                .ToList();

            var changed = new List<string>();
            foreach (var file in files)
            {
                if (!File.Exists(file))
                    continue;
                long currentTicks = File.GetLastWriteTimeUtc(file).Ticks;
                if (!_activeSession.ResolvedFileWriteTimes.TryGetValue(file, out long storedTicks) || currentTicks != storedTicks)
                    changed.Add(file);
            }

            if (changed.Count == 0)
                return;

            await _bookmarkResolver.ResolveFilesAsync(changed, _activeSession);
            RefreshTreeNodes();
            await _repository.SaveStateAsync(_activeSession);
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
            var callstack = await _captureService.CaptureCurrentCallstackAsync();
            if (callstack == null)
                return;

            if (_activeSession == null)
            {
                var leafFrame = callstack.Frames.LastOrDefault();
                var sessionName = !string.IsNullOrWhiteSpace(leafFrame?.FunctionName) ? leafFrame.FunctionName : "New Session";
                _activeSession = CreateSession(sessionName);
                SetActiveSession(_activeSession);
                ActiveSessionName = _activeSession.Name;
                NotifyHomePageProperties();

                await EnsureSessionLoadedAsync(_activeSession);
            }

            AddOrUpdateCallstack(_activeSession, callstack);
            await _repository.SaveSessionMetadataAsync(_activeSession);
            await _repository.SaveCallstacksAsync(_activeSession);

            RefreshTreeNodes();
            RefreshSessionsList();
        }

        private bool CanCapture()
        {
            return _captureService.IsDebuggerInBreakMode;
        }

        private void ExecuteSearch()
        {
            _searchMatches.Clear();
            _currentMatchIndex = -1;

            if (string.IsNullOrWhiteSpace(SearchText))
                return;

            var searchLower = SearchText.ToLower();
            foreach (var rootNode in TreeNodes)
                CollectMatchingNodes(rootNode, searchLower, _searchMatches);

            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                NavigateToMatch(_searchMatches[0]);
            }
        }

        private void CollectMatchingNodes(TreeViewNode node, string searchText, List<TreeViewNode> matches)
        {
            if (node.DisplayText != null && node.DisplayText.ToLower().Contains(searchText))
                matches.Add(node);

            foreach (var child in node.Children)
                CollectMatchingNodes(child, searchText, matches);
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
                NavigateToFile?.Invoke(parent.Frame.FileName, parent.Frame.LineNumber);
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
                NavigateToFile?.Invoke(SelectedNode.Frame.FileName, SelectedNode.Frame.LineNumber);
            }
        }

        private void ExecuteSetColor()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            Color? initialColor = null;
            if (SelectedNode.DisplayForeground is SolidColorBrush solidBrush)
                initialColor = solidBrush.Color;

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
                _activeSession.NodeColors[SelectedNode.NodeKey] = hexColor;
                SaveStateAsync();
            }
        }

        private void ExecuteClearColor()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            if (SelectedNode.Frame != null)
            {
                _activeSession.NodeColors.Remove(SelectedNode.NodeKey);
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
                _activeSession.NodeNotes[key] = new List<NodeNote>();
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
            await _repository.SaveNotesAsync(_activeSession);
        }

        private bool CanToggleAncestors()
        {
            if (SelectedNode == null || _activeSession == null)
                return false;

            if (SelectedNode.IsDisplayRoot)
                return true;

            return _treeBuilder.CanHideAncestors(SelectedNode);
        }

        private async void ExecuteToggleAncestors()
        {
            if (SelectedNode == null || _activeSession == null)
                return;

            if (SelectedNode.IsDisplayRoot)
                _treeBuilder.ClearHiddenAncestorsForPath(_activeSession, SelectedNode);
            else
                _treeBuilder.SetHiddenAncestors(_activeSession, SelectedNode);

            await _repository.SaveStateAsync(_activeSession);
            RefreshTreeNodes();
        }

        private async void SaveStateAsync()
        {
            await _repository.SaveStateAsync(_activeSession);
        }

        private void UpdatePathBolding()
        {
            foreach (var rootNode in TreeNodes)
                ClearBoldRecursive(rootNode);

            if (_selectedNode != null)
                _selectedNode.SetPathBold(true);
        }

        private void ClearBoldRecursive(TreeViewNode node)
        {
            node.IsBold = false;
            foreach (var child in node.Children)
                ClearBoldRecursive(child);
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

        private void ExecuteResetActiveSession()
        {
            _activeSession = null;
            ActiveSessionName = string.Empty;
            NotifyHomePageProperties();
            RefreshTreeNodes();
            RefreshSessionsList();
            IsTreeViewMode = true;
        }

        private async void ExecuteActivateSession(CallstackSession session)
        {
            if (session == null)
                return;

            SetActiveSession(session);
            _activeSession = session;
            ActiveSessionName = session.Name;
            NotifyHomePageProperties();

            await EnsureSessionLoadedAsync(session);
            if (_bookmarkResolver != null)
                await _bookmarkResolver.ResolveSessionAsync(session);

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
                await _repository.SaveSessionMetadataAsync(_activeSession);
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

            _sessionData.Sessions.Remove(session);
            DeleteSessionFolder(session);

            if (_activeSession == session)
            {
                _activeSession = null;
                ActiveSessionName = string.Empty;
                NotifyHomePageProperties();
                RefreshTreeNodes();
            }

            RefreshSessionsList();
        }

        private void DeleteSessionFolder(CallstackSession session)
        {
            var folder = _repository.GetSessionFolderPath(session);
            if (folder != null && Directory.Exists(folder))
            {
                try
                {
                    Directory.Delete(folder, true);
                    Logger.Log($"SessionViewModel: Deleted folder {folder}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"SessionViewModel: Failed to delete folder {folder}", ex);
                }
            }
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

        public CallstackSession CreateSession(string name)
        {
            var session = new CallstackSession(name)
            {
                FolderName = _repository.GenerateSessionFolderName()
            };
            _sessionData.Sessions.Add(session);
            return session;
        }

        public void SetActiveSession(CallstackSession session)
        {
            _sessionData.PreviousSessionId = session?.Id;
            _repository.SavePreviousSessionIdAsync(_sessionData.PreviousSessionId).ConfigureAwait(false);
        }

        public void AddOrUpdateCallstack(CallstackSession session, CallstackData callstack)
        {
            var existing = session.Callstacks.FirstOrDefault(c => c.LeafHashCode == callstack.LeafHashCode);
            if (existing != null)
            {
                var index = session.Callstacks.IndexOf(existing);
                session.Callstacks[index] = callstack;
            }
            else
            {
                session.Callstacks.Add(callstack);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
