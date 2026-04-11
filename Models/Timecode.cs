namespace JokerDBDTracker.Models
{
    public class Timecode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string VideoId { get; set; } = string.Empty;
        public string VideoTitle { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public int Seconds { get; set; }
        public string Label { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

        // Runtime-only flag — not persisted to JSON.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsCurrentVideo { get; set; }

        public string TimeFormatted => TimeSpan.FromSeconds(Seconds).ToString(@"hh\:mm\:ss");

        public string SavedAtText
        {
            get
            {
                var local = SavedAtUtc.ToLocalTime();
                var daysAgo = (DateTime.Now.Date - local.Date).Days;
                if (daysAgo <= 0) return $"сегодня {local:HH:mm}";
                if (daysAgo == 1) return $"вчера {local:HH:mm}";
                return $"{local:yyyy-MM-dd HH:mm}";
            }
        }
    }
}
