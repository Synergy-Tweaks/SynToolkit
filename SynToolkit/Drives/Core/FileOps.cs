// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.IO;
using System.IO.Enumeration;

namespace SynToolkit.Drives.Core;

public static class FileOps
{
    public readonly record struct DeleteResult(long Freed, int Files, int Failed);

    private static EnumerationOptions Deep() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private static DateTime Cutoff(TimeSpan? minAge) => minAge is { } age ? DateTime.UtcNow - age : DateTime.MaxValue;

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static (long Bytes, int Files) Measure(string path, TimeSpan? minAge, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0;
        DateTime cutoff = Cutoff(minAge);
        try
        {
            if (SafeFs.TrustedPath(path) is not { } root)
            {
                return (0, 0);
            }

            if (File.Exists(root))
            {
                FileInfo fileInfo = new(root);
                return fileInfo.LastWriteTimeUtc <= cutoff ? (fileInfo.Length, 1) : (0, 0);
            }

            if (!Directory.Exists(root))
            {
                return (0, 0);
            }

            FileSystemEnumerable<(long Len, DateTime Mod)> entries = new(
                root,
                static (ref FileSystemEntry entry) => (entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                Deep())
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory
            };

            foreach ((long length, DateTime modifiedUtc) in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (modifiedUtc <= cutoff)
                {
                    bytes += length;
                    files++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return (bytes, files);
    }

    public static DeleteResult Delete(string path, TimeSpan? minAge, CancellationToken ct = default)
    {
        DateTime cutoff = Cutoff(minAge);
        if (SafeFs.TrustedPath(path) is not { } root)
        {
            return default;
        }

        Guard(path);
        Guard(root);

        if (File.Exists(root))
        {
            FileInfo fileInfo = new(root);
            if (fileInfo.LastWriteTimeUtc > cutoff)
            {
                return default;
            }

            long length = fileInfo.Length;
            return SafeFs.Delete(root, root) ? new DeleteResult(length, 1, 0) : new DeleteResult(0, 0, 1);
        }

        if (!Directory.Exists(root))
        {
            return default;
        }

        List<(string Path, long Len, DateTime Mod)> files;
        try
        {
            files = new FileSystemEnumerable<(string, long, DateTime)>(
                root,
                static (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                Deep())
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory
            }.ToList();
        }
        catch
        {
            return default;
        }

        long freed = 0;
        int ok = 0;
        int failed = 0;
        foreach ((string filePath, long len, DateTime mod) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (mod > cutoff)
            {
                continue;
            }

            if (SafeFs.Delete(filePath, root))
            {
                freed += len;
                ok++;
            }
            else
            {
                failed++;
            }
        }

        try
        {
            List<string> directories = new FileSystemEnumerable<string>(root, static (ref FileSystemEntry entry) => entry.ToFullPath(), Deep())
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => entry.IsDirectory
            }.ToList();

            foreach (string dir in directories.OrderByDescending(value => value.Count(ch => ch == '\\')))
            {
                SafeFs.Delete(dir, root);
            }
        }
        catch
        {
        }

        return new DeleteResult(freed, ok, failed);
    }

    public static void RemoveIfEmpty(string path)
    {
        if (SafeFs.TrustedPath(path) is not { } real || !Directory.Exists(real))
        {
            return;
        }

        try
        {
            Guard(real);
        }
        catch
        {
            return;
        }

        SafeFs.Delete(real, real);
    }

    public static DeleteResult Recycle(string path, TimeSpan? minAge)
    {
        if (SafeFs.TrustedPath(path) is not { } real)
        {
            return default;
        }

        if (Directory.Exists(real))
        {
            Guard(path);
            Guard(real);
            (long bytes, int count) = Measure(real, null);
            return Native.SendToRecycleBin(real) && !Directory.Exists(real)
                ? new DeleteResult(bytes, count, 0)
                : new DeleteResult(0, 0, 1);
        }

        if (!File.Exists(real))
        {
            return default;
        }

        Guard(path);
        Guard(real);
        FileInfo fileInfo = new(real);
        if (fileInfo.LastWriteTimeUtc > Cutoff(minAge))
        {
            return default;
        }

        long length = fileInfo.Length;
        return Native.SendToRecycleBin(real) && !File.Exists(real)
            ? new DeleteResult(length, 1, 0)
            : new DeleteResult(0, 0, 1);
    }

    private static readonly string[] Protected = new[]
    {
        Env.SystemRoot,
        Env.Windows,
        Path.Combine(Env.Windows, "System32"),
        Env.UserProfile,
        Path.GetDirectoryName(Env.UserProfile) ?? string.Empty,
        Env.ProgramFiles,
        Env.ProgramFilesX86,
        Env.ProgramData,
        Env.Local,
        Env.Roaming,
        Env.Downloads,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
    }.Where(path => path.Length > 0).Select(Normalize).ToArray();

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch
        {
            return path.TrimEnd('\\');
        }
    }

    public static void Guard(string path)
    {
        string normalized = Normalize(path);
        if (normalized.Length <= 3 || Protected.Any(protectedPath => string.Equals(protectedPath, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Refusing to clean a protected location: {path}");
        }
    }
}
