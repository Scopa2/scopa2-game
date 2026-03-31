namespace Scopa2Game.Scripts;

public static class Constants
{
    public const int CardWidth = 140;
    public const int CardHeight = 190;
    
    public static readonly ServerEndpoint[] Endpoints = 
    {
        new ServerEndpoint("EU", "murkrow.macbook", 8080),
        new ServerEndpoint("US", "murkrow.macbook", 8081)
    };
}

public class ServerEndpoint
{
    public string Region { get; }
    public string Host { get; }
    public int Port { get; }
    public string BaseUrl => $"http://{Host}:{Port}/api";
    public string WsUrl => $"ws://{Host}:{Port}/ws/app/app-key?protocol=7&client=Godot&version=1.0.0";
    
    public ServerEndpoint(string region, string host, int port)
    {
        Region = region;
        Host = host;
        Port = port;
    }
}
