using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("pipewriter", port);

Socket listener = PlaygroundContext.CreateListener(port);

while (true)
{
    Socket client = await listener.AcceptAsync();
    client.NoDelay = true;
    _ = HandleAsync(client);
}

async Task HandleAsync(Socket client)
{
    byte[] buf  = new byte[8192];
    byte[] body = new byte[ctx.ChunkBytes];
    await using var stream = new NetworkStream(client, ownsSocket: true);
    PipeWriter writer = PipeWriter.Create(stream);
    try
    {
        while (true)
        {
            int n = await stream.ReadAsync(buf);
            if (n <= 0)
            {
                return;
            }

            string url = Http.ParseUrl(buf.AsSpan(0, n));
            if (!ctx.TryRoute(url, out Cache? cache, out string key, out bool useFs))
            {
                writer.Write(ctx.NotFound);
                await writer.FlushAsync();
                continue;
            }

            if (useFs)
            {
                if (ctx.Random.TryGet(key, out Entry? entry))
                {
                    // Write headers + all body chunks into the PipeWriter buffer; single FlushAsync at end.
                    writer.Write(ctx.HeadersFor(entry));
                    await using var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    int read;
                    while ((read = fs.Read(body, 0, body.Length)) > 0)
                    {
                        writer.Write(body.AsSpan(0, read));
                    }
                    await writer.FlushAsync();
                }
                else
                {
                    writer.Write(ctx.NotFound);
                    await writer.FlushAsync();
                }
            }
            else if (cache!.TryGet(key, out Entry? entry))
            {
                // Headers + body chunks written into the PipeWriter buffer, single FlushAsync at end.
                // Body bytes go directly into writer.GetMemory — no intermediate ArrayPool copy.
                // /sendfile/ will throw inside cache.Read (Sendfile bypasses userspace) — caught below.
                writer.Write(ctx.HeadersFor(entry));

                long offset    = 0;
                long remaining = entry.Size;
                while (remaining > 0)
                {
                    int          want = (int)Math.Min(remaining, ctx.ChunkBytes);
                    Memory<byte> mem  = writer.GetMemory(want);
                    int          r    = cache.Read(entry, mem.Span[..want], offset);
                    if (r <= 0)
                    {
                        break;
                    }
                    writer.Advance(r);
                    offset    += r;
                    remaining -= r;
                }
                await writer.FlushAsync();
            }
            else
            {
                writer.Write(ctx.NotFound);
                await writer.FlushAsync();
            }
        }
    }
    catch
    {
    }
    finally
    {
        await writer.CompleteAsync();
    }
}
