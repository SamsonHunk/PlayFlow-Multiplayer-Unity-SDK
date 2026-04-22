using System;
using System.Collections;
using System.Collections.Generic;

namespace PlayFlow
{
    // Network interfaces
    public interface INetworkManager
    {
        IEnumerator Get(string url, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null);
        IEnumerator Post(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null);
        IEnumerator Put(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null);
        IEnumerator Delete(string url, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null);
        // V3: DELETE with body is required for some V3 endpoints (none currently, but kept for parity with old code path).
        IEnumerator Patch(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null);
    }
    
    public interface ILobbyAPI
    {
        // V3: the x-player-id header is required on every request. The manager should call SetActivePlayerId
        // whenever the local player's identity changes so that endpoints like GetLobby/ListLobbies
        // (which don't take a playerId parameter) can still send a valid header.
        string ActivePlayerId { get; }
        void SetActivePlayerId(string playerId);

        // Create lobby. `playerId` is used for the `x-player-id` header (V3 resolves host from it).
        // When `forceFresh` is true, the backend deletes any lobby the caller already hosts in
        // this config before creating the new one. No-op when the caller has no prior lobby.
        IEnumerator CreateLobby(
            string configName,
            string lobbyName,
            int maxPlayers,
            bool isPrivate,
            bool allowLateJoin,
            string region,
            Dictionary<string, object> customSettings,
            bool forceFresh,
            string playerId,
            Action<Lobby> onSuccess,
            Action<string> onError);

        IEnumerator JoinLobby(string lobbyId, string playerId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator JoinLobbyByCode(string inviteCode, string playerId, Action<Lobby> onSuccess, Action<string> onError);

        // V3 /me routes — lobbyId parameter is ignored on the wire (resolved from x-player-id header),
        // but kept in the signature for Phase 3 manager compatibility.
        IEnumerator LeaveLobby(string lobbyId, string playerId, Action onSuccess, Action<string> onError);
        IEnumerator GetLobby(string lobbyId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator ListLobbies(Action<List<Lobby>> onSuccess, Action<string> onError);
        IEnumerator UpdatePlayerState(string lobbyId, string requesterId, string targetPlayerId, Dictionary<string, object> state, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator KickPlayer(string lobbyId, string requesterId, string playerToKickId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator UpdateLobby(string lobbyId, string requesterId, Newtonsoft.Json.Linq.JObject payload, Action<Lobby> onSuccess, Action<string> onError);

        // V3 lifecycle operations (host-only).
        IEnumerator StartMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator EndMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator FindMatch(string lobbyId, string requesterId, string mode, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator CancelMatchmaking(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator ConfirmMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError);
        IEnumerator DeclineMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError);

        // V3: GET /me with x-player-id = playerId. V3 only allows querying your own lobby.
        IEnumerator FindLobbyByPlayerId(string playerId, Action<Lobby> onSuccess, Action<string> onError);

        // V3: host-leave auto-deletes. Aliased to LeaveLobby for back-compat.
        IEnumerator DeleteLobby(string lobbyId, string requesterId, Action onSuccess, Action<string> onError);

        // V3: SSE IS the heartbeat. Kept as a no-op for Phase 3 refresh manager compat.
        IEnumerator SendHeartbeat(string lobbyId, string playerId, Action onSuccess, Action<string> onError);

        // Removed: UpdateLobbyStatus — replaced by StartMatch (POST /me/start).
        // V2 callers that needed status=in_game should call StartMatch instead.
        IEnumerator UpdateLobbyStatus(string lobbyId, string playerId, string status, Action<Lobby> onSuccess, Action<string> onError);

        // V3 admin-only: GET /api/v3/lobbies/{config}/{id}. Requires a server key (pf_*), not a client key.
        IEnumerator AdminGetLobbyById(string lobbyId, Action<Lobby> onSuccess, Action<string> onError);
    }
    
    public interface IEventDispatcher
    {
        void Dispatch(string eventName, object data = null);
        void Subscribe(string eventName, Action<object> handler);
        void Unsubscribe(string eventName, Action<object> handler);
    }
    
    // Data structures
    public enum LobbyState
    {
        Disconnected,
        Connecting,
        Connected,
        InLobby,
        Error
    }
    
    public enum PlayerActionType
    {
        Join,
        Leave,
        Kick,
        StateChange,
        Disconnect
    }
    
    public struct PlayerAction
    {
        public string PlayerId;
        public PlayerActionType ActionType;

        public PlayerAction(string playerId, PlayerActionType actionType)
        {
            PlayerId = playerId;
            ActionType = actionType;
        }

        public static PlayerAction Joined(string playerId) => new PlayerAction(playerId, PlayerActionType.Join);
        public static PlayerAction Left(string playerId) => new PlayerAction(playerId, PlayerActionType.Leave);
        public static PlayerAction Kicked(string playerId) => new PlayerAction(playerId, PlayerActionType.Kick);
    }
}
 