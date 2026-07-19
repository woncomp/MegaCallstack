using System.Collections.Generic;

namespace MegaCallstack.Services
{
    public interface IFuzzyBookmarkDiagnostics
    {
        string BeginOperation(string filePath);
        void OnScopeParsed(string operationId, ScopeNode root, IReadOnlyList<string> lines);
        void OnBookmarkCreated(string operationId, int originalLine, FuzzyBookmark bookmark, ScopeNodeSummary deepestScope);
        void OnBookmarksResolved(string operationId, IReadOnlyList<ResolveBookmarkDetails> details);
        void CompleteOperation(string operationId);
    }
}
