using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TAS.EverestInterop;

/// Where to fetch a Studio release archive and how to verify it. Filled per platform at the call site.
public sealed class StudioDownload(string url, string archiveName, string md5Checksum) {
    public string Url { get; } = url;
    public string ArchiveName { get; } = archiveName;
    public string Md5Checksum { get; } = md5Checksum;
}

/// Generic download/verify/extract/launch machinery for an external editor, independent of which game embeds it.
/// The install is version-gated: a <c>.version</c> marker in the target directory is compared against the requested
/// version, so a matching install is a no-op and a mismatch triggers a fresh download.
public static class StudioInstaller {
    public static async Task InstallAsync(string studioDirectory, string version, StudioDownload download) {
        string versionFile = Path.Combine(studioDirectory, ".version");
        if (File.Exists(versionFile) && File.ReadAllText(versionFile) == version) {
            Log.Info("Studio", $"Studio {version} already installed");
            return;
        }

        Directory.CreateDirectory(studioDirectory);
        string archivePath = Path.Combine(studioDirectory, download.ArchiveName);

        if (!ArchiveMatches(archivePath, download.Md5Checksum)) {
            if (File.Exists(archivePath)) {
                File.Delete(archivePath);
            }
            Log.Info("Studio", $"Downloading Studio {version} from {download.Url}");
            await DownloadFileAsync(download.Url, archivePath).ConfigureAwait(false);

            if (!ArchiveMatches(archivePath, download.Md5Checksum)) {
                throw new InvalidOperationException($"Checksum mismatch for downloaded archive {download.ArchiveName}");
            }
        }

        // Extract into a temp dir and only swap it in once complete, so an interrupted install can't leave a
        // half-written Studio behind.
        string tempDir = Path.Combine(studioDirectory, ".temp_install");
        if (Directory.Exists(tempDir)) {
            Directory.Delete(tempDir, recursive: true);
        }
        Directory.CreateDirectory(tempDir);

        Log.Info("Studio", $"Extracting {archivePath}");
        ZipFile.ExtractToDirectory(archivePath, tempDir);
        File.WriteAllText(Path.Combine(tempDir, ".version"), version);

        foreach (string file in Directory.GetFiles(studioDirectory)) {
            if (file != archivePath) {
                File.Delete(file);
            }
        }
        foreach (string dir in Directory.GetDirectories(studioDirectory)) {
            if (dir != tempDir) {
                Directory.Delete(dir, recursive: true);
            }
        }
        foreach (string file in Directory.GetFiles(tempDir)) {
            File.Move(file, Path.Combine(studioDirectory, Path.GetFileName(file)));
        }
        foreach (string dir in Directory.GetDirectories(tempDir)) {
            Directory.Move(dir, Path.Combine(studioDirectory, Path.GetFileName(dir)));
        }
        Directory.Delete(tempDir, recursive: true);

        Log.Info("Studio", $"Installed Studio {version}");
    }

    public static void Launch(string executablePath) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            // Launch through Explorer to detach Studio from the game process (avoids Steam-overlay issues).
            Process.Start("explorer", $"\"{executablePath}\"");
            return;
        }

        // r2modman extracts the package from a zip, which drops the unix exec bit.
        MakeExecutable(executablePath);
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false });
    }

    private static void MakeExecutable(string path) {
        try {
            using var proc = Process.Start(new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false });
            proc?.WaitForExit();
        } catch (Exception e) {
            Log.Error("Studio", $"Failed to chmod +x {path}: {e}");
        }
    }

    private static bool ArchiveMatches(string path, string expectedMd5) {
        if (!File.Exists(path)) {
            return false;
        }

        using var md5 = MD5.Create();
        using var fs = File.OpenRead(path);
        string actual = BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "");
        return string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadFileAsync(string url, string destination) {
        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var dst = File.Create(destination);
        await src.CopyToAsync(dst).ConfigureAwait(false);
    }
}
