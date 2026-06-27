using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE90a;
using MegaCallstack.Models;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace MegaCallstack.Services
{
    public class CallstackManager
    {
        private readonly DTE _dte;
        private SolutionSessionData _sessionData;
        private string _dataDirectory;
        private List<string> _userCodeRoots;

        private static readonly Random _random = new Random();

        public SolutionSessionData SessionData => _sessionData;

        // EnvDTE event objects are kept alive by this reference; without it the
        // GC can collect them and the break/run/design mode events stop firing.
        private EnvDTE.Events _dteEvents;
        private EnvDTE.DebuggerEvents _debuggerEvents;

        public CallstackManager(DTE dte)
        {
            _dte = dte;
            _sessionData = new SolutionSessionData();
            HookDebuggerEvents(dte);
        }

        public string GetSolutionDirectory()
        {
            try
            {
                if (_dte?.Solution?.FullName != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                {
                    return Path.GetDirectoryName(_dte.Solution.FullName);
                }
            }
            catch
            {
            }
            return null;
        }

        private string GetSolutionName()
        {
            try
            {
                if (_dte?.Solution?.FileName != null && !string.IsNullOrEmpty(_dte.Solution.FileName))
                {
                    return Path.GetFileNameWithoutExtension(_dte.Solution.FileName);
                }
            }
            catch
            {
            }
            return null;
        }

        private string GetDataDirectory()
        {
            var solutionDir = GetSolutionDirectory();
            var solutionName = GetSolutionName();
            if (solutionDir == null || solutionName == null)
                return null;

            return Path.Combine(solutionDir, ".vs", solutionName, Constants.DataFolderName);
        }

        private async Task SwitchToMainThreadIfNeededAsync()
        {
            if (_dte != null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
        }

        /// <summary>
        /// Solution folder project kind GUID. These are virtual containers in
        /// the solution, not real projects, so they have no files of their own.
        /// </summary>
        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        /// <summary>
        /// Computes the user-code root directories for the currently loaded
        /// solution by walking its project tree and running the root-detection
        /// algorithm. The result feeds <see cref="TrimToUserCode"/>. Safe to
        /// call with no solution (leaves roots unset, trimming falls back).
        /// </summary>
        public async Task ComputeSolutionRootsAsync()
        {
            await SwitchToMainThreadIfNeededAsync();

            if (_dte?.Solution == null || string.IsNullOrEmpty(_dte.Solution.FullName))
            {
                Logger.Log("ComputeSolutionRoots: No solution open, skipping");
                _userCodeRoots = null;
                return;
            }

            var files = CollectSolutionFilePaths();
            Logger.Log($"ComputeSolutionRoots: Collected {files.Count} file paths from solution");

            string[] fileArray = files.ToArray();
            var roots = await Task.Run(() => SolutionRootDetector.DetectProjectFolders(fileArray, Constants.MaxUserCodeRoots));

            _userCodeRoots = new List<string>();
            foreach (var root in roots)
            {
                var normalized = NormalizeRoot(root);
                if (normalized != null && !_userCodeRoots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    _userCodeRoots.Add(normalized);
                }
            }

            Logger.Log($"ComputeSolutionRoots: Detected {_userCodeRoots.Count} root(s): {string.Join("; ", _userCodeRoots)}");
        }

        /// <summary>
        /// Walks the solution's EnvDTE project tree (on the UI thread) and
        /// collects source file paths. Solution folders are recursed but
        /// contribute no files. Capped at <see cref="Constants.MaxSolutionFilesToScan"/>.
        /// </summary>
        private List<string> CollectSolutionFilePaths()
        {
            var files = new List<string>();
            try
            {
                var projects = _dte?.Solution?.Projects;
                if (projects == null)
                    return files;

                foreach (Project project in projects)
                {
                    CollectFromProject(project, files);
                    if (files.Count >= Constants.MaxSolutionFilesToScan)
                    {
                        Logger.Log($"CollectSolutionFilePaths: Hit cap of {Constants.MaxSolutionFilesToScan} files, stopping early");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CollectSolutionFilePaths: Failed to enumerate solution projects", ex);
            }
            return files;
        }

        private void CollectFromProject(Project project, List<string> files)
        {
            if (project == null)
                return;

            if (files.Count >= Constants.MaxSolutionFilesToScan)
                return;

            try
            {
                // The project file itself (e.g. .csproj) anchors the project
                // location even if its items fail to enumerate.
                if (!string.IsNullOrEmpty(project.FullName))
                {
                    files.Add(project.FullName);
                }
            }
            catch
            {
            }

            try
            {
                if (project.Kind != null &&
                    project.Kind.Equals(SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
                {
                    // Solution folder: recurse into its project items, which
                    // may contain nested projects via SubProject.
                    CollectFromProjectItems(project.ProjectItems, files);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                CollectFromProjectItems(project.ProjectItems, files);
            }
            catch
            {
            }
        }

        private void CollectFromProjectItems(ProjectItems items, List<string> files)
        {
            if (items == null)
                return;

            foreach (ProjectItem item in items)
            {
                if (files.Count >= Constants.MaxSolutionFilesToScan)
                    return;

                try
                {
                    // FileNames(1) is the 1-based first file of the item.
                    if (item.FileCount > 0)
                    {
                        var name = item.FileNames[1];
                        if (!string.IsNullOrEmpty(name))
                            files.Add(name);
                    }
                }
                catch
                {
                }

                try
                {
                    // Nested project (e.g. inside a solution folder).
                    var subProject = item.SubProject;
                    if (subProject != null)
                    {
                        CollectFromProject(subProject, files);
                    }
                }
                catch
                {
                }

                try
                {
                    // Recurse into folder items.
                    if (item.ProjectItems != null)
                    {
                        CollectFromProjectItems(item.ProjectItems, files);
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Normalizes a directory to a canonical absolute path with a trailing
        /// separator, suitable for prefix matching. Returns null if the path
        /// cannot be normalized.
        /// </summary>
        private static string NormalizeRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return null;

            try
            {
                return Path.GetFullPath(dir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the effective set of user-code root prefixes: the detected
        /// roots plus the solution directory (which preserves coverage for
        /// solutions whose code already lives under the .sln). Bare drive
        /// roots are excluded. Empty when there is no solution.
        /// </summary>
        private List<string> GetEffectiveUserCodeRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_userCodeRoots != null)
            {
                foreach (var root in _userCodeRoots)
                {
                    roots.Add(root);
                }
            }

            var solutionDir = GetSolutionDirectory();
            if (!string.IsNullOrEmpty(solutionDir))
            {
                var normalized = NormalizeRoot(solutionDir);
                // Skip bare drive roots like "C:\" to avoid treating the whole
                // drive as user code.
                if (normalized != null && normalized.Length > 3)
                {
                    roots.Add(normalized);
                }
            }

            return roots.ToList();
        }

        public async Task LoadDataAsync()
        {
            await SwitchToMainThreadIfNeededAsync();

            if (_dataDirectory == null)
            {
                _dataDirectory = GetDataDirectory();
            }

            if (_dataDirectory == null)
            {
                Logger.Log("LoadData: No solution open, using empty data");
                _sessionData = new SolutionSessionData();
                return;
            }

            Logger.Log($"LoadData: Directory={_dataDirectory}");

            _sessionData = new SolutionSessionData();

            if (!Directory.Exists(_dataDirectory))
            {
                Logger.Log("LoadData: Directory not found, using empty data");
                return;
            }

            foreach (var folder in Directory.GetDirectories(_dataDirectory).OrderBy(d => d))
            {
                var sessionFile = Path.Combine(folder, Constants.SessionFileName);
                if (!File.Exists(sessionFile))
                    continue;

                try
                {
                    var json = File.ReadAllText(sessionFile);
                    var session = JsonConvert.DeserializeObject<CallstackSession>(json);
                    if (session != null)
                    {
                        session.FolderName = Path.GetFileName(folder);
                        session.Callstacks = new List<CallstackData>();
                        session.NodeColors = new Dictionary<int, string>();
                        session.CollapsedNodes = new Dictionary<int, bool>();
                        session.IsLoaded = false;
                        _sessionData.Sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"LoadData: Failed to load session from {folder}", ex);
                }
            }

            Logger.Log($"LoadData: Loaded {_sessionData.Sessions.Count} session metadata");

            LoadActiveSessionId();
        }

        private void LoadActiveSessionId()
        {
            if (_dataDirectory == null)
                return;

            var filePath = Path.Combine(_dataDirectory, Constants.ActiveSessionFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                var activeId = JsonConvert.DeserializeObject<string>(json);
                if (!string.IsNullOrEmpty(activeId) && _sessionData.Sessions.Any(s => s.Id == activeId))
                {
                    _sessionData.ActiveSessionId = activeId;
                    Logger.Log($"LoadActiveSessionId: Restored {activeId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LoadActiveSessionId: Failed to load active session id", ex);
            }
        }

        public async Task LoadSessionDetailsAsync(CallstackSession session)
        {
            await SwitchToMainThreadIfNeededAsync();

            if (session == null || session.IsLoaded)
                return;

            var folder = GetSessionFolderPath(session);
            if (folder == null || !Directory.Exists(folder))
                return;

            await LoadCallstacksAsync(session, folder);
            await LoadStateAsync(session, folder);

            session.IsLoaded = true;
        }

        private async Task LoadCallstacksAsync(CallstackSession session, string folder)
        {
            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(filePath));
                var callstacks = JsonConvert.DeserializeObject<List<CallstackData>>(json);
                session.Callstacks = callstacks ?? new List<CallstackData>();
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadSessionDetails: Failed to load callstacks from {filePath}", ex);
                session.Callstacks = new List<CallstackData>();
            }
        }

        private async Task LoadStateAsync(CallstackSession session, string folder)
        {
            var filePath = Path.Combine(folder, Constants.StateFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(filePath));
                var state = JsonConvert.DeserializeObject<SessionState>(json);
                if (state != null)
                {
                    session.NodeColors = state.NodeColors ?? new Dictionary<int, string>();
                    session.CollapsedNodes = state.CollapsedNodes ?? new Dictionary<int, bool>();
                    session.HiddenAncestorNodes = state.HiddenAncestorNodes ?? new Dictionary<int, bool>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadSessionDetails: Failed to load state from {filePath}", ex);
            }
        }

        public async Task SaveSessionMetadataAsync(CallstackSession session)
        {
            await SwitchToMainThreadIfNeededAsync();

            if (session == null)
                return;

            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.SessionFileName);
            var metadata = new CallstackSession(session.Name)
            {
                Id = session.Id,
                CreatedTime = session.CreatedTime,
                FolderName = session.FolderName
            };

            await WriteJsonAsync(filePath, metadata);
            Logger.Log($"SaveSessionMetadata: Saved {filePath}");
        }

        public async Task SaveCallstacksAsync(CallstackSession session)
        {
            await SwitchToMainThreadIfNeededAsync();

            if (session == null)
                return;

            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            await WriteJsonAsync(filePath, session.Callstacks);
            Logger.Log($"SaveCallstacks: Saved {filePath}");
        }

        public async Task SaveStateAsync(CallstackSession session)
        {
            await SwitchToMainThreadIfNeededAsync();

            if (session == null)
                return;

            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.StateFileName);
            var state = new SessionState
            {
                NodeColors = session.NodeColors ?? new Dictionary<int, string>(),
                CollapsedNodes = session.CollapsedNodes ?? new Dictionary<int, bool>(),
                HiddenAncestorNodes = session.HiddenAncestorNodes ?? new Dictionary<int, bool>()
            };

            await WriteJsonAsync(filePath, state);
            Logger.Log($"SaveState: Saved {filePath}");
        }

        private async Task WriteJsonAsync(string filePath, object data)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = await Task.Run(() => JsonConvert.SerializeObject(data, Formatting.Indented));
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"WriteJson: Failed to write {filePath}", ex);
            }
        }

        private string GetOrCreateSessionFolder(CallstackSession session)
        {
            if (session == null)
                return null;

            if (string.IsNullOrEmpty(session.FolderName))
                session.FolderName = GenerateSessionFolderName();

            var folder = GetSessionFolderPath(session);
            if (folder == null)
                return null;

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        private string GetSessionFolderPath(CallstackSession session)
        {
            if (_dataDirectory == null || session?.FolderName == null)
                return null;

            return Path.Combine(_dataDirectory, session.FolderName);
        }

        private string GenerateSessionFolderName()
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var hash = GenerateRandomHash(3);
            var folderName = $"{timestamp}-{hash}";

            if (_dataDirectory != null && Directory.Exists(_dataDirectory))
            {
                int suffix = 1;
                var candidate = folderName;
                while (Directory.Exists(Path.Combine(_dataDirectory, candidate)))
                {
                    var newHash = GenerateRandomHash(3);
                    candidate = $"{timestamp}-{newHash}";
                    suffix++;
                    if (suffix > 100)
                    {
                        candidate = $"{timestamp}-{hash}-{Guid.NewGuid().ToString("N").Substring(0, 4)}";
                        break;
                    }
                }
                folderName = candidate;
            }

            return folderName;
        }

        private string GenerateRandomHash(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sb = new StringBuilder(length);
            lock (_random)
            {
                for (int i = 0; i < length; i++)
                {
                    sb.Append(chars[_random.Next(chars.Length)]);
                }
            }
            return sb.ToString();
        }

        public async Task SaveDataAsync()
        {
            await SwitchToMainThreadIfNeededAsync();

            if (_dataDirectory == null)
            {
                _dataDirectory = GetDataDirectory();
                if (_dataDirectory == null)
                {
                    Logger.Log("SaveData: No solution open, skipping");
                    return;
                }
            }

            foreach (var session in _sessionData.Sessions)
            {
                await SaveSessionMetadataAsync(session);
                if (session.IsLoaded)
                {
                    await SaveCallstacksAsync(session);
                    await SaveStateAsync(session);
                }
            }
        }

        public bool HasCallstacks(CallstackSession session)
        {
            if (session == null)
                return false;

            if (session.IsLoaded)
                return session.Callstacks.Count > 0;

            var folder = GetSessionFolderPath(session);
            if (folder == null)
                return false;

            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            if (!File.Exists(filePath))
                return false;

            try
            {
                var json = File.ReadAllText(filePath);
                var callstacks = JsonConvert.DeserializeObject<List<CallstackData>>(json);
                return callstacks != null && callstacks.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CallstackData> CaptureCurrentCallstackAsync()
        {
            await SwitchToMainThreadIfNeededAsync();

            if (_dte?.Debugger == null || _dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return null;

            var currentThread = _dte.Debugger.CurrentThread;
            if (currentThread == null)
                return null;

            var rawFrames = new List<CallstackFrame>();
            Logger.Log($"Capture: Starting capture, thread={currentThread.Name}, frameCount={currentThread.StackFrames.Count}");

            for (int i = 1; i <= currentThread.StackFrames.Count; i++)
            {
                try
                {
                    StackFrame vsFrame = currentThread.StackFrames.Item(i);
                    StackFrame2 vsFrame2 = vsFrame as StackFrame2;

                    string functionName = vsFrame.FunctionName;
                    string fileName = "";
                    int lineNumber = 0;
                    string language = "";
                    string module = "";
                    string lineContent = "";

                    if (vsFrame2 != null)
                    {
                        try { fileName = vsFrame2.FileName ?? ""; } catch { }
                        try { lineNumber = (int)vsFrame2.LineNumber; } catch { }
                        try { language = vsFrame2.Language ?? ""; } catch { }
                        try { module = vsFrame2.Module ?? ""; } catch { }
                    }
                    else
                    {
                        try { language = vsFrame.Language ?? ""; } catch { }
                        try { module = vsFrame.Module ?? ""; } catch { }
                    }

                    if (!string.IsNullOrEmpty(fileName) && lineNumber > 0)
                    {
                        lineContent = ReadSourceLine(fileName, lineNumber);
                    }

                    Logger.Log($"Capture: Frame {i}: Func={functionName}, File={fileName}, Line={lineNumber}, Lang={language}, Module={module}, Source={lineContent}");
                    rawFrames.Add(new CallstackFrame(functionName, fileName, lineNumber, language, module)
                    {
                        LineContent = lineContent
                    });
                }
                catch
                {
                    break;
                }
            }

            if (rawFrames.Count == 0)
                return null;

            rawFrames = TrimToUserCode(rawFrames);
            rawFrames.Reverse();

            var frames = new List<CallstackFrame>();
            int currentHash = 0;
            foreach (var f in rawFrames)
            {
                currentHash = CallstackFrame.ComputeFNV1aHash(currentHash, f.FunctionName);
                f.HashCode = currentHash;
                frames.Add(f);
            }

            return new CallstackData(frames);
        }

        private List<CallstackFrame> TrimToUserCode(List<CallstackFrame> frames)
        {
            var roots = GetEffectiveUserCodeRoots();
            if (roots.Count == 0)
                return frames;

            int firstUserCodeIndex = -1;
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                var fileName = frames[i].FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        var normalizedFile = Path.GetFullPath(fileName);
                        foreach (var root in roots)
                        {
                            if (normalizedFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            {
                                firstUserCodeIndex = i;
                                break;
                            }
                        }
                        if (firstUserCodeIndex >= 0)
                            break;
                    }
                    catch
                    {
                    }
                }
            }

            if (firstUserCodeIndex >= 0 && firstUserCodeIndex < frames.Count - 1)
            {
                Logger.Log($"TrimToUserCode: Keeping frames 0..{firstUserCodeIndex} (trimmed {frames.Count - 1 - firstUserCodeIndex} root frames) using {roots.Count} user-code root(s)");
                return frames.GetRange(0, firstUserCodeIndex + 1);
            }

            return frames;
        }

        private static string ReadSourceLine(string fileName, int lineNumber)
        {
            try
            {
                if (!File.Exists(fileName))
                    return string.Empty;

                var lines = File.ReadAllLines(fileName);
                if (lineNumber > 0 && lineNumber <= lines.Length)
                {
                    return lines[lineNumber - 1]?.Trim() ?? string.Empty;
                }
            }
            catch
            {
            }
            return string.Empty;
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

        public CallstackSession CreateSession(string name)
        {
            var session = new CallstackSession(name)
            {
                FolderName = GenerateSessionFolderName()
            };
            _sessionData.Sessions.Add(session);
            return session;
        }

        public void DeleteSession(CallstackSession session)
        {
            if (session == null)
                return;

            _sessionData.Sessions.Remove(session);

            var folder = GetSessionFolderPath(session);
            if (folder != null && Directory.Exists(folder))
            {
                try
                {
                    Directory.Delete(folder, true);
                    Logger.Log($"DeleteSession: Deleted folder {folder}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"DeleteSession: Failed to delete folder {folder}", ex);
                }
            }
        }

        public CallstackSession GetActiveSession()
        {
            if (_sessionData.ActiveSessionId == null)
                return null;

            return _sessionData.Sessions.FirstOrDefault(s => s.Id == _sessionData.ActiveSessionId);
        }

        public CallstackSession GetLastActiveSession()
        {
            return GetActiveSession();
        }

        public bool IsDebuggerInBreakMode
        {
            get
            {
                try
                {
                    return _dte?.Debugger != null && _dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool HasAnySessions
        {
            get { return _sessionData.Sessions.Count > 0; }
        }

        private void HookDebuggerEvents(DTE dte)
        {
            if (dte == null)
            {
                Logger.Log("CallstackManager: No DTE, skipping debugger-event subscription");
                return;
            }

            try
            {
                _dteEvents = dte.Events;
                _debuggerEvents = _dteEvents.DebuggerEvents;
                _debuggerEvents.OnEnterBreakMode += (dbgEventReason reason, ref dbgExecutionAction executionAction) =>
                {
                    Logger.Log("CallstackManager: Entered break mode");
                    InvalidateDebuggerDependentCommands();
                };
                _debuggerEvents.OnEnterRunMode += (dbgEventReason reason) =>
                {
                    Logger.Log("CallstackManager: Entered run mode");
                    InvalidateDebuggerDependentCommands();
                };
                _debuggerEvents.OnEnterDesignMode += (dbgEventReason reason) =>
                {
                    Logger.Log("CallstackManager: Entered design mode");
                    InvalidateDebuggerDependentCommands();
                };
                Logger.Log("CallstackManager: Subscribed to debugger events");
            }
            catch (Exception ex)
            {
                Logger.Error("CallstackManager: Failed to subscribe to debugger events", ex);
            }
        }

        private void InvalidateDebuggerDependentCommands()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        public void SetActiveSession(string sessionId)
        {
            _sessionData.ActiveSessionId = sessionId;
            SaveActiveSessionId();
        }

        private void SaveActiveSessionId()
        {
            if (_dataDirectory == null)
                return;

            try
            {
                var filePath = Path.Combine(_dataDirectory, Constants.ActiveSessionFileName);
                var json = JsonConvert.SerializeObject(_sessionData.ActiveSessionId, Formatting.Indented);
                File.WriteAllText(filePath, json);
                Logger.Log($"SaveActiveSessionId: Saved {_sessionData.ActiveSessionId}");
            }
            catch (Exception ex)
            {
                Logger.Error("SaveActiveSessionId: Failed to save active session id", ex);
            }
        }

        public List<TreeViewNode> BuildTreeNodes(CallstackSession session)
        {
            var nodes = new List<TreeViewNode>();

            if (session == null)
                return nodes;

            int mergeId = 1;
            foreach (var callstack in session.Callstacks)
            {
                var rootNode = BuildTreeFromCallstack(callstack, session, ref mergeId);
                if (rootNode != null)
                {
                    MergeTree(nodes, rootNode);
                }
            }

            SortTree(nodes);
            return nodes;
        }

        private TreeViewNode BuildTreeFromCallstack(CallstackData callstack, CallstackSession session, ref int mergeId)
        {
            if (callstack.Frames.Count == 0)
                return null;

            TreeViewNode rootNode = null;
            TreeViewNode currentNode = null;

            for (int i = 0; i < callstack.Frames.Count; i++)
            {
                var frame = callstack.Frames[i];
                var node = new TreeViewNode
                {
                    Frame = frame,
                    DisplayText = frame.FunctionName,
                    IsExpanded = GetExpansionState(session, frame.HashCode, true)
                };

                if (session.NodeColors.TryGetValue(frame.HashCode, out var hexColor))
                {
                    node.DisplayBackground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
                }

                if (i == 0)
                {
                    rootNode = node;
                }
                else
                {
                    currentNode.Children.Add(node);
                }

                currentNode = node;
            }

            var lastFrame = callstack.Frames[callstack.Frames.Count - 1];
            var leafNode = CreateLeafNode(lastFrame, ref mergeId);
            currentNode.Children.Add(leafNode);

            return rootNode;
        }

        private TreeViewNode CreateLeafNode(CallstackFrame frame, ref int mergeId)
        {
            string displayText;
            if (frame.LineContent != null)
            {
                if (frame.LineContent.Length > Constants.LeafNodeDisplayMaxLength)
                {
                    displayText = frame.LineContent.Substring(0, Constants.LeafNodeDisplayMaxLength - 3) + "...";
                }
                else
                {
                    displayText = frame.LineContent;
                }
            }
            else if (!string.IsNullOrEmpty(frame.FileName))
            {
                displayText = $"<{frame.FileName}:{frame.LineNumber}>";
            }
            else
            {
                displayText = "<current line>";
            }

            return new TreeViewNode
            {
                Frame = frame,
                DisplayText = displayText,
                IsLeaf = true,
                MergeId = mergeId++
            };
        }

        private void MergeTree(IList<TreeViewNode> roots, TreeViewNode newNode)
        {
            var existing = roots.FirstOrDefault(r => r.Frame?.HashCode == newNode.Frame?.HashCode);
            if (existing == null)
            {
                roots.Add(newNode);
                return;
            }

            var children = newNode.Children.ToList();
            foreach (var child in children)
            {
                MergeTree(existing.Children, child);
            }
        }

        private void SortTree(IList<TreeViewNode> nodes)
        {
            var sorted = nodes.OrderBy(n => n.IsLeaf ? 1 : 0)
                              .ThenBy(n => n.Frame?.LineNumber ?? int.MaxValue)
                              .ThenBy(n => n.DisplayText)
                              .ToList();

            nodes.Clear();
            foreach (var node in sorted)
            {
                nodes.Add(node);
                SortTree(node.Children);
            }
        }

        private bool GetExpansionState(CallstackSession session, int hashCode, bool defaultValue)
        {
            if (session.CollapsedNodes.TryGetValue(hashCode, out var state))
                return !state;
            return defaultValue;
        }

        public void SaveExpansionState(CallstackSession session, int hashCode, bool isExpanded)
        {
            session.CollapsedNodes[hashCode] = !isExpanded;
        }

        public bool CanHideAncestors(TreeViewNode node)
        {
            if (node == null)
                return false;

            foreach (var ancestor in node.GetAncestors())
            {
                if (ancestor.Children.Count > 1)
                    return false;
            }

            return node.Parent != null;
        }

        public bool IsDisplayRoot(TreeViewNode node, CallstackSession session)
        {
            if (node == null || session == null)
                return false;

            foreach (var ancestor in node.GetAncestors())
            {
                if (session.HiddenAncestorNodes.ContainsKey(ancestor.Frame?.HashCode ?? 0))
                    return true;
            }

            return false;
        }

        public void SetHiddenAncestors(CallstackSession session, TreeViewNode node)
        {
            if (session == null || node == null)
                return;

            ClearHiddenAncestorsForPath(session, node);

            foreach (var ancestor in node.GetAncestors())
            {
                var hashCode = ancestor.Frame?.HashCode ?? 0;
                if (hashCode != 0)
                    session.HiddenAncestorNodes[hashCode] = true;
            }
        }

        public void ClearHiddenAncestorsForPath(CallstackSession session, TreeViewNode node)
        {
            if (session == null || node == null)
                return;

            foreach (var ancestor in node.GetAncestors())
            {
                var hashCode = ancestor.Frame?.HashCode ?? 0;
                if (hashCode != 0)
                    session.HiddenAncestorNodes.Remove(hashCode);
            }
        }

        public List<TreeViewNode> BuildDisplayTreeNodes(CallstackSession session, List<TreeViewNode> fullTree)
        {
            var displayRoots = new List<TreeViewNode>();
            if (session == null || fullTree == null)
                return displayRoots;

            foreach (var root in fullTree)
            {
                displayRoots.AddRange(GetVisibleRoots(session, root));
            }

            return displayRoots;
        }

        private IEnumerable<TreeViewNode> GetVisibleRoots(CallstackSession session, TreeViewNode root)
        {
            var current = root;
            while (current != null)
            {
                var hashCode = current.Frame?.HashCode ?? 0;
                bool wantsHidden = hashCode != 0 && session.HiddenAncestorNodes.ContainsKey(hashCode);
                bool isBranched = current.Children.Count > 1;

                if (wantsHidden && !isBranched)
                {
                    var next = current.Children.FirstOrDefault(c => !c.IsLeaf);
                    if (next == null)
                    {
                        yield return current;
                        yield break;
                    }

                    foreach (var child in current.Children)
                    {
                        foreach (var visibleRoot in GetVisibleRoots(session, child))
                        {
                            yield return visibleRoot;
                        }
                    }
                    yield break;
                }

                yield return current;
                yield break;
            }
        }
    }
}
