using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace PlayFlow
{
    // Thin coroutine-wrapper layer over LobbyAPIImpl. V3: most /me routes resolve the lobby from the
    // x-player-id header, so `lobbyId` parameters are passed through for Phase 3 manager compatibility
    // but are not used on the wire.
    public class LobbyOperations
    {
        private readonly PlayFlowSettings _settings;
        private readonly PlayFlowEvents _events;
        private ILobbyAPI _api => PlayFlowCore.Instance.LobbyAPI;

        public LobbyOperations(PlayFlowSettings settings, PlayFlowEvents events)
        {
            _settings = settings;
            _events = events;
        }

        public IEnumerator CreateLobbyCoroutine(string lobbyName, int maxPlayers, bool isPrivate, bool allowLateJoin, string region, Dictionary<string, object> customSettings, bool forceFresh, string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.CreateLobby(_settings.defaultLobbyConfig, lobbyName, maxPlayers, isPrivate, allowLateJoin, region, customSettings, forceFresh, playerId, onSuccess, onError);
        }

        public IEnumerator JoinLobbyCoroutine(string lobbyId, string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.JoinLobby(lobbyId, playerId, onSuccess, onError);
        }

        public IEnumerator JoinLobbyByCodeCoroutine(string inviteCode, string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.JoinLobbyByCode(inviteCode, playerId, onSuccess, onError);
        }

        public IEnumerator LeaveLobbyCoroutine(string lobbyId, string playerId, Action onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.LeaveLobby(lobbyId, playerId, onSuccess, onError);
        }

        public IEnumerator GetLobbyCoroutine(string lobbyId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.GetLobby(lobbyId, onSuccess, onError);
        }

        public IEnumerator ListLobbiesCoroutine(Action<List<Lobby>> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.ListLobbies(onSuccess, onError);
        }

        public IEnumerator UpdatePlayerStateCoroutine(string lobbyId, string requesterId, string targetPlayerId, Dictionary<string, object> state, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            yield return _api.UpdatePlayerState(lobbyId, requesterId, targetPlayerId, state, onSuccess, onError);
        }

        public IEnumerator UpdateLobbyStatusCoroutine(string lobbyId, string playerId, string status, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null)
            {
                onError?.Invoke("Lobby API not initialized");
                yield break;
            }

            // V3: status transitions are host-triggered via dedicated endpoints; UpdateLobbyStatus maps
            // the legacy "in_game" transition to StartMatch inside the API layer.
            yield return _api.UpdateLobbyStatus(lobbyId, playerId, status, onSuccess, onError);
        }

        public IEnumerator KickPlayerCoroutine(string lobbyId, string requesterId, string playerToKickId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.KickPlayer(lobbyId, requesterId, playerToKickId, onSuccess, onError);
        }

        public IEnumerator TransferHostCoroutine(string lobbyId, string requesterId, string newHostId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }

            // V3: host transfer is not a supported settings update. Phase 3 should remove this code path
            // or wire it to a future dedicated endpoint. Fail explicitly instead of silently mutating state.
            onError?.Invoke("V3 does not support explicit host transfer. The server promotes a new host automatically when the current host leaves.");
            yield break;
        }

        public IEnumerator UpdateLobbyCoroutine(
            string lobbyId,
            string requesterId,
            string name,
            int? maxPlayers,
            bool? isPrivate,
            bool? useInviteCode,
            bool? allowLateJoin,
            string region,
            Dictionary<string, object> customSettings,
            Action<Lobby> onSuccess,
            Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }

            // V3: PATCH /me/settings accepts { name?, maxPlayers?, isPrivate?, allowLateJoin?, region?, settings? }.
            // `useInviteCode` is no longer a distinct concept — all private lobbies get a code automatically.
            var payload = new JObject();

            if (name != null) payload["name"] = name;
            if (maxPlayers.HasValue) payload["maxPlayers"] = maxPlayers.Value;
            if (isPrivate.HasValue) payload["isPrivate"] = isPrivate.Value;
            if (allowLateJoin.HasValue) payload["allowLateJoin"] = allowLateJoin.Value;
            if (region != null) payload["region"] = region;
            if (customSettings != null) payload["settings"] = JObject.FromObject(customSettings);

            yield return _api.UpdateLobby(lobbyId, requesterId, payload, onSuccess, onError);
        }

        public IEnumerator DeleteLobbyCoroutine(string lobbyId, string requesterId, Action onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.DeleteLobby(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator FindLobbyByPlayerIdCoroutine(string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.FindLobbyByPlayerId(playerId, onSuccess, onError);
        }

        public IEnumerator SendHeartbeatCoroutine(string lobbyId, string playerId, Action onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            // V3: SSE is the heartbeat; the API-layer stub returns success immediately.
            yield return _api.SendHeartbeat(lobbyId, playerId, onSuccess, onError);
        }

        public IEnumerator StartMatchmakingCoroutine(string lobbyId, string requesterId, string mode, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.FindMatch(lobbyId, requesterId, mode, onSuccess, onError);
        }

        public IEnumerator CancelMatchmakingCoroutine(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.CancelMatchmaking(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator StartMatchCoroutine(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.StartMatch(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator EndMatchCoroutine(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.EndMatch(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator ConfirmMatchCoroutine(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.ConfirmMatch(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator DeclineMatchCoroutine(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            if (_api == null) { onError?.Invoke("Lobby API not initialized"); yield break; }
            yield return _api.DeclineMatch(lobbyId, requesterId, onSuccess, onError);
        }
    }
}
