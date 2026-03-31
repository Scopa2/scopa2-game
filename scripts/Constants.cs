namespace Scopa2Game.Scripts;

public static class Constants
{
    public const int CardWidth = 140;
    public const int CardHeight = 190;
    
    public static readonly ServerEndpoint[] Endpoints = 
    {
        // new ServerEndpoint("EU", "murkrow.macbook", 8080),
        // new ServerEndpoint("US", "murkrow.macbook", 8081)
        new ServerEndpoint("EU", "scopa-eu.murkrowdev.org"),
        new ServerEndpoint("US", "scopa-us.murkrowdev.org")
    };
}

public class ServerEndpoint
{
    public string Region { get; }
    public string Host { get; }

    public string BaseUrl => $"https://{Host}/api";
    public string WsUrl => $"wss://{Host}/ws/app/app-key?protocol=7&client=Godot&version=1.0.0";
    
    public ServerEndpoint(string region, string host)
    {
        Region = region;
        Host = host;
    }
}
