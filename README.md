# R.E.P.O. REST API

Standalone MIT-licensed BepInEx 5 plugin exposing authenticated local HTTP control of R.E.P.O. Uses REPOLib 4.2.0 for network spawning. No TikTools-app changes or runtime dependency on Node.js, Python or Bun.

**Validation status:** builds against both the published 0.4.4 reference package and the locally installed 0.4.4.3 game assemblies. HTTP/core tests run outside Unity. In-game spawning and multiplayer replication are not certified; see [testing status](docs/TESTING.md). Read `/api/capabilities` before invoking actions.

## Features

- Local asynchronous HTTP server, strong generated bearer token, request size/rate/concurrency limits.
- Bounded Unity dispatcher with deadlines, per-frame limits, session-change rejection and shutdown cancellation.
- Dynamic REPOLib item, valuable, enemy and physical upgrade catalogs, including compatible registered mod content.
- Host-only network spawning with conservative collision bounds, floor checks and enemy NavMesh validation.
- Current player listing; public heal/revive method invocation. API-owned enemy tracking and removal.
- Required idempotency keys for mutations, structured errors and embedded [OpenAPI 3.0](docs/openapi.json).

Teleport is explicitly unavailable. Heal/revive responses confirm invocation, not the effect on the owning client. Multi-object enemy setups are excluded because REPOLib spawns their members at the same origin. Upgrade actions spawn physical pickups; direct upgrade grants are unavailable.

## Install

1. Install [BepInExPack 5.4.2305](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) (BepInEx 5.4.23.5, Mono x64). Do not use BepInEx 6/IL2CPP.
2. Extract `artifacts/RepoRestApi-1.0.0.zip` into the game directory or mod profile. The package contains `BepInEx/plugins/RepoRestApi/{RepoRestApi.dll,RepoRestApi.Core.dll,Newtonsoft.Json.dll}` and `BepInEx/plugins/REPOLib/REPOLib.dll` (4.2.0). If your profile already manages REPOLib, keep one copy of the required version; do not install duplicate copies.
3. Launch through your mod manager. For Steam/Proton, configure the BepInEx loader as instructed by your manager; manual installations normally need `WINEDLLOVERRIDES="winhttp=n,b" %command%` in Steam launch options.
4. Find `REST server listening on 127.0.0.1:17453` in `BepInEx/LogOutput.log`. The first run generates `BepInEx/config/org.repo.rest-api.cfg` containing `AuthenticationToken`. Read it privately; it is never printed in normal plugin logs.
5. Set `REPO_API_TOKEN` locally from that setting and call health:

```bash
curl -H "Authorization: Bearer $REPO_API_TOKEN" http://127.0.0.1:17453/health
```

Health can return HTTP 200 before a gameplay level is available; check `inLevel`, `isHost` and `gameplayReady`. Requests are dispatched through Unity, so a paused/stalled game loop can return 504.

## Configuration

BepInEx generates these settings; changes require a game restart. Protect the configuration file because it contains a bearer credential.

| Section | Key | Default |
|---|---|---|
| HTTP | Enabled | true |
| HTTP | Host | 127.0.0.1 |
| HTTP | Port | 17453 |
| HTTP | RequireAuth | true |
| HTTP | AllowRemote | false |
| HTTP | AuthenticationToken | generated 32 random bytes, Base64 encoded |
| Limits | MaxRequestsPerSecond | 20 |
| Limits | MaxSpawnQuantity | 10 |
| Limits | MaxRequestBytes | 16384 |
| Limits | QueueCapacity | 64 |
| Limits | RequestTimeoutMs | 5000 |
| Limits | CommandsPerFrame | 2 |
| Diagnostics | Verbose | false |

Host must be a literal IP. Remote binding requires `AllowRemote=true`, authentication enabled and a token of at least 43 characters. The server uses HTTP; restrict remote access to a trusted network/VPN or a TLS reverse proxy. There is no CORS support, arbitrary command execution or general RPC endpoint.

## Build and test

Requires .NET 8 SDK. Runtime requires only the game's Unity/Mono environment and BepInEx dependencies.

```bash
dotnet build RepoRestApi.sln -c Release
dotnet test tests/RepoRestApi.Tests -c Release
bash scripts/package.sh
```

`package.sh` uses Bash and zip (or Python 3 as an archive-building fallback) on the development machine. The game plugin never launches an external runtime. `DOTNET=/path/to/dotnet` selects a specific SDK. It packages only the plugin, core, Newtonsoft.Json, REPOLib, licenses and documentation; proprietary game assemblies, Unity assemblies and BepInEx framework files are excluded.

To compile against your actual installation without publicized game reference assemblies:

```bash
dotnet build src/RepoRestApi -c Release -p:GameManagedPath="/path/to/REPO/REPO_Data/Managed"
```

The default build pins `R.E.P.O.GameLibs.Steam` 0.4.4-ngd.0, UnityEngine.Modules 2022.3.62, REPOLib 4.2.0, BepInEx.Core 5.4.21 (the API baseline REPOLib uses) and Newtonsoft.Json 13.0.4. No private access publicizer or Harmony patch is used by this plugin.

See [API usage](docs/API.md), [compatibility evidence](docs/COMPATIBILITY.md), [architecture](docs/ARCHITECTURE.md) and [testing](docs/TESTING.md).

## Troubleshooting

- **No listener:** check plugin load errors, dependency version, Enabled, port conflicts, literal IP and configuration limits. Binding failures are logged without the token.
- **401:** use the current private token from this profile's configuration. Every endpoint, including OpenAPI and health, requires authentication by default.
- **409:** host a generated playable level; lobbies, menus, shops and transitions do not permit mutations. Refresh player/catalog IDs after scene/session/authority changes.
- **422:** placement is deliberately conservative; try another player/location. Unsupported collider shapes and occupied world positions are rejected without spawning.
- **504:** command expired in the queue and did not execute. Keep the same idempotency key to retrieve that outcome; a genuinely new attempt needs a new key. A command already executing returns its actual outcome even if it finishes after the queue deadline.
- **500 SPAWN_FAILED:** inspect `error.details.spawned` and the BepInEx log. A batch can partially complete; the cached result prevents automatic duplicate retries.
- **501:** the capability is unavailable; this is intentional and is not a successful no-op.

## License

MIT. Bundled REPOLib and Newtonsoft.Json have their own MIT notices in `third-party/`. R.E.P.O., Unity and BepInEx are separate prerequisites and are not included as game/framework redistributions.
