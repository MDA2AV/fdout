using System.Runtime.InteropServices;
using static fdout.Native.Libc;

namespace fdout;

/// <summary>
/// Public helper for libc <c>sendfile(2)</c>. This isn't part of the cache contract —
/// it's a thin syscall wrapper that takes a socket fd and a file fd, with EAGAIN
/// poll-retry handling. Use it directly when you want the zero-copy file→socket path:
/// look up an <see cref="Entry"/> via <see cref="Cache.TryGet"/>, then call
/// <c>Sendfile.Send(socketFd, entry.Fd, entry.Size)</c>.
/// </summary>
/// <remarks>
/// Linux-only. Synchronous; blocks the calling thread until the full file is sent
/// or the socket can no longer accept data. Handles short writes and EAGAIN (poll
/// on POLLOUT, retry).
/// </remarks>
public static unsafe class Sendfile
{
    public static void Send(int sockFd, int fileFd, long fileSize)
    {
        long offset    = 0;
        long remaining = fileSize;

        while (remaining > 0)
        {
            long localOff = offset;
            long sent     = sendfile(sockFd, fileFd, &localOff, (nuint)remaining);
            offset = localOff;

            if (sent > 0)
            {
                remaining -= sent;
                continue;
            }

            if (sent == 0)
            {
                return;
            }

            int err = Marshal.GetLastPInvokeError();
            if (err == EAGAIN || err == EINTR)
            {
                pollfd pfd = new() { fd = sockFd, events = POLLOUT };
                poll(&pfd, 1, -1);
                continue;
            }

            return;
        }
    }
}
