using System.IO.Enumeration;

namespace Roomy.Core;

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

/// <summary>Walks a whole drive and builds a folder-size tree. Read-only.</summary>
public sealed class SpaceScanner
{
    const int BigCount = 40;

    long _files, _bytes, _bigFloor = 50L * 1024 * 1024;
    readonly object _gate = new();
    readonly List<BigFile> _big = [];

    public long Files => Interlocked.Read(ref _files);
    public long Bytes => Interlocked.Read(ref _bytes);

    public IReadOnlyList<BigFile> BigFiles
    {
        get { lock (_gate) return _big.OrderByDescending(b => b.Size).Take(BigCount).ToList(); }
    }

    static readonly EnumerationOptions Flat = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public SpaceNode Scan(string root, CancellationToken ct = default)
    {
        var node = new SpaceNode { Name = root, FullPath = root };
        ScanNode(node, 0, ct);
        return node;
    }

    void ScanNode(SpaceNode node, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dirs = new List<SpaceNode>();
        long fileBytes = 0;
        int fileCount = 0;
        try
        {
            var e = new FileSystemEnumerable<(string Name, bool Dir, long Len)>(node.FullPath,
                (ref FileSystemEntry x) => (x.FileName.ToString(), x.IsDirectory, x.IsDirectory ? 0 : x.Length), Flat);
            foreach (var (name, dir, len) in e)
            {
                if (dir)
                {
                    dirs.Add(new SpaceNode { Name = name, FullPath = Path.Combine(node.FullPath, name), Parent = node });
                }
                else
                {
                    fileBytes += len;
                    fileCount++;
                    if (len > Interlocked.Read(ref _bigFloor)) Track(Path.Combine(node.FullPath, name), len);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }

        node.FileBytes = fileBytes;
        node.FileCount = fileCount;
        Interlocked.Add(ref _files, fileCount);
        Interlocked.Add(ref _bytes, fileBytes);

        if (depth < 3 && dirs.Count > 1)
            Parallel.ForEach(dirs, new ParallelOptions { CancellationToken = ct }, d => ScanNode(d, depth + 1, ct));
        else
            foreach (var d in dirs) ScanNode(d, depth + 1, ct);

        dirs.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Children = dirs;
        node.Size = fileBytes + dirs.Sum(d => d.Size);
    }

    void Track(string path, long len)
    {
        lock (_gate)
        {
            _big.Add(new BigFile(path, len));
            if (_big.Count > BigCount * 2)
            {
                _big.Sort((a, b) => b.Size.CompareTo(a.Size));
                _big.RemoveRange(BigCount, _big.Count - BigCount);
                Interlocked.Exchange(ref _bigFloor, _big[^1].Size);
            }
        }
    }
}
