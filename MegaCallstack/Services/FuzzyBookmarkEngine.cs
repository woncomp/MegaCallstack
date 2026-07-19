////////////////////////////////////////////////////////////////////////////////////////////////////
//  FuzzyBookmarkEngine — a self-contained, single-file fuzzy code-position bookmark engine.
//
//  PROBLEM
//  -------
//  Recording a source location by (file, line number) is brittle: any edit that inserts or
//  deletes lines above the bookmark shifts the recorded number onto the wrong line. This
//  engine encodes a source line by CONTENT plus CONTEXT and by a structural (Scope) anchor,
//  so a bookmark can be relocated in a file that has since been edited.
//
//  HOW IT WORKS
//  ------------
//  A bookmark (FuzzyBookmark) stores, for one target line:
//    * LineContent          — normalized text of the line (trimmed, whitespace-collapsed).
//    * LineHash             — FNV-1a of LineContent (a quick pre-filter).
//    * ScopePath            — identity of the enclosing Scope chain (outer->inner). Each element
//                             packs (FNV-1a(Scope.Name) low 24 bits) | (quantized Scope.Rank 8 bits).
//                             Empty array means the line lives outside any real Scope.
//    * Ratio                — position of the line within its deepest Scope, in [0,1]. This is the
//                             position anchor: it REPLACES an absolute line number and survives
//                             insertions/deletions above the bookmark. When the deepest Scope can no
//                             longer be found, the same Ratio is reused against a parent Scope.
//    * Pre/PostContextHashes— per-line FNV-1a hashes of the normalized lines above/below the target.
//                             Variable length: for a target line that appears N times, the window
//                             expands (up to MaxContextSpan) until each side holds >= min(N,5)
//                             differing lines, which lets duplicate occurrences be disambiguated.
//
//  Resolution (Resolve) relocates the bookmark in a possibly-edited file through a tiered strategy,
//  preferring a confident miss over a wrong match:
//    L1  Exact content      — first line whose normalized text equals LineContent.
//    L2a Full context       — among L1 candidates, one whose [Pre, LineHash, Post] all line up.
//    L2b Partial context    — among L1 candidates, the one with the most matching context hashes
//                             (>= K, where K adapts to the window length); ties broken by nearest to seed.
//    L3  Fuzzy content      — within a window around the seed, the line with the highest mixed
//                             similarity (0.5*Jaccard + 0.5*normalized-Levenshtein) if it clears
//                             FuzzyThreshold. Catches lightly-edited lines (e.g. a changed literal).
//    L4  Fallback           — clamp the seed-derived line into range. Always yields a line, but with
//                             low confidence (the caller decides whether to use it or give up).
//
//  The "seed" is derived from (ScopePath, Ratio): the engine matches the Scope path against the
//  target file's Scope tree (by name hash, breaking same-name ties by Rank proximity, narrowing
//  layer by layer); if a layer cannot be matched it falls back to the parent layer, and ultimately
//  to a file-level position (Ratio * lineCount). The seed is only a starting point — the content
//  tiers (L1/L2/L3) do the actual relocation.
//
//  Each result carries a Confidence in [0,1] and a MatchLevel string so callers can decide whether
//  to trust, prompt, or silently fall back.
//
//  PORTABILITY
//  -----------
//  This single .cs file is intentionally self-contained. It depends only on the .NET BCL
//  (System, System.Collections.Generic, System.Globalization, System.IO, System.Linq,
//  System.Text, System.Text.RegularExpressions) — no EnvDTE, no Visual Studio SDK, no other
//  project code. To reuse in another project: copy this one file, optionally rename the namespace.
//
//  USAGE
//  -----
//    var engine = new FuzzyBookmarkEngine();
//
//    // Capture a bookmark at line 42 of a file:
//    FuzzyBookmark bm = engine.Create(filePath, 42);
//    // ... or from in-memory lines:
//    // FuzzyBookmark bm = engine.Create(lines, 42);
//
//    // Relocate it later, after the file may have been edited:
//    ResolveResult r = engine.Resolve(bm, filePath);
//    if (r.Line > 0) { /* navigate to r.Line; check r.Confidence / r.MatchLevel as needed */ }
//
//    // Batch relocate many bookmarks against one file (parses/normalizes the file once):
//    IReadOnlyList<ResolveResult> results = engine.ResolveAll(bookmarks, lines);
//
//  FNV-1a hashing is exposed as FuzzyBookmarkEngine.FNV1a(int hash, string value) so callers that
//  previously relied on a separate hash helper can use this file as the single source of FNV-1a.
////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Generates and resolves fuzzy code-position bookmarks. A bookmark encodes a source line by
    /// normalized content, per-line context hashes, and a (ScopePath, Ratio) position anchor.
    /// Resolution relocates the line in a possibly-edited file through a tiered strategy: exact
    /// content, full context window, partial context, fuzzy similarity, then fallback. See the
    /// file header for the full design.
    /// </summary>
    public sealed class FuzzyBookmarkEngine
    {
        private readonly FuzzyBookmarkOptions _options;
        private readonly LightScopeParser _parser = new LightScopeParser();
        private readonly IFuzzyBookmarkDiagnostics _diagnostics;

        public FuzzyBookmarkEngine() : this(new FuzzyBookmarkOptions(), null) { }

        public FuzzyBookmarkEngine(FuzzyBookmarkOptions options) : this(options, null) { }

        public FuzzyBookmarkEngine(IFuzzyBookmarkDiagnostics diagnostics) : this(new FuzzyBookmarkOptions(), diagnostics) { }

        public FuzzyBookmarkEngine(FuzzyBookmarkOptions options, IFuzzyBookmarkDiagnostics diagnostics)
        {
            _options = options ?? new FuzzyBookmarkOptions();
            _diagnostics = diagnostics;
        }

        // ---------- Create ----------

        public IReadOnlyList<FuzzyBookmark> CreateAll(IEnumerable<int> lineNumbers, string filePath)
        {
            if (lineNumbers == null) throw new ArgumentNullException(nameof(lineNumbers));
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            var lines = ReadAllLinesSafe(filePath);
            if (lines == null)
                throw new FileNotFoundException("Source file not found.", filePath);

            string operationId = _diagnostics?.BeginOperation(filePath);
            try
            {
                var normalized = NormalizeAll(lines);
                var root = _parser.Parse(lines);
                _diagnostics?.OnScopeParsed(operationId, root, lines);

                var result = new List<FuzzyBookmark>();
                foreach (int lineNumber in lineNumbers)
                {
                    if (lineNumber < 1 || lineNumber > lines.Length)
                    {
                        _diagnostics?.OnBookmarkCreated(operationId, lineNumber, null, null);
                        result.Add(null);
                        continue;
                    }

                    var bookmark = CreateCore(normalized, root, lineNumber);
                    _diagnostics?.OnBookmarkCreated(operationId, lineNumber, bookmark, ScopeNodeSummary.FromNode(FindInnermostRealScope(root, lineNumber - 1)));
                    result.Add(bookmark);
                }
                return result;
            }
            finally
            {
                _diagnostics?.CompleteOperation(operationId);
            }
        }

        private FuzzyBookmark CreateCore(string[] normalized, ScopeNode root, int lineNumber)
        {
            int idx0 = lineNumber - 1;
            var node = FindInnermostRealScope(root, idx0);

            var bookmark = new FuzzyBookmark
            {
                LineContent = normalized[idx0],
                LineHash = FNV1a(0, normalized[idx0])
            };

            if (node != null)
            {
                bookmark.ScopePath = BuildScopePath(node);
                int span = node.EndLine - node.StartLine;
                bookmark.Ratio = span > 0
                    ? Clamp((double)(idx0 - node.StartLine) / span, 0.0, 1.0)
                    : 0.0;
            }
            else
            {
                bookmark.ScopePath = new uint[0];
                bookmark.Ratio = normalized.Length > 1
                    ? Clamp((double)idx0 / (normalized.Length - 1), 0.0, 1.0)
                    : 0.0;
            }

            // Dynamic context expansion for duplicate target lines.
            int n = CountOccurrences(normalized, bookmark.LineContent);
            int ctxLen = _options.ContextSpan;
            if (n > 1)
            {
                int target = Math.Min(n, _options.MaxContextSpan);
                ctxLen = ExpandContext(normalized, idx0, target, _options.ContextSpan, _options.MaxContextSpan, bookmark.LineContent);
            }

            bookmark.PreContextHashes = HashRange(normalized, idx0 - ctxLen, idx0 - 1);
            bookmark.PostContextHashes = HashRange(normalized, idx0 + 1, idx0 + ctxLen);
            return bookmark;
        }

        // ---------- Resolve ----------

        public IReadOnlyList<ResolveResult> ResolveAll(IEnumerable<FuzzyBookmark> bookmarks, string filePath)
        {
            if (bookmarks == null) throw new ArgumentNullException(nameof(bookmarks));

            var lines = ReadAllLinesSafe(filePath);
            if (lines == null || lines.Length == 0)
                return bookmarks.Select(_ => new ResolveResult(0, 0.0, "NotFound")).ToList();

            string operationId = _diagnostics?.BeginOperation(filePath);
            try
            {
                return ResolveAllCore(bookmarks, lines, operationId);
            }
            finally
            {
                _diagnostics?.CompleteOperation(operationId);
            }
        }

        private IReadOnlyList<ResolveResult> ResolveAll(IEnumerable<FuzzyBookmark> bookmarks, IReadOnlyList<string> lines)
        {
            return ResolveAllCore(bookmarks, lines, null);
        }

        private IReadOnlyList<ResolveResult> ResolveAllCore(IEnumerable<FuzzyBookmark> bookmarks, IReadOnlyList<string> lines, string operationId)
        {
            if (lines == null || lines.Count == 0)
                return bookmarks.Select(_ => new ResolveResult(0, 0.0, "NotFound")).ToList();

            var normalized = NormalizeAll(lines);
            var root = _parser.Parse(ToArray(lines));
            _diagnostics?.OnScopeParsed(operationId, root, lines);

            var resolvedDetails = _diagnostics != null ? new List<ResolveBookmarkDetails>() : null;
            var results = new List<ResolveResult>();
            foreach (var bm in bookmarks)
            {
                if (bm == null) { results.Add(new ResolveResult(0, 0.0, "NotFound")); continue; }
                var scopeMatch = MatchScopes(bm.ScopePath, root);
                double seed0 = DeriveSeed(bm.Ratio, scopeMatch.Node, lines.Count);
                results.Add(ResolveCore(bm, normalized, seed0, scopeMatch, operationId, resolvedDetails));
            }
            _diagnostics?.OnBookmarksResolved(operationId, resolvedDetails);
            return results;
        }

        // ---------- Core resolution ----------

        private ResolveResult ResolveCore(FuzzyBookmark bookmark, string[] normalized, double seed0, ScopeMatch scopeMatch, string operationId, List<ResolveBookmarkDetails> resolvedDetails)
        {
            int lineCount = normalized.Length;
            var details = new ResolveDecisionDetails
            {
                InputLineCount = lineCount,
                Seed = seed0,
                ScopeMatch = Summarize(scopeMatch)
            };

            // L1: exact content candidates.
            var candidates = CollectExactCandidates(normalized, bookmark.LineContent, bookmark.LineHash);
            details.L1CandidateCount = candidates.Count;
            details.L1CandidateLines = candidates.Select(c => c + 1).ToList();

            // L2: context disambiguation among L1 candidates (and the only source of a hit when L1 is high-freq).
            if (candidates.Count > 0)
            {
                bool highFreq = candidates.Count >= _options.HighFreqThreshold;

                // L2a: full context-window match (Pre + LineHash + Post all line up).
                var fullMatch = FindFullContextMatch(candidates, normalized, bookmark);
                details.L2aFullContextMatched = fullMatch.HasValue;
                details.L2aMatchedLine = fullMatch;
                if (fullMatch.HasValue)
                {
                    var result = BuildResult(fullMatch.Value, scopeMatch, "ContextFull", 0.9);
                    details.Result = result;
                    RecordResolvedDetails(resolvedDetails, bookmark, details);
                    return result;
                }

                // L2b: partial context match. Only meaningful when there is more than one candidate;
                // a unique candidate with no full match is treated as ambiguous -> fall through to L2b/L3.
                int k = ComputePartialK(bookmark);
                details.PartialK = k;
                var partial = FindPartialContextMatch(candidates, normalized, bookmark, k, seed0);
                details.L2bPartialContextMatched = partial.HasValue;
                details.L2bMatchedLine = partial;
                if (partial.HasValue)
                {
                    var result = BuildResult(partial.Value, scopeMatch, "ContextPartial", 0.7);
                    details.Result = result;
                    RecordResolvedDetails(resolvedDetails, bookmark, details);
                    return result;
                }

                // If L1 had a small number of exact candidates but none survived L2, do not silently
                // trust one of them; let L3 try, then fallback.
                if (!highFreq && candidates.Count == 1)
                {
                    var result = BuildResult(ClampLine((int)Math.Round(seed0) + 1, lineCount), scopeMatch, "Fallback", 0.1);
                    details.Result = result;
                    RecordResolvedDetails(resolvedDetails, bookmark, details);
                    return result;
                }
            }

            // L3: fuzzy content within the search window.
            int fuzzyLine = FindFuzzyMatch(normalized, bookmark.LineContent, seed0, out int fuzzyStart, out int fuzzyEnd, out double bestSim);
            details.L3SearchStart = fuzzyStart;
            details.L3SearchEnd = fuzzyEnd;
            details.L3FuzzyMatched = fuzzyLine > 0;
            details.L3BestLine = fuzzyLine > 0 ? (int?)fuzzyLine : null;
            details.L3BestSimilarity = bestSim;
            if (fuzzyLine > 0)
            {
                var result = BuildResult(fuzzyLine, scopeMatch, "Fuzzy", 0.5);
                details.Result = result;
                RecordResolvedDetails(resolvedDetails, bookmark, details);
                return result;
            }

            // L4: clamp fallback.
            var fallback = BuildResult(ClampLine((int)Math.Round(seed0) + 1, lineCount), scopeMatch, "Fallback", 0.1);
            details.Result = fallback;
            RecordResolvedDetails(resolvedDetails, bookmark, details);
            return fallback;
        }

        private static void RecordResolvedDetails(List<ResolveBookmarkDetails> resolvedDetails, FuzzyBookmark bookmark, ResolveDecisionDetails details)
        {
            if (resolvedDetails == null) return;
            resolvedDetails.Add(new ResolveBookmarkDetails
            {
                OriginalLine = details.Result?.Line ?? 0,
                Bookmark = bookmark,
                Seed = details.Seed,
                ScopeMatch = details.ScopeMatch,
                Decision = details
            });
        }

        private static ScopeMatchSummary Summarize(ScopeMatch match)
        {
            if (match == null) return null;
            return new ScopeMatchSummary
            {
                HasScopePath = match.HasScopePath,
                ScopePathLength = match.ScopePathLength,
                MatchedDepth = match.MatchedDepth,
                RankDistSum = match.RankDistSum,
                HadAmbiguity = match.HadAmbiguity,
                MatchedScope = ScopeNodeSummary.FromNode(match.Node)
            };
        }

        private ResolveResult BuildResult(int line, ScopeMatch scopeMatch, string matchLevel, double levelScore)
        {
            double scopeScore = scopeMatch.HasScopePath
                ? (scopeMatch.ScopePathLength > 0 ? (double)scopeMatch.MatchedDepth / scopeMatch.ScopePathLength : 1.0)
                : 1.0; // file-level bookmark: do not penalize.
            double rankScore = scopeMatch.HadAmbiguity
                ? Clamp(1.0 - (scopeMatch.RankDistSum / (255.0 * Math.Max(1, scopeMatch.ScopePathLength))), 0.0, 1.0)
                : 1.0;
            double confidence = Clamp(0.4 * scopeScore + 0.3 * rankScore + 0.3 * levelScore, 0.0, 1.0);
            if (line == 0) matchLevel = "NotFound";
            return new ResolveResult(line, confidence, matchLevel);
        }

        // ---------- L1: exact content ----------

        private List<int> CollectExactCandidates(string[] normalized, string lineContent, int lineHash)
        {
            var result = new List<int>();
            if (lineContent == null) return result;
            for (int i = 0; i < normalized.Length; i++)
            {
                // Hash quick pre-filter then exact compare to rule out hash collisions.
                if (normalized[i] != null && FNV1a(0, normalized[i]) == lineHash && normalized[i] == lineContent)
                    result.Add(i); // 0-based
            }
            return result;
        }

        // ---------- L2a: full context window ----------

        private int? FindFullContextMatch(List<int> candidates, string[] normalized, FuzzyBookmark bookmark)
        {
            int[] pre = bookmark.PreContextHashes ?? new int[0];
            int[] post = bookmark.PostContextHashes ?? new int[0];
            foreach (int c in candidates) // 0-based
            {
                if (ContextWindowMatches(normalized, c, pre, post))
                    return c + 1;
            }
            return null;
        }

        private bool ContextWindowMatches(string[] normalized, int idx0, int[] pre, int[] post)
        {
            // Pre: normalized[idx0-pre.Length .. idx0-1] must equal pre hashes (in order).
            int preStart = idx0 - pre.Length;
            if (preStart < 0) return false;
            for (int i = 0; i < pre.Length; i++)
            {
                if (HashOf(normalized, preStart + i) != pre[i]) return false;
            }
            // Post: normalized[idx0+1 .. idx0+post.Length] must equal post hashes (in order).
            int postEnd = idx0 + post.Length;
            if (postEnd >= normalized.Length) return false;
            for (int i = 0; i < post.Length; i++)
            {
                if (HashOf(normalized, idx0 + 1 + i) != post[i]) return false;
            }
            return true;
        }

        // ---------- L2b: partial context ----------

        private int? FindPartialContextMatch(List<int> candidates, string[] normalized, FuzzyBookmark bookmark, int k, double seed0)
        {
            if (candidates.Count <= 1) return null; // nothing to disambiguate
            int[] pre = bookmark.PreContextHashes ?? new int[0];
            int[] post = bookmark.PostContextHashes ?? new int[0];

            int bestCount = -1;
            int bestLine = 0; // 1-based
            double bestDist = double.MaxValue;
            foreach (int c in candidates) // 0-based
            {
                int count = CountContextMatches(normalized, c, pre, post);
                if (count < k) continue;
                double dist = Math.Abs(c - seed0);
                if (count > bestCount || (count == bestCount && dist < bestDist))
                {
                    bestCount = count;
                    bestLine = c + 1;
                    bestDist = dist;
                }
            }
            return bestCount >= k ? (int?)bestLine : null;
        }

        private static int CountContextMatches(string[] normalized, int idx0, int[] pre, int[] post)
        {
            int count = 0;
            int preStart = idx0 - pre.Length;
            for (int i = 0; i < pre.Length; i++)
            {
                int li = preStart + i;
                if (li >= 0 && li < normalized.Length && HashOf(normalized, li) == pre[i]) count++;
            }
            for (int i = 0; i < post.Length; i++)
            {
                int li = idx0 + 1 + i;
                if (li >= 0 && li < normalized.Length && HashOf(normalized, li) == post[i]) count++;
            }
            return count;
        }

        private int ComputePartialK(FuzzyBookmark bookmark)
        {
            int pre = bookmark.PreContextHashes?.Length ?? 0;
            int post = bookmark.PostContextHashes?.Length ?? 0;
            int total = pre + post;
            if (total == 0) return 1;
            return Math.Max(1, (2 * total) / 3);
        }

        // ---------- L3: fuzzy content ----------

        private int FindFuzzyMatch(string[] normalized, string lineContent, double seed0)
        {
            int dummyStart, dummyEnd;
            double dummySim;
            return FindFuzzyMatch(normalized, lineContent, seed0, out dummyStart, out dummyEnd, out dummySim);
        }

        private int FindFuzzyMatch(string[] normalized, string lineContent, double seed0, out int start, out int end, out double bestSimilarity)
        {
            start = 0;
            end = 0;
            bestSimilarity = 0.0;
            if (string.IsNullOrEmpty(lineContent)) return 0;
            int lineCount = normalized.Length;

            int win = _options.SearchWindow;
            if (win <= 0) { start = 0; end = lineCount - 1; }
            else
            {
                int center = (int)Math.Round(seed0);
                start = Math.Max(0, center - win);
                end = Math.Min(lineCount - 1, center + win);
            }

            double best = -1.0;
            int bestLine = 0;
            double bestDist = double.MaxValue;
            var targetTokens = Tokenize(lineContent);

            for (int i = start; i <= end; i++)
            {
                string line = normalized[i];
                if (string.IsNullOrEmpty(line)) continue;
                // Performance guard: skip very long lines.
                if (line.Length > 10240) continue;

                double sim = MixedSimilarity(line, lineContent, targetTokens);
                if (sim < _options.FuzzyThreshold) continue;
                double dist = Math.Abs(i - seed0);
                if (sim > best || (Math.Abs(sim - best) < 1e-9 && dist < bestDist))
                {
                    best = sim;
                    bestLine = i + 1;
                    bestDist = dist;
                }
            }
            bestSimilarity = best;
            return best >= _options.FuzzyThreshold ? bestLine : 0;
        }

        private double MixedSimilarity(string a, string b, HashSet<string> bTokens)
        {
            double j = Jaccard(a, b, bTokens);
            double l = NormalizedLevenshtein(a, b);
            return 0.5 * j + 0.5 * l;
        }

        private static double Jaccard(string a, string b, HashSet<string> bTokens)
        {
            var aTokens = Tokenize(a);
            if (aTokens.Count == 0 && bTokens.Count == 0) return 1.0;
            if (aTokens.Count == 0 || bTokens.Count == 0) return 0.0;
            int inter = 0;
            foreach (var t in aTokens) if (bTokens.Contains(t)) inter++;
            int union = aTokens.Count + bTokens.Count - inter;
            return union == 0 ? 0.0 : (double)inter / union;
        }

        private static double NormalizedLevenshtein(string a, string b)
        {
            if (a == b) return 1.0;
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;
            int dist = Levenshtein(a, b);
            return 1.0 - (double)dist / maxLen;
        }

        private static int Levenshtein(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[m];
        }

        private static HashSet<string> Tokenize(string s)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(s)) return set;
            int start = -1;
            for (int i = 0; i <= s.Length; i++)
            {
                bool isWord = i < s.Length && IsTokenChar(s[i]);
                if (isWord)
                {
                    if (start < 0) start = i;
                }
                else
                {
                    if (start >= 0)
                    {
                        set.Add(s.Substring(start, i - start));
                        start = -1;
                    }
                }
            }
            return set;
        }

        private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // ---------- Scope matching ----------

        private ScopeMatch MatchScopes(uint[] scopePath, ScopeNode root)
        {
            var match = new ScopeMatch { HasScopePath = scopePath != null && scopePath.Length > 0 };
            if (match.HasScopePath)
            {
                match.ScopePathLength = scopePath.Length;
                ScopeNode currentRoot = root;
                for (int i = 0; i < scopePath.Length; i++)
                {
                    int storedName = FuzzyBookmark.NameHashOf(scopePath[i]);
                    int storedRank = FuzzyBookmark.RankOf(scopePath[i]);

                    var hits = new List<ScopeNode>();
                    CollectNameHashMatches(currentRoot, storedName, hits);

                    if (hits.Count == 0) break;
                    if (hits.Count > 1) match.HadAmbiguity = true;

                    ScopeNode best = null;
                    int bestDist = int.MaxValue;
                    foreach (var h in hits)
                    {
                        int r8 = QuantizeRank(h.Rank);
                        int d = Math.Abs(r8 - storedRank);
                        if (d < bestDist) { bestDist = d; best = h; }
                    }
                    match.MatchedDepth = i + 1;
                    match.RankDistSum += bestDist;
                    match.Node = best;
                    currentRoot = best; // narrow to best subtree
                }
            }
            return match;
        }

        private static void CollectNameHashMatches(ScopeNode node, int nameHash24, List<ScopeNode> hits)
        {
            if (node == null) return;
            if (node.Type != "Root" && node.Type != "Filler")
            {
                if ((FNV1a(0, node.Name ?? "") & 0xFFFFFF) == nameHash24)
                    hits.Add(node);
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectNameHashMatches(child, nameHash24, hits);
            }
        }

        private static double DeriveSeed(double ratio, ScopeNode node, int lineCount)
        {
            if (node != null)
            {
                int span = node.EndLine - node.StartLine;
                return span > 0 ? node.StartLine + ratio * span : node.StartLine;
            }
            return lineCount > 1 ? ratio * (lineCount - 1) : 0;
        }

        // ---------- Scope helpers (Create side) ----------

        private ScopeNode FindInnermostRealScope(ScopeNode root, int idx0)
        {
            // Descend to the deepest child whose [StartLine,EndLine] contains idx0.
            ScopeNode current = root;
            while (current.Children != null && current.Children.Count > 0)
            {
                ScopeNode next = null;
                foreach (var child in current.Children)
                {
                    if (idx0 >= child.StartLine && idx0 <= child.EndLine) { next = child; break; }
                }
                if (next == null) break;
                current = next;
            }
            // Walk up to the first real (non-Filler, non-Root) ancestor.
            while (current != null && (current.Type == "Filler" || current.Type == "Root"))
                current = current.Parent;
            return current;
        }

        private static uint[] BuildScopePath(ScopeNode node)
        {
            var path = new List<ScopeNode>();
            for (var n = node; n != null && n.Type != "Root"; n = n.Parent)
            {
                if (n.Type == "Filler") continue;
                path.Add(n);
            }
            path.Reverse(); // outer-to-inner
            var ids = new uint[path.Count];
            for (int i = 0; i < path.Count; i++)
                ids[i] = FuzzyBookmark.PackScopeId(FNV1a(0, path[i].Name ?? ""), path[i].Rank);
            return ids;
        }

        // ---------- Normalization ----------

        private string[] NormalizeAll(IReadOnlyList<string> lines)
        {
            var result = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                result[i] = NormalizeLine(lines[i]);
            return result;
        }

        private string NormalizeLine(string line)
        {
            if (line == null) return string.Empty;
            // Trim CR/LF and surrounding whitespace first.
            string s = line.Trim('\r', '\n');
            s = s.Trim();
            if (s.Length == 0) return string.Empty;

            var sb = new StringBuilder(s.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\t') c = ' ';
                if (c == ' ')
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            string normalized = sb.ToString();

            if (_options.NormalizeUnicode)
                normalized = NormalizeUnicode(normalized);
            return normalized;
        }

        private static string NormalizeUnicode(string s)
        {
            // NFC composition.
            string nfc = s.Normalize(NormalizationForm.FormC);
            // Full-width -> half-width (ASCII range).
            var sb = new StringBuilder(nfc.Length);
            foreach (char c in nfc)
            {
                int u = c;
                if (u >= 0xFF01 && u <= 0xFF5E) sb.Append((char)(u - 0xFEE0));
                else if (u == 0x3000) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------- Context expansion ----------

        private int ExpandContext(string[] normalized, int idx0, int target, int minLen, int maxLen, string lineContent)
        {
            for (int len = minLen; len <= maxLen; len++)
            {
                int preDiff = CountDifferingInRange(normalized, idx0 - len, idx0 - 1, lineContent);
                int postDiff = CountDifferingInRange(normalized, idx0 + 1, idx0 + len, lineContent);
                if (preDiff >= target && postDiff >= target) return len;
            }
            return maxLen;
        }

        private static int CountDifferingInRange(string[] normalized, int from, int to, string lineContent)
        {
            int count = 0;
            for (int i = from; i <= to; i++)
            {
                if (i < 0 || i >= normalized.Length) continue;
                if (normalized[i] != lineContent) count++;
            }
            return count;
        }

        // ---------- Small utilities ----------

        private static int CountOccurrences(string[] normalized, string value)
        {
            if (value == null) return 0;
            int count = 0;
            for (int i = 0; i < normalized.Length; i++)
                if (normalized[i] == value) count++;
            return count;
        }

        private static int[] HashRange(string[] normalized, int from, int to)
        {
            if (from > to) return new int[0];
            var list = new List<int>(to - from + 1);
            for (int i = from; i <= to; i++)
            {
                int li = Math.Max(0, Math.Min(normalized.Length - 1, i));
                list.Add(HashOf(normalized, li));
            }
            return list.ToArray();
        }

        private static int HashOf(string[] normalized, int index)
        {
            if (index < 0 || index >= normalized.Length) return 0;
            return FNV1a(0, normalized[index] ?? string.Empty);
        }

        private static int QuantizeRank(float rank)
        {
            if (rank < 0f) rank = 0f;
            if (rank > 1f) rank = 1f;
            return (int)Math.Round(rank * 255f);
        }

        private static int ClampLine(int line, int lineCount)
        {
            if (lineCount <= 0) return 0;
            if (line < 1) return 1;
            if (line > lineCount) return lineCount;
            return line;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        private static string[] ToArray(IReadOnlyList<string> lines)
        {
            if (lines is string[] arr) return arr;
            var result = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) result[i] = lines[i];
            return result;
        }

        private static string[] ReadAllLinesSafe(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                return File.ReadAllLines(filePath);
            }
            catch { return null; }
        }

        /// <summary>
        /// FNV-1a (32-bit) hash. Chains from <paramref name="hash"/>; null/empty input returns
        /// the input hash unchanged. Identical to the legacy HashUtils.FNV1a implementation so
        /// this file is a drop-in single source of FNV-1a.
        /// </summary>
        public static int FNV1a(int hash, string value)
        {
            unchecked
            {
                if (!string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619;
                    }
                }
                return hash;
            }
        }

        // ---------- Scope match carrier ----------

        private sealed class ScopeMatch
        {
            public bool HasScopePath;
            public int ScopePathLength;
            public int MatchedDepth;
            public int RankDistSum;
            public bool HadAmbiguity;
            public ScopeNode Node;
        }
    }

    /// <summary>
    /// A fuzzy code-position bookmark. Encodes a source line by content plus enough context to
    /// relocate the line after the file has been edited. Position is anchored to a Scope path +
    /// ratio rather than an absolute line number, so it survives insertions/deletions above the
    /// bookmark. See the file header for the full design.
    /// </summary>
    public sealed class FuzzyBookmark
    {
        /// <summary>Normalized text of the target line (trimmed, whitespace-collapsed, tab->space).</summary>
        public string LineContent { get; set; }

        /// <summary>FNV-1a hash of <see cref="LineContent"/>; used as an L1 quick pre-filter.</summary>
        public int LineHash { get; set; }

        /// <summary>
        /// Scope identity path, outer-to-inner. Each element packs
        /// (FNV-1a(Scope.Name) low 24 bits) into the high 24 bits and the quantized Scope.Rank
        /// (8 bits) into the low 8 bits. Empty array means file-level (no enclosing real Scope).
        /// </summary>
        public uint[] ScopePath { get; set; }

        /// <summary>Position within the deepest matched Scope, [0,1]. Reused when falling back to a parent Scope.</summary>
        public double Ratio { get; set; }

        /// <summary>Per-line FNV-1a hashes of the normalized lines above the target. Variable length.</summary>
        public int[] PreContextHashes { get; set; }

        /// <summary>Per-line FNV-1a hashes of the normalized lines below the target. Variable length.</summary>
        public int[] PostContextHashes { get; set; }

        public FuzzyBookmark()
        {
            ScopePath = new uint[0];
            PreContextHashes = new int[0];
            PostContextHashes = new int[0];
        }

        /// <summary>Pack a 24-bit name hash and an 8-bit quantized rank into a single Scope id.</summary>
        public static uint PackScopeId(int nameHash, float rank)
        {
            int name24 = nameHash & 0xFFFFFF;
            int rank8 = (int)Math.Round(Clamp(rank, 0f, 1f) * 255f);
            return ((uint)name24 << 8) | (uint)rank8;
        }

        /// <summary>Extract the 24-bit name hash from a packed Scope id.</summary>
        public static int NameHashOf(uint scopeId) => (int)((scopeId >> 8) & 0xFFFFFF);

        /// <summary>Extract the 8-bit quantized rank from a packed Scope id.</summary>
        public static int RankOf(uint scopeId) => (int)(scopeId & 0xFF);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        public override string ToString()
        {
            string scope = ScopePath != null && ScopePath.Length > 0
                ? ScopeToString(ScopePath)
                : "<file>";
            return string.Format(CultureInfo.InvariantCulture,
                "FuzzyBookmark[scope={0}, ratio={1:0.000}, content=\"{2}\"]", scope, Ratio, LineContent);
        }

        private static string ScopeToString(uint[] path)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture, "0x{0:x8}", path[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Configurable parameters for <see cref="FuzzyBookmarkEngine"/>.</summary>
    public sealed class FuzzyBookmarkOptions
    {
        /// <summary>Default context span (N lines above and below) when the target line is unique in the file.</summary>
        public int ContextSpan { get; set; } = 2;

        /// <summary>Maximum context span when expanding to disambiguate duplicate target lines.</summary>
        public int MaxContextSpan { get; set; } = 5;

        /// <summary>Minimum mixed similarity (0..1) for an L3 fuzzy content match.</summary>
        public double FuzzyThreshold { get; set; } = 0.6;

        /// <summary>Half-window (lines) around the seed for L3 fuzzy search; 0 means the whole file.</summary>
        public int SearchWindow { get; set; } = 200;

        /// <summary>L1 candidate count above which the target line is treated as "high frequency" and L1 demotes to L2.</summary>
        public int HighFreqThreshold { get; set; } = 20;

        /// <summary>Files larger than this (bytes) skip scope/normalize work and resolve straight to fallback.</summary>
        public long MaxFileSize { get; set; } = 5_000_000;

        /// <summary>When true, normalization applies NFC + full-width-to-half-width conversion.</summary>
        public bool NormalizeUnicode { get; set; } = true;
    }

    /// <summary>Outcome of relocating a <see cref="FuzzyBookmark"/> against a (possibly edited) file.</summary>
    public sealed class ResolveResult
    {
        /// <summary>Resolved 1-based line number. 0 means confidently not found.</summary>
        public int Line { get; set; }

        /// <summary>Confidence in the resolution, 0..1.</summary>
        public double Confidence { get; set; }

        /// <summary>How the line was matched: Exact|ContextFull|ContextPartial|Fuzzy|Fallback|NotFound.</summary>
        public string MatchLevel { get; set; }

        public ResolveResult() { }

        public ResolveResult(int line, double confidence, string matchLevel)
        {
            Line = line;
            Confidence = confidence;
            MatchLevel = matchLevel;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "ResolveResult[{0}] line={1} conf={2:0.000}", MatchLevel, Line, Confidence);
    }

    /// <summary>
    /// A lightweight, brace-and-regex based scope parser. Splits a source file into nested
    /// ScopeNodes (namespace/class/struct/interface/enum/function) plus Filler nodes that tile the
    /// gaps between them. Each node carries a Rank in [0,1] describing its relative size among
    /// same-named siblings (0 = smallest/unique, 1 = largest), used by the bookmark engine to
    /// disambiguate same-name Scopes. Kept public so existing tests can exercise it directly.
    /// </summary>
    public class LightScopeParser
    {
        private static readonly Regex TypeRegex = new Regex(
            @"^\s*(namespace|class|struct|interface|enum)\s+(?<name>\w+)",
            RegexOptions.Compiled);

        private static readonly Regex FuncRegex = new Regex(
            @"^\s*(?<return>[\w\*\&\<\>:]+)\s+(?<name>\w+)\s*\(.*\)\s*(const)?\s*(?=\{|\s*$|:)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> ControlKeywords = new HashSet<string>
        {
            "if", "for", "while", "switch", "catch", "sizeof", "typeof"
        };

        private string[] _lines;

        public ScopeNode Parse(string[] lines)
        {
            _lines = lines;

            if (lines == null || lines.Length == 0)
            {
                return new ScopeNode("Root", "Root", 0) { EndLine = 0 };
            }

            ScopeNode root = new ScopeNode
            {
                Name = "Root",
                Type = "Root",
                StartLine = 0,
                IndentLevel = -1
            };

            ScopeNode activeNode = root;
            PendingAnchor pending = null;
            int internalBraceCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int currentIndent = GetIndentLevel(line);

                while (activeNode != root
                       && currentIndent < activeNode.IndentLevel
                       && !IsEmptyOrComment(line))
                {
                    activeNode.EndLine = i - 1;
                    activeNode = activeNode.Parent;
                    if (activeNode.Type == "Function")
                        internalBraceCount = 0;
                }

                if (activeNode.Type != "Function" || internalBraceCount == 0)
                {
                    var anchor = DetectAnchor(line, i, currentIndent);
                    if (anchor != null)
                    {
                        pending = anchor;
                        continue;
                    }
                }

                if (line.Contains("{"))
                {
                    if (pending != null)
                    {
                        var newNode = new ScopeNode(pending.Name, pending.Type, pending.StartLine, pending.IndentLevel);
                        newNode.Parent = activeNode;
                        activeNode.Children.Add(newNode);
                        activeNode = newNode;
                        pending = null;
                    }
                    else if (activeNode.Type == "Function")
                    {
                        internalBraceCount++;
                    }
                    continue;
                }

                if (line.Contains("}"))
                {
                    if (activeNode.Type == "Function" && internalBraceCount > 0)
                    {
                        internalBraceCount--;
                    }
                    else if (activeNode != root)
                    {
                        activeNode.EndLine = i;
                        activeNode = activeNode.Parent;
                        internalBraceCount = 0;
                    }
                    continue;
                }
            }

            while (activeNode != null)
            {
                activeNode.EndLine = lines.Length - 1;
                activeNode = activeNode.Parent;
            }

            TileGaps(root, lines.Length - 1);
            ComputeRanks(root);

            return root;
        }

        private void TileGaps(ScopeNode node, int maxLine)
        {
            foreach (var child in node.Children)
                TileGaps(child, maxLine);

            if (node.Children.Count == 0)
            {
                if (node.Type == "Root" && node.StartLine < node.EndLine)
                    node.Children.Add(CreateFiller(node.StartLine, node.EndLine, "Global_Header"));
                return;
            }

            var sorted = node.Children.OrderBy(c => c.StartLine).ToList();
            var tiled = new List<ScopeNode>();

            if (sorted[0].StartLine > node.StartLine)
            {
                tiled.Add(CreateFiller(
                    node.StartLine,
                    sorted[0].StartLine - 1,
                    node.Type == "Root" ? "Global_Header" : "Code_Gap"));
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                tiled.Add(sorted[i]);

                if (i + 1 < sorted.Count)
                {
                    int expectedStart = sorted[i].EndLine + 1;
                    int nextStart = sorted[i + 1].StartLine;
                    if (nextStart > expectedStart)
                    {
                        tiled.Add(CreateFiller(expectedStart, nextStart - 1, "Code_Gap"));
                    }
                }
            }

            if (sorted[sorted.Count - 1].EndLine < node.EndLine)
            {
                tiled.Add(CreateFiller(
                    sorted[sorted.Count - 1].EndLine + 1,
                    node.EndLine,
                    node.Type == "Root" ? "Global_Footer" : "Code_Gap"));
            }

            node.Children.Clear();
            foreach (var c in tiled)
            {
                c.Parent = node;
                node.Children.Add(c);
            }
        }

        private ScopeNode CreateFiller(int start, int end, string fillerType)
        {
            var contentType = AnalyzeFillerContent(start, end);
            return new ScopeNode
            {
                Name = $"{fillerType}_{contentType}",
                Type = "Filler",
                StartLine = start,
                EndLine = end,
                IndentLevel = -1
            };
        }

        private string AnalyzeFillerContent(int start, int end)
        {
            int commentCount = 0, includeCount = 0, preprocessorCount = 0, variableCount = 0, codeCount = 0;

            for (int i = start; i <= end && i < (_lines?.Length ?? 0); i++)
            {
                var line = _lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#include"))
                    includeCount++;
                else if (trimmed.StartsWith("#"))
                    preprocessorCount++;
                else if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    commentCount++;
                else if (trimmed.Contains(";"))
                    variableCount++;
                else
                    codeCount++;
            }

            int max = Math.Max(Math.Max(commentCount, includeCount), Math.Max(Math.Max(preprocessorCount, variableCount), codeCount));
            if (codeCount == max) return "Code";
            if (includeCount == max) return "Includes";
            if (variableCount == max) return "Variables";
            if (preprocessorCount == max) return "Preprocessor";
            if (commentCount == max) return "Comments";
            return "Code";
        }

        private PendingAnchor DetectAnchor(string line, int lineIdx, int indent)
        {
            var typeMatch = TypeRegex.Match(line);
            if (typeMatch.Success)
            {
                string typeStr = typeMatch.Groups[1].Value;
                string nameStr = typeMatch.Groups["name"].Value;
                return new PendingAnchor
                {
                    Name = $"{typeStr} {nameStr}",
                    Type = char.ToUpper(typeStr[0]) + typeStr.Substring(1),
                    StartLine = lineIdx,
                    IndentLevel = indent
                };
            }

            var funcMatch = FuncRegex.Match(line);
            if (funcMatch.Success)
            {
                string funcName = funcMatch.Groups["name"].Value;
                if (!ControlKeywords.Contains(funcName))
                {
                    return new PendingAnchor
                    {
                        Name = $"function {funcName}()",
                        Type = "Function",
                        StartLine = lineIdx,
                        IndentLevel = indent
                    };
                }
            }

            return null;
        }

        private int GetIndentLevel(string line)
        {
            int indent = 0;
            foreach (char c in line)
            {
                if (c == ' ') indent++;
                else if (c == '\t') indent += 4;
                else break;
            }
            return indent;
        }

        private bool IsEmptyOrComment(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;
            var trimmed = line.Trim();
            return trimmed.StartsWith("//")
                || trimmed.StartsWith("/*")
                || trimmed.StartsWith("*");
        }

        private void ComputeRanks(ScopeNode root)
        {
            var allNodes = new List<ScopeNode>();
            CollectAllNodes(root, allNodes);

            var groups = allNodes.GroupBy(n => n.Name);
            foreach (var group in groups)
            {
                var nodes = group.ToList();
                if (nodes.Count == 1)
                {
                    nodes[0].Rank = 0f;
                    continue;
                }

                int minLineCount = nodes.Min(n => n.EndLine - n.StartLine + 1);
                var adjustedCounts = nodes.Select(n => (float)(n.EndLine - n.StartLine + 1 - minLineCount)).ToList();
                float maxAdjusted = adjustedCounts.Max();
                if (maxAdjusted > 0)
                {
                    for (int i = 0; i < nodes.Count; i++)
                        nodes[i].Rank = adjustedCounts[i] / maxAdjusted;
                }
                else
                {
                    foreach (var n in nodes)
                        n.Rank = 0f;
                }
            }
        }

        private void CollectAllNodes(ScopeNode node, List<ScopeNode> result)
        {
            result.Add(node);
            foreach (var child in node.Children)
                CollectAllNodes(child, result);
        }

        public void PrintTree(ScopeNode node, int depth = 0)
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}{node}");
            foreach (var child in node.Children)
                PrintTree(child, depth + 1);
        }

        private class PendingAnchor
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int StartLine { get; set; }
            public int IndentLevel { get; set; }
        }
    }

    /// <summary>
    /// A node in the scope tree produced by <see cref="LightScopeParser"/>. Represents a
    /// namespace/class/struct/interface/enum/function, a Filler gap, or the synthetic Root.
    /// </summary>
    public class ScopeNode
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int IndentLevel { get; set; }
        public List<ScopeNode> Children { get; set; } = new List<ScopeNode>();
        public ScopeNode Parent { get; set; }
        public float Rank { get; set; }

        public ScopeNode() { }

        public ScopeNode(string name, string type, int startLine, int indentLevel = 0)
        {
            Name = name;
            Type = type;
            StartLine = startLine;
            IndentLevel = indentLevel;
        }

        public override string ToString()
        {
            return $"[{Type}] {Name} : [{StartLine}, {EndLine}]";
        }
    }
}
