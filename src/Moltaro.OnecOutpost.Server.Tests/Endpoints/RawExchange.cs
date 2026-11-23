using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Server.Tests.Endpoints;

// HTTP/1.1 written by hand, one request on a connection of its own, for the checks of §9 of docs/protocol.md that the
// server makes before it reads a body. The header lines go exactly as the test gives them, a header repeated on two
// lines included, which HttpClient would join into one; the exchange adds Host and the framing of the body.
//
// - SendAsync declares the length of the body with Expect: 100-continue, and sends the body only when the server asks
//   for it with 100 Continue, which Kestrel sends when the application first reads the body. It waits for that as long
//   as the test waits, as a client whose Expect100ContinueTimeout is longer than the test's own wait would. So an
//   answer that comes with BodySent false was decided before the body was read, and no reset of the connection can
//   wipe it out.
// - SendWithoutExpectAsync declares the length and sends the body right after the head, as when a hop drops Expect.
// - SendChunkedAsync declares no length, as when a hop drops it, and sends the body in one chunk at once.
//
// Each waits no longer than the deadline of LoopbackServer.
internal static class RawExchange
{
    public static async Task<RawAnswer> SendAsync(
        Uri address,
        string request,
        IReadOnlyList<(string Name, string Value)> headers,
        byte[] body,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoopbackServer.Deadline);

        using TcpClient connection = new();
        await connection.ConnectAsync(address.Host, address.Port, deadline.Token);
        NetworkStream stream = connection.GetStream();

        List<(string Name, string Value)> head =
        [
            .. headers,
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
            ("Expect", "100-continue"),
        ];

        await WriteHeadAsync(stream, address, request, head, deadline.Token);

        (int status, List<(string Name, string Value)> answerHeaders) = await ReadHeadAsync(stream, deadline.Token);
        bool bodySent = false;

        if (status == 100)
        {
            await stream.WriteAsync(body, deadline.Token);
            bodySent = true;

            (status, answerHeaders) = await ReadHeadAsync(stream, deadline.Token);
        }

        byte[] answerBody = await ReadBodyAsync(stream, answerHeaders, deadline.Token);

        return new RawAnswer(status, Find(answerHeaders, "Outpost-Signature"), answerBody, bodySent);
    }

    public static async Task<RawAnswer> SendWithoutExpectAsync(
        Uri address,
        string request,
        IReadOnlyList<(string Name, string Value)> headers,
        byte[] body,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoopbackServer.Deadline);

        using TcpClient connection = new();
        await connection.ConnectAsync(address.Host, address.Port, deadline.Token);
        NetworkStream stream = connection.GetStream();

        List<(string Name, string Value)> head =
        [
            .. headers,
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
        ];

        await WriteHeadAsync(stream, address, request, head, deadline.Token);
        await stream.WriteAsync(body, deadline.Token);

        (int status, List<(string Name, string Value)> answerHeaders) = await ReadHeadAsync(stream, deadline.Token);
        byte[] answerBody = await ReadBodyAsync(stream, answerHeaders, deadline.Token);

        return new RawAnswer(status, Find(answerHeaders, "Outpost-Signature"), answerBody, BodySent: true);
    }

    // The status of the answer, or null when the connection was reset or closed before a status came.
    public static async Task<int?> SendChunkedAsync(
        Uri address,
        string request,
        IReadOnlyList<(string Name, string Value)> headers,
        byte[] body,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoopbackServer.Deadline);

        using TcpClient connection = new();
        await connection.ConnectAsync(address.Host, address.Port, deadline.Token);
        NetworkStream stream = connection.GetStream();

        await WriteHeadAsync(stream, address, request, [.. headers, ("Transfer-Encoding", "chunked")], deadline.Token);

        // The status is read while the body is sent: the server may answer, and close the connection, before it has
        // read the last of the body.
        Task sending = SendChunkAsync(stream, body, deadline.Token);
        int? status = await ReadStatusAsync(stream, deadline.Token);

        // A body that the server stopped reading is not sent to its end: closing the connection ends the send.
        connection.Close();
        await sending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        return status;
    }

    private static async Task WriteHeadAsync(
        NetworkStream stream,
        Uri address,
        string request,
        IReadOnlyList<(string Name, string Value)> headers,
        CancellationToken cancellationToken)
    {
        StringBuilder head = new();
        head.Append(CultureInfo.InvariantCulture, $"POST {new Uri(address, request).AbsolutePath} HTTP/1.1\r\n");
        head.Append(CultureInfo.InvariantCulture, $"Host: {address.Authority}\r\n");

        foreach ((string name, string value) in headers)
        {
            head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        }

        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken);
    }

    private static async Task SendChunkAsync(NetworkStream stream, byte[] body, CancellationToken cancellationToken)
    {
        byte[] size = Encoding.ASCII.GetBytes(body.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n");

        try
        {
            await stream.WriteAsync(size, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.WriteAsync("\r\n0\r\n\r\n"u8.ToArray(), cancellationToken);
        }
        catch (IOException)
        {
            // The server reset the connection while the body was sent; the status tells the rest.
        }
    }

    private static async Task<int?> ReadStatusAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            (int status, _) = await ReadHeadAsync(stream, cancellationToken);

            return status;
        }
        catch (IOException)
        {
            // A reset, or the end of the connection before the head of an answer was complete.
            return null;
        }
    }

    // The status line and the header lines of one answer, up to the empty line that ends them.
    private static async Task<(int Status, List<(string Name, string Value)> Headers)> ReadHeadAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        // HTTP/1.1 400 Bad Request
        string statusLine = await ReadLineAsync(stream, cancellationToken);
        int status = int.Parse(statusLine.Split(' ')[1], CultureInfo.InvariantCulture);

        List<(string Name, string Value)> headers = [];

        while (await ReadLineAsync(stream, cancellationToken) is { Length: > 0 } line)
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            headers.Add((line[..colon], line[(colon + 1)..].Trim()));
        }

        return (status, headers);
    }

    // The body of an answer, framed by its Content-Length or in chunks, or else up to the end of the connection.
    private static async Task<byte[]> ReadBodyAsync(
        NetworkStream stream,
        List<(string Name, string Value)> headers,
        CancellationToken cancellationToken)
    {
        if (Find(headers, "Transfer-Encoding") is "chunked")
        {
            List<byte> body = [];

            while (true)
            {
                string sizeLine = await ReadLineAsync(stream, cancellationToken);
                int size = int.Parse(sizeLine.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                if (size == 0)
                {
                    // The trailer section, which ends with an empty line.
                    while (await ReadLineAsync(stream, cancellationToken) is { Length: > 0 })
                    {
                    }

                    return [.. body];
                }

                body.AddRange(await ReadExactlyAsync(stream, size, cancellationToken));
                await ReadLineAsync(stream, cancellationToken);
            }
        }

        if (Find(headers, "Content-Length") is { } length)
        {
            return await ReadExactlyAsync(stream, int.Parse(length, CultureInfo.InvariantCulture), cancellationToken);
        }

        using MemoryStream rest = new();
        await stream.CopyToAsync(rest, cancellationToken);

        return rest.ToArray();
    }

    // One line without its CRLF. The heads of answers are short, so it reads a byte at a time.
    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        List<byte> line = [];
        byte[] next = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(next, cancellationToken) == 0)
            {
                throw new EndOfStreamException("The connection ended inside a line of the answer.");
            }

            if (next[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
            }

            line.Add(next[0]);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(
        NetworkStream stream,
        int count,
        CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken);

        return bytes;
    }

    // The value of the first line of a header, its name compared without regard to case.
    private static string? Find(List<(string Name, string Value)> headers, string name)
    {
        foreach ((string headerName, string value) in headers)
        {
            if (headerName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
