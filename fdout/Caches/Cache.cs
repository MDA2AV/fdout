using System.Diagnostics.CodeAnalysis;

namespace fdout;

/// <summary>
/// Abstract base class for all fdout caches. Owns the directory scan, the
/// <see cref="Entry"/> dictionary, and the dispose lifecycle. Concrete subclasses
/// (<see cref="RandomAccessCache"/>, <see cref="IoUringCache"/>) override
/// <see cref="Read"/> with their syscall-specific mechanism.
/// </summary>
/// <remarks>
/// The cache does NOT own any I/O sink. It exposes file metadata via
/// <see cref="TryGet"/> and bytes via <see cref="Read"/>; callers send those bytes
/// to their own Socket, Stream, PipeWriter, etc. — fdout has no opinion on the wire.
/// </remarks>
public abstract class Cache : ICache
{
    private readonly Dictionary<string, Entry> _entries;
    private int _disposed;

    public abstract Mode Mode { get; }
    public string RootDir { get; }
    public int Count => _entries.Count;

    protected Cache(string rootDir) {
        RootDir  = Path.GetFullPath(rootDir);
        _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        if (!Directory.Exists(RootDir)) {
            throw new DirectoryNotFoundException(RootDir);
        }

        foreach (string path in Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories)) {
            var    entry = new Entry(path);
            string url   = "/" + Path.GetRelativePath(RootDir, path).Replace('\\', '/');
            _entries[url] = entry;
        }
    }

    /// <summary>
    /// Convenience factory — picks the right concrete cache by enum.
    /// Equivalent to <c>new RandomAccessCache(...)</c> / <c>new IoUringCache(...)</c>.
    /// </summary>
    public static Cache Create(string rootDir, Mode mode) {
        return mode switch {
            Mode.RandomAccess => new RandomAccessCache(rootDir),
            Mode.IoUring      => new IoUringCache(rootDir),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public bool TryGet(string url, [MaybeNullWhen(false)] out Entry entry) {
        return _entries.TryGetValue(url, out entry);
    }

    public abstract int Read(Entry entry, Span<byte> dest, long offset = 0);

    public virtual void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        foreach (var e in _entries.Values) {
            e.Dispose();
        }
        _entries.Clear();
    }
}
