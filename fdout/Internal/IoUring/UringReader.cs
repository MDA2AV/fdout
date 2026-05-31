namespace fdout;

/// <summary>
/// Internal io_uring file reader. Rents a Ring per call, submits one IORING_OP_READ
/// SQE, waits for the CQE inline. Disposed when the parent fdout is disposed.
/// </summary>
internal sealed unsafe class UringReader : IDisposable
{
    private readonly RingPool _pool;

    public UringReader(int rings = 0, uint depth = 32)
    {
        _pool = new RingPool(rings, depth);
    }

    public int Read(int fd, Span<byte> buffer, long offset)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        Ring ring = _pool.Rent();
        try
        {
            fixed (byte* p = buffer)
            {
                int res = ring.SubmitRead(fd, p, (uint)buffer.Length, offset);
                if (res < 0)
                {
                    throw new IOException($"io_uring read failed: errno={-res}");
                }
                return res;
            }
        }
        finally
        {
            _pool.Return(ring);
        }
    }

    public void Dispose()
    {
        _pool.Dispose();
    }
}
