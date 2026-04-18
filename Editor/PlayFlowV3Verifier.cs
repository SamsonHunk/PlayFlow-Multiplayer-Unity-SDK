// V3 lobby API verification for PlayFlow Unity SDK.
// Run via Tools -> PlayFlow -> Run V3 Verification.
//
// This script is intentionally self-contained: it does NOT depend on the SDK's
// PlayFlowLobbyManagerV2 / coroutine layer. It calls the V3 HTTP API directly
// using UnityWebRequest + Newtonsoft.Json so the test exercises the real wire
// protocol end-to-end.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PlayFlow.Editor
{
    // -----------------------------------------------------------------------------
    //  JSON shapes (standalone, do NOT import SDK's Lobby class)
    // -----------------------------------------------------------------------------
    [Serializable]
    internal class TestLobby
    {
        public string id;
        public string code;
        public string config;
        public string name;
        public string status;
        public string host;
        public int maxPlayers;
        public int currentPlayers;
        public List<TestPlayer> players;
        public TestServer server;
        public TestMatchmaking matchmaking;
        public string matchId;
    }

    [Serializable]
    internal class TestPlayer
    {
        public string id;
        public Dictionary<string, object> state;
        public bool isHost;
    }

    [Serializable]
    internal class TestServer
    {
        public string instanceId;
        public string status;
        public List<TestPort> ports;
    }

    [Serializable]
    internal class TestPort
    {
        public string name;
        public string host;
        public int port;
        public string protocol;
    }

    [Serializable]
    internal class TestMatchmaking
    {
        public string mode;
        public string startedAt;
        public TestQueueStats queueStats;
    }

    [Serializable]
    internal class TestQueueStats
    {
        public int playersSearching;
        public int lobbiesInQueue;
        public float avgWaitSeconds;
    }

    [Serializable]
    internal class TestBrowseResponse
    {
        public List<TestLobby> lobbies;
        public int total;
        public int limit;
        public int offset;
        public bool hasMore;
    }

    [Serializable]
    internal class TestError
    {
        public string error;
        public string detail;
    }

    // -----------------------------------------------------------------------------
    //  HTTP result
    // -----------------------------------------------------------------------------
    internal class HttpResult
    {
        public int StatusCode;
        public string Body;
        public string Error;
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    }

    // -----------------------------------------------------------------------------
    //  SSE listener (background thread)
    // -----------------------------------------------------------------------------
    internal class SseEvent
    {
        public string Event;
        public string Data;
    }

    internal class SseListener
    {
        private readonly string _url;
        private readonly string _apiKey;
        private readonly string _playerId;
        private readonly Thread _thread;
        private readonly object _lock = new object();
        private readonly Queue<SseEvent> _events = new Queue<SseEvent>();
        private HttpWebRequest _request;
        private volatile bool _stop;
        public string LastError;

        public SseListener(string url, string apiKey, string playerId)
        {
            _url = url;
            _apiKey = apiKey;
            _playerId = playerId;
            _thread = new Thread(Run) { IsBackground = true, Name = "PlayFlowV3Sse" };
        }

        public void Start()
        {
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            try { _request?.Abort(); } catch { }
            try { if (_thread.IsAlive) _thread.Join(500); } catch { }
        }

        public bool TryDequeue(out SseEvent evt)
        {
            lock (_lock)
            {
                if (_events.Count == 0)
                {
                    evt = null;
                    return false;
                }
                evt = _events.Dequeue();
                return true;
            }
        }

        private void Run()
        {
            try
            {
                _request = (HttpWebRequest)WebRequest.Create(_url);
                _request.Method = "GET";
                _request.Headers["api-key"] = _apiKey;
                if (!string.IsNullOrEmpty(_playerId))
                {
                    _request.Headers["x-player-id"] = _playerId;
                }
                _request.Accept = "text/event-stream";
                _request.Timeout = 30000;
                _request.ReadWriteTimeout = 30000;
                _request.KeepAlive = true;
                _request.AllowAutoRedirect = true;

                using (var response = (HttpWebResponse)_request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string currentEvent = null;
                    var dataBuilder = new StringBuilder();

                    while (!_stop)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            // Stream closed.
                            break;
                        }

                        if (line.Length == 0)
                        {
                            // Dispatch.
                            if (dataBuilder.Length > 0 || !string.IsNullOrEmpty(currentEvent))
                            {
                                var evt = new SseEvent
                                {
                                    Event = currentEvent ?? "message",
                                    Data = dataBuilder.ToString(),
                                };
                                lock (_lock) { _events.Enqueue(evt); }
                            }
                            currentEvent = null;
                            dataBuilder.Length = 0;
                            continue;
                        }

                        if (line.StartsWith(":"))
                        {
                            // Comment / keep-alive, ignore.
                            continue;
                        }

                        if (line.StartsWith("event:"))
                        {
                            currentEvent = line.Substring(6).Trim();
                        }
                        else if (line.StartsWith("data:"))
                        {
                            if (dataBuilder.Length > 0) dataBuilder.Append('\n');
                            dataBuilder.Append(line.Substring(5).TrimStart());
                        }
                        // id:/retry: lines ignored.
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stop)
                {
                    LastError = ex.Message;
                }
            }
        }
    }

    // -----------------------------------------------------------------------------
    //  Editor window
    // -----------------------------------------------------------------------------
    public class PlayFlowV3Verifier : EditorWindow
    {
        // Persisted inputs.
        private string _apiKey;
        private string _baseUrl = "https://api.computeflow.cloud";
        private string _config = "default";

        // UI state.
        private Vector2 _scroll;
        private readonly StringBuilder _log = new StringBuilder();
        private bool _running;

        // Test state (driven by EditorApplication.update).
        private IEnumerator<object> _runner;
        private UnityWebRequest _activeRequest;
        private UnityWebRequestAsyncOperation _activeOp;
        private HttpResult _lastResult;
        private SseListener _sse;
        private double _waitUntil;

        // Bookkeeping across tests.
        private string _p1;
        private string _p2;
        private string _p3;
        private TestLobby _lobby;
        private int _pass;
        private int _fail;
        private int _skip;
        private readonly List<string> _failures = new List<string>();
        private bool _skipMatchmaking;
        private string _chosenMode;

        private const string PrefKeyApi = "PlayFlowV3Verifier.apiKey";
        private const string PrefKeyBase = "PlayFlowV3Verifier.baseUrl";
        private const string PrefKeyConfig = "PlayFlowV3Verifier.config";

        [MenuItem("Tools/PlayFlow/Run V3 Verification")]
        public static void ShowWindow()
        {
            var w = GetWindow<PlayFlowV3Verifier>("PlayFlow V3 Verifier");
            w.minSize = new Vector2(640, 480);
            w.Show();
        }

        private void OnEnable()
        {
            _apiKey = EditorPrefs.GetString(PrefKeyApi, "");
            _baseUrl = EditorPrefs.GetString(PrefKeyBase, "https://api.computeflow.cloud");
            _config = EditorPrefs.GetString(PrefKeyConfig, "default");
        }

        private void OnDisable()
        {
            StopRunner();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PlayFlow V3 API Verifier", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Verifies all 13 V3 lobby/matchmaking endpoints + SSE by driving the API directly via UnityWebRequest. " +
                "Use a client API key (pfclient_*). No admin privileges required.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_running))
            {
                _apiKey = EditorGUILayout.TextField("API Key", _apiKey);
                _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
                _config = EditorGUILayout.TextField("Lobby Config", _config);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_running || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_config)))
                {
                    if (GUILayout.Button(_running ? "Running..." : "Run All Tests", GUILayout.Height(28)))
                    {
                        PersistInputs();
                        StartRunner();
                    }
                }

                using (new EditorGUI.DisabledScope(_log.Length == 0))
                {
                    if (GUILayout.Button("Clear Log", GUILayout.Height(28), GUILayout.Width(100)))
                    {
                        _log.Length = 0;
                        Repaint();
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void PersistInputs()
        {
            EditorPrefs.SetString(PrefKeyApi, _apiKey);
            EditorPrefs.SetString(PrefKeyBase, _baseUrl);
            EditorPrefs.SetString(PrefKeyConfig, _config);
        }

        // ---------------------------------------------------------------------
        //  Logging
        // ---------------------------------------------------------------------
        private void Log(string message)
        {
            _log.AppendLine(message);
            Debug.Log("[PlayFlowV3] " + message);
            Repaint();
        }

        private void Pass(string test, string detail = null)
        {
            _pass++;
            Log($"[PASS] {test}" + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
        }

        private void Fail(string test, string detail)
        {
            _fail++;
            var msg = $"{test} — {detail}";
            _failures.Add(msg);
            Log($"[FAIL] {msg}");
        }

        private void Skip(string test, string detail)
        {
            _skip++;
            Log($"[SKIP] {test} — {detail}");
        }

        // ---------------------------------------------------------------------
        //  Runner (state-machine via IEnumerator driven by EditorApplication.update)
        // ---------------------------------------------------------------------
        private void StartRunner()
        {
            _log.Length = 0;
            _pass = 0;
            _fail = 0;
            _skip = 0;
            _failures.Clear();
            _lobby = null;
            _skipMatchmaking = false;
            _chosenMode = null;

            var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
            _p1 = $"test_p1_{guid}";
            _p2 = $"test_p2_{guid}";
            _p3 = $"test_p3_{guid}";

            Log("==============================================");
            Log("PlayFlow V3 Verification starting");
            Log($"  Base URL: {_baseUrl}");
            Log($"  Config:   {_config}");
            Log($"  p1 (host): {_p1}");
            Log($"  p2:        {_p2}");
            Log($"  p3:        {_p3}");
            Log("==============================================");

            _running = true;
            _runner = RunAll().GetEnumerator();
            EditorApplication.update += Tick;
        }

        private void StopRunner()
        {
            if (_sse != null)
            {
                _sse.Stop();
                _sse = null;
            }
            if (_activeRequest != null)
            {
                try { _activeRequest.Abort(); } catch { }
                try { _activeRequest.Dispose(); } catch { }
                _activeRequest = null;
                _activeOp = null;
            }
            if (_running)
            {
                EditorApplication.update -= Tick;
                _running = false;
            }
            _runner = null;
        }

        private void Tick()
        {
            try
            {
                if (_runner == null)
                {
                    StopRunner();
                    return;
                }
                if (!_runner.MoveNext())
                {
                    StopRunner();
                    return;
                }
                // The yielded value isn't used — the IEnumerator is just a state machine
                // that advances one step per Editor frame. Nested "waits" poll their own
                // completion and yield null until satisfied.
            }
            catch (Exception ex)
            {
                Fail("Runner", $"Exception: {ex.Message}");
                Log(ex.ToString());
                FinishSummary();
                StopRunner();
            }
        }

        // ---------------------------------------------------------------------
        //  HTTP helpers (non-blocking — yields until the UnityWebRequest completes)
        // ---------------------------------------------------------------------
        private string BuildUrl(string path)
        {
            var cfg = Uri.EscapeDataString(_config ?? string.Empty);
            var baseUrl = (_baseUrl ?? "").TrimEnd('/');
            var suffix = string.IsNullOrEmpty(path) ? "" : (path.StartsWith("/") ? path : "/" + path);
            return $"{baseUrl}/api/v3/lobbies/{cfg}{suffix}";
        }

        private IEnumerable<object> Request(string method, string path, string playerId, object body, Action<HttpResult> onDone)
        {
            var url = BuildUrl(path);
            var req = new UnityWebRequest(url, method);
            req.SetRequestHeader("api-key", _apiKey);
            if (!string.IsNullOrEmpty(playerId))
            {
                req.SetRequestHeader("x-player-id", playerId);
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                var bytes = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.SetRequestHeader("Content-Type", "application/json");
            }

            _activeRequest = req;
            _activeOp = req.SendWebRequest();

            while (_activeOp != null && !_activeOp.isDone)
            {
                yield return null;
            }

            var result = new HttpResult
            {
                StatusCode = (int)req.responseCode,
                Body = req.downloadHandler != null ? req.downloadHandler.text : null,
#if UNITY_2020_2_OR_NEWER
                Error = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError
                    ? req.error
                    : null,
#else
                Error = req.isNetworkError ? req.error : null,
#endif
            };

            try { req.Dispose(); } catch { }
            _activeRequest = null;
            _activeOp = null;
            _lastResult = result;
            onDone?.Invoke(result);
        }

        private IEnumerable<object> Wait(double seconds)
        {
            _waitUntil = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < _waitUntil)
            {
                yield return null;
            }
        }

        private static TestLobby ParseLobby(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JsonConvert.DeserializeObject<TestLobby>(body); }
            catch { return null; }
        }

        private static TestError ParseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JsonConvert.DeserializeObject<TestError>(body); }
            catch { return null; }
        }

        private static string Shorten(string s, int max = 240)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        // ---------------------------------------------------------------------
        //  Tests — one IEnumerable<object> orchestrator drives all 15 tests.
        // ---------------------------------------------------------------------
        private IEnumerable<object> RunAll()
        {
            foreach (var s in Test1_Create()) yield return s;
            foreach (var s in Test2_GetMe()) yield return s;
            foreach (var s in Test3_Browse()) yield return s;
            foreach (var s in Test4_UpdatePlayer()) yield return s;
            foreach (var s in Test5_UpdateSettings()) yield return s;
            foreach (var s in Test6_Join()) yield return s;
            foreach (var s in Test7_JoinByCode()) yield return s;
            foreach (var s in Test8_Kick()) yield return s;
            foreach (var s in Test9_SseConnected()) yield return s;
            foreach (var s in Test10_SseLiveUpdate()) yield return s;
            foreach (var s in Test11_StartMatchmaking()) yield return s;
            foreach (var s in Test12_QueueStats()) yield return s;
            foreach (var s in Test13_CancelMatchmaking()) yield return s;
            foreach (var s in Test14_LeaveP2()) yield return s;
            foreach (var s in Test15_LeaveHost()) yield return s;
            FinishSummary();
        }

        private IEnumerable<object> Test1_Create()
        {
            Log("");
            Log(">>> Test 1: Create lobby");
            var body = new
            {
                name = "v3-test-lobby",
                maxPlayers = 4,
                isPrivate = false,
                state = new Dictionary<string, object> { { "mmr", 1500 } },
            };
            HttpResult r = null;
            foreach (var s in Request("POST", "", _p1, body, x => r = x)) yield return s;

            if (r == null || !r.IsSuccess)
            {
                Fail("Test 1 (create)", $"status={r?.StatusCode} body={Shorten(r?.Body)}");
                yield break;
            }
            var lobby = ParseLobby(r.Body);
            if (lobby == null) { Fail("Test 1 (create)", "response did not parse as Lobby"); yield break; }

            bool ok = true;
            string why = null;
            if (string.IsNullOrEmpty(lobby.id)) { ok = false; why = "id empty"; }
            else if (string.IsNullOrEmpty(lobby.code)) { ok = false; why = "code empty"; }
            else if (lobby.host != _p1) { ok = false; why = $"host={lobby.host} expected={_p1}"; }
            else if (lobby.status != "waiting") { ok = false; why = $"status={lobby.status}"; }
            else if (lobby.players == null || lobby.players.Count != 1) { ok = false; why = $"players.Count={lobby.players?.Count}"; }
            else if (lobby.players[0].id != _p1) { ok = false; why = $"players[0].id={lobby.players[0].id}"; }
            else if (!lobby.players[0].isHost) { ok = false; why = "players[0].isHost=false"; }
            else
            {
                var state = lobby.players[0].state;
                if (state == null || !state.ContainsKey("mmr")) { ok = false; why = "state.mmr missing"; }
                else
                {
                    var mmr = Convert.ToInt32(state["mmr"]);
                    if (mmr != 1500) { ok = false; why = $"mmr={mmr}"; }
                }
            }

            if (!ok) { Fail("Test 1 (create)", why); yield break; }
            _lobby = lobby;
            Pass("Test 1: Create lobby", $"id={lobby.id} code={lobby.code}");
        }

        private IEnumerable<object> Test2_GetMe()
        {
            Log("");
            Log(">>> Test 2: Get my lobby");
            if (_lobby == null) { Skip("Test 2", "no lobby from Test 1"); yield break; }
            HttpResult r = null;
            foreach (var s in Request("GET", "/me", _p1, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 2 (get me)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.id != _lobby.id) { Fail("Test 2 (get me)", $"id mismatch got={lobby?.id} expected={_lobby.id}"); yield break; }
            Pass("Test 2: Get my lobby");
        }

        private IEnumerable<object> Test3_Browse()
        {
            Log("");
            Log(">>> Test 3: Browse public lobbies");
            if (_lobby == null) { Skip("Test 3", "no lobby from Test 1"); yield break; }
            HttpResult r = null;
            foreach (var s in Request("GET", "", _p1, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 3 (browse)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            TestBrowseResponse resp;
            try { resp = JsonConvert.DeserializeObject<TestBrowseResponse>(r.Body); }
            catch (Exception ex) { Fail("Test 3 (browse)", $"parse failed: {ex.Message}"); yield break; }
            if (resp?.lobbies == null) { Fail("Test 3 (browse)", "no lobbies array"); yield break; }
            bool found = resp.lobbies.Any(l => l.id == _lobby.id);
            if (!found) { Fail("Test 3 (browse)", $"our lobby {_lobby.id} not found in browse (total={resp.total})"); yield break; }
            Pass("Test 3: Browse", $"total={resp.total}, our lobby present");
        }

        private IEnumerable<object> Test4_UpdatePlayer()
        {
            Log("");
            Log(">>> Test 4: Update my player state");
            if (_lobby == null) { Skip("Test 4", "no lobby"); yield break; }
            var body = new
            {
                state = new Dictionary<string, object> { { "ready", true }, { "role", "medic" } },
            };
            HttpResult r = null;
            foreach (var s in Request("PATCH", "/me", _p1, body, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 4 (update player)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            var me = lobby?.players?.FirstOrDefault(p => p.id == _p1);
            if (me?.state == null) { Fail("Test 4 (update player)", "player state missing"); yield break; }

            if (!me.state.ContainsKey("ready") || !Convert.ToBoolean(me.state["ready"])) { Fail("Test 4", "ready not set"); yield break; }
            if (!me.state.ContainsKey("role") || me.state["role"]?.ToString() != "medic") { Fail("Test 4", "role not set"); yield break; }
            if (!me.state.ContainsKey("mmr") || Convert.ToInt32(me.state["mmr"]) != 1500) { Fail("Test 4", "mmr lost — state replaced instead of merged"); yield break; }

            Pass("Test 4: Update my player state (merge preserved mmr)");
        }

        private IEnumerable<object> Test5_UpdateSettings()
        {
            Log("");
            Log(">>> Test 5: Update lobby settings");
            if (_lobby == null) { Skip("Test 5", "no lobby"); yield break; }
            var body = new { name = "updated-lobby", maxPlayers = 6 };
            HttpResult r = null;
            foreach (var s in Request("PATCH", "/me/settings", _p1, body, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 5 (settings)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null) { Fail("Test 5", "parse failed"); yield break; }
            if (lobby.name != "updated-lobby") { Fail("Test 5", $"name={lobby.name}"); yield break; }
            if (lobby.maxPlayers != 6) { Fail("Test 5", $"maxPlayers={lobby.maxPlayers}"); yield break; }
            _lobby = lobby;
            Pass("Test 5: Update lobby settings");
        }

        private IEnumerable<object> Test6_Join()
        {
            Log("");
            Log(">>> Test 6: p2 joins by lobbyId");
            if (_lobby == null) { Skip("Test 6", "no lobby"); yield break; }
            var body = new { lobbyId = _lobby.id };
            HttpResult r = null;
            foreach (var s in Request("POST", "/join", _p2, body, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 6 (join)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.currentPlayers != 2 || lobby.players?.Count != 2)
            {
                Fail("Test 6 (join)", $"currentPlayers={lobby?.currentPlayers} count={lobby?.players?.Count}");
                yield break;
            }
            Pass("Test 6: p2 joined");
        }

        private IEnumerable<object> Test7_JoinByCode()
        {
            Log("");
            Log(">>> Test 7: p3 joins by code");
            if (_lobby == null) { Skip("Test 7", "no lobby"); yield break; }
            var body = new { code = _lobby.code };
            HttpResult r = null;
            foreach (var s in Request("POST", "/join", _p3, body, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 7 (join by code)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.currentPlayers != 3)
            {
                Fail("Test 7 (join by code)", $"currentPlayers={lobby?.currentPlayers}");
                yield break;
            }
            Pass("Test 7: p3 joined by code");
        }

        private IEnumerable<object> Test8_Kick()
        {
            Log("");
            Log(">>> Test 8: Host kicks p3");
            if (_lobby == null) { Skip("Test 8", "no lobby"); yield break; }
            HttpResult r = null;
            foreach (var s in Request("DELETE", $"/me/players/{Uri.EscapeDataString(_p3)}", _p1, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 8 (kick)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.currentPlayers != 2 || lobby.players.Any(p => p.id == _p3))
            {
                Fail("Test 8 (kick)", $"p3 still present or bad count: {lobby?.currentPlayers}");
                yield break;
            }

            // Verify p3's /me now 404s.
            HttpResult r2 = null;
            foreach (var s in Request("GET", "/me", _p3, null, x => r2 = x)) yield return s;
            if (r2 == null || r2.StatusCode != 404)
            {
                Fail("Test 8 (kick)", $"p3 /me expected 404 got {r2?.StatusCode}");
                yield break;
            }
            Pass("Test 8: Kicked p3, p3 /me returns 404");
        }

        private IEnumerable<object> Test9_SseConnected()
        {
            Log("");
            Log(">>> Test 9: SSE connection (p1 connected event)");
            if (_lobby == null) { Skip("Test 9", "no lobby"); yield break; }
            _sse?.Stop();
            _sse = new SseListener(BuildUrl("/me/events"), _apiKey, _p1);
            _sse.Start();

            bool connected = false;
            bool recorded = false;
            var deadline = EditorApplication.timeSinceStartup + 5.0;
            while (EditorApplication.timeSinceStartup < deadline && !connected && !recorded)
            {
                yield return null;
                while (_sse.TryDequeue(out var evt))
                {
                    if (evt.Event == "connected")
                    {
                        var lobby = ParseLobby(evt.Data);
                        if (lobby != null && lobby.id == _lobby.id)
                        {
                            connected = true;
                            Pass("Test 9: SSE connected", "data parsed, id matches");
                        }
                        else
                        {
                            Fail("Test 9 (SSE)", $"connected event did not parse/match lobby id (data={Shorten(evt.Data)})");
                            recorded = true;
                        }
                        break;
                    }
                }
            }
            if (!connected && !recorded)
            {
                var err = _sse.LastError ?? "timed out waiting for connected event";
                Fail("Test 9 (SSE)", err);
            }
            // Keep SSE open for Test 10.
        }

        private IEnumerable<object> Test10_SseLiveUpdate()
        {
            Log("");
            Log(">>> Test 10: SSE live update (p2 state change observed by p1)");
            if (_sse == null || _lobby == null) { Skip("Test 10", "no SSE"); yield break; }

            // Trigger p2 state update.
            var body = new
            {
                state = new Dictionary<string, object> { { "team", "blue" } },
            };
            HttpResult r = null;
            foreach (var s in Request("PATCH", "/me", _p2, body, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 10 (trigger)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }

            // Now wait up to 3s for lobby_updated event on p1's SSE.
            bool got = false;
            var deadline = EditorApplication.timeSinceStartup + 3.0;
            while (EditorApplication.timeSinceStartup < deadline && !got)
            {
                yield return null;
                while (_sse.TryDequeue(out var evt))
                {
                    if (evt.Event == "lobby_updated")
                    {
                        var lobby = ParseLobby(evt.Data);
                        var p2 = lobby?.players?.FirstOrDefault(p => p.id == _p2);
                        if (p2?.state != null && p2.state.ContainsKey("team") && p2.state["team"]?.ToString() == "blue")
                        {
                            got = true;
                            Pass("Test 10: SSE lobby_updated received with p2.team=blue");
                            break;
                        }
                    }
                }
            }

            _sse.Stop();
            _sse = null;

            if (!got) { Fail("Test 10 (SSE)", "did not observe lobby_updated with p2.team=blue within 3s"); }
        }

        private IEnumerable<object> Test11_StartMatchmaking()
        {
            Log("");
            Log(">>> Test 11: Start matchmaking");
            if (_lobby == null) { Skip("Test 11", "no lobby"); _skipMatchmaking = true; yield break; }

            // Try with a dummy mode first to see the error shape. Real approach: just try "default"
            // and if it fails parse the error and skip gracefully.
            _chosenMode = "default";
            var body = new { mode = _chosenMode };
            HttpResult r = null;
            foreach (var s in Request("POST", "/me/matchmaking", _p1, body, x => r = x)) yield return s;

            if (r == null)
            {
                Fail("Test 11 (matchmaking)", "no response");
                _skipMatchmaking = true;
                yield break;
            }
            if (!r.IsSuccess)
            {
                var err = ParseError(r.Body);
                var msg = err?.error ?? err?.detail ?? r.Body ?? "";
                if (r.StatusCode == 400 || r.StatusCode == 404)
                {
                    if (msg.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("matchmaking", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Skip("Test 11-13 (matchmaking)", "No matchmaking modes configured on this config — skipping. Configure modes in dashboard to test matchmaking.");
                        // Count 12 and 13 as skipped too.
                        _skip += 2;
                        _skipMatchmaking = true;
                        yield break;
                    }
                }
                Fail("Test 11 (matchmaking)", $"status={r.StatusCode} body={Shorten(r.Body)}");
                _skipMatchmaking = true;
                yield break;
            }

            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.status != "in_queue")
            {
                Fail("Test 11 (matchmaking)", $"status={lobby?.status} expected=in_queue");
                _skipMatchmaking = true;
                yield break;
            }
            if (lobby.matchmaking == null || lobby.matchmaking.mode != _chosenMode)
            {
                Fail("Test 11 (matchmaking)", $"matchmaking.mode={lobby.matchmaking?.mode} expected={_chosenMode}");
                _skipMatchmaking = true;
                yield break;
            }
            _lobby = lobby;
            Pass("Test 11: Matchmaking started", $"mode={_chosenMode}");
        }

        private IEnumerable<object> Test12_QueueStats()
        {
            Log("");
            Log(">>> Test 12: SSE queue_stats event");
            if (_skipMatchmaking) { Log("  (skipped — Test 11 did not succeed)"); yield break; }

            _sse?.Stop();
            _sse = new SseListener(BuildUrl("/me/events"), _apiKey, _p1);
            _sse.Start();

            bool got = false;
            var deadline = EditorApplication.timeSinceStartup + 12.0;
            while (EditorApplication.timeSinceStartup < deadline && !got)
            {
                yield return null;
                while (_sse.TryDequeue(out var evt))
                {
                    if (evt.Event == "queue_stats")
                    {
                        TestQueueStats stats = null;
                        try { stats = JsonConvert.DeserializeObject<TestQueueStats>(evt.Data); }
                        catch { /* ignore */ }
                        if (stats != null && stats.playersSearching >= 0)
                        {
                            got = true;
                            Pass("Test 12: queue_stats received", $"playersSearching={stats.playersSearching}");
                            break;
                        }
                    }
                }
            }

            _sse.Stop();
            _sse = null;

            if (!got) { Fail("Test 12 (queue_stats)", "did not receive queue_stats within 12s"); }
        }

        private IEnumerable<object> Test13_CancelMatchmaking()
        {
            Log("");
            Log(">>> Test 13: Cancel matchmaking");
            if (_skipMatchmaking) { Log("  (skipped — Test 11 did not succeed)"); yield break; }

            HttpResult r = null;
            foreach (var s in Request("DELETE", "/me/matchmaking", _p1, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 13 (cancel)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }
            var lobby = ParseLobby(r.Body);
            if (lobby == null || lobby.status != "waiting")
            {
                Fail("Test 13 (cancel)", $"status={lobby?.status} expected=waiting");
                yield break;
            }
            _lobby = lobby;
            Pass("Test 13: Matchmaking cancelled");
        }

        private IEnumerable<object> Test14_LeaveP2()
        {
            Log("");
            Log(">>> Test 14: p2 leaves");
            if (_lobby == null) { Skip("Test 14", "no lobby"); yield break; }
            HttpResult r = null;
            foreach (var s in Request("DELETE", "/me", _p2, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 14 (leave p2)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }

            // Verify via host's GET /me.
            HttpResult r2 = null;
            foreach (var s in Request("GET", "/me", _p1, null, x => r2 = x)) yield return s;
            if (r2 == null || !r2.IsSuccess) { Fail("Test 14 (verify)", $"GET /me status={r2?.StatusCode}"); yield break; }
            var lobby = ParseLobby(r2.Body);
            if (lobby == null || lobby.currentPlayers != 1)
            {
                Fail("Test 14 (verify)", $"currentPlayers={lobby?.currentPlayers} expected=1");
                yield break;
            }
            Pass("Test 14: p2 left, lobby still alive with host");
        }

        private IEnumerable<object> Test15_LeaveHost()
        {
            Log("");
            Log(">>> Test 15: Host leaves (last player triggers cleanup)");
            if (_lobby == null) { Skip("Test 15", "no lobby"); yield break; }
            HttpResult r = null;
            foreach (var s in Request("DELETE", "/me", _p1, null, x => r = x)) yield return s;
            if (r == null || !r.IsSuccess) { Fail("Test 15 (leave host)", $"status={r?.StatusCode} body={Shorten(r?.Body)}"); yield break; }

            HttpResult r2 = null;
            foreach (var s in Request("GET", "/me", _p1, null, x => r2 = x)) yield return s;
            if (r2 == null || r2.StatusCode != 404)
            {
                Fail("Test 15 (verify cleanup)", $"GET /me expected 404 got {r2?.StatusCode}");
                yield break;
            }
            Pass("Test 15: Host left, lobby cleaned up (p1 /me returns 404)");
        }

        private void FinishSummary()
        {
            Log("");
            Log("==============================================");
            Log("PlayFlow V3 Verification Summary");
            Log("==============================================");
            Log($"Tests passed:  {_pass} / 15");
            Log($"Tests failed:  {_fail} / 15");
            Log($"Tests skipped: {_skip} / 15");
            if (_failures.Count > 0)
            {
                Log("");
                Log("Failures:");
                foreach (var f in _failures) Log("  " + f);
            }
            Log("==============================================");
        }
    }
}
#endif
