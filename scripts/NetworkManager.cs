using Godot;
using System.Text;
using System.Threading.Tasks;

namespace Scopa2Game.Scripts;

public partial class NetworkManager : Node
{
    [Signal]
    public delegate void StateUpdatedEventHandler(Godot.Collections.Dictionary state);

    private const string BaseUrl = "http://100.76.114.126:8000/api";
    private const string ReverbUrl = "ws://100.76.114.126:6001/app/app-key?protocol=7&client=Godot&version=1.0.0";

    private string _gameId = "";
    private string _playerSecret = "";
    private PusherClient _pusherClient;

    public override void _Ready()
    {
        // Generate a unique player secret for this session
        // _playerSecret = System.Guid.NewGuid().ToString();
        // GD.Print($"NetworkManager: Player secret generated: {_playerSecret}");
        
        // For testing purposes, get player secret from environment variable
        _playerSecret = System.Environment.GetEnvironmentVariable("PLAYER_SECRET") ?? "";
        GD.Print($"NetworkManager: Player secret from env: {_playerSecret}");
        
        InitializeWebSocket();
    }

    // --- GAME ACTIONS ---

    public async void StartGame()
    {
        GD.Print("NetworkManager: Starting new game...");
        var data = await SendApiRequest("/games", HttpClient.Method.Post);

        if (data != null && data.ContainsKey("game_id"))
        {
            _gameId = data["game_id"].AsString();
            GD.Print($"NetworkManager: Game created with ID: {_gameId}");
            
            _pusherClient.Subscribe(_playerSecret + "_games");
            await FetchGameState();
        }
    }

    public async void JoinGame(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;

        _gameId = gameId;
        GD.Print($"NetworkManager: Joining game {_gameId}");

        var data = await SendApiRequest($"/games/{_gameId}/join", HttpClient.Method.Post);

        if (data != null)
        {
            _pusherClient.Subscribe(_playerSecret + "_games");
            await FetchGameState();
        }
    }

    public async void SendAction(string action)
    {
        if (string.IsNullOrEmpty(_gameId)) return;

        GD.Print($"NetworkManager: Sending action: {action}");
        var body = new Godot.Collections.Dictionary { { "action", action } };
        
        // We don't need the return data for actions, just fire and forget
        await SendApiRequest($"/games/{_gameId}/action", HttpClient.Method.Post, body);
    }

    public async Task FetchGameState()
    {
        if (string.IsNullOrEmpty(_gameId)) return;

        var data = await SendApiRequest($"/games/{_gameId}", HttpClient.Method.Get);
        
        if (data != null && data.ContainsKey("state"))
        {
            EmitSignal(SignalName.StateUpdated, data);
        }
    }

    // --- UTILITIES ---

    /// <summary>
    /// A single utility method to handle ALL HTTP requests, JSON parsing, and node cleanup.
    /// </summary>
    private async Task<Godot.Collections.Dictionary> SendApiRequest(string endpoint, HttpClient.Method method, Godot.Collections.Dictionary body = null)
    {
        // 1. Setup Request Node
        var req = new HttpRequest();
        AddChild(req);

        // 2. Prepare Headers & Body
        string[] headers = {
            "Accept: application/json",
            "Content-Type: application/json",
            $"player_secret: {_playerSecret}"
        };
        string jsonBody = body != null ? Json.Stringify(body) : "";

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
            GD.PrintErr($"NetworkManager: API Error {responseCode} at {endpoint}. Response: {Encoding.UTF8.GetString(responseBody)}");
            return null;
        }

        string jsonString = Encoding.UTF8.GetString(responseBody);
        var jsonResult = Json.ParseString(jsonString);

        return jsonResult.VariantType == Variant.Type.Nil ? null : jsonResult.AsGodotDictionary();
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
        
        // NOTE: I corrected a potential typo here from "game_sate_updated" to "game_state_updated"
        if (eventName == "game_state_updated" && data.VariantType == Variant.Type.Dictionary)
        {
            EmitSignal(SignalName.StateUpdated, data.AsGodotDictionary());
        }
    }
}