namespace Moltaro.OnecOutpost.Server.Tests.Endpoints;

// How a request breaks a header that §4 of docs/protocol.md requires in it, each once and in its form.
public enum HeaderFaultEnum
{
    Missing,

    // The header comes on two lines, each with the right value.
    Repeated,

    // The header comes once, with the value in another form.
    OtherForm,
}
