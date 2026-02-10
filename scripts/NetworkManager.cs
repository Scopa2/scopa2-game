using System;
using Godot;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

public partial class NetworkManager : Node
{
    // C# Events for complex types (Godot Signals don't support POCOs well)
    public event Action<GameState> StateUpdated;
    public event Action<RoundFinished> RoundFinished;
    public event Action<GameFinished> GameFinished;

    [Signal]
    public delegate void NetworkErrorEventHandler(string errorMessage);

    //private const string BaseUrl = "http://100.76.114.126:8000/api";
    private const string BaseUrl = "http://100.109.16.123:8000/api";
    private const string ReverbUrl = "ws://100.109.16.123:6001/app/app-key?protocol=7&client=Godot&version=1.0.0";

    private string _gameId = "";
    private string _playerSecret = "";
    private PusherClient _pusherClient;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override void _Ready()
    {
        // For testing purposes, get player secret from environment variable
        _playerSecret = System.Environment.GetEnvironmentVariable("PLAYER_SECRET") ?? "SECRET";
        GD.Print($"NetworkManager: Player secret from env: {_playerSecret}");

        InitializeWebSocket();
    }

    // --- GAME ACTIONS ---

    public async void StartGame()
    {
        GD.Print("NetworkManager: Starting new game...");
        // We expect a dictionary or object with game_id
        var data = await SendApiRequest<JsonElement>("/games", HttpClient.Method.Post);

        if (data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("game_id", out var gameIdProp))
        {
            _gameId = gameIdProp.GetString();
            GD.Print($"NetworkManager: Game created with ID: {_gameId}");

            _pusherClient.Subscribe(_playerSecret + "_games");
            await FetchGameState();
        }
        else
        {
            EmitSignal(SignalName.NetworkError, "Failed to start game. Please try again.");
        }
    }

    public async void JoinGame(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;

        _gameId = gameId;
        GD.Print($"NetworkManager: Joining game {_gameId}");

        var data = await SendApiRequest<JsonElement>($"/games/{_gameId}/join", HttpClient.Method.Post);

        if (data.ValueKind != JsonValueKind.Undefined)
        {
            _pusherClient.Subscribe(_playerSecret + "_games");
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
            $"player_secret: {_playerSecret}"
        };
        string jsonBody = body != null ? JsonSerializer.Serialize(body, _jsonOptions) : "";

        // 3. Send Request
        req.Request(BaseUrl + endpoint, headers, method, jsonBody);

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
            }
        }
        catch (JsonException ex)
        {
             GD.PrintErr($"NetworkManager: Failed to deserialize Pusher event '{eventName}': {ex.Message}");
        }
    }
}