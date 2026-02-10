using System.Collections.Generic;
using System.Text.Json.Serialization;
using Scopa2Game.Scripts.Models.Converters;

namespace Scopa2Game.Scripts.Models;

public partial class PlayerState
{
    [JsonPropertyName("hand")]
    public List<string> Hand { get; set; } = new();

    [JsonPropertyName("captured")]
    public List<string> Captured { get; set; } = new();

    [JsonPropertyName("scope")]
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int Scope { get; set; }

    [JsonPropertyName("totalScore")]
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int TotalScore { get; set; }

    [JsonPropertyName("blood")]
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int Blood { get; set; }

    [JsonPropertyName("santi")]
    public List<ShopItem> Santi { get; set; } = new();
}