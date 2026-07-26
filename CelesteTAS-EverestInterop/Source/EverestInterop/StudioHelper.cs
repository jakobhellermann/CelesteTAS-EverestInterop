using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BepInEx;
using TAS.Module;

namespace TAS.EverestInterop;

/// Silksong-specific wiring around <see cref="StudioInstaller"/>: which archive to fetch per platform/arch, where to
/// install it, and when to launch it. The download coordinates are placeholder tokens filled by the release CI (see
/// tools/build-studio-release.sh); INSTALL_STUDIO is defined only for those builds, so a local/dev build compiles the
/// install away and simply launches an already-present Studio if one exists.
public static class StudioHelper {
    #region Auto-filled by the release CI

    private const string CurrentStudioVersion = "##STUDIO_VERSION##";

    private const string DownloadURL_Windows_x64 = "##URL_WINDOWS_x64##";
    private const string DownloadURL_Linux_x64   = "##URL_LINUX_x64##";
    private const string DownloadURL_MacOS_x64   = "##URL_MACOS_x64##";
    private const string DownloadURL_MacOS_ARM64 = "##URL_MACOS_ARM64##";

    private const string FileName_Windows_x64 = "##FILENAME_WINDOWS_x64##";
    private const string FileName_Linux_x64   = "##FILENAME_LINUX_x64##";
    private const string FileName_MacOS_x64   = "##FILENAME_MACOS_x64##";
    private const string FileName_MacOS_ARM64 = "##FILENAME_MACOS_ARM64##";

    private const string Checksum_Windows_x64 = "##CHECKSUM_WINDOWS_x64##";
    private const string Checksum_Linux_x64   = "##CHECKSUM_LINUX_x64##";
    private const string Checksum_MacOS_x64   = "##CHECKSUM_MACOS_x64##";
    private const string Checksum_MacOS_ARM64 = "##CHECKSUM_MACOS_ARM64##";

    #endregion

    private static string StudioDirectory => Path.Combine(Paths.GameRootPath, "CelesteStudio");

    private static string ExecutableName {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "CelesteStudio.WPF.exe";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "CelesteStudio.GTK";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "CelesteStudio.Mac";
            throw new PlatformNotSupportedException(RuntimeInformation.OSDescription);
        }
    }

    private static StudioDownload CurrentDownload {
        get {
            var arch = RuntimeInformation.OSArchitecture;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && arch == Architecture.X64) {
                return new StudioDownload(DownloadURL_Windows_x64, FileName_Windows_x64, Checksum_Windows_x64);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && arch == Architecture.X64) {
                return new StudioDownload(DownloadURL_Linux_x64, FileName_Linux_x64, Checksum_Linux_x64);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && arch == Architecture.X64) {
                return new StudioDownload(DownloadURL_MacOS_x64, FileName_MacOS_x64, Checksum_MacOS_x64);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && arch == Architecture.Arm64) {
                return new StudioDownload(DownloadURL_MacOS_ARM64, FileName_MacOS_ARM64, Checksum_MacOS_ARM64);
            }

            throw new PlatformNotSupportedException($"{RuntimeInformation.OSDescription} ({arch})");
        }
    }

    private static volatile bool installed;

    [Initialize]
    private static void Initialize() {
#if INSTALL_STUDIO
        installed = false;
        Task.Run(async () => {
            try {
                await StudioInstaller.InstallAsync(StudioDirectory, CurrentStudioVersion, CurrentDownload);
                installed = true;
            } catch (Exception e) {
                Log.Error("Studio", $"Failed to install Studio: {e}");
            }
        });
#else
        installed = true;
#endif

        if (TasMod.Instance.TasSettings.LaunchStudioAtBoot.Value) {
            LaunchStudio();
        }
    }

    internal static void LaunchStudio() => Task.Run(async () => {
        try {
            // Wait for a possibly in-progress install before looking for the executable.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!installed) {
                if (DateTime.UtcNow > deadline) {
                    Log.Warn("Studio", "Timed out waiting for Studio installation");
                    return;
                }
                await Task.Delay(500);
            }

            string executable = Path.Combine(StudioDirectory, ExecutableName);
            if (!File.Exists(executable)) {
                Log.Info("Studio", $"Studio executable not found at {executable}, skipping launch");
                return;
            }

            Log.Info("Studio", $"Launching Studio at {executable}");
            StudioInstaller.Launch(executable);
        } catch (Exception e) {
            Log.Error("Studio", $"Failed to launch Studio: {e}");
        }
    });
}
