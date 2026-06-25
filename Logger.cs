using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace MegaCallstack
{
    public static class Logger
    {
        private static IVsOutputWindowPane _pane;
        private static Guid PaneGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var outputWindow = serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return;

            outputWindow.CreatePane(ref PaneGuid, "Mega Callstack", 1, 1);
            outputWindow.GetPane(ref PaneGuid, out _pane);
        }

        public static void Log(string message)
        {
            // Fast path: if the output pane was never created (e.g. before the
            // package initializes, or in unit tests with no VS), skip the
            // thread switch entirely rather than queuing work that can't write.
            if (_pane == null)
                return;

            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _pane?.OutputString($"[MegaCallstack] {DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                });
            }
            catch
            {
            }
        }

        public static void Error(string message, Exception ex = null)
        {
            Log($"ERROR: {message} {ex?.Message}");
        }
    }
}
