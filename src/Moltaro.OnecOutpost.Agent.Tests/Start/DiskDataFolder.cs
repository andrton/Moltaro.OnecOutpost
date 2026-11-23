using System.Buffers.Text;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// A data folder on the disk, in the test's temporary folder, with the access list that the console's install gives it
// and the account that runs the tests in the place of the agent's, for the tests with Requires=Windows: policy.json,
// agent.json and secrets.dat as an administrator and the console write them, secrets.dat through SecretsFile, encrypted
// with DPAPI for the machine. Disposing it removes it.
[SupportedOSPlatform("windows")]
internal sealed class DiskDataFolder : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    public DiskDataFolder()
    {
        Path = _temporary.PathOf("data");
        Directory.CreateDirectory(Path);
        AccessListEdits.AdmitNobodyElse(Path);
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "audit"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "outcomes"));
    }

    public string Path { get; }

    public AgentFolders Folders => new(
        new DiskFolder(Path),
        new DiskFolder(System.IO.Path.Combine(Path, "audit")),
        new DiskFolder(System.IO.Path.Combine(Path, "outcomes")));

    public string PathOf(string relativePath)
    {
        return System.IO.Path.Combine(Path, relativePath);
    }

    // policy.json, agent.json and secrets.dat as an administrator and the console write them.
    public void WriteFiles()
    {
        WriteFiles(PairedFolder.Policy);
    }

    // The same with this policy, which serves PairedFolder's bases and modes, and the passwords of the worker accounts
    // of these bases (the S9c brief).
    public void WriteFiles(string policy, params (string Base, string Password)[] workers)
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa agent = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        File.WriteAllText(PathOf("policy.json"), policy);
        File.WriteAllText(
            PathOf("agent.json"),
            $$"""{"server":"https://outpost.example.com/agents","agent":"{{PairedFolder.Agent}}","serverKey":"{{Base64Url.EncodeToString(server.ExportSubjectPublicKeyInfo())}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""");

        AgentSecrets secrets = new(
            agent.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [("accounting", OutpostModeEnum.Read)] = "read-pw",
                [("accounting", OutpostModeEnum.Write)] = "write-pw",
                [("warehouse", OutpostModeEnum.Read)] = "",
            },
            workers.ToDictionary(entry => entry.Base, entry => entry.Password, StringComparer.Ordinal));

        File.WriteAllBytes(PathOf("secrets.dat"), SecretsFile.Write(secrets, new MachineDpapi()));
    }

    // Every file of the data folder, audit\ and outcomes\ included, with its bytes.
    public IEnumerable<(string Path, byte[] Bytes)> Files()
    {
        foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            yield return (file, File.ReadAllBytes(file));
        }
    }

    // The last entry of the audit file of the UTC day of the time.
    public JsonElement LastEntry(DateTimeOffset day)
    {
        string line = File.ReadAllLines(PathOf(System.IO.Path.Combine("audit", AuditLines.FileOf(day))))[^1];

        using JsonDocument document = JsonDocument.Parse(line);

        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        _temporary.Dispose();
    }
}
