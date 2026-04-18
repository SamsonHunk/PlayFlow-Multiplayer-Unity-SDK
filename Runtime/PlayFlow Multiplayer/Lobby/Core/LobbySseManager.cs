using System;
using System.Collections;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlayFlow
{
    /// <summary>
    /// Internal SSE manager for real-time lobby updates (V3).
    /// Connects to GET {baseUrl}/api/v3/lobbies/{config}/me/events with headers:
    ///   api-key, x-player-id, Accept: text/event-stream, Cache-Control: no-cache
    /// The server resolves the player's current lobby from x-player-id — no lobbyId required.
    /// While this SSE is open the server treats the player as alive (no separate heartbeat needed).
    /// This is completely transparent to game developers.
    /// </summary>
    internal class LobbySseManager : MonoBehaviour
    {
        // Connection config (V3)
        private string _playerId;
        private string _apiKey;
        private string _lobbyConfigName;
        private string _baseUrl;

        // Internal state
        private bool _isConnecting = false;
        private bool _shouldReconnect = true;
        private float _reconnectDelay = 1f;
        private float _maxReconnectDelay = 30f;
        private int _reconnectAttempts = 0;
        private int _maxReconnectAttempts = 10;
        private Coroutine _sseCoroutine;
        private Coroutine _reconnectCoroutine;
        private Coroutine _periodicRetryCoroutine;
        private float _lastDataReceived;
        private bool _isPaused = false;
        private float _lastSuccessfulConnection = 0f;
        private bool _hasReachedMaxAttempts = false;
        private float _periodicRetryInterval = 10f; // Try SSE again every 10s when in polling mode
        private bool _debugLogging = false;
        private bool _hasConnectionParams = false;

        // Events for internal use
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<Lobby> OnLobbyUpdated;
        public event Action<string> OnLobbyDeleted;
        public event Action<QueueStats> OnQueueStats;
        public event Action<string> OnError;

        // Connection state
        public bool IsConnected { get; private set; } = false;

        private static LobbySseManager _instance;
        public static LobbySseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("LobbySseManager");
                    go.hideFlags = HideFlags.HideInHierarchy;
                    _instance = go.AddComponent<LobbySseManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Connect to the V3 player event stream.
        /// The server resolves the player's current lobby from the x-player-id header —
        /// no lobbyId is needed. Call Disconnect() to tear down.
        /// </summary>
        public void Connect(string playerId, string configName, string baseUrl, string apiKey)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[LobbySseManager] Connect called with empty playerId");
                return;
            }
            if (string.IsNullOrEmpty(configName))
            {
                Debug.LogError("[LobbySseManager] Connect called with empty configName");
                return;
            }
            if (string.IsNullOrEmpty(baseUrl))
            {
                Debug.LogError("[LobbySseManager] Connect called with empty baseUrl");
                return;
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[LobbySseManager] Connect called with empty apiKey");
                return;
            }

            // Check platform support
            if (!PlatformSSEHandler.IsSSESupported())
            {
                Debug.LogWarning("[LobbySseManager] SSE not supported on this platform, using polling instead");
                OnError?.Invoke("SSE not supported on this platform, using polling instead");
                return;
            }

            _playerId = playerId;
            _lobbyConfigName = configName;
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _hasConnectionParams = true;
            _shouldReconnect = true;

            // Reset retry state for a fresh connection
            _reconnectAttempts = 0;
            _reconnectDelay = 1f;
            _hasReachedMaxAttempts = false;

            if (_debugLogging)
            {
                Debug.Log($"[LobbySseManager] Connect - PlayerId: {_playerId}, Config: {_lobbyConfigName}, URL: {_baseUrl}");
            }

            // Stop any in-flight connection before starting a new one
            StopConnectionCoroutines(false);

            if (_sseCoroutine == null && !_isPaused)
            {
                _sseCoroutine = StartCoroutine(SSEConnectionCoroutine());
            }

            if (_periodicRetryCoroutine == null)
            {
                _periodicRetryCoroutine = StartCoroutine(PeriodicSSERetryCoroutine());
            }
        }

        /// <summary>
        /// Deprecated V2 entrypoint — forwards to Connect(). debugLogging is preserved.
        /// Kept for backward compatibility until Phase 3 updates callers.
        /// </summary>
        public void Initialize(string playerId, string apiKey, string lobbyConfigName, string baseUrl, bool debugLogging = false)
        {
            _debugLogging = debugLogging;
            _playerId = playerId;
            _apiKey = apiKey;
            _lobbyConfigName = lobbyConfigName;
            _baseUrl = baseUrl?.TrimEnd('/');
            _hasConnectionParams = !string.IsNullOrEmpty(_playerId)
                                && !string.IsNullOrEmpty(_apiKey)
                                && !string.IsNullOrEmpty(_lobbyConfigName)
                                && !string.IsNullOrEmpty(_baseUrl);

            if (_debugLogging)
            {
                Debug.Log($"[LobbySseManager] Initialized (legacy) - PlayerId: {_playerId}, Config: {_lobbyConfigName}, URL: {_baseUrl}");
            }
        }

        /// <summary>
        /// Deprecated V2 entrypoint. V3: lobbyId is ignored — server resolves the lobby
        /// from x-player-id. Forwards to Connect() using previously-initialized params.
        /// </summary>
        public void ConnectToLobby(string lobbyId)
        {
            // V3: lobbyId is ignored, server resolves via x-player-id
            if (!_hasConnectionParams)
            {
                Debug.LogWarning("[LobbySseManager] ConnectToLobby called before Initialize()");
                return;
            }
            Connect(_playerId, _lobbyConfigName, _baseUrl, _apiKey);
        }

        /// <summary>
        /// Disconnect from the current SSE connection.
        /// </summary>
        public void Disconnect()
        {
            _shouldReconnect = false;
            StopConnectionCoroutines(true);
        }

        /// <summary>
        /// Pauses the SSE connection, typically when the app goes into the background.
        /// </summary>
        public void Pause()
        {
            if (_isPaused) return;
            if (_debugLogging)
            {
                Debug.Log("[LobbySseManager] Pausing SSE connection.");
            }
            _isPaused = true;
            StopConnectionCoroutines(false); // Stop connection but keep params for resume
        }

        /// <summary>
        /// Resumes the SSE connection, typically when the app returns to the foreground.
        /// </summary>
        public void Resume()
        {
            if (!_isPaused) return;
            if (_debugLogging)
            {
                Debug.Log("[LobbySseManager] Resuming SSE connection.");
            }
            _isPaused = false;

            if (_hasConnectionParams && _sseCoroutine == null && _shouldReconnect)
            {
                // Reset retry state so resume gets a fresh attempt
                _reconnectAttempts = 0;
                _reconnectDelay = 1f;
                _hasReachedMaxAttempts = false;
                _sseCoroutine = StartCoroutine(SSEConnectionCoroutine());

                if (_periodicRetryCoroutine == null)
                {
                    _periodicRetryCoroutine = StartCoroutine(PeriodicSSERetryCoroutine());
                }
            }
        }

        private void StopConnectionCoroutines(bool clearParams)
        {
            if (clearParams)
            {
                _hasConnectionParams = false;
            }

            if (_sseCoroutine != null)
            {
                StopCoroutine(_sseCoroutine);
                _sseCoroutine = null;
            }

            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }

            if (_periodicRetryCoroutine != null)
            {
                StopCoroutine(_periodicRetryCoroutine);
                _periodicRetryCoroutine = null;
            }

            _isConnecting = false;

            if (IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        private IEnumerator SSEConnectionCoroutine()
        {
            if (_isConnecting)
            {
                yield break;
            }

            _isConnecting = true;

            // V3: GET {baseUrl}/api/v3/lobbies/{config}/me/events
            // api-key and x-player-id are HEADERS now (not query params).
            string encodedConfig = UnityWebRequest.EscapeURL(_lobbyConfigName);
            string fullUrl = $"{_baseUrl}/api/v3/lobbies/{encodedConfig}/me/events";

            if (_debugLogging)
            {
                Debug.Log($"[LobbySseManager] Connecting to SSE URL: {fullUrl} (player: {_playerId})");
            }

            using (var request = UnityWebRequest.Get(fullUrl))
            {
                request.SetRequestHeader("api-key", _apiKey);
                request.SetRequestHeader("x-player-id", _playerId);
                request.SetRequestHeader("Accept", "text/event-stream");
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.downloadHandler = new SSEDownloadHandler(this);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Unity can report "Unknown Error" with HTTP 200 on a clean SSE stream closure.
                    // Handle this gracefully — not a critical error.
                    bool isCleanClosure = request.responseCode == 200 && request.error == "Unknown Error";

                    if (!isCleanClosure)
                    {
                        string error = $"SSE connection failed: {request.error} (HTTP {request.responseCode})";
                        Debug.LogError($"[LobbySseManager] {error}");
                        OnError?.Invoke(error);
                    }
                    else if (_debugLogging)
                    {
                        Debug.Log("[LobbySseManager] SSE stream closed by the server (HTTP 200). This is usually normal.");
                    }

                    if (_shouldReconnect && _reconnectCoroutine == null && !_isPaused)
                    {
                        _reconnectCoroutine = StartCoroutine(ReconnectCoroutine());
                    }
                }
            }

            _isConnecting = false;
            _sseCoroutine = null;

            if (IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        private IEnumerator ReconnectCoroutine()
        {
            // Exponential backoff with jitter
            float jitter = UnityEngine.Random.Range(0.5f, 1.5f);
            float delay = Mathf.Min(_reconnectDelay * jitter, _maxReconnectDelay);

            yield return new WaitForSeconds(delay);

            _reconnectCoroutine = null;
            _reconnectAttempts++;

            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                if (!_hasReachedMaxAttempts)
                {
                    _hasReachedMaxAttempts = true;
                    OnError?.Invoke($"Failed to reconnect after {_maxReconnectAttempts} attempts. Will retry periodically.");
                    if (_debugLogging)
                    {
                        Debug.Log("[LobbySseManager] Max reconnect attempts reached. Switching to periodic retry mode.");
                    }
                }
                // Fall through to periodic retry — don't set _shouldReconnect = false.
                yield break;
            }

            _reconnectDelay = Mathf.Min(_reconnectDelay * 2f, _maxReconnectDelay);

            if (_shouldReconnect && _hasConnectionParams && !_isPaused)
            {
                if (_sseCoroutine == null)
                {
                    _sseCoroutine = StartCoroutine(SSEConnectionCoroutine());
                }
            }
        }

        internal void HandleSSEMessage(string eventType, string data)
        {
            _lastDataReceived = Time.time;
            _lastSuccessfulConnection = Time.time;
            _reconnectAttempts = 0;
            _reconnectDelay = 1f;
            _hasReachedMaxAttempts = false;

            try
            {
                switch (eventType)
                {
                    case "connected":
                    {
                        // V3: data IS the lobby (NOT wrapped in { lobby: ... })
                        if (_debugLogging)
                        {
                            Debug.Log("[LobbySseManager] Received 'connected' event");
                        }
                        var lobby = JsonConvert.DeserializeObject<Lobby>(data);
                        // Always fire OnConnected — even if the player isn't currently in a lobby
                        // the stream is still live (server may send lobby_updated later).
                        if (!IsConnected)
                        {
                            IsConnected = true;
                            OnConnected?.Invoke();
                            if (_debugLogging)
                            {
                                Debug.Log("[LobbySseManager] SSE connection established successfully");
                            }
                        }
                        if (lobby != null && !string.IsNullOrEmpty(lobby.id))
                        {
                            OnLobbyUpdated?.Invoke(lobby);
                        }
                        break;
                    }

                    case "lobby_updated":
                    {
                        var updatedLobby = JsonConvert.DeserializeObject<Lobby>(data);
                        if (updatedLobby != null)
                        {
                            OnLobbyUpdated?.Invoke(updatedLobby);
                        }
                        break;
                    }

                    case "queue_stats":
                    {
                        var stats = JsonConvert.DeserializeObject<QueueStats>(data);
                        if (stats != null)
                        {
                            OnQueueStats?.Invoke(stats);
                        }
                        break;
                    }

                    case "lobby_deleted":
                    {
                        var deletedData = JObject.Parse(data);
                        var lobbyId = deletedData["id"]?.ToString();
                        if (!string.IsNullOrEmpty(lobbyId))
                        {
                            OnLobbyDeleted?.Invoke(lobbyId);
                        }
                        break;
                    }

                    case "ping":
                        // V3 keepalive — ignore silently (arrival already reset reconnect state above)
                        break;

                    default:
                        if (_debugLogging)
                        {
                            Debug.Log($"[LobbySseManager] Unknown SSE event: {eventType}");
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                OnError?.Invoke($"Failed to parse SSE message ({eventType}): {e.Message}");
            }
        }

        /// <summary>
        /// Periodically attempts to reconnect to SSE when in polling-fallback mode.
        /// </summary>
        private IEnumerator PeriodicSSERetryCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(_periodicRetryInterval);

                if (!IsConnected && _hasConnectionParams && _hasReachedMaxAttempts && !_isPaused)
                {
                    if (_debugLogging)
                    {
                        Debug.Log("[LobbySseManager] Attempting periodic SSE reconnection...");
                    }

                    _reconnectAttempts = 0;
                    _reconnectDelay = 1f;
                    _hasReachedMaxAttempts = false;

                    if (_sseCoroutine == null)
                    {
                        _sseCoroutine = StartCoroutine(SSEConnectionCoroutine());
                    }
                }
            }
        }

        void OnDestroy()
        {
            Disconnect();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Custom download handler for SSE that processes the stream as it arrives.
        /// </summary>
        private class SSEDownloadHandler : DownloadHandlerScript
        {
            private readonly LobbySseManager _manager;
            private StringBuilder _buffer = new StringBuilder();
            private string _currentEventType = "";
            private StringBuilder _currentData = new StringBuilder();

            public SSEDownloadHandler(LobbySseManager manager) : base()
            {
                _manager = manager;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength == 0)
                    return true;

                string text = Encoding.UTF8.GetString(data, 0, dataLength);
                _buffer.Append(text);

                // Process complete lines
                string bufferContent = _buffer.ToString();
                string[] lines = bufferContent.Split('\n');

                // Keep the last incomplete line in the buffer
                _buffer.Clear();
                if (!bufferContent.EndsWith("\n"))
                {
                    _buffer.Append(lines[lines.Length - 1]);
                    lines = lines.Take(lines.Length - 1).ToArray();
                }

                foreach (string line in lines)
                {
                    ProcessLine(line.TrimEnd('\r'));
                }

                return true;
            }

            private void ProcessLine(string line)
            {
                if (string.IsNullOrEmpty(line))
                {
                    // Empty line signals end of event
                    if (!string.IsNullOrEmpty(_currentEventType))
                    {
                        _manager.HandleSSEMessage(_currentEventType, _currentData.ToString());
                        _currentEventType = "";
                        _currentData.Clear();
                    }
                    return;
                }

                if (line.StartsWith("event: "))
                {
                    _currentEventType = line.Substring(7);
                }
                else if (line.StartsWith("data: "))
                {
                    if (_currentData.Length > 0)
                        _currentData.AppendLine();
                    _currentData.Append(line.Substring(6));
                }
            }

            protected override void CompleteContent()
            {
                // Process any remaining data
                if (!string.IsNullOrEmpty(_currentEventType))
                {
                    _manager.HandleSSEMessage(_currentEventType, _currentData.ToString());
                }
            }
        }
    }
}
