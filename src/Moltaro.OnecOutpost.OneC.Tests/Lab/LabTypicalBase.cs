namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// A copy of a typical base on the lab's cluster, as the owner set it up: its configuration and version, the accounts of
// its two modes, the document type whose latest posted document a test posts again, and the safe mode of its Writes,
// false for a typical configuration that does not post in safe mode (concept §12, O1). Nothing of it enters the
// repository.
internal sealed class LabTypicalBase
{
    // The key of a copy in the tests' policies and commands.
    public const string Key = "typical";

    // The address of the cluster, such as localhost:1541.
    public required string Server { get; init; }

    public required string Database { get; init; }

    public required string Configuration { get; init; }

    public required string Version { get; init; }

    public required LabAccount Read { get; init; }

    public required LabAccount Write { get; init; }

    // The name of a document type of the configuration, as its metadata has it. The owner names a type whose posting
    // depends on no sequence and no later document, in a period that no closing date of changes covers.
    public required string DocumentType { get; init; }

    public required bool WriteSafeMode { get; init; }
}
