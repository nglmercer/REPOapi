# Code audit — 2026-10-18

[Documentation index](README.md) · [Testing record](TESTING.md)

Reviewed the HTTP core, Unity adapter, configuration/lifecycle, example clients, build workflow, package script, and API documentation. This was a source and automated-test audit; no gameplay commands were sent and no installed game files were changed.

## Fixes

| Area | Finding and change |
|---|---|
| HTTP reliability | Response writes had no deadline after upload completion. Successful, error and capacity responses now have a separate configured write deadline. |
| Shutdown | Repeated stop/dispose calls repeated cancellation and could access a disposed cancellation source. Calls now share one shutdown task and disposal is idempotent. |
| Request validation | Empty/nested player routes, duplicate filters and filters on unrelated endpoints were accepted. These now fail before dispatch. Position fields must match the mode, as specified by OpenAPI. |
| JSON validation | JObject can discard comments between properties. A reader-level check now rejects comments before loading the object, including nested comments. |
| Client security | Python followed HTTP redirects with bearer credentials; Python and PowerShell now explicitly refuse redirects. A local HTTP regression verifies the Python behavior. |
| Dispatch | Expired entries consumed the live action budget. Pump now skips them without delaying live actions. Queue admission compacts only under capacity pressure, avoiding a scan on each request. Invalid dispatcher arguments fail before admission. |
| Idempotency | Every retry scanned all retained keys; retention used wall time and began at admission. Cleanup is throttled to once per second and retention lasts at least ten monotonic minutes after completion. Pending entries remain protected. |
| Catalogs | Upgrade filtering could reintroduce IDs rejected as ambiguous by the item catalog. Upgrades now derive from the validated item catalog. |
| Placement | Collider centers are now explicitly checked for nonfinite values before physics queries. Existing collision and navigation checks remain in force. |
| Routing overhead | Mutation routes use a reusable set; the embedded OpenAPI response is loaded once. |
| Packaging | Example copying now selects source files instead of copying local caches and other incidental files. Python cache files are also ignored by Git. |

## Verification

- Release solution build: zero warnings/errors.
- Plugin compilation against installed game managed assemblies: zero warnings/errors.
- Core tests: 61 passed (previously 41), including real concurrent HTTP requests and repeated shutdown/disposal.
- Python redirect regression: passed; included in CI.
- NuGet vulnerability query with transitive dependencies: no known vulnerable packages reported by configured feeds on the audit date. This does not certify proprietary game assemblies or runtime safety.
- Python, JavaScript and Bash syntax checks: passed. PowerShell is unavailable in this environment; its runtime behavior was not tested.
- Release package rebuilt; ZIP integrity, DLL whitelist and SHA-256 checked.

## Remaining validation

Unity physics, enemy AI, pickup usability, heal/revive effects and second-client replication require live gameplay testing. See the existing [manual checklist](TESTING.md#gameplay-and-multiplayer-release-gate). Compilation against installed game assemblies verifies API compatibility, not those behaviors. No claim is made that an audit proves the absence of all defects.

## Spawn hitch follow-up

A reported hitch during enemy creation led to a second review of the spawn path. Each mutation previously reset and rebuilt every catalog, dereferencing unrelated prefab assets on the Unity thread. Lookup now enumerates current registrations but resolves only matching IDs in the requested category; it still rejects ambiguity and revalidates the selected prefab. Catalog reads also build/cache only their requested category. Six catalog tests execute the real selection source against lightweight doubles, verifying that unrelated prefabs are untouched and removed/ambiguous IDs remain rejected. They do not simulate Unity or measure in-game speed.

Spawn diagnostics separate catalog, placement and native instantiation time, enabled by Verbose or automatically for spawns taking at least 100 ms. Bounds computation reuses its inverse rotation, and random-valid placement no longer samples an unused initial point. REPOLib's native enemy setup/initialization remains synchronous. A live before/after hitch measurement is pending; these changes remove verified unnecessary work without claiming to eliminate every first-spawn engine cost.

Dependency source reviewed: [REPOLib 4.2.0 enemy spawning](https://github.com/ZehsTeam/REPOLib/blob/17f35cd94e4628ea66476a7bba48b333db78d973/REPOLib/Modules/Enemies.cs), matching the pinned NuGet package commit.
