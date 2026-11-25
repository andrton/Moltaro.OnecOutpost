# Embedding the server

## A first session with the shell

**Status.** The shell session runs in automated tests with PostgreSQL and an agent with a
fake COM worker. Its run against real 1C is pending. The ASP.NET Core embedding section is
planned.

Use the shell to try one Read and one Write against the repository's test configuration.
You need .NET 10, a PostgreSQL database with UTF-8 encoding, and an agent whose local policy
opens the test base under the key `test` for Read and Write. Follow
[Installing and running an agent](agent.md) to prepare the agent machine.

Set `OUTPOST_SHELL_POSTGRES` in the shell's environment to the database connection
string. The database account needs permission to create and migrate the `outpost`
schema. Create a private directory for the signing key and substitute its path below.
From `src`, start the shell.

```text shell-start
dotnet run --project Moltaro.OnecOutpost.Shell -- --key-folder <private-key-dir> --listen http://127.0.0.1:0
```

The shell prints its agent URL after the migrations finish and the listener starts.
Port zero picks a free port on a numeric HTTP loopback address. Without `--listen` the
shell binds to `http://127.0.0.1:5000`. On subsequent starts, use the same directory so
the shell uses the signing key the agent pinned during pairing.

For an agent on another machine, set `--listen` to the concrete HTTPS origin on port
443 and give `--certificate` the path to its PFX. The agent machine must trust the
certificate for that host. Set `OUTPOST_SHELL_CERTIFICATE_PASSWORD` in the shell's
environment if the PFX requires one. Give the administrator the exact agent URL printed
by the shell, including the `/outpost` path.

In the shell, ask for a pairing token. The output includes the agent ID, the token and
its expiry. Send the token privately to the administrator and have them follow the
[pairing steps](agent.md#pairing) using the printed URL and token. Start the agent and
list it in the same shell.

```text shell
token Test base agent
```

```text shell
agents
```

Find the paired, connected agent and confirm that it holds `test` with `readOpen` and
`writeOpen` both true. Paste this Read as one block. The `.parameters` line starts its
JSON parameters and the `.` line submits the command. The shell shows `Succeeded` and
the result object with `description` set to `Test product`.

```text shell
read test
Result = New Structure("description", Parameters["description"]);
.parameters
{
  "description": "Test product"
}
.
```

Paste this Write to add one product to the test catalog. The shell shows `Succeeded`
and the description it wrote. Paste the same block again to see the retained outcome
with the same command ID. Use a new caller key when you mean to create another product.

```text shell
write test first-shell-product
Product = Catalogs.Products.CreateItem();
Product.Description = Parameters["description"];
Product.Write();
Result = New Structure("description", Product.Description);
.parameters
{
  "description": "Test product"
}
.
```

End input to stop the shell. In a terminal, use Ctrl+D on Unix or Ctrl+Z followed by
Enter on Windows. Ctrl+C cancels the shell's wait. A submitted command can still run.

### When the session fails

| What you see | What to check |
| --- | --- |
| Usage error | Check the startup options or the command header. End a Read or Write with a line containing only `.` and give `.parameters` a JSON object when you use it. |
| Infrastructure failure | Check the PostgreSQL connection and migration permissions, the signing key directory and the listener settings. Keep any passwords in the environment. |
| `Unavailable` | Start the agent and check that it is polling the printed URL. |
| `Refused` | Check the outcome code, the base key and the modes the local policy opens. For `caller-key-conflict`, use the script and parameters of the first call to retrieve its outcome. |
| `Expired` | The command did not reach the agent in time. Check the agent's connection and try again. |
| `Failed` | Read the outcome code and the error text from 1C. A failed Write wrote nothing. |
| `Unknown` | Repeat the identical Write with the same caller key to read its retained outcome. Check what it did in the base before submitting a new Write. |

See [Writing scripts](scripts.md) for the script contract, outcome codes and more
examples on the test configuration.
