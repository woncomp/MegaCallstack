using System;
using System.Threading.Tasks;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Provides the currently loaded <see cref="SolutionInfo"/> and notifies
    /// consumers when the active solution changes or becomes ready.
    /// </summary>
    public interface ISolutionInfoProvider
    {
        /// <summary>
        /// The current solution info. Null when no solution is loaded.
        /// When a solution is loading, this remains null until user-code root
        /// detection has completed.
        /// </summary>
        SolutionInfo Current { get; }

        /// <summary>
        /// Raised when <see cref="Current"/> changes or when its readiness state changes.
        /// </summary>
        event EventHandler CurrentChanged;
    }
}
