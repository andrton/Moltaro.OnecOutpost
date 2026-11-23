using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Access;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWindows;

// The access lists of a data folder on the disk as Windows keeps them, with a program folder that lets nobody else
// change it, as FakeAccessLists answers: for the tests that state a rule of the data folder on the disk, whose start
// would otherwise stop on the program folder first. The program folder on the disk has tests of its own
// (ProgramWritersTests), since a real one under the temporary folder is judged by the parents of the machine.
[SupportedOSPlatform("windows")]
internal sealed class DataFolderAccessLists : IAccessLists
{
    private readonly WindowsAccessLists _dataFolder;

    private readonly FakeAccessLists _programs = new();

    public DataFolderAccessLists(string dataFolder)
    {
        _dataFolder = new WindowsAccessLists(dataFolder);
    }

    public IReadOnlyList<string> Others(string path)
    {
        return _dataFolder.Others(path);
    }

    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        return _programs.Writers(folder);
    }
}
