using Godot;
using System;

namespace Scopa2Game.Scripts;

public partial class PusherClient : Node
{
    [Signal]
    public delegate void ConnectedEventHandler();
    
    [Signal]
    public delegate void EventReceivedEventHandler(string eventName, Variant data);
    
    [Signal]
    public delegate void DisconnectedEventHandler();

    private enum State
    {
        Disconnected,
        Connecting,
        Connected
    }

    private const float PingInterval = 25.0f;

    private WebSocketPeer _peer;
    private State _currentState = State.Disconnected;
    private Timer _pingTimer;
    private string _socketId;

    public override void _Ready()
    {
        _peer = new WebSocketPeer();
        _pingTimer = new Timer();
        _pingTimer.WaitTime = PingInterval;
        _pingTimer.OneShot = false;
        _pingTimer.Timeout += SendPing;
        AddChild(_pingTimer);
    }

    public override void _Process(double delta)
    {
        if (_currentState == State.Disconnected) return;

        _peer.Poll();
        var newState = _peer.GetReadyState();

        if (newState == WebSocketPeer.State.Closed)
        {
            Disconnect();
            return;
        }

        while (_peer.GetAvailablePacketCount() > 0)
        {
            var packet = _peer.GetPacket();
            ParseMessage(packet.GetStringFromUtf8());
        }
    }

    public void ConnectToServer(string url)
    {
        if (_currentState != State.Disconnected)
        {
            GD.PrintErr("PusherClient: Already connected or connecting.");
            return;
        }

        GD.Print($"PusherClient: Connecting to {url}");
        _currentState = State.Connecting;
        var err = _peer.ConnectToUrl(url);
        if (err != Error.Ok)
        {
            GD.PrintErr("PusherClient: Failed to connect to URL.");
            _currentState = State.Disconnected;
        }
    }

    public void Subscribe(string channelName)
    {
        if (_currentState != State.Connected)
        {
            GD.PrintErr("PusherClient: Cannot subscribe, not connected.");
            return;
        }

        var payload = new Godot.Collections.Dictionary
        {
            { "event", "pusher:subscribe" },
            { "data", new Godot.Collections.Dictionary { { "channel", channelName } } }
        };
        SendJson(payload);
        GD.Print($"PusherClient: Subscribed to channel '{channelName}'");
    }

    private void SendJson(Godot.Collections.Dictionary data)
    {
        string jsonString = Json.Stringify(data);
        var err = _peer.SendText(jsonString);
        if (err != Error.Ok)
        {
            GD.PrintErr($"PusherClient: Error sending JSON: {err}");
        }
    }

    private void ParseMessage(string rawMessage)
    {
        var result = Json.ParseString(rawMessage);
        if (result.VariantType == Variant.Type.Nil)
        {
             GD.PrintErr($"PusherClient: Failed to parse incoming JSON: {rawMessage}");
             return;
        }

        var message = result.AsGodotDictionary();
        string eventName = message.ContainsKey("event") ? message["event"].AsString() : "";

        switch (eventName)
        {
            case "pusher:connection_established":
                HandleConnectionEstablished(message.ContainsKey("data") ? message["data"].AsString() : "");
                break;
            case "pusher:ping":
                SendJson(new Godot.Collections.Dictionary { { "event", "pusher:pong" }, { "data", new Godot.Collections.Dictionary() } });
                break;
            case "pusher_internal:subscription_succeeded":
                string channel = message.ContainsKey("channel") ? message["channel"].AsString() : "unknown";
                GD.Print($"PusherClient: Subscription succeeded for channel '{channel}'");
                break;
            default:
                var dataField = message.ContainsKey("data") ? message["data"] : default;
                Variant dataPayload = new Variant();

                if (dataField.VariantType == Variant.Type.String)
                {
                    var parsed = Json.ParseString(dataField.AsString());
                    // In C#, checking if parsed is "valid" might need more than type check if it returns null equivalent.
                    // But usually Json.ParseString returns Nil if invalid.
                    if (parsed.VariantType != Variant.Type.Nil)
                    {
                        dataPayload = parsed;
                    }
                    else
                    {
                        // Maybe it's just a string payload?
                        // GDScript logic: `if data_payload == null` (after parse)
                        // If it fails to parse as JSON, we might ignore or error.
                        // But wait, the server might send a string that ISN'T json?
                        // Re-reading GDScript: `data_payload = JSON.parse_string(data_field)`... `if data_payload == null: printerr...`
                        // So it expects JSON.
                        GD.PrintErr($"PusherClient: Failed to parse data string for event '{eventName}': {dataField.AsString()}");
                        return;
                    }
                }
                else if (dataField.VariantType == Variant.Type.Dictionary)
                {
                    dataPayload = dataField;
                }
                else
                {
                    // It might be Nil or something else
                    GD.PrintErr($"PusherClient: Unexpected data type for event '{eventName}': {dataField.VariantType}");
                    return;
                }
                
                EmitSignal(SignalName.EventReceived, eventName, dataPayload);
                break;
        }
    }

    private void HandleConnectionEstablished(string dataStr)
    {
        var result = Json.ParseString(dataStr);
        if (result.VariantType == Variant.Type.Nil)
        {
            GD.PrintErr("PusherClient: Failed to parse connection data.");
            Disconnect();
            return;
        }
        var data = result.AsGodotDictionary();

        _socketId = data.ContainsKey("socket_id") ? data["socket_id"].AsString() : "";
        int activityTimeout = data.ContainsKey("activity_timeout") ? data["activity_timeout"].AsInt32() : 30;
        
        _pingTimer.WaitTime = activityTimeout - 5;
        _pingTimer.Start();

        _currentState = State.Connected;
        EmitSignal(SignalName.Connected);
        GD.Print($"PusherClient: Connection established. Socket ID: {_socketId}");
    }

    private void SendPing()
    {
        if (_currentState == State.Connected)
        {
            SendJson(new Godot.Collections.Dictionary { { "event", "pusher:ping" }, { "data", new Godot.Collections.Dictionary() } });
        }
    }

    private void Disconnect()
    {
        if (_currentState == State.Disconnected) return;

        GD.Print("PusherClient: Disconnected.");
        _peer.Close();
        _pingTimer.Stop();
        _currentState = State.Disconnected;
        _socketId = "";
        EmitSignal(SignalName.Disconnected);
    }
}
