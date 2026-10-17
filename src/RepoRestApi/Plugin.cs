using System;
using BepInEx;
using RepoRestApi.Config;
using RepoRestApi.Core.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace RepoRestApi;
[BepInPlugin(Guid, "R.E.P.O. REST API", Version)]
[BepInDependency("REPOLib", "4.2.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "org.repo.rest-api";
    public const string Version = "1.0.0";
    private ApiOptions? options;
    private ApiRuntime? runtime;
    private bool stopping;
    private void Awake()
    {
        try
        {
            options = ApiConfig.Load(Config);
            if (!options.Enabled) { Logger.LogInfo("REST API disabled in configuration."); return; }
            // On this Unity build the loader's bootstrap object is destroyed before the first
            // scene Update. Create our persistent runner after scene activation instead.
            SceneManager.sceneLoaded += SceneLoaded;
            Application.quitting += Shutdown;
        }
        catch (Exception e) { Logger.LogError("REST configuration failed: " + e.Message); }
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => StartRuntime();
    private void Start() => StartRuntime(); // Also supports loaders attaching after sceneLoaded.
    private void StartRuntime()
    {
        if (stopping || options == null || !options.Enabled || runtime != null) return;
        SceneManager.sceneLoaded -= SceneLoaded;
        var owner = new GameObject("RepoRestApi.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(owner);
        runtime = owner.AddComponent<ApiRuntime>();
        runtime.Initialize(options, message => Logger.LogInfo(message), message => Logger.LogWarning(message));
    }
    private void OnApplicationQuit() => Shutdown();
    private void OnDestroy()
    {
        // Bootstrap destruction before scene activation is not application shutdown.
        // The one-shot scene callback starts the independently owned runtime.
        if (runtime != null) Shutdown();
    }
    private void Shutdown()
    {
        stopping = true;
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.quitting -= Shutdown;
        runtime?.Shutdown();
    }
}
