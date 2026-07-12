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
            : this(dte, new FuzzyBookmarkEngine())
        {
        }

        public CallstackCaptureService(DTE dte, FuzzyBookmarkEngine bookmarkEngine)
        {
            _dte = dte;
            _bookmarkEngine = bookmarkEngine ?? new FuzzyBookmarkEngine();
            HookDebuggerEvents(dte);
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

                    if (!string.IsNullOrEmpty(fileName) && lineNumber > 0)
                    {
                        try
                        {
                            frame.Bookmark = _bookmarkEngine.Create(fileName, lineNumber);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Capture: Failed to create fuzzy bookmark: {ex.Message}");
                        }
                    }

                    rawFrames.Add(frame);
                }
                catch
                {
                    break;
                }
            }

            if (rawFrames.Count == 0)
                return null;

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

                    var result = _bookmarkEngine.Resolve(frame.Bookmark, frame.FileName);
                    if (result.Line > 0)
                        frame.LineNumber = result.Line;

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
    }
}
