using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Detects a small set of "project root" directories from a collection of
    /// file paths. The algorithm is an AdaptiveBranching detector that scores
    /// every candidate directory by how strongly it looks like an independent
    /// project root, then greedily selects the top-K non-redundant roots.
    /// </summary>
    /// <remarks>
    /// The intent is to identify the common root folders that contain a
    /// solution's actual user code, even when that code lives outside the
    /// .sln directory. The result is used to decide whether a stack frame is
    /// user code (vs. host/framework noise).
    /// </remarks>
    public static class SolutionRootDetector
    {
        /// <summary>
        /// Returns up to <paramref name="maxProjectCount"/> project root
        /// directories derived from <paramref name="filePaths"/>.
        /// </summary>
        public static List<string> DetectProjectFolders(string[] filePaths, int maxProjectCount)
        {
            if (filePaths == null || filePaths.Length == 0 || maxProjectCount <= 0)
                return new List<string>();

            // 1. Count file coverage for every directory node on every path.
            var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in filePaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                string normalizedPath = path.Replace('\\', '/');
                int lastSlash = normalizedPath.LastIndexOf('/');
                if (lastSlash <= 0)
                    continue;

                string dirPath = normalizedPath.Substring(0, lastSlash);
                string[] parts = dirPath.Split('/');
                string currentPath = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[i] : currentPath + "/" + parts[i];
                    if (dirCounts.ContainsKey(currentPath))
                        dirCounts[currentPath]++;
                    else
                        dirCounts[currentPath] = 1;
                }
            }

            // 2. Identify branch points (AdaptiveBranching) and score them.
            var candidates = new List<(string Path, int Count, double Score)>();
            var sortedDirs = dirCounts.Keys.OrderBy(d => d.Split('/').Length).ToList();

            foreach (var dir in sortedDirs)
            {
                int currentCount = dirCounts[dir];
                if (currentCount < 2)
                    continue; // Filter absolute noise.

                var directChildren = dirCounts.Keys
                    .Where(k => k.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase)
                             && k.Substring(dir.Length + 1).IndexOf('/') == -1)
                    .ToList();

                bool isBranchPoint = false;

                if (directChildren.Count == 0)
                {
                    isBranchPoint = true; // Leaf node is always a candidate.
                }
                else
                {
                    int maxChildCount = directChildren.Max(child => dirCounts[child]);
                    // A branch point is where no single child dominates the
                    // parent, or where the directory fans out to >2 children.
                    if ((double)maxChildCount / currentCount < 0.80 || directChildren.Count > 2)
                    {
                        isBranchPoint = true;
                    }
                }

                if (isBranchPoint)
                {
                    int depth = dir.Split('/').Length;
                    if (depth > 1) // Ignore system roots (e.g. "C:").
                    {
                        // Score = file count * sqrt(depth). The depth factor
                        // prevents the algorithm from collapsing to a bare
                        // drive root as the single "project".
                        double score = currentCount * Math.Sqrt(depth);
                        candidates.Add((dir, currentCount, score));
                    }
                }
            }

            // 3. Greedy selection by score, deduping parent/child redundancy,
            //    capped at maxProjectCount.
            var orderedCandidates = candidates.OrderByDescending(c => c.Score).ToList();
            var finalRoots = new List<string>();

            foreach (var candidate in orderedCandidates)
            {
                if (finalRoots.Count >= maxProjectCount)
                    break; // Hit the user-specified cap; stop.

                // Reject candidates that contain, or are contained by, an
                // already-selected root so we don't return nested duplicates.
                bool isRedundant = finalRoots.Any(root =>
                    candidate.Path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
                    root.StartsWith(candidate.Path + "/", StringComparison.OrdinalIgnoreCase));

                if (!isRedundant)
                {
                    finalRoots.Add(candidate.Path);
                }
            }

            // 4. Loosening: if redundancy pruning left us short of the cap,
            //    allow nested candidates to coexist to fill the remainder.
            if (finalRoots.Count < maxProjectCount && finalRoots.Count < orderedCandidates.Count)
            {
                foreach (var candidate in orderedCandidates)
                {
                    if (finalRoots.Count >= maxProjectCount)
                        break;
                    if (!finalRoots.Contains(candidate.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        finalRoots.Add(candidate.Path);
                    }
                }
            }

            return finalRoots
                .Select(p => p.Replace('/', Path.DirectorySeparatorChar))
                .ToList();
        }
    }
}
