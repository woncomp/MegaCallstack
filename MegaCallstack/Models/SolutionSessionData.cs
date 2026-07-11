using System.Collections.Generic;

namespace MegaCallstack.Models
{
    public class SolutionSessionData
    {
        public string ActiveSessionId { get; set; }
        public List<CallstackSession> Sessions { get; set; } = new List<CallstackSession>();
    }
}
