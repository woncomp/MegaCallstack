using System.Collections.Generic;

namespace MegaCallstack.Models
{
    public class SessionState
    {
        public Dictionary<int, string> NodeColors { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, bool> CollapsedNodes { get; set; } = new Dictionary<int, bool>();
        public Dictionary<int, bool> HiddenAncestorNodes { get; set; } = new Dictionary<int, bool>();
        public Dictionary<string, long> ResolvedFileWriteTimes { get; set; } = new Dictionary<string, long>();
    }
}
