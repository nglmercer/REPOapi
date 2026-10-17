using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using RepoRestApi.Core.Models;
using UnityEngine;
namespace RepoRestApi.Game;
internal sealed class PlayerService
{
    private readonly Dictionary<PlayerAvatar, string> ids = new Dictionary<PlayerAvatar, string>();
    public void Reset() => ids.Clear();
    public List<PlayerAvatar> Current()
    {
        var players = (SemiFunc.PlayerGetList() ?? new List<PlayerAvatar>()).Where(p => p != null && p.gameObject.scene.IsValid() && p.gameObject.scene.isLoaded && (!PhotonNetwork.InRoom || (p.photonView != null && p.photonView.Owner != null && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.Players.ContainsKey(p.photonView.Owner.ActorNumber)))).Distinct().ToList();
        foreach (var old in ids.Keys.ToList()) if (!players.Contains(old)) ids.Remove(old);
        foreach (var p in players) if (!ids.ContainsKey(p)) ids[p] = System.Guid.NewGuid().ToString("N");
        return players;
    }
    public PlayerAvatar Resolve(string? id)
    {
        var player = Current().FirstOrDefault(p => ids[p] == id);
        return player ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "Player is unavailable or disconnected; refresh /api/players.");
    }
    private object Describe(PlayerAvatar player) => new { id = ids[player], name = SemiFunc.PlayerGetName(player), position = new { x = player.transform.position.x, y = player.transform.position.y, z = player.transform.position.z } };
    public object List(string? id) => id == null ? (object)new { players = Current().Select(Describe).ToArray() } : Describe(Resolve(id));
    public object Act(string action, JObject request)
    {
        var player = Resolve(request.Value<string>("playerId"));
        switch (action)
        {
            case "heal":
                if (player.playerHealth == null) throw new ApiException(409, "PLAYER_NOT_READY", "Player health is unavailable.");
                player.playerHealth.HealOther(request.Value<int>("amount"), true); break;
            case "revive": player.Revive(false); break;
            case "teleport":
                // Public Spawn is used by the reference implementation. Death-body teleport requires private state.
                throw new ApiException(501, "UNSUPPORTED_ACTION", "Safe teleport for both living and dead players requires private death/tumble state; this version does not expose it.");
            default: throw new ApiException(501, "UNSUPPORTED_ACTION", "Unsupported player action.");
        }
        // These public methods return void and route effects via RPC. Do not claim an owner acknowledgement.
        return new { playerId = ids[player], action, outcome = "invoked", effectConfirmed = false };
    }
}
