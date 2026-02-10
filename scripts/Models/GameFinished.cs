using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class GameFinished
{
    
    [JsonPropertyName("lastCapturePlayer")]
    public string LastCapturePlayer { get; set; }
    
    [JsonPropertyName("roundScores")]
    public Dictionary<string, PlayerRoundScores> RoundScores { get; set; } = new();
    
    [JsonPropertyName("gameScores")]
    public Dictionary<string, int> GameScores { get; set; } = new();
    
    [JsonPropertyName("winner")]
    public string Winner { get; set; }
}