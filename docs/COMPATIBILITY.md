# Compatibility research

[Documentation index](README.md) · [API reference](API.md)

Research performed 2026-10-17. Source and binary inspection preceded game integration. Game code and proprietary assemblies are not redistributed.

## Pinned dependencies and evidence

| Dependency | Inspected version | Evidence |
|---|---|---|
| R.E.P.O. installed game | v0.4.4.3; Unity 2022.3.67f2 | Installed game's managed assemblies and version output |
| Game reference package | R.E.P.O.GameLibs.Steam 0.4.4-ngd.0 | Latest published NuGet version at inspection; original access annotations were checked |
| REPOLib | 4.2.0 | [Thunderstore](https://thunderstore.io/c/repo/p/Zehs/REPOLib/), NuGet DLL and source commit `17f35cd94e4628ea66476a7bba48b333db78d973` (v4.2.0) |
| BepInExPack | 5.4.2305, BepInEx 5.4.23.5 | [REPO package](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/), declared REPOLib dependency and package DLLs |
| BepInEx.Core compile baseline | 5.4.21 | REPOLib 4.2.0's own package dependency; public APIs also checked against 5.4.23.5 |
| Newtonsoft.Json | 13.0.4 package | REPOLib dependency; runtime must tolerate the Unity-provided Newtonsoft assembly with the same identity |

REPOLib Thunderstore ZIP SHA-256: `2d3c1bfa50cce768ee4c36874d2d2c2676dd17697dd19d8655b6456d83d25747`.
Game reference NuGet SHA-256: `6db2e05b1ff27fc97e458fa01e2560f31b20193964c988f9dfd3cafb64cdc3e4`.
Installed Assembly-CSharp SHA-256: `ce995a182ddc884ea965e87786f1986248d9616300fa825bcc04bca671ee6526`.

The installed game assemblies are not publicized. Successful compilation with `GameManagedPath` therefore verifies that the plugin does not accidentally depend on the compile package's publicized internal fields. This does not verify gameplay behavior.

## Exact APIs used

[REPOLib source](https://github.com/ZehsTeam/REPOLib/tree/17f35cd94e4628ea66476a7bba48b333db78d973/REPOLib/Modules) uses namespace `REPOLib.Modules`:

```csharp
Items.AllItems                    // IReadOnlyList<Item>
Items.RegisteredItems              // IReadOnlyList<Item>
Items.SpawnItem(Item? item, Vector3 position, Quaternion rotation) // GameObject?
Valuables.AllValuables             // IReadOnlyList<PrefabRef>
Valuables.RegisteredValuables      // IReadOnlyList<PrefabRef>
Valuables.SpawnValuable(PrefabRef? prefabRef, Vector3 position, Quaternion rotation) // GameObject?
Enemies.AllEnemies                // IReadOnlyList<EnemySetup>
Enemies.RegisteredEnemies         // IReadOnlyList<EnemySetup>
Enemies.SpawnEnemy(EnemySetup? enemySetup, Vector3 position, Quaternion rotation,
                   bool spawnDespawned = false) // List<EnemyParent>?
```

Deprecated `GetItems`, `GetEnemies`, `GetValuables` and name lookup methods are not used. Several old versions now have obsolete attributes with compile-time errors. `REPOLib.Extensions.EnemySetupExtensions` is internal, so this plugin reads the public `EnemySetup.spawnObjects` collection instead of calling that extension.

The spawners route through `NetworkPrefabs.SpawnNetworkPrefab(PrefabRef, Vector3, Quaternion, byte = 0, object[]? = null)`. It checks host authority, uses `PhotonNetwork.InstantiateRoomObject` in multiplayer and Unity instantiation only in singleplayer. `Enemies.SpawnEnemy` additionally updates RunManager enemy bookkeeping and EnemyDirector spawn points, sets setup flags and teleports the enemy. This logic is reused rather than reimplemented.

Native public APIs inspected in actual game binaries:

- `PrefabRef.IsValid()`, `Prefab`, `PrefabName`, `ResourcePath`.
- `RunManager.instance`, `levelCurrent`, `levels`; `LevelGenerator.Instance`, `Generated`.
- `SemiFunc.IsMasterClientOrSingleplayer()`, `IsMultiplayer()`, `PlayerGetList()`, `PlayerGetName(PlayerAvatar)`.
- `PlayerAvatar.playerHealth`, `PlayerAvatar.Revive(bool = false)` and `PlayerHealth.HealOther(int, bool)`.
- `PhotonNetwork.CurrentRoom`, `MasterClient`, `Destroy(GameObject)`; `PhotonView.Owner`, `IsRoomView`, `IsMine`.
- Unity public collider bounds metadata, Physics queries and NavMesh queries.

`HealOther` calls local `Heal` when owned/singleplayer, otherwise sends `HealOtherRPC` to the owner. `Revive` sends its normal game RPC. Both have void return types and can be ignored when game prerequisites are missing. The API therefore reports invocation rather than inventing effect confirmation.

## Unavailable or deliberately restricted features

| Feature | Concrete reason |
|---|---|
| Teleport | `PlayerAvatar.Spawn(Vector3, Quaternion)` exists, but the reference's safe handling also reads non-public `deadSet` and manipulates death/tumble/push state. No verified public operation covers both living and dead players safely. Endpoint returns 501. |
| Private health/death state | `PlayerHealth.health`, `maxHealth` and `PlayerAvatar.deadSet` are non-public in the installed game. Publicized compile references must not be mistaken for public runtime support. |
| Confirmed remote heal/revive outcome | Public methods return void and provide no command-correlated owner acknowledgement. Responses explicitly say `invoked` and `effectConfirmed=false`. |
| Direct upgrade grants | REPOLib's upgrade registry covers registered custom `PlayerUpgrade`s, not a complete verified native grant contract. Physical items typed `player_upgrade`/`item_upgrade` are discoverable and spawnable using Items. |
| Multi-object enemy setups | REPOLib spawns all setup objects at one supplied origin. Separating members would bypass setup semantics, while overlapping their volumes breaks safe placement. These setups are excluded from spawn catalogs. |
| Unknown collider shape | Safe bounds cannot be inferred for every modded Collider subclass without instantiation. The command returns 422 instead of guessing. |
| Native enemy deletion | Intentionally outside the contract; only API-created objects with retained references are accepted. |
| Certified multiplayer | Requires second-client replication tests that have not been performed. |

## Reference implementations inspected

- [repo-live-control](https://github.com/jkieley/repo-live-control/tree/1221e93b4a8adbd5f8f37b844fe65ddc25be40d2), especially runtime catalogs, player selection/actions and REPOLib spawning. Its extra reflection, owner relays and command console are not copied into the HTTP API.
- [REPO-IVSpawner](https://github.com/MohammadReza-GhasemiFar/REPO-IVSpawner/tree/0e7b74c88c28c0a50094d905cda43e0159820357), for historical spawn behavior. Its hardcoded names and old APIs are not used as a catalog.
- [BepInEx source and releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), plus released BaseUnityPlugin/Chainloader implementation.
- [TikTools Minecraft declarative example](https://github.com/nglmercer/TikTools-app/blob/remake/examples/minecraft-server/plugin.json), read for future HTTP integration. No TikTools files were modified.

## Unity Newtonsoft compatibility

Runtime probing found the game-provided Newtonsoft DLL has `JToken.ToString(Formatting, params JsonConverter[])` but lacks the newer single-argument `ToString(Formatting)` overload. The API passes an explicit empty converter array to bind the shared signature. The error was found by an actual Unity/Proton request; a desktop .NET unit test alone would not expose the assembly-unification difference.
