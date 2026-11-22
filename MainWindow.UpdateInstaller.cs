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
            scriptContent = scriptContent.Replace("$cleanupAfterInstall = @()",
                "$cleanupAfterInstall = @('" + extractRoot.Replace("'", "''") + "', '" + zipPath.Replace("'", "''") + "', '" + scriptPath.Replace("'", "''") + "')");
            File.WriteAllText(scriptPath, scriptContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

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
    try {{ Add-Content -LiteralPath $logFile -Value ('[' + (Get-Date -Format o) + '] ' + $message) }} catch {{ }}
}}
$canRestart = $true
$resultPath = Join-Path $target 'update_result.txt'
$result = 'failed'
$cleanupAfterInstall = @()
if ($cleanupAfterInstall.Count -eq 3) {{
    $backup = Join-Path ([IO.Path]::GetDirectoryName($cleanupAfterInstall[0])) ([IO.Path]::GetFileName($backup))
}}
$manifest = Join-Path $target '.joker-installed-files.json'
function Get-OwnedFiles {{
    $files = @{{}}
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {{
        $relative = $file.FullName.Substring($source.TrimEnd('\').Length + 1)
        if ($relative -match '(^|\\)(Logs|YouTube_Profile|WebView_Profile)(\\|$)' -or
            $file.Name -in @('update_apply.log', 'update_result.txt', '.joker-installed-files.json')) {{ continue }}
        $files[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }}
    return $files
}}
function Remove-ObsoleteFiles($incoming) {{
    if (!(Test-Path -LiteralPath $manifest -PathType Leaf)) {{ return }}
    $previous = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    foreach ($property in $previous.PSObject.Properties) {{
        if ($incoming.ContainsKey($property.Name)) {{ continue }}
        $root = [IO.Path]::GetFullPath($target).TrimEnd('\') + '\'
        $old = [IO.Path]::GetFullPath((Join-Path $target $property.Name))
        if (!$old.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {{ continue }}
        $item = Get-Item -LiteralPath $old -ErrorAction SilentlyContinue
        if (!$item -or $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {{ continue }}
        $directory = $item.Directory
        while ($directory -and $directory.FullName.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and
            -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {{ $directory = $directory.Parent }}
        if (!$directory -or $directory.FullName.TrimEnd('\') -ne $root.TrimEnd('\')) {{ continue }}
        if ((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -eq $property.Value) {{
            Remove-Item -LiteralPath $old -Force
        }}
    }}
}}
function Copy-Tree([string]$from, [string]$to) {{
    # Windows PowerShell's native argument quoting treats a trailing backslash
    # before a closing quote as an escaped quote. A final path component avoids
    # that ambiguity, including when copying to a drive root.
    $fromArgument = Join-Path $from '.'
    $toArgument = Join-Path $to '.'
    & robocopy $fromArgument $toArgument /E /IS /IT /XJ /R:3 /W:1 /NFL /NDL /NJH /NJS /NP /XD Logs YouTube_Profile WebView_Profile /XF update_apply.log update_result.txt | Out-Null
    if ($LASTEXITCODE -ge 8) {{ throw ('File copy failed: ' + $LASTEXITCODE) }}
}}
try {{
    $parent = Get-Process -Id {parentProcessId} -ErrorAction SilentlyContinue
    if ($parent) {{ $parent.WaitForExit(90000) | Out-Null }}
    if (Get-Process -Id {parentProcessId} -ErrorAction SilentlyContinue) {{ $canRestart = $false; throw 'Application is still running. Update aborted.' }}
    if (!(Test-Path -LiteralPath $source -PathType Container) -or !(Test-Path -LiteralPath $target -PathType Container)) {{ throw 'Update directories are missing.' }}
    Copy-Tree $target $backup
    try {{
        $canRestart = $false
        Copy-Tree $source $target
        $incoming = Get-OwnedFiles
        Remove-ObsoleteFiles $incoming
        ConvertTo-Json -InputObject $incoming -Compress | Set-Content -LiteralPath $manifest -Encoding UTF8
        $canRestart = $true
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
        $canRestart = $true
        $result = 'restored'
        throw
    }}
    $result = 'installed'
    Write-Log 'Update completed successfully.'
}} catch {{
    try {{ Write-Log $_.Exception.Message }} catch {{ }}
}} finally {{
    try {{ Set-Content -LiteralPath $resultPath -Value $result -Encoding Ascii }} catch {{ }}
    $exe = Join-Path $target $exeName
    if ($canRestart -and (Test-Path -LiteralPath $exe)) {{
        Start-Process -FilePath $exe -WorkingDirectory $target
        if ($result -eq 'installed' -and $cleanupAfterInstall.Count -eq 3) {{
            $updates = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($cleanupAfterInstall[2]))
            foreach ($path in $cleanupAfterInstall) {{
                $full = [IO.Path]::GetFullPath($path)
                if ([IO.Path]::GetDirectoryName($full) -ne $updates) {{ continue }}
                $entry = Get-Item -LiteralPath $full -ErrorAction SilentlyContinue
                if (!$entry -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {{ continue }}
                if ($entry.PSIsContainer -and $entry.Name -match '^update_extract_[a-f0-9]{{32}}$' -and -not $backup.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase)) {{
                    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
                }} elseif (!$entry.PSIsContainer) {{
                    Remove-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
                }}
            }}
        }}
    }}
}}";
        }
    }
}
