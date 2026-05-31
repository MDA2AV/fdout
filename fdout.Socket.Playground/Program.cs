using System.Buffers;
using System.Net.Sockets;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("socket", port);

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
    try
    {
        while (true)
        {
            int n = await client.ReceiveAsync(buf, SocketFlags.None);
            if (n <= 0)
            {
                return;
            }

            string url = Http.ParseUrl(buf.AsSpan(0, n));
            if (!ctx.TryRoute(url, out Cache? cache, out string key, out bool useFs))
            {
                await client.SendAsync(ctx.NotFound.AsMemory(), SocketFlags.None);
                continue;
            }

            if (useFs)
            {
                if (ctx.Random.TryGet(key, out Entry? entry))
                {
                    byte[] headers = ctx.HeadersFor(entry);
                    await using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    var  segs      = new ArraySegment<byte>[2];
                    bool firstSend = true;
                    int  read;
                    while ((read = stream.Read(body, 0, body.Length)) > 0)
                    {
                        if (firstSend)
                        {
                            // Gather headers + first chunk in one sendmsg(iovec).
                            segs[0] = new ArraySegment<byte>(headers);
                            segs[1] = new ArraySegment<byte>(body, 0, read);
                            await client.SendAsync(segs, SocketFlags.None);
                            firstSend = false;
                        }
                        else
                        {
                            await client.SendAsync(body.AsMemory(0, read), SocketFlags.None);
                        }
                    }
                    if (firstSend)
                    {
                        // Zero-byte file: still need to send headers.
                        await client.SendAsync(headers.AsMemory(), SocketFlags.None);
                    }
                }
                else
                {
                    await client.SendAsync(ctx.NotFound.AsMemory(), SocketFlags.None);
                }
            }
            else if (cache!.TryGet(key, out Entry? entry))
            {
                byte[] headers = ctx.HeadersFor(entry);

                if (cache.Mode == Mode.Sendfile)
                {
                    // Sendfile can't gather with userspace bytes — two syscalls.
                    await client.SendAsync(headers.AsMemory(), SocketFlags.None);
                    await cache.SendAsync(client, entry);
                }
                else
                {
                    // RandomAccess / IoUring: gather headers + first chunk into one sendmsg.
                    byte[] rent = ArrayPool<byte>.Shared.Rent(ctx.ChunkBytes);
                    try
                    {
                        int firstWant = (int)Math.Min(entry.Size, ctx.ChunkBytes);
                        int firstRead = cache.Read(entry, rent.AsSpan(0, firstWant), 0);

                        var segs = new ArraySegment<byte>[]
                        {
                            new(headers),
                            new(rent, 0, firstRead),
                        };
                        await client.SendAsync(segs, SocketFlags.None);

                        long offset    = firstRead;
                        long remaining = entry.Size - firstRead;
                        while (remaining > 0)
                        {
                            int want = (int)Math.Min(remaining, ctx.ChunkBytes);
                            int got  = cache.Read(entry, rent.AsSpan(0, want), offset);
                            if (got <= 0)
                            {
                                break;
                            }
                            await client.SendAsync(rent.AsMemory(0, got), SocketFlags.None);
                            offset    += got;
                            remaining -= got;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rent);
                    }
                }
            }
            else
            {
                await client.SendAsync(ctx.NotFound.AsMemory(), SocketFlags.None);
            }
        }
    }
    catch
    {
    }
    finally
    {
        client.Dispose();
    }
}
