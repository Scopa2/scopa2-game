using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models.Converters;

public class DoubleToIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Read as double then cast to int
            double val = reader.GetDouble();
            return (int)val;
        }
        
        // Fallback or error
        return reader.GetInt32();
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
