using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Protocol.Tests.Dependencies;

public sealed class DependencyTests
{
    // An assembly of .NET is one of the shared framework this test runs on. A package, another shared framework such as
    // ASP.NET Core, or another project of the repository is not there.
    [Fact]
    public void The_protocol_references_only_assemblies_of_dotnet()
    {
        string frameworkDirectory = RuntimeEnvironment.GetRuntimeDirectory();

        string[] others = typeof(OutpostProtocol).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(frameworkDirectory, name + ".dll")))
            .ToArray();

        Assert.Empty(others);
    }
}
