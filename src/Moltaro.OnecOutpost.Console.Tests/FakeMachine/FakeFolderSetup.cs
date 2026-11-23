using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The folders of a test's machine as NTFS keeps them. Kind answers what the test says is at a path, nothing until it
// says otherwise, and FinalPath the final path the test gives a spelling of a folder, with or without its ending
// separator, otherwise the spelling without it, as GetFinalPathNameByHandleW never gives one; neither changes anything,
// so neither is a line. Each change is first a line of the operation list of the machine's data folder: "apply program
// folder {folder} from {source}" or "apply program folder {folder}" for a null source, "apply data folder {folder} for
// {sid}, creating it" or "apply data folder {folder} for {sid}", and "own {file}". Then it fails with the exception the
// test gave for it, an IOException or an UnauthorizedAccessException as the boundary says, or does nothing more. Not
// thread-safe.
internal sealed class FakeFolderSetup : IFolderSetup
{
    private readonly MemoryDataFolder _notes;

    public FakeFolderSetup(MemoryDataFolder notes)
    {
        _notes = notes;
    }

    // What is at each full path; a path the test names not is Absent.
    public Dictionary<string, FolderKindEnum> Kinds { get; } = new(StringComparer.Ordinal);

    // The final path of each spelling of a folder that the test names without its ending separator, such as a short
    // name.
    public Dictionary<string, string> FinalPaths { get; } = new(StringComparer.Ordinal);

    public Exception? ProgramFolderFailure { get; set; }

    public Exception? DataFolderFailure { get; set; }

    public Exception? OwnFailure { get; set; }

    public FolderKindEnum Kind(string path)
    {
        return Kinds.GetValueOrDefault(path, FolderKindEnum.Absent);
    }

    public string FinalPath(string folder)
    {
        string spelling = Path.TrimEndingDirectorySeparator(folder);

        return FinalPaths.GetValueOrDefault(spelling) ?? spelling;
    }

    public void ApplyProgramFolder(string folder, string? source)
    {
        _notes.Note(source is null ? $"apply program folder {folder}" : $"apply program folder {folder} from {source}");

        if (ProgramFolderFailure is not null)
        {
            throw ProgramFolderFailure;
        }
    }

    public void ApplyDataFolder(string folder, string agentSid, bool create)
    {
        _notes.Note(create ? $"apply data folder {folder} for {agentSid}, creating it" : $"apply data folder {folder} for {agentSid}");

        if (DataFolderFailure is not null)
        {
            throw DataFolderFailure;
        }
    }

    public void OwnByAdministrators(string file)
    {
        _notes.Note($"own {file}");

        if (OwnFailure is not null)
        {
            throw OwnFailure;
        }
    }
}
