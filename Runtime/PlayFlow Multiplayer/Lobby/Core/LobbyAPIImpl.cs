using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayFlow
{
    // V3 Lobby HTTP API.
    //
    // Base URL format: {baseUrl}/api/v3/lobbies/{configName}[/sub-path]
    // Required headers on every request: api-key, x-player-id
    //
    // Public method signatures mirror the V2 API so the Phase 3 refresh/manager refactor is minimal.
    // `lobbyId` parameters on /me routes are kept for signature compatibility but are not used on the
    // wire — the server resolves the caller's lobby from the x-player-id header.
    public class LobbyAPIImpl : ILobbyAPI
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly INetworkManager _networkManager;
        private readonly string _lobbyConfigName;

        private string _activePlayerId;

        public string ActivePlayerId => _activePlayerId;

        public void SetActivePlayerId(string playerId)
        {
            _activePlayerId = playerId;
        }

        public LobbyAPIImpl(string baseUrl, string apiKey, string lobbyConfigName, INetworkManager networkManager)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _apiKey = apiKey;
            _lobbyConfigName = lobbyConfigName;
            _networkManager = networkManager;
        }

        // -------------------------------------------------------------------------
        //  URL + header helpers
        // -------------------------------------------------------------------------
        private string BuildUrl(string path)
        {
            var configSegment = Uri.EscapeDataString(_lobbyConfigName ?? string.Empty);
            var suffix = string.IsNullOrEmpty(path) ? string.Empty : (path.StartsWith("/") ? path : "/" + path);
            return $"{_baseUrl}/api/v3/lobbies/{configSegment}{suffix}";
        }

        private Dictionary<string, string> Headers(string playerId)
        {
            var pid = !string.IsNullOrEmpty(playerId) ? playerId : _activePlayerId;
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(pid))
            {
                headers["x-player-id"] = pid;
            }
            return headers;
        }

        private static Lobby ParseLobby(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<Lobby>(json);
        }

        // -------------------------------------------------------------------------
        //  Create / Join / Leave
        // -------------------------------------------------------------------------

        public IEnumerator CreateLobby(
            string configName,
            string lobbyName,
            int maxPlayers,
            bool isPrivate,
            bool allowLateJoin,
            string region,
            Dictionary<string, object> customSettings,
            string playerId,
            Action<Lobby> onSuccess,
            Action<string> onError)
        {
            // V3: configName on the creating call must match the configured config; we still use the
            // LobbyAPIImpl's stored config name for the URL path.
            var url = BuildUrl(string.Empty);

            var payload = new JObject
            {
                ["name"] = lobbyName,
                ["maxPlayers"] = maxPlayers,
                ["isPrivate"] = isPrivate,
                ["allowLateJoin"] = allowLateJoin,
            };
            if (!string.IsNullOrEmpty(region)) payload["region"] = region;
            if (customSettings != null) payload["settings"] = JObject.FromObject(customSettings);

            // Remember the player id for subsequent /me calls.
            if (!string.IsNullOrEmpty(playerId)) _activePlayerId = playerId;

            yield return _networkManager.Post(url, payload.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(playerId));
        }

        public IEnumerator JoinLobby(string lobbyId, string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/join with body { lobbyId }.
            var url = BuildUrl("/join");
            var payload = new JObject { ["lobbyId"] = lobbyId };

            if (!string.IsNullOrEmpty(playerId)) _activePlayerId = playerId;

            yield return _networkManager.Post(url, payload.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(playerId));
        }

        public IEnumerator JoinLobbyByCode(string inviteCode, string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/join with body { code }.
            var url = BuildUrl("/join");
            var payload = new JObject { ["code"] = inviteCode };

            if (!string.IsNullOrEmpty(playerId)) _activePlayerId = playerId;

            yield return _networkManager.Post(url, payload.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(playerId));
        }

        public IEnumerator LeaveLobby(string lobbyId, string playerId, Action onSuccess, Action<string> onError)
        {
            // V3: DELETE /api/v3/lobbies/{config}/me. lobbyId is resolved from x-player-id header.
            var url = BuildUrl("/me");

            yield return _networkManager.Delete(url, _apiKey, (_) =>
            {
                onSuccess?.Invoke();
            }, onError, Headers(playerId));
        }

        // -------------------------------------------------------------------------
        //  Read
        // -------------------------------------------------------------------------

        public IEnumerator GetLobby(string lobbyId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: GET /api/v3/lobbies/{config}/me — returns the caller's lobby.
            // lobbyId is kept in the signature for Phase 3 manager compat but ignored; server resolves
            // the lobby from the x-player-id header.
            var url = BuildUrl("/me");

            Action<string> customErrorHandler = (error) =>
            {
                if (error != null && (error.Contains("404") || error.ToLower().Contains("not found")))
                {
                    onSuccess?.Invoke(null);
                }
                else
                {
                    onError?.Invoke(error);
                }
            };

            yield return _networkManager.Get(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, customErrorHandler, Headers(null));
        }

        public IEnumerator ListLobbies(Action<List<Lobby>> onSuccess, Action<string> onError)
        {
            // V3: GET /api/v3/lobbies/{config} — returns { lobbies, total, limit, offset, hasMore }.
            var url = BuildUrl(string.Empty);

            yield return _networkManager.Get(url, _apiKey, (response) =>
            {
                try
                {
                    // V3 browse response wraps the array; tolerate a raw array for robustness.
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        onSuccess?.Invoke(new List<Lobby>());
                        return;
                    }

                    var trimmed = response.TrimStart();
                    List<Lobby> lobbies;
                    if (trimmed.StartsWith("["))
                    {
                        lobbies = JsonConvert.DeserializeObject<List<Lobby>>(response) ?? new List<Lobby>();
                    }
                    else
                    {
                        var obj = JObject.Parse(response);
                        var arr = obj["lobbies"] as JArray;
                        lobbies = arr != null
                            ? arr.ToObject<List<Lobby>>()
                            : new List<Lobby>();
                    }
                    onSuccess?.Invoke(lobbies ?? new List<Lobby>());
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobbies: {e.Message}");
                }
            }, onError, Headers(null));
        }

        public IEnumerator FindLobbyByPlayerId(string playerId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: only lets you query yourself. Send x-player-id = playerId and hit GET /me.
            var url = BuildUrl("/me");

            Action<string> customErrorHandler = (error) =>
            {
                if (error != null && (error.Contains("404") || error.ToLower().Contains("not found")))
                {
                    onSuccess?.Invoke(null);
                }
                else
                {
                    onError?.Invoke(error);
                }
            };

            yield return _networkManager.Get(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, customErrorHandler, Headers(playerId));
        }

        // -------------------------------------------------------------------------
        //  Player state / settings / kick
        // -------------------------------------------------------------------------

        public IEnumerator UpdatePlayerState(string lobbyId, string requesterId, string targetPlayerId, Dictionary<string, object> state, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3 only allows a player to update their OWN state.
            if (!string.IsNullOrEmpty(targetPlayerId) && !string.IsNullOrEmpty(requesterId) && targetPlayerId != requesterId)
            {
                onError?.Invoke("V3 does not allow updating another player's state. Pass the host's own state via requesterId only.");
                yield break;
            }

            var url = BuildUrl("/me");
            var payload = new JObject
            {
                ["state"] = state != null ? JObject.FromObject(state) : new JObject(),
            };

            yield return _networkManager.Patch(url, payload.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator UpdateLobby(string lobbyId, string requesterId, JObject payload, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: PATCH /api/v3/lobbies/{config}/me/settings. Only whitelisted fields are accepted:
            // name, maxPlayers, isPrivate, allowLateJoin, region, settings. Strip V2-only fields.
            var url = BuildUrl("/me/settings");

            var body = new JObject();
            if (payload != null)
            {
                foreach (var kvp in payload)
                {
                    switch (kvp.Key)
                    {
                        case "name":
                        case "maxPlayers":
                        case "isPrivate":
                        case "allowLateJoin":
                        case "region":
                        case "settings":
                            body[kvp.Key] = kvp.Value;
                            break;
                        // Silently drop V2-only fields (requesterId, useInviteCode, host transfer, status,
                        // matchmaking action). Dedicated V3 endpoints exist for host-only operations.
                    }
                }
            }

            yield return _networkManager.Patch(url, body.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator UpdateLobbyStatus(string lobbyId, string playerId, string status, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: no direct status PUT. For V2 callers that used this to set status=in_game, route to StartMatch.
            if (string.Equals(status, "in_game", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase))
            {
                yield return StartMatch(lobbyId, playerId, onSuccess, onError);
                yield break;
            }

            onError?.Invoke($"V3 does not support setting lobby status directly (requested '{status}'). Use StartMatch / FindMatch / CancelMatchmaking instead.");
            yield break;
        }

        public IEnumerator KickPlayer(string lobbyId, string requesterId, string playerToKickId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: DELETE /api/v3/lobbies/{config}/me/players/{playerId}.
            var url = BuildUrl($"/me/players/{Uri.EscapeDataString(playerToKickId ?? string.Empty)}");

            yield return _networkManager.Delete(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    // Some V3 responses may be empty on success; treat parse failure as best-effort success.
                    Debug.LogWarning($"[LobbyAPIImpl] Could not parse kick response, assuming success. Error: {e.Message}");
                    onSuccess?.Invoke(null);
                }
            }, onError, Headers(requesterId));
        }

        // -------------------------------------------------------------------------
        //  Host lifecycle operations
        // -------------------------------------------------------------------------

        public IEnumerator StartMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/me/start — host starts the game directly.
            var url = BuildUrl("/me/start");

            yield return _networkManager.Post(url, "{}", _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator EndMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/me/end-match — host resets lobby to waiting.
            var url = BuildUrl("/me/end-match");

            yield return _networkManager.Post(url, "{}", _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator FindMatch(string lobbyId, string requesterId, string mode, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/me/matchmaking with { mode }.
            var url = BuildUrl("/me/matchmaking");
            var payload = new JObject { ["mode"] = mode };

            yield return _networkManager.Post(url, payload.ToString(), _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator CancelMatchmaking(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: DELETE /api/v3/lobbies/{config}/me/matchmaking.
            var url = BuildUrl("/me/matchmaking");

            yield return _networkManager.Delete(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    // Empty success responses are acceptable — pass null through.
                    Debug.LogWarning($"[LobbyAPIImpl] Could not parse cancel-matchmaking response, assuming success. Error: {e.Message}");
                    onSuccess?.Invoke(null);
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator ConfirmMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: POST /api/v3/lobbies/{config}/me/confirm-match — accept a found match.
            var url = BuildUrl("/me/confirm-match");

            yield return _networkManager.Post(url, "{}", _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(requesterId));
        }

        public IEnumerator DeclineMatch(string lobbyId, string requesterId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3: DELETE /api/v3/lobbies/{config}/me/confirm-match — cancel the match for all lobbies.
            var url = BuildUrl("/me/confirm-match");

            yield return _networkManager.Delete(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LobbyAPIImpl] Could not parse decline-match response, assuming success. Error: {e.Message}");
                    onSuccess?.Invoke(null);
                }
            }, onError, Headers(requesterId));
        }

        // -------------------------------------------------------------------------
        //  Delete / heartbeat (compat stubs)
        // -------------------------------------------------------------------------

        public IEnumerator DeleteLobby(string lobbyId, string requesterId, Action onSuccess, Action<string> onError)
        {
            // V3: the lobby auto-deletes when the host leaves. Alias to LeaveLobby for back-compat.
            yield return LeaveLobby(lobbyId, requesterId, onSuccess, onError);
        }

        public IEnumerator SendHeartbeat(string lobbyId, string playerId, Action onSuccess, Action<string> onError)
        {
            // V3: the SSE connection IS the heartbeat. This is a no-op stub so the Phase 3 refresh
            // manager keeps compiling until it's refactored.
            onSuccess?.Invoke();
            yield break;
        }

        // -------------------------------------------------------------------------
        //  Admin-only
        // -------------------------------------------------------------------------

        public IEnumerator AdminGetLobbyById(string lobbyId, Action<Lobby> onSuccess, Action<string> onError)
        {
            // V3 admin: GET /api/v3/lobbies/{config}/{id}. REQUIRES a server key (pf_*), not a client key (pfclient_*).
            // This method is exposed for server-side/admin tooling; do not call from shipped clients using a client key.
            var url = BuildUrl($"/{Uri.EscapeDataString(lobbyId ?? string.Empty)}");

            yield return _networkManager.Get(url, _apiKey, (response) =>
            {
                try
                {
                    onSuccess?.Invoke(ParseLobby(response));
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse lobby response: {e.Message}");
                }
            }, onError, Headers(null));
        }
    }
}
