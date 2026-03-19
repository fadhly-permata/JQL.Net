using Newtonsoft.Json.Linq;

namespace JQL.Net;

/// <summary>
///     An object that represents a SQL-like request.
/// </summary>
public class JsonQueryRequest
{
    public string From { get; set; } = "$";
    public string[]? Select { get; set; }
    public string[]? Conditions { get; set; }
    public string[]? Order { get; set; }
    public string[]? GroupBy { get; set; }
    public string[]? Having { get; set; }
    public string[]? Join { get; set; }
    public JObject? Data { get; set; }
    public string? RawQuery { get; set; }

    /// <summary>
    ///     Parse a SQL-like query into a JsonQueryRequest object.
    /// </summary>
    /// <param name="rawQuery">
    ///     A SQL-like query.
    /// </param>
    /// <returns>
    ///     A JsonQueryRequest object.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="rawQuery" /> is null or empty.
    /// </exception>
    public JsonQueryRequest Parse(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
            throw new ArgumentException("Query string cannot be null or empty.");

        RawQuery = rawQuery.Replace(Environment.NewLine, " ").Trim();

        Select = GetSection(RawQuery, "SELECT", ["FROM"])
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        From =
            GetSection(RawQuery, "FROM", ["JOIN", "WHERE", "GROUP BY", "ORDER BY"])?.Trim() ?? "$";

        Join = GetSections(RawQuery, "JOIN", ["WHERE", "GROUP BY", "ORDER BY"]);

        Conditions = GetSection(RawQuery, "WHERE", ["GROUP BY", "ORDER BY"])
            ?.Split([" AND ", " OR ", " and ", " or "], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        GroupBy = GetSection(RawQuery, "GROUP BY", ["HAVING", "ORDER BY"])
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        Having = GetSection(RawQuery, "HAVING", ["ORDER BY"])
            ?.Split([" AND ", " OR ", " and ", " or "], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        Order = GetSection(RawQuery, "ORDER BY", null)
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        return this;
    }

    private static string? GetSection(string query, string startKey, string[]? endKeys)
    {
        int startIndex = query.IndexOf(startKey, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
            return null;

        startIndex += startKey.Length;
        int endIndex = query.Length;

        if (endKeys != null)
        {
            foreach (var key in endKeys)
            {
                int index = query.IndexOf(
                    $" {key} ",
                    startIndex,
                    StringComparison.OrdinalIgnoreCase
                );
                if (index != -1 && index < endIndex)
                    endIndex = index;
            }
        }
        return query.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static string[]? GetSections(string query, string key, string[]? endKeys)
    {
        var results = new List<string>();
        int currentIndex = 0;
        while (
            (currentIndex = query.IndexOf(key, currentIndex, StringComparison.OrdinalIgnoreCase))
            != -1
        )
        {
            int startIndex = currentIndex + key.Length;
            int endIndex = query.Length;
            if (endKeys != null)
            {
                foreach (var eKey in endKeys)
                {
                    int index = query.IndexOf(
                        $" {eKey} ",
                        startIndex,
                        StringComparison.OrdinalIgnoreCase
                    );
                    if (index != -1 && index < endIndex)
                        endIndex = index;
                }
                int nextJoin = query.IndexOf(
                    " JOIN ",
                    startIndex,
                    StringComparison.OrdinalIgnoreCase
                );
                if (nextJoin != -1 && nextJoin < endIndex)
                    endIndex = nextJoin;
            }
            results.Add(query.Substring(startIndex, endIndex - startIndex).Trim());
            currentIndex = startIndex;
        }
        return results.Count > 0 ? results.ToArray() : null;
    }
}
