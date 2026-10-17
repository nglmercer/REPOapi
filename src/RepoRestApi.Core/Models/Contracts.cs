using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace RepoRestApi.Core.Models;
public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public object? Details { get; }
    public ApiException(int status, string code, string message, object? details = null) : base(message) { Status = status; Code = code; Details = details; }
}
public sealed class ApiOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17453;
    public bool RequireAuth { get; set; } = true;
    public bool AllowRemote { get; set; }
    public string Token { get; set; } = "";
    public int MaxRequestsPerSecond { get; set; } = 20;
    public int MaxSpawnQuantity { get; set; } = 10;
    public int MaxRequestBytes { get; set; } = 16384;
    public int QueueCapacity { get; set; } = 64;
    public int RequestTimeoutMs { get; set; } = 5000;
    public int CommandsPerFrame { get; set; } = 2;
    public bool Verbose { get; set; }
    public void Validate()
    {
        if (!System.Net.IPAddress.TryParse(Host, out var address)) throw new ArgumentException("Host must be a literal IP address.");
        if (!System.Net.IPAddress.IsLoopback(address) && (!AllowRemote || !RequireAuth || Token.Length < 43))
            throw new ArgumentException("Remote binding requires AllowRemote, authentication and a token of at least 43 characters.");
        if (RequireAuth && Token.Length < 43) throw new ArgumentException("Authentication requires a strong token of at least 43 characters.");
        if (Port < 1 || Port > 65535 || MaxRequestsPerSecond < 1 || MaxSpawnQuantity < 1 || MaxSpawnQuantity > 100 || MaxRequestBytes < 128 || MaxRequestBytes > 1048576 || QueueCapacity < 1 || QueueCapacity > 4096 || RequestTimeoutMs < 100 || RequestTimeoutMs > 60000 || CommandsPerFrame < 1 || CommandsPerFrame > 10)
            throw new ArgumentException("Configuration limits are out of range.");
    }
}
public sealed class GameState
{
    public bool GameRunning { get; set; }
    public bool InLevel { get; set; }
    public bool IsHost { get; set; }
    public string GameVersion { get; set; } = "unknown";
}
public sealed class PositionRequest
{
    public string Mode { get; set; } = "near_player";
    public string? PlayerId { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? Z { get; set; }
    public void Validate()
    {
        if (Mode != "near_player" && Mode != "random_player" && Mode != "random_valid" && Mode != "world_position") throw new ApiException(400, "INVALID_POSITION", "Unknown position mode.");
        if (Mode == "near_player" && string.IsNullOrWhiteSpace(PlayerId)) throw new ApiException(400, "INVALID_POSITION", "near_player requires playerId.");
        if (Mode == "world_position" && (!Finite(X) || !Finite(Y) || !Finite(Z))) throw new ApiException(400, "INVALID_POSITION", "Finite x, y and z within +/-10000 are required.");
    }
    private static bool Finite(float? v) => v.HasValue && !float.IsNaN(v.Value) && !float.IsInfinity(v.Value) && Math.Abs(v.Value) <= 10000;
}
public interface IGameAdapter
{
    long Generation { get; }
    GameState State();
    object Versions();
    object Capabilities();
    object Catalog(string category, string? search);
    object Players(string? id);
    object Enemies();
    object Spawn(string category, string id, int quantity, PositionRequest position);
    object PlayerAction(string action, JObject request);
    object Despawn(string? id);
}
