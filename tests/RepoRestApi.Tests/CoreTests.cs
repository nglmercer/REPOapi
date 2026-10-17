using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RepoRestApi.Core.Game;
using RepoRestApi.Core.Http;
using RepoRestApi.Core.Models;
using Xunit;
namespace RepoRestApi.Tests;
public sealed class FakeGame : IGameAdapter
{
    public long Generation { get; set; }
    public bool Host = true;
    public bool Ready = true;
    public int Spawns;
    public int Thread;
    public GameState State() => new GameState { InLevel = Ready, IsHost = Host, GameRunning = true };
    public object Versions() => new { apiVersion = "1.0.0" };
    public object Capabilities() => new { multiplayerTested = false };
    public object Catalog(string category, string? search) => new { items = new[] { new { id = "registered" } } };
    public object Players(string? id) => new { players = Array.Empty<object>() };
    public object Enemies() => new { enemies = Array.Empty<object>() };
    public object Spawn(string category, string id, int quantity, PositionRequest position)
    {
        if (id != "registered") throw new ApiException(404, "UNKNOWN_OBJECT_ID", "Unknown object.");
        Thread = Environment.CurrentManagedThreadId; Interlocked.Increment(ref Spawns); return new { count = quantity };
    }
    public object PlayerAction(string action, JObject request) => throw new ApiException(501, "UNSUPPORTED_ACTION", "Unavailable.");
    public object Despawn(string? id) => new { removed = Array.Empty<string>() };
}
public sealed class CoreTests
{
    private const string Body = "{\"itemId\":\"registered\",\"quantity\":1,\"position\":{\"mode\":\"random_valid\"}}";
    private const string Token = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJ";
    private static ApiOptions Options() => new ApiOptions { Token = Token, MaxRequestsPerSecond = 1000, RequestTimeoutMs = 1000 };
    private static async Task<ApiResponse> Complete(Task<ApiResponse> task, MainThreadDispatcher dispatcher)
    {
        for (int i = 0; !task.IsCompleted && i < 500; i++) { dispatcher.Pump(64); await Task.Delay(2); }
        return await task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    private static Task<ApiResponse> Post(ApiRouter router, string body = Body, string key = "test") => router.Handle("POST", "/api/spawn/item", "Bearer " + Token, key, body, "application/json");
    [Fact]
    public void AuthenticationUsesExactToken()
    { Assert.True(AuthenticationMiddleware.Authenticate("Bearer " + Token, Token)); Assert.False(AuthenticationMiddleware.Authenticate("Bearer wrong", Token)); Assert.False(AuthenticationMiddleware.Authenticate(null, Token)); Assert.NotEqual(AuthenticationMiddleware.GenerateToken(), AuthenticationMiddleware.GenerateToken()); }
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/game/state")]
    [InlineData("/api/version")]
    [InlineData("/api/capabilities")]
    [InlineData("/api/catalog/items")]
    [InlineData("/api/players")]
    [InlineData("/api/enemies")]
    [InlineData("/openapi.json")]
    public async Task ReadRoutes(string path)
    { using var d = new MainThreadDispatcher(64); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Equal(200, (await Complete(r.Handle("GET", path, "Bearer " + Token), d)).Status); }
    [Fact]
    public async Task HealthSeparatesGameReadiness()
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame { Ready = false }, d); var response = await Complete(r.Handle("GET", "/health", "Bearer " + Token), d); var json = JObject.Parse(response.Json); Assert.Equal("ready", json.Value<string>("status")); Assert.False(json.Value<bool>("gameplayReady")); }
    [Theory]
    [InlineData("PUT", "/health", 405)]
    [InlineData("POST", "/api/unknown", 404)]
    [InlineData("GET", "/api/spawn/item", 405)]
    public async Task RejectsUnknownRoutesAndMethods(string method, string path, int status)
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Equal(status, (await r.Handle(method, path, "Bearer " + Token)).Status); }
    [Fact]
    public async Task UnauthorizedRequestNeverQueues()
    { using var d = new MainThreadDispatcher(1); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Equal(401, (await r.Handle("GET", "/health", "Bearer bad")).Status); }
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{} {}")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public async Task InvalidJson(string body)
    { using var d = new MainThreadDispatcher(1); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Contains("INVALID_JSON", (await Post(r, body)).Json); }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task QuantityLimit(int quantity)
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame(), d); var body = JObject.Parse(Body); body["quantity"] = quantity; Assert.Contains("INVALID_QUANTITY", (await Post(r, body.ToString())).Json); }
    [Fact]
    public async Task UnknownObject()
    { using var d = new MainThreadDispatcher(4); var game = new FakeGame(); var r = new ApiRouter(Options(), game, d); Assert.Contains("UNKNOWN_OBJECT_ID", (await Complete(Post(r, Body.Replace("registered", "missing")), d)).Json); Assert.Equal(0, game.Spawns); }
    [Fact]
    public async Task HostRestriction()
    { using var d = new MainThreadDispatcher(4); var game = new FakeGame { Host = false }; var r = new ApiRouter(Options(), game, d); Assert.Contains("HOST_REQUIRED", (await Complete(Post(r), d)).Json); Assert.Equal(0, game.Spawns); }
    [Fact]
    public async Task SessionChangeInvalidatesQueuedMutation()
    { using var d = new MainThreadDispatcher(4); var game = new FakeGame(); var r = new ApiRouter(Options(), game, d); var pending = Post(r); game.Generation++; Assert.Contains("GAME_SESSION_CHANGED", (await Complete(pending, d)).Json); Assert.Equal(0, game.Spawns); }
    [Fact]
    public async Task HugeQuantityIsValidationError()
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Equal(400, (await Post(r, Body.Replace("\"quantity\":1", "\"quantity\":99999999999999999999999999999999999"))).Status); }
    [Fact]
    public async Task LevelRestriction()
    { using var d = new MainThreadDispatcher(4); var game = new FakeGame { Ready = false }; var r = new ApiRouter(Options(), game, d); Assert.Contains("GAME_NOT_READY", (await Complete(Post(r), d)).Json); Assert.Equal(0, game.Spawns); }
    [Fact]
    public async Task DuplicateRequestsShareOneExecution()
    {
        using var d = new MainThreadDispatcher(4); var game = new FakeGame(); var r = new ApiRouter(Options(), game, d);
        var tasks = Enumerable.Range(0, 32).Select(_ => Post(r)).ToArray();
        await Complete(tasks[0], d); var results = await Task.WhenAll(tasks); Assert.Equal(1, game.Spawns); Assert.All(results, x => Assert.Equal(results[0].Json, x.Json));
        Assert.Equal(409, (await Post(r, Body.Replace("registered", "different"))).Status);
    }
    [Fact]
    public async Task MissingIdempotencyKey()
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", (await r.Handle("POST", "/api/spawn/item", "Bearer " + Token, null, Body, "application/json")).Json); }
    [Fact]
    public async Task RateLimit()
    { var o = Options(); o.MaxRequestsPerSecond = 1; using var d = new MainThreadDispatcher(4); var r = new ApiRouter(o, new FakeGame(), d); await Complete(r.Handle("GET", "/health", "Bearer " + Token), d); Assert.Equal(429, (await r.Handle("GET", "/health", "Bearer " + Token)).Status); }
    [Fact]
    public async Task QueueCapacityAndShutdown()
    { using var d = new MainThreadDispatcher(1); var first = d.Execute(() => new ApiResponse(200, new { }), 5000); var full = await Assert.ThrowsAsync<ApiException>(() => d.Execute(() => new ApiResponse(200, new { }), 100)); Assert.Equal("QUEUE_FULL", full.Code); d.Dispose(); Assert.Equal("SHUTTING_DOWN", (await Assert.ThrowsAsync<ApiException>(() => first)).Code); Assert.Equal("SHUTTING_DOWN", (await Assert.ThrowsAsync<ApiException>(() => d.Execute(() => new ApiResponse(200, new { }), 100))).Code); }
    [Fact]
    public async Task TimeoutCannotExecuteLater()
    { using var d = new MainThreadDispatcher(1); bool ran = false; var pending = d.Execute(() => { ran = true; return new ApiResponse(200, new { }); }, 30); Assert.Equal("COMMAND_TIMEOUT", (await Assert.ThrowsAsync<ApiException>(() => pending)).Code); d.Pump(1); Assert.False(ran); var next = d.Execute(() => new ApiResponse(200, new { }), 1000); d.Pump(1); Assert.Equal(200, (await next).Status); }
    [Fact]
    public async Task ExecutingActionReturnsActualOutcomeAfterDeadline()
    { using var d = new MainThreadDispatcher(1); var pending = d.Execute(() => { Thread.Sleep(80); return new ApiResponse(200, new { executed = true }); }, 50); d.Pump(1); Assert.Equal(200, (await pending).Status); }
    [Fact]
    public async Task ActionRunsOnPumpThread()
    { using var d = new MainThreadDispatcher(2); var game = new FakeGame(); var r = new ApiRouter(Options(), game, d); var task = Post(r); int thread = Environment.CurrentManagedThreadId; d.Pump(1); await task; Assert.Equal(thread, game.Thread); }
    [Fact]
    public async Task UnsupportedPlayerAction()
    { using var d = new MainThreadDispatcher(4); var r = new ApiRouter(Options(), new FakeGame(), d); var response = await Complete(r.Handle("POST", "/api/player/teleport", "Bearer " + Token, "teleport", "{\"playerId\":\"current\",\"position\":{\"mode\":\"world_position\",\"x\":0,\"y\":1,\"z\":0}}", "application/json"), d); Assert.Contains("UNSUPPORTED_ACTION", response.Json); }
    [Theory]
    [InlineData("{\"mode\":\"world_position\",\"x\":\"NaN\",\"y\":0,\"z\":0}")]
    [InlineData("{\"mode\":\"world_position\",\"x\":1e300,\"y\":0,\"z\":0}")]
    [InlineData("{\"mode\":\"near_player\"}")]
    [InlineData("{\"mode\":\"invented\"}")]
    public async Task PositionValidation(string position)
    { using var d = new MainThreadDispatcher(2); var r = new ApiRouter(Options(), new FakeGame(), d); Assert.Equal(400, (await Post(r, "{\"itemId\":\"registered\",\"position\":" + position + "}")).Status); }
    [Fact]
    public async Task PayloadAndMediaLimits()
    { var o = Options(); o.MaxRequestBytes = 128; using var d = new MainThreadDispatcher(1); var r = new ApiRouter(o, new FakeGame(), d); Assert.Equal(413, (await Post(r, new string('x', 129))).Status); Assert.Equal(415, (await r.Handle("POST", "/api/spawn/item", "Bearer " + Token, "x", Body, "text/plain")).Status); }
    [Fact]
    public void RemoteBindingRequiresOptInAndAuthentication()
    { var o = Options(); o.Host = "0.0.0.0"; Assert.Throws<ArgumentException>(o.Validate); o.AllowRemote = true; o.RequireAuth = false; Assert.Throws<ArgumentException>(o.Validate); o.RequireAuth = true; o.Validate(); }
    [Fact]
    public async Task RealHttpConcurrentRequestsAndCleanShutdown()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var o = Options(); o.Port = port; using var d = new MainThreadDispatcher(64); var game = new FakeGame(); var r = new ApiRouter(o, game, d); using var server = new RestServer(o, r, _ => { }); server.Start();
        using var client = new HttpClient(); client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        var requests = Enumerable.Range(0, 12).Select(async i => { using var message = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/api/spawn/item"); message.Headers.Add("Idempotency-Key", "http-" + i); message.Content = new StringContent(Body, Encoding.UTF8, "application/json"); return await client.SendAsync(message); }).ToArray();
        for (int i = 0; !Task.WhenAll(requests).IsCompleted && i < 1000; i++) { d.Pump(64); await Task.Delay(2); }
        var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(3)); Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode)); Assert.Equal(12, game.Spawns); foreach (var response in responses) response.Dispose();
        d.Dispose(); await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }
}
