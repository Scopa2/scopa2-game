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
    // C# Events for complex types (Godot Signals don't support POCOs well)
    public event Action<GameState> StateUpdated;
    public event Action<RoundFinished> RoundFinished;
    public event Action<GameFinished> GameFinished;
    public event Action<string> MatchFound;
    public event Action<ServerEndpoint> EndpointSelected;
    public event Action<ServerEndpoint> ServerSwitching; // New server being switched to
    public event Action<ServerEndpoint> ServerSwitched;  // Switch completed
    public event Action AllServersFailed;

    [Signal]
    public delegate void NetworkErrorEventHandler(string errorMessage);

    private ServerEndpoint _selectedEndpoint;
    private bool _endpointReady = false;
    private bool _isFailingOver = false;
    private ulong _lastFailoverTime = 0;
    private const ulong FailoverCooldownMs = 5000; // 5 seconds
    
    // Failure detection & server priority
    private int _consecutiveFailures = 0;
    private const int FailureThreshold = 3;
    private readonly HashSet<ServerEndpoint> _failedEndpoints = new();
    private List<ServerEndpoint> _endpointPriority = new();
    
    // WebSocket channel tracking
    private readonly List<string> _activeChannels = new();

    private string _gameId = "";
    private PusherClient _pusherClient;
    private AuthManager _authManager;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new DoubleToIntConverter() }
    };

    /// <summary>Convenience accessor for the player secret (user ID from AuthManager).</summary>
    private string PlayerSecret => _authManager?.PlayerSecret ?? "";

    public override async void _Ready()
    {
        _authManager = GetNode<AuthManager>("/root/AuthManager");
        GD.Print($"NetworkManager: Using AuthManager, logged in: {_authManager.IsLoggedIn}");

        await SelectEndpoint();
        InitializeWebSocket();
    }

    private async Task SelectEndpoint()
    {
        GD.Print("NetworkManager: Selecting best endpoint...");
        
        var selector = new EndpointSelector();
        AddChild(selector);
        
        // Get all metrics and build priority list
        var allMetrics = await selector.GetEndpointMetrics(Constants.Endpoints);
        _endpointPriority = allMetrics
            .Where(m => m.IsReachable)
            .OrderBy(m => m.Score)
            .Select(m => m.Endpoint)
            .ToList();
        
        if (_endpointPriority.Count == 0)
        {
            GD.PrintErr("NetworkManager: No reachable endpoints! Using fallback.");
            _endpointPriority = Constants.Endpoints.ToList();
        }
        
        _selectedEndpoint = _endpointPriority.First();
        
        selector.QueueFree();
        _endpointReady = true;
        
        GD.Print($"NetworkManager: Using endpoint {_selectedEndpoint.Region} at {_selectedEndpoint.BaseUrl}");
        GD.Print($"NetworkManager: Priority list: {string.Join(", ", _endpointPriority.Select(e => e.Region))}");
        EndpointSelected?.Invoke(_selectedEndpoint);
    }

    public ServerEndpoint GetSelectedEndpoint() => _selectedEndpoint;
    
    public bool IsEndpointReady() => _endpointReady;

    /// <summary>
    /// Resets all failed endpoints and re-enables them for connection attempts.
    /// Used when user manually retries after all servers fail.
    /// </summary>
    public void ResetFailedEndpoints()
    {
        GD.Print($"NetworkManager: Resetting {_failedEndpoints.Count} failed endpoints.");
        _failedEndpoints.Clear();
        _consecutiveFailures = 0;
        _isFailingOver = false;
    }

    /// <summary>
    /// Gets the next available endpoint from the priority list, excluding failed ones.
    /// Returns null if no servers are available.
    /// </summary>
    private ServerEndpoint GetNextAvailableEndpoint()
    {
        var available = _endpointPriority.Where(e => !_failedEndpoints.Contains(e)).ToList();
        
        if (available.Count == 0)
        {
            GD.PrintErr("NetworkManager: No available endpoints remaining!");
            return null;
        }

        // Get first available that's not the current one
        var next = available.FirstOrDefault(e => e != _selectedEndpoint) ?? available.First();
        return next;
    }

    /// <summary>
    /// Marks an endpoint as failed and adds it to the exclusion list.
    /// </summary>
    private void MarkEndpointAsFailed(ServerEndpoint endpoint)
    {
        if (!_failedEndpoints.Contains(endpoint))
        {
            _failedEndpoints.Add(endpoint);
            GD.Print($"NetworkManager: Marked {endpoint.Region} as failed. Failed endpoints: {_failedEndpoints.Count}");
        }
    }

    // --- GAME ACTIONS ---

    public async void SubscribeToGameChannel()
    {
        if (_pusherClient != null && !string.IsNullOrEmpty(_gameId))
        {
            // Subscribe to private player events
            string privateChannelName = "private-" + "game_" + _gameId + "_player_" + _authManager.UserId;
            await AuthenticateAndSubscribe(privateChannelName);

            // Subscribe to both players events
            string bothPlayersChannelName = "private-" + "game_" + _gameId;
            await AuthenticateAndSubscribe(bothPlayersChannelName);
        }
    }

    public async void SubscribeToMatchmakingChannel()
    {
        if (_pusherClient != null && !string.IsNullOrEmpty(PlayerSecret))
        {
            string channelName = "private-" + PlayerSecret + "_matchmaking_result";
            await AuthenticateAndSubscribe(channelName);
        }
    }

    private async Task AuthenticateAndSubscribe(string channelName)
    {
        if (_pusherClient.SocketId == null)
        {
            GD.PrintErr("NetworkManager: Cannot authenticate, socket_id is null.");
            return;
        }

        var req = new HttpRequest();
        AddChild(req);

        string[] headers =
        {
            "Accept: application/json",
            "Content-Type: application/x-www-form-urlencoded",
            $"Authorization: Bearer {_authManager?.Token ?? ""}"
        };

        string body = $"socket_id={_pusherClient.SocketId}&channel_name={channelName}";
        
        string authUrl = _selectedEndpoint.BaseUrl.Replace("/api", "") + "/broadcasting/auth";

        req.Request(authUrl, headers, HttpClient.Method.Post, body);

        var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
        req.QueueFree();

        long responseCode = result[1].AsInt64();
        byte[] responseBody = result[3].AsByteArray();

        if (responseCode < 200 || responseCode >= 300)
        {
            GD.PrintErr(
                $"NetworkManager: Auth API Error {responseCode} at {authUrl}. Response: {Encoding.UTF8.GetString(responseBody)}");
            return;
        }

        string jsonString = Encoding.UTF8.GetString(responseBody);

        try
        {
            var authResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString, _jsonOptions);
            if (authResponse != null && authResponse.TryGetValue("auth", out var authHashElement))
            {
                string authHash = authHashElement.GetString();
                GD.Print($"NetworkManager: Authenticated and subscribed to {channelName} with hash {authHash}");
                _pusherClient.Subscribe(channelName, authHash);
                
                // Track active channel
                if (!_activeChannels.Contains(channelName))
                {
                    _activeChannels.Add(channelName);
                }
            }
            else
            {
                GD.PrintErr($"NetworkManager: Invalid auth response for channel {channelName}: {jsonString}");
            }
        }
        catch (JsonException ex)
        {
            GD.PrintErr($"NetworkManager: Auth JSON Parse Error: {ex.Message}");
        }
    }

    public async void StartGame()
    {
        GD.Print("NetworkManager: Starting new game...");
        // We expect a dictionary or object with game_id
        var data = await SendApiRequest<JsonElement>("/games", HttpClient.Method.Post);

        if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("game_id", out var gameIdProp))
        {
            _gameId = gameIdProp.GetString();
            GD.Print($"NetworkManager: Game created with ID: {_gameId}");

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
        GD.Print($"NetworkManager: Connecting to match {_gameId}");

        SubscribeToGameChannel();
        await FetchGameState();
    }

    public async void JoinGame(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;

        _gameId = gameId;
        GD.Print($"NetworkManager: Joining game {_gameId}");

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

        GD.Print($"NetworkManager: Sending action: {action}");
        var body = new { action };

        // We don't need the return data for actions, just fire and forget
        await SendApiRequest<JsonElement>($"/games/{_gameId}/action", HttpClient.Method.Post, body);
    }

    public async Task FetchGameState()
    {
        if (string.IsNullOrEmpty(_gameId)) return;

        var data = await SendApiRequest<JsonElement>($"/games/{_gameId}", HttpClient.Method.Get);

        if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("state", out var stateProp))
        {
            try
            {
                var gameState = stateProp.Deserialize<GameState>(_jsonOptions);
                StateUpdated?.Invoke(gameState);
            }
            catch (JsonException ex)
            {
                GD.PrintErr($"NetworkManager: Failed to deserialize game state: {ex.Message}");
                EmitSignal(SignalName.NetworkError, "Failed to parse game state.");
            }
        }
        else
        {
            EmitSignal(SignalName.NetworkError, "Failed to fetch game state. Please try again.");
        }
    }

    // --- UTILITIES ---

    /// <summary>
    /// A single utility method to handle ALL HTTP requests, JSON parsing, and node cleanup.
    /// Returns default(T) (null) on failure.
    /// </summary>
    private async Task<T> SendApiRequest<T>(string endpoint, HttpClient.Method method, object body = null, bool isRetry = false)
    {
        // 1. Setup Request Node
        var req = new HttpRequest();
        AddChild(req);

        // 2. Prepare Headers & Body
        string[] headers =
        {
            "Accept: application/json",
            "Content-Type: application/json",
            $"player_secret: {PlayerSecret}",
            $"Authorization: Bearer {_authManager?.Token ?? ""}"
        };
        string jsonBody = body != null ? JsonSerializer.Serialize(body, _jsonOptions) : "";

        // 3. Send Request
        req.Request(_selectedEndpoint.BaseUrl + endpoint, headers, method, jsonBody);

        // 4. Wait for response using Godot's ToSignal (Async/Await)
        var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
        req.QueueFree(); // Immediately clean up the node

        // 5. Parse Results
        long httpResult = result[0].AsInt64();
        long responseCode = result[1].AsInt64();
        byte[] responseBody = result[3].AsByteArray();

        // Check if this is a server error (network issue or 5xx)
        if (IsServerError(httpResult, responseCode))
        {
            _consecutiveFailures++;
            GD.PrintErr(
                $"NetworkManager: Server Error at {endpoint}. HTTP Result: {httpResult}, Code: {responseCode}. " +
                $"Consecutive failures: {_consecutiveFailures}/{FailureThreshold}");
            
            // Trigger failover if threshold reached and not already retrying
            if (_consecutiveFailures >= FailureThreshold && !_isFailingOver && !isRetry)
            {
                GD.Print("NetworkManager: Failure threshold reached, triggering failover and retrying request...");
                bool failoverSuccess = await TriggerFailover();
                
                if (failoverSuccess)
                {
                    // Retry the request on the new server
                    GD.Print($"NetworkManager: Retrying request to {endpoint} on new server {_selectedEndpoint.Region}");
                    return await SendApiRequest<T>(endpoint, method, body, isRetry: true);
                }
            }
            
            return default;
        }

        // Application-level error (4xx) - don't count as server failure
        if (responseCode >= 400 && responseCode < 500)
        {
            GD.PrintErr(
                $"NetworkManager: Application Error {responseCode} at {endpoint}. Response: {Encoding.UTF8.GetString(responseBody)}");
            // Reset failure counter - server is responding properly
            _consecutiveFailures = 0;
            return default;
        }

        // Success - reset failure counter
        if (responseCode >= 200 && responseCode < 300)
        {
            _consecutiveFailures = 0;
            
            string jsonString = Encoding.UTF8.GetString(responseBody);

            try
            {
                return JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);
            }
            catch (JsonException ex)
            {
                GD.PrintErr($"NetworkManager: JSON Parse Error: {ex.Message}");
                return default;
            }
        }

        // Shouldn't reach here, but handle gracefully
        GD.PrintErr($"NetworkManager: Unexpected response code {responseCode} at {endpoint}");
        return default;
    }

    /// <summary>
    /// Determines if an error is a server failure (network issue or server error).
    /// Returns true for timeouts, connection errors, and 5xx errors.
    /// Returns false for successful responses and 4xx client errors.
    /// </summary>
    private bool IsServerError(long httpResult, long responseCode)
    {
        // HTTPRequest result codes: 0 = success, non-zero = network error
        // Common Godot HTTPRequest.Result values:
        // RESULT_SUCCESS = 0
        // RESULT_CANT_CONNECT = 7
        // RESULT_CANT_RESOLVE = 8
        // RESULT_CONNECTION_ERROR = 9
        // RESULT_TIMEOUT = 11
        
        if (httpResult != 0)
        {
            // Network-level failure (timeout, can't connect, etc.)
            return true;
        }

        // Server error (5xx)
        if (responseCode >= 500 && responseCode < 600)
        {
            return true;
        }

        return false;
    }

    // --- FAILOVER LOGIC ---

    /// <summary>
    /// Triggers a failover to the next available server.
    /// Called when consecutive failures reach the threshold.
    /// </summary>
    private async Task<bool> TriggerFailover()
    {
        if (_isFailingOver)
        {
            GD.Print("NetworkManager: Failover already in progress, skipping.");
            return false;
        }

        // Check failover cooldown
        var currentTime = Time.GetTicksMsec();
        if (currentTime - _lastFailoverTime < FailoverCooldownMs)
        {
            GD.Print($"NetworkManager: Failover on cooldown. Wait {(FailoverCooldownMs - (currentTime - _lastFailoverTime)) / 1000}s");
            return false;
        }

        _isFailingOver = true;
        _lastFailoverTime = currentTime;
        GD.Print($"NetworkManager: Triggering failover after {_consecutiveFailures} failures.");

        // Mark current endpoint as failed
        MarkEndpointAsFailed(_selectedEndpoint);

        // Get next available endpoint
        var nextEndpoint = GetNextAvailableEndpoint();
        if (nextEndpoint == null)
        {
            GD.PrintErr("NetworkManager: All servers failed!");
            AllServersFailed?.Invoke();
            _isFailingOver = false;
            return false;
        }

        GD.Print($"NetworkManager: Switching from {_selectedEndpoint.Region} to {nextEndpoint.Region}");
        ServerSwitching?.Invoke(nextEndpoint);

        // Switch endpoint
        _selectedEndpoint = nextEndpoint;
        _consecutiveFailures = 0; // Reset counter for new server

        // Reconnect WebSocket
        bool reconnected = await ReconnectWebSocket();

        if (reconnected)
        {
            GD.Print($"NetworkManager: Failover complete. Now using {_selectedEndpoint.Region}");
            ServerSwitched?.Invoke(_selectedEndpoint);
            _isFailingOver = false;
            return true;
        }
        else
        {
            GD.PrintErr($"NetworkManager: Failed to reconnect WebSocket to {_selectedEndpoint.Region}");
            _isFailingOver = false;
            // Try next server
            return await TriggerFailover();
        }
    }

    /// <summary>
    /// Reconnects the WebSocket to the current selected endpoint.
    /// Resubscribes to all active channels.
    /// </summary>
    private async Task<bool> ReconnectWebSocket()
    {
        GD.Print("NetworkManager: Reconnecting WebSocket...");

        // Disconnect old WebSocket
        if (_pusherClient != null)
        {
            _pusherClient.QueueFree();
        }

        // Create new WebSocket connection
        InitializeWebSocket();

        // Wait for connection (timeout after 10 seconds)
        var startTime = Time.GetTicksMsec();
        const long ConnectionTimeout = 10000; // 10 seconds

        while (_pusherClient.SocketId == null && (Time.GetTicksMsec() - startTime) < ConnectionTimeout)
        {
            await Task.Delay(100);
        }

        if (_pusherClient.SocketId == null)
        {
            GD.PrintErr("NetworkManager: WebSocket connection timeout!");
            return false;
        }

        GD.Print($"NetworkManager: WebSocket connected. Resubscribing to {_activeChannels.Count} channels...");

        // Resubscribe to all active channels
        var channelsToResubscribe = new List<string>(_activeChannels);
        _activeChannels.Clear(); // Will be re-added by AuthenticateAndSubscribe

        foreach (var channel in channelsToResubscribe)
        {
            await AuthenticateAndSubscribe(channel);
        }

        GD.Print("NetworkManager: WebSocket reconnection complete.");
        return true;
    }

    // --- WEBSOCKETS ---

    private void InitializeWebSocket()
    {
        GD.Print("NetworkManager: Connecting WebSocket...");
        _pusherClient = new PusherClient();
        AddChild(_pusherClient);
        _pusherClient.EventReceived += OnPusherEventReceived;
        _pusherClient.ConnectToServer(_selectedEndpoint.WsUrl);
    }

    private void OnPusherEventReceived(string eventName, Variant data)
    {
        GD.Print($"NetworkManager: WS Event '{eventName}'");
        
        try 
        {
            // Convert Godot Dictionary back to JSON string for consistent deserialization
            string json = Godot.Json.Stringify(data);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (eventName)
            {
                case "game_state_updated":
                {
                    GameState gameState = null;
                
                    // Check if wrapped in "state"
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("state", out var stateProp))
                    {
                        gameState = stateProp.Deserialize<GameState>(_jsonOptions);
                    }
                
                    if (gameState != null)
                    {
                        StateUpdated?.Invoke(gameState);
                    }

                    break;
                }
                case "round_finished":
                {
                    RoundFinished finished = null;
                    // Check if wrapped in "state" or similar - though usually round_finished might be distinct
                    // For now, assume it might be wrapped or direct, similar pattern
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var stateProp))
                    {
                        finished = stateProp.Deserialize<RoundFinished>(_jsonOptions);
                    }

                    if (finished != null)
                    {
                        RoundFinished?.Invoke(finished);
                    }

                    break;
                }
                case "game_finished":
                {
                    GameFinished finished = null;
                    // Check if wrapped in "state" or similar - though usually round_finished might be distinct
                    // For now, assume it might be wrapped or direct, similar pattern
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var stateProp))
                    {
                        finished = stateProp.Deserialize<GameFinished>(_jsonOptions);
                    }

                    if (finished != null)
                    {
                        GameFinished?.Invoke(finished);
                    }

                    break;
                }
                case "match_found":
                {
                    string matchGameId = null;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("game_id", out var gameIdProp))
                    {
                        matchGameId = gameIdProp.GetString();
                    }
                    if (matchGameId != null)
                    {
                        MatchFound?.Invoke(matchGameId);
                    }
                    break;
                }
            }
        }
        catch (JsonException ex)
        {
             GD.PrintErr($"NetworkManager: Failed to deserialize Pusher event '{eventName}': {ex.Message}");
        }
    }
}