namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        // Stream catalog endpoints.
        private const string StreamsUrl = "https://www.youtube.com/@JokerDBD/streams";

        // Achievement IDs.
        private const string AchievementCursed15 = "cursed_15_effects_full_stream";

        // Profile progression limits.
        private const int MaxRecentStreamsInProfile = 5;
        private const int MaxLevel = 100;
        private const int MaxPrestige = 100;

        // XP rewards and multipliers.
        private const int XpFirstWatch = 900;
        private const int XpAchievement = 3500;
        private const double WatchSessionXpMultiplier = 1.30;
        private const double QuestRewardXpMultiplier = 1.40;

        // Update source repository.
        private const string GitHubRepoOwner = "dragloner";
        private const string GitHubRepoName = "JokerDBDTracker";
    }
}
