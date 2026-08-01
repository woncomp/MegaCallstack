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
        public const string PreviousSessionFileName = "previous_session.json";
        public const string DiagnosticsFolderName = "Diagnostics";

        /// <summary>
        /// Schema version for session.json. Increment when the session metadata
        /// persistence format changes in a non-backward-compatible way.
        /// </summary>
        public const int SessionSchemaVersion = 0;
    }
}
