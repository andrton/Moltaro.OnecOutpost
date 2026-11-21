using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Protocol.Tests;

// The fixed values that the tests of the protocol share: the identities of the examples of docs/protocol.md, and one
// public key with its fingerprint. The key was generated once, outside the codec, and its fingerprint is the SHA-256 of
// its 91 bytes, computed outside the codec too.
internal static class Sample
{
    public const string AgentText = "3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b";

    public const string OtherAgentText = "7a1d0c9b-2e3f-4a5b-8c6d-0e1f2a3b4c5d";

    public const string CommandText = "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6";

    public const string OtherCommandText = "1b2a3c4d-5e6f-4a7b-9c8d-e0f1a2b3c4d5";

    // The 91 bytes of a public key in the form of §2, in hexadecimal digits.
    public const string PublicKeyHex =
        "3059301306072a8648ce3d020106082a8648ce3d03010703420004"
        + "14644602dad49abcdff37fff695f67b5f38eafa762d61419cccf0a4815b12ca5"
        + "9e0feb7a168b54f956bb7b7ce608e09c59b534933c863b81b71c993a8c1ea2df";

    // The same 91 bytes as base64url without padding.
    public const string PublicKeyText =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFGRGAtrUmrzf83__aV9ntfOOr6di1hQZzM8KSBWxLKWeD-t6FotU-Va7e3zmCOCcWbU0kzyGO4G3HJk6jB6i3w";

    // SHA-256 over the same 91 bytes.
    public const string PublicKeyFingerprint = "f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91";

    public static Guid Agent { get; } = Guid.Parse(AgentText);

    public static Guid OtherAgent { get; } = Guid.Parse(OtherAgentText);

    public static Guid Command { get; } = Guid.Parse(CommandText);

    public static Guid OtherCommand { get; } = Guid.Parse(OtherCommandText);

    public static byte[] PublicKey => Convert.FromHexString(PublicKeyHex);

    // A new key of the protocol: ECDSA on P-256. The caller disposes it.
    public static ECDsa NewKey()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }
}
