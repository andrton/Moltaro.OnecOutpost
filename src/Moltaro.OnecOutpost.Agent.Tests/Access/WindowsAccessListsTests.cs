using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Access;

// The access lists of the data folder as Windows keeps them ("The files of the agent", the S6a brief): the accounts other
// than Administrators, SYSTEM and the agent's own that own an entry, or that an allow entry applying to the entry itself
// gives any right, named as Windows translates their SID or by the SID. Each test makes a data folder of its own in its
// temporary folder, with the access list that the console's install gives the data folder; the account that runs the
// tests is the agent's, as WindowsIdentity.GetCurrent() says.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WindowsAccessListsTests : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    private readonly string _data;

    public WindowsAccessListsTests()
    {
        _data = _temporary.PathOf("data");
        Directory.CreateDirectory(_data);
        AccessListEdits.AdmitNobodyElse(_data);
        Directory.CreateDirectory(Path.Combine(_data, "audit"));
        File.WriteAllText(Path.Combine(_data, "audit", "2026-11-22.jsonl"), "");
        File.WriteAllText(Path.Combine(_data, "policy.json"), "{}");
    }

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact]
    public void A_data_folder_as_the_install_sets_it_admits_nobody_else()
    {
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Empty(access.Others("").All);
        Assert.Empty(access.Others("audit").All);
        Assert.Empty(access.Others(@"audit\2026-11-22.jsonl").All);
        Assert.Empty(access.Others("policy.json").All);
    }

    // A local user could plant a policy, a pinned key or an outcome in a file that admits him.
    [Fact]
    public void A_file_whose_access_list_admits_anybody_else_names_that_account()
    {
        AccessListEdits.Allow(Path.Combine(_data, "policy.json"), AccessListEdits.Everyone);
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Equal([AccessListEdits.NameOf(AccessListEdits.Everyone)], access.Others("policy.json").All);
        Assert.Empty(access.Others("").All);
    }

    [Fact]
    public void A_folder_whose_access_list_admits_anybody_else_names_that_account()
    {
        AccessListEdits.Allow(Path.Combine(_data, "audit"), AccessListEdits.Everyone);
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Equal([AccessListEdits.NameOf(AccessListEdits.Everyone)], access.Others("audit").All);
    }

    // An account that Windows cannot name, such as one of a domain that no longer exists, is named by its SID.
    [Fact]
    public void An_account_that_Windows_cannot_translate_is_named_by_its_SID()
    {
        SecurityIdentifier unknown = new("S-1-5-21-1-2-3-1001");
        AccessListEdits.Allow(Path.Combine(_data, "policy.json"), unknown);
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Equal(["S-1-5-21-1-2-3-1001"], access.Others("policy.json").All);
    }

    // A deny entry gives no right.
    [Fact]
    public void A_deny_entry_admits_nobody()
    {
        AccessListEdits.Deny(Path.Combine(_data, "policy.json"), AccessListEdits.Everyone);
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Empty(access.Others("policy.json").All);
    }

    // An entry that applies only to what is created inside the folder does not apply to the folder itself; a file
    // created afterwards inherits it, and then it applies to that file.
    [Fact]
    public void An_entry_that_applies_only_inside_a_folder_admits_nobody_to_the_folder_itself()
    {
        string outcomes = Path.Combine(_data, "outcomes");
        Directory.CreateDirectory(outcomes);
        AccessListEdits.AllowInsideOnly(outcomes, AccessListEdits.Everyone);
        File.WriteAllText(Path.Combine(outcomes, "planted.json"), "{}");
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.Empty(access.Others("outcomes").All);
        Assert.Equal([AccessListEdits.NameOf(AccessListEdits.Everyone)], access.Others(@"outcomes\planted.json").All);
    }

    [Fact]
    public void The_list_of_an_entry_that_does_not_exist_cannot_be_read_and_fails_with_IOException()
    {
        WindowsAccessLists access = new(_data, AccessListEdits.TestAccount);

        Assert.ThrowsAny<IOException>(() => access.Others("secrets.dat").All);
    }
}
