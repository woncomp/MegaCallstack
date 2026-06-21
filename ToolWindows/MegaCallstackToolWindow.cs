using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace MegaCallstack.ToolWindows
{
    [Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    public class MegaCallstackToolWindow : ToolWindowPane
    {
        public const string ToolWindowGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        public MegaCallstackToolWindow() : base(null)
        {
            Caption = "Mega Callstack";
            Content = new MegaCallstackToolWindowControl();
        }
    }
}
