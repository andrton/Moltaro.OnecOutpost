namespace Moltaro.OnecOutpost.Protocol.Outcomes;

// The kinds of the outcome object that an agent sends (§8 of docs/protocol.md): `unknown`, `succeeded`, `failed` and
// `refused`. Only the server sets Expired and Unavailable, so they are not here. Unknown is the default, since it
// claims nothing.
internal enum OutpostAgentOutcomeKindEnum
{
    Unknown = 0,
    Succeeded,
    Failed,
    Refused,
}
