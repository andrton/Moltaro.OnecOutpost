using System.Runtime.Versioning;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.Secrets;

// The secrets are encrypted with DPAPI for the machine (concept §5, principle 9), so that the agent's account, which has
// no profile of its own, reads what an administrator's console wrote. A blob of DPAPI starts with its version, 1, and
// the identity of its provider, df9d8cd0-1501-11d1-8c7a-00c04fc297eb; at byte 40 it records the flags it was made with,
// among them CRYPTPROTECT_LOCAL_MACHINE (4) for a blob of the machine. Nothing in a test can copy a blob to another
// machine, so a blob that DPAPI did not make, or that was changed, stands for one.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class MachineDpapiTests
{
    private const uint LocalMachine = 0x4;

    private static readonly Guid Provider = Guid.Parse("df9d8cd0-1501-11d1-8c7a-00c04fc297eb");

    [Fact]
    public void Unprotect_gives_back_what_Protect_was_given_and_the_blob_holds_no_plaintext()
    {
        byte[] data = "the S3cret of the agent"u8.ToArray();
        MachineDpapi protection = new();

        byte[] blob = protection.Protect(data);

        Assert.Equal(-1, blob.AsSpan().IndexOf("S3cret"u8));
        Assert.Equal(data, protection.Unprotect(blob));
    }

    [Fact]
    public void Protect_makes_a_blob_of_DPAPI_for_the_machine()
    {
        byte[] blob = new MachineDpapi().Protect("the S3cret of the agent"u8.ToArray());

        AssertBlobOfTheMachine(blob);
    }

    [Fact]
    public void The_secrets_file_written_through_DPAPI_is_a_blob_for_the_machine_and_reads_back()
    {
        using ECDsa agent = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MachineDpapi protection = new();

        AgentSecrets secrets = new(agent.ExportPkcs8PrivateKey(), new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "S3cret-password",
        }, new Dictionary<string, string>());

        byte[] file = SecretsFile.Write(secrets, protection);
        AgentSecrets read = SecretsFile.Read(file, protection);

        AssertBlobOfTheMachine(file);
        Assert.Equal(secrets.PrivateKey, read.PrivateKey);
        Assert.Equal("S3cret-password", read.Passwords[("accounting", OutpostModeEnum.Read)]);
    }

    [Fact]
    public void Unprotect_of_bytes_that_DPAPI_did_not_make_fails_with_CryptographicException()
    {
        byte[] notABlob = RandomNumberGenerator.GetBytes(230);

        Assert.ThrowsAny<CryptographicException>(() => new MachineDpapi().Unprotect(notABlob));
    }

    [Fact]
    public void Unprotect_of_a_blob_that_was_changed_fails_with_CryptographicException()
    {
        MachineDpapi protection = new();
        byte[] blob = protection.Protect("the S3cret of the agent"u8.ToArray());
        blob[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protection.Unprotect(blob));
    }

    private static void AssertBlobOfTheMachine(byte[] blob)
    {
        Assert.Equal(1u, BitConverter.ToUInt32(blob, 0));
        Assert.Equal(Provider, new Guid(blob.AsSpan(4, 16)));
        Assert.Equal(LocalMachine, BitConverter.ToUInt32(blob, 40) & LocalMachine);
    }
}
