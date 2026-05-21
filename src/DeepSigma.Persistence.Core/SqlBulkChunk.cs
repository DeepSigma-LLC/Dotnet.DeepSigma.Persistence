namespace DeepSigma.Persistence.Core;

/// <summary>
/// Default chunk size for the SQL backends' bulk operations
/// (<c>GetManyAsync</c>, <c>SetManyAsync</c>, <c>DeleteManyAsync</c>).
/// </summary>
/// <remarks>
/// Picked to stay well below both providers' bound-parameter ceilings:
/// SQLite defaults to 32766 (configurable per-build), Postgres' wire protocol caps at 65535.
/// A single chunk of 1000 key/value pairs uses ~4000 parameters in the upsert path, leaving
/// generous headroom. Standardised across SQL backends so behaviour is consistent.
/// </remarks>
public static class SqlBulkChunk
{
    /// <summary>The chunk size used by SQL backends for bulk get/set/delete operations.</summary>
    public const int Size = 1000;
}
