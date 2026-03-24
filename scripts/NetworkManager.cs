using System;
using System.Collections.Generic;
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

    [Signal]
    public delegate void NetworkErrorEventHandler(string errorMessage);

    private const string ReverbUrl = "ws://murkrow.macbook:8080/ws/app/app-key?protocol=7&client=Godot&version=1.0.0";

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

    public override void _Ready()
    {
        _authManager = GetNode<AuthManager>("/root/AuthManager");
        GD.Print($"NetworkManager: Using AuthManager, logged in: {_authManager.IsLoggedIn}");

        InitializeWebSocket();
    }

    // --- GAME ACTIONS ---

    public async void SubscribeToGameChannel()
    {
        if (_pusherClient != null && !string.IsNullOrEmpty(_gameId))
        {
            string channelName = "private-" + "game_" + _gameId;
            await AuthenticateAndSubscribe(channelName);
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
        
        string authUrl = Constants.BaseUrl.Replace("/api", "") + "/broadcasting/auth";

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
    private async Task<T> SendApiRequest<T>(string endpoint, HttpClient.Method method, object body = null)
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
        req.Request(Constants.BaseUrl + endpoint, headers, method, jsonBody);

        // 4. Wait for response using Godot's ToSignal (Async/Await)
        var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
        req.QueueFree(); // Immediately clean up the node

        // 5. Parse Results
        long responseCode = result[1].AsInt64();
        byte[] responseBody = result[3].AsByteArray();

        if (responseCode < 200 || responseCode >= 300)
        {
            GD.PrintErr(
                $"NetworkManager: API Error {responseCode} at {endpoint}. Response: {Encoding.UTF8.GetString(responseBody)}");
            return default;
        }

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

    // --- WEBSOCKETS ---

    private void InitializeWebSocket()
    {
        GD.Print("NetworkManager: Connecting WebSocket...");
        _pusherClient = new PusherClient();
        AddChild(_pusherClient);
        _pusherClient.EventReceived += OnPusherEventReceived;
        _pusherClient.ConnectToServer(ReverbUrl);
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