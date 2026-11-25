using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Commands;

// The metadata report records each census, page or common Read with its outcome size, kind, code and error text.
// The category, first index and count are set for pages. Failed worker outcomes carry one of the codes checked by
// OutpostOutcomeCodes.IsComWorkerOutcome. The console adds two forms without outcome bytes: failed run-limit when
// the deadline passes and failed without a code when the worker did not reopen. The latter carries the opening error.
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
