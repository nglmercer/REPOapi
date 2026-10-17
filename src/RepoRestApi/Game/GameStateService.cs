using System;
using BepInEx;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using RepoRestApi.Core.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace RepoRestApi.Game;
internal sealed class GameStateService : IGameAdapter
{
    private readonly CatalogService catalogs;
    private readonly PlayerService players = new PlayerService();
    private readonly SpawnService spawning;
    private readonly ApiOptions options;
    private object? room;
    private int scene = -1;
    private int generator;
    private int host;
    private bool wasReady;
    private long generation;
    public long Generation => System.Threading.Interlocked.Read(ref generation);
    public GameStateService(ApiOptions options, Action<string> log)
    {
        this.options = options;
        catalogs = new CatalogService(log); spawning = new SpawnService(catalogs, players, options.MaxSpawnQuantity);
    }
    public void ObserveSession()
    {
        int nextScene = SceneManager.GetActiveScene().handle;
        int nextGenerator = LevelGenerator.Instance != null ? LevelGenerator.Instance.GetInstanceID() : 0;
        int nextHost = PhotonNetwork.MasterClient?.ActorNumber ?? 0;
        bool ready = InLevel();
        if (!ReferenceEquals(room, PhotonNetwork.CurrentRoom) || scene != nextScene || generator != nextGenerator || host != nextHost || wasReady != ready)
        { room = PhotonNetwork.CurrentRoom; scene = nextScene; generator = nextGenerator; host = nextHost; wasReady = ready; catalogs.Reset(); players.Reset(); spawning.Reset(); System.Threading.Interlocked.Increment(ref generation); }
    }
    private static bool InLevel()
    {
        var run = RunManager.instance;
        return run != null && run.levelCurrent != null && run.levels != null && run.levels.Contains(run.levelCurrent) && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated;
    }
    public GameState State()
    {
        ObserveSession(); return new GameState { GameRunning = true, InLevel = InLevel(), IsHost = SemiFunc.IsMasterClientOrSingleplayer(), GameVersion = ActualGameVersion() };
    }
    private static string ActualGameVersion() => BuildManager.instance != null && BuildManager.instance.version != null ? BuildManager.instance.version.title : "unknown";
    public object Versions() => new { apiVersion = "1.0.0", modVersion = Plugin.Version, gameVersion = ActualGameVersion(), compiledGameVersion = "0.4.4", bepinexVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString(), repoLibVersion = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("REPOLib", out var dependency) ? dependency.Metadata.Version.ToString() : "unknown", runtimeCompatibilityVerified = false };
    public object Capabilities() => new
    {
        apiVersion = "1.0.0",
        requiresHost = true,
        requiresGeneratedLevel = true,
        multiplayerTested = false,
        catalogs = new[] { "items", "enemies", "valuables", "upgrades" },
        spawn = new { item = true, enemy = true, valuable = true, upgrade = true, upgradeMode = "physical_pickup_only", enemyGroups = false, enemyGroupReason = "Multi-object setups spawn at one origin and cannot be safely separated through the public REPOLib API." },
        players = new { list = true, heal = new { supported = true, outcome = "invoked", effectAcknowledgement = false }, revive = new { supported = true, outcome = "invoked", effectAcknowledgement = false }, teleport = new { supported = false, reason = "Safe handling of dead and tumbling players requires private game state." }, privateHealthState = new { supported = false, reason = "PlayerHealth.health, maxHealth and PlayerAvatar.deadSet are non-public in the game." } },
        enemies = new { list = true, despawnApiOwned = true, clearNative = false },
        positions = new[] { "near_player", "random_player", "random_valid", "world_position" },
        positioning = "Conservative prefab collider bounds, floor checks and enemy NavMesh validation; rejects unknown shapes and occupied positions.",
        idempotency = new { required = true, retentionSeconds = 600, persistentAcrossRestart = false },
        limits = new { maxSpawnQuantity = options.MaxSpawnQuantity, maxRequestBytes = options.MaxRequestBytes, maxRequestsPerSecond = options.MaxRequestsPerSecond, queueCapacity = options.QueueCapacity, commandTimeoutMs = options.RequestTimeoutMs },
        directUpgradeGrant = new { supported = false, reason = "No complete verified native upgrade grant contract is available; use physical upgrade pickups." },
        gameThread = true,
        reflection = false,
        harmonyPatches = false
    };
    public object Catalog(string category, string? search) { State(); return catalogs.List(category, search); }
    public object Players(string? id) { State(); return players.List(id); }
    public object Enemies() { State(); return spawning.ListEnemies(); }
    public object Spawn(string category, string id, int quantity, PositionRequest position) => spawning.Spawn(category, id, quantity, position);
    public object PlayerAction(string action, JObject request) => players.Act(action, request);
    public object Despawn(string? id) => spawning.Despawn(id);
}
