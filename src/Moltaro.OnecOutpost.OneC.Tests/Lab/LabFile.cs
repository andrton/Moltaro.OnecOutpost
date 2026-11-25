using System.Text.Json;

namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// The lab's src/OneC.TestConfiguration/lab.local.json, which Git ignores and which never leaves the lab machine: the
// test base with the accounts of its users, the copies of typical bases, and the worker account. lab.local.example.json
// shows its shape. The members of the file that the tests do not use are ignored. No class of it prints its values,
// since they hold passwords.
internal sealed class LabFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public required LabTestBase TestBase { get; init; }

    public IReadOnlyList<LabTypicalBase> TypicalBases { get; init; } = [];

    // The local Windows account under which the agent of a test runs the COM workers of a base whose Writes run
    // without safe mode, with its password: a user outside Administrators with "Log on as a batch job" that never
    // signed in, so that it has no profile, as the console's install prepares one. The lab administrator configures
    // it by hand; the tests that do not need it run without it.
    public LabAccount? WorkerAccount { get; init; }

    public static LabFile Read()
    {
        string path = Repository.LabFilePath;

        Assert.True(
            File.Exists(path),
            $"{path} is missing: build the test base with src/OneC.TestConfiguration/Build-TestBase.ps1 first.");

        return JsonSerializer.Deserialize<LabFile>(File.ReadAllBytes(path), Options)
            ?? throw new InvalidDataException($"{path} holds null.");
    }
}
