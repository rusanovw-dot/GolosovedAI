using System.Collections.Generic;

namespace GolosovedAI.Services
{
    public class TranscriptionResult
    {
        public string Text { get; set; } = "";
        public List<Segment> Segments { get; set; } = new();

        public class Segment
        {
            public float Start { get; set; }
            public float End { get; set; }
            public string Text { get; set; } = "";
        }
    }
}