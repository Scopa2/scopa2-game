using System.Collections.Generic;
using System.Text.Json.Serialization;
using Scopa2Game.Scripts.Models.Converters;

namespace Scopa2Game.Scripts.Models;

public partial class GameState
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("isMyTurn")]
    public bool IsMyTurn { get; set; }

    [JsonPropertyName("lastMovePgn")]
    public string LastMovePgn { get; set; }
    
    [JsonPropertyName("mutations")]
    [JsonConverter(typeof(MutationsDictionaryConverter))]
    public Dictionary<string,string> Mutations { get; set; } = new();

    [JsonPropertyName("deck")]
    public List<string> Deck { get; set; } = new();

    [JsonPropertyName("table")]
    public List<string> Table { get; set; } = new();

    [JsonPropertyName("players")]
    public Dictionary<string, PlayerState> Players { get; set; } = new();

    [JsonPropertyName("shop")]
    public List<ShopItem> Shop { get; set; } = new();
}