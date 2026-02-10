using System.Text.Json.Serialization;
using Scopa2Game.Scripts.Models.Converters;

namespace Scopa2Game.Scripts.Models;

public partial class ShopItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("cost")]
    [JsonConverter(typeof(DoubleToIntConverter))]
    public int Cost { get; set; }
}
