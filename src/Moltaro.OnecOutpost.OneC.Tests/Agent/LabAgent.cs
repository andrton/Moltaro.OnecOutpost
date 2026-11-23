using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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
// one base of lab.local.json, the test base as the base test unless a test names a copy of a typical base, read and
// written, with one worker per mode, a run limit of a minute and a retryDelayMax of a second; the passwords of its two
// accounts come from lab.local.json and never from a file in Git. Its Writes run in safe mode, or without it under the
// worker account of lab.local.json, whose password secrets.dat holds as well (the S9c brief).
//
// Beside the data folder is its program folder, as the console's install sets one (the S9c brief): inheritance off,
// full control for Administrators, SYSTEM and the account that runs the tests, read and run for Users, holding a copy
// of the worker beside the tests, with the files of its build, and of the executor of the repository, so that a worker
// account may run them. Its clock is a fake one, which its runs and their servers share, and its workers are in one job
// of the test. A test starts a run of the agent on it, and another after the first stopped, as a restart does.
// Disposing it disposes the job, which ends every worker left, and removes the folders.
[SupportedOSPlatform("windows")]
internal sealed class LabAgent : IDisposable
{
    public const string TestBaseKey = "test";

    public const string TypicalBaseKey = "typical";

    public const string Version = "0.1.0-lab";

    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan RetryDelayMax = TimeSpan.FromSeconds(1);

    // How long a test waits for an event of 1C, such as an open or an outcome: the first connection of a Windows account
    // that never used 1C took twenty seconds on the lab machine (concept §12).
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    private const string WorkerName = "Moltaro.OnecOutpost.ComWorker";

    private readonly DirectoryInfo _temporary;

    private LabAgent(DirectoryInfo temporary, string key)
    {
        _temporary = temporary;
        Key = key;
        DataPath = Path.Combine(temporary.FullName, "data");
        ProgramsPath = Path.Combine(temporary.FullName, "programs");
    }

    // The key of the base in the policy.
    public string Key { get; }

    public string DataPath { get; }

    // The program folder, which the start judges and from which the workers run.
    public string ProgramsPath { get; }

    public string WorkerPath => Path.Combine(ProgramsPath, WorkerName + ".exe");

    public string ExecutorPath => Path.Combine(ProgramsPath, "OutpostExecutor.epf");

    public Guid Agent { get; } = Guid.NewGuid();

    public ECDsa ServerKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public ECDsa AgentKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 22, 10, 15, 0, TimeSpan.Zero));

    public WorkerJob Job { get; } = new();

    public AgentFolders Folders => new(
        new DiskFolder(DataPath),
        new DiskFolder(Path.Combine(DataPath, "audit")),
        new DiskFolder(Path.Combine(DataPath, "outcomes")));

    // A data folder with the files of the agent of the test base, its Writes in safe mode.
    public static LabAgent Create()
    {
        LabTestBase testBase = LabFile.Read().TestBase;

        return Create(TestBaseKey, testBase.Server, testBase.Database, testBase.Read, testBase.Write, workerAccount: null);
    }

    // The same, its Writes without safe mode under the worker account.
    public static LabAgent WithoutSafeMode(LabTestBase testBase, LabAccount workerAccount)
    {
        return Create(TestBaseKey, testBase.Server, testBase.Database, testBase.Read, testBase.Write, workerAccount);
    }

    // A data folder with the files of the agent of a copy of a typical base, as the base typical, its Writes without
    // safe mode under the worker account.
    public static LabAgent WithoutSafeMode(LabTypicalBase typicalBase, LabAccount workerAccount)
    {
        return Create(TypicalBaseKey, typicalBase.Server, typicalBase.Database, typicalBase.Read, typicalBase.Write, workerAccount);
    }

    // The worker account of lab.local.json, which the owner adds by hand.
    public static LabAccount WorkerAccountOf(LabFile lab)
    {
        Assert.True(
            lab.WorkerAccount is not null,
            "lab.local.json names no workerAccount: a local user outside Administrators with \"Log on as a batch job\".");

        return lab.WorkerAccount;
    }

    // Starts a run of the agent on the data folder, with a server and a launcher of its own.
    public LabRun Start()
    {
        return LabRun.Start(this);
    }

    // Writes a file beside the data folder that Users may read and run, and gives its full path: a file that a worker
    // account may read, at the same depth of the same folder as the data folder.
    public string WriteFileForUsers(string name, string content)
    {
        string path = Path.Combine(_temporary.FullName, name);

        File.WriteAllText(path, content);
        AccessListEdits.Allow(path, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute);

        return path;
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

    private static LabAgent Create(
        string key,
        string server,
        string database,
        LabAccount read,
        LabAccount write,
        LabAccount? workerAccount)
    {
        Assert.True(
            File.Exists(Repository.ExecutorPath),
            $"{Repository.ExecutorPath} is missing: it is built from the sources in src/Executor with the Designer.");

        LabAgent agent = new(Directory.CreateTempSubdirectory("outpost-lab-agent-"), key);

        try
        {
            agent.WriteFiles(server, database, read, write, workerAccount);
            agent.WritePrograms();

            return agent;
        }
        catch
        {
            agent.Dispose();
            throw;
        }
    }

    private void WriteFiles(string server, string database, LabAccount read, LabAccount write, LabAccount? workerAccount)
    {
        Directory.CreateDirectory(DataPath);
        AccessListEdits.AdmitNobodyElse(DataPath);
        Directory.CreateDirectory(Path.Combine(DataPath, "audit"));
        Directory.CreateDirectory(Path.Combine(DataPath, "outcomes"));

        File.WriteAllBytes(Path.Combine(DataPath, "policy.json"), Policy(server, database, read.User, write.User, workerAccount?.User));

        AgentPairingSettings pairing = new(
            new Uri("https://outpost.example.com/agents"),
            Agent,
            OutpostKeys.ExportPublicKey(ServerKey),
            LabServer.PollTimeout,
            LabServer.LeaseGrace);

        File.WriteAllBytes(Path.Combine(DataPath, "agent.json"), AgentPairingFile.Write(pairing));

        Dictionary<string, string> workerPasswords = [];

        if (workerAccount is not null)
        {
            workerPasswords[Key] = workerAccount.Password;
        }

        AgentSecrets secrets = new(
            AgentKey.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [(Key, OutpostModeEnum.Read)] = read.Password,
                [(Key, OutpostModeEnum.Write)] = write.Password,
            },
            workerPasswords);

        File.WriteAllBytes(Path.Combine(DataPath, "secrets.dat"), SecretsFile.Write(secrets, new MachineDpapi()));
    }

    // The worker with the files of its build, and the executor.
    private void WritePrograms()
    {
        Directory.CreateDirectory(ProgramsPath);
        AccessListEdits.LetUsersReadAndRun(ProgramsPath);

        foreach (string extension in new[] { ".exe", ".dll", ".runtimeconfig.json", ".deps.json" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, WorkerName + extension), Path.Combine(ProgramsPath, WorkerName + extension));
        }

        File.Copy(Repository.ExecutorPath, ExecutorPath);
    }

    // policy.json as an administrator writes it for the base.
    private byte[] Policy(string server, string database, string readUser, string writeUser, string? workerAccount)
    {
        using MemoryStream policy = new();

        using (Utf8JsonWriter writer = new(policy, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("retryDelayMax", RetryDelayMax.ToString("c"));
            writer.WriteStartArray("bases");
            writer.WriteStartObject();
            writer.WriteString("key", Key);
            writer.WriteString("server", server);
            writer.WriteString("database", database);
            WriteMode(writer, "read", readUser);
            WriteMode(writer, "write", writeUser, workerAccount);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return policy.ToArray();
    }

    // A mode; a write with a worker account turns safe mode off.
    private static void WriteMode(Utf8JsonWriter writer, string mode, string account, string? workerAccount = null)
    {
        writer.WriteStartObject(mode);
        writer.WriteString("account", account);
        writer.WriteNumber("workers", 1);
        writer.WriteString("runLimit", RunLimit.ToString("c"));

        if (workerAccount is not null)
        {
            writer.WriteBoolean("safeMode", false);
            writer.WriteString("workerAccount", workerAccount);
        }

        writer.WriteEndObject();
    }
}
