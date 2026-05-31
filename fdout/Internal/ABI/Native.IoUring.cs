using System.Runtime.InteropServices;

namespace fdout;

internal static partial class Native
{
    /// <summary>
    /// io_uring syscall ABI: setup/enter syscall numbers, opcodes, flags, ring
    /// offsets, and the SQE/CQE struct layouts. The setup/enter wrappers go
    /// through libc's generic <c>syscall(2)</c> entry point.
    /// </summary>
    internal static unsafe class IoUring
    {
        private const long SYS_IO_URING_SETUP = 425;
        private const long SYS_IO_URING_ENTER = 426;

        public const byte IORING_OP_READ = 22;

        public const uint IORING_ENTER_GETEVENTS = 1u << 0;
        public const uint IORING_SETUP_ATTACH_WQ = 1u << 5;

        public const long IORING_OFF_SQ_RING = 0;
        public const long IORING_OFF_SQES    = 0x10000000;

        [DllImport("libc", EntryPoint = "syscall")]
        private static extern long syscall3(long nr, uint a1, IoUringParams* a2);

        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long syscall6(long nr, uint a1, uint a2, uint a3, uint a4, void* a5, nuint a6);

        public static int io_uring_setup(uint entries, IoUringParams* p)
        {
            return (int)syscall3(SYS_IO_URING_SETUP, entries, p);
        }

        public static int io_uring_enter(int fd, uint toSubmit, uint minComplete, uint flags)
        {
            long r = syscall6(SYS_IO_URING_ENTER, (uint)fd, toSubmit, minComplete, flags, null, 0);
            return r < 0 ? -Marshal.GetLastPInvokeError() : (int)r;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SqRingOffsets
        {
            public uint  head, tail, ring_mask, ring_entries, flags, dropped, array, resv1;
            public ulong resv2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CqRingOffsets
        {
            public uint  head, tail, ring_mask, ring_entries, overflow, cqes, flags, resv1;
            public ulong resv2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoUringParams
        {
            public uint          sq_entries, cq_entries, flags, sq_thread_cpu, sq_thread_idle;
            public uint          features, wq_fd, resv0, resv1, resv2;
            public SqRingOffsets sq_off;
            public CqRingOffsets cq_off;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct IoUringSqe
        {
            [FieldOffset(0)]  public byte   opcode;
            [FieldOffset(1)]  public byte   flags;
            [FieldOffset(2)]  public ushort ioprio;
            [FieldOffset(4)]  public int    fd;
            [FieldOffset(8)]  public ulong  off;
            [FieldOffset(16)] public ulong  addr;
            [FieldOffset(24)] public uint   len;
            [FieldOffset(28)] public uint   op_flags;
            [FieldOffset(32)] public ulong  user_data;
            [FieldOffset(40)] public ushort buf_index;
            [FieldOffset(42)] public ushort personality;
            [FieldOffset(44)] public int    splice_fd_in;
            [FieldOffset(48)] public ulong  addr3;
            [FieldOffset(56)] public ulong  __pad2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoUringCqe
        {
            public ulong user_data;
            public int   res;
            public uint  flags;
        }
    }
}
