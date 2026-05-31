# fdout

A small Linux-targeted .NET library for serving file bytes from disk into whatever sink a caller already has — a raw socket, a NetworkStream, a PipeWriter, an SslStream, anything that accepts bytes.

The intended use case is static-asset serving: a web framework wants to push 10KB-to-100MB files out to clients with the lowest reasonable overhead. fdout focuses on the disk side of that — caching file descriptors at startup, reading bytes via the right Linux syscall — and stays out of the wire side. The caller writes the bytes wherever it wants.

## What it does

Given a directory, `Cache` walks it once at construction and opens every file with `File.OpenHandle`. Each `Entry` carries the file's raw fd, its size, and its absolute path, kept open for the cache's lifetime. Per request, the framework looks up an entry by URL key and calls `Read` to pull a chunk of bytes into a span the framework owns. That's the entire surface for the two main caches.

Two concrete cache types use different underlying read mechanisms:

`RandomAccessCache` uses `System.IO.RandomAccess.Read`, which under the hood is a `pread(2)` syscall. Synchronous, single fd, single read per call. This is the default and the one that composes cleanly with TLS, since the bytes pass through userspace where SslStream can encrypt them.

`IoUringCache` submits one `IORING_OP_READ` SQE per read on a pooled set of rings, calls `io_uring_enter`, and reaps the CQE inline. For warm-cache reads it completes without leaving the syscall, much like pread does. The API is identical to RandomAccess — the difference is the syscall and the ABI underneath.

Then there's `Sendfile.Send`, which is not a cache at all. It's a free-standing static helper that calls `sendfile(2)` directly with a socket fd, a file fd, and a size, and handles short writes and EAGAIN with a poll-retry loop. It exists because sendfile cannot return bytes to userspace — its entire purpose is to copy from the page cache to the socket buffer without the CPU touching the data. So instead of pretending to fit the cache contract, it lives on the side and gets called explicitly when zero-copy is what you want.

## Using it

For the read-into-buffer modes (RandomAccess and IoUring) the shape is the same: look up the entry, read chunks into your own buffer, write the buffer to whatever sink you have. Sendfile is different — it pushes bytes from the kernel to a socket without any userspace buffer in between.

<details>
<summary><b>RandomAccess</b> — pread into a buffer, write to a Stream</summary>

```csharp
using System.Buffers;
using fdout;

using var cache = new RandomAccessCache("/var/www");

// per request:
//   `stream` is the caller's Stream, `url` is the lookup key
if (cache.TryGet(url, out var entry))
{
    byte[] buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
    try
    {
        long offset = 0;
        while (offset < entry.Size)
        {
            int want = (int)Math.Min(buf.Length, entry.Size - offset);
            int got  = cache.Read(entry, buf.AsSpan(0, want), offset);
            if (got <= 0) break;
            await stream.WriteAsync(buf.AsMemory(0, got));
            offset += got;
        }
    }
    finally { ArrayPool<byte>.Shared.Return(buf); }
}
```

</details>

<details>
<summary><b>IoUring</b> — io_uring IORING_OP_READ into a buffer, write to a Stream</summary>

```csharp
using System.Buffers;
using fdout;

using var cache = new IoUringCache("/var/www");

// identical shape to RandomAccess — only the cache type differs.
// The cache submits one SQE per Read call and waits for the CQE inline.
if (cache.TryGet(url, out var entry))
{
    byte[] buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
    try
    {
        long offset = 0;
        while (offset < entry.Size)
        {
            int want = (int)Math.Min(buf.Length, entry.Size - offset);
            int got  = cache.Read(entry, buf.AsSpan(0, want), offset);
            if (got <= 0) break;
            await stream.WriteAsync(buf.AsMemory(0, got));
            offset += got;
        }
    }
    finally { ArrayPool<byte>.Shared.Return(buf); }
}
```

</details>

<details>
<summary><b>Sendfile</b> — kernel pushes bytes straight to a socket, no userspace buffer</summary>

```csharp
using fdout;

// any cache works for the entry lookup — only the fd and size are consumed
using var cache = new RandomAccessCache("/var/www");

// per request:
//   `socket` is a System.Net.Sockets.Socket, `url` is the lookup key
if (cache.TryGet(url, out var entry))
{
    Sendfile.Send((int)socket.Handle, entry.Fd, entry.Size);
}
```

`Sendfile.Send` takes a socket fd, a file fd, and a size. It blocks the calling thread until the file is fully written to the socket (or the connection drops), handling short writes and `EAGAIN` internally via `poll(POLLOUT)`. This is the one fdout API that touches a socket directly — it has to, because sendfile cannot return bytes to userspace by design.

</details>

The read mechanism and the sink are independent: swap `RandomAccessCache` for `IoUringCache` to change how reads happen, swap `stream.WriteAsync` for `socket.SendAsync` or `writer.WriteAsync` to change where bytes go. For PipeWriter you can skip the ArrayPool entirely and read directly into `writer.GetMemory(want)`.

## What it does not do

It does not form HTTP responses. It does not cache headers. It does not own a Socket or Stream or PipeWriter. It has no opinion about MIME types, ETags, content negotiation, range requests, conditional GETs, or anything else that belongs to whatever HTTP framework is calling into it. The library's only outputs are the cached file metadata and the bytes themselves.

This is deliberate. fdout is a building block; the framework on top decides what an HTTP response looks like, which fields it caches, how it gathers headers with the first body chunk into a single sendmsg, whether it cares about TLS, and so on.

## Layout

The repository has the library under `fdout/`, with the public types at the root of that folder and an `Internal/` subtree organized by feature: `ABI/` holds the libc and io_uring P/Invoke surface as a partial `Native` class split into nested `Native.Libc` and `Native.IoUring` types; `IoUring/` has the Ring, RingPool, and UringReader internals; the Sendfile loop sits next to the public `Sendfile.Send` wrapper at the lib root because it has no internal-only parts worth hiding.

Under `Playground/` are three sample applications that exercise the library from three different transport positions: raw Socket, NetworkStream, and PipeWriter. They share routing and asset-lookup code via `fdout.Playground.Shared`. Each demo handles the same four URL prefixes (`/random/`, `/io_uring/`, `/sendfile/`, `/fs/`) where the prefix selects the read mechanism, and the demo's handler does the writing in whatever way is natural for its sink. The `/sendfile/` route is Socket-only since sendfile requires a raw fd; the Stream and PipeWriter variants return 404 for it. The `/fs/` route is included as a baseline — it opens a `FileStream` per request the way most .NET code naturally would, and bypasses fdout entirely.
