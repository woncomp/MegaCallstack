using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Captures the current debugger callstack. This service is stateless and
    /// does not depend on the loaded solution.
    /// </summary>
    public interface ICallstackCaptureService
    {
        Task<CallstackData> CaptureCurrentCallstackAsync();
        bool IsDebuggerInBreakMode { get; }
        event System.EventHandler DebuggerStateChanged;
    }
}
