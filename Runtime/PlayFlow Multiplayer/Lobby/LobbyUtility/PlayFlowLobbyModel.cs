using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace PlayFlow
{
    // ---------------------------------------------------------------------
    //  V3 Lobby data model
    // ---------------------------------------------------------------------

    [Serializable]
    public class LobbyPlayer
    {
        public string id;
        public Dictionary<string, object> state;
        public bool isHost;
    }

    /// <summary>
    /// A network port mapping on the game server. Matches the servers API
    /// (`PortMappingSchema`) — the same shape you get from GET /v3/servers/{id}.
    /// </summary>
    [Serializable]
    public class GameServerPort
    {
        /// <summary>Friendly name (e.g. "game_udp", "voice_tcp").</summary>
        public string name;
        /// <summary>Port the game server listens on inside the container.</summary>
        public int internal_port;
        /// <summary>Public port game clients connect to.</summary>
        public int external_port;
        /// <summary>Network protocol: "udp" or "tcp".</summary>
        public string protocol;
        /// <summary>Hostname or IP address clients connect to.</summary>
        public string host;
        /// <summary>True if TLS termination is enabled (TCP only).</summary>
        public bool tls_enabled;
    }

    /// <summary>
    /// Full game server state — mirrors `InstanceResponseSchema` from the
    /// servers API. Reading `lobby.server` returns the same shape you'd get
    /// from GET /v3/servers/{instance_id}. The server is a primitive the
    /// lobby wraps around.
    /// </summary>
    [Serializable]
    public class GameServerInfo
    {
        public string instance_id;
        public string name;
        /// <summary>"launching" | "running" | "stopped".</summary>
        public string status;
        /// <summary>Port mappings. Connect to host:external_port per entry.</summary>
        public List<GameServerPort> network_ports;
        public string startup_args;
        /// <summary>"match_based" | "persistent_world".</summary>
        public string service_type;
        public string compute_size;
        public string region;
        public string version_tag;
        public int? version;
        public string started_at;
        public string stopped_at;
        public bool auto_restart;
        public Dictionary<string, object> custom_data;
        public int? ttl;
        public bool is_pool_server;
        public string pool_claimed_at;
        public string match_id;
        public string created_at;
        public string updated_at;

        [JsonIgnore]
        public bool IsRunning => status == "running";

        /// <summary>First network port (typically your main game port).</summary>
        [JsonIgnore]
        public GameServerPort PrimaryPort => network_ports != null && network_ports.Count > 0 ? network_ports[0] : null;

        /// <summary>Look up a port by its friendly name (e.g. "game_udp"). Returns null if not found.</summary>
        public GameServerPort FindPort(string portName)
        {
            if (network_ports == null || string.IsNullOrEmpty(portName)) return null;
            foreach (var p in network_ports)
            {
                if (p != null && p.name == portName) return p;
            }
            return null;
        }

        /// <summary>True when a port with the given name exists and was populated.</summary>
        public bool TryGetPort(string portName, out GameServerPort port)
        {
            port = FindPort(portName);
            return port != null;
        }

        /// <summary>Host (IP or DNS) to connect to for the given port name. Null if no such port.</summary>
        public string GetHost(string portName) => FindPort(portName)?.host;

        /// <summary>External port number clients connect to for the given port name. 0 if no such port.</summary>
        public int GetExternalPort(string portName) => FindPort(portName)?.external_port ?? 0;
    }

    [Serializable]
    public class QueueStats
    {
        public int playersSearching;
        public int lobbiesInQueue;
        public float avgWaitSeconds;
    }

    [Serializable]
    public class MatchConfirmationInfo
    {
        /// <summary>ISO 8601 deadline. The match cancels if any lobby hasn't confirmed by then.</summary>
        public string deadline;
        /// <summary>True if THIS lobby has confirmed.</summary>
        public bool confirmed;
    }

    [Serializable]
    public class MatchmakingInfo
    {
        public string mode;
        public string startedAt;
        public QueueStats queueStats;
        /// <summary>Non-null while status=match_found and players are accepting/declining.</summary>
        public MatchConfirmationInfo confirmation;
    }

    [Serializable]
    public class Lobby
    {
        public string id;
        public string code;           // V3: was inviteCode
        public string config;         // V3: was lobbyConfigId
        public string name;
        public string status;
        public string host;
        public int maxPlayers;
        public int currentPlayers;
        public string region;
        public bool isPrivate;
        public bool allowLateJoin;
        public Dictionary<string, object> settings;
        public List<LobbyPlayer> players;     // V3: was string[]
        public GameServerInfo server;         // V3: was Dictionary gameServer
        public MatchmakingInfo matchmaking;   // V3: was scattered fields
        public string matchId;
        public string createdAt;
        public string updatedAt;

        // -----------------------------------------------------------------
        //  Back-compat alias for calling code that still says inviteCode
        // -----------------------------------------------------------------
        [JsonIgnore]
        public string InviteCode => code;

        // -----------------------------------------------------------------
        //  Status helpers
        // -----------------------------------------------------------------
        [JsonIgnore]
        public bool HasServer => server != null;

        [JsonIgnore]
        public bool HasMatchmaking => matchmaking != null;

        [JsonIgnore]
        public bool IsInGame => status == "in_game";

        [JsonIgnore]
        public bool IsInQueue => status == "in_queue";

        [JsonIgnore]
        public bool IsWaiting => status == "waiting";

        // -----------------------------------------------------------------
        //  Player helpers
        // -----------------------------------------------------------------
        [JsonIgnore]
        public string[] PlayerIds => players?.Select(p => p.id).ToArray() ?? Array.Empty<string>();

        public LobbyPlayer FindPlayer(string playerId)
        {
            return players?.FirstOrDefault(p => p.id == playerId);
        }

        public Dictionary<string, object> GetPlayerState(string playerId)
        {
            var p = FindPlayer(playerId);
            return p?.state;
        }

        public bool ContainsPlayer(string playerId)
        {
            return players != null && players.Any(p => p.id == playerId);
        }

        // -----------------------------------------------------------------
        //  Region display helper (kept from V2)
        // -----------------------------------------------------------------
        public string GetRegionDisplayName()
        {
            if (string.IsNullOrEmpty(region))
                return "Unknown";

            return region switch
            {
                "us-east" => "US East",
                "us-west" => "US West",
                "eu-west" => "Europe West",
                "eu-central" => "Europe Central",
                "asia-east" => "Asia East",
                "asia-south" => "Asia South",
                "australia" => "Australia",
                _ => region
            };
        }

        // -----------------------------------------------------------------
        //  Game server status (kept, adapted to V3)
        // -----------------------------------------------------------------
        public string GetGameServerStatus()
        {
            return server?.status ?? "N/A";
        }

        // -----------------------------------------------------------------
        //  Port mapping helpers
        // -----------------------------------------------------------------
        public List<PortMappingInfo> GetPortMappings()
        {
            var mappings = new List<PortMappingInfo>();
            if (server?.network_ports == null)
                return mappings;

            foreach (var p in server.network_ports)
            {
                mappings.Add(PortMappingInfoExtensions.FromGameServerPort(p));
            }
            return mappings;
        }

        public bool TryGetPortMapping(int internalPort, out PortMappingInfo portMapping)
        {
            foreach (var mapping in GetPortMappings())
            {
                if (mapping.InternalPort == internalPort)
                {
                    portMapping = mapping;
                    return true;
                }
            }
            portMapping = default;
            return false;
        }

        // -----------------------------------------------------------------
        //  Name-based port lookup (preferred) — delegates to server
        // -----------------------------------------------------------------

        /// <summary>Look up a port by its friendly name (e.g. "game_udp"). Returns null if no server or no such port.</summary>
        public GameServerPort FindPort(string portName) => server?.FindPort(portName);

        /// <summary>True when the server is running and has a port with the given name.</summary>
        public bool TryGetPort(string portName, out GameServerPort port)
        {
            port = FindPort(portName);
            return port != null;
        }

        /// <summary>Host (IP or DNS) clients connect to for the given port name. Null if not available.</summary>
        public string GetHost(string portName) => server?.GetHost(portName);

        /// <summary>External port number clients connect to for the given port name. 0 if not available.</summary>
        public int GetExternalPort(string portName) => server?.GetExternalPort(portName) ?? 0;

        public static ConnectionInfo? GetPrimaryConnectionInfo(Lobby lobby)
        {
            var primary = lobby?.server?.PrimaryPort;
            if (primary == null) return null;
            if (string.IsNullOrEmpty(primary.host)) return null;

            return new ConnectionInfo { Ip = primary.host, Port = primary.external_port };
        }

        // -----------------------------------------------------------------
        //  Deep clone
        // -----------------------------------------------------------------
        public Lobby Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<Lobby>(json);
        }

        public override string ToString()
        {
            return $"Lobby[{name}({id}), Players: {currentPlayers}/{maxPlayers}, Status: {status}]";
        }
    }

    // -------------------------------------------------------------------------
    //  Adapter: GameServerPort -> PortMappingInfo (public API compatibility)
    // -------------------------------------------------------------------------
    public static class PortMappingInfoExtensions
    {
        public static PortMappingInfo FromGameServerPort(GameServerPort p)
        {
            return new PortMappingInfo
            {
                Name = p.name,
                Protocol = p.protocol,
                Host = p.host,
                InternalPort = p.internal_port,
                ExternalPort = p.external_port,
            };
        }
    }
}
