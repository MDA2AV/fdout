using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace fdout;

/// <summary>
/// Contract for a directory-scoped static-file cache. Implementations differ in how
/// they deliver bytes (pread / sendfile / io_uring). All HTTP semantics — headers,
/// content-type, ETag — are the caller's responsibility.
/// </summary>
public interface ICache : IDisposable
{
    /// <summary>
    /// Tags which underlying mechanism the implementation uses.
    /// </summary>
    Mode Mode { get; }

    /// <summary>
    /// Absolute root directory the cache was built over.
    /// </summary>
    string RootDir { get; }

    /// <summary>
    /// Number of pre-opened files in the cache.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Chunk buffer size used by the streaming send loops (ignored by Sendfile).
    /// </summary>
    int ChunkBytes { get; }

    /// <summary>
    /// Look up a pre-opened entry by URL path.
    /// </summary>
    bool TryGet(string url, [MaybeNullWhen(false)] out Entry entry);

    /// <summary>
    /// Read up to <paramref name="dest"/>.Length bytes from <paramref name="offset"/>.
    /// Returns bytes read. NOT supported for Sendfile mode (bytes never enter userspace).
    /// </summary>
    int Read(Entry entry, Span<byte> dest, long offset = 0);

    /// <summary>
    /// Send the entry's body bytes to a socket using the implementation's mechanism.
    /// </summary>
    ValueTask SendAsync(Socket socket, Entry entry);

    /// <summary>
    /// Send the entry's body bytes to a generic Stream (e.g. SslStream). Throws for
    /// Sendfile-backed implementations — sendfile requires a raw socket fd.
    /// </summary>
    ValueTask SendAsync(Stream stream, Entry entry);
}
