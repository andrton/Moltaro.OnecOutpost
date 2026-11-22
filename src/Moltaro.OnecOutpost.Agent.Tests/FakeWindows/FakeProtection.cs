using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Secrets;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWindows;

// The protection of the secrets in place of DPAPI: it changes every byte reversibly, so that no plaintext is in what it
// protects, and puts a mark first, so that it refuses to unprotect what it did not protect. A test can also make it
// refuse every unprotect, as DPAPI refuses a file copied from another machine.
internal sealed class FakeProtection : IMachineProtection
{
    private const byte Mask = 0x5A;

    private static readonly byte[] Mark = "fake-protection:"u8.ToArray();

    // When true, Unprotect throws CryptographicException whatever it is given.
    public bool Refuses { get; set; }

    public byte[] Protect(byte[] data)
    {
        return [.. Mark, .. data.Select(value => (byte)(value ^ Mask))];
    }

    public byte[] Unprotect(byte[] data)
    {
        if (Refuses || !data.AsSpan().StartsWith(Mark))
        {
            throw new CryptographicException("The fake protection refuses to unprotect the data.");
        }

        return [.. data.Skip(Mark.Length).Select(value => (byte)(value ^ Mask))];
    }
}
