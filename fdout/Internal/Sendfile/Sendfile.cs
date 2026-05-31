using System.Runtime.InteropServices;
using static fdout.Native.Libc;

namespace fdout;

/// <summary>
/// Internal sendfile(2) dispatcher with short-write + EAGAIN handling.
/// </summary>
internal static unsafe class Sendfile
{
    public static void SendCore(int sockFd, int fileFd, long fileSize)
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
