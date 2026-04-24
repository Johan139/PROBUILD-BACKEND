using System;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

namespace BuildigBackend.Models
{
    public class WebsiteJobTrackerModel
    {
        public Guid Id { get; set; }
        public string IpAddress { get; set; }
        public int JobCount { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }
}

