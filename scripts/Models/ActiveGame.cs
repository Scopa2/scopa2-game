using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models;

public partial class ActiveGame
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("opponent_id")]
    public int OpponentId { get; set; }

    [JsonPropertyName("opponent_name")]
    public string OpponentName { get; set; }

    [JsonPropertyName("is_my_turn")]
    public bool IsMyTurn { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; }
}
