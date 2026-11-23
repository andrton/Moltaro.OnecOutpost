using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Console.Folders;

// The folders as NTFS keeps them. It makes Administrators the owner of every entry it writes, as an elevated token may.
// The data folder: O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{agent}), which CreateDirectoryW fails to make
// with 183 when anything is there, so that only a folder judged by the agent's Others is ever applied to; audit\ and
// outcomes\ inherit it plus (A;OICI;0x1301bf;;;{agent}). The program folder:
// O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU).
[SupportedOSPlatform("windows")]
internal sealed class WindowsFolderSetup : IFolderSetup
{
    public FolderKindEnum Kind(string path)
    {
        throw new NotSupportedException();
    }

    public string FinalPath(string folder)
    {
        throw new NotSupportedException();
    }

    public void ApplyProgramFolder(string folder, string? source)
    {
        throw new NotSupportedException();
    }

    public void ApplyDataFolder(string folder, string agentSid, bool create)
    {
        throw new NotSupportedException();
    }

    public void OwnByAdministrators(string file)
    {
        throw new NotSupportedException();
    }
}
