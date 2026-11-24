namespace Moltaro.OnecOutpost.Console.Commands;

// The lines of check, in the order of the stage contract, in which its table shows them.
internal enum CheckLineEnum
{
    // The COM connector and its version.
    Connector,

    // The connection to a base under one of its accounts.
    Connection,

    // The event subscriptions of a base with write whose common module is not available to an external connection.
    Subscriptions,

    // The rights to change data that the read account of a base holds.
    ReadRights,

    // The server, by an outcome of a made-up command.
    Server,

    // The skew between this machine's clock and the server's.
    ClockSkew,

    // The logon of a base's worker account and the rule of its token.
    WorkerAccount,

    // The two privileges of the agent's account.
    AgentRights,

    // A start of the COM worker under a worker account on its desktop, which opens no base.
    WorkerStart,

    // The access lists of the data folder and its files, of the program folder and of each registered connector's
    // folder.
    AccessLists,

    // policy.json, agent.json and secrets.dat.
    Files,
}
