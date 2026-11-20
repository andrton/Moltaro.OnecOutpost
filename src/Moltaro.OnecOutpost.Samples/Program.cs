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

builder.Services.AddOutpostServer(options =>
{
    options.ConnectionString = connectionString;
    options.SigningKeyPem = signingKey.ExportPkcs8PrivateKeyPem();
});

await using WebApplication app = builder.Build();
string step = "MigrateAsync";

try
{
    await OutpostMigrations.MigrateAsync(connectionString, "outpost");

    step = "MapOutpostAgents";
    app.MapOutpostAgents("/outpost");
    await app.StartAsync();

    IOutpostServer server = app.Services.GetRequiredService<IOutpostServer>();

    // The token itself is a credential for the administrator of the agent machine; its text leaves it out.
    step = "CreatePairingTokenAsync";
    OutpostPairingToken token = await server.CreatePairingTokenAsync("Sample agent");
    Console.WriteLine(token);

    step = "ReadAsync";
    OutpostOutcome outcome = await server.ReadAsync(new OutpostReadRequest { BaseKey = "accounting", Script = "Result = 1 + 1;" });
    Console.WriteLine($"The read in the base accounting ended as {outcome.Kind} {outcome.Code}.");

    await app.StopAsync();
}
catch (NotSupportedException)
{
    Console.WriteLine($"The sample stops at {step}: it is not implemented yet.");
}
