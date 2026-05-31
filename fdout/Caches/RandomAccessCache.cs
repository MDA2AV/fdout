namespace fdout;

/// <summary>
/// pread(2) on a cached SafeFileHandle. Bytes pass through userspace, so this
/// composes with SslStream and any userspace stream wrapper. Synchronous reads.
/// </summary>
public sealed class RandomAccessCache : Cache
{
    public override Mode Mode => Mode.RandomAccess;

    public RandomAccessCache(string rootDir, int chunkBytes = DefaultChunkBytes)
        : base(rootDir, chunkBytes)
    {
    }

    public override int Read(Entry entry, Span<byte> dest, long offset = 0)
    {
        return RandomAccess.Read(entry.Handle, dest, offset);
    }
}
