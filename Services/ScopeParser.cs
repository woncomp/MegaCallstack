using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MegaCallstack.Services
{
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
        {
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
}
