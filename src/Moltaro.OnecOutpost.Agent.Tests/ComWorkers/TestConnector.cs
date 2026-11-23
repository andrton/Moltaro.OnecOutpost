using System.Runtime.Versioning;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// A class of the COM connector that a test registers for the current user, under HKEY_CURRENT_USER\Software\Classes,
// in the registry views it needs (the S9b brief, "The bitness"), and the finder that reads those classes in both views
// as the agent's finder reads HKLM's. Its ProgID and its class identifier are its own, so that no registration of the
// machine is touched. {ProgId}\CLSID gives the class identifier as its default value, a key that both views share. Each
// view of the registration gets the key InprocServer32 of the class, with a path as its default value, under CLSID in
// the view of 64 bits and under Wow6432Node\CLSID in that of 32 bits. No file is at the path, and nothing reads it.
// Disposing removes every key it made, in both views, and closes the keys of its finder.
[SupportedOSPlatform("windows")]
internal sealed class TestConnector : IDisposable
{
    private static readonly RegistryView[] Views = [RegistryView.Registry64, RegistryView.Registry32];

    private readonly string _class = Guid.NewGuid().ToString("B");

    // The current user's classes in the view of 64 bits and in that of 32 bits, which the finder reads.
    private readonly RegistryKey _classes64 = ClassesOf(RegistryView.Registry64);

    private readonly RegistryKey _classes32 = ClassesOf(RegistryView.Registry32);

    private TestConnector()
    {
        Finder = new ConnectorFinder(_classes64, _classes32, ProgId, []);
    }

    public string ProgId { get; } = $"Moltaro.OnecOutpost.TestConnector.{Guid.NewGuid():N}";

    // The finder of this class, for the launcher of a test.
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

    // Registers the class in one more view, as an installation of a connector of that bitness does.
    public void RegisterIn(RegistryView view)
    {
        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

        using (RegistryKey progId = user.CreateSubKey($@"Software\Classes\{ProgId}\CLSID"))
        {
            progId.SetValue("", _class);
        }

        using RegistryKey server = user.CreateSubKey($@"Software\Classes\CLSID\{_class}\InprocServer32");
        server.SetValue("", "comcntr.dll");
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
