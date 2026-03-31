using System;
using System.Text;
using System.Text.Json;
using Godot;

namespace Scopa2Game.Scripts;

/// <summary>
/// Handles matchmaking queue operations: join, leave, poll status.
/// Uses Bearer token auth from AuthManager. Fires events for UI updates.
/// </summary>
public partial class MatchmakingManager : Node
{
    private AuthManager _authManager;
    private NetworkManager _networkManager;
    private bool _isSearching;

    // Events
    public event Action QueueJoined;
    public event Action QueueLeft;
    public event Action<string> QueueError;

    public bool IsSearching => _isSearching;

    public override void _Ready()
    {
        _authManager = GetNode<AuthManager>("/root/AuthManager");
        _networkManager = GetNode<NetworkManager>("/root/NetworkManager");
    }

    /// <summary>
    /// Join the matchmaking queue. Sends POST /api/matchmaking/join.
    /// </summary>
    public async void JoinQueue()
    {
        if (_isSearching)
        {
            QueueError?.Invoke("Already searching for a match.");
            return;
        }

        var (responseCode, json) = await SendRequest("/matchmaking/join", HttpClient.Method.Post);

        if (responseCode >= 200 && responseCode < 300)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var status = doc.RootElement.GetProperty("status").GetString();

                if (status == "queued" || status == "already_queued")
                {
                    _isSearching = true;
                    QueueJoined?.Invoke();
                    GD.Print($"MatchmakingManager: Joined queue (status: {status})");
                }
                else
                {
                    QueueError?.Invoke($"Unexpected status: {status}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"MatchmakingManager: Failed to parse join response: {ex.Message}");
                QueueError?.Invoke("Failed to join queue.");
            }
        }
        else
        {
            GD.PrintErr($"MatchmakingManager: Join failed with HTTP {responseCode}: {json}");
            QueueError?.Invoke($"Failed to join queue (HTTP {responseCode}).");
        }
    }

    /// <summary>
    /// Leave the matchmaking queue. Sends POST /api/matchmaking/leave.
    /// </summary>
    public async void LeaveQueue()
    {
        _isSearching = false;

        var (responseCode, json) = await SendRequest("/matchmaking/leave", HttpClient.Method.Post);

        if (responseCode >= 200 && responseCode < 300)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var status = doc.RootElement.GetProperty("status").GetString();
                GD.Print($"MatchmakingManager: Left queue (status: {status})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"MatchmakingManager: Failed to parse leave response: {ex.Message}");
            }
        }
        else
        {
            GD.PrintErr($"MatchmakingManager: Leave failed with HTTP {responseCode}: {json}");
        }

        QueueLeft?.Invoke();
    }

    /// <summary>
    /// Called when a match is found (from NetworkManager detecting the match_found WS event).
    /// Resets state.
    /// </summary>
    public void OnMatchFound()
    {
        _isSearching = false;
        GD.Print("MatchmakingManager: Match found.");
    }

    /// <summary>
    /// Generic HTTP request helper with Bearer token auth.
    /// </summary>
    private async System.Threading.Tasks.Task<(long responseCode, string json)> SendRequest(
        string endpoint, HttpClient.Method method)
    {
        var req = new HttpRequest();
        AddChild(req);

        string[] headers =
        {
            "Accept: application/json",
            "Content-Type: application/json",
            $"Authorization: Bearer {_authManager?.Token ?? ""}"
        };

        // Get the selected endpoint from NetworkManager
        var selectedEndpoint = _networkManager?.GetSelectedEndpoint();
        string baseUrl = selectedEndpoint?.BaseUrl ?? Constants.Endpoints[0].BaseUrl;
        
        req.Request(baseUrl + endpoint, headers, method);

        var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
        req.QueueFree();

        long responseCode = result[1].AsInt64();
        byte[] responseBody = result[3].AsByteArray();
        string json = Encoding.UTF8.GetString(responseBody);

        return (responseCode, json);
    }
}
