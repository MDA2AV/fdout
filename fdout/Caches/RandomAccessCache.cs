namespace fdout;

/// <summary>
/// pread(2) on a cached SafeFileHandle. Bytes pass through userspace; the caller
/// decides what to do with them. Synchronous.
/// </summary>
public sealed class RandomAccessCache : Cache
{
    public override Mode Mode => Mode.RandomAccess;

    public RandomAccessCache(string rootDir) : base(rootDir) { }

    public override int Read(Entry entry, Span<byte> dest, long offset = 0) 
        => RandomAccess.Read(entry.Handle, dest, offset);
}
