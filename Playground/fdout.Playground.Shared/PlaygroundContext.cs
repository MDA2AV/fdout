using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace fdout.Playground.Shared;

/// <summary>
/// Which dispatch path a URL routes to. The playground framework picks one of these
/// per request; each variant (Socket / Stream / PipeWriter) decides what its handler
/// does for each Route. <see cref="Route.Sendfile"/> only works in the Socket variant —
/// the others 404 it.
/// </summary>
public enum Route
{
    NotFound,
    Random,
    IoUring,
    Sendfile,
    FileStream,
}

/// <summary>
/// Shared playground harness. Builds two fdout caches (RandomAccess + IoUring),
/// owns URL routing, the 404 byte block, and a lazy per-asset HTTP header cache.
/// The lib itself doesn't form headers — that's a framework concern, which lives
/// here.
/// </summary>
public sealed class PlaygroundContext : IDisposable
{
    public Cache Random  { get; }
    public Cache IoUring { get; }

    public byte[] NotFound   { get; }
    public int    ChunkBytes { get; }
    public string RootDir    { get; }
    public int    FileCount  => Random.Count;

    // Lazy per-asset header cache. First request per asset builds the byte[];
    // subsequent requests are a single Dictionary lookup. Caching is a framework
    // CHOICE — the lib has no opinion on it.
    private readonly ConcurrentDictionary<string, byte[]> _headers = new();

    public PlaygroundContext(string rootDir, int chunkBytes)
    {
        RootDir    = Path.GetFullPath(rootDir);
        ChunkBytes = chunkBytes;
        Random     = new RandomAccessCache(rootDir);
        IoUring    = new IoUringCache(rootDir);
        NotFound   = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"u8.ToArray();
    }

    /// <summary>Return the prebaked response header block for an entry (built on first ask).</summary>
    public byte[] HeadersFor(Entry entry)
    {
        return _headers.GetOrAdd(entry.Path, _ => ResponseHeaders.Build200(entry.Path, entry.Size));
    }

    /// <summary>
    /// Parse the standard playground CLI args: [rootDir] [port] [chunkBytes].
    /// Defaults: AppContext.BaseDirectory/wwwroot, 8080, 64 KB.
    /// </summary>
    public static PlaygroundContext FromArgs(string[] args, out int port)
    {
        string rootDir    = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "wwwroot");
                port      = args.Length > 1 ? int.Parse(args[1]) : 8080;
        int    chunkBytes = args.Length > 2 ? int.Parse(args[2]) : 64 * 1024;
        return new PlaygroundContext(rootDir, chunkBytes);
    }

    /// <summary>Standard loopback listener with ReuseAddress + 8192 backlog.</summary>
    public static Socket CreateListener(int port)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
        listener.Listen(8192);
        return listener;
    }

    /// <summary>
    /// Map a URL prefix to a route + the cache used for the asset lookup + the
    /// dictionary key. Returns <see cref="Route.NotFound"/> if no prefix matched.
    /// Note that <see cref="Route.Sendfile"/> and <see cref="Route.FileStream"/>
    /// use the Random cache only for the entry lookup; the actual delivery is
    /// done by the demo handler (libc sendfile / new FileStream).
    /// </summary>
    public Route TryRoute(string url, out Cache? cache, out string key)
    {
        cache = null;
        key   = "";

        if (url.Length <= 1)
        {
            return Route.NotFound;
        }

        char c = url[1];
        if (c == 'r' && url.StartsWith("/random/", StringComparison.Ordinal))
        {
            cache = Random;
            key   = url[7..];
            return Route.Random;
        }
        if (c == 's' && url.StartsWith("/sendfile/", StringComparison.Ordinal))
        {
            cache = Random;   // any cache works — both have the same entry set
            key   = url[9..];
            return Route.Sendfile;
        }
        if (c == 'i' && url.StartsWith("/io_uring/", StringComparison.Ordinal))
        {
            cache = IoUring;
            key   = url[9..];
            return Route.IoUring;
        }
        if (c == 'f' && url.StartsWith("/fs/", StringComparison.Ordinal))
        {
            cache = Random;   // any cache works — only entry.Path is consumed by the FS branch
            key   = url[3..];
            return Route.FileStream;
        }
        return Route.NotFound;
    }

    public void PrintBanner(string variant, int port, bool supportsSendfile)
    {
        Console.WriteLine($"[fdout.{variant}.playground] cached {FileCount} files under {RootDir}; listening on http://localhost:{port} (chunk size = {ChunkBytes} bytes)");
        Console.WriteLine($"[fdout.{variant}.playground] routes:  /random/<path>    → fdout RandomAccessCache.Read (pread + caller writes)");
        Console.WriteLine($"[fdout.{variant}.playground]          /io_uring/<path>  → fdout IoUringCache.Read      (io_uring READ + caller writes)");
        if (supportsSendfile)
        {
            Console.WriteLine($"[fdout.{variant}.playground]          /sendfile/<path>  → fdout.Sendfile.Send         (libc sendfile(2), kernel-only)");
        }
        else
        {
            Console.WriteLine($"[fdout.{variant}.playground]          /sendfile/<path>  → 404 (Socket variant only)");
        }
        Console.WriteLine($"[fdout.{variant}.playground]          /fs/<path>        → vanilla FileStream           (open/read/close per request)");
        Console.WriteLine($"[fdout.{variant}.playground]          anything else     → 404");
    }

    public void Dispose()
    {
        Random.Dispose();
        IoUring.Dispose();
    }
}
