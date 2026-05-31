using System.Net.Sockets;

namespace fdout;

/// <summary>
/// libc sendfile(2) from a cached file fd to the socket fd. Kernel-only;
/// bytes never enter userspace. Plain TCP only — incompatible with SslStream.
/// </summary>
public sealed class SendfileCache : Cache
{
    public override Mode Mode => Mode.Sendfile;

    public SendfileCache(string rootDir, int chunkBytes = DefaultChunkBytes)
        : base(rootDir, chunkBytes)
    {
    }

    public override int Read(Entry entry, Span<byte> dest, long offset = 0)
    {
        throw new NotSupportedException(
            "SendfileCache delivers bytes kernel-only — they never enter userspace. Use RandomAccessCache or IoUringCache if you need direct reads.");
    }

    public override ValueTask SendAsync(Socket socket, Entry entry)
    {
        Sendfile.SendCore((int)socket.Handle, entry.Fd, entry.Size);
        return default;
    }

    public override ValueTask SendAsync(Stream stream, Entry entry)
    {
        throw new NotSupportedException(
            "sendfile(2) requires a raw socket fd; it can't write to a Stream. Use RandomAccessCache or IoUringCache for Stream sinks (e.g. SslStream).");
    }
}
