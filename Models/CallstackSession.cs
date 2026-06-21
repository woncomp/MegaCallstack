using System;
using System.Collections.Generic;

namespace MegaCallstack.Models
{
    public class CallstackSession
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<CallstackData> Callstacks { get; set; } = new List<CallstackData>();
        public Dictionary<int, string> NodeColors { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, bool> NodeExpansionStates { get; set; } = new Dictionary<int, bool>();

        public CallstackSession()
        {
            Id = Guid.NewGuid().ToString();
        }

        public CallstackSession(string name) : this()
        {
            Name = name;
        }
    }
}
