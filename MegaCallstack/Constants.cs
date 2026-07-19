namespace MegaCallstack
{
    public static class Constants
    {
        public const string ExtensionName = "MegaCallstack";
        public const string DataFolderName = "MegaCallstack";
        public const string SessionFileName = "session.json";
        public const string CallstacksFileName = "callstacks.json";
        public const string StateFileName = "state.json";
        public const string NotesFileName = "notes.json";
        public const string ActiveSessionFileName = "active_session.json";
        public const string DiagnosticsFolderName = "Diagnostics";
        public const int LeafNodeDisplayMaxLength = 120;

        /// <summary>
        /// Maximum number of user-code root directories to compute from the
        /// solution's files. Bounds how many distinct project roots are used
        /// when deciding whether a stack frame is user code.
        /// </summary>
        public const int MaxUserCodeRoots = 5;

        /// <summary>
        /// Safety cap on the number of file paths walked from the solution's
        /// EnvDTE project tree before running root detection, so very large
        /// solutions don't stall the UI thread enumeration.
        /// </summary>
        public const int MaxSolutionFilesToScan = 50000;
    }
}
