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

    public JsonValueSerializer() : this(null) { }

    public JsonValueSerializer(JsonSerializerOptions? options)
    {
        _options = options ?? JsonSerializerOptions.Default;
    }

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
