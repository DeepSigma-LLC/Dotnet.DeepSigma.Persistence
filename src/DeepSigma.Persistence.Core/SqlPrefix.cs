namespace DeepSigma.Persistence.Core;

/// <summary>
/// Helpers for building safe LIKE-prefix clauses against SQL backends.
/// Both SQLite and PostgreSQL accept the same ESCAPE syntax with backslash.
/// </summary>
public static class SqlPrefix
{
    /// <summary>Escapes the LIKE wildcards (%, _) and the backslash escape character itself.</summary>
    public static string Escape(string prefix) =>
        prefix.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>
    /// Returns the SQL fragment to append to a WHERE clause and the bound parameter value.
    /// Both are null/empty when prefix is null. The fragment uses parameter name "@Prefix".
    /// </summary>
    /// <param name="prefix">The user-supplied prefix to filter by, or null for no filtering.</param>
    /// <param name="keyColumn">
    /// The column name to filter on, formatted with the target backend's identifier quoting if needed.
    /// Defaults to <c>"key"</c> — works for Postgres (case-folded lowercase) and SQLite. Pass
    /// <c>"[key]"</c> for SQL Server, where <c>KEY</c> is a reserved keyword.
    /// </param>
    public static (string Clause, string? PrefixValue) Build(string? prefix, string keyColumn = "key") =>
        prefix is null
            ? ("", null)
            : ($" AND {keyColumn} LIKE @Prefix ESCAPE '\\'", Escape(prefix) + "%");
}
