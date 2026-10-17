# Example clients

[Project README](../README.md) · [Documentation index](../docs/README.md) · [Complete usage guide](../docs/EXAMPLES.md)

Optional programs for calling the mod from your own applications. The plugin does not execute or require them. Load `REPO_API_TOKEN` using [the setup guide](../docs/GETTING_STARTED.md#load-your-token), then run commands from the repository root.

| Language | Files | Read-only start | One physical upgrade pickup |
|---|---|---|---|
| JavaScript, Node.js 22+ | [HTTP client](javascript/repo-api.mjs), [spawn helper](javascript/spawn.mjs) | `node examples/javascript/repo-api.mjs GET /health` | `node examples/javascript/spawn.mjs upgrade` |
| Python 3.10+ | [HTTP client](python/repo_api.py), [spawn helper](python/spawn.py) | `python3 examples/python/repo_api.py GET /health` | `python3 examples/python/spawn.py upgrade` |
| Bash/curl 7.76+ | [Request helper](curl/repo-api.sh) | `bash examples/curl/repo-api.sh GET /health` | [Catalog discovery recipe](../docs/EXAMPLES.md#bash-and-curl), requires jq |
| PowerShell 7+ | [Request helper](powershell/repo-api.ps1) | `pwsh -NoProfile -File examples/powershell/repo-api.ps1 -Method GET -Path /health` | [Invoke-RestMethod recipe](../docs/EXAMPLES.md#powershell-with-invoke-restmethod) |

## Shared behavior

- Default base URL: `http://127.0.0.1:17453`; override with `REPO_API_URL`.
- Authentication token: `REPO_API_TOKEN`. The clients do not print the bearer token.
- Client timeout: 15 seconds.
- Every POST gets a UUID idempotency key, printed to stderr before sending.
- Generic clients accept an explicit key when retrying the original request.
- No automatic retries, no background spawning, no package-manager dependencies for JavaScript or Python.
- Spawn helpers check readiness/capabilities, use live catalog IDs and default to one entity near a current player.

## Generic request syntax

```text
bash examples/curl/repo-api.sh GET|POST /path [JSON-or-@file] [idempotency-key]
node examples/javascript/repo-api.mjs GET|POST /path [JSON] [idempotency-key]
python3 examples/python/repo_api.py GET|POST /path [JSON] [idempotency-key]
pwsh -NoProfile -File examples/powershell/repo-api.ps1 -Method GET|POST -Path /path [-Body JSON] [-IdempotencyKey key]
```

The `GET|POST` notation means choose one method; it is not a literal shell command. Refer to [the endpoint recipes](../docs/EXAMPLES.md#every-endpoint-with-any-client) for the path and body for every operation.

## Spawn helper syntax

```text
node examples/javascript/spawn.mjs item|enemy|valuable|upgrade [search] [quantity]
python3 examples/python/spawn.py item|enemy|valuable|upgrade [search] [quantity]
```

To specify quantity with no search, pass an empty string, for example `python3 examples/python/spawn.py item "" 2`. Each invocation is a new action. If its result is uncertain, use a generic client with the original body and printed key rather than running discovery again. Details: [Retrying without duplicate spawns](../docs/EXAMPLES.md#retrying-without-duplicate-spawns).
