using Testcontainers.PostgreSql;

namespace Moltaro.OnecOutpost.TestSupport;

// A real PostgreSQL in a container, shared by the tests of a class that carries the trait Requires=PostgreSQL. The
// container starts when a test first asks for its connection, so a class whose tests are all skipped starts none, and
// it is removed when the fixture is disposed. Each test works in a schema of its own.
public sealed class PostgreSqlFixture : IAsyncDisposable
{
    // Pinned, so that every run tests against the same server.
    private const string Image = "postgres:17";

    // Started once, without the token of the test that asked first, so that its cancellation ends only its own wait.
    private readonly Lazy<Task<PostgreSqlContainer>> _container = new(StartAsync);

    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken)
    {
        PostgreSqlContainer container = await _container.Value.WaitAsync(cancellationToken);

        return container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_container.IsValueCreated)
        {
            return;
        }

        Task<PostgreSqlContainer> started = _container.Value;

        // A start that failed has removed its container, and its failure reached the test that waited for it.
        await ((Task)started).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (started.IsCompletedSuccessfully)
        {
            await started.Result.DisposeAsync();
        }
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

        try
        {
            await container.StartAsync();
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return container;
    }
}
