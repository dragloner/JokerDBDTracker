using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void StartPortableSelfUpdate(string zipPath)
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
            ExtractZipSafely(zipPath, extractRoot);

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
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                {
                    continue;
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
$parentPid = {parentProcessId}
$source = '{Esc(sourceDirectory)}'
$target = '{Esc(targetDirectory)}'
$exeName = '{Esc(executableName)}'
$logFile = Join-Path $target 'update_apply.log'

function Write-Log([string]$message) {{
    try {{
        Add-Content -Path $logFile -Value ('[' + (Get-Date -Format o) + '] ' + $message)
    }} catch {{
    }}
}}

Write-Log 'Updater script started.'

for ($i = 0; $i -lt 360; $i++) {{
    try {{
        Get-Process -Id $parentPid -ErrorAction Stop | Out-Null
        Start-Sleep -Milliseconds 250
    }} catch {{
        break
    }}
}}

if (!(Test-Path $source) -or !(Test-Path $target)) {{
    Write-Log 'Source or target path is missing.'
    if (Test-Path (Join-Path $target $exeName)) {{
        Start-Process -FilePath (Join-Path $target $exeName)
    }}
    exit 1
}}

$updatedExe = Join-Path $target $exeName
$copyOk = $false

for ($attempt = 1; $attempt -le 8; $attempt++) {{
    Start-Sleep -Milliseconds 120
    $robocopyResult = & robocopy $source $target /E /R:4 /W:1 /NFL /NDL /NJH /NJS /NP
    $exitCode = $LASTEXITCODE
    if ($exitCode -lt 8) {{
        $copyOk = $true
        break
    }}

    Start-Sleep -Milliseconds 700
}}

if (-not $copyOk) {{
    Write-Log 'Copy failed after retries. Starting existing executable.'
    if (Test-Path $updatedExe) {{
        Start-Process -FilePath $updatedExe
    }}
    exit 2
}}

if (Test-Path $updatedExe) {{
    Start-Sleep -Milliseconds 300
    Write-Log 'Copy succeeded. Starting updated executable.'
    Start-Process -FilePath $updatedExe
}}

Write-Log 'Updater script finished.'";
        }
    }
}
