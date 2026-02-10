using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class GameState
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("isMyTurn")]
    public bool IsMyTurn { get; set; }

    [JsonPropertyName("lastMovePgn")]
    public string LastMovePgn { get; set; }

    [JsonPropertyName("deck")]
    public List<string> Deck { get; set; } = new();

    [JsonPropertyName("table")]
    public List<string> Table { get; set; } = new();

    [JsonPropertyName("players")]
    public Dictionary<string, PlayerState> Players { get; set; } = new();

    [JsonPropertyName("shop")]
    public List<ShopItem> Shop { get; set; } = new();
}