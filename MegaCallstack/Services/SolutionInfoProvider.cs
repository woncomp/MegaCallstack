using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using MegaCallstack.Models;
using Microsoft.VisualStudio.Shell;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Tracks the Visual Studio solution lifecycle and publishes a ready
    /// <see cref="SolutionInfo"/> only after user-code roots have been computed.
    /// Roots are computed on a background thread; results from a closed solution
    /// are discarded.
    /// </summary>
    public class SolutionInfoProvider : ISolutionInfoProvider
    {
        private readonly DTE _dte;
        private readonly EnvDTE.Events _dteEvents;
        private readonly EnvDTE.SolutionEvents _solutionEvents;
        private SolutionInfo _current;
        private Guid _currentOperationId;

        public SolutionInfo Current => _current;

        public event EventHandler CurrentChanged;

        public SolutionInfoProvider(DTE dte)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _dteEvents = dte.Events;
            _solutionEvents = _dteEvents.SolutionEvents;
            _solutionEvents.Opened += OnSolutionOpened;
            _solutionEvents.BeforeClosing += OnSolutionBeforeClosing;

            if (IsSolutionLoaded())
            {
                BeginResolveSolutionInfo();
            }
        }

        private bool IsSolutionLoaded()
        {
            try
            {
                return _dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName);
            }
            catch
            {
                return false;
            }
        }

        private void OnSolutionOpened()
        {
            Logger.Log("SolutionInfoProvider: Solution opened");
            BeginResolveSolutionInfo();
        }

        private void OnSolutionBeforeClosing()
        {
            Logger.Log("SolutionInfoProvider: Solution before closing, clearing");
            _currentOperationId = Guid.NewGuid();
            SetCurrent(null);
        }

        private void BeginResolveSolutionInfo()
        {
            _currentOperationId = Guid.NewGuid();
            var operationId = _currentOperationId;

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (operationId != _currentOperationId)
                    return;

                string fullPath;
                try
                {
                    fullPath = _dte.Solution?.FullName;
                }
                catch
                {
                    return;
                }

                if (string.IsNullOrEmpty(fullPath))
                    return;

                List<string> roots;
                try
                {
                    var filePaths = await Task.Run(() => CollectSolutionFilePaths());
                    roots = await Task.Run(() => SolutionRootDetector.DetectProjectFolders(filePaths.ToArray(), Constants.MaxUserCodeRoots));
                }
                catch (Exception ex)
                {
                    Logger.Error("SolutionInfoProvider: Failed to compute user-code roots", ex);
                    roots = new List<string>();
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (operationId != _currentOperationId)
                {
                    Logger.Log("SolutionInfoProvider: Discarding stale roots");
                    return;
                }

                if (!IsSolutionLoaded())
                {
                    Logger.Log("SolutionInfoProvider: Solution closed while computing roots, discarding");
                    return;
                }

                try
                {
                    var info = new SolutionInfo(fullPath, NormalizeRoots(roots, fullPath));
                    Logger.Log($"SolutionInfoProvider: Found {info.UserCodeRoots.Count} user-code roots:");
                    foreach (var root in info.UserCodeRoots)
                        Logger.Log($"  - {root}");
                    SetCurrent(info);
                }
                catch (Exception ex)
                {
                    Logger.Error("SolutionInfoProvider: Failed to create SolutionInfo", ex);
                }
            });
        }

        private List<string> NormalizeRoots(List<string> roots, string solutionFullPath)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots ?? Enumerable.Empty<string>())
            {
                var normalized = NormalizeRoot(root);
                if (normalized != null)
                    result.Add(normalized);
            }

            var solutionDir = NormalizeRoot(Path.GetDirectoryName(solutionFullPath));
            if (solutionDir != null && solutionDir.Length > 3)
                result.Add(solutionDir);

            return result.ToList();
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
                        Logger.Log($"SolutionInfoProvider: Hit cap of {Constants.MaxSolutionFilesToScan} files");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("SolutionInfoProvider: Failed to enumerate solution projects", ex);
            }
            return files;
        }

        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private void CollectFromProject(Project project, List<string> files)
        {
            if (project == null || files.Count >= Constants.MaxSolutionFilesToScan)
                return;

            try
            {
                if (!string.IsNullOrEmpty(project.FullName))
                    files.Add(project.FullName);
            }
            catch { }

            try
            {
                if (project.Kind != null && project.Kind.Equals(SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
                {
                    CollectFromProjectItems(project.ProjectItems, files);
                    return;
                }
            }
            catch { }

            try
            {
                CollectFromProjectItems(project.ProjectItems, files);
            }
            catch { }
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
                    if (item.FileCount > 0)
                    {
                        var name = item.FileNames[1];
                        if (!string.IsNullOrEmpty(name))
                            files.Add(name);
                    }
                }
                catch { }

                try
                {
                    var subProject = item.SubProject;
                    if (subProject != null)
                        CollectFromProject(subProject, files);
                }
                catch { }

                try
                {
                    if (item.ProjectItems != null)
                        CollectFromProjectItems(item.ProjectItems, files);
                }
                catch { }
            }
        }

        private void SetCurrent(SolutionInfo value)
        {
            if (_current == value)
                return;

            _current = value;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
