namespace fdout;

/// <summary>
/// Read mechanism backing a <see cref="Cache"/>. Picked once at construction and
/// surfaced via <see cref="Cache.Mode"/> for inspection.
/// </summary>
public enum Mode
{
    /// <summary>pread(2) on a cached SafeFileHandle into a user-supplied buffer.</summary>
    RandomAccess,

    /// <summary>
    /// io_uring IORING_OP_READ on the cached fd into a user buffer. Submit one SQE,
    /// io_uring_enter, inline-completes on warm cache.
    /// </summary>
    IoUring,
}
