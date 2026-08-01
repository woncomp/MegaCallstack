using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE90a;
using MegaCallstack.Models;
using Microsoft.VisualStudio.Shell;

namespace MegaCallstack.Services
{
    public class CallstackCaptureService : ICallstackCaptureService
    {
        private readonly DTE _dte;
        private readonly FuzzyBookmarkEngine _bookmarkEngine;
        private readonly IBookmarkResolver _bookmarkResolver;
        private readonly List<string> _userCodeRoots;
        private readonly List<string> _skipRootFunctions;
        private EnvDTE.Events _dteEvents;
        private EnvDTE.DebuggerEvents _debuggerEvents;

        public event EventHandler DebuggerStateChanged;

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

        public CallstackCaptureService(DTE dte)
            : this(dte, null, null, new FuzzyBookmarkEngine(), null)
        {
        }

        public CallstackCaptureService(DTE dte, FuzzyBookmarkEngine bookmarkEngine)
            : this(dte, null, null, bookmarkEngine, null)
        {
        }

        public CallstackCaptureService(DTE dte, IEnumerable<string> userCodeRoots)
            : this(dte, userCodeRoots, null, new FuzzyBookmarkEngine(), null)
        {
        }

        public CallstackCaptureService(DTE dte, IEnumerable<string> userCodeRoots, FuzzyBookmarkEngine bookmarkEngine)
            : this(dte, userCodeRoots, null, bookmarkEngine, null)
        {
        }

        public CallstackCaptureService(DTE dte, IEnumerable<string> userCodeRoots, IEnumerable<string> skipRootFunctions, FuzzyBookmarkEngine bookmarkEngine, IBookmarkResolver bookmarkResolver)
        {
            _dte = dte;
            _bookmarkEngine = bookmarkEngine ?? new FuzzyBookmarkEngine();
            _bookmarkResolver = bookmarkResolver;
            _userCodeRoots = NormalizeUserCodeRoots(userCodeRoots);
            _skipRootFunctions = NormalizeSkipRootFunctions(skipRootFunctions);
            HookDebuggerEvents(dte);
        }

        public CallstackCaptureService(DTE dte, IEnumerable<string> userCodeRoots, IEnumerable<string> skipRootFunctions)
            : this(dte, userCodeRoots, skipRootFunctions, new FuzzyBookmarkEngine(), null)
        {
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

                    Logger.Log($"Capture: Frame {i}: Func={functionName}, File={fileName}, Line={lineNumber}");
                    var frame = new CallstackFrame(functionName, fileName, lineNumber, language, module)
                    {
                        LineContent = lineContent
                    };

                    rawFrames.Add(frame);
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

            var callstack = TransformToPersistentCallstack(frames);

            if (_bookmarkResolver != null)
                await _bookmarkResolver.CreateBookmarksForCallstackAsync(callstack);

            return callstack;
        }

        internal static CallstackData TransformToPersistentCallstack(List<CallstackFrame> originalFrames)
        {
            if (originalFrames == null || originalFrames.Count == 0)
                return null;

            var transformedFrames = new List<CallstackFrame>(originalFrames.Count + 1);

            for (int i = 0; i < originalFrames.Count; i++)
            {
                var originalFrame = originalFrames[i];
                CallstackFrame transformedFrame;

                if (i == 0)
                {
                    transformedFrame = new CallstackFrame(originalFrame.FunctionName, null, 0)
                    {
                        Language = originalFrame.Language,
                        Module = originalFrame.Module,
                        HashCode = originalFrame.HashCode
                    };
                }
                else
                {
                    var parentFrame = originalFrames[i - 1];
                    transformedFrame = new CallstackFrame(originalFrame.FunctionName, parentFrame.FileName, parentFrame.LineNumber)
                    {
                        LineContent = parentFrame.LineContent,
                        Language = parentFrame.Language,
                        Module = parentFrame.Module,
                        Bookmark = parentFrame.Bookmark,
                        HashCode = originalFrame.HashCode
                    };
                }

                transformedFrames.Add(transformedFrame);
            }

            var deepestFrame = originalFrames[originalFrames.Count - 1];
            var leafFrame = new CallstackFrame(null, deepestFrame.FileName, deepestFrame.LineNumber)
            {
                LineContent = deepestFrame.LineContent,
                Language = deepestFrame.Language,
                Module = deepestFrame.Module,
                Bookmark = deepestFrame.Bookmark,
                HashCode = deepestFrame.HashCode
            };
            transformedFrames.Add(leafFrame);

            return new CallstackData(transformedFrames);
        }

        public async Task<int> ResolveFrameLineNumberAsync(CallstackFrame frame)
        {
            if (frame?.Bookmark == null || string.IsNullOrEmpty(frame.FileName))
                return frame?.LineNumber ?? 0;

            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(frame.FileName))
                        return frame.LineNumber;

                    var results = _bookmarkEngine.ResolveAll(new[] { frame.Bookmark }, frame.FileName);
                    if (results.Count > 0 && results[0].Line > 0)
                        frame.LineNumber = results[0].Line;

                    return frame.LineNumber;
                }
                catch (Exception ex)
                {
                    Logger.Log($"ResolveFrameLineNumber: Failed to resolve bookmark: {ex.Message}");
                    return frame.LineNumber;
                }
            });
        }

        private async Task SwitchToMainThreadIfNeededAsync()
        {
            if (_dte != null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
        }

        private void HookDebuggerEvents(DTE dte)
        {
            if (dte == null)
            {
                Logger.Log("CallstackCaptureService: No DTE, skipping debugger-event subscription");
                return;
            }

            try
            {
                _dteEvents = dte.Events;
                _debuggerEvents = _dteEvents.DebuggerEvents;
                _debuggerEvents.OnEnterBreakMode += (dbgEventReason reason, ref dbgExecutionAction executionAction) => OnDebuggerStateChanged();
                _debuggerEvents.OnEnterRunMode += (dbgEventReason reason) => OnDebuggerStateChanged();
                _debuggerEvents.OnEnterDesignMode += (dbgEventReason reason) => OnDebuggerStateChanged();
                Logger.Log("CallstackCaptureService: Subscribed to debugger events");
            }
            catch (Exception ex)
            {
                Logger.Error("CallstackCaptureService: Failed to subscribe to debugger events", ex);
            }
        }

        private void OnDebuggerStateChanged()
        {
            DebuggerStateChanged?.Invoke(this, EventArgs.Empty);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private static string ReadSourceLine(string fileName, int lineNumber)
        {
            try
            {
                if (!File.Exists(fileName))
                    return string.Empty;

                var lines = File.ReadAllLines(fileName);
                if (lineNumber > 0 && lineNumber <= lines.Length)
                    return lines[lineNumber - 1]?.Trim() ?? string.Empty;
            }
            catch
            {
            }
            return string.Empty;
        }

        private List<CallstackFrame> TrimToUserCode(List<CallstackFrame> frames)
        {
            var skipRootFunctions = GetEffectiveSkipRootFunctions();
            if (skipRootFunctions.Count > 0)
            {
                int skipRootIndex = FindDeepestSkipRootIndex(frames, skipRootFunctions);
                if (skipRootIndex > 0)
                {
                    Logger.Log($"TrimToUserCode: Matched skip root '{frames[skipRootIndex].FunctionName}' at {skipRootIndex}; keeping frames 0..{skipRootIndex - 1}");
                    return frames.GetRange(0, skipRootIndex);
                }
            }

            if (_userCodeRoots == null || _userCodeRoots.Count == 0)
                return frames;

            int firstUserCodeIndex = -1;
            int lastKnownBoundaryIndex = -1;
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                var fileName = frames[i].FileName;
                if (string.IsNullOrEmpty(fileName))
                    continue;

                if (lastKnownBoundaryIndex < 0)
                    lastKnownBoundaryIndex = i;

                try
                {
                    var normalizedFile = Path.GetFullPath(fileName);
                    bool isUserCode = false;
                    foreach (var root in _userCodeRoots)
                    {
                        if (normalizedFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            isUserCode = true;
                            break;
                        }
                    }

                    if (isUserCode)
                    {
                        firstUserCodeIndex = i;
                        break;
                    }
                }
                catch
                {
                }
            }

            int keepUntilIndex = firstUserCodeIndex >= 0 ? firstUserCodeIndex : lastKnownBoundaryIndex;
            if (keepUntilIndex >= 0 && keepUntilIndex < frames.Count - 1)
            {
                Logger.Log($"TrimToUserCode: Keeping frames 0..{keepUntilIndex} (trimmed {frames.Count - 1 - keepUntilIndex} root frames) using {_userCodeRoots.Count} user-code root(s)");
                return frames.GetRange(0, keepUntilIndex + 1);
            }

            return frames;
        }

        private List<string> GetEffectiveSkipRootFunctions()
        {
            var result = new List<string>();
            if (_skipRootFunctions != null)
            {
                foreach (var functionName in _skipRootFunctions)
                {
                    if (!result.Contains(functionName, StringComparer.Ordinal))
                        result.Add(functionName);
                }
            }

            var currentSettings = SettingsService.CurrentSettings;
            if (currentSettings?.SkipRootFunctions != null)
            {
                foreach (var functionName in currentSettings.SkipRootFunctions)
                {
                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;

                    var normalized = functionName.Trim();
                    if (!result.Contains(normalized, StringComparer.Ordinal))
                        result.Add(normalized);
                }
            }

            return result;
        }

        private static int FindDeepestSkipRootIndex(List<CallstackFrame> frames, List<string> skipRootFunctions)
        {
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                var functionName = frames[i].FunctionName;
                if (string.IsNullOrEmpty(functionName))
                    continue;

                foreach (var skipRoot in skipRootFunctions)
                {
                    if (functionName.Equals(skipRoot, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static List<string> NormalizeUserCodeRoots(IEnumerable<string> userCodeRoots)
        {
            var result = new List<string>();
            if (userCodeRoots == null)
                return result;

            foreach (var root in userCodeRoots)
            {
                var normalized = NormalizeRoot(root);
                if (normalized != null && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    result.Add(normalized);
            }

            return result;
        }

        private static List<string> NormalizeSkipRootFunctions(IEnumerable<string> skipRootFunctions)
        {
            var result = new List<string>();
            if (skipRootFunctions == null)
                return result;

            foreach (var functionName in skipRootFunctions)
            {
                if (string.IsNullOrWhiteSpace(functionName))
                    continue;

                var normalized = functionName.Trim();
                if (!result.Contains(normalized, StringComparer.Ordinal))
                    result.Add(normalized);
            }

            return result;
        }

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
    }
}
