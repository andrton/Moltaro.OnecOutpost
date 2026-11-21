namespace Moltaro.OnecOutpost.ComWorker;

// The boundary of the COM connection: it opens a session in one base. ComConnector is the implementation of the
// program and the only one that touches COM; the automated tests give Worker a fake of their own instead.
internal interface IOneCConnector
{
    // Opens a session in the base under the account given. The location is the part of a 1C connection string that
    // names the base, for a cluster or for a file base; the connector adds the user and the password to it, quoted as
    // 1C wants. It throws when no session opens. The text of that failure may quote the password, since 1C or .NET may
    // quote a value it refused, so Worker never writes it as it is.
    IOneCSession Connect(string location, string user, string password);
}
