using System.Text;

namespace fdout.Playground.Shared;

/// <summary>
/// Forms HTTP/1.1 response header blocks. This lives in the playground (the
/// "framework"), NOT in the fdout library — fdout only knows about file
/// bytes, never HTTP semantics.
/// </summary>
public static class ResponseHeaders
{
    /// <summary>
    /// Build a <c>HTTP/1.1 200 OK</c> response header block for an asset of the
    /// given path + content length. Terminated with CRLFCRLF.
    /// </summary>
    public static byte[] Build200(string path, long contentLength)
    {
        string mime = Mime(System.IO.Path.GetExtension(path));
        string s    = "HTTP/1.1 200 OK\r\nContent-Type: " + mime
                    + "\r\nContent-Length: " + contentLength
                    + "\r\nConnection: keep-alive\r\n\r\n";
        return Encoding.ASCII.GetBytes(s);
    }

    private static string Mime(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css"            => "text/css; charset=utf-8",
            ".js"             => "application/javascript; charset=utf-8",
            ".json"           => "application/json; charset=utf-8",
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg"            => "image/svg+xml",
            _                 => "application/octet-stream",
        };
    }
}
