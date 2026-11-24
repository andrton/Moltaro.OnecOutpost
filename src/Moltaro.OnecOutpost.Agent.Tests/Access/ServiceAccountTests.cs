using Moltaro.OnecOutpost.Agent.Access;

namespace Moltaro.OnecOutpost.Agent.Tests.Access;

// The account NT SERVICE\{name} that Windows gives a service (the S10 brief, "ServiceAccount.Sid", and the S10b brief,
// "Names"): its name, spelled once, and its SID, S-1-5-80- and the SHA-1 of the upper-cased name in UTF-16 as five
// sub-authorities, as Windows derives it, computed without Windows, so on every platform. That Windows translates the
// account of a service it created to this SID is a test of the console's with Requires=Administrator.
public sealed class ServiceAccountTests
{
    // The SID that Windows gives NT SERVICE\TrustedInstaller, whose name is not all in upper case.
    [Fact]
    public void The_SID_of_a_service_account_is_the_one_Windows_derives_from_the_name_of_the_service()
    {
        Assert.Equal("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", ServiceAccount.Sid("TrustedInstaller"));
    }

    [Fact]
    public void The_name_of_a_service_account_is_NT_SERVICE_and_the_name_of_the_service()
    {
        Assert.Equal(@"NT SERVICE\MoltaroOnecOutpost", ServiceAccount.Name("MoltaroOnecOutpost"));
    }
}
