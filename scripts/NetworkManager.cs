using Godot;
using System;
using System.Text;

namespace Scopa2Game.Scripts;

public partial class NetworkManager : Node
{
    [Signal]
    public delegate void StateUpdatedEventHandler(Godot.Collections.Dictionary state);

    private const string BaseUrl = "http://localhost:8000/api";
    private const string ReverbUrl = "ws://127.0.0.1:6001/app/app-key?protocol=7&client=Godot&version=1.0.0";

    private string _gameId = "";
    private string _playerId = "p1";

    private HttpRequest _httpRequest;
    private PusherClient _pusherClient;

    public override void _Ready()
    {
        // Setup HTTP client
        _httpRequest = new HttpRequest();
        AddChild(_httpRequest);
        _httpRequest.RequestCompleted += OnRequestCompleted;

        // Setup WebSocket client
        GD.Print("NetworkManager: WebSocket mode enabled.");
        _pusherClient = new PusherClient();
        AddChild(_pusherClient);
        _pusherClient.EventReceived += OnPusherEventReceived;
        _pusherClient.ConnectToServer(ReverbUrl);
    }

    public void StartGame()
    {
        string[] headers = { "Accept: application/json" };
        var error = _httpRequest.Request(BaseUrl + "/games", headers, HttpClient.Method.Post);
        if (error != Error.Ok)
        {
            GD.PrintErr("NetworkManager: Error in HTTPRequest.StartGame().");
        }
    }

    public void SendAction(string action)
    {
        GD.Print($"NetworkManager: Sending action: {action}");
        if (string.IsNullOrEmpty(_gameId))
        {
            GD.PrintErr("NetworkManager: Cannot send action, no game ID.");
            return;
        }

        var url = $"{BaseUrl}/games/{_gameId}/action?player={_playerId}";
        string[] headers = { "Content-Type: application/json", "Accept: application/json" };
        
        var bodyDict = new Godot.Collections.Dictionary { { "action", action } };
        string body = Json.Stringify(bodyDict);

        var error = _httpRequest.Request(url, headers, HttpClient.Method.Post, body);
        if (error != Error.Ok)
        {
            GD.PrintErr("NetworkManager: Error in HTTPRequest.SendAction().");
        }
    }

    private void OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode < 200 || responseCode >= 300)
        {
            string bodyStr = Encoding.UTF8.GetString(body);
            GD.PrintErr($"NetworkManager: HTTP request failed with code {responseCode}. Body: {bodyStr}");
            return;
        }

        string jsonString = Encoding.UTF8.GetString(body);
        var jsonResult = Json.ParseString(jsonString);
        
        if (jsonResult.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr("NetworkManager: Failed to parse HTTP JSON response.");
            return;
        }

        var data = jsonResult.AsGodotDictionary();

        // Handle initial response
        if (data.ContainsKey("game_id") && !data.ContainsKey("state"))
        {
            if (string.IsNullOrEmpty(_gameId))
            {
                _gameId = data["game_id"].AsString();
                GD.Print($"NetworkManager: Game created with ID: {_gameId}");
                // _pusherClient.Subscribe("game." + _gameId);
                _pusherClient.Subscribe("games");
                FetchHttpGameState();
                return;
            }
        }

        if (data.ContainsKey("state"))
        {
            var state = data["state"].AsGodotDictionary();
            if (state.ContainsKey("turnIndex") && state["turnIndex"].AsInt32() == 1)
            {
                EmitSignal(SignalName.StateUpdated, data);
            }
        }
    }

    private void OnPusherEventReceived(string eventName, Variant data)
    {
        GD.Print($"NetworkManager: WebSocket event received '{eventName}' with data: {data}");
        if (eventName == "game_sate_updated" && data.VariantType == Variant.Type.Dictionary)
        {
            EmitSignal(SignalName.StateUpdated, data.AsGodotDictionary());
        }
    }

    private void FetchHttpGameState()
    {
        if (string.IsNullOrEmpty(_gameId))
        {
            GD.PrintErr("NetworkManager: Cannot fetch state, no game ID.");
            return;
        }

        var url = $"{BaseUrl}/games/{_gameId}?player={_playerId}";
        string[] headers = { "Accept: application/json" };
        var error = _httpRequest.Request(url, headers, HttpClient.Method.Get);
        if (error != Error.Ok)
        {
             GD.PrintErr("NetworkManager: Error fetching game state via HTTP.");
        }
    }
}
