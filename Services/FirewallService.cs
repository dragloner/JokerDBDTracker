using System.Diagnostics;

namespace JokerDBDTracker.Services
{
    /// <summary>
    /// Manages Windows Firewall rules for Watch Together TCP port.
    /// Uses netsh.exe (available on all Windows versions) to check/create inbound rules.
    /// </summary>
    public static class FirewallService
    {
        private const string RuleName = "JokerDBDTracker Watch Together";

        /// <summary>
        /// Checks if a firewall rule for the given port already exists.
        /// </summary>
        public static bool IsRuleExists(int port)
        {
            try
            {
                var result = RunNetsh($"advfirewall firewall show rule name=\"{RuleName}\"");
                return result.ExitCode == 0 &&
                       result.Output.Contains(port.ToString(), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates an inbound TCP firewall rule for the given port.
        /// Returns true if the rule was created successfully.
        /// Requires admin privileges — will trigger UAC prompt.
        /// </summary>
        public static async Task<bool> EnsureRuleAsync(int port)
        {
            if (IsRuleExists(port))
            {
                return true;
            }

            try
            {
                // First, remove any existing rule with the same name but different port.
                var deleteScript =
                    $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1; " +
                    $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {deleteScript}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process is null)
                {
                    return false;
                }

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                // User declined UAC or other error.
                return false;
            }
        }

        private static (int ExitCode, string Output) RunNetsh(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, string.Empty);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return (process.ExitCode, output);
        }
    }
}
