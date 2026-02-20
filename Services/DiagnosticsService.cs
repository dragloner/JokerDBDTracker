using System.IO;
using System.Text;

namespace JokerDBDTracker.Services
{
    public static class DiagnosticsService
    {
        private static readonly object Sync = new();

        public static string GetLogDirectory()
        {
            return Path.Combine(AppStoragePaths.GetCurrentLocalAppDataDirectory(), "Logs");
        }

        public static string GetLogFilePath()
        {
            return Path.Combine(GetLogDirectory(), "app.log");
        }

        public static void LogInfo(string source, string message)
        {
            WriteLine(source, message);
        }

        public static void LogException(string source, Exception exception)
        {
            var details = exception.ToString();
            WriteLine(source, details);
        }

        private static void WriteLine(string source, string message)
        {
            try
            {
                var directory = GetLogDirectory();
                Directory.CreateDirectory(directory);
                var path = GetLogFilePath();
                var line =
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] [{source}] {message}{Environment.NewLine}";

                lock (Sync)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
