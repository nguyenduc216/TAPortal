using System.Text.RegularExpressions;

namespace TAPortal.DbMcp.Security;

public static partial class SqlReadOnlyGuard
{
    private static readonly string[] ForbiddenKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE", "CREATE", "ALTER", "DROP",
        "TRUNCATE", "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE", "DBCC", "KILL", "USE",
        "OPENROWSET", "OPENDATASOURCE", "BULK", "INTO", "WAITFOR"
    ];

    public static string ValidateAndNormalize(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL query is required.", nameof(sql));

        var normalized = StripComments(sql).Trim();
        if (normalized.Length == 0)
            throw new InvalidOperationException("SQL query is empty after removing comments.");

        if (normalized.Contains(';') && normalized.TrimEnd().TrimEnd(';').Contains(';'))
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");

        normalized = normalized.TrimEnd().TrimEnd(';').Trim();

        if (!StartsWithSelectOrCte().IsMatch(normalized))
            throw new InvalidOperationException("Only SELECT statements and CTEs ending in SELECT are allowed.");

        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidOperationException($"Forbidden SQL keyword detected: {keyword}.");
        }

        // Block SQL Server techniques that can invoke side effects or escape the intended database context.
        if (Regex.IsMatch(normalized, @"\bxp_\w+\b|\bsp_\w+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Stored procedure/system procedure references are not allowed in ad-hoc read-only queries.");

        return normalized;
    }

    private static string StripComments(string sql)
    {
        var withoutBlock = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"--.*?$", " ", RegexOptions.Multiline);
    }

    [GeneratedRegex(@"^\s*(SELECT\b|WITH\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartsWithSelectOrCte();
}
