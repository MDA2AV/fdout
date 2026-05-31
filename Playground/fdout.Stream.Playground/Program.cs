using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("stream", port, supportsSendfile: false);

Socket listener = PlaygroundContext.CreateListener(port);

while (true)
{
    Socket client = await listener.AcceptAsync();
    client.NoDelay = true;
    _ = HandleAsync(client);
}

async Task
    HandleAsync(Socket client)
{
    byte[] buf  = new byte[8192];
    byte[] body = new byte[ctx.ChunkBytes];
    await using var stream = new NetworkStream(client, ownsSocket: true);
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

            // Stream variant can't sendfile — needs a raw socket fd.
            if (route == Route.NotFound || route == Route.Sendfile || !cache!.TryGet(key, out Entry? entry))
            {
                await stream.WriteAsync(ctx.NotFound);
                continue;
            }

            byte[] headers = ctx.HeadersFor(entry);

            switch (route)
            {
                case Route.Random:
                case Route.IoUring:
                {
                    await stream.WriteAsync(headers);
                    long offset    = 0;
                    long remaining = entry.Size;
                    while (remaining > 0)
                    {
                        int want = (int)Math.Min(remaining, ctx.ChunkBytes);
                        int got  = cache.Read(entry, body.AsSpan(0, want), offset);
                        if (got <= 0)
                        {
                            break;
                        }
                        await stream.WriteAsync(body.AsMemory(0, got));
                        offset    += got;
                        remaining -= got;
                    }
                    break;
                }

                case Route.FileStream:
                {
                    await stream.WriteAsync(headers);
                    await using var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    int read;
                    while ((read = fs.Read(body, 0, body.Length)) > 0)
                    {
                        await stream.WriteAsync(body.AsMemory(0, read));
                    }
                    break;
                }
            }
        }
    }
    catch
    {
    }
}
