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
        return string.IsNullOrWhiteSpace(value: RawQuery)
            ? throw new ArgumentException(message: "Query string cannot be null or empty.")
            : Parse(rawQuery: RawQuery);
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
        if (string.IsNullOrWhiteSpace(value: rawQuery))
            throw new ArgumentException(message: "Query string cannot be null or empty.");

        RawQuery = rawQuery.Replace(oldValue: Environment.NewLine, newValue: " ").Trim();

        if (
            RawQuery.Contains(
                value: "SELECT DISTINCT",
                comparisonType: StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Distinct = true;
            RawQuery = RawQuery
                .Replace(
                    oldValue: "DISTINCT",
                    newValue: "",
                    comparisonType: StringComparison.OrdinalIgnoreCase
                )
                .Trim();
        }

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

        if (string.IsNullOrWhiteSpace(value: From))
            throw new JsonQueryException(message: "FROM clause cannot be empty");

        Join = GetSections(
            query: RawQuery,
            key: "JOIN",
            endKeys: ["WHERE", "GROUP BY", "ORDER BY"]
        );

        // Perbaikan: WHERE split lebih sensitif terhadap spasi
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

        if (endKeys == null)
            return query[startIndex..endIndex].Trim();

        foreach (var key in endKeys)
        {
            var index = query.IndexOf(
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
            var startIndex = currentIndex + key.Length;
            var endIndex = query.Length;
            if (endKeys != null)
            {
                foreach (var eKey in endKeys)
                {
                    var index = query.IndexOf(
                        value: $" {eKey} ",
                        startIndex: startIndex,
                        comparisonType: StringComparison.OrdinalIgnoreCase
                    );
                    if (index != -1 && index < endIndex)
                        endIndex = index;
                }
            }
            results.Add(item: query[startIndex..endIndex].Trim());
            currentIndex = startIndex;
        }
        return results.Count > 0 ? [.. results] : null;
    }
}
