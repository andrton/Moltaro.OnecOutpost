namespace Moltaro.OnecOutpost.Console.Commands;

// The one script that metadata runs in a base as Reads, as CheckScripts holds check's, in the English variant of the 1C
// language and within what the compatibility mode 8.2.13 allows, without a procedure, Find and Mid where a later mode
// has StrFind and StrSplit. Its parameter read chooses what it reads: census, the configuration and the count of each of
// its fifteen categories; page, the objects of a category from an index with their fields and, when asked, their
// storage by GetDBStorageStructureInfo with the DBMS's names; common, the storage tables of no object of those
// categories. The S10 brief, "Internal of S10c", gives its outline and the form of what it gives.
internal static class MetadataScripts
{
    // Written by the implementation of S10c; no command runs it before then.
    public const string Walk = "";
}
