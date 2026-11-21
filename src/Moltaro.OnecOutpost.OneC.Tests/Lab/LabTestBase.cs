namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// The base built from the repository's test configuration on the lab's cluster, with the users outpost-read and
// outpost-write that Build-TestBase.ps1 creates.
internal sealed class LabTestBase
{
    // The address of the cluster, such as localhost:1541.
    public required string Server { get; init; }

    public required string Database { get; init; }

    public required LabAccount Read { get; init; }

    public required LabAccount Write { get; init; }
}
