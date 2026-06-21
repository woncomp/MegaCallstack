using Newtonsoft.Json;

namespace MegaCallstack.Models
{
    public class CallstackFrame
    {
        public string FunctionName { get; set; }
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public int HashCode { get; set; }
        public string Language { get; set; }
        public string Module { get; set; }
        public string LineContent { get; set; }

        public CallstackFrame()
        {
        }

        public CallstackFrame(string functionName, string fileName, int lineNumber)
        {
            FunctionName = functionName;
            FileName = fileName;
            LineNumber = lineNumber;
        }

        public CallstackFrame(string functionName, string fileName, int lineNumber, string language, string module)
            : this(functionName, fileName, lineNumber)
        {
            Language = language;
            Module = module;
        }

        public static int ComputeFNV1aHash(int parentHash, string functionName)
        {
            unchecked
            {
                int hash = parentHash;
                if (!string.IsNullOrEmpty(functionName))
                {
                    for (int i = 0; i < functionName.Length; i++)
                    {
                        hash ^= functionName[i];
                        hash *= 16777619;
                    }
                }
                return hash;
            }
        }

        public static int ComputeHashForPath(string[] functionNames)
        {
            int hash = 0;
            foreach (var name in functionNames)
            {
                hash = ComputeFNV1aHash(hash, name);
            }
            return hash;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(FileName))
                return FunctionName;
            return $"{FunctionName} - {FileName}:{LineNumber}";
        }

        public string BuildTooltipText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Function: {FunctionName}");
            if (!string.IsNullOrEmpty(FileName))
            {
                sb.AppendLine($"File: {FileName}");
                sb.AppendLine($"Line: {LineNumber}");
            }
            if (!string.IsNullOrEmpty(LineContent))
            {
                sb.AppendLine($"Source: {LineContent}");
            }
            if (!string.IsNullOrEmpty(Language))
            {
                sb.AppendLine($"Language: {Language}");
            }
            if (!string.IsNullOrEmpty(Module))
            {
                sb.AppendLine($"Module: {Module}");
            }
            sb.Append($"Hash: {HashCode}");
            return sb.ToString();
        }
    }
}
