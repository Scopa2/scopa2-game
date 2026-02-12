using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scopa2Game.Scripts.Models.Converters;

/// <summary>
/// Converter for mutations dictionary that handles various JSON formats.
/// Mutations come as { "1S": "1D", "8S": "8D" } but may be parsed differently by Godot.
/// </summary>
public class MutationsDictionaryConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, string>();
        
        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }
        
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            // If it's not an object, return empty dictionary
            reader.Skip();
            return result;
        }
        
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }
            
            string key = reader.GetString();
            reader.Read();
            
            string value = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDouble().ToString(),
                JsonTokenType.Null => null,
                _ => null
            };
            
            if (key != null && value != null)
            {
                result[key] = value;
            }
        }
        
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }
}
