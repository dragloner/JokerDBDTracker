namespace JokerDBDTracker.Models
{
    public class YouTubeVideo
    {
        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PublishedAtText { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public DateTime? LastViewedAtUtc { get; set; }
        public int LastPlaybackSeconds { get; set; }
        public int OriginalOrder { get; set; }
        public bool IsFavorite { get; set; }

        public string EmbedUrl => $"https://www.youtube-nocookie.com/embed/{VideoId}";
        public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
        public string FavoriteGlyph => IsFavorite ? "★" : "☆";

        public string LastViewedText
        {
            get
            {
                if (!LastViewedAtUtc.HasValue)
                {
                    return "Последний просмотр: никогда";
                }

                var local = LastViewedAtUtc.Value.ToLocalTime();
                var daysAgo = (DateTime.Now.Date - local.Date).Days;
                var daysText = daysAgo <= 0 ? "сегодня" : $"{daysAgo} дн. назад";
                return $"Последний просмотр: {local:yyyy-MM-dd HH:mm} ({daysText})";
            }
        }

        public string ResumeText => LastPlaybackSeconds > 0
            ? $"Продолжить с: {TimeSpan.FromSeconds(LastPlaybackSeconds):hh\\:mm\\:ss}"
            : "Продолжить с: 00:00:00";
    }
}


