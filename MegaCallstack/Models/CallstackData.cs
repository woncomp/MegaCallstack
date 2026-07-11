using System;
using System.Collections.Generic;

namespace MegaCallstack.Models
{
    public class CallstackData
    {
        public int LeafHashCode { get; set; }
        public List<CallstackFrame> Frames { get; set; } = new List<CallstackFrame>();
        public DateTime CapturedTime { get; set; }

        public CallstackData()
        {
        }

        public CallstackData(List<CallstackFrame> frames)
        {
            Frames = frames;
            CapturedTime = DateTime.Now;
            if (frames.Count > 0)
            {
                LeafHashCode = frames[frames.Count - 1].HashCode;
            }
        }
    }
}
