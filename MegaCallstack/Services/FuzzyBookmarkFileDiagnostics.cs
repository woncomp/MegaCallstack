using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MegaCallstack.Services
{
    public sealed class FuzzyBookmarkFileDiagnostics : IFuzzyBookmarkDiagnostics
    {
        private readonly string _outputDirectory;
        private readonly object _lock = new object();
        private readonly Dictionary<string, OperationState> _operations = new Dictionary<string, OperationState>();

        public FuzzyBookmarkFileDiagnostics(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            _outputDirectory = outputDirectory;
            if (!Directory.Exists(_outputDirectory))
                Directory.CreateDirectory(_outputDirectory);
        }

        public string BeginOperation(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            string baseId = BuildOperationId(filePath);
            lock (_lock)
            {
                int counter = 1;
                string operationId = baseId;
                while (_operations.ContainsKey(operationId) || OperationFilesExist(operationId))
                {
                    operationId = $"{baseId}-{counter}";
                    counter++;
                }
                _operations[operationId] = new OperationState { FilePath = filePath };
                return operationId;
            }
        }

        public void OnScopeParsed(string operationId, ScopeNode root, IReadOnlyList<string> lines)
        {
            if (string.IsNullOrWhiteSpace(operationId) || root == null) return;
            var serializable = SerializableScopeNode.FromScopeNode(root);
            var wrapper = new ScopeParserOutput
            {
                OperationId = operationId,
                LineCount = lines?.Count ?? 0,
                Root = serializable
            };
            string filePath = GetScopeParserFilePath(operationId);
            WriteFile(filePath, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
        }

        public void OnBookmarkCreated(string operationId, int originalLine, FuzzyBookmark bookmark, ScopeNodeSummary deepestScope)
        {
            if (string.IsNullOrWhiteSpace(operationId)) return;
            var details = new CreateBookmarkDetails
            {
                OriginalLine = originalLine,
                Bookmark = bookmark,
                DeepestScope = deepestScope
            };
            AppendToResolveLog(operationId, FuzzyBookmarkDiagnosticsFormatter.FormatBookmarkCreated(details));
        }

        public void OnBookmarkResolved(string operationId, FuzzyBookmark bookmark, ResolveDecisionDetails details)
        {
            if (string.IsNullOrWhiteSpace(operationId)) return;
            var input = new ResolveBookmarkDetails
            {
                OriginalLine = details?.Result?.Line ?? 0,
                Bookmark = bookmark,
                Seed = details?.Seed ?? 0.0,
                ScopeMatch = details?.ScopeMatch
            };
            AppendToResolveLog(operationId, FuzzyBookmarkDiagnosticsFormatter.FormatBookmarkResolved(input, details));
        }

        public void CompleteOperation(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId)) return;
            lock (_lock)
            {
                _operations.Remove(operationId);
            }
        }

        public string GetScopeParserFilePath(string operationId)
        {
            return Path.Combine(_outputDirectory, $"{Sanitize(operationId)}-scope-parser.json");
        }

        public string GetBookmarkResolveFilePath(string operationId)
        {
            return Path.Combine(_outputDirectory, $"{Sanitize(operationId)}-bookmark-resolve.txt");
        }

        private void AppendToResolveLog(string operationId, string text)
        {
            string filePath = GetBookmarkResolveFilePath(operationId);
            lock (_lock)
            {
                File.AppendAllText(filePath, text);
            }
        }

        private void WriteFile(string filePath, string content)
        {
            lock (_lock)
            {
                File.WriteAllText(filePath, content);
            }
        }

        private bool OperationFilesExist(string operationId)
        {
            return File.Exists(GetScopeParserFilePath(operationId))
                || File.Exists(GetBookmarkResolveFilePath(operationId));
        }

        private static string BuildOperationId(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath) ?? "unknown";
            string timestamp = DateTime.Now.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return $"{timestamp}-{fileName}";
        }

        private static string Sanitize(string operationId)
        {
            if (string.IsNullOrEmpty(operationId)) return operationId;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(operationId.Length);
            foreach (char c in operationId)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private sealed class OperationState
        {
            public string FilePath { get; set; }
        }

        private sealed class ScopeParserOutput
        {
            public string OperationId { get; set; }
            public int LineCount { get; set; }
            public SerializableScopeNode Root { get; set; }
        }
    }
}
