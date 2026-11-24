namespace Moltaro.OnecOutpost.TestSupport.Metadata;

// One category of the report of the console's metadata, or one row of the table that it writes (the S10 brief, "The
// report"): how many objects the census found in it, how many were read without an error, and how many failed, with an
// error of their own or in a page that failed.
internal sealed record MetadataCategory(string Category, int Found, int Read, int Failed);
