# API examples

[Documentation index](README.md) · [Token setup](GETTING_STARTED.md#load-your-token) · [API reference](API.md) · [Runnable files](../examples/README.md)

Run commands from the repository root after loading `REPO_API_TOKEN`. All clients default to `http://127.0.0.1:17453`; optionally set `REPO_API_URL`. Gameplay mutations require hosting/singleplayer and a generated level. No example runs automatically inside the game.

## Pick your language

| Client | Requirements | Generic request | Catalog-driven spawn |
|---|---|---|---|
| JavaScript | Node.js 22+, built-in fetch, no npm packages | [repo-api.mjs](../examples/javascript/repo-api.mjs) | [spawn.mjs](../examples/javascript/spawn.mjs) |
| Python | Python 3.10+, standard library, no pip packages | [repo_api.py](../examples/python/repo_api.py) | [spawn.py](../examples/python/spawn.py) |
| curl | Bash, curl 7.76+; jq for discovery recipes | [repo-api.sh](../examples/curl/repo-api.sh) | Recipes below |
| PowerShell | PowerShell 7+, built-in Invoke-RestMethod | [repo-api.ps1](../examples/powershell/repo-api.ps1) | Recipes below |

All generic helpers accept GET or POST, set bearer authentication, and print JSON. POST helpers generate a UUID idempotency key and print it to stderr. They send one request with no automatic retries. Error responses retain the server's error code and details, including partial spawn results.

The JavaScript client uses Node.js fetch. Browser requests from another origin are not supported because this mod does not provide CORS headers. These optional client runtimes are separate from the C# mod.

## JavaScript with fetch

Read health, list players and search the live catalog:

```bash
node examples/javascript/repo-api.mjs GET /health
node examples/javascript/repo-api.mjs GET /api/players
node examples/javascript/repo-api.mjs GET '/api/catalog/items?search=health'
```

Each following command is an independent action. Pick the one you want:

```bash
# One matching health item.
node examples/javascript/spawn.mjs item health

# One physical upgrade pickup, chosen from the upgrade catalog.
node examples/javascript/spawn.mjs upgrade

# One enemy, chosen from the enemy catalog.
node examples/javascript/spawn.mjs enemy

# One valuable, chosen from the valuable catalog.
node examples/javascript/spawn.mjs valuable

# Three matching health items, if the configured limit permits it.
node examples/javascript/spawn.mjs item health 3
```

The spawn helper checks readiness and capabilities, searches the current catalog and selects its first spawnable match. It uses `random_player` positioning. An empty result stops before POST. Rerunning the helper represents a **new spawn request**, with a new key; it is not a retry of an interrupted earlier request.

To use the client in your own Node.js module, save the following as a `.mjs` file in the repository root. Importing the client alone sends no request:

```javascript
import { RepoApi, ApiError } from './examples/javascript/repo-api.mjs';

const api = new RepoApi(); // Reads REPO_API_TOKEN and optional REPO_API_URL.
try {
  const health = await api.get('/health');
  if (!health.gameplayReady) throw new Error('Host and enter a gameplay level first.');

  const catalog = await api.get('/api/catalog/items?search=health');
  const item = catalog.items.find(entry => entry.spawnable && entry.category === 'item');
  if (!item) throw new Error('No matching item is registered.');

  const response = await api.post('/api/spawn/item', {
    itemId: item.id,
    quantity: 1,
    position: { mode: 'random_player' },
  });
  console.log(response.result.spawned);
} catch (error) {
  if (error instanceof ApiError) {
    console.error(error.status, error.response.error); // Keep error.details for partial batches.
  } else {
    console.error(error.message);
  }
}
```

The fetch implementation handles JSON, non-2xx responses, a 15-second client deadline and idempotency headers. You can customize it with `new RepoApi({ baseUrl, token, timeoutMs })`.

## Python with urllib

```bash
python3 examples/python/repo_api.py GET /health
python3 examples/python/repo_api.py GET /api/players
python3 examples/python/repo_api.py GET '/api/catalog/items?search=health'
```

Choose one action:

```bash
python3 examples/python/spawn.py item health
python3 examples/python/spawn.py upgrade
python3 examples/python/spawn.py enemy
python3 examples/python/spawn.py valuable
python3 examples/python/spawn.py item health 3
```

To integrate with a script saved in the repository root:

```python
from examples.python.repo_api import RepoApi, ApiError

api = RepoApi()  # Standard-library HTTP; no requests dependency.
try:
    if not api.get('/health')['gameplayReady']:
        raise RuntimeError('Host and enter a gameplay level first.')

    catalog = api.get('/api/catalog/upgrades')['upgrades']
    upgrade = next((entry for entry in catalog if entry['spawnable']), None)
    if upgrade is None:
        raise RuntimeError('No physical upgrade is registered yet.')

    response = api.post('/api/spawn/upgrade', {
        'upgradeId': upgrade['id'],
        'quantity': 1,
        'position': {'mode': 'random_player'},
    })
    print(response['result']['spawned'])
except ApiError as error:
    print(error.status, error.response['error'])
except (RuntimeError, ValueError) as error:
    print(error)
```

The client preserves structured HTTP errors and uses a 15-second socket timeout. Constructor overrides are `RepoApi(token=..., base_url=..., timeout=...)`. To add dynamic query parameters, use `urllib.parse.urlencode` as shown in [spawn.py](../examples/python/spawn.py).

## Bash and curl

The helper uses `curl --fail-with-body`, so error JSON is visible and HTTP failures return a nonzero exit status:

```bash
bash examples/curl/repo-api.sh GET /health
bash examples/curl/repo-api.sh GET '/api/catalog/items?search=health'
bash examples/curl/repo-api.sh GET /api/catalog/upgrades
bash examples/curl/repo-api.sh GET /api/players
```

To spawn one health item, discover its ID and build JSON with jq. If the catalog is empty, `jq -e` fails and `&&` prevents the POST:

```bash
REPO_ITEM_REQUEST=$(bash examples/curl/repo-api.sh GET '/api/catalog/items?search=health' |
  jq -ce '([.items[] | select(.spawnable and .category == "item")][0]
    // error("No matching item; enter a level or change the search"))
    | {itemId:.id,quantity:1,position:{mode:"random_player"}}') &&
bash examples/curl/repo-api.sh POST /api/spawn/item "$REPO_ITEM_REQUEST"
```

To spawn a physical upgrade pickup:

```bash
REPO_UPGRADE_REQUEST=$(bash examples/curl/repo-api.sh GET /api/catalog/upgrades |
  jq -ce '([.upgrades[] | select(.spawnable)][0]
    // error("No upgrade available; enter a level and wait for registration"))
    | {upgradeId:.id,quantity:1,position:{mode:"random_player"}}') &&
bash examples/curl/repo-api.sh POST /api/spawn/upgrade "$REPO_UPGRADE_REQUEST"
```

For direct curl without the helper, use the same discovered body and a fresh key for a new action:

```bash
REPO_SPAWN_KEY="$(cat /proc/sys/kernel/random/uuid)"
curl --silent --show-error --fail-with-body --max-time 15 \
  -X POST "${REPO_API_URL:-http://127.0.0.1:17453}/api/spawn/item" \
  -H "Authorization: Bearer $REPO_API_TOKEN" \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $REPO_SPAWN_KEY" \
  --data-binary "$REPO_ITEM_REQUEST"
```

This direct-curl block executes another action if you already ran the previous item command. On macOS, use `uuidgen` instead of `/proc/sys/kernel/random/uuid`. The Bash helper supports either UUID source.

## PowerShell with Invoke-RestMethod

Load environment variables using [Getting started](GETTING_STARTED.md#powershell-on-windows). Then:

```powershell
pwsh -NoProfile -File examples/powershell/repo-api.ps1 -Method GET -Path /health
pwsh -NoProfile -File examples/powershell/repo-api.ps1 -Method GET -Path '/api/catalog/items?search=health'
```

For catalog-driven calls in a PowerShell session:

```powershell
$RepoBaseUrl = if ($env:REPO_API_URL) { $env:REPO_API_URL.TrimEnd('/') } else { 'http://127.0.0.1:17453' }
$RepoHeaders = @{ Authorization = "Bearer $env:REPO_API_TOKEN" }
$RepoHealth = Invoke-RestMethod -Uri "$RepoBaseUrl/health" -Headers $RepoHeaders -TimeoutSec 15
if (-not $RepoHealth.gameplayReady) { throw 'Host and enter a gameplay level first.' }
```

Spawn one health item:

```powershell
$RepoItems = Invoke-RestMethod -Uri "$RepoBaseUrl/api/catalog/items?search=health" -Headers $RepoHeaders -TimeoutSec 15
$RepoItem = $RepoItems.items | Where-Object { $_.spawnable -and $_.category -eq 'item' } | Select-Object -First 1
if ($null -eq $RepoItem) { throw 'No matching item is available.' }
$RepoBody = @{ itemId = $RepoItem.id; quantity = 1; position = @{ mode = 'random_player' } } | ConvertTo-Json -Depth 4
./examples/powershell/repo-api.ps1 -Method POST -Path /api/spawn/item -Body $RepoBody
```

Spawn one physical upgrade:

```powershell
$RepoUpgrades = Invoke-RestMethod -Uri "$RepoBaseUrl/api/catalog/upgrades" -Headers $RepoHeaders -TimeoutSec 15
$RepoUpgrade = $RepoUpgrades.upgrades | Where-Object { $_.spawnable } | Select-Object -First 1
if ($null -eq $RepoUpgrade) { throw 'No physical upgrade is available.' }
$RepoBody = @{ upgradeId = $RepoUpgrade.id; quantity = 1; position = @{ mode = 'random_player' } } | ConvertTo-Json -Depth 4
./examples/powershell/repo-api.ps1 -Method POST -Path /api/spawn/upgrade -Body $RepoBody
```

The PowerShell helper prints the idempotency key and the server error body. It uses the operating system's script execution policy; follow your normal policy for running a local script.

## Every endpoint with any client

The four generic clients take the same method, path and optional JSON body. For example, the capabilities request is:

```bash
bash examples/curl/repo-api.sh GET /api/capabilities
node examples/javascript/repo-api.mjs GET /api/capabilities
python3 examples/python/repo_api.py GET /api/capabilities
```

```powershell
./examples/powershell/repo-api.ps1 -Method GET -Path /api/capabilities
```

Use these read routes to discover data:

| Method | Path | What to read |
|---|---|---|
| GET | `/health` | `gameplayReady` before mutations |
| GET | `/api/game/state` | `inLevel` and `isHost` |
| GET | `/api/version` | Game, mod and dependency versions |
| GET | `/api/capabilities` | Supported actions, limits and unavailable-feature reasons |
| GET | `/openapi.json` | Full OpenAPI document |
| GET | `/api/catalog/items?search=health` | `items[].id` |
| GET | `/api/catalog/enemies` | `enemies[].id` |
| GET | `/api/catalog/valuables` | `valuables[].id` |
| GET | `/api/catalog/upgrades` | `upgrades[].id` |
| GET | `/api/players` | `players[].id` |
| GET | `/api/players/{id}` | Replace `{id}` with a current player ID; URL-encode the path segment |
| GET | `/api/enemies` | `enemies[].id` and `apiSpawned` |

Every POST below uses `application/json` and an idempotency key. Values in angle brackets are **templates to replace with IDs from the corresponding current catalog/list**, not game identifiers:

| Path | JSON body |
|---|---|
| `/api/spawn/item` | `{"itemId":"<items[].id>","quantity":1,"position":{"mode":"random_player"}}` |
| `/api/spawn/enemy` | `{"enemyId":"<catalog enemies[].id>","quantity":1,"position":{"mode":"random_player"}}` |
| `/api/spawn/valuable` | `{"valuableId":"<valuables[].id>","quantity":1,"position":{"mode":"random_player"}}` |
| `/api/spawn/upgrade` | `{"upgradeId":"<upgrades[].id>","quantity":1,"position":{"mode":"random_player"}}` |
| `/api/player/heal` | `{"playerId":"<players[].id>","amount":25}` |
| `/api/player/revive` | `{"playerId":"<players[].id>"}` |
| `/api/player/teleport` | `{"playerId":"<players[].id>","position":{"mode":"world_position","x":0,"y":1,"z":0}}` — currently returns 501 in a ready host level |
| `/api/enemies/clear` | `{}` — removes only API-created enemies |
| `/api/enemies/despawn` | `{"enemyId":"<active API-spawned enemy id>"}` — use `/api/enemies`, not the catalog ID |

For JavaScript imports, call `api.post(path, body)` with a JavaScript object. For Python imports, call `api.post(path, body)` with a dictionary. For the CLIs, pass the JSON body as the third positional argument (PowerShell: `-Body`). All four clients can call all 21 routes in the tables.

Heal/revive report method invocation with `effectConfirmed=false`. Teleport remains unavailable. Upgrade spawning creates a pickup; direct grants are unavailable. Native enemies cannot be despawned through these routes.

### Position recipes

Replace a spawn request's `position` field with one of these objects:

| Mode | Position object | How to choose it |
|---|---|---|
| Near a specific player | `{"mode":"near_player","playerId":"<players[].id>"}` | Discover the current player ID first |
| Near a random current player | `{"mode":"random_player"}` | Default used by the spawn examples |
| Random valid map point | `{"mode":"random_valid"}` | Requires a current navigation mesh |
| World coordinates | `{"mode":"world_position","x":0,"y":1,"z":0}` | Illustrative coordinates; use a real location in your current map |

World coordinates are not guaranteed safe merely because they are numeric. The mod validates geometry, may project Y onto a nearby floor and rejects occupied/unsupported positions. Multiple requested objects cannot overlap at one explicit world position. Read [positioning details](API.md#spawn-positions).

## Retrying without duplicate spawns

**New action:** use a new key. **Retry of an uncertain action:** use the same key, method, path and original JSON body, preserving property order. The current server compares serialized JSON and does not normalize object property ordering. Keep the original body rather than reconstructing it from a fresh catalog query.

### JavaScript

```javascript
import { randomUUID } from 'node:crypto';
import { RepoApi } from './examples/javascript/repo-api.mjs';
const api = new RepoApi();
const catalog = await api.get('/api/catalog/items?search=health');
const item = catalog.items.find(entry => entry.spawnable && entry.category === 'item');
if (!item) throw new Error('No matching item.');
const key = randomUUID();
const body = { itemId: item.id, quantity: 1, position: { mode: 'random_player' } };
console.error('Keep for retry:', key, JSON.stringify(body));
const response = await api.post('/api/spawn/item', body, key);
// If the connection outcome is unclear, repeat api.post with this SAME body and key.
```

### Python

```python
import json
import uuid
from examples.python.repo_api import RepoApi
api = RepoApi()
catalog = api.get('/api/catalog/items?search=health')['items']
item = next((entry for entry in catalog if entry['spawnable'] and entry['category'] == 'item'), None)
if item is None:
    raise RuntimeError('No matching item.')
key = str(uuid.uuid4())
body = {'itemId': item['id'], 'quantity': 1, 'position': {'mode': 'random_player'}}
print('Keep for retry:', key, json.dumps(body))
response = api.post('/api/spawn/item', body, key)
# If the connection outcome is unclear, repeat api.post with this SAME body and key.
```

These snippets create a new key for an initial request; rerunning the whole snippet creates a new action. For a retry, retain the original body/key and repeat only the POST. The spawn helpers also print their request body and key to stderr. To retry via a CLI, pass that saved original JSON and key:

```bash
# REPO_ORIGINAL_JSON and REPO_ORIGINAL_KEY must come from the original request.
bash examples/curl/repo-api.sh POST /api/spawn/item "$REPO_ORIGINAL_JSON" "$REPO_ORIGINAL_KEY"
node examples/javascript/repo-api.mjs POST /api/spawn/item "$REPO_ORIGINAL_JSON" "$REPO_ORIGINAL_KEY"
python3 examples/python/repo_api.py POST /api/spawn/item "$REPO_ORIGINAL_JSON" "$REPO_ORIGINAL_KEY"
```

```powershell
./examples/powershell/repo-api.ps1 -Method POST -Path /api/spawn/item -Body $RepoOriginalJson -IdempotencyKey $RepoOriginalKey
```

The Bash helper also accepts `@filename.json` as the body, using curl's `--data-binary` behavior. Keep that file unchanged for retries.

A received **504 COMMAND_TIMEOUT** confirms that the queued command did not execute, and its key returns the cached timeout. A new intended attempt needs a new key. A client transport timeout is different: execution may have started, so retry with the old key to learn its result. A **500 SPAWN_FAILED** may include objects already created in `error.details.spawned`; inspect them before deciding on a new action.

Keys/results are retained in memory for at least ten minutes and do not survive restart. These clients do not automatically rerun spawns, retry failed batches or persist command history. See the [full idempotency contract](API.md#idempotency-deadlines-and-errors).

## HTTP client references

- [Node.js global fetch and AbortSignal](https://nodejs.org/api/globals.html).
- [Python urllib.request](https://docs.python.org/3/library/urllib.request.html).
- [curl command-line documentation](https://curl.se/docs/manpage.html).
- [PowerShell Invoke-RestMethod](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/invoke-restmethod).
