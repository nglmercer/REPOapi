using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using RepoRestApi.Core.Models;
using REPOLib.Modules;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
namespace RepoRestApi.Game;
internal sealed class SpawnService
{
    private readonly CatalogService catalogs;
    private readonly PlayerService players;
    private readonly int maximum;
    private readonly Dictionary<string, GameObject> createdEnemies = new Dictionary<string, GameObject>();
    private readonly Dictionary<EnemyParent, string> enemyIds = new Dictionary<EnemyParent, string>();
    public SpawnService(CatalogService catalogs, PlayerService players, int maximum) { this.catalogs = catalogs; this.players = players; this.maximum = maximum; }
    public void Reset() { createdEnemies.Clear(); enemyIds.Clear(); }
    private static Bounds Shape(GameObject prefab)
    {
        bool any = false; Bounds result = default;
        foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger) continue;
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
            else if (collider is CapsuleCollider capsule)
            {
                var size = Vector3.one * capsule.radius * 2;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2);
                local = new Bounds(capsule.center, size);
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else throw new ApiException(422, "UNSUPPORTED_BOUNDS", "Prefab has an unsupported collision shape.");
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        // Preserve prefab scale and child transforms; spawn rotation is identity.
                        var point = Quaternion.Inverse(prefab.transform.rotation) * (collider.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))) - prefab.transform.position);
                        if (!any) { result = new Bounds(point, Vector3.zero); any = true; } else result.Encapsulate(point);
                    }
        }
        if (!any || result.size.sqrMagnitude < 0.0001f || result.size.magnitude > 40 || float.IsNaN(result.size.sqrMagnitude) || float.IsInfinity(result.size.sqrMagnitude))
            throw new ApiException(422, "UNSUPPORTED_BOUNDS", "No finite, usable collider bounds are available for safe placement.");
        return result;
    }
    private Vector3 Origin(PositionRequest position, NavMeshTriangulation? navigation = null)
    {
        if (position.Mode == "world_position") return new Vector3(position.X!.Value, position.Y!.Value, position.Z!.Value);
        if (position.Mode == "near_player") return players.Resolve(position.PlayerId).transform.position;
        if (position.Mode == "random_player")
        {
            var current = players.Current(); if (current.Count == 0) throw new ApiException(409, "PLAYER_NOT_FOUND", "No current player is available.");
            return current[UnityEngine.Random.Range(0, current.Count)].transform.position;
        }
        var mesh = navigation ?? NavMesh.CalculateTriangulation();
        if (mesh.vertices.Length == 0 || mesh.indices.Length < 3) throw new ApiException(422, "NO_SAFE_POSITION", "No navigation mesh is available.");
        int triangle = UnityEngine.Random.Range(0, mesh.indices.Length / 3) * 3;
        float root = Mathf.Sqrt(UnityEngine.Random.value);
        float fraction = UnityEngine.Random.value;
        return mesh.vertices[mesh.indices[triangle]] * (1 - root)
            + mesh.vertices[mesh.indices[triangle + 1]] * (root * (1 - fraction))
            + mesh.vertices[mesh.indices[triangle + 2]] * (root * fraction);
    }
    private List<Vector3> Positions(PositionRequest request, Bounds bounds, int quantity, bool enemy)
    {
        var positions = new List<Vector3>(); var occupied = new List<Bounds>();
        NavMeshTriangulation? navigation = request.Mode == "random_valid" ? NavMesh.CalculateTriangulation() : (NavMeshTriangulation?)null;
        Vector3 origin = Origin(request, navigation);
        var extents = bounds.extents + Vector3.one * 0.08f;
        for (int i = 0; i < quantity; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                Vector3 candidate = origin;
                if (request.Mode == "random_valid") candidate = Origin(request, navigation);
                else if (request.Mode != "world_position")
                {
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2);
                    float radius = UnityEngine.Random.Range(2f + extents.magnitude, 6f + extents.magnitude);
                    candidate += new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                }
                else if (attempt > 0) break; // Explicit world positions are not silently moved horizontally.
                if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var floor, 6f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.75f || floor.collider.attachedRigidbody != null) continue;
                if (Mathf.Abs(floor.point.y - candidate.y) > 4f) continue;
                var destination = new Vector3(candidate.x, floor.point.y + extents.y - bounds.center.y + 0.05f, candidate.z);
                if (enemy)
                {
                    if (!NavMesh.SamplePosition(floor.point, out var nav, 0.75f, NavMesh.AllAreas) || Vector3.Distance(nav.position, floor.point) > 0.75f) continue;
                }
                var volume = new Bounds(destination + bounds.center, extents * 2);
                if (Physics.CheckBox(volume.center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore) || occupied.Any(b => b.Intersects(volume))) continue;
                occupied.Add(volume); positions.Add(destination); found = true; break;
            }
            if (!found) throw new ApiException(422, "NO_SAFE_POSITION", "No collision-free supported floor position is available for the entire request. No objects were spawned.");
        }
        return positions;
    }
    public object Spawn(string category, string id, int quantity, PositionRequest position)
    {
        string plural = category == "enemy" ? "enemies" : category == "valuable" ? "valuables" : category == "upgrade" ? "upgrades" : "items";
        var entry = catalogs.Resolve(plural, id);
        if (quantity * entry.Prefabs.Length > maximum) throw new ApiException(400, "INVALID_QUANTITY", "Enemy group size multiplied by quantity exceeds the entity limit.");
        // Multi-object enemy setups can spawn overlapping objects at one point. Decline rather than alter REPOLib's setup semantics.
        if (entry.Prefabs.Length != 1) throw new ApiException(422, "UNSUPPORTED_ENEMY_GROUP", "This version only supports enemy setups with one spawn object.");
        var positions = Positions(position, Shape(entry.Prefabs[0]), quantity, category == "enemy");
        var spawned = new List<object>();
        try
        {
            foreach (var destination in positions)
            {
                GameObject? instance;
                if (entry.Enemy != null)
                {
                    var parents = REPOLib.Modules.Enemies.SpawnEnemy(entry.Enemy, destination, Quaternion.identity);
                    if (parents == null || parents.Count != 1 || parents[0] == null) throw new InvalidOperationException("REPOLib did not return the expected enemy.");
                    instance = parents[0].gameObject;
                    string enemyId = System.Guid.NewGuid().ToString("N");
                    createdEnemies.Add(enemyId, instance); enemyIds[parents[0]] = enemyId;
                    spawned.Add(new { id = enemyId, catalogId = id, position = Coordinates(instance.transform.position) });
                }
                else
                {
                    instance = entry.Item != null ? Items.SpawnItem(entry.Item, destination, Quaternion.identity) : Valuables.SpawnValuable(entry.Valuable, destination, Quaternion.identity);
                    if (instance == null) throw new InvalidOperationException("REPOLib returned no spawned object.");
                    spawned.Add(new { catalogId = id, position = Coordinates(instance.transform.position) });
                }
                if (instance == null || instance.GetComponent<PhotonView>() == null) throw new InvalidOperationException("Spawned object has no network view.");
            }
        }
        catch (Exception e) { throw new ApiException(500, "SPAWN_FAILED", "Spawn failed after " + spawned.Count + " confirmed objects; do not retry with a new key without checking the result. " + e.GetType().Name, new { spawned }); }
        return new { spawned, count = spawned.Count, replicationVerified = false };
    }
    private static object Coordinates(Vector3 p) => new { x = p.x, y = p.y, z = p.z };
    public object ListEnemies()
    {
        foreach (var id in createdEnemies.Keys.ToList()) if (createdEnemies[id] == null) createdEnemies.Remove(id);
        foreach (var enemy in enemyIds.Keys.ToList()) if (enemy == null) enemyIds.Remove(enemy!);
        var enemies = Object.FindObjectsOfType<EnemyParent>().Where(e => e != null && e.gameObject.scene.IsValid()).Select(e =>
        {
            if (!enemyIds.TryGetValue(e, out var id)) { id = System.Guid.NewGuid().ToString("N"); enemyIds[e] = id; }
            return new { id, name = e.name, apiSpawned = createdEnemies.ContainsKey(id), position = Coordinates(e.transform.position) };
        }).ToArray();
        return new { enemies };
    }
    public object Despawn(string? id)
    {
        var targets = id == null ? createdEnemies.Keys.ToList() : new List<string> { id };
        if (id != null && (!createdEnemies.TryGetValue(id, out var existing) || existing == null)) throw new ApiException(404, "API_ENEMY_NOT_FOUND", "Only a current API-spawned enemy can be removed.");
        var removed = new List<string>();
        foreach (var key in targets)
        {
            var target = createdEnemies[key];
            if (target == null) { createdEnemies.Remove(key); continue; }
            if (SemiFunc.IsMultiplayer())
            {
                var view = target.GetComponent<PhotonView>();
                if (view == null || (!view.IsRoomView && !view.IsMine)) throw new ApiException(409, "OWNERSHIP_REQUIRED", "The host no longer controls this entity.", new { removed });
                PhotonNetwork.Destroy(target);
            }
            else Object.Destroy(target);
            createdEnemies.Remove(key); removed.Add(key);
        }
        return new { removed, outcome = "destroy_requested", replicationVerified = false };
    }
}
