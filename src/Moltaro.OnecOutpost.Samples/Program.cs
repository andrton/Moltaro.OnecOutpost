using System.Net;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server;
using Moltaro.Signals;

// An ASP.NET Core application that embeds the server on one node. Its store needs PostgreSQL: the connection string
// comes from OUTPOST_SAMPLE_POSTGRES, and without it the sample says so and ends.
string? connectionString = Environment.GetEnvironmentVariable("OUTPOST_SAMPLE_POSTGRES");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("The sample stops before AddOutpostServer: set OUTPOST_SAMPLE_POSTGRES to a PostgreSQL connection string.");
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");

// The application provides the ISignals of Moltaro.Signals: on one node, an in-process SignalHost.
builder.Services.AddSingleton<ISignals>(_ => new SignalHost(new SignalHostOptions
{
    ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
    Token = "sample-token",
}));

// A real application keeps its signing key in its own secret storage; the sample makes a new one at every run.
using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

// The server and the migrations name the same schema.
const string Schema = "outpost";

builder.Services.AddOutpostServer(options =>
{
    options.ConnectionString = connectionString;
    options.Schema = Schema;
    options.SigningKeyPem = signingKey.ExportPkcs8PrivateKeyPem();
});

await using WebApplication app = builder.Build();
string step = "MigrateAsync";

try
{
    await OutpostMigrations.MigrateAsync(connectionString, Schema);

    step = "MapOutpostAgents";
    app.MapOutpostAgents("/outpost");
    await app.StartAsync();

    IOutpostServer server = app.Services.GetRequiredService<IOutpostServer>();

    // The token itself is a credential for the administrator of the agent machine; its text leaves it out.
    step = "CreatePairingTokenAsync";
    OutpostPairingToken token = await server.CreatePairingTokenAsync("Sample agent");
    Console.WriteLine(token);

    // The agent of the token is pending until the console on the agent machine pairs with the token. Once paired, it
    // polls, and the list shows whether it is connected and which bases it declared.
    step = "GetAgentsAsync";
    IReadOnlyList<OutpostAgent> agents = await server.GetAgentsAsync();
    OutpostAgent created = agents.Single(agent => agent.Id == token.AgentId);
    Console.WriteLine($"The server knows {agents.Count} agents; the new one is {created.State}.");

    // A base key belongs to the first agent that declares it. Releasing it lets another agent take it, once the agent
    // that holds it no longer declares it; here no agent has declared it.
    step = "ReleaseBaseAsync";
    OutpostReleaseResultEnum released = await server.ReleaseBaseAsync("accounting");
    Console.WriteLine($"Releasing the base accounting: {released}.");

    // Revoking an agent refuses its requests from then on and frees its base keys; for a pending agent, it ends its
    // token, which the sample never gives an agent.
    step = "RevokeAgentAsync";
    bool revoked = await server.RevokeAgentAsync(token.AgentId);
    Console.WriteLine($"Revoking the new agent: {revoked}.");

    step = "ReadAsync";
    OutpostOutcome outcome = await server.ReadAsync(new OutpostReadRequest { BaseKey = "accounting", Script = "Result = 1 + 1;" });
    Console.WriteLine($"The read in the base accounting ended as {outcome.Kind} {outcome.Code}.");

    await app.StopAsync();
}
catch (NotSupportedException)
{
    Console.WriteLine($"The sample stops at {step}: it is not implemented yet.");
}
