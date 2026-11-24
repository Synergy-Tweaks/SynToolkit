// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.IO;
using System.IO.Enumeration;

#nullable enable

namespace SynToolkit.Drives.Core;

public sealed class SpaceNode
{
    public required string Name { get; set; }
    public required string FullPath { get; init; }
    public SpaceNode? Parent { get; init; }
    public long FileBytes { get; set; }
    public int FileCount { get; set; }
    public long Size { get; set; }
    public List<SpaceNode> Children { get; set; } = [];
}

public sealed record BigFile(string FullPath, long Size);

public sealed class SpaceScanner
{
    private const int BigCount = 40;

    private long _files;
    private long _bytes;
    private long _bigFloor = 50L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly List<BigFile> _big = [];

    public long Files => Interlocked.Read(ref _files);
    public long Bytes => Interlocked.Read(ref _bytes);

    public IReadOnlyList<BigFile> BigFiles
    {
        get
        {
            lock (_gate)
            {
                return _big.OrderByDescending(file => file.Size).Take(BigCount).ToList();
            }
        }
    }

    private static readonly EnumerationOptions Flat = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    public SpaceNode Scan(string root, CancellationToken ct = default)
    {
        SpaceNode node = new() { Name = root, FullPath = root };
        ScanNode(node, 0, ct);
        return node;
    }

    private void ScanNode(SpaceNode node, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<SpaceNode> directories = [];
        long fileBytes = 0;
        int fileCount = 0;
        try
        {
            FileSystemEnumerable<(string Name, bool IsDir, long Len)> entries = new(
                node.FullPath,
                static (ref FileSystemEntry entry) => (entry.FileName.ToString(), entry.IsDirectory, entry.IsDirectory ? 0 : entry.Length),
                Flat);

            foreach ((string name, bool isDir, long len) in entries)
            {
                if (isDir)
                {
                    directories.Add(new SpaceNode
                    {
                        Name = name,
                        FullPath = Path.Combine(node.FullPath, name),
                        Parent = node
                    });
                }
                else
                {
                    fileBytes += len;
                    fileCount++;
                    if (len > Interlocked.Read(ref _bigFloor))
                    {
                        Track(Path.Combine(node.FullPath, name), len);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }

        node.FileBytes = fileBytes;
        node.FileCount = fileCount;
        Interlocked.Add(ref _files, fileCount);
        Interlocked.Add(ref _bytes, fileBytes);

        if (depth < 3 && directories.Count > 1)
        {
            Parallel.ForEach(directories, new ParallelOptions { CancellationToken = ct }, dir => ScanNode(dir, depth + 1, ct));
        }
        else
        {
            foreach (SpaceNode dir in directories)
            {
                ScanNode(dir, depth + 1, ct);
            }
        }

        directories.Sort((left, right) => right.Size.CompareTo(left.Size));
        node.Children = directories;
        node.Size = fileBytes + directories.Sum(dir => dir.Size);
    }

    private void Track(string path, long len)
    {
        lock (_gate)
        {
            _big.Add(new BigFile(path, len));
            if (_big.Count > BigCount * 2)
            {
                _big.Sort((left, right) => right.Size.CompareTo(left.Size));
                _big.RemoveRange(BigCount, _big.Count - BigCount);
                Interlocked.Exchange(ref _bigFloor, _big[^1].Size);
            }
        }
    }
}
