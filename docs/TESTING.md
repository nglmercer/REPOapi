# Testing status

[Documentation index](README.md) · [API reference](API.md) · [Examples](EXAMPLES.md)

Recorded 2026-10-17. This document distinguishes tested infrastructure from unverified gameplay. Desktop mocks do not establish Unity or multiplayer correctness.

## Automated checks completed

- Release build of the solution: passed, zero warnings/errors.
- Plugin build with `GameManagedPath` pointing to the installed R.E.P.O. managed assemblies: passed, zero warnings/errors. No publicizer/Harmony access workaround is needed.
- `dotnet test tests/RepoRestApi.Tests -c Release`: 41 cases passed.
- OpenAPI 3.0.3 specification: passed openapi-spec-validator, all 21 paths.
- Release ZIP integrity and exact DLL whitelist: passed (plugin, core, Newtonsoft.Json and REPOLib only).

The core suite covers route/method handling, health vs gameplay readiness, bearer authentication, malformed JSON/duplicate properties, unknown object IDs, quantity/coordinate/payload limits including numeric overflow, rate limiting, required/reused/conflicting idempotency keys, 32 concurrent retries sharing one execution, bounded queue admission, expiration without late execution, truthful outcomes for already-started actions, session-change invalidation, host/level restrictions, unsupported player actions, main-thread consumer affinity, shutdown rejection and actual concurrent HTTP socket requests with listener shutdown.

The FakeGame adapter deliberately does not instantiate Unity objects, fabricate game catalogs or assert network replication. The Unity adapter is compiled against real assemblies, but gameplay behavior is covered by the manual checklist below.

## Actual game startup probe

The initial startup probe used an isolated game directory and fresh Proton prefix. Game assets were referenced from the installed game; no mod files were installed into the user's original game directory and no original saves were used. Versions observed: R.E.P.O. v0.4.4.3, Unity 2022.3.67f2, BepInEx 5.4.23.5, REPOLib 4.2.0.

Verified with real Unity Update and TCP requests:

- BepInEx loads REPOLib and this plugin.
- Persistent REST runner starts after the first scene activation.
- Authenticated `/health` returns HTTP 200 and valid JSON: `status=ready`, `gameRunning=true`, `inLevel=false`, `gameplayReady=false`.
- `/api/version`, `/api/capabilities`, `/api/catalog/items`, `/api/players` return parseable JSON with HTTP 200 before gameplay. Items are empty at this registration stage; this does not certify populated catalogs.
- The final packaged DLLs report the actual game version `v0.4.4.3` and REPOLib plugin version `4.2.0` (rather than Unity Application.version or the library assembly version).
- Before entering gameplay, final catalog probes returned 27 enemy entries and 157 valuable entries; the upgrade catalog was empty at that stage. No live-level spawn was attempted during that initial startup-only probe.
- `/openapi.json` returns the 21-path OpenAPI contract.
- A normal Windows close request causes the game to log `REST server stopped`; the process exits and port 17453 is no longer listening.
- Missing bearer token returns HTTP 401 with the structured error.
- An authenticated spawn request before a playable level returns HTTP 409 `GAME_NOT_READY`.

Two compatibility failures found during the probe were fixed: Unity's bundled Newtonsoft assembly lacks a newer ToString overload, and the loader bootstrap object is destroyed before the first scene Update on this installation. The plugin now uses the shared JSON signature and a separately owned runner created after scene activation. Application.version was also found to return `0.1`; the version endpoint now reads the public BuildManager version used by the game's own version log instead.

## Later installed-game item spawn

After the user requested installation on CachyOS, BepInEx, REPOLib and the packaged mod were installed in the actual Steam game directory, with an application-specific Proton loader override. A subsequent user-authorized API action observed:

- `/api/game/state` returned `inLevel=true`, `isHost=true` and `gameplayReady=true`.
- `/api/catalog/items` contained 58 entries at that point in the session.
- The live catalog supplied `Items/Item Health Pack Small`, displayed as `Small Health Pack (25)`.
- A quantity-one `near_player` request returned HTTP 200, `success=true`, one confirmed spawned object and its position. `replicationVerified` remained false.

This verifies one host-side item creation through the installed API. Physical pickup usability, visual collision placement and remote client replication were not independently confirmed. The observed catalog counts depend on game state/content; they are not an expected fixed catalog or automated fixture.

The documentation/client update adds optional JavaScript, Python, curl and PowerShell examples. No new automated test run or gameplay mutation was performed for that documentation change. The 41-case result above describes the earlier implementation test run. Example clients are not additional evidence of game or multiplayer correctness.

## Gameplay and multiplayer release gate

The following checks are **pending**, and `/api/capabilities` reports `multiplayerTested=false`. Do not describe this build as certified for multiplayer until the second-client checks pass.

- [x] Observe a generated playable level and host readiness in the installed game.
- [ ] Repeat the full gameplay checks below using a separate test save.
- [ ] Confirm nonempty native and registered mod item/enemy/valuable catalogs.
- [x] Receive a confirmed host API result for one item spawned near a current player.
- [ ] Confirm that item's physical usability and visual collision placement.
- [ ] Spawn one enemy; confirm initialized AI and no director errors.
- [ ] Spawn several enemies within the configured entity limit.
- [ ] Spawn a valuable and a physical upgrade pickup.
- [ ] Heal/revive a valid target; compare the actual result with the documented invocation-only response.
- [ ] Exercise all supported positioning modes, large bounds and blocked world positions.
- [ ] Disconnect a selected player while commands are queued; verify 404/409 and no fallback target.
- [ ] Re-send a spawn with the same idempotency key; confirm exactly one batch.
- [ ] Join with a second client using identical content mods and confirm every spawned entity replicates.
- [ ] Run the API on a non-host client and confirm mutations return 409 `HOST_REQUIRED`.
- [ ] Delete one API-created enemy and clear API-created enemies; native enemies remain.
- [ ] Leave the level and verify safe command failure and stale ID invalidation.
- [ ] Quit normally during pending requests and confirm listener and worker cleanup.

Runtime probing does not replace this checklist. There is no fabricated gameplay pass or second-client test.
