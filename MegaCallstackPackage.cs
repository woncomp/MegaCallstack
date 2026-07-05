using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MegaCallstack.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace MegaCallstack
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(MegaCallstackPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(MegaCallstackToolWindow), Style = Microsoft.VisualStudio.Shell.VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideAutoLoad(UIContextGuids.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class MegaCallstackPackage : AsyncPackage
    {
        public const string PackageGuidString = "76259f0b-18fa-4a6e-8d6b-7b29270be2f1";
        public const string CommandSetGuidString = "b5c8e1a2-3f4d-4e5a-9b6c-7d8e9f0a1b2c";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Logger.Initialize(this);
            Logger.Log("Package: Initialized");
            await ShowMegaCallstackWindowCommand.InitializeAsync(this);
        }
    }

    internal sealed class ShowMegaCallstackWindowCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid(MegaCallstackPackage.CommandSetGuidString);

        private readonly AsyncPackage _package;

        private ShowMegaCallstackWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static ShowMegaCallstackWindowCommand Instance { get; private set; }

        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => _package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new ShowMegaCallstackWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                ToolWindowPane window = await _package.FindToolWindowAsync(typeof(MegaCallstackToolWindow), 0, true, _package.DisposalToken);
                if (window?.Frame == null)
                {
                    throw new NotSupportedException("Cannot create Mega Callstack tool window.");
                }

                IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            });
        }
    }
}
