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
    private const float PollIntervalSeconds = 3.0f;

    private AuthManager _authManager;
    private Timer _pollTimer;
    private bool _isSearching;

    // Events
    public event Action QueueJoined;
    public event Action QueueLeft;
    public event Action<string> QueueError;
    public event Action<bool, int> StatusUpdated; // (isQueued, queueSize)

    public bool IsSearching => _isSearching;

    public override void _Ready()
    {
        _authManager = GetNode<AuthManager>("/root/AuthManager");

        _pollTimer = new Timer();
        _pollTimer.WaitTime = PollIntervalSeconds;
        _pollTimer.OneShot = false;
        _pollTimer.Timeout += PollStatus;
        AddChild(_pollTimer);
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
                    _pollTimer.Start();
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
        _pollTimer.Stop();

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
    /// Poll the matchmaking status. Called by timer while searching.
    /// </summary>
    private async void PollStatus()
    {
        if (!_isSearching) return;

        var (responseCode, json) = await SendRequest("/matchmaking/status", HttpClient.Method.Get);

        if (responseCode >= 200 && responseCode < 300)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                bool queued = root.GetProperty("queued").GetBoolean();
                int queueSize = root.GetProperty("queue_size").GetInt32();

                StatusUpdated?.Invoke(queued, queueSize);

                if (!queued && _isSearching)
                {
                    // Server removed us from queue (could mean match found or timeout)
                    _isSearching = false;
                    _pollTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"MatchmakingManager: Failed to parse status response: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Called when a match is found (from NetworkManager detecting the match_found WS event).
    /// Stops polling and resets state.
    /// </summary>
    public void OnMatchFound()
    {
        _isSearching = false;
        _pollTimer.Stop();
        GD.Print("MatchmakingManager: Match found, stopped polling.");
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

        req.Request(Constants.BaseUrl + endpoint, headers, method);

        var result = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
        req.QueueFree();

        long responseCode = result[1].AsInt64();
        byte[] responseBody = result[3].AsByteArray();
        string json = Encoding.UTF8.GetString(responseBody);

        return (responseCode, json);
    }
}
