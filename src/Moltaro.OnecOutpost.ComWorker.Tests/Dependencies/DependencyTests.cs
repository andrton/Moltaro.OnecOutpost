using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Dependencies;

// The COM worker depends on nothing outside .NET (principle 12): it reads and writes its lines with System.Text.Json and
// shares no type with the protocol assembly.
[Trait("Requires", "Windows")]
public sealed class DependencyTests
{
    // An assembly of .NET is one of the shared framework this test runs on. A package, another shared framework such as
    // ASP.NET Core, or another project of the repository is not there.
    [Fact]
    public void The_COM_worker_references_only_assemblies_of_dotnet()
    {
        string frameworkDirectory = RuntimeEnvironment.GetRuntimeDirectory();

        string[] others = typeof(Worker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(frameworkDirectory, name + ".dll")))
            .ToArray();

        Assert.Empty(others);
    }
}
