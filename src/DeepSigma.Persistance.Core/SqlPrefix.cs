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
    public static (string Clause, string? PrefixValue) Build(string? prefix) =>
        prefix is null
            ? ("", null)
            : (" AND key LIKE @Prefix ESCAPE '\\'", Escape(prefix) + "%");
}
