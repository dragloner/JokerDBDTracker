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

        public bool IsTwitchStream { get; set; }

        public string EmbedUrl => IsTwitchStream
            ? $"https://player.twitch.tv/?channel={VideoId.Replace("twitch:", "")}&parent=localhost&autoplay=true"
            : $"https://www.youtube-nocookie.com/embed/{VideoId}";
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

        // True when there is a meaningful resume position (> 1 min saved).
        public bool HasResumePosition => LastPlaybackSeconds > 60;
        public int TimecodeCount { get; set; }
        public bool HasTimecodes => TimecodeCount > 0;

        // Estimated visual width (0–162 px) for the thumbnail progress bar.
        // We use a logarithmic-ish scale anchored at 1 h ≈ half-width so short
        // clips still show visible progress while very long ones don't saturate.
        public double ResumeBarWidth
        {
            get
            {
                if (LastPlaybackSeconds <= 0) return 0;
                const double refSeconds = 7200.0; // ~2 h → saturates at 162 px
                var ratio = Math.Min(1.0, LastPlaybackSeconds / refSeconds);
                return Math.Round(ratio * 162, 1);
            }
        }

        // Same but for the 80 px thumbnail in the Continue Watching card.
        public double ResumeBarWidthSmall
        {
            get
            {
                if (LastPlaybackSeconds <= 0) return 0;
                const double refSeconds = 7200.0;
                var ratio = Math.Min(1.0, LastPlaybackSeconds / refSeconds);
                return Math.Round(ratio * 80, 1);
            }
        }
    }
}


