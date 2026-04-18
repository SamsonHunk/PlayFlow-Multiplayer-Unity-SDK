using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace PlayFlow
{
    // V3 refresh + SSE wiring.
    //
    // - The SSE stream is player-centric: we Connect(playerId, config, baseUrl, apiKey) at
    //   Initialize-time (as soon as we have a playerId), and the server auto-binds/rebinds the
    //   stream whenever the player joins or leaves a lobby. No lobbyId is needed.
    // - HTTP polling remains as a fallback for when SSE is not available (e.g. WebGL, transient
    //   failures). When SSE is connected we skip the polled refresh for the current lobby.
    // - V3 has no HTTP heartbeat — SSE is the heartbeat. The heartbeat coroutine and watchdog
    //   that lived here in V2 have been removed.
    public class LobbyRefreshManager : MonoBehaviour
    {
        private PlayFlowSettings _settings;
        private LobbyOperations _operations;
        private PlayFlowLobbyManagerV2 _lobbyManager;
        private PlayFlowEvents _events;
        private Coroutine _refreshCoroutine;
        private LobbySseManager _sseManager;
        private bool _isSSEConnected = false;
        private bool _sseStarted = false;
        private LobbyUpdateDeduplicator _deduplicator = new LobbyUpdateDeduplicator();

        public event Action<List<Lobby>> OnLobbyListRefreshed;

        public void Initialize(PlayFlowSettings settings, LobbyOperations operations)
        {
            _settings = settings;
            _operations = operations;
            _lobbyManager = PlayFlowLobbyManagerV2.Instance;
            _events = GetComponent<PlayFlowEvents>();

            _sseManager = LobbySseManager.Instance;

#if UNITY_IOS || UNITY_ANDROID
            if (PlatformSSEHandler.ShouldReconnectOnBackground())
            {
                var lifecycleGO = new GameObject("[MobileLifecycleHandler]");
                lifecycleGO.transform.SetParent(transform);
                lifecycleGO.AddComponent<MobileLifecycleHandler>();
            }
#endif

            // Subscribe to SSE events.
            _sseManager.OnConnected += HandleSSEConnected;
            _sseManager.OnDisconnected += HandleSSEDisconnected;
            _sseManager.OnLobbyUpdated += HandleSSELobbyUpdate;
            _sseManager.OnLobbyDeleted += HandleSSELobbyDeleted;
            _sseManager.OnQueueStats += HandleSSEQueueStats;
            _sseManager.OnError += HandleSSEError;

            // Subscribe to manager events so we know when the player leaves (to drop local SSE flag).
            if (_lobbyManager != null)
            {
                _lobbyManager.Events.OnLobbyLeft.AddListener(HandleSessionLobbyLeft);
            }

            // V3: connect SSE as soon as we have the player id. The stream auto-binds to whichever
            // lobby the player joins, so we don't wait for a join to open it.
            TryStartSSE();

            if (_settings != null && _settings.autoRefresh && _lobbyManager != null && _refreshCoroutine == null)
            {
                _refreshCoroutine = StartCoroutine(RefreshLoop());
            }
        }

        private void OnEnable()
        {
            if (_settings != null && _settings.autoRefresh && _lobbyManager != null && _refreshCoroutine == null)
            {
                _refreshCoroutine = StartCoroutine(RefreshLoop());
            }

            // If we were initialized before a player id was set, pick it up on re-enable.
            TryStartSSE();
        }

        private void OnDisable()
        {
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
            }

            if (_sseManager != null && _sseStarted)
            {
                _sseManager.Disconnect();
                _sseStarted = false;
                _isSSEConnected = false;
            }
        }

        /// <summary>
        /// Opens the V3 player-centric SSE stream if we haven't already and the player id is known.
        /// Safe to call repeatedly; only opens once.
        /// </summary>
        private void TryStartSSE()
        {
            if (_sseStarted) return;
            if (_sseManager == null || _lobbyManager == null || _settings == null) return;
            if (string.IsNullOrEmpty(_lobbyManager.PlayerId)) return;
            if (string.IsNullOrEmpty(_settings.apiKey)) return;
            if (string.IsNullOrEmpty(_settings.baseUrl)) return;
            if (string.IsNullOrEmpty(_settings.defaultLobbyConfig)) return;

            if (_settings.debugLogging)
            {
                Debug.Log($"[LobbyRefreshManager] Opening V3 SSE stream for player {_lobbyManager.PlayerId} on config '{_settings.defaultLobbyConfig}'");
            }

            _sseManager.Connect(
                _lobbyManager.PlayerId,
                _settings.defaultLobbyConfig,
                _settings.baseUrl,
                _settings.apiKey);

            _sseStarted = true;
        }

        /// <summary>
        /// Public: the manager calls this after Initialize(playerId) so we can open the SSE stream
        /// once a player identity exists.
        /// </summary>
        public void NotifyPlayerIdAvailable()
        {
            TryStartSSE();
        }

        private IEnumerator RefreshLoop()
        {
            var wait = new WaitForSeconds(_settings.refreshInterval);

            while (enabled)
            {
                yield return wait;

                // Keep trying to open SSE while we're running — handles the case where Initialize
                // was called before the player id was set.
                TryStartSSE();

                if (_lobbyManager != null && _lobbyManager.IsInLobby && _lobbyManager.CurrentLobby != null)
                {
                    // Skip the HTTP poll while SSE is live — SSE is the primary source.
                    bool shouldPoll = !_isSSEConnected;

                    if (shouldPoll)
                    {
                        if (_settings.debugLogging)
                        {
                            Debug.Log($"[LobbyRefreshManager] Polling lobby {_lobbyManager.CurrentLobby.id} - SSE Connected: {_isSSEConnected}");
                        }
                        yield return RefreshCurrentLobby(_lobbyManager.CurrentLobby.id);
                    }
                    else if (_settings.debugLogging)
                    {
                        Debug.Log($"[LobbyRefreshManager] Skipping poll - SSE active for lobby {_lobbyManager.CurrentLobby.id}");
                    }
                }
                else
                {
                    yield return RefreshLobbyList();
                }
            }
        }

        private IEnumerator RefreshLobbyList()
        {
            yield return _operations.ListLobbiesCoroutine(
                lobbies =>
                {
                    OnLobbyListRefreshed?.Invoke(lobbies);
                },
                error =>
                {
                    if (_settings.debugLogging)
                    {
                        Debug.LogError($"[LobbyRefreshManager] Failed to refresh lobby list: {error}");
                    }
                }
            );
        }


        private IEnumerator RefreshCurrentLobby(string lobbyId)
        {
            if (!_isSSEConnected && (_settings?.debugLogging ?? false))
            {
                Debug.LogWarning($"[LobbyRefreshManager] POLLING lobby {lobbyId} via HTTP (SSE not connected)");
            }
            yield return _operations.GetLobbyCoroutine(lobbyId,
                lobby =>
                {
                    if (_lobbyManager != null && lobby != null && _lobbyManager.CurrentLobby?.id == lobby.id)
                    {
                        if (_deduplicator.IsDuplicateUpdate(lobby))
                        {
                            if (_settings.debugLogging)
                            {
                                Debug.Log($"[LobbyRefreshManager] Skipping duplicate polled update for lobby {lobby.id}");
                            }
                            return;
                        }
                        _lobbyManager.UpdateCurrentLobby(lobby);
                    }
                    else if (lobby == null && _lobbyManager != null && _lobbyManager.CurrentLobby?.id == lobbyId)
                    {
                        // V3 GetLobby returns null on 404 — the player is no longer in a lobby.
                        _lobbyManager.ClearCurrentLobby();
                    }
                },
                error =>
                {
                    if (error.Contains("404") || error.Contains("Not Found"))
                    {
                        if (_lobbyManager != null && _lobbyManager.CurrentLobby?.id == lobbyId)
                        {
                            _lobbyManager.ClearCurrentLobby();
                        }

                        if (_settings.debugLogging)
                        {
                            Debug.Log($"[LobbyRefreshManager] Poll for lobby {lobbyId} failed — lobby no longer exists (404).");
                        }
                    }
                    else if (_settings.debugLogging)
                    {
                        Debug.LogError($"[LobbyRefreshManager] Failed to refresh lobby: {error}");
                    }
                });
        }

        public void ForceRefresh()
        {
            if (_lobbyManager != null && _lobbyManager.IsInLobby && _lobbyManager.CurrentLobby != null)
            {
                StartCoroutine(RefreshCurrentLobby(_lobbyManager.CurrentLobby.id));
            }
            else
            {
                StartCoroutine(RefreshLobbyList());
            }
        }

        public void PauseRefresh()
        {
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;

                if (_settings?.debugLogging ?? false)
                {
                    Debug.Log("[LobbyRefreshManager] Refresh paused");
                }
            }
        }

        public void ResumeRefresh()
        {
            if (_settings != null && _settings.autoRefresh && _lobbyManager != null && _refreshCoroutine == null)
            {
                _refreshCoroutine = StartCoroutine(RefreshLoop());

                if (_settings.debugLogging)
                {
                    Debug.Log("[LobbyRefreshManager] Refresh resumed");
                }
            }
        }

        /// <summary>
        /// Mark a lobby update as coming from a local API call to help with deduplication.
        /// </summary>
        public void MarkLocalUpdate(Lobby lobby)
        {
            if (lobby != null && _deduplicator != null)
            {
                _deduplicator.IsDuplicateUpdate(lobby); // Pre-record it
                if (_settings?.debugLogging ?? false)
                {
                    Debug.Log($"[LobbyRefreshManager] Marked local API update for lobby {lobby.id}");
                }
            }
        }

        // -------------------------------------------------------------------------
        //  SSE event handlers
        // -------------------------------------------------------------------------

        private void HandleSessionLobbyLeft()
        {
            // V3: the SSE stream survives a lobby leave — the server just sends an empty/next update
            // when the player joins a new lobby. We don't tear it down here.
            if (_settings?.debugLogging ?? false)
            {
                Debug.Log("[LobbyRefreshManager] Player left lobby (SSE stream stays open).");
            }
        }

        private void HandleSSEConnected()
        {
            _isSSEConnected = true;
            if (_settings?.debugLogging ?? false)
            {
                Debug.Log("[LobbyRefreshManager] SSE connected - pausing HTTP polling while active");
            }

            // When SSE (re)connects, pull latest lobby state once via HTTP to cover any gap.
            if (_lobbyManager != null && _lobbyManager.CurrentLobby != null)
            {
                StartCoroutine(RefreshCurrentLobby(_lobbyManager.CurrentLobby.id));
            }
        }

        private void HandleSSEDisconnected()
        {
            _isSSEConnected = false;

            if (_settings?.debugLogging ?? false)
            {
                Debug.Log("[LobbyRefreshManager] SSE disconnected - resuming HTTP polling");
            }
        }

        private void HandleSSELobbyUpdate(Lobby lobby)
        {
            if (_lobbyManager == null || lobby == null) return;

            // V3: the player-centric SSE stream emits updates for the lobby the player is currently in.
            // If we're not in any lobby locally yet (e.g. late initial connect), adopt the lobby.
            if (_lobbyManager.CurrentLobby == null)
            {
                if (lobby.ContainsPlayer(_lobbyManager.PlayerId))
                {
                    if (_settings?.debugLogging ?? false)
                    {
                        Debug.Log($"[LobbyRefreshManager] Adopting lobby {lobby.id} from initial SSE event.");
                    }
                    _lobbyManager.SetCurrentLobby(lobby);
                }
                return;
            }

            // Only apply updates that match our current lobby.
            if (lobby.id != _lobbyManager.CurrentLobby.id) return;

            if (_deduplicator.IsDuplicateUpdate(lobby))
            {
                if (_settings.debugLogging)
                {
                    Debug.Log($"[LobbyRefreshManager] Skipping duplicate SSE update for lobby {lobby.id}");
                }
                return;
            }

            _lobbyManager.UpdateCurrentLobby(lobby);

            if (_settings?.debugLogging ?? false)
            {
                Debug.Log($"[LobbyRefreshManager] SSE update for lobby {lobby.id} - Status: {lobby.status}");
            }
        }

        private void HandleSSELobbyDeleted(string lobbyId)
        {
            if (_lobbyManager?.CurrentLobby?.id == lobbyId)
            {
                _lobbyManager.ClearCurrentLobby();

                if (_settings?.debugLogging ?? false)
                {
                    Debug.Log($"[LobbyRefreshManager] Lobby {lobbyId} was deleted (SSE notification)");
                }
            }
        }

        private void HandleSSEQueueStats(QueueStats stats)
        {
            if (stats == null) return;
            if (_settings?.debugLogging ?? false)
            {
                Debug.Log($"[LobbyRefreshManager] SSE queue_stats - searching: {stats.playersSearching}, lobbies: {stats.lobbiesInQueue}, avgWait: {stats.avgWaitSeconds}s");
            }
            _events?.InvokeQueueStats(stats);
        }

        private void HandleSSEError(string error)
        {
            Debug.LogWarning($"[LobbyRefreshManager] SSE error: {error}");
        }

        private void OnDestroy()
        {
            if (_sseManager != null)
            {
                _sseManager.OnConnected -= HandleSSEConnected;
                _sseManager.OnDisconnected -= HandleSSEDisconnected;
                _sseManager.OnLobbyUpdated -= HandleSSELobbyUpdate;
                _sseManager.OnLobbyDeleted -= HandleSSELobbyDeleted;
                _sseManager.OnQueueStats -= HandleSSEQueueStats;
                _sseManager.OnError -= HandleSSEError;
            }

            if (_lobbyManager != null)
            {
                _lobbyManager.Events.OnLobbyLeft.RemoveListener(HandleSessionLobbyLeft);
            }
        }
    }
}
