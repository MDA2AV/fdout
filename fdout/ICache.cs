using System.Diagnostics.CodeAnalysis;

namespace fdout;

/// <summary>
/// Contract for a directory-scoped static-file cache. Implementations differ in how
/// they read file bytes (pread / io_uring). The cache does NOT own any I/O sink —
/// it only exposes file metadata + bytes-into-buffer reads. The caller is responsible
/// for writing the bytes to whatever sink they have (socket, Stream, PipeWriter, etc.).
/// </summary>
public interface ICache : IDisposable
{
    /// <summary>Tags which underlying read mechanism the implementation uses.</summary>
    Mode Mode { get; }

    /// <summary>Absolute root directory the cache was built over.</summary>
    string RootDir { get; }

    /// <summary>Number of pre-opened files in the cache.</summary>
    int Count { get; }

    /// <summary>
    /// Look up a pre-opened entry by URL path. <paramref name="entry"/> is non-null
    /// when the method returns true.
    /// </summary>
    bool TryGet(string url, [MaybeNullWhen(false)] out Entry entry);

    /// <summary>
    /// Read up to <paramref name="dest"/>.Length bytes from <paramref name="offset"/>
    /// into <paramref name="dest"/>. Returns bytes actually read (may be less than
    /// requested near EOF). Caller loops for files larger than a single buffer.
    /// </summary>
    int Read(Entry entry, Span<byte> dest, long offset = 0);
}
