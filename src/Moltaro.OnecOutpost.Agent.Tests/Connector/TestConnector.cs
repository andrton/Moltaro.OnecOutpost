using System.Runtime.Versioning;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Agent.Tests.Connector;

// A class of the COM connector that a test registers for the current user, under HKEY_CURRENT_USER\Software\Classes,
// in the registry views it needs (the S9b brief, "The bitness"), and the finder that reads those classes in both views
// as the agent's finder reads HKLM's. Its ProgID and its class identifier are its own, so that no registration of the
// machine is touched. {ProgId}\CLSID gives the class identifier as its default value, a key that both views share. Each
// view of the registration gets the key InprocServer32 of the class, with a path as its default value, under CLSID in
// the view of 64 bits and under Wow6432Node\CLSID in that of 32 bits. The path is that of a file that exists, the
// assembly of these tests, unless the test gives another; nothing loads the file. Disposing removes every key it made,
// in both views, and closes the keys of its finders.
[SupportedOSPlatform("windows")]
internal sealed class TestConnector : IDisposable
{
    private static readonly RegistryView[] Views = [RegistryView.Registry64, RegistryView.Registry32];

    private readonly string _class = Guid.NewGuid().ToString("B");

    // The current user's classes in the view of 64 bits and in that of 32 bits, which the finders read.
    private readonly RegistryKey _classes64 = ClassesOf(RegistryView.Registry64);

    private readonly RegistryKey _classes32 = ClassesOf(RegistryView.Registry32);

    private TestConnector()
    {
        Finder = FinderOver([]);
    }

    // A file that exists for as long as the tests run, which a registration names unless the test names another.
    public static string ExistingFile { get; } = typeof(TestConnector).Assembly.Location;

    public string ProgId { get; } = $"Moltaro.OnecOutpost.TestConnector.{Guid.NewGuid():N}";

    // The finder of this class, with no folders of the platform, for the launcher of a test.
    public ConnectorFinder Finder { get; }

    // Registers a class of its own in each of the views given; none registers nothing, and gives a ProgID that no class
    // has.
    public static TestConnector Register(params RegistryView[] views)
    {
        TestConnector connector = new();

        try
        {
            foreach (RegistryView view in views)
            {
                connector.RegisterIn(view);
            }
        }
        catch
        {
            connector.Dispose();
            throw;
        }

        return connector;
    }

    // The finder of this class with the folders of the platform given, which the connector's keys outlive.
    public ConnectorFinder FinderOver(IReadOnlyList<string> platformFolders)
    {
        return new ConnectorFinder(_classes64, _classes32, ProgId, platformFolders);
    }

    // Registers the class in one more view, as an installation of a connector of that bitness does, with the file that
    // exists.
    public void RegisterIn(RegistryView view)
    {
        RegisterIn(view, ExistingFile, RegistryValueKind.String);
    }

    // Registers the class in one more view with the path given, as a string or as a string that the registry expands,
    // replacing the path a registration in that view gave before.
    public void RegisterIn(RegistryView view, string path, RegistryValueKind kind)
    {
        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

        using (RegistryKey progId = user.CreateSubKey($@"Software\Classes\{ProgId}\CLSID"))
        {
            progId.SetValue("", _class);
        }

        using RegistryKey server = user.CreateSubKey($@"Software\Classes\CLSID\{_class}\InprocServer32");
        server.SetValue("", path, kind);
    }

    public void Dispose()
    {
        foreach (RegistryView view in Views)
        {
            using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

            user.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            user.DeleteSubKeyTree($@"Software\Classes\CLSID\{_class}", throwOnMissingSubKey: false);
        }

        _classes64.Dispose();
        _classes32.Dispose();
    }

    private static RegistryKey ClassesOf(RegistryView view)
    {
        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

        return user.CreateSubKey(@"Software\Classes");
    }
}
