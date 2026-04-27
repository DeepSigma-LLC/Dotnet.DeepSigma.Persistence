using System.Text.Json;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Default <see cref="IJsonValueSerializer"/> implementation, backed by <see cref="JsonSerializer"/>.
/// </summary>
/// <remarks>
/// For AOT/trimming scenarios, pass a <see cref="JsonSerializerOptions"/> instance whose
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> is set to a source-generated
/// <c>JsonSerializerContext</c>. This eliminates the reflection-based metadata lookup the
/// default resolver performs and lets the serializer participate in trimming.
/// </remarks>
public sealed class JsonValueSerializer : IJsonValueSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the JsonValueSerializer class with default settings.
    /// </summary>
    public JsonValueSerializer() : this(null) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonValueSerializer"/> class with the specified <see cref="JsonSerializerOptions"/>.
    /// </summary>
    /// <param name="options"></param>
    public JsonValueSerializer(JsonSerializerOptions? options)
    {
        _options = options ?? JsonSerializerOptions.Default;
    }

    /// <inheritdoc/>
    public byte[] Serialize<T>(T value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, _options);
        }
        catch (Exception ex)
        {
            throw new SerializationException($"Failed to serialize value of type {typeof(T).Name}.", ex);
        }
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (Exception ex)
        {
            throw new SerializationException($"Failed to deserialize value of type {typeof(T).Name}.", ex);
        }
    }
}
