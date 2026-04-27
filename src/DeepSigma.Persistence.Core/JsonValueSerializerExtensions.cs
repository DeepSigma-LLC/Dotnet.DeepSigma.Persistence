using System.Text;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Convenience helpers for SQL backends that store the serializer's UTF-8 byte output as TEXT.
/// </summary>
public static class JsonValueSerializerExtensions
{
    /// <summary>
    /// Serializes the specified value to a JSON string using the provided serializer.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="serializer">The JSON value serializer to use for serialization. Cannot be null.</param>
    /// <param name="value">The value to serialize to a JSON string.</param>
    /// <returns>A string containing the JSON representation of the specified value.</returns>
    public static string SerializeToString<T>(this IJsonValueSerializer serializer, T value) =>
        Encoding.UTF8.GetString(serializer.Serialize(value));

    /// <summary>
    /// Deserializes the specified JSON string into an object of type T using the provided serializer.
    /// </summary>
    /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
    /// <param name="serializer">The JSON value serializer used to perform the deserialization.</param>
    /// <param name="data">The JSON string to deserialize. Must be a valid UTF-8 encoded JSON representation of an object of type T.</param>
    /// <returns>An instance of type T deserialized from the specified JSON string, or null if the input is null or empty.</returns>
    public static T? DeserializeFromString<T>(this IJsonValueSerializer serializer, string data) =>
        serializer.Deserialize<T>(Encoding.UTF8.GetBytes(data));
}
