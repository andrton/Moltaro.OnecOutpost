namespace Moltaro.OnecOutpost.TestSupport.Metadata;

// One Read of the walk as the report of the console's metadata records it in reads (the S10 brief, "The report"): what
// it read, census, page or common, the category, the index and the count of a page, null for the others, the bytes of
// its outcome, 0 for one that gave none within the run limit or never ran, and its kind, code and error text.
internal sealed record MetadataRead(
    string Read,
    string? Category,
    int? From,
    int? Count,
    long Size,
    string Kind,
    string? Code,
    string? ErrorText);
