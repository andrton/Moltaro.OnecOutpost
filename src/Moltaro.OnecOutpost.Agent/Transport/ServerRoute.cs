namespace Moltaro.OnecOutpost.Agent.Transport;

// The way to the server: which addresses the agent reaches (§11 of docs/protocol.md) and the handler it reaches them
// through, with the proxy of the policy's proxy row.
internal static class ServerRoute
{
    // Accepts an absolute https address, and an http one whose Uri.Host is localhost, an address in 127.0.0.0/8 with
    // HostNameType IPv4, or [::1] with HostNameType IPv6; nothing is resolved. So loopback, 127.1 and 2130706433 count,
    // and localhost. and foo.localhost do not: Uri normalises before anyone reads the host, each form it maps there is
    // loopback, and one rule on its result keeps §11.
    public static bool IsAllowed(Uri server)
    {
        throw new NotSupportedException();
    }

    // Gives a SocketsHttpHandler with AllowAutoRedirect false, no decompression, MaxResponseDrainSize 0, no cookies, no
    // check of revocation, the 1-second Expect100ContinueTimeout of .NET (X2 of the register), UseProxy only for an
    // https server with a proxy named, so never the system's, and no credentials for the proxy or the server, so never
    // NTLM. Throws ArgumentException for an address that IsAllowed refuses.
    public static SocketsHttpHandler CreateHandler(Uri server, Uri? proxy)
    {
        throw new NotSupportedException();
    }
}
