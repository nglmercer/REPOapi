using System;
using RepoRestApi.Core.Game;
using RepoRestApi.Core.Http;
using RepoRestApi.Core.Models;
using RepoRestApi.Game;
using UnityEngine;
namespace RepoRestApi;
public sealed class ApiRuntime : MonoBehaviour
{
    private MainThreadDispatcher? dispatcher;
    private RestServer? server;
    private ApiOptions? options;
    private GameStateService? game;
    private Action<string>? log;
    public void Initialize(ApiOptions options, Action<string> log, Action<string> warning)
    {
        this.options = options; this.log = log;
        try
        {
            dispatcher = new MainThreadDispatcher(options.QueueCapacity);
            game = new GameStateService(options, warning);
            game.ObserveSession();
            var router = new ApiRouter(options, game, dispatcher, log);
            server = new RestServer(options, router, log);
            server.Start();
            log("REPOLib 4.2.0 integration initialized. Multiplayer replication remains unverified.");
        }
        catch (Exception e) { warning("REST API startup failed: " + e.GetType().Name + ": " + e.Message); Shutdown(); }
    }
    private void Update()
    {
        game?.ObserveSession();
        dispatcher?.Pump(options?.CommandsPerFrame ?? 2);
    }
    private void OnApplicationQuit() => Shutdown();
    private void OnDestroy() => Shutdown();
    public void Shutdown()
    {
        dispatcher?.Dispose(); dispatcher = null;
        try { server?.Dispose(); } catch (Exception e) { log?.Invoke("REST shutdown: " + e.GetType().Name); }
        server = null; game = null;
    }
}
