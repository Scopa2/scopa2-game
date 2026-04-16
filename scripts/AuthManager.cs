using System;
using System.Text.Json;
using Godot;

namespace Scopa2Game.Scripts;

/// <summary>
/// Manages user authentication, persistent credential storage, and registration.
/// Stores user ID and API token in a local config file.
/// </summary>
public partial class AuthManager : Node
{
    private const string ConfigPath = "user://auth.cfg";
    private const string ConfigSection = "auth";

    public int UserId { get; private set; }
    public string Token { get; private set; }
    public string Username { get; private set; }

    /// <summary>The user ID as a string, used as player_secret in game API calls.</summary>
    public string PlayerSecret => UserId.ToString();

    public bool IsLoggedIn => UserId > 0 && !string.IsNullOrEmpty(Token);

    // Events
    public event Action<string> RegisterSucceeded; // username
    public event Action<string> RegisterFailed;    // error message

    public override void _Ready()
    {
        LoadCredentials();
    }

    /// <summary>
    /// Attempts to register a new user with the given username.
    /// On success, stores credentials persistently and fires RegisterSucceeded.
    /// On failure, fires RegisterFailed with an error message.
    /// </summary>
    public async void Register(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            RegisterFailed?.Invoke("Username cannot be empty.");
            return;
        }

        var networkManager = GetNode<NetworkManager>("/root/NetworkManager");
        var data = await networkManager.SendApiRequest<JsonElement>(
            "/auth/register", HttpClient.Method.Post, new { username });

        if (data.ValueKind == JsonValueKind.Undefined)
        {
            RegisterFailed?.Invoke("Registration failed. Please try again.");
            return;
        }

        try
        {
            var responseData = data.GetProperty("data");
            var user  = responseData.GetProperty("user");
            var token = responseData.GetProperty("token").GetString();
            var id    = user.GetProperty("id").GetInt32();
            var name  = user.GetProperty("username").GetString();

            UserId   = id;
            Token    = token;
            Username = name;
            SaveCredentials();

            GD.Print($"AuthManager: Registered successfully as '{Username}' (ID: {UserId})");
            RegisterSucceeded?.Invoke(Username);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"AuthManager: Failed to parse register response: {ex.Message}");
            RegisterFailed?.Invoke("Unexpected server response.");
        }
    }

    /// <summary>Clears stored credentials and resets state.</summary>
    public void Logout()
    {
        UserId = 0;
        Token = null;
        Username = null;

        var config = new ConfigFile();
        config.Save(ConfigPath); // save empty file to clear
        GD.Print("AuthManager: Logged out, credentials cleared.");
    }

    private void LoadCredentials()
    {
        var config = new ConfigFile();
        var err = config.Load(ConfigPath);
        if (err != Error.Ok)
        {
            GD.Print("AuthManager: No saved credentials found.");
            return;
        }

        UserId = (int)config.GetValue(ConfigSection, "user_id", 0);
        Token = (string)config.GetValue(ConfigSection, "token", "");
        Username = (string)config.GetValue(ConfigSection, "username", "");

        if (IsLoggedIn)
        {
            GD.Print($"AuthManager: Loaded credentials for '{Username}' (ID: {UserId})");
        }
    }

    private void SaveCredentials()
    {
        var config = new ConfigFile();
        config.SetValue(ConfigSection, "user_id", UserId);
        config.SetValue(ConfigSection, "token", Token);
        config.SetValue(ConfigSection, "username", Username);
        config.Save(ConfigPath);
        GD.Print("AuthManager: Credentials saved.");
    }
}
