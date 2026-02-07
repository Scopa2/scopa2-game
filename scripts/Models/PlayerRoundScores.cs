using System.Collections.Generic;
using System.Text.Json.Serialization;
using Scopa2Game.Scripts.Models.Converters;

namespace Scopa2Game.Scripts.Models;

public partial class PlayerRoundScores
{
    [JsonPropertyName("settebello")] 
    public bool Settebello { get; set; }

    [JsonPropertyName("primiera")] 
    public bool Primiera { get; set; }

    [JsonPropertyName("scopaCount")] 
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int ScopaCount { get; set; }

    [JsonPropertyName("allungo")] 
    public bool Allungo { get; set; }

    [JsonPropertyName("cardsCaptured")] 
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int CardsCaptured { get; set; }

    [JsonPropertyName("denari")] 
    public bool Denari { get; set; }

    [JsonPropertyName("denariCount")] 
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int DenariCount { get; set; }
}
