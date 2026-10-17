# Architecture

[Documentation index](README.md) · [API reference](API.md)

`RepoRestApi.Core` targets .NET Standard 2.1 and has no Unity references. `RepoRestApi` is the BepInEx entry point and the direct game adapter. The test project targets .NET 8 and substitutes an adapter. There is no web framework, reflection bridge, Harmony patch or external runtime in the mod.

```text
HttpListener async accept / bounded active requests
  -> bearer authentication / byte limit / rate limiter
  -> route / JSON shape / idempotency admission
  -> bounded dispatcher queue with expiration
  -> ApiRuntime.Update: at most CommandsPerFrame actions
  -> recheck session, level, host, IDs and object references
  -> REPOLib/game public APIs
  -> actual response cached by idempotency key
  -> JSON HTTP response
```

Unity objects are only accessed in scene callbacks/Update or queued work consumed by Update. The sole adapter value read by worker threads is an atomic numeric generation counter; a queued mutation records it, and revalidates after refreshing game state. Unity state, catalogs, players, Physics, NavMesh, entity tracking and Photon actions stay on the main thread.

`MainThreadDispatcher` uses one lock for bounded queue admission and work state transitions, asynchronous task completions, and a timer for each outstanding request. Timer cancellation wins only before execution. Expired work is discarded and never run; an executing synchronous action completes normally. Pump never blocks waiting for HTTP or an async task. It processes a bounded number of entries per frame, and idle Update performs a few identity/readiness comparisons plus an empty queue check. Catalog scans and navigation queries happen only on demand.

`IdempotencyStore` reserves keys before queue submission, shares in-flight results, caches completed errors/successes and refuses capacity pressure instead of evicting pending work. The cache is process-local; a separate persistent command-status system is intentionally absent, so no endpoint returns accepted/queued success.

On shutdown the dispatcher rejects admission and faults pending tasks. Server cancellation aborts incomplete reads/responses, closes the listener, awaits the accept loop and all active handlers, then disposes its cancellation source. Tests exercise concurrent real HTTP requests and shutdown; Unity/Proton shutdown observations are listed separately in TESTING.md.

Batch spawning prevalidates every intended volume and checks the configured entity count before calling REPOLib. There is no transactional network rollback: partial failures report their confirmed result and remain cached under the key. Tracking only covers API-created enemies and is cleared on scene/room/generator/readiness/host changes. Removal cannot accept arbitrary network IDs or delete native enemies.

The BepInEx plugin loads configuration during Awake, then creates a dedicated persistent ApiRuntime after the first scene activates. This avoids tying the server to a loader bootstrap object which was observed being destroyed before its first Update on the installed Unity build. ApiRuntime owns the dispatcher/server and shuts both down on application quit or runtime destruction. A one-shot scene callback and Application.quitting subscription are removed during shutdown.
