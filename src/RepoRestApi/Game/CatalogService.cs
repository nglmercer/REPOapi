using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using REPOLib.Modules;
using UnityEngine;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Game;
internal sealed class CatalogEntry
{
    public string Id = "";
    public string Name = "";
    public string Category = "";
    public Item? Item;
    public PrefabRef? Valuable;
    public EnemySetup? Enemy;
    public GameObject[] Prefabs = Array.Empty<GameObject>();
    public object Metadata() => new { id = Id, name = Name, category = Category, spawnable = true, entitiesPerSpawn = Prefabs.Length };
}
internal sealed class CatalogService
{
    private readonly Action<string> log;
    private readonly Dictionary<string, List<CatalogEntry>> cache = new Dictionary<string, List<CatalogEntry>>();
    private readonly Dictionary<string, float> refreshed = new Dictionary<string, float>();
    public CatalogService(Action<string> log) { this.log = log; }
    public void Reset() { cache.Clear(); refreshed.Clear(); }
    private static bool Valid(PrefabRef? p) => p != null && p.IsValid() && !string.IsNullOrWhiteSpace(p.ResourcePath) && p.Prefab != null && p.Prefab.GetComponent<PhotonView>() != null;
    public List<CatalogEntry> Entries(string category)
    {
        if (!refreshed.TryGetValue(category, out float timestamp) || Time.realtimeSinceStartup - timestamp >= 3)
        {
            cache[category] = Build(category);
            refreshed[category] = Time.realtimeSinceStartup;
        }
        return cache[category];
    }
    private List<CatalogEntry> Build(string category, string? id = null)
    {
        var items = new List<CatalogEntry>(); var valuables = new List<CatalogEntry>(); var enemies = new List<CatalogEntry>();
        if (category == "items" || category == "upgrades")
        foreach (var item in Items.AllItems.Concat(Items.RegisteredItems).Distinct())
        {
            try
            {
                if (item == null || item.prefab == null || (id != null && item.prefab.ResourcePath != id) || string.IsNullOrWhiteSpace(item.itemName) || item.disabled || !item.physicalItem || !Valid(item.prefab)) continue;
                items.Add(new CatalogEntry { Id = item.prefab.ResourcePath, Name = item.itemName, Category = IsUpgrade(item) ? "upgrade" : "item", Item = item, Prefabs = new[] { item.prefab.Prefab } });
            }
            catch (Exception e) { log("Invalid item catalog entry: " + e.GetType().Name); }
        }
        if (category == "valuables")
        foreach (var p in Valuables.AllValuables.Concat(Valuables.RegisteredValuables).Distinct())
        {
            try { if (p != null && (id == null || p.ResourcePath == id) && Valid(p) && p.Prefab.GetComponent<ValuableObject>() != null) valuables.Add(new CatalogEntry { Id = p.ResourcePath, Name = p.PrefabName, Category = "valuable", Valuable = p, Prefabs = new[] { p.Prefab } }); }
            catch (Exception e) { log("Invalid valuable catalog entry: " + e.GetType().Name); }
        }
        if (category == "enemies")
        foreach (var enemy in REPOLib.Modules.Enemies.AllEnemies.Concat(REPOLib.Modules.Enemies.RegisteredEnemies).Distinct())
        {
            try
            {
                if (enemy == null || (id != null && enemy.name != id) || enemy.spawnObjects == null || enemy.spawnObjects.Count != 1 || enemy.spawnObjects.Any(p => !Valid(p) || p.Prefab.GetComponent<EnemyParent>() == null)) continue;
                // Setup name is a real asset name, not a hand-maintained enemy alias.
                enemies.Add(new CatalogEntry { Id = enemy.name, Name = enemy.name, Category = "enemy", Enemy = enemy, Prefabs = enemy.spawnObjects.Select(p => p.Prefab).ToArray() });
            }
            catch (Exception e) { log("Invalid enemy catalog entry: " + e.GetType().Name); }
        }
        // Reject ambiguous identifiers instead of selecting an arbitrary third-party asset.
        List<CatalogEntry> Unique(IEnumerable<CatalogEntry> entries) => entries.Where(e => !string.IsNullOrWhiteSpace(e.Id) && e.Id.Length <= 256).GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Select(e => (object?)e.Item ?? e.Valuable ?? (object?)e.Enemy).Distinct().Count() == 1).Select(g => g.First()).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (category == "items") return Unique(items);
        if (category == "upgrades") return Unique(items).Where(e => e.Category == "upgrade").ToList();
        return Unique(category == "enemies" ? enemies : valuables);
    }
    private static bool IsUpgrade(Item item) => item.itemType == SemiFunc.itemType.player_upgrade || item.itemType == SemiFunc.itemType.item_upgrade;
    public object List(string category, string? search)
    {
        var entries = Entries(category).Where(e => string.IsNullOrEmpty(search) || e.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Select(e => e.Metadata()).ToArray();
        return new Dictionary<string, object> { [category] = entries };
    }
    public CatalogEntry Resolve(string category, string id)
    {
        // Read current registrations and reject duplicate IDs, but resolve/load only the
        // requested asset. Full catalog scans here can load unrelated prefabs and hitch Unity.
        var entry = Build(category, id).SingleOrDefault();
        return entry ?? throw new ApiException(404, "UNKNOWN_OBJECT_ID", "ID is not in the current " + category + " catalog.");
    }
}
