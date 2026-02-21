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

        public static string GetCurrentLocalAppDataDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, CurrentFolderName);
        }

        public static string GetProgramDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                return baseDirectory;
            }

            return Directory.GetCurrentDirectory();
        }

        public static string GetWebViewProfileDirectory()
        {
            return Path.Combine(GetCurrentLocalAppDataDirectory(), "YouTube_Profile");
        }
    }
}
