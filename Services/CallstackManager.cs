using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string _dataFilePath;

        public SolutionSessionData SessionData => _sessionData;

        public CallstackManager(DTE dte)
        {
            _dte = dte;
            _sessionData = new SolutionSessionData();
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

        private string GetDataFilePath()
        {
            var solutionDir = GetSolutionDirectory();
            var solutionName = GetSolutionName();
            if (solutionDir == null || solutionName == null)
                return null;

            return Path.Combine(solutionDir, ".vs", solutionName, Constants.DataFolderName, Constants.DataFileName);
        }

        public async Task LoadDataAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _dataFilePath = GetDataFilePath();
            if (_dataFilePath == null)
            {
                Logger.Log("LoadData: No solution open, using empty data");
                _sessionData = new SolutionSessionData();
                return;
            }

            Logger.Log($"LoadData: Path={_dataFilePath}");

            if (File.Exists(_dataFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_dataFilePath);
                    _sessionData = JsonConvert.DeserializeObject<SolutionSessionData>(json)
                                   ?? new SolutionSessionData();
                    Logger.Log($"LoadData: Loaded {_sessionData.Sessions.Count} sessions, active={_sessionData.ActiveSessionId}");
                }
                catch (Exception ex)
                {
                    Logger.Error("LoadData: Failed to deserialize", ex);
                    _sessionData = new SolutionSessionData();
                }
            }
            else
            {
                Logger.Log("LoadData: File not found, using empty data");
                _sessionData = new SolutionSessionData();
            }
        }

        public async Task SaveDataAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_dataFilePath == null)
            {
                _dataFilePath = GetDataFilePath();
                if (_dataFilePath == null)
                {
                    Logger.Log("SaveData: No solution open, skipping");
                    return;
                }
            }

            try
            {
                var directory = Path.GetDirectoryName(_dataFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(_sessionData, Formatting.Indented);
                File.WriteAllText(_dataFilePath, json);
                Logger.Log($"SaveData: Saved {_sessionData.Sessions.Count} sessions to {_dataFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Error("SaveData: Failed to write", ex);
            }
        }

        public async Task<CallstackData> CaptureCurrentCallstackAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

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

                    Logger.Log($"Capture: Frame {i}: Func={functionName}, File={fileName}, Line={lineNumber}, Lang={language}, Module={module}");
                    rawFrames.Add(new CallstackFrame(functionName, fileName, lineNumber, language, module));
                }
                catch
                {
                    break;
                }
            }

            if (rawFrames.Count == 0)
                return null;

            rawFrames.Reverse();

            rawFrames = TrimToUserCode(rawFrames);

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
            var solutionDir = GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return frames;

            var normalizedDir = Path.GetFullPath(solutionDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;

            // After Reverse(), frames are ordered: [leaf, ..., root]
            // Scan from ROOT (end of list) backward to find the first user code frame
            int firstUserCodeIndex = -1;
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                var fileName = frames[i].FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        var normalizedFile = Path.GetFullPath(fileName);
                        if (normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                        {
                            firstUserCodeIndex = i;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (firstUserCodeIndex >= 0 && firstUserCodeIndex < frames.Count - 1)
            {
                Logger.Log($"TrimToUserCode: Keeping frames 0..{firstUserCodeIndex} (trimmed {frames.Count - 1 - firstUserCodeIndex} root frames)");
                return frames.GetRange(0, firstUserCodeIndex + 1);
            }

            return frames;
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
            var session = new CallstackSession(name);
            _sessionData.Sessions.Add(session);
            return session;
        }

        public CallstackSession GetActiveSession()
        {
            if (_sessionData.ActiveSessionId == null && _sessionData.Sessions.Count > 0)
            {
                _sessionData.ActiveSessionId = _sessionData.Sessions[0].Id;
            }

            return _sessionData.Sessions.FirstOrDefault(s => s.Id == _sessionData.ActiveSessionId);
        }

        public void SetActiveSession(string sessionId)
        {
            _sessionData.ActiveSessionId = sessionId;
        }

        public List<TreeViewNode> BuildTreeNodes(CallstackSession session)
        {
            var nodes = new List<TreeViewNode>();

            if (session == null)
                return nodes;

            foreach (var callstack in session.Callstacks)
            {
                var rootNode = BuildTreeFromCallstack(callstack, session);
                if (rootNode != null)
                {
                    nodes.Add(rootNode);
                }
            }

            return nodes;
        }

        private TreeViewNode BuildTreeFromCallstack(CallstackData callstack, CallstackSession session)
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

            return rootNode;
        }

        private bool GetExpansionState(CallstackSession session, int hashCode, bool defaultValue)
        {
            if (session.NodeExpansionStates.TryGetValue(hashCode, out var state))
                return state;
            return defaultValue;
        }

        public void SaveExpansionState(CallstackSession session, int hashCode, bool isExpanded)
        {
            session.NodeExpansionStates[hashCode] = isExpanded;
        }
    }
}
