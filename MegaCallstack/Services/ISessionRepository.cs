using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Loads and saves sessions, callstacks, state, and notes for a specific solution.
    /// The repository is bound to a <see cref="SolutionInfo"/> and is invalid without one.
    /// </summary>
    public interface ISessionRepository
    {
        SolutionInfo SolutionInfo { get; }

        Task<SolutionSessionData> LoadDataAsync();
        Task LoadSessionDetailsAsync(CallstackSession session);
        Task SaveSessionMetadataAsync(CallstackSession session);
        Task SaveCallstacksAsync(CallstackSession session);
        Task SaveStateAsync(CallstackSession session);
        Task SaveNotesAsync(CallstackSession session);
        Task SavePreviousSessionIdAsync(string previousSessionId);
        string GetOrCreateSessionFolder(CallstackSession session);
        string GetSessionFolderPath(CallstackSession session);
        bool HasCallstacks(CallstackSession session);
        string GenerateSessionFolderName();
    }
}
