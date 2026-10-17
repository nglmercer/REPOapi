# R.E.P.O. REST API

Control R.E.P.O. through a local, authenticated REST API. This standalone MIT-licensed **BepInEx 5** plugin uses **REPOLib 4.2.0** to discover and spawn game content. The game-side plugin requires no Node.js, Python or Bun runtime.

## Documentation index

| Guide | Contents |
|---|---|
| [All documentation](docs/README.md) | Start here to browse every guide and language client |
| [Getting started](docs/GETTING_STARTED.md) | Install on Windows or Steam/Proton, load your token, spawn your first item |
| [Examples in popular languages](docs/EXAMPLES.md) | curl, JavaScript fetch, Python and PowerShell, with endpoint recipes |
| [API reference](docs/API.md) | All routes, payloads, responses, positioning and error codes |
| [OpenAPI 3.0.3](docs/openapi.json) | Machine-readable contract for all 21 endpoints |
| [Configuration](docs/CONFIGURATION.md) | BepInEx settings, defaults, limits and authentication |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Installation, empty catalogs, HTTP failures and safe placement |
| [Architecture](docs/ARCHITECTURE.md) | Unity thread execution, networking, idempotency and lifecycle |
| [Compatibility](docs/COMPATIBILITY.md) | Dependency versions, verified APIs and feature limitations |
| [Testing status](docs/TESTING.md) | Tests completed and manual checks still outstanding |

## Quick start

1. Install [BepInExPack 5.4.2305](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) and extract [the release ZIP](artifacts/RepoRestApi-1.0.0.zip) into the game directory or mod profile. REPOLib and Newtonsoft.Json are bundled; keep only one REPOLib copy in your profile.
2. Launch R.E.P.O. The plugin creates `BepInEx/config/org.repo.rest-api.cfg`. Load its `AuthenticationToken` as described in [Getting started](docs/GETTING_STARTED.md#load-your-token).
3. Check the API:

```bash
curl -sS -H "Authorization: Bearer $REPO_API_TOKEN" \
  http://127.0.0.1:17453/health
```

4. Host or start singleplayer and enter a generated gameplay level. From this repository, choose a spawn example:

```bash
# JavaScript: one health item, selected from the current catalog.
node examples/javascript/spawn.mjs item health

# Python: one available physical upgrade pickup.
python3 examples/python/spawn.py upgrade
```

Run only the action you want. The examples use live catalog IDs and display the selected object. See equivalent [curl and PowerShell commands](docs/EXAMPLES.md).

**Linux/CachyOS/Steam Deck:** follow the [Proton loader instructions](docs/GETTING_STARTED.md#linux-cachyos-and-steam-deck). The default API address is `http://127.0.0.1:17453`; every route requires the bearer token by default.

## What the mod supports

- Health, game state, dependency versions and capabilities.
- Dynamic item, enemy, valuable and physical upgrade catalogs, including compatible REPOLib registrations.
- Host-only network spawning with configurable quantities and validated positions.
- Current player listing and public heal/revive method invocation.
- Enemy listing and removal restricted to enemies created through this API.
- Bounded Unity main-thread dispatch, authentication, rate/request limits and idempotency keys.

**Current limits:** teleport and direct upgrade grants are unavailable. Upgrades spawn as physical pickups. Heal/revive report `invoked`, with no owner effect acknowledgement. Multi-object enemy setups are excluded. Check `/api/capabilities` before integrating actions.

**Validation:** the plugin builds against both published game references and actual R.E.P.O. v0.4.4.3 assemblies. The existing core suite has 41 passing cases. Actual game probes confirmed startup, catalogs, authentication, shutdown and one item spawn reported by the host API. Physical usability, enemy behavior and second-client replication still need manual checks; [the testing record](docs/TESTING.md) states the evidence and limits.

## Build and package

Requires .NET 8 SDK on the development machine:

```bash
dotnet build RepoRestApi.sln -c Release
dotnet test tests/RepoRestApi.Tests -c Release
bash scripts/package.sh
```

The ZIP contains the plugin, core, REPOLib, Newtonsoft.Json, documentation, example clients and licenses. It excludes proprietary game/Unity assemblies and the BepInEx framework. Packaging uses Bash and zip, with Python 3 as an archive-building fallback. `DOTNET=/path/to/dotnet` selects an SDK executable.

To compile against your installed game assemblies:

```bash
dotnet build src/RepoRestApi -c Release \
  -p:GameManagedPath="/path/to/REPO/REPO_Data/Managed"
```

Dependency pins and API verification are documented in [Compatibility](docs/COMPATIBILITY.md). Client examples are in [examples/](examples/README.md). The REST API operates independently of TikTools-app; that integration is outside the current implementation.

## License

[MIT](LICENSE). Bundled dependencies retain their [REPOLib](third-party/REPOLib.LICENSE.txt) and [Newtonsoft.Json](third-party/Newtonsoft.Json.LICENSE.md) notices. R.E.P.O., Unity and BepInEx are separate prerequisites.
