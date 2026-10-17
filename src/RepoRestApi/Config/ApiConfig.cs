using BepInEx.Configuration;
using RepoRestApi.Core.Http;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Config;
internal static class ApiConfig
{
    public static ApiOptions Load(ConfigFile config)
    {
        var token = config.Bind("HTTP", "AuthenticationToken", "", "Bearer token. Keep private; never copy into logs.");
        if (string.IsNullOrEmpty(token.Value)) { token.Value = AuthenticationMiddleware.GenerateToken(); config.Save(); }
        var o = new ApiOptions
        {
            Enabled = config.Bind("HTTP", "Enabled", true, "Enable the REST server. Restart required for settings changes.").Value,
            Host = config.Bind("HTTP", "Host", "127.0.0.1", "Literal listen IP; use loopback by default.").Value,
            Port = config.Bind("HTTP", "Port", 17453, "Listen port.").Value,
            RequireAuth = config.Bind("HTTP", "RequireAuth", true, "Require bearer authentication on every endpoint.").Value,
            AllowRemote = config.Bind("HTTP", "AllowRemote", false, "Explicitly allow remote binding with authentication. HTTP is unencrypted; use a trusted VPN or TLS reverse proxy.").Value,
            Token = token.Value,
            MaxRequestsPerSecond = config.Bind("Limits", "MaxRequestsPerSecond", 20, "Global authenticated request limit per rolling second.").Value,
            MaxSpawnQuantity = config.Bind("Limits", "MaxSpawnQuantity", 10, "Maximum spawned entities per command, including enemy groups.").Value,
            MaxRequestBytes = config.Bind("Limits", "MaxRequestBytes", 16384, "Maximum UTF-8 JSON body bytes.").Value,
            QueueCapacity = config.Bind("Limits", "QueueCapacity", 64, "Maximum pending game commands and concurrent HTTP requests.").Value,
            RequestTimeoutMs = config.Bind("Limits", "RequestTimeoutMs", 5000, "Queue deadline and upload timeout in milliseconds.").Value,
            CommandsPerFrame = config.Bind("Limits", "CommandsPerFrame", 2, "Maximum commands processed by each Unity Update.").Value,
            Verbose = config.Bind("Diagnostics", "Verbose", false, "Log action execution durations.").Value
        };
        o.Validate(); return o;
    }
}
