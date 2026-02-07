using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class RoundFinished
{
    
    [JsonPropertyName("lastCapturePlayer")]
    public string LastCapturePlayer { get; set; }
    
    [JsonPropertyName("roundScores")]
    public Dictionary<string, PlayerRoundScores> RoundScores { get; set; } = new();
    
}