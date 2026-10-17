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
    private float refreshed = -100;
    public CatalogService(Action<string> log) { this.log = log; }
    public void Reset() { cache.Clear(); refreshed = -100; }
    private static bool Valid(PrefabRef? p) => p != null && p.IsValid() && !string.IsNullOrWhiteSpace(p.ResourcePath) && p.Prefab != null && p.Prefab.GetComponent<PhotonView>() != null;
    public List<CatalogEntry> Entries(string category)
    {
        if (Time.realtimeSinceStartup - refreshed >= 3 || cache.Count == 0) Refresh();
        return cache[category];
    }
    private void Refresh()
    {
        var items = new List<CatalogEntry>(); var valuables = new List<CatalogEntry>(); var enemies = new List<CatalogEntry>();
        foreach (var item in Items.AllItems.Concat(Items.RegisteredItems).Distinct())
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemName) || item.disabled || !item.physicalItem || !Valid(item.prefab)) continue;
                items.Add(new CatalogEntry { Id = item.prefab.ResourcePath, Name = item.itemName, Category = IsUpgrade(item) ? "upgrade" : "item", Item = item, Prefabs = new[] { item.prefab.Prefab } });
            }
            catch (Exception e) { log("Invalid item catalog entry: " + e.GetType().Name); }
        }
        foreach (var p in Valuables.AllValuables.Concat(Valuables.RegisteredValuables).Distinct())
        {
            try { if (Valid(p) && p.Prefab.GetComponent<ValuableObject>() != null) valuables.Add(new CatalogEntry { Id = p.ResourcePath, Name = p.PrefabName, Category = "valuable", Valuable = p, Prefabs = new[] { p.Prefab } }); }
            catch (Exception e) { log("Invalid valuable catalog entry: " + e.GetType().Name); }
        }
        foreach (var enemy in REPOLib.Modules.Enemies.AllEnemies.Concat(REPOLib.Modules.Enemies.RegisteredEnemies).Distinct())
        {
            try
            {
                if (enemy == null || enemy.spawnObjects == null || enemy.spawnObjects.Count != 1 || enemy.spawnObjects.Any(p => !Valid(p) || p.Prefab.GetComponent<EnemyParent>() == null)) continue;
                // Setup name is a real asset name, not a hand-maintained enemy alias.
                enemies.Add(new CatalogEntry { Id = enemy.name, Name = enemy.name, Category = "enemy", Enemy = enemy, Prefabs = enemy.spawnObjects.Select(p => p.Prefab).ToArray() });
            }
            catch (Exception e) { log("Invalid enemy catalog entry: " + e.GetType().Name); }
        }
        // Reject ambiguous identifiers instead of selecting an arbitrary third-party asset.
        List<CatalogEntry> Unique(IEnumerable<CatalogEntry> entries) => entries.Where(e => !string.IsNullOrWhiteSpace(e.Id) && e.Id.Length <= 256).GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Select(e => (object?)e.Item ?? e.Valuable ?? (object?)e.Enemy).Distinct().Count() == 1).Select(g => g.First()).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        cache["items"] = Unique(items); cache["upgrades"] = Unique(items.Where(e => e.Category == "upgrade"));
        cache["valuables"] = Unique(valuables); cache["enemies"] = Unique(enemies); refreshed = Time.realtimeSinceStartup;
    }
    private static bool IsUpgrade(Item item) => item.itemType == SemiFunc.itemType.player_upgrade || item.itemType == SemiFunc.itemType.item_upgrade;
    public object List(string category, string? search)
    {
        var entries = Entries(category).Where(e => string.IsNullOrEmpty(search) || e.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Select(e => e.Metadata()).ToArray();
        return new Dictionary<string, object> { [category] = entries };
    }
    public CatalogEntry Resolve(string category, string id)
    {
        // Refresh on every mutation: revalidate registrations and prefabs at execution time.
        Reset(); var entry = Entries(category).SingleOrDefault(e => e.Id == id);
        return entry ?? throw new ApiException(404, "UNKNOWN_OBJECT_ID", "ID is not in the current " + category + " catalog.");
    }
}
