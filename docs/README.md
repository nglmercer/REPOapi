# Documentation index

[Project README](../README.md) · [Runnable examples](../examples/README.md)

Start with **[Getting started](GETTING_STARTED.md)** to install the mod, load your token and make the first request. Then follow **[Examples](EXAMPLES.md)** in your preferred language.

| Document | What you will find |
|---|---|
| [Getting started](GETTING_STARTED.md) | Windows and Linux/Steam/Proton installation, authentication, health and the first item/upgrade spawn |
| [Examples](EXAMPLES.md) | curl, JavaScript fetch, Python and PowerShell; every endpoint, positioning and retries |
| [API reference](API.md) | Request/response contracts, game restrictions, catalogs, status codes and ownership |
| [OpenAPI specification](openapi.json) | OpenAPI 3.0.3 schema for tools and generated clients; also served by authenticated `/openapi.json` |
| [Configuration](CONFIGURATION.md) | Every BepInEx setting, defaults, limits and remote binding |
| [Troubleshooting](TROUBLESHOOTING.md) | Loader problems, empty catalogs, authentication, placement and request errors |
| [Architecture](ARCHITECTURE.md) | HTTP lifecycle, Unity thread dispatch, session invalidation and entity tracking |
| [Compatibility research](COMPATIBILITY.md) | Verified dependency versions, actual game signatures, source evidence and unavailable features |
| [Testing status](TESTING.md) | Automated results, actual game observations and outstanding manual/multiplayer checks |

## Choose a client

| Language/tool | HTTP implementation | Files |
|---|---|---|
| JavaScript | Node.js built-in `fetch` | [Client](../examples/javascript/repo-api.mjs), [catalog-driven spawn](../examples/javascript/spawn.mjs) |
| Python | Standard-library `urllib.request` | [Client](../examples/python/repo_api.py), [catalog-driven spawn](../examples/python/spawn.py) |
| Bash/curl | curl | [Request helper](../examples/curl/repo-api.sh) |
| PowerShell | `Invoke-RestMethod` | [Request helper](../examples/powershell/repo-api.ps1) |

These programs are optional API consumers. The C# game plugin runs without any of these external runtimes.
