using System.IO;

namespace JokerDBDTracker.Services
{
    public static class AppStoragePaths
    {
        public const string CurrentFolderName = "JokerDBDTracker";
        public const string LegacyFolderName = "StreamTrackerWpf";

        public static string GetCurrentAppDataDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, CurrentFolderName);
        }

        public static string GetLegacyAppDataDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, LegacyFolderName);
        }
    }
}
