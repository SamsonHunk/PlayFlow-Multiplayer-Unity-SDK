<div align="center">

<img src="https://raw.githubusercontent.com/PlayFlowCloud/PlayFlow-Multiplayer-Unity-SDK/main/Resources/playflow.png" width="150">

# PlayFlow Multiplayer Unity SDK
**The complete, production-ready backend for your Unity multiplayer game.**

</div>

<div align="center">

[![Made with Unity](https://img.shields.io/badge/Made%20with-Unity-57b9d3.svg?style=for-the-badge&logo=unity)](https://unity.com)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)
[![YouTube Channel](https://img.shields.io/badge/YouTube-%40playflow__cloud-red.svg?style=for-the-badge&logo=youtube)](https://www.youtube.com/@playflow_cloud)
[![Discord](https://img.shields.io/discord/882650237829779546?style=for-the-badge&logo=discord&label=Discord)](https://discord.gg/P5w45Vx5Q8)

</div>

---

**PlayFlow** is the all-in-one multiplayer platform that handles the complexity of game server hosting, lobbies, and matchmaking, so you can focus on building your game. This official Unity SDK is your gateway to unlocking PlayFlow's powerful features directly in your project.

## Core Features

| Feature | Description | Key Components |
|---|---|---|
| **🚀 One-Click Server Hosting** | Build and deploy dedicated Linux servers for your game directly from the Unity Editor. No command-line or Docker knowledge required. | `PlayFlowCloudDeploy` Window |
| **🤝 Modern Lobby System** | A powerful, event-driven system for creating and managing game lobbies. Fully compatible with WebGL, consoles, and PC. | `PlayFlowLobbyManagerV2` |
| **⚔️ Rules-Based Matchmaking** | Match by skill, version, region, or any custom rule via CEL expressions. Configurable "Accept Match" dialog, auto-expanding skill buckets, party queueing. | `PlayFlowLobbyManagerV2.FindMatch()` |
| **🎮 Direct Server API** | Programmatic C# access to manage your entire game server fleet for advanced, custom scaling logic. | `PlayflowServerApiClient` |

---

## Installation

1.  In the Unity Editor, open the **Package Manager** (`Window > Package Manager`).
2.  Click the **'+'** icon and select **"Add package from git URL..."**.
3.  Enter the following URL and click **Add**:
    ```
    https://github.com/PlayFlowCloud/PlayFlow-Multiplayer-Unity-SDK.git
    ```

---

## Quick Start

### 1. Deploy a Server in One Click

Get your dedicated server running on the PlayFlow cloud without ever leaving the Unity Editor.

1.  Open the deployment window from **PlayFlow > PlayFlow Cloud**.
2.  Enter your **API Token** from your [PlayFlow Dashboard](https://app.playflowcloud.com).
3.  Select your server scene and give your build a version tag (e.g., "v1.0").
4.  Click **Upload Server**.

That's it! PlayFlow handles building the headless Linux binary, uploading it, and making it available to your game clients.

*![A screenshot or GIF of the PlayFlowCloudDeploy editor window would be great here to show the one-click process.]*

### 2. Create and Manage a Lobby

The `PlayFlowLobbyManagerV2` provides a simple, singleton-based interface for all lobby operations. Its event-driven architecture makes it easy to integrate with your UI.

This example shows how to create a lobby and handle the success and error callbacks. This pattern is fully compatible with all platforms, including WebGL.

```csharp
using UnityEngine;
using PlayFlow;
using System;
using System.Collections.Generic;

public class LobbyExample : MonoBehaviour
{
    void Start()
    {
        // The manager is a singleton, easily accessible anywhere.
        // Make sure you have the PlayFlowLobbyManagerV2 prefab in your scene.
        if (PlayFlowLobbyManagerV2.Instance == null)
        {
            Debug.LogError("PlayFlowLobbyManagerV2 not found in scene!");
            return;
        }

        // Initialize with a unique player ID.
        string playerId = "player-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PlayFlowLobbyManagerV2.Instance.Initialize(playerId, () => {
            Debug.Log("PlayFlow Lobby Manager is ready!");
        });

        // Subscribe to events to make your UI react to lobby changes.
        PlayFlowLobbyManagerV2.Instance.Events.OnLobbyJoined.AddListener(HandleLobbyJoined);
    }

    public void CreateLobby()
    {
        Debug.Log("Creating a new lobby...");
        
        PlayFlowLobbyManagerV2.Instance.CreateLobby(
            "My Awesome Game", 
            maxPlayers: 8, 
            isPrivate: false,
            onSuccess: (createdLobby) => {
                Debug.Log($"Lobby created successfully! ID: {createdLobby.id}");
            },
            onError: (error) => {
                Debug.LogError($"Failed to create lobby: {error}");
            }
        );
    }
    
    private void HandleLobbyJoined(Lobby lobby)
    {
        Debug.Log($"EVENT: Successfully joined lobby '{lobby.name}' with {lobby.currentPlayers} players.");
        // Your logic to transition to the lobby screen would go here.
    }

    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks.
        if (PlayFlowLobbyManagerV2.Instance != null)
        {
            PlayFlowLobbyManagerV2.Instance.Events.OnLobbyJoined.RemoveListener(HandleLobbyJoined);
        }
    }
}
```

### 3. Find a Match

Matchmaking is part of the same `PlayFlowLobbyManagerV2`. You configure matchmaking **modes** (1v1, 5v5, FFA, asymmetric roles, …) in the [dashboard](https://app.playflowcloud.com), then queue for them from your lobby. When opponents are found, every matched lobby transitions to `in_game` with the same `matchId` and the same game server.

```csharp
using UnityEngine;
using PlayFlow;

public class MatchmakingExample : MonoBehaviour
{
    void Start()
    {
        var events = PlayFlowLobbyManagerV2.Instance.Events;

        // Fired when opponents are found + the server transitions to running
        events.OnMatchFound.AddListener(lobby =>
        {
            // Look up by the port name you defined in the dashboard
            if (lobby.TryGetPort("game_udp", out var port))
                Debug.Log($"Connect to {port.host}:{port.external_port}");
        });

        // Live queue telemetry — "42 players searching, ~12s avg wait"
        events.OnQueueStats.AddListener(stats =>
            Debug.Log($"Searching: {stats.playersSearching} players, avg wait {stats.avgWaitSeconds}s"));
    }

    // Host queues the lobby for a specific mode (must exist in your dashboard config)
    public void QueueRanked1v1()
    {
        PlayFlowLobbyManagerV2.Instance.FindMatch(
            mode: "ranked_1v1",
            onSuccess: lobby => Debug.Log($"In queue. Status: {lobby.status}"),
            onError: err => Debug.LogError($"Matchmaking failed: {err}")
        );
    }

    // Cancel searching — back to 'waiting'
    public void StopSearching()
    {
        PlayFlowLobbyManagerV2.Instance.CancelMatchmaking();
    }
}
```

**Matchmaking rules** (defined per mode in the dashboard) shape who matches with whom. PlayFlow ships 5 primitives: `difference` (skill buckets), `equals` (same version/map), `not_equals` (anti-rematch), `region` (overlap), `expression` (CEL for anything custom). See the [matchmaking guide](https://docs.playflowcloud.com/lobbies/matchmaking) for examples.

### 4. CS2-Style "Accept Match" flow (optional)

If your mode has `matchConfirmation.enabled: true` in the dashboard, matches pause at `match_found` for players to accept or decline before the server launches.

```csharp
var events = PlayFlowLobbyManagerV2.Instance.Events;

events.OnMatchAwaitingConfirmation.AddListener(lobby =>
{
    // Show your "Accept Match" dialog
    var deadline = lobby.matchmaking.confirmation.deadline;
    Debug.Log($"Match found! Accept within deadline: {deadline}");
});

events.OnMatchConfirmed.AddListener(_ =>
    Debug.Log("You accepted. Waiting on other players…"));

events.OnMatchDeclined.AddListener(_ =>
    Debug.Log("Match cancelled. Everyone is back in 'waiting' — re-queue when ready."));

// Wired to your UI buttons
void OnAcceptClicked()  => PlayFlowLobbyManagerV2.Instance.ConfirmMatch();
void OnDeclineClicked() => PlayFlowLobbyManagerV2.Instance.DeclineMatch();
```

If any player declines or the timeout expires, **all** participating lobbies return to `waiting` — players explicitly re-queue when ready.

### 5. Rematch (keep the same lobby)

When a match ends, the host can recycle the lobby for another round instead of re-inviting everyone:

```csharp
// Host only. Stops the game server, returns lobby to 'waiting', keeps players + invite code.
PlayFlowLobbyManagerV2.Instance.EndMatch();
```

If the game server stops on its own (TTL, crash, clean exit), the lobby **auto-heals** to `waiting` with the same effect — no call needed.

## Documentation & Support

-   **Full Documentation:** For detailed guides and API references, visit our [Official Docs](https://docs.playflowcloud.com).
-   **Community & Support:** Have questions? Join our [Discord Server](https://discord.gg/P5w45Vx5Q8) to chat with the team and other developers.
-   **Contact Us:** For business inquiries, email us at [support@playflowcloud.com](mailto:support@playflowcloud.com).