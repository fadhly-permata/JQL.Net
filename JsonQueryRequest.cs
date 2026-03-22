using JQL.Net.Exceptions;
using Newtonsoft.Json.Linq;

namespace JQL.Net;

/// <summary>
///     An object that represents a SQL-like request.
/// </summary>
public class JsonQueryRequest
{
    /// <summary>
    ///     The path to the data to query.
    /// </summary>
    public string From { get; set; } = "$";

    /// <summary>
    ///     The fields to select.
    /// </summary>
    public string[]? Select { get; set; }

    /// <summary>
    ///     The conditions to apply.
    /// </summary>
    public string[]? Conditions { get; set; }

    /// <summary>
    ///     The order to apply.
    /// </summary>
    public string[]? Order { get; set; }

    /// <summary>
    ///     The fields to group by.
    /// </summary>
    public string[]? GroupBy { get; set; }

    /// <summary>
    ///     The having conditions to apply.
    /// </summary>
    public string[]? Having { get; set; }

    /// <summary>
    ///     The joins to apply.
    /// </summary>
    public string[]? Join { get; set; }

    /// <summary>
    ///     The data to query.
    /// </summary>
    public JObject? Data { get; set; }

    /// <summary>
    ///     The raw SQL-like query.
    /// </summary>
    public string? RawQuery { get; set; }

    /// <summary>
    ///     Whether to return distinct results.
    /// </summary>
    public bool Distinct { get; set; }

    /// <summary>
    ///     Parse a SQL-like query into a JsonQueryRequest object.
    /// </summary>
    /// <returns>
    ///     A JsonQueryRequest object.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <see cref="RawQuery" /> is null or empty.
    /// </exception>
    public JsonQueryRequest Parse()
    {
        if (string.IsNullOrWhiteSpace(RawQuery))
            throw new ArgumentException("Query string cannot be null or empty.");

        return Parse(RawQuery);
    }

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

        if (RawQuery.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase))
        {
            Distinct = true;
            RawQuery = RawQuery.Replace("DISTINCT", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        Select = GetSection(RawQuery, "SELECT", ["FROM"])
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .ToArray();

        From =
            GetSection(RawQuery, "FROM", ["JOIN", "WHERE", "GROUP BY", "ORDER BY"])?.Trim() ?? "$";

        if (string.IsNullOrWhiteSpace(From))
            throw new JsonQueryException("FROM clause cannot be empty");

        Join = GetSections(RawQuery, "JOIN", ["WHERE", "GROUP BY", "ORDER BY"]);

        // Perbaikan: WHERE split lebih sensitif terhadap spasi
        Conditions = GetSection(RawQuery, "WHERE", ["GROUP BY", "ORDER BY"])
            ?.Split([" AND ", " OR ", " and ", " or "], StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .ToArray();

        GroupBy = GetSection(RawQuery, "GROUP BY", ["HAVING", "ORDER BY"])
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .ToArray();

        Having = GetSection(RawQuery, "HAVING", ["ORDER BY"])
            ?.Split([" AND ", " OR ", " and ", " or "], StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .ToArray();

        Order = GetSection(RawQuery, "ORDER BY", null)
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .ToArray();

        return this;
    }

    private static string? GetSection(string query, string startKey, string[]? endKeys)
    {
        var startIndex = query.IndexOf(startKey, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
            return null;

        startIndex += startKey.Length;
        var endIndex = query.Length;

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

        return query[startIndex..endIndex].Trim();
    }

    private static string[]? GetSections(string query, string key, string[]? endKeys)
    {
        var results = new List<string>();
        var currentIndex = 0;
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
            }
            results.Add(query[startIndex..endIndex].Trim());
            currentIndex = startIndex;
        }
        return results.Count > 0 ? [.. results] : null;
    }
}
