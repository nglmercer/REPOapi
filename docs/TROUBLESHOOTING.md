# Troubleshooting

[Documentation index](README.md) · [Getting started](GETTING_STARTED.md) · [API error reference](API.md#idempotency-deadlines-and-errors)

## No configuration file or HTTP listener

Check the active profile's `BepInEx/LogOutput.log` for plugin load, dependency and bind errors. Confirm BepInEx 5 Mono x64, REPOLib 4.2.0 and all three RepoRestApi DLLs are installed. Check Enabled, the listen port and whether another process uses it.

On Linux/Proton, confirm the winhttp override using [the setup instructions](GETTING_STARTED.md#linux-cachyos-and-steam-deck). The configuration is generated only after the plugin loads, and the listener starts after the first scene activation. A mod manager's profile path may differ from the original game directory.

## 401 UNAUTHORIZED

Load AuthenticationToken from the configuration belonging to the running profile. Every route, including `/health` and `/openapi.json`, requires it by default. Restart your example client after changing environment variables. The provided clients do not automatically read `.env` files.

## Empty catalogs or no matching item

Catalog contents depend on content registration and game state. Host or start singleplayer, enter a generated gameplay level and wait for registration. Try a broader search, or list the full category. Some catalogs may be available in menus while others are still empty.

IDs must come from the matching current catalog. Use an entry's `id`, not its display name. Physical upgrades use `/api/catalog/upgrades`; `/api/spawn/upgrade` expects `upgradeId`. Refresh IDs after scene/session changes. Disabled/nonphysical items, invalid prefabs, ambiguous IDs and multi-object enemy setups are excluded.

## Request error guide

| Status/code | What to do |
|---|---|
| 400 INVALID_JSON / UNKNOWN_FIELD | Send one JSON object with the documented field names. Helpers handle JSON content types; generic clients expect a JSON body argument. |
| 400 INVALID_QUANTITY | Use an integer from 1 to `/api/capabilities` → `limits.maxSpawnQuantity` (default 10). |
| 400 IDEMPOTENCY_KEY_REQUIRED | Include a key on every POST. All provided clients generate one and print it to stderr. |
| 404 UNKNOWN_OBJECT_ID | Discover the ID again in the correct catalog. Quote IDs containing spaces in your client code. |
| 404 PLAYER_NOT_FOUND | Refresh `/api/players`; the player may have disconnected or the session changed. |
| 404 API_ENEMY_NOT_FOUND | Despawn accepts only enemies still tracked as created by this API. Native enemy IDs cannot be deleted. |
| 405 METHOD_NOT_ALLOWED | Use the method from the endpoint table; opening a POST URL in a browser sends GET. |
| 409 GAME_NOT_READY | Enter a generated playable level. Menus, lobbies, shops and transitions are excluded. |
| 409 HOST_REQUIRED | Run the command on the host or singleplayer instance. |
| 409 GAME_SESSION_CHANGED | Refresh health, players and catalogs. The queued command belongs to an earlier scene/session. |
| 409 IDEMPOTENCY_CONFLICT | You reused a key with a different path/body. Preserve the exact original request for retries; use a new key for a new action. |
| 413 PAYLOAD_TOO_LARGE | Reduce the JSON body to the configured byte limit. |
| 415 UNSUPPORTED_MEDIA_TYPE | Send `Content-Type: application/json`. |
| 422 NO_SAFE_POSITION / UNSUPPORTED_BOUNDS | Move to a clearer location or choose another supported prefab. A failed prevalidation did not spawn objects. |
| 429 RATE_LIMITED | Slow down; default is 20 authenticated requests per rolling second. The response includes `Retry-After: 1`. |
| 500 SPAWN_FAILED | Read `error.details.spawned` and the game log. A batch may have partially completed; do not blindly repeat with a new key. |
| 501 UNSUPPORTED_ACTION | Read capabilities. Teleport and direct upgrade grants are unavailable. |
| 503 QUEUE_FULL / SERVER_BUSY | Allow outstanding requests to finish and reduce concurrency. |
| 503 SHUTTING_DOWN | The game or runtime is closing. |
| 504 COMMAND_TIMEOUT | The queued command expired without executing. The old key returns that cached outcome; a new intended attempt needs a new key. |

## HTTP success but no expected visual effect

Spawn success reports the confirmed object's position in `result.spawned`. Look nearby; a safe placement may be several meters from the player. Upgrade pickups must be collected. The API does not claim remote client replication was acknowledged.

Heal/revive return `outcome="invoked"` and `effectConfirmed=false`. Their public game methods can ignore an action when prerequisites are missing; HTTP success reports invocation, not a confirmed health or death-state change.

## Transport failure or client timeout

An interrupted connection does not prove the action failed. Keep the original path, body and printed idempotency key. Use the generic client to retry exactly that request within the cache retention window; do not rerun a discovery helper and generate a new key. [Retry examples](EXAMPLES.md#retrying-without-duplicate-spawns) explain the distinction between a received 504 and an unknown transport outcome.

## Browser fetch fails while curl works

The mod does not provide CORS headers. The JavaScript example uses Node.js fetch on a local backend. Do not put the bearer credential in a public website. Use the JavaScript client from a trusted local process or choose Python/curl/PowerShell.
