using System.Text;

namespace fdout.Playground.Shared;

/// <summary>Minimal HTTP/1.1 request-line parsing helpers for the playground demos.</summary>
public static class Http
{
    /// <summary>
    /// Extract the URL from the request line (e.g. "GET /index.html HTTP/1.1\r\n..." → "/index.html").
    /// Returns empty string on malformed input.
    /// </summary>
    public static string ParseUrl(ReadOnlySpan<byte> req)
    {
        int sp1 = req.IndexOf((byte)' ');
        if (sp1 < 0)
        {
            return "";
        }

        var rest = req[(sp1 + 1)..];
        int sp2 = rest.IndexOf((byte)' ');
        if (sp2 < 0)
        {
            return "";
        }

        return Encoding.ASCII.GetString(rest[..sp2]);
    }
}
