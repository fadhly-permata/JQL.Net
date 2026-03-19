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
        if (string.IsNullOrWhiteSpace(value: rawQuery))
            throw new ArgumentException(message: "Query string cannot be null or empty.");

        RawQuery = rawQuery.Replace(oldValue: Environment.NewLine, newValue: " ").Trim();

        Select = GetSection(query: RawQuery, startKey: "SELECT", endKeys: ["FROM"])
            ?.Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries)
            .Select(selector: static s => s.Trim())
            .ToArray();

        From =
            GetSection(
                query: RawQuery,
                startKey: "FROM",
                endKeys: ["JOIN", "WHERE", "GROUP BY", "ORDER BY"]
            )
                ?.Trim() ?? "$";

        Join = GetSections(
            query: RawQuery,
            key: "JOIN",
            endKeys: ["WHERE", "GROUP BY", "ORDER BY"]
        );

        Conditions = GetSection(
            query: RawQuery,
            startKey: "WHERE",
            endKeys: ["GROUP BY", "ORDER BY"]
        )
            ?.Split(
                separator: [" AND ", " OR ", " and ", " or "],
                options: StringSplitOptions.RemoveEmptyEntries
            )
            .Select(selector: static s => s.Trim())
            .ToArray();

        GroupBy = GetSection(query: RawQuery, startKey: "GROUP BY", endKeys: ["HAVING", "ORDER BY"])
            ?.Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries)
            .Select(selector: static s => s.Trim())
            .ToArray();

        Having = GetSection(query: RawQuery, startKey: "HAVING", endKeys: ["ORDER BY"])
            ?.Split(
                separator: [" AND ", " OR ", " and ", " or "],
                options: StringSplitOptions.RemoveEmptyEntries
            )
            .Select(selector: static s => s.Trim())
            .ToArray();

        Order = GetSection(query: RawQuery, startKey: "ORDER BY", endKeys: null)
            ?.Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries)
            .Select(selector: static s => s.Trim())
            .ToArray();

        return this;
    }

    private static string? GetSection(string query, string startKey, string[]? endKeys)
    {
        var startIndex = query.IndexOf(
            value: startKey,
            comparisonType: StringComparison.OrdinalIgnoreCase
        );
        if (startIndex == -1)
            return null;

        startIndex += startKey.Length;
        var endIndex = query.Length;

        if (endKeys != null)
            foreach (var key in endKeys)
            {
                int index = query.IndexOf(
                    value: $" {key} ",
                    startIndex: startIndex,
                    comparisonType: StringComparison.OrdinalIgnoreCase
                );
                if (index != -1 && index < endIndex)
                    endIndex = index;
            }

        return query[startIndex..endIndex].Trim();
    }

    private static string[]? GetSections(string query, string key, string[]? endKeys)
    {
        var results = new List<string>();
        var currentIndex = 0;
        while (
            (
                currentIndex = query.IndexOf(
                    value: key,
                    startIndex: currentIndex,
                    comparisonType: StringComparison.OrdinalIgnoreCase
                )
            ) != -1
        )
        {
            int startIndex = currentIndex + key.Length;
            int endIndex = query.Length;
            endIndex = LocateSectionEnd(
                query: query,
                endKeys: endKeys,
                startIndex: startIndex,
                endIndex: endIndex
            );
            results.Add(item: query[startIndex..endIndex].Trim());
            currentIndex = startIndex;
        }
        return results.Count > 0 ? [.. results] : null;

        static int LocateSectionEnd(string query, string[]? endKeys, int startIndex, int endIndex)
        {
            if (endKeys != null)
            {
                foreach (var eKey in endKeys)
                {
                    int index = query.IndexOf(
                        value: $" {eKey} ",
                        startIndex: startIndex,
                        comparisonType: StringComparison.OrdinalIgnoreCase
                    );
                    if (index != -1 && index < endIndex)
                        endIndex = index;
                }
                int nextJoin = query.IndexOf(
                    value: " JOIN ",
                    startIndex: startIndex,
                    comparisonType: StringComparison.OrdinalIgnoreCase
                );
                if (nextJoin != -1 && nextJoin < endIndex)
                    endIndex = nextJoin;
            }

            return endIndex;
        }
    }
}
