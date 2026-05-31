namespace fdout;

/// <summary>
/// Body-delivery mechanism. Picked once at Cache construction and used for every
/// subsequent <see cref="Cache.SendAsync(System.Net.Sockets.Socket, Entry)"/> or
/// <see cref="Cache.Read"/> call.
/// </summary>
public enum Mode
{
    /// <summary>
    /// pread(2) on a cached SafeFileHandle into a user-supplied buffer. Bytes pass
    /// through userspace, so this composes with SslStream and any other userspace
    /// stream wrapper. Synchronous.
    /// </summary>
    RandomAccess,

    /// <summary>
    /// libc sendfile(2) directly from the cached fd to the socket fd. Kernel-only;
    /// bytes never enter userspace. Plain TCP only — incompatible with SslStream
    /// (which has to see the plaintext to encrypt it). Synchronous.
    /// </summary>
    Sendfile,

    /// <summary>
    /// io_uring IORING_OP_READ on the cached fd into a user buffer. Submit one SQE,
    /// io_uring_enter, inline-completes on warm cache. Same userspace data path as
    /// RandomAccess; only the syscall mechanism differs. Composes with SslStream.
    /// </summary>
    IoUring,
}
