using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace fdout.Playground.Shared;

/// <summary>
/// Shared playground harness. Builds three Cache instances (one per mode),
/// owns the URL routing logic, the 404 bytes, and a lazy per-asset HTTP response-
/// header cache. (The lib itself is HTTP-agnostic — header construction lives here,
/// in the "framework" half of the demo.) Each playground variant (Socket / Stream /
/// PipeWriter) wires its variant-specific send pipeline on top.
/// </summary>
public sealed class PlaygroundContext : IDisposable
{
    public Cache Random   { get; }
    public Cache Sendfile { get; }
    public Cache IoUring  { get; }

    public byte[] NotFound   { get; }
    public int ChunkBytes { get; }
    public string RootDir    { get; }
    public int FileCount  => Random.Count;

    // Lazy per-asset header cache. Built on first request per (path, size); subsequent
    // requests are a single Dictionary lookup. Headers being cached is a framework
    // CHOICE — the lib has no opinion on it.
    private readonly ConcurrentDictionary<string, byte[]> _headers = new();

    public PlaygroundContext(string rootDir, int chunkBytes)
    {
        RootDir    = Path.GetFullPath(rootDir);
        ChunkBytes = chunkBytes;
        Random     = new RandomAccessCache(rootDir, chunkBytes);
        Sendfile   = new SendfileCache(rootDir,     chunkBytes);
        IoUring    = new IoUringCache(rootDir,      chunkBytes);
        NotFound   = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"u8.ToArray();
    }

    /// <summary>Return the prebaked response header block for an entry (built on first ask).</summary>
    public byte[] HeadersFor(Entry entry)
    {
        return _headers.GetOrAdd(entry.Path, _ => ResponseHeaders.Build200(entry.Path, entry.Size));
    }

    /// <summary>
    /// Parse the standard playground CLI args: [rootDir] [port] [chunkBytes].
    /// Defaults: AppContext.BaseDirectory/wwwroot, 8080, Cache.DefaultChunkBytes.
    /// </summary>
    public static PlaygroundContext FromArgs(string[] args, out int port)
    {
        string rootDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "wwwroot");
        port = args.Length > 1 ? int.Parse(args[1]) : 8080;
        int chunkBytes = args.Length > 2 ? int.Parse(args[2]) : Cache.DefaultChunkBytes;
        
        return new PlaygroundContext(rootDir, chunkBytes);
    }

    /// <summary>Standard loopback listener with ReuseAddress + a 8192 backlog.</summary>
    public static Socket CreateListener(int port)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
        listener.Listen(8192);
        return listener;
    }

    /// <summary>
    /// Map a URL prefix to a cache + dictionary key. Returns false if no prefix matched
    /// (caller should send <see cref="NotFound"/>).
    /// </summary>
    public bool TryRoute(string url, out Cache? cache, out string key, out bool useFs)
    {
        cache = null;
        key   = "";
        useFs = false;

        if (url.Length <= 1)
        {
            return false;
        }

        char c = url[1];
        if (c == 'r' && url.StartsWith("/random/", StringComparison.Ordinal))
        {
            cache = Random;
            key   = url[7..];
            return true;
        }
        if (c == 's' && url.StartsWith("/sendfile/", StringComparison.Ordinal))
        {
            cache = Sendfile;
            key   = url[9..];
            return true;
        }
        if (c == 'i' && url.StartsWith("/io_uring/", StringComparison.Ordinal))
        {
            cache = IoUring;
            key   = url[9..];
            return true;
        }
        if (c == 'f' && url.StartsWith("/fs/", StringComparison.Ordinal))
        {
            useFs = true;
            key   = url[3..];
            return true;
        }
        return false;
    }

    public void PrintBanner(string variant, int port)
    {
        Console.WriteLine($"[fdout.{variant}.playground] cached {FileCount} files under {RootDir}; listening on http://localhost:{port} (chunk size = {ChunkBytes} bytes)");
        Console.WriteLine($"[fdout.{variant}.playground] routes:  /random/<path>    → fdout RandomAccess (pread on cached fd)");
        Console.WriteLine($"[fdout.{variant}.playground]          /sendfile/<path>  → fdout Sendfile     (libc sendfile(2), kernel-only — Socket variant only)");
        Console.WriteLine($"[fdout.{variant}.playground]          /io_uring/<path>  → fdout IoUring      (io_uring IORING_OP_READ)");
        Console.WriteLine($"[fdout.{variant}.playground]          /fs/<path>        → FileStream           (open/read/close per request, NOT via fdout)");
        Console.WriteLine($"[fdout.{variant}.playground]          anything else     → 404");
    }

    public void Dispose()
    {
        Random.Dispose();
        Sendfile.Dispose();
        IoUring.Dispose();
    }
}
