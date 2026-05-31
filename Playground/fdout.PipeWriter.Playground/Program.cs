using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("pipewriter", port, supportsSendfile: false);

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

            string url   = Http.ParseUrl(buf.AsSpan(0, n));
            Route  route = ctx.TryRoute(url, out Cache? cache, out string key);

            // PipeWriter variant can't sendfile — needs a raw socket fd.
            if (route == Route.NotFound || route == Route.Sendfile || !cache!.TryGet(key, out Entry? entry))
            {
                writer.Write(ctx.NotFound);
                await writer.FlushAsync();
                continue;
            }

            byte[] headers = ctx.HeadersFor(entry);

            switch (route)
            {
                case Route.Random:
                case Route.IoUring:
                {
                    // Headers + every body chunk written into the PipeWriter buffer,
                    // single FlushAsync at end. Body bytes go directly into writer.GetMemory —
                    // no intermediate ArrayPool copy.
                    writer.Write(headers);

                    long offset    = 0;
                    long remaining = entry.Size;
                    while (remaining > 0)
                    {
                        int          want = (int)Math.Min(remaining, ctx.ChunkBytes);
                        Memory<byte> mem  = writer.GetMemory(want);
                        int          got  = cache.Read(entry, mem.Span[..want], offset);
                        if (got <= 0)
                        {
                            break;
                        }
                        writer.Advance(got);
                        offset    += got;
                        remaining -= got;
                    }
                    await writer.FlushAsync();
                    break;
                }

                case Route.FileStream:
                {
                    writer.Write(headers);
                    await using var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    int read;
                    while ((read = fs.Read(body, 0, body.Length)) > 0)
                    {
                        writer.Write(body.AsSpan(0, read));
                    }
                    await writer.FlushAsync();
                    break;
                }
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
