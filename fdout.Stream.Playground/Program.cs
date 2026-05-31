using System.Net.Sockets;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("stream", port);

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
                await stream.WriteAsync(ctx.NotFound);
                continue;
            }

            if (useFs)
            {
                if (ctx.Random.TryGet(key, out Entry? entry))
                {
                    await stream.WriteAsync(ctx.HeadersFor(entry));
                    await using var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    int read;
                    while ((read = fs.Read(body, 0, body.Length)) > 0)
                    {
                        await stream.WriteAsync(body.AsMemory(0, read));
                    }
                }
                else
                {
                    await stream.WriteAsync(ctx.NotFound);
                }
            }
            else if (cache!.TryGet(key, out Entry? entry))
            {
                // /sendfile/ will throw inside cache.SendAsync (sendfile needs a Socket fd, not a Stream).
                // The catch block below handles it — connection drops.
                await stream.WriteAsync(ctx.HeadersFor(entry));
                await cache.SendAsync(stream, entry);
            }
            else
            {
                await stream.WriteAsync(ctx.NotFound);
            }
        }
    }
    catch
    {
    }
}
