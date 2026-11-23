namespace Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

// The fake of the COM connection behind the worker's boundary: it records what the worker connects with, and opens its
// one session, or throws Failure as a session that does not open.
internal sealed class FakeConnector(FakeSession session) : IOneCConnector
{
    public Exception? Failure { get; init; }

    public List<(string Location, string User, string Password)> Connections { get; } = [];

    public IOneCSession Connect(string location, string user, string password)
    {
        Connections.Add((location, user, password));

        if (Failure is not null)
        {
            throw Failure;
        }

        return session;
    }
}
