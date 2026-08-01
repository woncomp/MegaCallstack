namespace MegaCallstack.Models
{
    public sealed class MegaCallstackSettings
    {
        public bool DiagnosticLoggingEnabled { get; set; }
        public bool BookmarkFileDiagnosticsEnabled { get; set; }
        public int LeafNodeDisplayMaxLength { get; set; } = 120;
        public int MaxUserCodeRoots { get; set; } = 8;
        public int MaxSolutionFilesToScan { get; set; } = 100000;
    }
}
