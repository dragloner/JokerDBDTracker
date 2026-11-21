using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Input;
using JokerDBDTracker;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "regression-data", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("JOKERDBD_DATA_ROOT", root);
DiagnosticsService.SetEnabled(false);
var passed = 0;
var failed = 0;
async Task Test(string name, Func<Task> run)
{
    try { await run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> run) where T : Exception
{
    try { await run(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
object? Invoke(Type type, string method, params object?[] args) => type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
string Fixture(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
Task Validate(string path, long size, string sha) => (Task)Invoke(typeof(GitHubUpdateService), "EnsureDownloadedAssetValidAsync", path, size, sha, CancellationToken.None)!;

await Test("Start respects the filtered catalogue and resumes the most recent saved position", () =>
{
    var first = new YouTubeVideo { VideoId = "first" };
    var old = new YouTubeVideo { VideoId = "old", LastPlaybackSeconds = 120, LastViewedAtUtc = DateTime.UtcNow.AddDays(-2) };
    var recent = new YouTubeVideo { VideoId = "recent", LastPlaybackSeconds = 240, LastViewedAtUtc = DateTime.UtcNow };
    Check(ReferenceEquals(Invoke(typeof(MainWindow), "SelectStartVideo", new List<YouTubeVideo> { first, old, recent }), recent));
    Check(ReferenceEquals(Invoke(typeof(MainWindow), "SelectStartVideo", new List<YouTubeVideo> { first }), first));
    Check(Invoke(typeof(MainWindow), "SelectStartVideo", new List<YouTubeVideo>()) is null);
    return Task.CompletedTask;
});

await Test("Consent pages remain interactive and player resumes after dismissal", async () =>
{
    var fixture = Path.Combine(root, "web-scripts.json");
    await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new
    {
        predicate = (string)Invoke(typeof(PlayerWindow), "BuildPlayerDocumentPredicate", "abcdefghijk")!,
        kiosk = (string)Invoke(typeof(PlayerWindow), "BuildKioskModeScript", "abcdefghijk", Key.H, Array.Empty<Key>())!,
        early = (string)Invoke(typeof(PlayerWindow), "BuildEarlyKioskBootstrapScript", "abcdefghijk")!,
        media = (string)Invoke(typeof(PlayerWindow), "BuildMediaDiagnosticsScript")!
    }));
    var start = new System.Diagnostics.ProcessStartInfo("node") { UseShellExecute = false };
    start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "player-document-policy.test.cjs"));
    start.ArgumentList.Add(fixture);
    using var process = System.Diagnostics.Process.Start(start)!;
    await process.WaitForExitAsync();
    Check(process.ExitCode == 0, "Player document JavaScript regression failed");
});

await Test("Player audio reset, pitch, sound lifecycle and decode cancellation", async () =>
{
    var soundType = typeof(PlayerWindow).GetNestedType("SoundEffectAudioParams", BindingFlags.NonPublic)!;
    var neutral = Activator.CreateInstance(soundType, nonPublic: true)!;
    var colored = Activator.CreateInstance(soundType, nonPublic: true)!;
    soundType.GetProperty("PitchSemitones")!.SetValue(colored, 12d);
    soundType.GetProperty("Reverb")!.SetValue(colored, 0.5d);
    var fixture = Path.Combine(root, "audio-scripts.json");
    string Effects(double pitch) => (string)Invoke(typeof(PlayerWindow), "BuildEffectsScript",
        JsonSerializer.Serialize(new { Flags = new bool[15], AudioPitchSemitones = pitch }))!;
    string Sound(string kind, object parameters) => (string)Invoke(typeof(PlayerWindow), "BuildSoundEffectScript", kind, "AA==", false, parameters)!;
    await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new
    {
        neutralVideo = Effects(0), pitchVideo = Effects(12),
        edgeVideo = (string)Invoke(typeof(PlayerWindow), "BuildEffectsScript", JsonSerializer.Serialize(new
        {
            Flags = Enumerable.Range(0, 15).Select(index => index is 0 or 8).ToArray(),
            Fisheye = 0.5, FisheyeCenterX = 0d, FisheyeCenterY = 0d
        }))!,
        coloredSound = Sound("AuraFarm", colored), neutralSound = Sound("AuraFarm", neutral),
        otherSound = Sound("Laugh", neutral),
        neutralSync = (string)Invoke(typeof(PlayerWindow), "BuildSoundParamsScript", JsonSerializer.Serialize(neutral))!,
        eqSync = (string)Invoke(typeof(PlayerWindow), "BuildSoundParamsScript", "{\"Reverb\":0.5,\"PitchSemitones\":12,\"EqLowDb\":3}")!
    }));
    var start = new System.Diagnostics.ProcessStartInfo("node") { UseShellExecute = false };
    start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "player-audio-state.test.cjs"));
    start.ArgumentList.Add(fixture);
    using var process = System.Diagnostics.Process.Start(start)!;
    await process.WaitForExitAsync();
    Check(process.ExitCode == 0, "Audio state JavaScript regression failed");
});

await Test("Encrypted profile round trip and daily membership", async () =>
{
    var history = new WatchHistoryService(Fixture("profile-roundtrip"));
    var data = new WatchHistoryData { TotalXp = 321, Prestige = 2 };
    data.WatchedVideoIdsByDay["2026-11-19"] = ["abcdefghijk"];
    await history.SaveAsync(data);
    var loaded = await history.LoadAsync();
    Check(loaded.TotalXp == 321 && loaded.WatchedVideoIdsByDay["2026-11-19"].Contains("abcdefghijk"));
    Check(!File.ReadAllText(Path.Combine(Fixture("profile-roundtrip"), "watch-history.json")).Contains("TotalXp"));
});
await Test("Corrupt encryption recovers backup", async () =>
{
    var dir = Fixture("profile-crypto"); var service = new WatchHistoryService(dir);
    await service.SaveAsync(new() { TotalXp = 555 }); await service.SaveAsync(new() { TotalXp = 666 });
    await File.WriteAllTextAsync(Path.Combine(dir, "watch-history.json"), "{\"Version\":2,\"Protected\":true,\"Payload\":\"AQIDBA==\"}");
    Check((await service.LoadAsync()).TotalXp == 555);
});
await Test("Invalid base64 recovers backup", async () =>
{
    var dir = Fixture("profile-base64"); var service = new WatchHistoryService(dir);
    await service.SaveAsync(new() { TotalXp = 123 }); await service.SaveAsync(new() { TotalXp = 456 });
    await File.WriteAllTextAsync(Path.Combine(dir, "watch-history.json"), "{\"Version\":2,\"Protected\":true,\"Payload\":\"%%%\"}");
    Check((await service.LoadAsync()).TotalXp == 123);
});
await Test("All damaged profile copies fail without resetting user data", async () =>
{
    var dir = Fixture("profile-damaged"); var path = Path.Combine(dir, "watch-history.json");
    await File.WriteAllTextAsync(path, "broken");
    await Throws<InvalidDataException>(() => new WatchHistoryService(dir).LoadAsync());
    Check(await File.ReadAllTextAsync(path) == "broken");
});
await Test("Optional null collections normalized", async () =>
{
    var dir = Fixture("profile-null");
    await File.WriteAllTextAsync(Path.Combine(dir, "watch-history.json"), "{\"LastWatchedByVideoId\":{},\"FavoriteVideoIds\":null}");
    Check((await new WatchHistoryService(dir).LoadAsync()).FavoriteVideoIds is not null);
});
await Test("Mandatory null collection rejected", async () =>
{
    var dir = Fixture("profile-invalid-schema");
    await File.WriteAllTextAsync(Path.Combine(dir, "watch-history.json"), "{\"LastWatchedByVideoId\":null}");
    await Throws<InvalidDataException>(() => new WatchHistoryService(dir).LoadAsync());
});
await Test("Concurrent timecode transactions preserve all changes", async () =>
{
    var dir = Fixture("timecodes-concurrent");
    await Task.WhenAll(Enumerable.Range(0, 40).Select(i => new TimecodeService(dir).UpdateAsync(items => items.Add(new() { VideoId = "abcdefghijk", Label = i.ToString(), Seconds = i }))));
    Check((await new TimecodeService(dir).LoadAsync()).Count == 40);
});
await Test("Concurrent timecode deletions do not resurrect deleted entries", async () =>
{
    var service = new TimecodeService(Fixture("timecodes-delete"));
    await service.SaveAsync([new() { Id = "a", VideoId = "abcdefghijk" }, new() { Id = "b", VideoId = "abcdefghijk" }]);
    await Task.WhenAll(service.UpdateAsync(items => items.RemoveAll(x => x.Id == "a")), service.UpdateAsync(items => items.RemoveAll(x => x.Id == "b")));
    Check((await service.LoadAsync()).Count == 0);
});
await Test("Timecode write error propagates", async () =>
{
    var dir = Fixture("timecodes-denied"); Directory.CreateDirectory(Path.Combine(dir, "timecodes.json"));
    await Throws<IOException>(() => new TimecodeService(dir).SaveAsync([]));
});
await Test("Corrupt timecodes recover backup", async () =>
{
    var dir = Fixture("timecodes-recover"); var service = new TimecodeService(dir);
    await service.SaveAsync([new() { VideoId = "abcdefghijk", Label = "saved" }]); await service.SaveAsync([]);
    await File.WriteAllTextAsync(Path.Combine(dir, "timecodes.json"), "broken");
    Check((await service.LoadAsync()).Single().Label == "saved");
});
await Test("Settings and custom presets round trip", async () =>
{
    var service = new AppSettingsService(); var settings = await service.LoadAsync();
    settings.PlayerCustomPresetNames = ["Glass"];
    settings.PlayerCustomPresetPayloads = ["{}"];
    await service.SaveAsync(settings);
    var refreshed = await service.LoadAsync(); refreshed.Language = "en"; await service.SaveAsync(refreshed);
    Check((await service.LoadAsync()).PlayerCustomPresetNames[0] == "Glass");
});
await Test("Empty settings recover the last good backup", async () =>
{
    var service = new AppSettingsService(); var settings = await service.LoadAsync();
    settings.UiScale = 1.1; await service.SaveAsync(settings);
    settings.UiScale = 1.2; await service.SaveAsync(settings);
    await File.WriteAllTextAsync(Path.Combine(AppStoragePaths.GetCurrentAppDataDirectory(), "settings.json"), string.Empty);
    Check((await service.LoadAsync()).UiScale == 1.1);
    await service.SaveAsync(settings);
});
await Test("Wrong update size rejected", async () =>
{
    var file = Path.Combine(root, "update.zip"); await File.WriteAllTextAsync(file, "data");
    await Throws<InvalidDataException>(() => Validate(file, 999, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))));
});
await Test("Missing update hash rejected", async () =>
{
    var file = Path.Combine(root, "update.zip");
    await Throws<InvalidDataException>(() => Validate(file, 4, ""));
});
await Test("Wrong update hash rejected", async () =>
{
    var file = Path.Combine(root, "update.zip");
    await Throws<InvalidDataException>(() => Validate(file, 4, new string('0', 64)));
});
await Test("Correct update hash accepted", async () =>
{
    var file = Path.Combine(root, "update.zip"); await Validate(file, 4, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
});
await Test("Locked update destination uses alternative filename", async () =>
{
    var source = Path.Combine(root, "download.tmp"); var target = Path.Combine(root, "locked.zip");
    await File.WriteAllTextAsync(source, "new"); await File.WriteAllTextAsync(target, "old");
    using var hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var result = await (Task<string>)Invoke(typeof(GitHubUpdateService), "MoveDownloadedFileWithFallbackAsync", source, target, CancellationToken.None)!;
    Check(result != target && File.ReadAllText(result) == "new");
});
await Test("Only Windows x64 release archives can be selected", () =>
{
    foreach (var name in new[] { "app-linux-x64.zip", "app-win-arm64.zip", "app-win-x64-symbols.zip", "app-debug-win-x64.zip" })
        Check((int)Invoke(typeof(GitHubUpdateService), "GetPortableAssetPriority", name, ".zip")! == 0);
    Check((int)Invoke(typeof(GitHubUpdateService), "GetPortableAssetPriority", "app-win-x64.zip", ".zip")! > 0);
    return Task.CompletedTask;
});
await Test("Update script preserves special characters and includes rollback", async () =>
{
    var script = (string)Invoke(typeof(MainWindow), "BuildApplyUpdateScript", 42, @"C:\A&B\stage", @"C:\O'Brien [app]\target", "JokerDBDTracker.exe")!;
    Check(script.Contains(@"C:\A&B\stage") && script.Contains(@"C:\O''Brien [app]\target") && script.Contains("Copy-Tree $backup $target") && script.Contains("-LiteralPath"));
    await File.WriteAllTextAsync(Path.Combine(root, "updater.ps1"), script);
    Console.WriteLine("Updater syntax fixture: " + Path.Combine(root, "updater.ps1"));
});
await Test("ZIP traversal rejected", () =>
{
    var zip = Path.Combine(root, "traversal.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var stream = archive.CreateEntry("../outside.txt").Open(); stream.WriteByte(1); }
    try { Invoke(typeof(MainWindow), "ExtractZipSafely", zip, Fixture("extract")); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return Task.CompletedTask; }
    throw new Exception("Traversal was not rejected");
});
await Test("Playback coverage merges overlaps and does not count repeats", () =>
{
    var coverage = new PlaybackCoverage(); coverage.Add(0, 10); coverage.Add(5, 20); coverage.Add(0, 20); coverage.Add(30, 40);
    coverage.Add(double.NaN, 50); coverage.Add(-1, 5); coverage.Add(8, 4);
    Check(coverage.Seconds == 30); return Task.CompletedTask;
});
await Test("Profile dates use Moscow midnight regardless of system time zone", () =>
{
    Check(ProfileClock.Day(new DateTime(2026, 11, 19, 20, 59, 59, DateTimeKind.Utc)) == new DateOnly(2026, 11, 19));
    Check(ProfileClock.Day(new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc)) == new DateOnly(2026, 11, 20));
    return Task.CompletedTask;
});
await Test("Reserved, modifier, and invalid key bindings are rejected", () =>
{
    Check(!KeyBindingRules.IsAllowed(Key.M) && !KeyBindingRules.IsAllowed(Key.LeftCtrl) && !KeyBindingRules.IsAllowed((Key)9999));
    Check(KeyBindingRules.IsAllowed(Key.J) && KeyBindingRules.Canonical(Key.NumPad5) == Key.D5);
    return Task.CompletedTask;
});
await Test("Legacy duplicate key bindings and numeric aliases are normalized", async () =>
{
    var service = new AppSettingsService(); var settings = await service.LoadAsync();
    settings.Effect1Bind = "Y"; settings.Effect2Bind = "NumPad1";
    await service.SaveAsync(settings); var normalized = await service.LoadAsync();
    var bindings = typeof(AppSettingsData).GetProperties().Where(p => p.Name.EndsWith("Bind", StringComparison.Ordinal))
        .Select(p => KeyBindingRules.Canonical(Enum.Parse<Key>((string)p.GetValue(normalized)!, true))).ToList();
    Check(bindings.Distinct().Count() == bindings.Count);
});
await Test("Catalog metadata uses rows rather than incidental numeric content", () =>
{
    using var document = JsonDocument.Parse("""
        {"metadata":{"lockupMetadataViewModel":{"title":{"content":"Stream title"},"incidental":{"content":"248"},"metadata":{"contentMetadataViewModel":{"metadataRows":[{"metadataParts":[{"text":{"content":"1 day ago"}},{"text":{"content":"248 views"}}]}]}}}}}
        """);
    var label = (string)Invoke(typeof(YouTubeStreamsService), "ReadLockupMetadataText", document.RootElement)!;
    Check(label == "1 day ago · 248 views");
    return Task.CompletedTask;
});
async Task RunUpdater(string source, string target)
{
    var script = (string)Invoke(typeof(MainWindow), "BuildApplyUpdateScript", int.MaxValue, source, target, "no-executable-in-fixture.exe")!;
    var path = Path.Combine(Path.GetDirectoryName(source)!, "apply.ps1");
    await File.WriteAllTextAsync(path, script);
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path },
        UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        RedirectStandardError = true, RedirectStandardOutput = true
    })!;
    var errors = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    Check(process.ExitCode == 0, await errors);
    await output;
}
await Test("Updater applies files using literal paths containing special characters", async () =>
{
    var dir = Fixture("updater A&B [literal]");
    var source = Path.Combine(dir, "stage"); var target = Path.Combine(dir, "app");
    Directory.CreateDirectory(source); Directory.CreateDirectory(target);
    await File.WriteAllTextAsync(Path.Combine(source, "app.dll"), "new");
    await File.WriteAllTextAsync(Path.Combine(target, "app.dll"), "old");
    await RunUpdater(source, target);
    Check(await File.ReadAllTextAsync(Path.Combine(target, "app.dll")) == "new");
    Check(Directory.GetDirectories(dir, "rollback_*").Length == 1);
});
await Test("Updater handles spaced directories ending in a separator in Windows PowerShell", async () =>
{
    var dir = Fixture("updater trailing separator");
    var source = Path.Combine(dir, "stage folder") + Path.DirectorySeparatorChar;
    var target = Path.Combine(dir, "app folder") + Path.DirectorySeparatorChar;
    Directory.CreateDirectory(source); Directory.CreateDirectory(target);
    await File.WriteAllTextAsync(Path.Combine(source, "app.dll"), "new");
    await File.WriteAllTextAsync(Path.Combine(target, "app.dll"), "old");
    await RunUpdater(source, target);
    Check(await File.ReadAllTextAsync(Path.Combine(target, "app.dll")) == "new");
    Check((await File.ReadAllTextAsync(Path.Combine(target, "update_apply.log"))).Contains("Update completed successfully."));
});
await Test("Updater restores the old version after a failed copy", async () =>
{
    var dir = Fixture("updater-rollback");
    var source = Path.Combine(dir, "stage"); var target = Path.Combine(dir, "app");
    Directory.CreateDirectory(source); Directory.CreateDirectory(target);
    var lockedSource = Path.Combine(source, "app.dll");
    await File.WriteAllTextAsync(lockedSource, "new");
    await File.WriteAllTextAsync(Path.Combine(target, "app.dll"), "old");
    await File.WriteAllTextAsync(Path.Combine(source, "introduced.dll"), "new-only");
    using var hold = new FileStream(lockedSource, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    await RunUpdater(source, target);
    Check(await File.ReadAllTextAsync(Path.Combine(target, "app.dll")) == "old");
    Check(!File.Exists(Path.Combine(target, "introduced.dll")));
    Check((await File.ReadAllTextAsync(Path.Combine(target, "update_apply.log"))).Contains("restoring backup"));
});
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
Environment.ExitCode = failed == 0 ? 0 : 1;
