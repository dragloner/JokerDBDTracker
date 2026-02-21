using System.IO;
using System.Text;

namespace JokerDBDTracker.Services
{
    public static class DiagnosticsService
    {
        private static readonly object Sync = new();
        private static volatile bool _enabled = true;
        private static string? _resolvedLogDirectory;

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static bool IsEnabled()
        {
            return _enabled;
        }

        public static string GetLogDirectory()
        {
            lock (Sync)
            {
                _resolvedLogDirectory ??= ResolveLogDirectory();
                return _resolvedLogDirectory;
            }
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
            if (!_enabled)
            {
                return;
            }

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
                try
                {
                    // If install directory is not writable (e.g. Program Files), switch once to fallback.
                    lock (Sync)
                    {
                        _resolvedLogDirectory = GetFallbackLogDirectory();
                    }

                    var fallbackDirectory = GetLogDirectory();
                    Directory.CreateDirectory(fallbackDirectory);
                    var fallbackPath = Path.Combine(fallbackDirectory, "app.log");
                    var fallbackLine =
                        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] [{source}] {message}{Environment.NewLine}";
                    File.AppendAllText(fallbackPath, fallbackLine, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never crash the app.
                }
            }
        }

        private static string ResolveLogDirectory()
        {
            var programLogs = Path.Combine(AppStoragePaths.GetProgramDirectory(), "Logs");
            if (CanWriteToDirectory(programLogs))
            {
                return programLogs;
            }

            return GetFallbackLogDirectory();
        }

        private static string GetFallbackLogDirectory()
        {
            return Path.Combine(AppStoragePaths.GetCurrentLocalAppDataDirectory(), "Logs");
        }

        private static bool CanWriteToDirectory(string directoryPath)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                var probePath = Path.Combine(directoryPath, $".probe_{Environment.ProcessId}_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, string.Empty, Encoding.UTF8);
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
