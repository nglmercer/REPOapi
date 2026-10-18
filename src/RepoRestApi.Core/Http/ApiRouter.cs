using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RepoRestApi.Core.Game;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Core.Http;
public sealed class ApiRouter
{
    private static readonly HashSet<string> WritePaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "/api/spawn/item", "/api/spawn/enemy", "/api/spawn/valuable", "/api/spawn/upgrade",
        "/api/player/heal", "/api/player/revive", "/api/player/teleport", "/api/enemies/clear", "/api/enemies/despawn"
    };
    private static readonly Lazy<ApiResponse> OpenApi = new Lazy<ApiResponse>(() =>
    {
        using var stream = typeof(ApiRouter).Assembly.GetManifestResourceStream("openapi.json")!;
        using var reader = new StreamReader(stream);
        return ApiResponse.Raw(reader.ReadToEnd());
    });
    private readonly ApiOptions options;
    private readonly IGameAdapter game;
    private readonly MainThreadDispatcher dispatcher;
    private readonly RateLimiter limiter;
    private readonly IdempotencyStore duplicates = new IdempotencyStore();
    private readonly Action<string>? log;
    public ApiRouter(ApiOptions options, IGameAdapter game, MainThreadDispatcher dispatcher, Action<string>? log = null)
    { this.options = options; this.game = game; this.dispatcher = dispatcher; this.log = log; limiter = new RateLimiter(options.MaxRequestsPerSecond); }
    public async Task<ApiResponse> Handle(string method, string target, string? authorization, string? idempotencyKey = null, string body = "", string? contentType = null)
    {
        try
        {
            if (options.RequireAuth && !AuthenticationMiddleware.Authenticate(authorization, options.Token)) throw new ApiException(401, "UNAUTHORIZED", "A valid bearer token is required.");
            if (!limiter.Allow()) throw new ApiException(429, "RATE_LIMITED", "Request rate exceeded; retry later.");
            if (Encoding.UTF8.GetByteCount(body) > options.MaxRequestBytes) throw new ApiException(413, "PAYLOAD_TOO_LARGE", "Request body exceeds the configured limit.");
            int split = target.IndexOf('?');
            string path = split < 0 ? target : target.Substring(0, split);
            string? search = null;
            if (split >= 0 && CatalogCategory(path) == null) throw new ApiException(400, "INVALID_QUERY", "Filters are only supported on catalog endpoints.");
            var queryKeys = new HashSet<string>(StringComparer.Ordinal);
            if (split >= 0)
                foreach (string pair in target.Substring(split + 1).Split('&'))
                {
                    var p = pair.Split(new[] { '=' }, 2);
                    if (!queryKeys.Add(p[0])) throw new ApiException(400, "INVALID_QUERY", "Duplicate query filters are not supported.");
                    if (p[0] != "search" && p[0] != "category") throw new ApiException(400, "INVALID_QUERY", "Only search and category filters are supported.");
                    if (p[0] == "search") search = Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : "");
                    if (p[0] == "category" && (p.Length < 2 || p[1] != path.Substring(path.LastIndexOf('/') + 1))) throw new ApiException(400, "INVALID_QUERY", "category must match the catalog endpoint.");
                }
            bool read = path == "/health" || path == "/api/version" || path == "/api/capabilities" || path == "/api/game/state" || path == "/openapi.json" || CatalogCategory(path) != null || path == "/api/players" || (path.StartsWith("/api/players/", StringComparison.Ordinal) && path.Length > "/api/players/".Length && path.IndexOf('/', "/api/players/".Length) < 0) || path == "/api/enemies";
            bool write = WritePaths.Contains(path);
            if (!read && !write) throw new ApiException(404, "NOT_FOUND", "Unknown endpoint.");
            if (method != (read ? "GET" : "POST")) throw new ApiException(405, "METHOD_NOT_ALLOWED", read ? "This endpoint requires GET." : "This endpoint requires POST.");
            if (path == "/openapi.json") return OpenApi.Value;
            if (read)
                return await dispatcher.Execute(() => Read(path, search), options.RequestTimeoutMs).ConfigureAwait(false);
            if (contentType == null || !contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase)) throw new ApiException(415, "UNSUPPORTED_MEDIA_TYPE", "POST requires application/json.");
            JObject request = Parse(body);
            Validate(path, request);
            long generation = game.Generation;
            return await duplicates.Run(idempotencyKey, path + "\n" + request.ToString(Formatting.None, Array.Empty<JsonConverter>()), () => dispatcher.Execute(() => Mutate(path, request, generation), options.RequestTimeoutMs)).ConfigureAwait(false);
        }
        catch (ApiException e) { log?.Invoke(e.Code); return ApiResponse.Error(e); }
        catch (Exception e) { log?.Invoke("Request failed: " + e.GetType().Name); return ApiResponse.Error(new ApiException(500, "INTERNAL_ERROR", "Request failed; inspect the game log.")); }
    }
    private static string? CatalogCategory(string path) => path == "/api/catalog/items" ? "items" : path == "/api/catalog/enemies" ? "enemies" : path == "/api/catalog/valuables" ? "valuables" : path == "/api/catalog/upgrades" ? "upgrades" : null;
    private ApiResponse Read(string path, string? search)
    {
        if (path == "/api/version") return new ApiResponse(200, game.Versions());
        if (path == "/api/capabilities") return new ApiResponse(200, game.Capabilities());
        if (path == "/health" || path == "/api/game/state") { var s = game.State(); return new ApiResponse(200, new { status = "ready", gameRunning = s.GameRunning, inLevel = s.InLevel, isHost = s.IsHost, gameplayReady = s.InLevel && s.IsHost, apiVersion = "1.0.0" }); }
        string? category = CatalogCategory(path);
        if (category != null) return new ApiResponse(200, game.Catalog(category, search));
        if (path == "/api/enemies") return new ApiResponse(200, game.Enemies());
        return new ApiResponse(200, game.Players(path == "/api/players" ? null : Uri.UnescapeDataString(path.Substring("/api/players/".Length))));
    }
    private ApiResponse Mutate(string path, JObject request, long generation)
    {
        var state = game.State();
        if (game.Generation != generation) throw new ApiException(409, "GAME_SESSION_CHANGED", "The game session changed while this command was queued; refresh game state and retry with a new key.");
        if (!state.InLevel) throw new ApiException(409, "GAME_NOT_READY", "No active generated gameplay level is available.");
        if (!state.IsHost) throw new ApiException(409, "HOST_REQUIRED", "This action requires the host or singleplayer instance.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            object result;
            if (path.StartsWith("/api/spawn/", StringComparison.Ordinal))
            {
                string kind = path.Substring("/api/spawn/".Length);
                result = game.Spawn(kind, request.Value<string>(kind + "Id")!, request.Value<int?>("quantity") ?? 1, request["position"]!.ToObject<PositionRequest>()!);
            }
            else if (path.StartsWith("/api/player/", StringComparison.Ordinal)) result = game.PlayerAction(path.Substring("/api/player/".Length), request);
            else result = game.Despawn(path == "/api/enemies/clear" ? null : request.Value<string>("enemyId"));
            return new ApiResponse(200, new { success = true, result });
        }
        catch (Exception e) { log?.Invoke("Game action failed: " + e.GetType().Name + ": " + e.Message); throw; }
        finally { if (options.Verbose) log?.Invoke(path + " completed in " + clock.ElapsedMilliseconds + "ms"); }
    }
    private void Validate(string path, JObject r)
    {
        string[] allowed;
        if (path.StartsWith("/api/spawn/", StringComparison.Ordinal))
        {
            string kind = path.Substring("/api/spawn/".Length); allowed = new[] { kind + "Id", "quantity", "position" };
            RequiredString(r, kind + "Id");
            if (r["quantity"] != null && (r["quantity"]!.Type != JTokenType.Integer || !InRange(r["quantity"]!, 1, options.MaxSpawnQuantity))) throw new ApiException(400, "INVALID_QUANTITY", "quantity must be an integer from 1 to " + options.MaxSpawnQuantity + ".");
            ValidatePosition(r["position"]);
        }
        else if (path.StartsWith("/api/player/", StringComparison.Ordinal))
        {
            allowed = path.EndsWith("/heal", StringComparison.Ordinal) ? new[] { "playerId", "amount" } : path.EndsWith("/teleport", StringComparison.Ordinal) ? new[] { "playerId", "position" } : new[] { "playerId" };
            RequiredString(r, "playerId");
            if (path.EndsWith("/heal", StringComparison.Ordinal) && (r["amount"]?.Type != JTokenType.Integer || !InRange(r["amount"]!, 1, 1000))) throw new ApiException(400, "INVALID_AMOUNT", "amount must be an integer from 1 to 1000.");
            if (path.EndsWith("/teleport", StringComparison.Ordinal)) ValidatePosition(r["position"]);
        }
        else { allowed = path.EndsWith("/despawn", StringComparison.Ordinal) ? new[] { "enemyId" } : Array.Empty<string>(); if (allowed.Length > 0) RequiredString(r, "enemyId"); }
        if (r.Properties().Any(p => !allowed.Contains(p.Name))) throw new ApiException(400, "UNKNOWN_FIELD", "Request contains an unknown field.");
    }
    private static bool InRange(JToken value, long min, long max) => long.TryParse(value.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long number) && number >= min && number <= max;
    private static void RequiredString(JObject r, string key) { if (r[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(r.Value<string>(key)) || r.Value<string>(key)!.Length > 256) throw new ApiException(400, "INVALID_REQUEST", key + " must be a nonempty string of at most 256 characters."); }
    private static void ValidatePosition(JToken? token)
    {
        if (!(token is JObject p) || p.Properties().Any(v => !new[] { "mode", "playerId", "x", "y", "z" }.Contains(v.Name))) throw new ApiException(400, "INVALID_POSITION", "A position object with mode is required.");
        RequiredString(p, "mode");
        if (p["playerId"] != null) RequiredString(p, "playerId");
        foreach (string axis in new[] { "x", "y", "z" }) if (p[axis] != null && p[axis]!.Type != JTokenType.Float && p[axis]!.Type != JTokenType.Integer) throw new ApiException(400, "INVALID_POSITION", "Coordinates must be numbers.");
        string? mode = p.Value<string>("mode");
        string[] allowed = mode == "near_player" ? new[] { "mode", "playerId" } : mode == "world_position" ? new[] { "mode", "x", "y", "z" } : new[] { "mode" };
        if (p.Properties().Any(v => !allowed.Contains(v.Name))) throw new ApiException(400, "INVALID_POSITION", "Position fields must match the selected mode.");
        try { p.ToObject<PositionRequest>()!.Validate(); } catch (JsonException) { throw new ApiException(400, "INVALID_POSITION", "Invalid position values."); }
    }
    // JObject discards comments between properties even with CommentHandling.Load.
    // Reject them at the reader boundary so nested/property comments cannot slip through.
    private sealed class RequestJsonReader : JsonTextReader
    {
        public RequestJsonReader(TextReader reader) : base(reader) { }
        public override bool Read()
        {
            bool read = base.Read();
            if (TokenType == JsonToken.Comment) throw new JsonReaderException("Comments are not allowed.");
            return read;
        }
    }
    private static JObject Parse(string body)
    {
        try
        {
            using var sr = new StringReader(body); using var reader = new RequestJsonReader(sr) { MaxDepth = 16, DateParseHandling = DateParseHandling.None };
            var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            if (!(token is JObject result) || reader.Read() || result.DescendantsAndSelf().Any(t => t.Type == JTokenType.Comment)) throw new JsonReaderException();
            return result;
        }
        catch (JsonException) { throw new ApiException(400, "INVALID_JSON", "Expected one JSON object without duplicate keys or comments (maximum depth 16)."); }
    }
}
