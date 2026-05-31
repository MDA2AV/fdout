using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using fdout;
using fdout.Playground.Shared;

using PlaygroundContext ctx = PlaygroundContext.FromArgs(args, out int port);
ctx.PrintBanner("socket", port, supportsSendfile: true);

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

            string url   = Http.ParseUrl(buf.AsSpan(0, n));
            Route  route = ctx.TryRoute(url, out Cache? cache, out string key);

            if (route == Route.NotFound || !cache!.TryGet(key, out Entry? entry))
            {
                await client.SendAsync(ctx.NotFound.AsMemory(), SocketFlags.None);
                continue;
            }

            byte[] headers = ctx.HeadersFor(entry);

            switch (route)
            {
                case Route.Sendfile:
                    // sendfile branch — writes directly to the socket fd in the kernel.
                    // Headers go out first (1 syscall), then sendfile pushes the body bytes.
                    await client.SendAsync(headers.AsMemory(), SocketFlags.None);
                    Sendfile.Send((int)client.Handle, entry.Fd, entry.Size);
                    break;

                case Route.Random:
                case Route.IoUring:
                {
                    // Read first chunk, then gather-send (headers + chunk) in one sendmsg.
                    int firstWant = (int)Math.Min(entry.Size, ctx.ChunkBytes);
                    int firstRead = cache.Read(entry, body.AsSpan(0, firstWant), 0);

                    var segs = new ArraySegment<byte>[]
                    {
                        new(headers),
                        new(body, 0, firstRead),
                    };
                    await client.SendAsync(segs, SocketFlags.None);

                    long offset    = firstRead;
                    long remaining = entry.Size - firstRead;
                    while (remaining > 0)
                    {
                        int want = (int)Math.Min(remaining, ctx.ChunkBytes);
                        int got  = cache.Read(entry, body.AsSpan(0, want), offset);
                        if (got <= 0)
                        {
                            break;
                        }
                        await client.SendAsync(body.AsMemory(0, got), SocketFlags.None);
                        offset    += got;
                        remaining -= got;
                    }
                    break;
                }

                case Route.FileStream:
                {
                    // Per-request open + chunked read + gather headers with first chunk.
                    await using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                    var  segs      = new ArraySegment<byte>[2];
                    bool firstSend = true;
                    int  read;
                    while ((read = stream.Read(body, 0, body.Length)) > 0)
                    {
                        if (firstSend)
                        {
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
                        await client.SendAsync(headers.AsMemory(), SocketFlags.None);
                    }
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
        client.Dispose();
    }
}
