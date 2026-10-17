# API 1.0.0

Base URL: `http://127.0.0.1:17453`. All endpoints require `Authorization: Bearer <token>` unless authentication was explicitly disabled on loopback. JSON is UTF-8. [openapi.json](openapi.json) is also served by authenticated `GET /openapi.json`.

## Endpoints

| Method | Path | Result |
|---|---|---|
| GET | `/health` | HTTP/Unity/game readiness |
| GET | `/api/version` | Runtime and compiled dependency versions |
| GET | `/api/game/state` | Same readiness fields as health |
| GET | `/api/capabilities` | Supported operations and unavailable feature reasons |
| GET | `/openapi.json` | OpenAPI 3.0.3 |
| GET | `/api/catalog/items` | `{ "items": [...] }` |
| GET | `/api/catalog/enemies` | `{ "enemies": [...] }` |
| GET | `/api/catalog/valuables` | `{ "valuables": [...] }` |
| GET | `/api/catalog/upgrades` | `{ "upgrades": [...] }`, physical pickups |
| POST | `/api/spawn/item` | `itemId`, optional `quantity`, `position` |
| POST | `/api/spawn/enemy` | `enemyId`, optional `quantity`, `position` |
| POST | `/api/spawn/valuable` | `valuableId`, optional `quantity`, `position` |
| POST | `/api/spawn/upgrade` | `upgradeId`, optional `quantity`, `position` |
| GET | `/api/players` | Current player IDs, display names and positions |
| GET | `/api/players/{id}` | One current player |
| POST | `/api/player/heal` | `playerId`, integer `amount` 1–1000 |
| POST | `/api/player/revive` | `playerId` |
| POST | `/api/player/teleport` | `playerId`, `position`; currently returns 501 |
| GET | `/api/enemies` | Current scene enemies, including native ones |
| POST | `/api/enemies/clear` | `{}`; remove only enemies created by this API |
| POST | `/api/enemies/despawn` | `enemyId`; remove one API-created enemy |

Read responses use the contract directly. Mutations return `{ "success": true, "result": ... }`. Errors always return `{ "success": false, "error": { "code": "...", "message": "...", "details": null } }`; partial spawn errors include details.

## Readiness and authority

```json
{"status":"ready","gameRunning":true,"inLevel":false,"isHost":true,"gameplayReady":false,"apiVersion":"1.0.0"}
```

`ready` means the HTTP server and Unity dispatcher can respond. A level is usable when `RunManager.levelCurrent` is in `RunManager.levels` and `LevelGenerator.Generated` is true. All mutations require this state and `SemiFunc.IsMasterClientOrSingleplayer()`. Clients receive 409 `HOST_REQUIRED`. Scene, room, generator, level readiness and host changes invalidate queued mutations and cached player/entity references.

Multiplayer replication has not been tested with multiple clients. Network object creation uses REPOLib, which uses Photon room instantiation in multiplayer. Every client must have the same required content mods. A returned Unity object confirms creation on the executing host; it does not acknowledge replication to another client.

## Catalogs

Get IDs at runtime; no prefab list is hardcoded. Items/valuables use `PrefabRef.ResourcePath`, enemies use their `EnemySetup` asset name. IDs are case sensitive; ambiguous collisions are excluded. Entries include `id`, `name`, `category`, `spawnable` and `entitiesPerSpawn`. Content must have a valid prefab reference and root PhotonView. Disabled/nonphysical items, broken references and enemy setups with multiple spawn objects are excluded.

```bash
curl -H "Authorization: Bearer $REPO_API_TOKEN" \
  'http://127.0.0.1:17453/api/catalog/items?search=health'
curl -H "Authorization: Bearer $REPO_API_TOKEN" \
  'http://127.0.0.1:17453/api/catalog/enemies'
curl -H "Authorization: Bearer $REPO_API_TOKEN" \
  'http://127.0.0.1:17453/api/players'
```

`search` is a case-insensitive name/ID substring. Optional `category` must match the plural catalog endpoint. Other query parameters are rejected. Metadata refreshes on scene/session changes, on every mutation, and at most three seconds after the previous catalog request. Registered mod content is included through REPOLib collections; enumeration never instantiates objects. Catalog availability does not guarantee a particular map has a sufficiently large collision-free spawn location.

## Spawn positions

| Mode | Fields | Behavior |
|---|---|---|
| `near_player` | `playerId` | Search a ring around that current player |
| `random_player` | none | Choose a current player, then search nearby |
| `random_valid` | none | Sample points inside current NavMesh triangles |
| `world_position` | finite `x`, `y`, `z` | Preserve x/z and search the nearby floor vertically |

Coordinates must be numeric and within ±10000. Prefab collider bounds are computed from existing asset components. Candidates must have a sufficiently level floor, fit without overlapping colliders or other requested positions, and, for enemies, be near a NavMesh. There are at most 48 attempts per entity. Unsupported collision shapes, missing bounds, no floor or no available space return 422. World position may adjust the Y coordinate to rest the object above the floor; it never silently changes X/Z. Quantity greater than one at one explicit world position will normally be rejected because the objects overlap. Geometry can move after validation; map-specific placement still needs gameplay testing.

Set the IDs below to values returned by the current catalogs and player listing. The shell variables are placeholders for discovered IDs, not predefined game identifiers.

```bash
curl -X POST 'http://127.0.0.1:17453/api/spawn/enemy' \
  -H "Authorization: Bearer $REPO_API_TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: my-unique-event-001' \
  --data "{\"enemyId\":\"$ENEMY_ID\",\"quantity\":1,\"position\":{\"mode\":\"near_player\",\"playerId\":\"$PLAYER_ID\"}}"
```

The same pattern applies to `/api/spawn/item` with `itemId`, `/api/spawn/valuable` with `valuableId`, and `/api/spawn/upgrade` with `upgradeId`. Quantities default to 1 and must not exceed the configured limit (10 by default). Upgrades are physical pickups; direct player stat grants are not exposed.

Spawn success includes `result.spawned`, `result.count`, and `replicationVerified=false`. Enemy results also include the opaque entity `id` used for deletion. All positions are prevalidated, but exceptions during creation can still partially complete a batch. `500 SPAWN_FAILED` reports confirmed partial results in `error.details.spawned`; do not create a new key and blindly repeat the whole request.

## Player actions and enemy ownership

Player IDs are random per current scene/session and never include platform account IDs, Photon actor/view identifiers, room identifiers or credentials. Player output deliberately omits private health/death fields. Refresh IDs after transitions/disconnects.

Heal invokes `PlayerHealth.HealOther(amount, true)` and revive invokes `PlayerAvatar.Revive(false)`. Their void public APIs can ignore an action when prerequisites are missing and do not acknowledge owner completion. Responses explicitly contain `outcome="invoked"` and `effectConfirmed=false`; this is not a promise that health changed or revival completed. Teleport returns 501 with its concrete capability reason.

`GET /api/enemies` includes an `apiSpawned` boolean. Despawn/clear only accept objects tracked as created by this mod in the current scene. Native IDs return 404. Removal uses Photon destroy for room/host-owned objects and Unity destruction in singleplayer; its result is `destroy_requested`, since the remote clients do not acknowledge destruction. Tracking is intentionally abandoned on session/authority changes rather than claiming ownership of unrelated entities.

## Idempotency, deadlines and errors

Every POST requires `Idempotency-Key` (1–128 characters) and `Content-Type: application/json`. Use a unique key per intended command. Retrying the same key/body returns the same cached result, including errors. Simultaneous retries share one execution. Reusing a key with a different action/body returns 409. Keys and results live in bounded memory for at least ten minutes after admission; they do not survive restart. Do not reuse keys after that interval. The server returns 503 rather than evicting pending requests or recent outcomes when the 2048-key cache is full.

Only GET and POST are supported on their designated endpoints. Request bodies are limited to 16 KiB by default, with maximum JSON depth 16. Unknown fields, duplicate JSON properties, unknown IDs, nonnumeric/nonfinite coordinates and invalid quantities are rejected. Global authenticated request rate defaults to 20 per rolling second. HTTP concurrency and the game queue are bounded separately by QueueCapacity.

A queued request that reaches its deadline returns 504 and cannot execute later. Once a synchronous game action has begun, it returns its actual result even if the deadline passes; the game thread is never interrupted mid-spawn. Client disconnection does not roll back a started command. Retain the key to retrieve its result. There is no 202/persistent background command API.

| Status | Typical code |
|---|---|
| 400 | `INVALID_JSON`, `INVALID_QUANTITY`, `INVALID_POSITION`, `UNKNOWN_FIELD`, `IDEMPOTENCY_KEY_REQUIRED` |
| 401 | `UNAUTHORIZED` |
| 404 | `NOT_FOUND`, `UNKNOWN_OBJECT_ID`, `PLAYER_NOT_FOUND`, `API_ENEMY_NOT_FOUND` |
| 405 | `METHOD_NOT_ALLOWED` |
| 409 | `GAME_NOT_READY`, `HOST_REQUIRED`, `GAME_SESSION_CHANGED`, `IDEMPOTENCY_CONFLICT`, `OWNERSHIP_REQUIRED` |
| 413 | `PAYLOAD_TOO_LARGE` |
| 415 | `UNSUPPORTED_MEDIA_TYPE` |
| 422 | `NO_SAFE_POSITION`, `UNSUPPORTED_BOUNDS` |
| 429 | `RATE_LIMITED` (Retry-After: 1) |
| 500 | `SPAWN_FAILED`, `ACTION_FAILED`, `INTERNAL_ERROR` |
| 501 | `UNSUPPORTED_ACTION` |
| 503 | `QUEUE_FULL`, `SERVER_BUSY`, `IDEMPOTENCY_CAPACITY`, `SHUTTING_DOWN` |
| 504 | `COMMAND_TIMEOUT` |

Malformed/incomplete HTTP uploads that exceed their read deadline, or broken connections, are aborted. Those requests have not reached gameplay execution and may not receive a JSON error if the transport is already unusable.
