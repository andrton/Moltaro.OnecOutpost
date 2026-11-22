using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Server.Signing;

// The server's signing key, made once by AddOutpostServer from the PEM that the options validated. It keeps the private
// key as PKCS#8 bytes, which never leave the server (principle 9), and signs the answers to the agents and the commands
// (§3 of docs/protocol.md).
//
// .NET does not document an ECDsa as safe for concurrent use, so each signature imports the key into an ECDsa of its
// own and disposes it. Nothing here changes after the constructor, so no lock is needed.
internal sealed class ServerSigningKey
{
    private readonly byte[] _pkcs8;

    public ServerSigningKey(string pkcs8Pem)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(pkcs8Pem);

        _pkcs8 = key.ExportPkcs8PrivateKey();
        PublicKey = OutpostKeys.ExportPublicKey(key);
        Fingerprint = OutpostKeys.Fingerprint(PublicKey);
    }

    // The public key in the 91-byte form of §2, which the answer to pair carries and the agent pins.
    public byte[] PublicKey { get; }

    // The fingerprint of the public key (§2), which every pairing token carries.
    public string Fingerprint { get; }

    // Signs an answer over its signing input of §3, so that it fits only the one request it answers.
    public byte[] SignAnswer(
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> answerBody)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(_pkcs8, out _);

        return OutpostSignatures.SignAnswer(key, kind, agent, status, requestBody, answerBody);
    }

    // Signs the exact bytes of a command over its signing input of §3. A call signs its command once, and the store
    // keeps the bytes and the signature, which every poll that hands the command out sends as they are.
    public byte[] SignCommand(ReadOnlySpan<byte> commandBytes)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(_pkcs8, out _);

        return OutpostSignatures.SignCommand(key, commandBytes);
    }
}
