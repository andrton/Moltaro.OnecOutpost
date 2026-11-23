using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// What the boundaries of Windows answer without changing anything, which needs no administrator, so with
// Requires=Windows alone (the S10 brief, "WindowsAccountRights", "WindowsFolderSetup" and "WindowsServiceControl"):
// UserSid names only a local user, so a name that Windows does not know and a group give null; FinalPath names a folder
// as Windows does, however it is spelled, and one that does not exist by its full path; Kind tells a folder from nothing
// and counts a file as nothing, so that a folder is never assumed in its place; a stop of a service that does not exist
// is NotInstalled.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WindowsLookupTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose()
    {
        _folder.Dispose();
    }

    // BUILTIN\Users by the name Windows gives it in the language of the machine.
    [Fact]
    public void UserSid_gives_null_for_an_account_that_Windows_does_not_know_and_for_a_group()
    {
        WindowsAccountRights rights = new();
        string group = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Translate(typeof(NTAccount)).Value;

        Assert.Null(rights.UserSid($@"{Environment.MachineName}\outpost-{Guid.NewGuid():N}"));
        Assert.Null(rights.UserSid(group));
    }

    [Fact]
    public void FinalPath_names_a_folder_alike_however_it_is_spelled_and_an_absent_one_by_its_full_path()
    {
        WindowsFolderSetup folders = new();
        string folder = Directory.CreateDirectory(_folder.PathOf("program")).FullName;
        string absent = _folder.PathOf("absent");

        string final = folders.FinalPath(folder + Path.DirectorySeparatorChar);

        Assert.Equal(final, folders.FinalPath(folder));
        Assert.DoesNotContain("?", final, StringComparison.Ordinal);
        Assert.Equal("program", Path.GetFileName(final));
        Assert.True(Directory.Exists(final));
        Assert.Equal(absent, folders.FinalPath(absent + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Kind_tells_a_folder_from_nothing_and_counts_a_file_as_nothing()
    {
        WindowsFolderSetup folders = new();
        string file = _folder.PathOf("policy.json");
        File.WriteAllText(file, "{}");

        Assert.Equal(FolderKindEnum.Folder, folders.Kind(_folder.Path));
        Assert.Equal(FolderKindEnum.Absent, folders.Kind(_folder.PathOf("absent")));
        Assert.Equal(FolderKindEnum.Absent, folders.Kind(file));
    }

    [Fact]
    public async Task A_stop_of_a_service_that_does_not_exist_is_NotInstalled()
    {
        WindowsServiceControl services = new(new FakeClock(TestTimes.Start));

        ServiceStopEnum stop = await TestTimes.EndOf(services.StopAsync(
            $"MoltaroOnecOutpostTest-{Guid.NewGuid():N}",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));

        Assert.Equal(ServiceStopEnum.NotInstalled, stop);
    }
}
