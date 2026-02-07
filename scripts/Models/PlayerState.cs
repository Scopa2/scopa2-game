using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class PlayerState
{
    [JsonPropertyName("hand")]
    public List<string> Hand { get; set; } = new();

    [JsonPropertyName("captured")]
    public List<string> Captured { get; set; } = new();

    [JsonPropertyName("scope")]
    public double Scope { get; set; }

    [JsonPropertyName("totalScore")]
    public double TotalScore { get; set; }
}
