using System.Text.Json;
using System.Text.Json.Serialization;
using DexStationPlanner.Core.Geometry;

namespace DexStationPlanner.Core.Serialization;

/// <summary>Writes <see cref="Vec2"/> as a compact <c>[x, y]</c> array, rounded to 0.1 mm.</summary>
public sealed class Vec2JsonConverter : JsonConverter<Vec2>
{
    public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [x, y] for a point.");
        reader.Read();
        var x = reader.GetDouble();
        reader.Read();
        var y = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Expected [x, y] for a point.");
        return new Vec2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 4));
        writer.WriteNumberValue(Math.Round(value.Y, 4));
        writer.WriteEndArray();
    }
}
