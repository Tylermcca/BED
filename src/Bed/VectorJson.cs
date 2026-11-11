using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bed;

/// <summary>
/// Records <see cref="Vector2"/>, <see cref="Vector3"/>, <see cref="Vector4"/> and <see cref="Quaternion"/> command
/// arguments as JSON arrays (<c>[x, y, z]</c>, <c>[x, y, z, w]</c>), the way scene files write them. Without these, the
/// default serializer writes the vector types as <c>{}</c> (their components are fields) and a replay silently loses them.
/// Reading is strict: exactly the right count of finite numbers. Writing a non-finite component throws, so the session
/// refuses to record it.
/// </summary>
internal sealed class VectorJson<T>(int count, Func<float[], T> make, Func<T, float[]> parts) : JsonConverter<T>
{
    /// <summary>The converters for every vector type a command may carry.</summary>
    public static readonly JsonConverter[] All =
    [
        new VectorJson<Vector2>(2, v => new(v[0], v[1]), v => [v.X, v.Y]),
        new VectorJson<Vector3>(3, v => new(v[0], v[1], v[2]), v => [v.X, v.Y, v.Z]),
        new VectorJson<Vector4>(4, v => new(v[0], v[1], v[2], v[3]), v => [v.X, v.Y, v.Z, v.W]),
        new VectorJson<Quaternion>(4, v => new(v[0], v[1], v[2], v[3]), v => [v.X, v.Y, v.Z, v.W]),
    ];

    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"expected an array of {count} numbers");
        var v = new float[count];
        var n = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (n == count) throw new JsonException($"expected {count} numbers, got more");
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out v[n]) || !float.IsFinite(v[n]))
                throw new JsonException($"element {n} must be a finite number");
            n++;
        }
        if (n != count) throw new JsonException($"expected {count} numbers, got {n}");
        return make(v);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var x in parts(value))
        {
            if (!float.IsFinite(x)) throw new ArgumentException("a vector component isn't a finite number, so the command can't be recorded");
            writer.WriteNumberValue(x);
        }
        writer.WriteEndArray();
    }
}
