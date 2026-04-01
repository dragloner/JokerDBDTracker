using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace JokerDBDTracker.Services
{
    public enum LogLevel
    {
        Info,
        Error,
    }

    public record LogEntry(DateTime Timestamp, LogLevel Level, string Source, string Message);

    public static class DiagnosticsService
    {
        private const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB
        private const int RingBufferCapacity = 200;

        private static readonly object Sync = new();
        private static volatile bool _enabled = true;
        private static string? _resolvedLogDirectory;
        private static readonly ConcurrentQueue<LogEntry> _ringBuffer = new();

        /// <summary>Fired on the calling thread whenever a new entry is added to the ring buffer.</summary>
        public static event Action<LogEntry>? EntryLogged;

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
            WriteEntry(LogLevel.Info, source, message);
        }

        public static void LogException(string source, Exception exception)
        {
            var details = SanitizeLogMessage(exception.ToString());
            WriteEntry(LogLevel.Error, source, details);
        }

        /// <summary>Returns a snapshot of recent log entries (up to <see cref="RingBufferCapacity"/>).</summary>
        public static LogEntry[] GetRecentEntries()
        {
            return _ringBuffer.ToArray();
        }

        /// <summary>Clears the in-memory ring buffer.</summary>
        public static void ClearInMemoryLog()
        {
            while (_ringBuffer.TryDequeue(out _)) { }
        }

        private static void WriteEntry(LogLevel level, string source, string message)
        {
            var timestamp = DateTime.UtcNow;
            var entry = new LogEntry(timestamp, level, source, message);

            // Update ring buffer (cap at RingBufferCapacity)
            _ringBuffer.Enqueue(entry);
            while (_ringBuffer.Count > RingBufferCapacity)
            {
                _ringBuffer.TryDequeue(out _);
            }

            // Notify subscribers (log viewer UI)
            try { EntryLogged?.Invoke(entry); } catch { /* never crash */ }

            if (!_enabled)
            {
                return;
            }

            var levelTag = level == LogLevel.Error ? "ERROR" : "INFO ";
            var line = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}Z] [{levelTag}] [{source}] {message}{Environment.NewLine}";

            try
            {
                var directory = GetLogDirectory();
                Directory.CreateDirectory(directory);
                var path = GetLogFilePath();

                lock (Sync)
                {
                    RotateLogIfNeeded(path);
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                try
                {
                    lock (Sync)
                    {
                        _resolvedLogDirectory = GetFallbackLogDirectory();
                    }

                    var fallbackDirectory = GetLogDirectory();
                    Directory.CreateDirectory(fallbackDirectory);
                    var fallbackPath = Path.Combine(fallbackDirectory, "app.log");
                    File.AppendAllText(fallbackPath, line, Encoding.UTF8);
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

        /// <summary>
        /// Redacts URLs and user-profile paths from log messages to prevent data leakage.
        /// </summary>
        private static string SanitizeLogMessage(string message)
        {
            message = Regex.Replace(message, @"(https?://[^/\s]+)/[^\s""'>\]]+", "$1/***");
            message = Regex.Replace(message, @"C:\\Users\\[^\\]+", @"C:\Users\***", RegexOptions.IgnoreCase);
            return message;
        }

        private static void RotateLogIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                {
                    return;
                }

                var info = new FileInfo(logPath);
                if (info.Length < MaxLogFileSizeBytes)
                {
                    return;
                }

                var oldPath = logPath + ".old";
                File.Copy(logPath, oldPath, overwrite: true);
                File.WriteAllText(logPath, string.Empty, Encoding.UTF8);
            }
            catch
            {
                // Log rotation should never crash the app.
            }
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
