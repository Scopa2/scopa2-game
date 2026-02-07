using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class RoundResults
{
    // Keeping it generic for now as we haven't seen the exact JSON structure for results yet.
    // Using JsonExtensionData to capture everything ensures we don't lose data.
    
    [JsonExtensionData]
    public Dictionary<string, object> Data { get; set; } = new();

    // Common Scopa result fields (anticipated)
    [JsonPropertyName("scores")]
    public Dictionary<string, int> Scores { get; set; }
    
    [JsonPropertyName("details")]
    public Dictionary<string, Dictionary<string, bool>> Details { get; set; }
}