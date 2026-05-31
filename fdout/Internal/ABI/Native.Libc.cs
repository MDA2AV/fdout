using System.Runtime.InteropServices;

namespace fdout;

internal static partial class Native
{
    /// <summary>
    /// libc P/Invokes and Linux POSIX constants used by the lib: sendfile, poll,
    /// mmap, munmap, close. Lives under <c>Native.Libc</c> for grouping; access
    /// via <c>using static fdout.Native.Libc;</c> in implementation files.
    /// </summary>
    internal static unsafe class Libc
    {
        public const int   EAGAIN  = 11;
        public const int   EINTR   = 4;
        public const short POLLOUT = 0x0004;

        public const int PROT_READ    = 1;
        public const int PROT_WRITE   = 2;
        public const int MAP_SHARED   = 1;
        public const int MAP_POPULATE = 0x8000;

        [DllImport("libc", SetLastError = true)]
        public static extern long sendfile(int out_fd, int in_fd, long* offset, nuint count);

        [DllImport("libc", SetLastError = true)]
        public static extern int poll(pollfd* fds, nuint nfds, int timeout);

        [DllImport("libc", SetLastError = true)]
        public static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

        [DllImport("libc")]
        public static extern int munmap(void* addr, nuint length);

        [DllImport("libc")]
        public static extern int close(int fd);

        [StructLayout(LayoutKind.Sequential)]
        public struct pollfd
        {
            public int   fd;
            public short events;
            public short revents;
        }
    }
}
