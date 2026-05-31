namespace fdout;

/// <summary>
/// io_uring IORING_OP_READ on a cached fd. Submit one SQE per read, io_uring_enter,
/// inline-completes on warm cache. Bytes pass through userspace (same shape as
/// <see cref="RandomAccessCache"/>); only the syscall mechanism differs.
/// </summary>
public sealed class IoUringCache : Cache
{
    // Lazy so the rings (one per CPU, depth=32) only spin up on first read.
    private readonly Lazy<UringReader> _uring = new(() => new UringReader());
    private          int               _disposed;

    public override Mode Mode => Mode.IoUring;

    public IoUringCache(string rootDir) : base(rootDir) { }

    public override int Read(Entry entry, Span<byte> dest, long offset = 0) {
        return _uring.Value.Read(entry.Fd, dest, offset);
    }

    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) {
            if (_uring.IsValueCreated) {
                _uring.Value.Dispose();
            }
        }
        base.Dispose();
    }
}
