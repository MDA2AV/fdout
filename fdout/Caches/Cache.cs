using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace fdout;

/// <summary>
/// Abstract base class for all fdout caches. Owns the directory scan, the
/// <see cref="Entry"/> dictionary, the chunk-size knob, and the chunked
/// send loop (Template Method — concretes override <see cref="Read"/>; the base's
/// <see cref="SendAsync(Socket, Entry)"/> drives the loop). Concrete
/// subclasses are <see cref="RandomAccessCache"/>, <see cref="SendfileCache"/>,
/// and <see cref="IoUringCache"/>.
/// </summary>
/// <remarks>
/// HTTP-agnostic — the library does NOT form or cache response headers. Callers
/// (frameworks) build their own headers and ship them before/around the body.
/// </remarks>
public abstract class Cache : ICache
{
    /// <summary>
    /// Default chunk size used when none is provided to the constructor — 64 KB.
    /// </summary>
    public const int DefaultChunkBytes = 64 * 1024;

    private readonly Dictionary<string, Entry> _entries;
    private int _disposed;

    public abstract Mode Mode { get; }
    public string RootDir    { get; }
    public int Count      => _entries.Count;
    public int ChunkBytes { get; }

    protected Cache(string rootDir, int chunkBytes)
    {
        if (chunkBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes), "Chunk size must be positive.");
        }

        RootDir    = Path.GetFullPath(rootDir);
        ChunkBytes = chunkBytes;
        _entries   = new Dictionary<string, Entry>(StringComparer.Ordinal);

        if (!Directory.Exists(RootDir))
        {
            throw new DirectoryNotFoundException(RootDir);
        }

        foreach (string path in Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories))
        {
            var entry  = new Entry(path);
            string url = "/" + Path.GetRelativePath(RootDir, path).Replace('\\', '/');
            _entries[url] = entry;
        }
    }

    /// <summary>
    /// Convenience factory — picks the right concrete cache by enum.
    /// Equivalent to <c>new RandomAccessCache(...)</c> / <c>new SendfileCache(...)</c> / <c>new IoUringCache(...)</c>.
    /// </summary>
    public static Cache Create(string rootDir, Mode mode, int chunkBytes = DefaultChunkBytes)
    {
        return mode switch
        {
            Mode.RandomAccess => new RandomAccessCache(rootDir, chunkBytes),
            Mode.Sendfile     => new SendfileCache(rootDir, chunkBytes),
            Mode.IoUring      => new IoUringCache(rootDir, chunkBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public bool TryGet(string url, [MaybeNullWhen(false)] out Entry entry)
    {
        return _entries.TryGetValue(url, out entry);
    }

    public abstract int Read(Entry entry, Span<byte> dest, long offset = 0);

    /// <summary>
    /// Default chunked send loop: pread/io_uring read into an ArrayPool buffer, then
    /// Socket.SendAsync. <see cref="SendfileCache"/> overrides this to call sendfile(2)
    /// directly with no userspace buffer.
    /// </summary>
    public virtual ValueTask SendAsync(Socket socket, Entry entry)
    {
        return SendChunkedAsync(socket, entry);
    }

    /// <summary>
    /// Default chunked send loop: pread/io_uring read into an ArrayPool buffer, then
    /// Stream.WriteAsync. <see cref="SendfileCache"/> overrides this to throw —
    /// sendfile requires a raw socket fd.
    /// </summary>
    public virtual ValueTask SendAsync(Stream stream, Entry entry)
    {
        return SendChunkedAsync(stream, entry);
    }

    protected async ValueTask SendChunkedAsync(Socket socket, Entry entry)
    {
        int    chunk = ChunkBytes;
        byte[] buf   = ArrayPool<byte>.Shared.Rent(chunk);
        try
        {
            long offset    = 0;
            long remaining = entry.Size;

            while (remaining > 0)
            {
                int want = (int)Math.Min(remaining, chunk);
                int n    = Read(entry, buf.AsSpan(0, want), offset);
                if (n <= 0)
                {
                    return;
                }

                await socket.SendAsync(buf.AsMemory(0, n), SocketFlags.None);
                offset    += n;
                remaining -= n;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    protected async ValueTask SendChunkedAsync(Stream stream, Entry entry)
    {
        int    chunk = ChunkBytes;
        byte[] buf   = ArrayPool<byte>.Shared.Rent(chunk);
        try
        {
            long offset    = 0;
            long remaining = entry.Size;

            while (remaining > 0)
            {
                int want = (int)Math.Min(remaining, chunk);
                int n    = Read(entry, buf.AsSpan(0, want), offset);
                if (n <= 0)
                {
                    return;
                }

                await stream.WriteAsync(buf.AsMemory(0, n));
                offset    += n;
                remaining -= n;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var e in _entries.Values)
        {
            e.Dispose();
        }
        _entries.Clear();
    }
}
