using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Scopa2Game.Scripts.Models;
using Scopa2Game.Scripts.Models.Converters;

namespace Scopa2Game.Scripts;

public partial class NetworkManager : Node
{
    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public event Action<GameState>      StateUpdated;
    public event Action<RoundFinished>  RoundFinished;
    public event Action<GameFinished>   GameFinished;
    public event Action<string>         MatchFound;
    public event Action<ServerEndpoint> EndpointSelected;
    public event Action<ServerEndpoint> ServerSwitching;
    public event Action<ServerEndpoint> ServerSwitched;
    public event Action                 AllServersFailed;

    [Signal]
    public delegate void NetworkErrorEventHandler(string errorMessage);

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const int   FailureThreshold      = 3;
    private const float ReconnectBaseDelaySec = 2.0f;
    private const ulong FailoverCooldownMs    = 5_000;
    private const long  WsConnectTimeoutMs    = 10_000;
    private const float HttpRequestTimeoutSec = 5.0f;
    private const ulong HeartbeatTimeoutMs    = 15_000; // 3 missed heartbeats at 5s each

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private AuthManager    _authManager;
    private PusherClient   _pusherClient;
    private string         _gameId = "";

    private ServerEndpoint          _selectedEndpoint;
    private bool                    _endpointReady    = false;
    private List<ServerEndpoint>    _endpointPriority = new();
    private HashSet<ServerEndpoint> _failedEndpoints  = new();

    private int   _consecutiveFailures = 0;
    private bool  _isFailingOver       = false;
    private ulong _lastFailoverTime    = 0;
    private ulong _lastHeartbeatAt     = 0;

    private readonly List<string> _activeChannels = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new DoubleToIntConverter() }
    };

    private string PlayerSecret => _authManager?.PlayerSecret ?? "";

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override async void _Ready()
    {
        _authManager = GetNode<AuthManager>("/root/AuthManager");
        GD.Print($"[NET] AuthManager ready. Logged in: {_authManager.IsLoggedIn}");

        await SelectEndpoint();
        await ConnectWebSocket();
    }

    public override async void _Process(double delta)
    {
        if (!_endpointReady || _isFailingOver || _pusherClient?.SocketId == null || _lastHeartbeatAt == 0) return;

        if (Time.GetTicksMsec() - _lastHeartbeatAt > HeartbeatTimeoutMs)
        {
            GD.PrintErr($"[WS] Heartbeat timeout — no beat for {HeartbeatTimeoutMs / 1000}s. Triggering failover...");
            _lastHeartbeatAt = Time.GetTicksMsec(); // prevent re-entry before failover completes
            await TriggerFailover();
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public ServerEndpoint GetSelectedEndpoint() => _selectedEndpoint;
    public bool IsEndpointReady() => _endpointReady;

    /// <summary>Resets all failed endpoints. Call after user manually retries.</summary>
    public void ResetFailedEndpoints()
    {
        GD.Print($"[NET] Resetting {_failedEndpoints.Count} failed endpoints.");
        _failedEndpoints.Clear();
        _consecutiveFailures = 0;
        _isFailingOver = false;
    }

    public async void SubscribeToGameChannel()
    {
        if (_pusherClient == null || string.IsNullOrEmpty(_gameId)) return;

        await AuthenticateAndSubscribe($"private-game_{_gameId}_player_{_authManager.UserId}");
        await AuthenticateAndSubscribe($"private-game_{_gameId}");
    }

    public async void SubscribeToMatchmakingChannel()
    {
        if (_pusherClient == null || string.IsNullOrEmpty(PlayerSecret)) return;

        await AuthenticateAndSubscribe($"private-{PlayerSecret}_matchmaking_result");
    }

    public async void StartGame()
    {
        GD.Print("[NET] Starting new game...");
        var data = await SendApiRequest<JsonElement>("/games", HttpClient.Method.Post);

        if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("game_id", out var gameIdProp))
        {
            _gameId = gameIdProp.GetString();
            GD.Print($"[NET] Game created: {_gameId}");
            SubscribeToGameChannel();
            await FetchGameState();
        }
        else
        {
            EmitSignal(SignalName.NetworkError, "Failed to start game. Please try again.");
        }
    }

    public async void ConnectToMatch(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        _gameId = gameId;
        GD.Print($"[NET] Connecting to match {_gameId}");
        SubscribeToGameChannel();
        await FetchGameState();
    }

    public async void JoinGame(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;

        _gameId = gameId;
        GD.Print($"[NET] Joining game {_gameId}");

        var data = await SendApiRequest<JsonElement>($"/games/{_gameId}/join", HttpClient.Method.Post);

        if (data.ValueKind != JsonValueKind.Undefined)
        {
            SubscribeToGameChannel();
            await FetchGameState();
        }
        else
        {
            EmitSignal(SignalName.NetworkError, $"Failed to join game {_gameId}. Please check the ID and try again.");
        }
    }

    public async void SendAction(string action)
    {
        if (string.IsNullOrEmpty(_gameId)) return;
        GD.Print($"[NET] Sending action: {action}");
        await SendApiRequest<JsonElement>($"/games/{_gameId}/action", HttpClient.Method.Post, new { action });
    }

    public async Task FetchGameState()
    {
        if (string.IsNullOrEmpty(_gameId)) return;

        var data = await SendApiRequest<JsonElement>($"/games/{_gameId}", HttpClient.Method.Get);

        if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("state", out var stateProp))
        {
            try
            {
                StateUpdated?.Invoke(stateProp.Deserialize<GameState>(_jsonOptions));
            }
            catch (JsonException ex)
            {
                GD.PrintErr($"[NET] Failed to deserialize game state: {ex.Message}");
                EmitSignal(SignalName.NetworkError, "Failed to parse game state.");
            }
        }
        else
        {
            EmitSignal(SignalName.NetworkError, "Failed to fetch game state. Please try again.");
        }
    }

    // -------------------------------------------------------------------------
    // HTTP (public so other managers can route through the failover system)
    // -------------------------------------------------------------------------

    public async Task<T> SendApiRequest<T>(string endpoint, HttpClient.Method method, object body = null, bool isRetry = false)
    {
        string[] headers =
        {
            "Accept: application/json",
            "Content-Type: application/json",
            $"player_secret: {PlayerSecret}",
            $"Authorization: Bearer {_authManager?.Token ?? ""}"
        };

        string jsonBody = body != null ? JsonSerializer.Serialize(body, _jsonOptions) : "";
        string url = _selectedEndpoint.BaseUrl + endpoint;

        for (int attempt = 1; attempt <= FailureThreshold; attempt++)
        {
            var req = new HttpRequest();
            req.Timeout = HttpRequestTimeoutSec;
            AddChild(req);

            GD.Print($"[HTTP] {method} {url} (attempt {attempt}/{FailureThreshold})");
            req.Request(url, headers, method, jsonBody);

            var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
            req.QueueFree();

            long httpResult    = result[0].AsInt64();
            long responseCode  = result[1].AsInt64();
            byte[] responseBody = result[3].AsByteArray();

            if (IsServerError(httpResult, responseCode))
            {
                GD.PrintErr($"[HTTP] Server error at {endpoint} — httpResult={httpResult}, code={responseCode}. " +
                            $"Attempt {attempt}/{FailureThreshold}");

                if (attempt < FailureThreshold)
                {
                    float delay = ReconnectBaseDelaySec * attempt;
                    GD.Print($"[HTTP] Retrying in {delay}s...");
                    await Task.Delay((int)(delay * 1000));
                    continue;
                }

                // All retries exhausted — trigger failover
                if (!_isFailingOver && !isRetry)
                {
                    GD.Print($"[FAILOVER] All HTTP retries exhausted for {endpoint}. Triggering failover...");
                    bool switched = await TriggerFailover();
                    if (switched)
                    {
                        GD.Print($"[HTTP] Retrying {endpoint} on {_selectedEndpoint.Region}");
                        return await SendApiRequest<T>(endpoint, method, body, isRetry: true);
                    }
                }

                return default;
            }

            if (responseCode >= 400 && responseCode < 500)
            {
                GD.PrintErr($"[HTTP] Client error {responseCode} at {endpoint}: {Encoding.UTF8.GetString(responseBody)}");
                return default;
            }

            if (responseCode >= 200 && responseCode < 300)
            {
                GD.Print($"[HTTP] {responseCode} OK — {endpoint}");
                try
                {
                    return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(responseBody), _jsonOptions);
                }
                catch (JsonException ex)
                {
                    GD.PrintErr($"[HTTP] JSON parse error: {ex.Message}");
                    return default;
                }
            }

            GD.PrintErr($"[HTTP] Unexpected response code {responseCode} at {endpoint}");
            return default;
        }

        return default;
    }

    private static bool IsServerError(long httpResult, long responseCode) =>
        httpResult != 0 || (responseCode >= 500 && responseCode < 600);

    // -------------------------------------------------------------------------
    // Endpoint selection
    // -------------------------------------------------------------------------

    private async Task SelectEndpoint()
    {
        GD.Print("[NET] Selecting best endpoint...");

        var selector = new EndpointSelector();
        AddChild(selector);

        var allMetrics = await selector.GetEndpointMetrics(Constants.Endpoints);
        _endpointPriority = allMetrics
            .Where(m => m.IsReachable)
            .OrderBy(m => m.Score)
            .Select(m => m.Endpoint)
            .ToList();

        if (_endpointPriority.Count == 0)
        {
            GD.PrintErr("[NET] No reachable endpoints — using fallback list.");
            _endpointPriority = Constants.Endpoints.ToList();
        }

        selector.QueueFree();

        _selectedEndpoint = _endpointPriority.First();
        _endpointReady    = true;

        GD.Print($"[NET] Selected: {_selectedEndpoint.Region} ({_selectedEndpoint.BaseUrl})");
        GD.Print($"[NET] Priority list: {string.Join(" > ", _endpointPriority.Select(e => e.Region))}");
        EndpointSelected?.Invoke(_selectedEndpoint);
    }

    private ServerEndpoint GetNextAvailableEndpoint()
    {
        var available = _endpointPriority.Where(e => !_failedEndpoints.Contains(e)).ToList();

        if (available.Count == 0)
        {
            GD.PrintErr("[FAILOVER] No available endpoints remaining!");
            return null;
        }

        GD.Print($"[FAILOVER] Available endpoints: {string.Join(", ", available.Select(e => e.Region))}");
        return available.FirstOrDefault(e => e != _selectedEndpoint) ?? available.First();
    }

    private void MarkEndpointAsFailed(ServerEndpoint endpoint)
    {
        if (_failedEndpoints.Add(endpoint))
            GD.PrintErr($"[FAILOVER] Marked {endpoint.Region} as FAILED. Total failed: {_failedEndpoints.Count}/{_endpointPriority.Count}");
    }

    // -------------------------------------------------------------------------
    // Failover
    // -------------------------------------------------------------------------

    private async Task<bool> TriggerFailover()
    {
        if (_isFailingOver)
        {
            GD.Print("[FAILOVER] Already in progress — skipping.");
            return false;
        }

        var now = Time.GetTicksMsec();
        if (now - _lastFailoverTime < FailoverCooldownMs)
        {
            ulong remaining = (FailoverCooldownMs - (now - _lastFailoverTime)) / 1000;
            GD.Print($"[FAILOVER] On cooldown — {remaining}s remaining.");
            return false;
        }

        _isFailingOver    = true;
        _lastFailoverTime = now;

        GD.Print($"[FAILOVER] ══════════════════════════════════════");
        GD.Print($"[FAILOVER] Starting failover from {_selectedEndpoint.Region}");
        GD.Print($"[FAILOVER] Active channels: [{string.Join(", ", _activeChannels)}]");

        MarkEndpointAsFailed(_selectedEndpoint);

        var next = GetNextAvailableEndpoint();
        if (next == null)
        {
            GD.PrintErr("[FAILOVER] ALL SERVERS FAILED.");
            AllServersFailed?.Invoke();
            _isFailingOver = false;
            return false;
        }

        GD.Print($"[FAILOVER] Switching: {_selectedEndpoint.Region} → {next.Region}");
        ServerSwitching?.Invoke(next);

        _selectedEndpoint    = next;
        _consecutiveFailures = 0;

        GD.Print($"[FAILOVER] Reconnecting WebSocket to {_selectedEndpoint.Region}...");
        bool connected = await ConnectWebSocket();

        _isFailingOver = false;

        if (connected)
        {
            GD.Print($"[FAILOVER] ✓ Complete — now using {_selectedEndpoint.Region}");
            GD.Print($"[FAILOVER] ══════════════════════════════════════");
            ServerSwitched?.Invoke(_selectedEndpoint);

            if (!string.IsNullOrEmpty(_gameId))
            {
                GD.Print($"[FAILOVER] Resyncing game state for {_gameId}...");
                await FetchGameState();
            }

            return true;
        }

        GD.PrintErr($"[FAILOVER] Failed to connect to {_selectedEndpoint.Region} — trying next server...");
        GD.Print($"[FAILOVER] ══════════════════════════════════════");
        return await TriggerFailover();
    }

    // -------------------------------------------------------------------------
    // WebSocket
    // -------------------------------------------------------------------------

    private void InitializePusherClient()
    {
        _pusherClient = new PusherClient();
        AddChild(_pusherClient);
        _pusherClient.EventReceived += OnPusherEventReceived;
        _pusherClient.Disconnected  += OnWebSocketDisconnected;
        _pusherClient.Connected     += OnWebSocketConnected;
        _pusherClient.ConnectToServer(_selectedEndpoint.WsUrl);
    }

    private async Task<bool> ConnectWebSocket()
    {
        GD.Print($"[WS] Connecting to {_selectedEndpoint.Region} ({_selectedEndpoint.WsUrl})...");

        _pusherClient?.QueueFree();
        InitializePusherClient();

        var start = Time.GetTicksMsec();
        while (_pusherClient.SocketId == null && (Time.GetTicksMsec() - start) < WsConnectTimeoutMs)
            await Task.Delay(100);

        if (_pusherClient.SocketId == null)
        {
            GD.PrintErr($"[WS] Connection timed out after {WsConnectTimeoutMs / 1000}s.");
            return false;
        }

        GD.Print($"[WS] Connected. Socket ID: {_pusherClient.SocketId}");

        // Public channel — no auth needed
        _pusherClient.Subscribe("heartbeat", "");
        GD.Print("[WS] Subscribed to heartbeat channel.");

        if (_activeChannels.Count > 0)
        {
            GD.Print($"[WS] Resubscribing {_activeChannels.Count} channels: [{string.Join(", ", _activeChannels)}]");
            var channels = new List<string>(_activeChannels);
            _activeChannels.Clear();
            foreach (var ch in channels)
                await AuthenticateAndSubscribe(ch);
            GD.Print($"[WS] Resubscription done. Active: [{string.Join(", ", _activeChannels)}]");
        }

        return true;
    }

    private void OnWebSocketConnected()
    {
        GD.Print($"[WS] Connected to {_selectedEndpoint.Region}.");
        _consecutiveFailures = 0;
        _lastHeartbeatAt     = Time.GetTicksMsec(); // seed timer so _Process doesn't fire before first beat
    }

    private async void OnWebSocketDisconnected()
    {
        if (_isFailingOver) return;

        _consecutiveFailures++;
        GD.PrintErr($"[WS] Disconnected from {_selectedEndpoint.Region}. Failure {_consecutiveFailures}/{FailureThreshold}");

        if (_consecutiveFailures >= FailureThreshold)
        {
            GD.Print("[FAILOVER] Threshold reached on WS disconnect. Triggering failover...");
            await TriggerFailover();
            return;
        }

        float delay = ReconnectBaseDelaySec * _consecutiveFailures;
        GD.Print($"[WS] Retrying in {delay}s...");
        await Task.Delay((int)(delay * 1000));
        await ConnectWebSocket();
    }

    // -------------------------------------------------------------------------
    // Channel authentication
    // -------------------------------------------------------------------------

    private async Task AuthenticateAndSubscribe(string channelName, bool isRetry = false)
    {
        if (_pusherClient?.SocketId == null)
        {
            GD.PrintErr($"[AUTH] Cannot subscribe to {channelName} — socket_id is null.");
            return;
        }

        // Pre-register channel so failover can resubscribe it even if this auth fails
        if (!_activeChannels.Contains(channelName))
            _activeChannels.Add(channelName);

        GD.Print($"[AUTH] Authenticating {channelName} on {_selectedEndpoint.Region}...");

        string[] headers =
        {
            "Accept: application/json",
            "Content-Type: application/x-www-form-urlencoded",
            $"Authorization: Bearer {_authManager?.Token ?? ""}"
        };

        string authUrl = _selectedEndpoint.BaseUrl.Replace("/api", "") + "/broadcasting/auth";
        string body    = $"socket_id={_pusherClient.SocketId}&channel_name={channelName}";

        for (int attempt = 1; attempt <= FailureThreshold; attempt++)
        {
            var req = new HttpRequest();
            req.Timeout = HttpRequestTimeoutSec;
            AddChild(req);

            GD.Print($"[AUTH] Attempt {attempt}/{FailureThreshold} for {channelName}");
            req.Request(authUrl, headers, HttpClient.Method.Post, body);
            var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
            req.QueueFree();

            long   httpResult   = result[0].AsInt64();
            long   responseCode = result[1].AsInt64();
            byte[] responseBody = result[3].AsByteArray();

            // Server error (network failure or 5xx)
            if (httpResult != 0 || (responseCode >= 500 && responseCode < 600))
            {
                GD.PrintErr($"[AUTH] Server error for {channelName} — httpResult={httpResult}, code={responseCode}. " +
                            $"Attempt {attempt}/{FailureThreshold}");

                if (attempt < FailureThreshold)
                {
                    float delay = ReconnectBaseDelaySec * attempt;
                    GD.Print($"[AUTH] Retrying in {delay}s...");
                    await Task.Delay((int)(delay * 1000));
                    continue;
                }

                // All retries exhausted — trigger failover
                // Channel stays in _activeChannels so ConnectWebSocket resubscribes it
                if (!_isFailingOver && !isRetry)
                {
                    GD.Print("[FAILOVER] All AUTH retries exhausted. Triggering failover...");
                    await TriggerFailover();
                }
                return;
            }

            // Client error (4xx) — don't retry; remove from active channels
            if (responseCode >= 400 && responseCode < 500)
            {
                GD.PrintErr($"[AUTH] Client error {responseCode} for {channelName} — removing from active channels.");
                _activeChannels.Remove(channelName);
                return;
            }

            // Success
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    Encoding.UTF8.GetString(responseBody), _jsonOptions);

                if (parsed != null && parsed.TryGetValue("auth", out var authHashEl))
                {
                    string authHash = authHashEl.GetString();
                    _pusherClient.Subscribe(channelName, authHash);
                    GD.Print($"[AUTH] ✓ Subscribed to {channelName}");
                }
                else
                {
                    GD.PrintErr($"[AUTH] Invalid auth response for {channelName}: {Encoding.UTF8.GetString(responseBody)}");
                    _activeChannels.Remove(channelName);
                }
            }
            catch (JsonException ex)
            {
                GD.PrintErr($"[AUTH] JSON parse error for {channelName}: {ex.Message}");
                _activeChannels.Remove(channelName);
            }
            return; // success or unrecoverable parse error — exit loop
        }
    }

    // -------------------------------------------------------------------------
    // WebSocket event handling
    // -------------------------------------------------------------------------

    private void OnPusherEventReceived(string eventName, Variant data)
    {
        if (eventName == "heartbeat")
        {
            GD.Print($"[WS] Received heartbeat from {_selectedEndpoint.Region}");
            _lastHeartbeatAt = Time.GetTicksMsec();
            return;
        }

        GD.Print($"[WS] Event: {eventName}");

        try
        {
            string json = Godot.Json.Stringify(data);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (eventName)
            {
                case "game_state_updated":
                    if (root.TryGetProperty("state", out var stateProp))
                        StateUpdated?.Invoke(stateProp.Deserialize<GameState>(_jsonOptions));
                    break;

                case "round_finished":
                    if (root.TryGetProperty("results", out var roundProp))
                        RoundFinished?.Invoke(roundProp.Deserialize<RoundFinished>(_jsonOptions));
                    break;

                case "game_finished":
                    if (root.TryGetProperty("results", out var gameProp))
                        GameFinished?.Invoke(gameProp.Deserialize<GameFinished>(_jsonOptions));
                    break;

                case "match_found":
                    if (root.TryGetProperty("game_id", out var gameIdProp))
                        MatchFound?.Invoke(gameIdProp.GetString());
                    break;
            }
        }
        catch (JsonException ex)
        {
            GD.PrintErr($"[WS] Failed to parse event '{eventName}': {ex.Message}");
        }
    }
}
