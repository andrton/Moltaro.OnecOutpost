using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Commands;

// One Read of the walk of metadata, as the report records it in reads (the S10 brief, "The report"): census, page or
// common; the category, the first index and the count of a page, null for the others; the bytes of its outcome; and its
// kind, code and error text. Two forms are the console's own, which no outcome of the COM worker takes, since a failed
// one always has a code and never run-limit (OutpostOutcomeCodes.IsComWorkerOutcome): a Read with no outcome within
// the run limit, failed run-limit with no bytes; and a Read never run after a session did not reopen, failed without a
// code and without bytes, the reason of that open as its error text.
internal sealed record WalkRead(
    string Read,
    string? Category,
    int? From,
    int? Count,
    long Size,
    OutpostAgentOutcomeKindEnum Kind,
    string? Code,
    string? ErrorText)
{
    public bool Succeeded => Kind == OutpostAgentOutcomeKindEnum.Succeeded;

    // The page did not fit: result-too-large from the executor, or no outcome within the run limit, after which a page
    // of more than one object is read again as two halves.
    public bool DoesNotFit => Code is OutpostOutcomeCodes.ResultTooLarge or OutpostOutcomeCodes.RunLimit;

    // How the Read ended, in the words of metadata's sentences: was not read, with the reason of the open; did not end
    // within the run limit; or ended with its kind, its code and its error text.
    public string Ended(TimeSpan runLimit)
    {
        if (Kind == OutpostAgentOutcomeKindEnum.Failed && Code is null)
        {
            return $"was not read: {ErrorText}";
        }

        if (Code == OutpostOutcomeCodes.RunLimit)
        {
            return $"did not end within {runLimit:c}";
        }

        string ended = $"ended {OutpostForms.WriteOutcomeKind(Kind)}";

        if (Code is not null)
        {
            ended = $"{ended} {Code}";
        }

        return ErrorText is null ? ended : $"{ended}: {ErrorText}";
    }
}
