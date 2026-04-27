namespace DeepSigma.Persistence;

/// <summary>
/// Serializes values to and from a JSON-compatible byte sequence.
/// </summary>
/// <remarks>
/// <para>
/// This interface is intentionally JSON-flavored. Implementations are expected to produce and
/// consume bytes that are valid JSON (UTF-8 encoded). Postgres stores values as JSONB which
/// requires JSON, FileSystem uses System.Text.Json directly, and SQLite stores TEXT that is
/// conventionally JSON — so the JSON constraint is implicit across the library.
/// </para>
/// <para>
/// To customize JSON behaviour (camelCase, custom converters, source-generated metadata for
/// AOT/trimming), construct <c>JsonValueSerializer</c> (from <c>DeepSigma.Persistence.Core</c>)
/// with a configured <c>JsonSerializerOptions</c> and register that instance before calling
/// <c>AddSqlitePersistence</c> / <c>AddPostgresPersistence</c>.
/// </para>
/// <para>
/// This library does not currently provide a polymorphism point for non-JSON binary formats
/// (MessagePack, protobuf). If that need arises, a separate typed interface will be added
/// alongside this one rather than reshaping it.
/// </para>
/// </remarks>
public interface IJsonValueSerializer
{
    /// <summary>Serializes <paramref name="value"/> to a UTF-8 JSON byte sequence.</summary>
    byte[] Serialize<T>(T value);

    /// <summary>Deserializes <paramref name="data"/> (UTF-8 JSON) back into <typeparamref name="T"/>.</summary>
    T? Deserialize<T>(byte[] data);
}
