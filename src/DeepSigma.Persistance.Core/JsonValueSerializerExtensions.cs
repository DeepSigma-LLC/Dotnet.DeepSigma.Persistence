using System.Text;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Convenience helpers for SQL backends that store the serializer's UTF-8 byte output as TEXT.
/// </summary>
public static class JsonValueSerializerExtensions
{
    public static string SerializeToString<T>(this IJsonValueSerializer serializer, T value) =>
        Encoding.UTF8.GetString(serializer.Serialize(value));

    public static T? DeserializeFromString<T>(this IJsonValueSerializer serializer, string data) =>
        serializer.Deserialize<T>(Encoding.UTF8.GetBytes(data));
}
