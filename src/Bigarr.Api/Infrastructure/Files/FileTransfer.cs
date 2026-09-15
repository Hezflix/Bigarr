using System.Runtime.InteropServices;
using Bigarr.Domain.Library;

namespace Bigarr.Api.Infrastructure.Files;

public enum TransferOutcome
{
    Hardlinked = 1,
    Copied = 2,
    Moved = 3,
}

/// <summary>
/// Puts a downloaded file into the library (blueprint §5): hardlink when both paths share a filesystem so the torrent
/// keeps seeding for free, copy when they do not (or when asked), move when the user accepts losing the seed.
/// Copies land under a temporary name and are renamed at the end, so a crash never leaves a half file in place.
/// </summary>
public sealed partial class FileTransfer(ILogger<FileTransfer> logger)
{
    private const string PartialSuffix = ".bigarr-partial";

    public async Task<TransferOutcome> TransferAsync(string source, string destination, TransferMode mode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Source file not found.", source);
        }

        if (File.Exists(destination))
        {
            throw new IOException($"Destination already exists: {destination}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        switch (mode)
        {
            case TransferMode.Move:
                await MoveAsync(source, destination, ct);
                return TransferOutcome.Moved;
            case TransferMode.Copy:
                await CopyAsync(source, destination, ct);
                return TransferOutcome.Copied;
            default:
                if (TryHardlink(source, destination, out var reason))
                {
                    return TransferOutcome.Hardlinked;
                }

                logger.LogInformation("Hardlink not possible for {Source} → {Destination} ({Reason}); copying instead", source, destination, reason);
                await CopyAsync(source, destination, ct);
                return TransferOutcome.Copied;
        }
    }

    /// <summary>Same inode, two names: the seed and the library share the bytes. False with the reason when the OS refuses.</summary>
    public static bool TryHardlink(string source, string destination, out string? reason)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (CreateHardLinkW(destination, source, IntPtr.Zero))
                {
                    reason = null;
                    return true;
                }

                var error = Marshal.GetLastPInvokeError();
                reason = error == 17 ? "different volumes" : $"Win32 error {error}";
                return false;
            }

            if (Link(source, destination) == 0)
            {
                reason = null;
                return true;
            }

            var errno = Marshal.GetLastPInvokeError();
            reason = errno switch
            {
                1 => "not permitted on this filesystem (EPERM)",
                18 => "different filesystems (EXDEV)",
                31 => "too many links to the file (EMLINK)",
                45 or 95 => "hardlinks not supported by the filesystem",
                _ => $"errno {errno}",
            };
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            reason = $"native link() unavailable: {ex.Message}";
            return false;
        }
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        var partial = destination + PartialSuffix;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, ct);
            }

            File.Move(partial, destination, overwrite: false);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static async Task MoveAsync(string source, string destination, CancellationToken ct)
    {
        try
        {
            File.Move(source, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(source) && !File.Exists(destination))
        {
            // Across devices the runtime normally copies for us; when it does not, do it by hand.
            await CopyAsync(source, destination, ct);
            File.Delete(source);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    static FileTransfer()
    {
        // "libc" alone does not resolve on glibc runtimes without the -dev package, nor on musl: try the real names.
        NativeLibrary.SetDllImportResolver(typeof(FileTransfer).Assembly, (name, assembly, path) =>
        {
            if (name != LibcName)
            {
                return IntPtr.Zero;
            }

            string[] candidates = OperatingSystem.IsMacOS()
                ? ["libSystem.dylib", "libc.dylib"]
                : ["libc.so.6", "libc.musl-x86_64.so.1", "libc.musl-aarch64.so.1", "libc.so", "libc"];
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        });
    }

    private const string LibcName = "bigarr-libc";

    [DllImport(LibcName, EntryPoint = "link", SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
