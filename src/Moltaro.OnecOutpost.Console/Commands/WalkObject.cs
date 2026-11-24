using System.Text.Json;

namespace Moltaro.OnecOutpost.Console.Commands;

// One object of the report of metadata (the S10 brief, "The report"): the category of the page that read it, the object
// as the page gave it, and its error: the page's own, or storage: {storageError} for one without an error in a page
// whose storage raised; null for an object read.
internal sealed record WalkObject(string Category, JsonElement Item, string? Error);
