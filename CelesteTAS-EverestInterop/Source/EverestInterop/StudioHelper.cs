// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.Tools;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class StudioHelper {
    #region Auto-filled values

    // These values will automatically get filled in by the Build.yml/Release.yml actions
    private const bool   DoubleZipArchive        = false; //DOUBLE_ZIP_ARCHIVE
    public  const string CurrentStudioVersion    = "##STUDIO_VERSION##";

    private const string DownloadURL_Windows_x64 = "##URL_WINDOWS_x64##";
    private const string DownloadURL_Linux_x64   = "##URL_LINUX_x64##";
    private const string DownloadURL_MacOS_x64   = "##URL_MACOS_x64##";
    private const string DownloadURL_MacOS_ARM64 = "##URL_MACOS_ARM64##";

    private const string FileName_Windows_x64    = "##FILENAME_WINDOWS_x64##";
    private const string FileName_Linux_x64      = "##FILENAME_LINUX_x64##";
    private const string FileName_MacOS_x64       = "##FILENAME_MACOS_x64##";
    private const string FileName_MacOS_ARM64    = "##FILENAME_MACOS_ARM64##";

    private const string Checksum_Windows_x64    = "##CHECKSUM_WINDOWS_x64##";
    private const string Checksum_Linux_x64      = "##CHECKSUM_LINUX_x64##";
    private const string Checksum_MacOS_x64      = "##CHECKSUM_MACOS_x64##";
    private const string Checksum_MacOS_ARM64    = "##CHECKSUM_MACOS_ARM64##";

    #endregion

    private static bool installed = false;

    internal static ConfigEntry<bool> ConfigLaunchStudioOnBoot = null!;
    internal static ConfigEntry<KeyboardShortcut> ConfigLaunchStudioShortcut = null!;

    private static string StudioDirectory => Path.Combine(Paths.GameRootPath, "CelesteStudio");
    private static string TempStudioInstallDirectory => Path.Combine(StudioDirectory, ".temp_install");
    private static string VersionFile => Path.Combine(StudioDirectory, ".version");
    private static string DownloadPath => Path.Combine(StudioDirectory, FileName);
    private static string InnerArchivePath => Path.Combine(StudioDirectory, ".InnerArchive.zip");

    private static bool IsWine() => WineInterop.IsWine();

    private static string UnixPathExpr(string windowsPath) {
        string prefix = Environment.GetEnvironmentVariable("WINEPREFIX") ?? "";
        string unixPrefix = prefix.Length > 2 && prefix[1] == ':' ? WineInterop.WindowsToUnix(prefix) : prefix.Replace('\\', '/');
        char drive = char.ToLowerInvariant(windowsPath[0]);
        string rel = windowsPath[3..].Replace('\\', '/');
        return $"$(readlink -f \"{unixPrefix}/dosdevices/{drive}:\")/{rel}";
    }

    private static void RunUnixScript(string name, string script) {
        string scriptPath = $"/tmp/{name}";
        File.WriteAllText(WineInterop.UnixToWindows(scriptPath), script);
        Process.Start("start", $"/unix /bin/sh {scriptPath}");
    }

    private static string DownloadURL {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWine()) {
                return DownloadURL_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return DownloadURL_MacOS_ARM64;
            }

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }
    private static string FileName {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWine()) {
                return FileName_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return FileName_MacOS_ARM64;
            }

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }
    private static string Checksum {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWine()) {
                return Checksum_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return Checksum_MacOS_ARM64;
            }

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }

    [Initialize(-1)]
    private static void Load() {
        if (SyncChecker.Active) {
            return;
        }

        ConfigLaunchStudioOnBoot = TasMod.Instance.Config.Bind("Studio", "Launch on start", false);
        ConfigLaunchStudioShortcut = TasMod.Instance.Config.Bind("Studio", "Launch", new KeyboardShortcut());

        // INSTALL_STUDIO is only set for release builds, since otherwise the URLs / checksums are invalid
#if INSTALL_STUDIO
        // Check if Studio is already up-to-date
        string installedVersion = "<None>";
        if (!File.Exists(VersionFile) || (installedVersion = File.ReadAllText(VersionFile)) != CurrentStudioVersion) {
            $"Celeste Studio version mismatch: Expected '{CurrentStudioVersion}', found '{installedVersion}'. Installing current version...".Log();

            Task.Run(async () => {
                try {
                    // Close all Studio instances to avoid issues with file usage
                    foreach (var process in Process.GetProcesses().Where(process => process.ProcessName is "CelesteStudio" or "CelesteStudio.WPF" or "CelesteStudio.GTK" or "CelesteStudio.Mac" or "Celeste Studio")) {
                        $"Closing process {process} ({process.Id})...".Log(LogLevel.Debug);
                        process.Terminate();
                        await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                        // Make sure it's _really_ closed
                        try {
                            process.Kill();
                            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        } catch {
                            // ignore
                        }
                        "Process terminated".Log(LogLevel.Debug);
                    }
                } catch (Exception ex) {
                    ex.LogException("Failed to close other studio instances");
                }

                try {
                    await DownloadStudio().ConfigureAwait(false);
                    installed = true;
                } catch (Exception ex) {
                    ReportError("Failed to install Studio", ex.ToString());

                    // Cleanup install
                    if (Directory.Exists(TempStudioInstallDirectory)) {
                        Directory.Delete(TempStudioInstallDirectory, recursive: true);
                    }
                    StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;

                    throw;
                }
            });
        } else {
            installed = true;
        }
#else
        installed = true;
#endif
    }

    [Initialize]
    private static void Initialize() {
        if (ConfigLaunchStudioOnBoot.Value) {
            LaunchStudio();
        }
    }

    private static async Task DownloadStudio() {
        using var md5 = MD5.Create();

        // Skip download if valid ZIP already exists
        bool skipDownload = false;

        if (File.Exists(DownloadPath)) {
            using var fs = OpenReadShared(DownloadPath);
            string hash = BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "");
            if (Checksum.Equals(hash, StringComparison.OrdinalIgnoreCase)) {
                skipDownload = true;
            } else {
                $"Checksum for {FileName} doesn't match. Expected {Checksum}, found {hash}".Log(LogLevel.Debug);
            }
        }

        if (!skipDownload) {
            // Existing archive doesn't match
            if (File.Exists(DownloadPath)) {
                File.Delete(DownloadPath);
            }

            if (!Directory.Exists(StudioDirectory)) {
                Directory.CreateDirectory(StudioDirectory);
            }

            // Download Studio archive
            $"Starting download of '{DownloadURL}'...".Log();

            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Download;
            await DownloadFileAsync(DownloadURL, DownloadPath).ConfigureAwait(false);

            if (!File.Exists(DownloadPath)) {
                ReportError("Download failed! The Studio archive went missing");
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                return;
            }
            "Finished download".Log();
            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Install;

            // Handle double ZIPs caused by GitHub actions
            if (DoubleZipArchive) {
                if (File.Exists(InnerArchivePath)) {
                    File.Delete(InnerArchivePath);
                }

                using (var zip = ZipFile.OpenRead(DownloadPath)) {
                    var entry = zip.Entries[0]; // There should only be a single entry in this case
                    $"Extracting inner ZIP archive: '{entry.Name}'".Log(LogLevel.Debug);

                    entry.ExtractToFile(InnerArchivePath);
                }

                if (File.Exists(DownloadPath)) {
                    File.Delete(DownloadPath);
                }
                File.Move(InnerArchivePath, DownloadPath);
            }
        }

        StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Install;

        // Verify checksum
        using (var fs = OpenReadShared(DownloadPath)) {
            string hash = BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "");
            if (!Checksum.Equals(hash, StringComparison.OrdinalIgnoreCase)) {
                ReportError($"Download failed! Invalid checksum for Studio archive file: Expected {Checksum} got {hash}");
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                return;
            }
            $"Downloaded Studio archive has a valid checksum: {hash}".Log(LogLevel.Debug);
        }

        // Install to another directory and only delete the old install once it was successful
        if (Directory.Exists(TempStudioInstallDirectory)) {
            Directory.Delete(TempStudioInstallDirectory, recursive: true);
        }
        Directory.CreateDirectory(TempStudioInstallDirectory);

        // Extract
        $"Extracting {DownloadPath} into {TempStudioInstallDirectory}".Log();
        ZipFile.ExtractToDirectory(DownloadPath, TempStudioInstallDirectory);
        "Successfully extracted Studio archive".Log();

        // Store installed version number
        File.WriteAllText(Path.Combine(TempStudioInstallDirectory, Path.GetFileName(VersionFile)), CurrentStudioVersion);

        // Fix lost file permissions
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsWine())) {
            var studioBinary = Path.Combine(TempStudioInstallDirectory, "CelesteStudio");
            bool success = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                success = ExecuteCommand(["chmod", "+x", studioBinary],
                    errorMessage: "Install failed! Couldn't make Studio executable");
            } else {
                string marker = $"{studioBinary}.ok";
                string errorFile = $"{studioBinary}.err";
                string script =
                    $"bin=\"{UnixPathExpr(studioBinary)}\"; " +
                    "err=\"$bin.err\"; ok=\"$bin.ok\"; " +
                    "chmod +x \"$bin\" 2> \"$err\" && rm -f \"$err\" && touch \"$ok\" || echo \"chmod exited with $?\" >> \"$err\"";
                try {
                    RunUnixScript("ninesols-tas-chmod.sh", script);
                } catch (Exception ex) {
                    ex.LogException("Couldn't make Studio executable");
                }

                // start.exe spawns the unix child and exits immediately, so poll for its outcome instead of waiting on it
                var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (DateTime.UtcNow < timeout) {
                    if (File.Exists(marker)) {
                        success = true;
                        break;
                    }

                    if (File.Exists(errorFile)) {
                        break;
                    }

                    Thread.Sleep(100);
                }

                if (File.Exists(marker)) {
                    File.Delete(marker);
                } else {
                    string reason = File.Exists(errorFile)
                        ? File.ReadAllText(errorFile)
                        : "the script did not finish in time";
                    string shortReason = reason.Split('\n')[0];
                    ReportError($"Install failed! Couldn't make Studio executable: {shortReason}", $"Command: start /unix /bin/sh /tmp/ninesols-tas-chmod.sh\n{script}\n{reason}");
                }
            }

            if (!success) {
                Directory.Delete(TempStudioInstallDirectory, recursive: true);
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                return;
            }
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            if (!ExecuteCommand(["chmod", "+x", Path.Combine(TempStudioInstallDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio")],
                    errorMessage: "Install failed! Couldn't make Studio executable") ||
                !ExecuteCommand(["xattr", "-c", Path.Combine(TempStudioInstallDirectory, "CelesteStudio.app")],
                    errorMessage: "Install failed! Couldn't clear Studio app bundle config"))
            {
                Directory.Delete(TempStudioInstallDirectory, recursive: true);
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                return;
            }
        }

        // Cleanup old install
        foreach (string file in Directory.GetFiles(StudioDirectory)) {
            if (file == DownloadPath || file == TempStudioInstallDirectory) {
                continue;
            }

            File.Delete(file);
        }
        foreach (string file in Directory.GetDirectories(StudioDirectory)) {
            if (file == DownloadPath || file == TempStudioInstallDirectory) {
                continue;
            }

            Directory.Delete(file, recursive: true);
        }

        // Setup new install
        foreach (string file in Directory.GetFiles(TempStudioInstallDirectory)) {
            File.Move(file, Path.Combine(StudioDirectory, Path.GetFileName(file)));
        }
        foreach (string file in Directory.GetDirectories(TempStudioInstallDirectory)) {
            Directory.Move(file, Path.Combine(StudioDirectory, Path.GetFileName(file)));
        }
        Directory.Delete(TempStudioInstallDirectory, recursive: true);

        StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Success;

        "Successfully installed Studio".Log();
    }

    private static bool ExecuteCommand(string[] parameters, string errorMessage) {
        var startInfo = new ProcessStartInfo(parameters[0]) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string param in parameters[1..]) {
            startInfo.ArgumentList.Add(param);
        }

        var proc = Process.Start(startInfo)!;
        proc.WaitForExit();

        string? line;

        if (proc.ExitCode != 0) {
            List<string> stdoutLines = [], stderrLines = [];
            while ((line = proc.StandardOutput.ReadLine()) != null) {
                stdoutLines.Add(line);
            }
            while ((line = proc.StandardError.ReadLine()) != null) {
                stderrLines.Add(line);
            }

            ReportError(
                $"{errorMessage}: Exit Code {proc.ExitCode}",
                $"""
                 Standard Out:
                 {string.Join(Environment.NewLine, stdoutLines)}
                 Standard Error:
                 {string.Join(Environment.NewLine, stderrLines)}
                 """);
            stdoutLines.ForEach(l => l.Log());
            stderrLines.ForEach(l => l.Log(LogLevel.Error));
        } else {
            while ((line = proc.StandardOutput.ReadLine()) != null) {
                line.Log(LogLevel.Debug);
            }
        }

        return proc.ExitCode == 0;
    }

    internal static void LaunchStudio() => Task.Run(async () => {
        if (SyncChecker.Active) {
            return;
        }

        "Launching Studio...".Log();
        try {
            var start = DateTime.UtcNow;
            var timeout = TimeSpan.FromMinutes(10);
            while (!installed) {
                if ((DateTime.UtcNow - start) > timeout) {
                    "Timed-out while waiting for Studio installation to be verified".Log(LogLevel.Warning);
                    return;
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }

            if (Process.GetProcesses().Any(process => process.ProcessName == "CelesteStudio")) {
                "Another Studio instance is already running".Log();
                return;
            }

            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Launch;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                if (IsWine()) {
                    var studioPath = Path.Combine(StudioDirectory, "CelesteStudio");
                    if (Environment.GetEnvironmentVariable("container") == "pressure-vessel") {
                        $"Couldn't launch Studio automatically: the game runs in a Steam container, start it yourself at {studioPath}".Log(LogLevel.Warning);
                        GameToasts.Show("Couldn't launch Studio automatically: start it yourself");
                        StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Inactive;
                        return;
                    }

                    var studioWinPath = Path.Combine(StudioDirectory, "CelesteStudio");
                    $"Starting process '{studioWinPath}'...".Log(LogLevel.Debug);
                    RunUnixScript("ninesols-tas-launch.sh", $"exec \"{UnixPathExpr(studioWinPath)}\"");
                } else {
                    var studioPath = Path.Combine(StudioDirectory, "CelesteStudio.exe");
                    $"Starting process '{studioPath}'...".Log(LogLevel.Debug);
                    // Start through explorer to detach Studio from the game process (and avoid issues with the Steam Overlay for example)
                    Process.Start("Explorer", $"\"{studioPath}\"");
                }
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                var studioPath = Path.Combine(StudioDirectory, "CelesteStudio");
                $"Starting process '{studioPath}'...".Log(LogLevel.Debug);
                Process.Start(new ProcessStartInfo(studioPath) { UseShellExecute = false });
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                var studioPath = Path.Combine(StudioDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio");
                $"Starting process '{studioPath}'...".Log(LogLevel.Debug);
                Process.Start(new ProcessStartInfo(studioPath) { UseShellExecute = false });
            }

            "Successfully launched Studio".Log();
        } catch (Exception ex) {
            ex.LogException("Failed to launch Studio");
        }
    });

    private static FileStream OpenReadShared(string path) {
        for (var attempt = 0; ; attempt++) {
            try {
                return File.OpenRead(path);
            } catch (IOException) when (attempt < 600) {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static async Task DownloadFileAsync(string url, string destination) {
        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var dst = File.Create(destination);
        await src.CopyToAsync(dst).ConfigureAwait(false);
    }

    private static void ReportError(string error, string? additionalInfo = null) {
        error.Log(LogLevel.Error);
        additionalInfo?.Log(LogLevel.Error);
        GameToasts.Show($"Studio: {error}");

        if (!Directory.Exists(StudioDirectory)) {
            Directory.CreateDirectory(StudioDirectory);
        }

        string path = Path.Combine(StudioDirectory, "error_report.txt");
        string text =
            $"""
             === NineSols TAS Studio v{CurrentStudioVersion} - Installation failed ===
             {DateTime.Now.ToString(CultureInfo.InvariantCulture)}

             {error}

             {additionalInfo ?? "<no additional information>"}
             """;

        File.WriteAllText(path, text);
        IOHelper.OpenInDefaultApp(path);
    }
}
