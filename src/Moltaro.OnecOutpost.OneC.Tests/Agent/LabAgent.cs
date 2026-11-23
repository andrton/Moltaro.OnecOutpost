using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// An agent of the lab's tests, installed and paired as the console will do it (the S9b brief, "The lab tests"): a data
// folder of its own in the temporary folder of the lab disk, outside the repository, whose access list admits nobody
// but Administrators, SYSTEM and the account that runs the tests, as AdmitNobodyElse sets it, with policy.json,
// agent.json written through AgentPairingFile and secrets.dat through SecretsFile with MachineDpapi. The policy names
// the test base of lab.local.json as the base test, read and written, with one worker per mode, a run limit of a minute
// and a retryDelayMax of a second; the passwords of its two accounts come from lab.local.json and never from a file in
// Git. Its clock is a fake one, which its runs and their servers share, and its workers are in one job of the test. A
// test starts a run of the agent on it, and another after the first stopped, as a restart does. Disposing it disposes
// the job, which ends every worker left, and removes the folder.
[SupportedOSPlatform("windows")]
internal sealed class LabAgent : IDisposable
{
    public const string BaseKey = "test";

    public const string Version = "0.1.0-lab";

    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan RetryDelayMax = TimeSpan.FromSeconds(1);

    // How long a test waits for an event of 1C, such as an open or an outcome: the first connection of a Windows account
    // that never used 1C took twenty seconds on the lab machine (concept §12).
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo _temporary;

    private LabAgent(DirectoryInfo temporary)
    {
        _temporary = temporary;
        DataPath = Path.Combine(temporary.FullName, "data");
    }

    public string DataPath { get; }

    public Guid Agent { get; } = Guid.NewGuid();

    public ECDsa ServerKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public ECDsa AgentKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 22, 10, 15, 0, TimeSpan.Zero));

    public WorkerJob Job { get; } = new();

    public AgentFolders Folders => new(
        new DiskFolder(DataPath),
        new DiskFolder(Path.Combine(DataPath, "audit")),
        new DiskFolder(Path.Combine(DataPath, "outcomes")));

    // A data folder with the files of the agent of the test base.
    public static LabAgent Create()
    {
        Assert.True(
            File.Exists(Repository.ExecutorPath),
            $"{Repository.ExecutorPath} is missing: it is built from the sources in src/Executor with the Designer.");

        LabAgent agent = new(Directory.CreateTempSubdirectory("outpost-lab-agent-"));

        try
        {
            agent.WriteFiles(LabFile.Read().TestBase);

            return agent;
        }
        catch
        {
            agent.Dispose();
            throw;
        }
    }

    // Starts a run of the agent on the data folder, with a server and a launcher of its own.
    public LabRun Start()
    {
        return LabRun.Start(this);
    }

    public void Dispose()
    {
        Job.Dispose();
        ServerKey.Dispose();
        AgentKey.Dispose();

        foreach (string file in Directory.EnumerateFiles(_temporary.FullName, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _temporary.Delete(recursive: true);
    }

    private void WriteFiles(LabTestBase testBase)
    {
        Directory.CreateDirectory(DataPath);
        AccessListEdits.AdmitNobodyElse(DataPath);
        Directory.CreateDirectory(Path.Combine(DataPath, "audit"));
        Directory.CreateDirectory(Path.Combine(DataPath, "outcomes"));

        File.WriteAllBytes(Path.Combine(DataPath, "policy.json"), Policy(testBase));

        AgentPairingSettings pairing = new(
            new Uri("https://outpost.example.com/agents"),
            Agent,
            OutpostKeys.ExportPublicKey(ServerKey),
            LabServer.PollTimeout,
            LabServer.LeaseGrace);

        File.WriteAllBytes(Path.Combine(DataPath, "agent.json"), AgentPairingFile.Write(pairing));

        AgentSecrets secrets = new(
            AgentKey.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [(BaseKey, OutpostModeEnum.Read)] = testBase.Read.Password,
                [(BaseKey, OutpostModeEnum.Write)] = testBase.Write.Password,
            },
            new Dictionary<string, string>());

        File.WriteAllBytes(Path.Combine(DataPath, "secrets.dat"), SecretsFile.Write(secrets, new MachineDpapi()));
    }

    // policy.json as an administrator writes it for the test base.
    private static byte[] Policy(LabTestBase testBase)
    {
        using MemoryStream policy = new();

        using (Utf8JsonWriter writer = new(policy, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("retryDelayMax", RetryDelayMax.ToString("c"));
            writer.WriteStartArray("bases");
            writer.WriteStartObject();
            writer.WriteString("key", BaseKey);
            writer.WriteString("server", testBase.Server);
            writer.WriteString("database", testBase.Database);
            WriteMode(writer, "read", testBase.Read.User);
            WriteMode(writer, "write", testBase.Write.User);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return policy.ToArray();
    }

    private static void WriteMode(Utf8JsonWriter writer, string mode, string account)
    {
        writer.WriteStartObject(mode);
        writer.WriteString("account", account);
        writer.WriteNumber("workers", 1);
        writer.WriteString("runLimit", RunLimit.ToString("c"));
        writer.WriteEndObject();
    }
}
