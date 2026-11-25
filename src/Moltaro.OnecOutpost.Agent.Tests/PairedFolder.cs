using System.Buffers.Text;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWindows;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests;

// The data folder of an agent that the console has installed, paired and given its secrets, in memory: policy.json
// and agent.json written by hand in the forms of "The files of the agent" and of the S6 brief, secrets.dat written by
// SecretsFile, whose own tests state its form, an empty audit log and an empty outbox, access lists that admit nobody
// else and the fake protection, and the fake launcher of the workers, which every start of the folder is given. A test
// changes what it needs, then starts the agent on it as often as a restart needs.
internal sealed class PairedFolder
{
    // The identity of the agent in agent.json.
    public static readonly Guid Agent = Guid.Parse("5f0c3a1e-2b4d-4c6e-8a10-b2c3d4e5f607");

    public const string Version = "0.1.0-test";

    // The program folder of the agent, as its start is given it.
    public const string ProgramFolder = @"C:\Program Files\Moltaro Outpost";

    // The executor in the program folder, as the host and the pool are given it.
    public const string Executor = ProgramFolder + @"\OutpostExecutor.epf";

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

    // The passwords of the policy's modes, as the console's secret writes them.
    public static readonly (string Base, OutpostModeEnum Mode, string Password)[] Passwords =
    [
        ("accounting", OutpostModeEnum.Read, "read-pw"),
        ("accounting", OutpostModeEnum.Write, "write-pw"),
        ("warehouse", OutpostModeEnum.Read, ""),
    ];

    private PairedFolder(byte[] serverKey, byte[] serverPrivateKey, byte[] privateKey)
    {
        ServerKey = serverKey;
        ServerPrivateKey = serverPrivateKey;
        PrivateKey = privateKey;
        Workers = new FakeWorkerLauncher(Data);
    }

    public MemoryDataFolder Data { get; } = new();

    public FakeAccessLists Access { get; } = new();

    public FakeProtection Protection { get; } = new();

    // The launcher that a start through Start is given, so that a test sees the logons of the worker accounts.
    public FakeWorkerLauncher Workers { get; }

    // The 91 bytes of the server key that the agent pinned.
    public byte[] ServerKey { get; }

    // The PKCS#8 form of the server's signing key, which only the server holds: the fake server of a test signs with it.
    public byte[] ServerPrivateKey { get; }

    // The PKCS#8 form of the agent's key.
    public byte[] PrivateKey { get; }

    // The text of agent.json as the console writes it.
    public string AgentJson => AgentJsonOf(ServerKey);

    // The text of agent.json as the console writes it for the server key.
    public static string AgentJsonOf(byte[] serverKey)
    {
        return $$"""{"server":"https://outpost.example.com/agents","agent":"{{Agent}}","serverKey":"{{Base64Url.EncodeToString(serverKey)}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""";
    }

    // The policy with these members of its own besides the bases, such as "retryDelayMax": "00:00:04".
    public static string PolicyWith(string members)
    {
        return Policy.Replace("\"bases\":", members + ", \"bases\":", StringComparison.Ordinal);
    }

    // The policy with the Writes of accounting out of safe mode under the worker account (the S9c brief).
    public static string PolicyWithWorkerAccount(string account)
    {
        return Policy.Replace(
            "\"write\": { \"account\": \"outpost-write\" }",
            $"\"write\": {{ \"account\": \"outpost-write\", \"safeMode\": false, \"workerAccount\": \"{account}\" }}",
            StringComparison.Ordinal);
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
        folder.SeedSecrets(Passwords);

        return folder;
    }

    // Writes secrets.dat with the agent's key and these passwords, as the console's secret writes them.
    public void SeedSecrets(params (string Base, OutpostModeEnum Mode, string Password)[] passwords)
    {
        SeedSecrets(passwords, []);
    }

    // The same, with the passwords of the worker accounts of these bases (the S9c brief).
    public void SeedSecrets(
        (string Base, OutpostModeEnum Mode, string Password)[] passwords,
        (string Base, string Password)[] workers)
    {
        AgentSecrets secrets = new(
            PrivateKey,
            passwords.ToDictionary(entry => (entry.Base, entry.Mode), entry => entry.Password),
            workers.ToDictionary(entry => entry.Base, entry => entry.Password, StringComparer.Ordinal));

        Data.Root.Seed("secrets.dat", SecretsFile.Write(secrets, Protection));
    }

    public AgentRun Start(TimeProvider clock)
    {
        return AgentStart.Open(Data.Folders, Access, Protection, Workers, ProgramFolder, clock, Version);
    }
}
