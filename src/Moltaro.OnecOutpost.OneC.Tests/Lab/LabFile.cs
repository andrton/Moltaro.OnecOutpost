using System.Text.Json;

namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// The lab's src/OneC.TestConfiguration/lab.local.json, which Git ignores and which never leaves the lab machine: the
// test base with the accounts of its users, and the copies of typical bases. lab.local.example.json shows its shape.
// The members of the file that the tests do not use are ignored. No class of it prints its values, since they hold
// passwords.
internal sealed class LabFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public required LabTestBase TestBase { get; init; }

    public IReadOnlyList<LabTypicalBase> TypicalBases { get; init; } = [];

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
