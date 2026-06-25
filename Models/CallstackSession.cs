using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MegaCallstack.Models
{
    public class CallstackSession
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedTime { get; set; }
        public string FolderName { get; set; }

        [JsonIgnore]
        public List<CallstackData> Callstacks { get; set; } = new List<CallstackData>();

        [JsonIgnore]
        public Dictionary<int, string> NodeColors { get; set; } = new Dictionary<int, string>();

        [JsonIgnore]
        public Dictionary<int, bool> CollapsedNodes { get; set; } = new Dictionary<int, bool>();

        [JsonIgnore]
        public Dictionary<int, bool> HiddenAncestorNodes { get; set; } = new Dictionary<int, bool>();

        [JsonIgnore]
        public bool IsLoaded { get; set; }

        public CallstackSession()
        {
            Id = Guid.NewGuid().ToString();
            CreatedTime = DateTime.Now;
        }

        public CallstackSession(string name) : this()
        {
            Name = name;
        }
    }
}
