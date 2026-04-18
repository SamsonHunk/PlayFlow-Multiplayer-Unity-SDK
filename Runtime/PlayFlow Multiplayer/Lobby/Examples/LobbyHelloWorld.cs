using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;
using PlayFlow;
using System.Collections;
using Newtonsoft.Json;

/// <summary>
/// A simple example demonstrating how to use the PlayFlow Lobby SDK (V3).
/// This script shows the core functionality with keyboard shortcuts for testing.
///
/// Setup Instructions:
/// 1. Create an empty GameObject and add the PlayFlowLobbyManagerV2 component to it.
/// 2. Configure your API key in the PlayFlowLobbyManagerV2 component in the Inspector.
/// 3. Set the defaultLobbyConfig to "default" in the inspector (V3 default).
/// 4. Create another empty GameObject and add this LobbyHelloWorld script to it.
/// 5. Play the scene and use the keyboard shortcuts below.
///
/// Keyboard Shortcuts:
/// - R: Refresh lobby list
/// - C: Create a new lobby (for 1v1 matchmaking)
/// - J: Join first available public lobby
/// - L: Leave current lobby
/// - S: Start the match (host only - direct match)
/// - E: End the match (host only - returns lobby to 'waiting' for rematch)
/// - T: Send test player state update (with MMR)
/// - U: Update other player's state (host only -- V3 only allows self)
/// - I: Get game server connection info
/// - M: Start 1v1 Matchmaking (host only)
/// - N: Cancel Matchmaking (host only)
/// - Y: Accept a found match (when status == match_found)
/// - X: Decline a found match (cancels for everyone — all → waiting)
/// - P: Print lobby player list with per-player state
/// </summary>
public class LobbyHelloWorld : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The name for lobbies created by this script")]
    public string lobbyName = "1v1 Match Lobby";

    [Tooltip("Maximum players for created lobbies (set to 2 for 1v1)")]
    [Range(2, 10)]
    public int maxPlayers = 2;

    [Tooltip("Whether created lobbies should be private")]
    public bool isPrivate = false;

    [Header("Matchmaking Settings")]
    [Tooltip("Your player's MMR (matchmaking rating) for testing")]
    [Range(800, 2000)]
    public int playerMMR = 1200;

    private string _playerId;

    void Start()
    {
        // Manager can now be accessed directly via the singleton instance
        if (PlayFlowLobbyManagerV2.Instance == null)
        {
            Debug.LogError("[LobbyHelloWorld] PlayFlowLobbyManagerV2 not found in the scene! Please add it to a GameObject.", this);
            return;
        }

        // Generate a unique player ID
        _playerId = "player-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Debug.Log($"[LobbyHelloWorld] Starting with player ID: {_playerId}");

        // Initialize the manager
        PlayFlowLobbyManagerV2.Instance.Initialize(_playerId, OnManagerReady);

        // Subscribe to events
        SubscribeToEvents();
    }

    void OnManagerReady()
    {
        Debug.Log("[LobbyHelloWorld] Manager is ready! Use keyboard shortcuts:");
        Debug.Log("  R - Refresh lobby list");
        Debug.Log("  C - Create a new lobby (for 1v1 matchmaking)");
        Debug.Log("  J - Join first available lobby");
        Debug.Log("  L - Leave current lobby");
        Debug.Log("  S - Start the match (direct match)");
        Debug.Log("  E - End the match (host only - rematch)");
        Debug.Log("  T - Send test player state (with MMR)");
        Debug.Log("  U - Update other player's state (V3: warns -- self-only)");
        Debug.Log("  I - Get game server connection info");
        Debug.Log("  M - Start 1v1 Matchmaking");
        Debug.Log("  N - Cancel Matchmaking");
        Debug.Log("  Y - Accept a found match (when status = match_found)");
        Debug.Log("  X - Decline a found match (cancels for everyone)");
        Debug.Log("  P - Print lobby players and per-player state");

        // Automatically set initial player state with MMR when ready
        StartCoroutine(SetInitialPlayerState());
    }

    void Update()
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsReady) return;

        // Refresh lobby list
        if (Input.GetKeyDown(KeyCode.R))
        {
            RefreshLobbies();
        }

        // Create lobby
        if (Input.GetKeyDown(KeyCode.C))
        {
            CreateLobby();
        }

        // Join first available lobby
        if (Input.GetKeyDown(KeyCode.J))
        {
            JoinFirstAvailableLobby();
        }

        // Leave lobby
        if (Input.GetKeyDown(KeyCode.L))
        {
            LeaveLobby();
        }

        // Send player state update
        if (Input.GetKeyDown(KeyCode.T))
        {
            SendTestPlayerState();
        }

        // Start match
        if (Input.GetKeyDown(KeyCode.S))
        {
            StartMatch();
        }

        // End match (host only, returns lobby to 'waiting' for a rematch)
        if (Input.GetKeyDown(KeyCode.E))
        {
            EndMatch();
        }

        // [Legacy / no-op in V3] Update another player's state (now warns)
        if (Input.GetKeyDown(KeyCode.U))
        {
            UpdateOtherPlayerState();
        }

        // Get game server connection info
        if (Input.GetKeyDown(KeyCode.I))
        {
            GetConnectionInfo();
        }

        // Start 1v1 Matchmaking
        if (Input.GetKeyDown(KeyCode.M))
        {
            StartMatchmaking();
        }

        // Cancel Matchmaking
        if (Input.GetKeyDown(KeyCode.N))
        {
            CancelMatchmaking();
        }

        // Accept a found match (fires when status == 'match_found')
        if (Input.GetKeyDown(KeyCode.Y))
        {
            ConfirmMatch();
        }

        // Decline a found match — cancels for EVERY participating lobby
        if (Input.GetKeyDown(KeyCode.X))
        {
            DeclineMatch();
        }

        // Print all players in the current lobby (V3 typed player list)
        if (Input.GetKeyDown(KeyCode.P))
        {
            PrintLobbyPlayers();
        }
    }

    void RefreshLobbies()
    {
        Debug.Log("[LobbyHelloWorld] Refreshing lobby list...");

        PlayFlowLobbyManagerV2.Instance.GetAvailableLobbies(
            onSuccess: (lobbies) => {
                Debug.Log($"[LobbyHelloWorld] Found {lobbies.Count} lobbies:");
                foreach (var lobby in lobbies)
                {
                    Debug.Log($"  - {lobby.name} ({lobby.currentPlayers}/{lobby.maxPlayers}) ID: {lobby.id}");
                }
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to get lobbies: {error}");
            }
        );
    }

    void CreateLobby()
    {
        Debug.Log($"[LobbyHelloWorld] Creating 1v1 lobby '{lobbyName}'...");

        // Create custom settings for the lobby
        var customSettings = new Dictionary<string, object>
        {
            ["gameMode"] = "1v1",
            ["matchType"] = "ranked"
        };

        // Create lobby optimized for 1v1 matchmaking
        PlayFlowLobbyManagerV2.Instance.CreateLobby(
            name: lobbyName,
            maxPlayers: 2, // Always 2 for 1v1
            isPrivate: isPrivate,
            allowLateJoin: false, // No late join for competitive 1v1
            region: "us-west", // You might want to make this configurable
            customSettings: customSettings,
            onSuccess: (lobby) => {
                Debug.Log($"[LobbyHelloWorld] Successfully created 1v1 lobby: {lobby.name} (ID: {lobby.id})");
                // V3: lobby.code (back-compat alias: lobby.InviteCode)
                if (lobby.isPrivate && !string.IsNullOrEmpty(lobby.code))
                {
                    Debug.Log($"[LobbyHelloWorld] Invite code: {lobby.code}");
                }
                Debug.Log("[LobbyHelloWorld] Tip: Press 'M' to start 1v1 matchmaking once you're ready!");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to create lobby: {error}");
            }
        );
    }

    void JoinFirstAvailableLobby()
    {
        Debug.Log("[LobbyHelloWorld] Looking for available lobbies...");

        PlayFlowLobbyManagerV2.Instance.GetAvailableLobbies(
            onSuccess: (lobbies) => {
                var availableLobby = lobbies.Find(l => !l.isPrivate && l.currentPlayers < l.maxPlayers);

                if (availableLobby != null)
                {
                    Debug.Log($"[LobbyHelloWorld] Joining lobby: {availableLobby.name}");

                    PlayFlowLobbyManagerV2.Instance.JoinLobby(availableLobby.id,
                        onSuccess: (lobby) => {
                            Debug.Log($"[LobbyHelloWorld] Successfully joined lobby: {lobby.name}");
                        },
                        onError: (error) => {
                            Debug.LogError($"[LobbyHelloWorld] Failed to join lobby: {error}");
                        }
                    );
                }
                else
                {
                    Debug.LogWarning("[LobbyHelloWorld] No available public lobbies found. Try creating one!");
                }
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to get lobbies: {error}");
            }
        );
    }

    void LeaveLobby()
    {
        if (PlayFlowLobbyManagerV2.Instance.CurrentLobby == null)
        {
            Debug.LogWarning("[LobbyHelloWorld] Not in a lobby!");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Leaving current lobby...");

        PlayFlowLobbyManagerV2.Instance.LeaveLobby(
            onSuccess: () => {
                Debug.Log("[LobbyHelloWorld] Successfully left lobby");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to leave lobby: {error}");
            }
        );
    }

    void StartMatch()
    {
        if (PlayFlowLobbyManagerV2.Instance.CurrentLobby == null || !PlayFlowLobbyManagerV2.Instance.IsHost)
        {
            Debug.LogWarning("[LobbyHelloWorld] Cannot start match: you must be in a lobby and be the host.");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Starting match...");

        PlayFlowLobbyManagerV2.Instance.StartMatch(
            onSuccess: (lobby) => {
                Debug.Log($"[LobbyHelloWorld] Match started successfully. Status: {lobby.status}");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to start match: {error}");
            }
        );
    }

    void EndMatch()
    {
        var lobby = PlayFlowLobbyManagerV2.Instance.CurrentLobby;
        if (lobby == null || !PlayFlowLobbyManagerV2.Instance.IsHost)
        {
            Debug.LogWarning("[LobbyHelloWorld] Cannot end match: you must be the host.");
            return;
        }
        if (lobby.status != "in_game")
        {
            Debug.LogWarning($"[LobbyHelloWorld] Cannot end match: lobby status is '{lobby.status}' (must be 'in_game').");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Ending match — lobby will return to 'waiting' for rematch...");

        PlayFlowLobbyManagerV2.Instance.EndMatch(
            onSuccess: (updated) => {
                Debug.Log($"[LobbyHelloWorld] Match ended. Status: {updated.status}, players retained: {updated.currentPlayers}/{updated.maxPlayers}");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to end match: {error}");
            }
        );
    }

    void SendTestPlayerState()
    {
        if (PlayFlowLobbyManagerV2.Instance.CurrentLobby == null)
        {
            Debug.LogWarning("[LobbyHelloWorld] Not in a lobby!");
            return;
        }

        var testState = new Dictionary<string, object>
        {
            ["position"] = new Dictionary<string, float> { ["x"] = 10f, ["y"] = 20f },
            ["health"] = 100,
            ["ready"] = true,
            ["mmr"] = playerMMR, // Include MMR for matchmaking
            ["regions"] = new List<string> { "eu-west", "eu-north" },
            ["timestamp"] = DateTime.UtcNow.ToString()
        };

        Debug.Log($"[LobbyHelloWorld] Sending player state update with MMR: {playerMMR}...");

        PlayFlowLobbyManagerV2.Instance.UpdatePlayerState(testState,
            onSuccess: (lobby) => {
                Debug.Log("[LobbyHelloWorld] Successfully updated player state");

                // V3: read your own state back from the typed player list
                var myState = lobby.GetPlayerState(PlayFlowLobbyManagerV2.Instance.PlayerId);
                if (myState != null)
                {
                    Debug.Log($"[LobbyHelloWorld] My state on server: {JsonConvert.SerializeObject(myState)}");
                }
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to update player state: {error}");
            }
        );
    }

    void GetConnectionInfo()
    {
        var manager = PlayFlowLobbyManagerV2.Instance;
        var currentLobby = manager.CurrentLobby;

        // V3: use IsInGame helper + HasServer check
        if (currentLobby == null || !currentLobby.IsInGame)
        {
            Debug.LogWarning("[LobbyHelloWorld] Cannot get connection info: not in a running match.");
            return;
        }

        if (!currentLobby.HasServer)
        {
            Debug.LogWarning("[LobbyHelloWorld] Match is starting but server details are not ready yet.");
            return;
        }

        // Preferred: typed server access (V3) — matches GET /v3/servers/{id}
        var server = currentLobby.server;
        Debug.Log($"[LobbyHelloWorld] Game server -- instance_id={server.instance_id}, status={server.status}, region={server.region}, size={server.compute_size}");
        foreach (var port in server.network_ports)
        {
            Debug.Log($"  port '{port.name}' {port.protocol} -> {port.host}:{port.external_port} (internal {port.internal_port})");
        }

        // Legacy helper (still supported, returns ConnectionInfo?)
        ConnectionInfo? connectionInfo = manager.GetGameServerConnectionInfo();
        if (connectionInfo.HasValue)
        {
            Debug.Log($"[LobbyHelloWorld] Primary connection: IP = {connectionInfo.Value.Ip}, Port = {connectionInfo.Value.Port}");
        }
        else
        {
            Debug.LogWarning("[LobbyHelloWorld] Primary connection info unavailable.");
        }
    }

    void UpdateOtherPlayerState()
    {
        var manager = PlayFlowLobbyManagerV2.Instance;
        if (!manager.IsHost)
        {
            Debug.LogWarning("[LobbyHelloWorld] Only the host can attempt to update another player's state.");
            return;
        }

        if (manager.CurrentLobby == null || manager.CurrentLobby.players == null || manager.CurrentLobby.players.Count < 2)
        {
            Debug.LogWarning("[LobbyHelloWorld] Need at least one other player in the lobby to test this feature.");
            return;
        }

        // V3: players is a typed list of LobbyPlayer (each has .id, .state, .isHost)
        string otherPlayerId = null;
        foreach (var p in manager.CurrentLobby.players)
        {
            if (p.id != manager.PlayerId)
            {
                otherPlayerId = p.id;
                break;
            }
        }

        if (string.IsNullOrEmpty(otherPlayerId))
        {
             Debug.LogWarning("[LobbyHelloWorld] Couldn't find another player in the lobby.");
             return;
        }

        var testState = new Dictionary<string, object>
        {
            ["messageFromHost"] = "The host tried to update your state!",
            ["timestamp"] = DateTime.UtcNow.ToString()
        };

        // NOTE: V3 does NOT allow a host to mutate another player's state. This call will
        // log an error via onError. It is kept in the sample as a demonstration of the
        // behaviour change relative to V2.
        Debug.Log($"[LobbyHelloWorld] (V3) Attempting host-update for player {otherPlayerId} -- expect an error in V3.");

        manager.UpdateStateForPlayer(otherPlayerId, testState,
            onSuccess: (lobby) => {
                Debug.Log($"[LobbyHelloWorld] Updated state for player {otherPlayerId}.");
            },
            onError: (error) => {
                Debug.LogWarning($"[LobbyHelloWorld] (expected in V3) UpdateStateForPlayer rejected: {error}");
            }
        );
    }

    void StartMatchmaking()
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsInLobby || !PlayFlowLobbyManagerV2.Instance.IsHost)
        {
            Debug.LogWarning("[LobbyHelloWorld] Must be host in a lobby to start matchmaking!");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Starting 1v1 matchmaking...");

        PlayFlowLobbyManagerV2.Instance.FindMatch("1VS1",
            onSuccess: (lobby) => {
                Debug.Log($"[LobbyHelloWorld] Successfully started matchmaking! Status: {lobby.status}");
                // V3: matchmaking details live on lobby.matchmaking (may be null immediately after request)
                if (lobby.matchmaking != null)
                {
                    Debug.Log($"[LobbyHelloWorld] Matchmaking mode: {lobby.matchmaking.mode}");
                    Debug.Log($"[LobbyHelloWorld] Queued at: {lobby.matchmaking.startedAt}");
                }
                Debug.Log("[LobbyHelloWorld] Queue stats will stream via OnQueueStats while you wait.");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to start matchmaking: {error}");
            }
        );
    }

    void CancelMatchmaking()
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsInLobby || !PlayFlowLobbyManagerV2.Instance.IsHost)
        {
            Debug.LogWarning("[LobbyHelloWorld] Must be host in a lobby to cancel matchmaking!");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Cancelling matchmaking...");

        PlayFlowLobbyManagerV2.Instance.CancelMatchmaking(
            onSuccess: (lobby) => {
                Debug.Log($"[LobbyHelloWorld] Successfully cancelled matchmaking! Status: {lobby.status}");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to cancel matchmaking: {error}");
            }
        );
    }

    void ConfirmMatch()
    {
        var lobby = PlayFlowLobbyManagerV2.Instance.CurrentLobby;
        if (lobby?.status != "match_found")
        {
            Debug.LogWarning($"[LobbyHelloWorld] No match to confirm (status: {lobby?.status ?? "not in lobby"}).");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Accepting match...");
        PlayFlowLobbyManagerV2.Instance.ConfirmMatch(
            onSuccess: (updated) => {
                Debug.Log($"[LobbyHelloWorld] Confirmed. Status: {updated.status}, confirmed: {updated.matchmaking?.confirmation?.confirmed}");
            },
            onError: (error) => Debug.LogError($"[LobbyHelloWorld] ConfirmMatch failed: {error}")
        );
    }

    void DeclineMatch()
    {
        var lobby = PlayFlowLobbyManagerV2.Instance.CurrentLobby;
        if (lobby?.status != "match_found")
        {
            Debug.LogWarning($"[LobbyHelloWorld] No match to decline (status: {lobby?.status ?? "not in lobby"}).");
            return;
        }

        Debug.Log("[LobbyHelloWorld] Declining match — this cancels for every participating lobby.");
        PlayFlowLobbyManagerV2.Instance.DeclineMatch(
            onSuccess: (updated) => {
                Debug.Log($"[LobbyHelloWorld] Declined. Status: {updated.status} (all lobbies in the match returned to 'waiting').");
            },
            onError: (error) => Debug.LogError($"[LobbyHelloWorld] DeclineMatch failed: {error}")
        );
    }

    void PrintLobbyPlayers()
    {
        var manager = PlayFlowLobbyManagerV2.Instance;
        var lobby = manager.CurrentLobby;
        if (lobby == null)
        {
            Debug.LogWarning("[LobbyHelloWorld] Not in a lobby.");
            return;
        }

        Debug.Log($"[LobbyHelloWorld] Lobby '{lobby.name}' players ({lobby.currentPlayers}/{lobby.maxPlayers}):");
        if (lobby.players == null) return;

        foreach (var p in lobby.players)
        {
            var hostTag = p.isHost ? " [HOST]" : "";
            var stateJson = p.state != null ? JsonConvert.SerializeObject(p.state) : "(no state)";
            Debug.Log($"  - {p.id}{hostTag}  state={stateJson}");
        }

        // V3 helper: string[] of player ids
        Debug.Log($"[LobbyHelloWorld] PlayerIds: {string.Join(", ", lobby.PlayerIds)}");
    }

    IEnumerator SetInitialPlayerState()
    {
        // Wait a frame to ensure everything is initialized
        yield return null;

        // Wait until we're in a lobby
        yield return new WaitUntil(() => PlayFlowLobbyManagerV2.Instance.IsInLobby);

        // Set initial player state with MMR
        var initialState = new Dictionary<string, object>
        {
            ["mmr"] = playerMMR,
            ["ready"] = false,
            ["playerName"] = $"Player_{_playerId.Substring(0, 8)}"
        };

        Debug.Log($"[LobbyHelloWorld] Setting initial player state with MMR: {playerMMR}");

        PlayFlowLobbyManagerV2.Instance.UpdatePlayerState(initialState,
            onSuccess: (lobby) => {
                Debug.Log("[LobbyHelloWorld] Initial player state set successfully");
            },
            onError: (error) => {
                Debug.LogError($"[LobbyHelloWorld] Failed to set initial player state: {error}");
            }
        );
    }

    void SubscribeToEvents()
    {
        var events = PlayFlowLobbyManagerV2.Instance.Events;

        // Lobby events
        events.OnLobbyCreated.AddListener(OnLobbyCreated);
        events.OnLobbyJoined.AddListener(OnLobbyJoined);
        events.OnLobbyUpdated.AddListener(OnLobbyUpdated);
        events.OnLobbyLeft.AddListener(OnLobbyLeft);

        // Match events
        events.OnMatchStarted.AddListener(OnMatchStarted);
        events.OnMatchEnded.AddListener(OnMatchEnded);
        events.OnMatchRunning.AddListener(OnMatchRunning);
        events.OnMatchServerDetailsReady.AddListener(OnMatchServerDetailsReady);

        // Matchmaking events
        events.OnMatchmakingStarted.AddListener(OnMatchmakingStarted);
        events.OnMatchmakingCancelled.AddListener(OnMatchmakingCancelled);
        events.OnMatchmakingTimeout.AddListener(OnMatchmakingTimeout);
        events.OnMatchFound.AddListener(OnMatchFound);
        events.OnMatchAwaitingConfirmation.AddListener(OnMatchAwaitingConfirmation);
        events.OnMatchConfirmed.AddListener(OnMatchConfirmed);
        events.OnMatchDeclined.AddListener(OnMatchDeclined);
        events.OnQueueStats.AddListener(OnQueueStats); // V3: live queue telemetry

        // Player events
        events.OnPlayerJoined.AddListener(OnPlayerJoined);
        events.OnPlayerLeft.AddListener(OnPlayerLeft);

        // System events
        events.OnError.AddListener(OnError);

        // Session state changes
        var manager = PlayFlowLobbyManagerV2.Instance;
        manager.Events.OnStateChanged.AddListener(OnStateChanged);
    }

    // Event handlers
    void OnStateChanged(LobbyState oldState, LobbyState newState)
    {
        Debug.Log($"[LobbyHelloWorld] State changed: {oldState} -> {newState}");
    }

    void OnLobbyCreated(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Lobby created - {lobby.name}");
    }

    void OnLobbyJoined(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Joined lobby - {lobby.name}");
        // V3: players is a typed list; use PlayerIds helper for a quick string summary
        Debug.Log($"  Players: {string.Join(", ", lobby.PlayerIds)}");
        Debug.Log($"  Host: {lobby.host}");
    }

    void OnLobbyUpdated(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Lobby updated - {lobby.name} ({lobby.currentPlayers}/{lobby.maxPlayers})");

        // Check if SSE is connected
        var sseManager = LobbySseManager.Instance;
        if (sseManager != null && sseManager.IsConnected)
        {
            Debug.Log("[LobbyHelloWorld] Update received via SSE (real-time)");
        }
        else
        {
            Debug.Log("[LobbyHelloWorld] Update received via polling");
        }
    }

    void OnLobbyLeft()
    {
        Debug.Log("[LobbyHelloWorld] EVENT: Left lobby");
    }

    // =============================================================================
    // PLAYER STATE UPDATE EXAMPLES
    // =============================================================================

    /// <summary>
    /// Example: Update your own player state.
    /// Any player can update their own state at any time.
    /// </summary>
    public void UpdateMyOwnState()
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsInLobby)
        {
            Debug.LogWarning("Not in a lobby!");
            return;
        }

        var myState = new Dictionary<string, object>
        {
            ["position"] = new Dictionary<string, float> { ["x"] = 100f, ["y"] = 50f, ["z"] = 0f },
            ["health"] = 85,
            ["armor"] = 50,
            ["weapon"] = "plasma_rifle",
            ["team"] = "blue",
            ["ready"] = true,
            ["lastUpdated"] = DateTime.UtcNow.ToString()
        };

        PlayFlowLobbyManagerV2.Instance.UpdatePlayerState(myState,
            onSuccess: (lobby) => {
                var selfId = PlayFlowLobbyManagerV2.Instance.PlayerId;
                Debug.Log($"Successfully updated my state. My player ID: {selfId}");

                // V3: read state back via the typed helper
                var updatedState = lobby.GetPlayerState(selfId);
                if (updatedState != null)
                {
                    Debug.Log($"My updated state: {JsonConvert.SerializeObject(updatedState)}");
                }
            },
            onError: (error) => {
                Debug.LogError($"Failed to update my state: {error}");
            }
        );
    }

    /// <summary>
    /// Example: Host attempts to update another player's state.
    ///
    /// NOTE on V3: this is rejected by the server. In V3 each player owns their own
    /// state -- a host cannot mutate another player. This method is kept to document
    /// the behaviour change versus V2.
    /// </summary>
    public void HostUpdateAnotherPlayerState(string targetPlayerId)
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsInLobby)
        {
            Debug.LogWarning("Not in a lobby!");
            return;
        }

        if (!PlayFlowLobbyManagerV2.Instance.IsHost)
        {
            Debug.LogError("Only the host can attempt this call!");
            return;
        }

        var currentLobby = PlayFlowLobbyManagerV2.Instance.CurrentLobby;
        if (!currentLobby.ContainsPlayer(targetPlayerId))
        {
            Debug.LogError($"Player {targetPlayerId} is not in the lobby!");
            return;
        }

        var targetPlayerState = new Dictionary<string, object>
        {
            ["team"] = "red",
            ["role"] = "sniper",
            ["spawnPoint"] = new Dictionary<string, float> { ["x"] = 200f, ["y"] = 100f, ["z"] = 50f },
            ["allowedWeapons"] = new List<string> { "sniper_rifle", "pistol" },
            ["updatedByHost"] = true,
            ["hostUpdatedAt"] = DateTime.UtcNow.ToString()
        };

        PlayFlowLobbyManagerV2.Instance.UpdateStateForPlayer(targetPlayerId, targetPlayerState,
            onSuccess: (lobby) => {
                Debug.Log($"Host successfully updated state for player: {targetPlayerId}");
                var updatedState = lobby.GetPlayerState(targetPlayerId);
                if (updatedState != null)
                {
                    Debug.Log($"Target player's updated state: {JsonConvert.SerializeObject(updatedState)}");
                }
            },
            onError: (error) => {
                // In V3 we expect this to land here with "UpdateStateForPlayer can only update self in V3" (or similar).
                Debug.LogWarning($"(V3) Host-update rejected for {targetPlayerId}: {error}");
            }
        );
    }

    /// <summary>
    /// Example: Each player picks their own team.
    /// V3: the host no longer writes other players' state -- each player writes its own.
    /// In a real game you'd have clients coordinate this (e.g. round-robin, draft UI).
    /// </summary>
    public void AssignMyTeam(string team)
    {
        if (!PlayFlowLobbyManagerV2.Instance.IsInLobby)
        {
            Debug.LogError("Must be in a lobby to assign team!");
            return;
        }

        var teamState = new Dictionary<string, object>
        {
            ["team"] = team,
            ["teamAssignedAt"] = DateTime.UtcNow.ToString()
        };

        PlayFlowLobbyManagerV2.Instance.UpdatePlayerState(teamState);
        Debug.Log($"Self-assigned to team '{team}'.");
    }

    void OnPlayerJoined(PlayerAction action)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Player joined - {action.PlayerId}");
    }

    void OnPlayerLeft(PlayerAction action)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Player left - {action.PlayerId}");
    }

    void OnError(string error)
    {
        Debug.LogError($"[LobbyHelloWorld] EVENT: Error - {error}");
    }

    void OnMatchStarted(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Match start has been triggered for lobby {lobby.name}. Waiting for server to be ready...");
    }

    void OnMatchRunning(ConnectionInfo connectionInfo)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Server is ready! IP: {connectionInfo.Ip}, Port: {connectionInfo.Port}");
        // Here you would connect your game client using the connectionInfo details.
    }

    void OnMatchServerDetailsReady(List<PortMappingInfo> portMappings)
    {
        Debug.Log("[LobbyHelloWorld] EVENT: Full server details are ready.");
        foreach (var portInfo in portMappings)
        {
            Debug.Log($"  - Port '{portInfo.Name}' ({portInfo.Protocol}): connect to {portInfo.Host}:{portInfo.ExternalPort} (game listens on internal port {portInfo.InternalPort})");
        }

        // Preferred: look up by the port name you defined in the dashboard
        var lobby = PlayFlowLobbyManagerV2.Instance.CurrentLobby;
        if (lobby != null && lobby.TryGetPort("game_udp", out var gamePort))
        {
            Debug.Log($"[LobbyHelloWorld] Connect your netcode to {gamePort.host}:{gamePort.external_port}");
            // NetworkManager.Singleton.StartClient(gamePort.host, gamePort.external_port);
        }
    }

    void OnMatchEnded(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Match ended in lobby {lobby.name}. Returning to 'waiting' status.");
    }

    void OnMatchmakingStarted(Lobby lobby)
    {
        // V3: matchmaking info lives on lobby.matchmaking (may be null in the first tick)
        var mode = lobby.matchmaking?.mode ?? "(unknown)";
        Debug.Log($"[LobbyHelloWorld] EVENT: Matchmaking started! Mode: {mode}, Status: {lobby.status}");
        Debug.Log("[LobbyHelloWorld] Looking for opponents with similar MMR...");
    }

    void OnMatchmakingCancelled(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Matchmaking cancelled. Status: {lobby.status}");
    }

    void OnMatchmakingTimeout(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Matchmaking timed out. Status: {lobby.status}");
    }

    void OnQueueStats(QueueStats stats)
    {
        // V3: streamed every ~10s while in queue
        Debug.Log($"[QUEUE] {stats.playersSearching} players searching across {stats.lobbiesInQueue} lobbies, avg wait: {stats.avgWaitSeconds:F1}s");
    }

    void OnMatchFound(Lobby lobby)
    {
        Debug.Log($"[LobbyHelloWorld] EVENT: Match found! Status: {lobby.status}");
        Debug.Log("[LobbyHelloWorld] Game server is being launched...");

        // V3: there's no longer a free-form matchmakingData blob. Final queue stats
        // (if any) live on lobby.matchmaking.queueStats.
        if (lobby.matchmaking?.queueStats != null)
        {
            var qs = lobby.matchmaking.queueStats;
            Debug.Log($"[LobbyHelloWorld] Final queue snapshot: searching={qs.playersSearching}, lobbies={qs.lobbiesInQueue}, avgWait={qs.avgWaitSeconds:F1}s");
        }
    }

    // ─── Match confirmation (CS2-style "Accept Match" flow) ────────────────
    //
    // Fires when the mode has `matchConfirmation.enabled: true` and a match
    // has been found. Players have `lobby.matchmaking.confirmation.deadline`
    // to accept (Y key) or decline (X key). If anyone declines or times out,
    // all matched lobbies return to `waiting` — players must re-queue.

    void OnMatchAwaitingConfirmation(Lobby lobby)
    {
        var deadline = lobby.matchmaking?.confirmation?.deadline ?? "unknown";
        Debug.Log($"[LobbyHelloWorld] EVENT: Match found — press Y to ACCEPT or X to DECLINE. Deadline: {deadline}");
    }

    void OnMatchConfirmed(Lobby lobby)
    {
        Debug.Log("[LobbyHelloWorld] EVENT: You confirmed the match. Waiting for other lobbies to accept…");
    }

    void OnMatchDeclined(Lobby lobby)
    {
        Debug.Log("[LobbyHelloWorld] EVENT: Match cancelled (decline or timeout). Lobby is back in 'waiting'. Press M to re-queue.");
    }

    void OnDestroy()
    {
        // Clean up
        if (PlayFlowLobbyManagerV2.Instance != null)
        {
            // If the player is in a lobby, make sure they leave it gracefully.
            if (PlayFlowLobbyManagerV2.Instance.IsInLobby)
            {
                // This is a fire-and-forget call. We don't wait for the response
                // because the application is likely quitting.
                PlayFlowLobbyManagerV2.Instance.LeaveLobby();
            }

            PlayFlowLobbyManagerV2.Instance.Disconnect();

            // Unsubscribe from events
            var events = PlayFlowLobbyManagerV2.Instance.Events;
            events.OnLobbyCreated.RemoveAllListeners();
            events.OnLobbyJoined.RemoveAllListeners();
            events.OnLobbyUpdated.RemoveAllListeners();
            events.OnLobbyLeft.RemoveAllListeners();
            events.OnPlayerJoined.RemoveAllListeners();
            events.OnPlayerLeft.RemoveAllListeners();
            events.OnMatchStarted.RemoveAllListeners();
            events.OnMatchEnded.RemoveAllListeners();
            events.OnMatchRunning.RemoveAllListeners();
            events.OnMatchServerDetailsReady.RemoveAllListeners();
            events.OnMatchmakingStarted.RemoveAllListeners();
            events.OnMatchmakingCancelled.RemoveAllListeners();
            events.OnMatchmakingTimeout.RemoveAllListeners();
            events.OnMatchFound.RemoveAllListeners();
            events.OnMatchAwaitingConfirmation.RemoveAllListeners();
            events.OnMatchConfirmed.RemoveAllListeners();
            events.OnMatchDeclined.RemoveAllListeners();
            events.OnQueueStats.RemoveAllListeners();
            events.OnError.RemoveAllListeners();
        }
    }
}
