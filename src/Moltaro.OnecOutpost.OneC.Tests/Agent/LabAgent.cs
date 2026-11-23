using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
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
// Its program folder (the S9c brief) is a folder of its own directly under the root of the system drive, as the
// console's install will put the program under %ProgramFiles%: there the worker account may read and run the program,
// which it may not inside the profile of the account that runs the tests, and the start's check of the program folder
// and its parents passes, since the root lets others add a folder only. It is made with inheritance off and the list
// that the install sets: full control for Administrators, SYSTEM and the account that runs the tests, which stands for
// the agent's, and read and run for Users, the worker account among them. It holds a copy of the worker beside the
// tests, with the files of its build, and of the executor of the repository.
//
// Its clock is a fake one, which its runs and their servers share, and its workers are in one job of the test. A test
// starts a run of the agent on it, and another after the first stopped, as a restart does. Disposing it disposes the
// job, which ends every worker left, waits until every process of the job ended, and then removes both folders:
// closing a job ends its processes asynchronously, and the file of a program that still runs cannot be removed. The
// process of the tests joins LabJob first, through which it finds the workers and waits for their end, those under a
// worker account included.
[SupportedOSPlatform("windows")]
internal sealed class LabAgent : IDisposable
{
    public const string Version = "0.1.0-lab";

    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan RetryDelayMax = TimeSpan.FromSeconds(1);

    private const string WorkerName = "Moltaro.OnecOutpost.ComWorker";

    private readonly DirectoryInfo _temporary;

    private LabAgent(DirectoryInfo temporary, string key, LabAccount? workerAccount)
    {
        _temporary = temporary;
        Key = key;
        WorkerAccount = workerAccount;
        DataPath = Path.Combine(temporary.FullName, "data");
        ProgramsPath = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory)!,
            $"outpost-lab-programs-{Guid.NewGuid():N}");
    }

    // The key of the base in the policy.
    public string Key { get; }

    // The account under which the agent runs the workers of its base, or null when they run under its own.
    public LabAccount? WorkerAccount { get; }

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

        return Create(LabTestBase.Key, testBase.Server, testBase.Database, testBase.Read, testBase.Write, workerAccount: null);
    }

    // The same, its Writes without safe mode under the worker account.
    public static LabAgent WithoutSafeMode(LabTestBase testBase, LabAccount workerAccount)
    {
        return Create(LabTestBase.Key, testBase.Server, testBase.Database, testBase.Read, testBase.Write, workerAccount);
    }

    // A data folder with the files of the agent of a copy of a typical base, as the base typical, its Writes without
    // safe mode under the worker account.
    public static LabAgent WithoutSafeMode(LabTypicalBase typicalBase, LabAccount workerAccount)
    {
        return Create(LabTypicalBase.Key, typicalBase.Server, typicalBase.Database, typicalBase.Read, typicalBase.Write, workerAccount);
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
        // Every process that the tests started and that runs is the agent's, its workers, since the tests run one at a
        // time and each waits for its own.
        IReadOnlyList<long> running = LabJob.Others();
        List<SafeProcessHandle> handles = LabJob.Open(running, WorkerAccount);

        try
        {
            Job.Dispose();
            ServerKey.Dispose();
            AgentKey.Dispose();

            Assert.True(
                LabJob.WaitUntilEnded(running, handles, LabLimits.Deadline),
                $"The workers of the agent did not end within {LabLimits.Deadline} after its job closed: processes {string.Join(", ", running)}.");
        }
        finally
        {
            // Closed before the removal: a handle of a process keeps the file of its program.
            foreach (SafeProcessHandle handle in handles)
            {
                handle.Dispose();
            }
        }

        Remove(_temporary.FullName);
        Remove(ProgramsPath);
    }

    private static LabAgent Create(
        string key,
        string server,
        string database,
        LabAccount read,
        LabAccount write,
        LabAccount? workerAccount)
    {
        Repository.AssertExecutorBuilt();

        LabJob.EnsureJoined();

        LabAgent agent = new(Directory.CreateTempSubdirectory("outpost-lab-agent-"), key, workerAccount);

        try
        {
            agent.WriteFiles(server, database, read, write);
            agent.WritePrograms();

            return agent;
        }
        catch
        {
            agent.Dispose();
            throw;
        }
    }

    // A folder of the agent with everything in it, read-only files included, when it was made.
    private static void Remove(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }

    private void WriteFiles(string server, string database, LabAccount read, LabAccount write)
    {
        Directory.CreateDirectory(DataPath);
        AccessListEdits.AdmitNobodyElse(DataPath);
        Directory.CreateDirectory(Path.Combine(DataPath, "audit"));
        Directory.CreateDirectory(Path.Combine(DataPath, "outcomes"));

        File.WriteAllBytes(Path.Combine(DataPath, "policy.json"), Policy(server, database, read.User, write.User, WorkerAccount?.User));

        AgentPairingSettings pairing = new(
            new Uri("https://outpost.example.com/agents"),
            Agent,
            OutpostKeys.ExportPublicKey(ServerKey),
            LabServer.PollTimeout,
            LabServer.LeaseGrace);

        File.WriteAllBytes(Path.Combine(DataPath, "agent.json"), AgentPairingFile.Write(pairing));

        Dictionary<string, string> workerPasswords = [];

        if (WorkerAccount is not null)
        {
            workerPasswords[Key] = WorkerAccount.Password;
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
