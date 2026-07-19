using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MegaCallstack.Services
{
    public sealed class CreateBookmarkDetails
    {
        public int OriginalLine { get; set; }
        public FuzzyBookmark Bookmark { get; set; }
        public ScopeNodeSummary DeepestScope { get; set; }
    }

    public sealed class ResolveBookmarkDetails
    {
        public int OriginalLine { get; set; }
        public FuzzyBookmark Bookmark { get; set; }
        public double Seed { get; set; }
        public ScopeMatchSummary ScopeMatch { get; set; }
    }

    public sealed class ResolveDecisionDetails
    {
        public int InputLineCount { get; set; }
        public double Seed { get; set; }
        public ScopeMatchSummary ScopeMatch { get; set; }
        public int L1CandidateCount { get; set; }
        public List<int> L1CandidateLines { get; set; } = new List<int>();
        public bool L2aFullContextMatched { get; set; }
        public int? L2aMatchedLine { get; set; }
        public int PartialK { get; set; }
        public bool L2bPartialContextMatched { get; set; }
        public int? L2bMatchedLine { get; set; }
        public int L3SearchStart { get; set; }
        public int L3SearchEnd { get; set; }
        public bool L3FuzzyMatched { get; set; }
        public int? L3BestLine { get; set; }
        public double L3BestSimilarity { get; set; }
        public ResolveResult Result { get; set; }
    }

    public sealed class ScopeNodeSummary
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public float Rank { get; set; }
        public string Path { get; set; }

        public static ScopeNodeSummary FromNode(ScopeNode node)
        {
            if (node == null) return null;
            return new ScopeNodeSummary
            {
                Name = node.Name,
                Type = node.Type,
                StartLine = node.StartLine,
                EndLine = node.EndLine,
                Rank = node.Rank,
                Path = BuildPath(node)
            };
        }

        private static string BuildPath(ScopeNode node)
        {
            var parts = new List<string>();
            for (var n = node; n != null && n.Type != "Root"; n = n.Parent)
            {
                if (n.Type == "Filler") continue;
                parts.Add($"[{n.Type}] {n.Name}");
            }
            parts.Reverse();
            return string.Join(" -> ", parts);
        }
    }

    public sealed class ScopeMatchSummary
    {
        public bool HasScopePath { get; set; }
        public int ScopePathLength { get; set; }
        public int MatchedDepth { get; set; }
        public int RankDistSum { get; set; }
        public bool HadAmbiguity { get; set; }
        public ScopeNodeSummary MatchedScope { get; set; }
    }

    public sealed class SerializableScopeNode
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int IndentLevel { get; set; }
        public float Rank { get; set; }
        public List<SerializableScopeNode> Children { get; set; } = new List<SerializableScopeNode>();

        public static SerializableScopeNode FromScopeNode(ScopeNode node)
        {
            if (node == null) return null;
            var result = new SerializableScopeNode
            {
                Name = node.Name,
                Type = node.Type,
                StartLine = node.StartLine,
                EndLine = node.EndLine,
                IndentLevel = node.IndentLevel,
                Rank = node.Rank
            };
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    result.Children.Add(FromScopeNode(child));
            }
            return result;
        }
    }

    public static class FuzzyBookmarkDiagnosticsFormatter
    {
        public static string FormatBookmarkCreated(CreateBookmarkDetails details)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Bookmark Created ===");
            sb.AppendLine($"Original line: {details.OriginalLine}");
            sb.AppendLine($"Line content: {details.Bookmark?.LineContent}");
            sb.AppendLine($"Line hash: {details.Bookmark?.LineHash}");
            sb.AppendLine($"Ratio: {details.Bookmark?.Ratio.ToString("0.000", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Scope path: {FormatScopePath(details.Bookmark?.ScopePath)}");
            sb.AppendLine($"Pre-context hashes: {FormatHashes(details.Bookmark?.PreContextHashes)}");
            sb.AppendLine($"Post-context hashes: {FormatHashes(details.Bookmark?.PostContextHashes)}");
            if (details.DeepestScope != null)
            {
                sb.AppendLine($"Deepest scope: {details.DeepestScope.Path}");
                sb.AppendLine($"  StartLine={details.DeepestScope.StartLine} EndLine={details.DeepestScope.EndLine} Rank={details.DeepestScope.Rank.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                sb.AppendLine("Deepest scope: (none / file-level)");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static string FormatBookmarkResolved(ResolveBookmarkDetails input, ResolveDecisionDetails details)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Bookmark Resolved ===");
            sb.AppendLine($"Original line: {input.OriginalLine}");
            sb.AppendLine($"Line content: {input.Bookmark?.LineContent}");
            sb.AppendLine($"Scope path: {FormatScopePath(input.Bookmark?.ScopePath)}");
            sb.AppendLine($"Ratio: {input.Bookmark?.Ratio.ToString("0.000", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Input line count: {details.InputLineCount}");
            sb.AppendLine($"Seed: {details.Seed.ToString("0.000", CultureInfo.InvariantCulture)}");
            if (details.ScopeMatch != null)
            {
                var sm = details.ScopeMatch;
                sb.AppendLine($"Scope match: HasPath={sm.HasScopePath}, PathLength={sm.ScopePathLength}, MatchedDepth={sm.MatchedDepth}, RankDistSum={sm.RankDistSum}, HadAmbiguity={sm.HadAmbiguity}");
                if (sm.MatchedScope != null)
                    sb.AppendLine($"  Matched scope: {sm.MatchedScope.Path} [{sm.MatchedScope.StartLine},{sm.MatchedScope.EndLine}]");
            }
            sb.AppendLine($"L1 exact candidates: {details.L1CandidateCount}");
            if (details.L1CandidateCount > 0)
                sb.AppendLine($"  candidate lines: {string.Join(", ", details.L1CandidateLines)}");
            sb.AppendLine($"L2a full context: matched={details.L2aFullContextMatched}, line={FormatNullableInt(details.L2aMatchedLine)}");
            sb.AppendLine($"L2b partial context: K={details.PartialK}, matched={details.L2bPartialContextMatched}, line={FormatNullableInt(details.L2bMatchedLine)}");
            sb.AppendLine($"L3 fuzzy search window: [{details.L3SearchStart},{details.L3SearchEnd}]");
            sb.AppendLine($"L3 fuzzy: matched={details.L3FuzzyMatched}, bestLine={FormatNullableInt(details.L3BestLine)}, bestSimilarity={details.L3BestSimilarity.ToString("0.000", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Result: line={details.Result?.Line}, confidence={details.Result?.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}, matchLevel={details.Result?.MatchLevel}");
            sb.AppendLine();
            return sb.ToString();
        }

        private static string FormatScopePath(uint[] path)
        {
            if (path == null || path.Length == 0) return "(file-level)";
            var sb = new StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.AppendFormat(CultureInfo.InvariantCulture, "0x{0:x8}", path[i]);
            }
            return sb.ToString();
        }

        private static string FormatHashes(int[] hashes)
        {
            if (hashes == null || hashes.Length == 0) return "(empty)";
            return string.Join(",", hashes);
        }

        private static string FormatNullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "(none)";
        }
    }
}
