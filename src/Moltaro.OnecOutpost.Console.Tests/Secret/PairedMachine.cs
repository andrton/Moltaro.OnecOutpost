using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Tests.Secret;

// A test's machine as install and pair left it, and earlier runs of secret: the data folder with the policy of
// TestMachine, and secrets.dat with the agent's key, a password for each mode of the policy, the one of the read-only
// base empty, and one for the worker account of accounting, a local user of the machine.
internal static class PairedMachine
{
    // The local user that accounting's workers run under, as secret names it to Windows.
    public static readonly string WorkerAccount = $@"{Environment.MachineName}\outpost-worker";

    public const string WorkerSid = "S-1-5-21-1004336348-1177238915-682003330-1001";

    // The secrets as earlier runs wrote them, each as "{base} {read|write|worker}: {password}".
    public static readonly string[] Earlier =
    [
        "accounting read: old-read",
        "accounting worker: old-worker",
        "accounting write: old-write",
        "warehouse read: ",
    ];

    public static TestMachine Create(out byte[] agentKey)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        agentKey = key.ExportPkcs8PrivateKey();

        TestMachine machine = new();
        machine.Installed();
        machine.Rights.Users[WorkerAccount] = WorkerSid;

        AgentSecrets secrets = new(
            agentKey,
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [("accounting", OutpostModeEnum.Read)] = "old-read",
                [("accounting", OutpostModeEnum.Write)] = "old-write",
                [("warehouse", OutpostModeEnum.Read)] = "",
            },
            new Dictionary<string, string> { ["accounting"] = "old-worker" });
        machine.Data.Root.Seed("secrets.dat", SecretsFile.Write(secrets, machine.Protection));

        return machine;
    }

    // What secrets.dat of the machine holds, read as the agent reads it.
    public static AgentSecrets SecretsOf(TestMachine machine)
    {
        return SecretsFile.Read(machine.Data.Root.Bytes("secrets.dat"), machine.Protection);
    }

    // The passwords of the secrets, each as "{base} {read|write|worker}: {password}", by ordinal order.
    public static IReadOnlyList<string> PasswordsOf(AgentSecrets secrets)
    {
        IEnumerable<string> modes = secrets.Passwords.Select(entry =>
            $"{entry.Key.Base} {(entry.Key.Mode == OutpostModeEnum.Read ? "read" : "write")}: {entry.Value}");
        IEnumerable<string> workers = secrets.WorkerPasswords.Select(entry => $"{entry.Key} worker: {entry.Value}");

        return [.. modes.Concat(workers).Order(StringComparer.Ordinal)];
    }
}
