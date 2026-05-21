using System.Reflection;
using System.Text;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Loads SQL migrations from an assembly's embedded resources. Backend-agnostic — returns plain
/// <c>(Id, Sql)</c> tuples so this can live in Core without a dependency on the
/// <c>DeepSigma.DataAccess</c> packages. Each provider wraps the result into the runner's
/// <c>Migration</c> type at the call site.
/// </summary>
/// <remarks>
/// Migrations are discovered by a <c>.Migrations.</c> segment in the manifest resource name and a
/// <c>.sql</c> extension. The id is the file stem; <c>{TableName}</c> in the SQL body is replaced
/// with the supplied table name so the same migration file can target a configurable table.
/// </remarks>
public static class MigrationLoader
{
    /// <summary>
    /// Loads all <c>*.sql</c> migration resources from <paramref name="assembly"/>, substituting
    /// <c>{TableName}</c> with <paramref name="tableName"/>. Returned in ordinal id order.
    /// </summary>
    public static IReadOnlyList<(string Id, string Sql)> Load(Assembly assembly, string tableName)
    {
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.") && n.EndsWith(".sql"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ToMigration(assembly, n, tableName))
            .ToList();
    }

    private static (string Id, string Sql) ToMigration(Assembly assembly, string resourceName, string tableName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var sql = new StreamReader(stream, Encoding.UTF8).ReadToEnd().Replace("{TableName}", tableName);
        var id = Path.GetFileNameWithoutExtension(resourceName.Split(".Migrations.")[1]);
        return (id, sql);
    }
}
