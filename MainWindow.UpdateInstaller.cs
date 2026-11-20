using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task StartPortableSelfUpdateAsync(string zipPath)
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                throw new InvalidOperationException(T(
                    "Не удалось определить путь к текущему исполняемому файлу.",
                    "Unable to resolve current executable path."));
            }

            var installDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                throw new InvalidOperationException(T(
                    "Не удалось определить папку установки приложения.",
                    "Unable to resolve application install directory."));
            }

            var updatesFolder = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
            var extractRoot = Path.Combine(updatesFolder, $"update_extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractRoot);
            await Task.Run(() => ExtractZipSafely(zipPath, extractRoot));

            var exeName = Path.GetFileName(currentExePath);
            var stagedRoot = ResolveStagedRoot(extractRoot, exeName);
            if (string.IsNullOrWhiteSpace(stagedRoot))
            {
                throw new InvalidOperationException(
                    T($"В архиве обновления не найден исполняемый файл {exeName}.", $"Executable {exeName} was not found in update archive."));
            }

            var scriptPath = Path.Combine(updatesFolder, $"apply_update_{Guid.NewGuid():N}.ps1");
            var scriptContent = BuildApplyUpdateScript(
                parentProcessId: Environment.ProcessId,
                sourceDirectory: stagedRoot,
                targetDirectory: installDirectory,
                executableName: exeName);
            File.WriteAllText(scriptPath, scriptContent);

            var powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (!File.Exists(powerShellPath))
            {
                powerShellPath = "powershell";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static string ResolveStagedRoot(string extractedDirectory, string executableName)
        {
            if (File.Exists(Path.Combine(extractedDirectory, executableName)))
            {
                return extractedDirectory;
            }

            var directChildMatch = Directory.EnumerateDirectories(extractedDirectory)
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, executableName)));
            if (!string.IsNullOrWhiteSpace(directChildMatch))
            {
                return directChildMatch;
            }

            var deepMatch = Directory.EnumerateFiles(extractedDirectory, executableName, SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(dir => !string.IsNullOrWhiteSpace(dir));
            return deepMatch ?? string.Empty;
        }

        private static void ExtractZipSafely(string zipPath, string destinationDirectory)
        {
            var fullDestination = Path.GetFullPath(destinationDirectory);
            // Ensure the prefix ends with a separator so "C:\app" doesn't match "C:\app-evil".
            if (!fullDestination.EndsWith(Path.DirectorySeparatorChar))
            {
                fullDestination += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            const long maximumExpandedBytes = 2L * 1024 * 1024 * 1024;
            const int maximumEntries = 20000;
            if (archive.Entries.Count > maximumEntries || archive.Entries.Sum(entry => entry.Length) > maximumExpandedBytes)
                throw new InvalidDataException("Update archive exceeds extraction limits.");
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                {
                    continue;
                }

                // Reject entries with path traversal sequences before normalization.
                if (entry.FullName.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unsafe path inside update archive: {entry.FullName}");
                }

                var targetPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
                if (!targetPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsafe path inside update archive: {entry.FullName}");
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        private static string BuildApplyUpdateScript(int parentProcessId, string sourceDirectory, string targetDirectory, string executableName)
        {
            static string Esc(string value) => value.Replace("'", "''");
            return $@"$ErrorActionPreference = 'Stop'
$source = '{Esc(sourceDirectory)}'
$target = '{Esc(targetDirectory)}'
$exeName = '{Esc(executableName)}'
$backup = Join-Path ([IO.Path]::GetDirectoryName($source.TrimEnd('\'))) ('rollback_' + [Guid]::NewGuid().ToString('N'))
$logFile = Join-Path $target 'update_apply.log'
function Write-Log([string]$message) {{
    Add-Content -LiteralPath $logFile -Value ('[' + (Get-Date -Format o) + '] ' + $message)
}}
function Copy-Tree([string]$from, [string]$to) {{
    # Windows PowerShell's native argument quoting treats a trailing backslash
    # before a closing quote as an escaped quote. A final path component avoids
    # that ambiguity, including when copying to a drive root.
    $fromArgument = Join-Path $from '.'
    $toArgument = Join-Path $to '.'
    & robocopy $fromArgument $toArgument /E /IS /IT /XJ /R:3 /W:1 /NFL /NDL /NJH /NJS /NP /XD Logs YouTube_Profile WebView_Profile /XF update_apply.log | Out-Null
    if ($LASTEXITCODE -ge 8) {{ throw ('File copy failed: ' + $LASTEXITCODE) }}
}}
try {{
    $parent = Get-Process -Id {parentProcessId} -ErrorAction SilentlyContinue
    if ($parent) {{ $parent.WaitForExit(90000) | Out-Null }}
    if (Get-Process -Id {parentProcessId} -ErrorAction SilentlyContinue) {{ throw 'Application is still running. Update aborted.' }}
    if (!(Test-Path -LiteralPath $source -PathType Container) -or !(Test-Path -LiteralPath $target -PathType Container)) {{ throw 'Update directories are missing.' }}
    Copy-Tree $target $backup
    try {{
        Copy-Tree $source $target
    }} catch {{
        Write-Log ('Update failed; restoring backup: ' + $_.Exception.Message)
        Copy-Tree $backup $target
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {{
            $relative = $file.FullName.Substring($source.TrimEnd('\').Length + 1)
            if (!(Test-Path -LiteralPath (Join-Path $backup $relative))) {{
                $introducedFile = [IO.Path]::GetFullPath((Join-Path $target $relative))
                if ($introducedFile.StartsWith([IO.Path]::GetFullPath($target).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {{
                    Remove-Item -LiteralPath $introducedFile -Force -ErrorAction SilentlyContinue
                }}
            }}
        }}
        throw
    }}
    Write-Log 'Update completed successfully.'
}} catch {{
    try {{ Write-Log $_.Exception.Message }} catch {{ }}
}} finally {{
    $exe = Join-Path $target $exeName
    if (Test-Path -LiteralPath $exe) {{ Start-Process -FilePath $exe -WorkingDirectory $target }}
}}";
        }
    }
}
