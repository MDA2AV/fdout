using Microsoft.Win32.SafeHandles;

namespace fdout;

/// <summary>
/// A pre-opened asset. Holds the cached SafeFileHandle, its raw fd, the file size,
/// and the absolute path. No HTTP awareness — response headers are the caller's
/// (framework's) responsibility. Shared across requests; thread-safe to use
/// concurrently (all reads are positional and the handle isn't mutated).
/// </summary>
public sealed class Entry : IDisposable
{
    private int _disposed;

    internal SafeFileHandle Handle { get; }

    /// <summary>
    /// Raw OS file descriptor — required by sendfile and io_uring dispatch.
    /// </summary>
    public int Fd { get; }

    /// <summary>
    /// File size in bytes captured at open time.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Absolute path the file was opened from.
    /// </summary>
    public string Path { get; }

    internal Entry(string path)
    {
        Path   = System.IO.Path.GetFullPath(path);
        Handle = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Fd     = (int)Handle.DangerousGetHandle();
        Size   = RandomAccess.GetLength(Handle);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Handle.Dispose();
        }
    }
}
