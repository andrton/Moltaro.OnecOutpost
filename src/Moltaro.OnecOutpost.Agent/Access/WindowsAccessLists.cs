using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Access;

// The access lists of the data folder as Windows keeps them, read with FileSystemAclExtensions. Each account is named
// as Windows translates its SID, or by the SID; the agent's own account is WindowsIdentity.GetCurrent(), which is
// NT SERVICE\MoltaroOnecOutpost as a service. An administrator's own account, as the owner of a file or in an entry,
// counts as somebody else, so a file that an administrator's account owns stops the agent until Administrators own it.
[SupportedOSPlatform("windows")]
internal sealed class WindowsAccessLists : IAccessLists
{
    // The full path of the data folder, against which the paths given to Others are taken.
    private readonly string _dataFolder;

    public WindowsAccessLists(string dataFolder)
    {
        _dataFolder = dataFolder;
    }

    public IReadOnlyList<string> Others(string path)
    {
        throw new NotSupportedException();
    }
}
