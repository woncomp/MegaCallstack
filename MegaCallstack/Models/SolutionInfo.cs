using System;
using System.Collections.Generic;
using System.IO;

namespace MegaCallstack.Models
{
    /// <summary>
    /// Immutable description of a loaded Visual Studio solution.
    /// The object is created once the solution is open; <see cref="UserCodeRoots"/>
    /// is populated asynchronously when root detection completes.
    /// </summary>
    public class SolutionInfo
    {
        public string FullPath { get; }
        public string Directory { get; }
        public string Name { get; }
        public string DataDirectory { get; }
        public IReadOnlyList<string> UserCodeRoots { get; }

        public bool IsReady => UserCodeRoots != null;

        public SolutionInfo(string fullPath, IEnumerable<string> userCodeRoots = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("Solution full path is required.", nameof(fullPath));

            FullPath = fullPath;
            Directory = Path.GetDirectoryName(fullPath);
            Name = Path.GetFileNameWithoutExtension(fullPath);
            DataDirectory = Path.Combine(Directory, ".vs", Name, Constants.DataFolderName);
            UserCodeRoots = userCodeRoots != null ? new List<string>(userCodeRoots) : null;
        }
    }
}
