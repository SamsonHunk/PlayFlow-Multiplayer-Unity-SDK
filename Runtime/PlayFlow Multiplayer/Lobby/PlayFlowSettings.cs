using UnityEngine;

namespace PlayFlow
{
    [CreateAssetMenu(fileName = "PlayFlowSettings", menuName = "PlayFlow/Settings")]
    public class PlayFlowSettings : ScriptableObject
    {
        [Header("API Configuration")]
        [Tooltip("Your PlayFlow API key")]
        public string apiKey;
        
        [Tooltip("The base URL for the PlayFlow backend (V3 host only; the /api/v3/lobbies/{config} path is added by the API/SSE layers)")]
        public string baseUrl = "https://api.computeflow.cloud";

        [Tooltip("The default lobby configuration name (V3 default is lowercase 'default')")]
        public string defaultLobbyConfig = "default";
        
        [Header("Network Settings")]
        [Tooltip("How often to refresh lobby data (in seconds)")]
        [Range(3f, 30f)]
        public float refreshInterval = 5f;
        
        [Tooltip("Enable or disable automatic refreshing of lobby data.")]
        public bool autoRefresh = true;
        
        [Tooltip("Maximum number of retry attempts for failed requests")]
        [Range(1, 10)]
        public int maxRetryAttempts = 3;
        
        [Tooltip("Delay between retry attempts (in seconds)")]
        [Range(0.5f, 5f)]
        public float retryDelay = 1f;
        
        [Header("Timeouts")]
        [Tooltip("Request timeout in seconds")]
        public float requestTimeout = 30f;
        
        [Tooltip("Connection timeout in seconds")]
        public float connectionTimeout = 10f;
        
        [Header("Heartbeat")]
        /// <summary>
        /// Legacy V2 field. In V3 the SSE connection IS the heartbeat — while a player's SSE stream
        /// is open, the server treats the player as alive. The API-level SendHeartbeat call is a no-op
        /// stub. This field is preserved so inspector-configured assets don't error; toggling it has
        /// no effect on V3 connection health.
        /// </summary>
        [Tooltip("V3: no-op. SSE is the heartbeat — leaving this disabled is recommended.")]
        public bool enableHeartbeat = false;

        /// <summary>
        /// Legacy V2 field. V3 uses SSE as the heartbeat; this interval is ignored at runtime.
        /// </summary>
        [Tooltip("V3: ignored. SSE is the heartbeat.")]
        [Range(15f, 300f)]
        public float heartbeatInterval = 30f;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        public bool debugLogging = false;
        
        private void OnValidate()
        {
            refreshInterval = Mathf.Max(3f, refreshInterval);
            requestTimeout = Mathf.Max(5f, requestTimeout);
            connectionTimeout = Mathf.Max(5f, connectionTimeout);
            heartbeatInterval = Mathf.Max(15f, heartbeatInterval);
        }
    }
} 