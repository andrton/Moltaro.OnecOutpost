using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Tests.FakeWindows;

namespace Moltaro.OnecOutpost.Agent.Tests;

// The data folder of an agent that the console has installed, paired and given its secrets, in memory: policy.json,
// agent.json and secrets.dat written by hand in the forms of "The files of the agent" and of the S6 brief, an empty
// audit log and an empty outbox, access lists that admit nobody else and the fake protection. A test changes what it
// needs, then starts the agent on it as often as a restart needs.
internal sealed class PairedFolder
{
    // The identity of the agent in agent.json.
    public static readonly Guid Agent = Guid.Parse("5f0c3a1e-2b4d-4c6e-8a10-b2c3d4e5f607");

    public const string Version = "0.1.0-test";

    // Two bases: one on a cluster that may be read and written, and one in a folder that is only read.
    public const string Policy = """
        {
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read" },
              "write": { "account": "outpost-write" }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    private PairedFolder(byte[] serverKey, byte[] serverPrivateKey, byte[] privateKey)
    {
        ServerKey = serverKey;
        ServerPrivateKey = serverPrivateKey;
        PrivateKey = privateKey;
    }

    public MemoryDataFolder Data { get; } = new();

    public FakeAccessLists Access { get; } = new();

    public FakeProtection Protection { get; } = new();

    // The 91 bytes of the server key that the agent pinned.
    public byte[] ServerKey { get; }

    // The PKCS#8 form of the server's signing key, which only the server holds: the fake server of a test signs with it.
    public byte[] ServerPrivateKey { get; }

    // The PKCS#8 form of the agent's key.
    public byte[] PrivateKey { get; }

    // The text of agent.json as the console writes it.
    public string AgentJson =>
        $$"""{"server":"https://outpost.example.com/agents","agent":"{{Agent}}","serverKey":"{{Base64Url.EncodeToString(ServerKey)}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""";

    // The policy with these members of its own besides the bases, such as "retryDelayMax": "00:00:04".
    public static string PolicyWith(string members)
    {
        return Policy.Replace("\"bases\":", members + ", \"bases\":", StringComparison.Ordinal);
    }

    public static PairedFolder Create()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa agent = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        PairedFolder folder = new(
            server.ExportSubjectPublicKeyInfo(),
            server.ExportPkcs8PrivateKey(),
            agent.ExportPkcs8PrivateKey());

        folder.Data.Root.Seed("policy.json", Policy);
        folder.Data.Root.Seed("agent.json", folder.AgentJson);
        folder.SeedSecrets(("accounting", "read", "read-pw"), ("accounting", "write", "write-pw"), ("warehouse", "read", ""));

        return folder;
    }

    // Writes secrets.dat with the agent's key and these passwords, as the console's secret writes them.
    public void SeedSecrets(params (string Base, string Mode, string Password)[] passwords)
    {
        string entries = string.Join(
            ",",
            passwords.Select(entry => $$"""{"base":"{{entry.Base}}","mode":"{{entry.Mode}}","password":"{{entry.Password}}"}"""));

        string plain = $$"""{"key":"{{Base64Url.EncodeToString(PrivateKey)}}","passwords":[{{entries}}]}""";

        Data.Root.Seed("secrets.dat", Protection.Protect(Encoding.UTF8.GetBytes(plain)));
    }

    public AgentRun Start(TimeProvider clock)
    {
        return AgentStart.Open(Data.Folders, Access, Protection, clock, Version);
    }
}
