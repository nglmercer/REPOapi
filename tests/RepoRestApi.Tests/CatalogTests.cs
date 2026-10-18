using System;
using System.Collections.Generic;
using RepoRestApi.Core.Models;
using RepoRestApi.Game;
using Xunit;

namespace RepoRestApi.Tests
{
    // These tests execute the real catalog selection code. The doubles below make
    // unrelated prefab loads fail, without pretending to simulate Unity gameplay.
    public sealed class CatalogTests
    {
        public CatalogTests()
        {
            REPOLib.Modules.Items.AllItems.Clear();
            REPOLib.Modules.Items.RegisteredItems.Clear();
            REPOLib.Modules.Valuables.AllValuables.Clear();
            REPOLib.Modules.Valuables.RegisteredValuables.Clear();
            REPOLib.Modules.Enemies.AllEnemies.Clear();
            REPOLib.Modules.Enemies.RegisteredEnemies.Clear();
        }
        private static CatalogService Catalog() => new CatalogService(message => throw new InvalidOperationException(message));
        private static EnemySetup Enemy(string id, PrefabRef prefab) => new EnemySetup { name = id, spawnObjects = new List<PrefabRef> { prefab } };

        [Fact]
        public void EnemyResolutionDoesNotLoadUnrelatedPrefabs()
        {
            var selected = Enemy("selected", new PrefabRef());
            REPOLib.Modules.Enemies.AllEnemies.Add(selected);
            REPOLib.Modules.Enemies.AllEnemies.Add(Enemy("other", new PrefabRef { ThrowOnLoad = true }));
            REPOLib.Modules.Items.AllItems.Add(new Item { prefab = new PrefabRef { ThrowOnLoad = true } });
            REPOLib.Modules.Valuables.AllValuables.Add(new PrefabRef { ThrowOnLoad = true });
            Assert.Same(selected, Catalog().Resolve("enemies", "selected").Enemy);
        }
        [Fact]
        public void ItemResolutionDoesNotLoadOtherItems()
        {
            var selected = new Item { prefab = new PrefabRef { ResourcePath = "selected" } };
            REPOLib.Modules.Items.AllItems.Add(selected);
            REPOLib.Modules.Items.AllItems.Add(new Item { prefab = new PrefabRef { ThrowOnLoad = true } });
            Assert.Same(selected, Catalog().Resolve("items", "selected").Item);
        }
        [Fact]
        public void DuplicateEnemyIdsAreRejected()
        {
            REPOLib.Modules.Enemies.AllEnemies.Add(Enemy("duplicate", new PrefabRef()));
            REPOLib.Modules.Enemies.RegisteredEnemies.Add(Enemy("duplicate", new PrefabRef()));
            Assert.Equal("UNKNOWN_OBJECT_ID", Assert.Throws<ApiException>(() => Catalog().Resolve("enemies", "duplicate")).Code);
        }
        [Fact]
        public void UpgradeResolutionRejectsAmbiguousItemIds()
        {
            REPOLib.Modules.Items.AllItems.Add(new Item { prefab = new PrefabRef { ResourcePath = "duplicate" } });
            REPOLib.Modules.Items.AllItems.Add(new Item { itemType = SemiFunc.itemType.player_upgrade, prefab = new PrefabRef { ResourcePath = "duplicate" } });
            Assert.Throws<ApiException>(() => Catalog().Resolve("upgrades", "duplicate"));
        }
        [Fact]
        public void EnemyCatalogIsCachedWithoutLoadingOtherCategories()
        {
            var prefab = new PrefabRef();
            REPOLib.Modules.Enemies.AllEnemies.Add(Enemy("selected", prefab));
            REPOLib.Modules.Valuables.AllValuables.Add(new PrefabRef { ThrowOnLoad = true });
            var catalog = Catalog();
            var first = catalog.Entries("enemies");
            int loads = prefab.Loads;
            Assert.Same(first, catalog.Entries("enemies"));
            Assert.Equal(loads, prefab.Loads);
        }
        [Fact]
        public void MutationRevalidatesSelectedRegistrationDespiteCachedCatalog()
        {
            var selected = Enemy("selected", new PrefabRef());
            REPOLib.Modules.Enemies.AllEnemies.Add(selected);
            var catalog = Catalog();
            Assert.Single(catalog.Entries("enemies"));
            REPOLib.Modules.Enemies.AllEnemies.Clear();
            Assert.Throws<ApiException>(() => catalog.Resolve("enemies", "selected"));
        }
    }
}

// Minimal API surface for catalog tests only. The plugin builds separately against
// actual game assemblies; these types never ship in the plugin or release archive.
namespace UnityEngine
{
    public static class Time { public static float realtimeSinceStartup => 0; }
    public sealed class GameObject { public T GetComponent<T>() where T : new() => new T(); }
}
namespace Photon.Pun { public sealed class PhotonView { } }
public sealed class ValuableObject { }
public sealed class EnemyParent { }
public static class SemiFunc { public enum itemType { other, player_upgrade, item_upgrade } }
public sealed class PrefabRef
{
    public string ResourcePath = "test";
    public string PrefabName => ResourcePath;
    public bool ThrowOnLoad;
    public int Loads;
    public bool IsValid() => true;
    public UnityEngine.GameObject Prefab
    {
        get
        {
            Loads++;
            if (ThrowOnLoad) throw new InvalidOperationException("Unrelated prefab was loaded");
            return new UnityEngine.GameObject();
        }
    }
}
public sealed class Item
{
    public string itemName = "test";
    public bool disabled;
    public bool physicalItem = true;
    public PrefabRef prefab = new PrefabRef();
    public SemiFunc.itemType itemType;
}
public sealed class EnemySetup
{
    public string name = "test";
    public List<PrefabRef> spawnObjects = new List<PrefabRef>();
}
namespace REPOLib.Modules
{
    public static class Items
    {
        public static readonly List<Item> AllItems = new List<Item>();
        public static readonly List<Item> RegisteredItems = new List<Item>();
    }
    public static class Valuables
    {
        public static readonly List<PrefabRef> AllValuables = new List<PrefabRef>();
        public static readonly List<PrefabRef> RegisteredValuables = new List<PrefabRef>();
    }
    public static class Enemies
    {
        public static readonly List<EnemySetup> AllEnemies = new List<EnemySetup>();
        public static readonly List<EnemySetup> RegisteredEnemies = new List<EnemySetup>();
    }
}
