using JQL.Net.Exceptions;
using Newtonsoft.Json.Linq;

namespace JQL.Net.Core;

/// <summary>
///     Executes a SQL-like query against a JObject
/// </summary>
public static class JsonQueryEngine
{
    /// <summary>
    ///     Executes a SQL-like query against a JObject
    /// </summary>
    /// <param name="request">
    ///     The SQL-like query
    /// </param>
    /// <returns>
    ///     The results of the query
    /// </returns>
    /// <exception cref="JsonQueryException">
    ///     Thrown when <paramref name="request" /> is null
    /// </exception>
    public static IEnumerable<JToken> Execute(JsonQueryRequest request)
    {
        if (request.Data == null)
            return [];

        try
        {
            var fromParts = request.From.Split(
                [" AS ", " as "],
                StringSplitOptions.RemoveEmptyEntries
            );
            var fromPath = fromParts[0].Trim();
            var fromAlias = fromParts.Length > 1 ? fromParts[1].Trim() : string.Empty;

            var targetToken = GetSourceToken(request.Data, fromPath);
            if (targetToken == null)
                return [];

            IEnumerable<JToken> query = targetToken is JArray array ? array : [targetToken];
            query = PerformAliasing(fromAlias: fromAlias, query: query);
            query = PerformJoin(request: request, query: query);
            query = PerformWhere(request: request, query: query);
            query = PerformGrouping(request: request, query: query);
            query = PerformSorting(request: request, query: query);

            var result = query;

            // Apply DISTINCT if requested
            if (request.Distinct)
                result = result.Distinct(new JTokenEqualityComparer());

            return result;
        }
        catch (Exception ex)
        {
            throw new JsonQueryException(
                "Error executing JSON query. Check inner exception for details.",
                ex
            );
        }

        static IEnumerable<JToken> PerformAliasing(string fromAlias, IEnumerable<JToken> query)
        {
            if (!string.IsNullOrEmpty(fromAlias))
                query = query.Select(item =>
                {
                    var wrapped = new JObject { [fromAlias] = item.DeepClone() };
                    if (item is JObject jo)
                        wrapped.Merge(content: jo);

                    return (JToken)wrapped;
                });

            return query;
        }

        static IEnumerable<JToken> PerformJoin(JsonQueryRequest request, IEnumerable<JToken> query)
        {
            if (request.Join != null)
                foreach (var joinStmt in request.Join)
                    query = ApplyJoin(
                        root: request.Data!,
                        mainQuery: query,
                        joinStatement: joinStmt
                    );

            return query;
        }

        static IEnumerable<JToken> PerformWhere(JsonQueryRequest request, IEnumerable<JToken> query)
        {
            if (request.Conditions?.Length > 0)
                query = query.Where(predicate: item =>
                    EvaluateConditions(
                        root: request.Data!,
                        item: item,
                        conditions: request.Conditions
                    )
                );

            return query;
        }

        static IEnumerable<JToken> PerformGrouping(
            JsonQueryRequest request,
            IEnumerable<JToken> query
        )
        {
            if (request.GroupBy?.Length > 0)
            {
                query = ProcessGrouping(root: request.Data!, query: query, request: request);

                if (request.Having != null)
                    query = query.Where(predicate: item =>
                        EvaluateConditions(
                            root: request.Data!,
                            item: item,
                            conditions: request.Having
                        )
                    );
            }
            else if (request.Select?.Length > 0)
            {
                query = query.Select(selector: item =>
                    ProjectItem(root: request.Data!, item: item, select: request.Select)
                );
            }

            return query;
        }

        static IEnumerable<JToken> PerformSorting(
            JsonQueryRequest request,
            IEnumerable<JToken> query
        )
        {
            if (request.Order != null)
                query = ApplyOrdering(query: query, order: request.Order);

            return query;
        }
    }

    private static JToken? GetSourceToken(JObject root, string path)
    {
        if (path == "$")
            return root;

        if (path.StartsWith(value: "$."))
        {
            var cleanPath = path[2..];
            return root.SelectToken(path: cleanPath) ?? root[cleanPath];
        }

        return root.SelectToken(path: path) ?? root[path];
    }

    private static IEnumerable<JToken> ApplyJoin(
        JObject root,
        IEnumerable<JToken> mainQuery,
        string joinStatement
    )
    {
        var onParts = joinStatement.Split(
            separator: [" ON ", " on "],
            options: StringSplitOptions.RemoveEmptyEntries
        );
        if (onParts.Length < 2)
            return mainQuery;

        var tableParts = onParts[0]
            .Trim()
            .Split(separator: [" AS ", " as "], options: StringSplitOptions.RemoveEmptyEntries);
        var joinTablePath = tableParts[0].Trim();
        var rightAlias =
            tableParts.Length > 1
                ? tableParts[1].Trim()
                : joinTablePath.Replace(oldValue: "$.", newValue: "");

        JToken? rightToken = GetSourceToken(root: root, path: joinTablePath);
        var rightArray = rightToken switch
        {
            JArray ja => ja,
            not null => new JArray(rightToken),
            _ => null,
        };

        if (rightArray == null)
            return mainQuery;

        string fullOnCondition = onParts[1].Trim();

        return mainQuery.Select(selector: leftItem =>
        {
            var match = rightArray.FirstOrDefault(predicate: rightItem =>
            {
                JObject joinContext = new() { [rightAlias] = rightItem.DeepClone() };
                if (leftItem is JObject jo)
                    joinContext.Merge(content: jo);

                var joinConditions = fullOnCondition
                    .Split(
                        separator: [" AND ", " OR ", " and ", " or "],
                        options: StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(selector: c => c.Trim())
                    .ToArray();

                return EvaluateConditions(
                    root: root,
                    item: joinContext,
                    conditions: joinConditions
                );
            });

            if (match == null)
                return leftItem;

            JObject combined = leftItem is JObject jo ? (JObject)jo.DeepClone() : [];
            combined[rightAlias] = match.DeepClone();
            return combined;
        });
    }

    private static JToken ProjectItem(JObject root, JToken item, string[] select)
    {
        // Handle khusus untuk SELECT $.*
        if (select.Length == 1 && select[0] == "$.*")
        {
            // Jika item adalah JObject, kembalikan langsung
            if (item is JObject)
                return item.DeepClone();

            // Jika item punya nilai tunggal, kembalikan nilai tersebut
            return item;
        }

        // Handle khusus untuk SELECT *
        if (select.Length == 1 && select[0] == "*")
        {
            // Jika item adalah JObject, kembalikan semua properti
            if (item is JObject jObj)
            {
                var result = new JObject();
                foreach (var prop in jObj.Properties())
                {
                    result[prop.Name] = prop.Value.DeepClone();
                }
                return result;
            }

            return item;
        }

        // Proyeksi normal untuk kolom spesifik
        JObject projectedObj = [];
        foreach (var selection in select)
        {
            var parts = selection.Split([" AS ", " as "], StringSplitOptions.RemoveEmptyEntries);
            string sourceField = parts[0].Trim();
            string aliasField =
                parts.Length > 1
                    ? parts[1].Trim()
                    : (
                        sourceField.Contains('.')
                            ? sourceField.Split('.').Last()
                            : sourceField.Replace("$.", "")
                    );

            projectedObj[aliasField] =
                GetTokenValue(root, item, sourceField)?.DeepClone() ?? JValue.CreateNull();
        }
        return projectedObj;
    }

    private static string GetAliasField(string[] parts, string sourceField)
    {
        if (parts.Length > 1)
            return parts[1].Trim();

        return sourceField.Contains(value: '.')
            ? sourceField.Split(separator: '.')[^1]
            : sourceField.Replace(oldValue: "$.", newValue: "");
    }

    private static JToken? GetTokenValue(JObject root, JToken item, string path)
    {
        if (path == "$")
            return root;
        if (path.StartsWith(value: "$."))
            return root.SelectToken(path: path[2..]);
        return item.SelectToken(path: path) ?? item[path];
    }

    private static bool EvaluateConditions(JObject root, JToken item, string[] conditions)
    {
        if (conditions.Length == 0)
            return true;
        bool finalResult = false;
        string currentOperator = "OR";

        foreach (var condition in conditions)
        {
            string upperCond = condition.Trim().ToUpper();
            if (upperCond is "AND" or "OR")
            {
                currentOperator = upperCond;
                continue;
            }

            bool currentCondResult = EvaluateSingleCondition(
                root: root,
                item: item,
                condition: condition
            );
            if (currentOperator == "OR")
                finalResult = finalResult || currentCondResult;
            else if (currentOperator == "AND")
                finalResult = finalResult && currentCondResult;
        }
        return finalResult;
    }

    private static bool EvaluateSingleCondition(JObject root, JToken item, string condition)
    {
        var parts = condition.Split(separator: ' ', options: StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        string property = parts[0];
        string op = parts[1];
        string rawValue = parts[2];

        JToken? leftVal = GetTokenValue(root: root, item: item, path: property);
        if (leftVal == null)
            return false;

        JToken? rightVal =
            (rawValue.StartsWith(value: '\'') && rawValue.EndsWith(value: '\''))
                ? rawValue.Trim(trimChar: '\'')
                : GetTokenValue(root: root, item: item, path: rawValue) ?? rawValue;

        if (rightVal == null)
            return false;

        string s1 = leftVal.ToString();
        string s2 = rightVal.ToString();

        if (
            double.TryParse(s: s1, result: out double n1)
            && double.TryParse(s: s2, result: out double n2)
        )
        {
            const double Epsilon = 0.0000001;

            return op switch
            {
                "==" => Math.Abs(n1 - n2) < Epsilon,
                "!=" => Math.Abs(n1 - n2) >= Epsilon,
                ">" => n1 > n2 + Epsilon,
                "<" => n1 < n2 - Epsilon,
                ">=" => n1 + Epsilon >= n2,
                "<=" => n1 <= n2 + Epsilon,
                _ => false,
            };
        }

        return op switch
        {
            "==" => s1.Equals(value: s2, comparisonType: StringComparison.OrdinalIgnoreCase),
            "!=" => !s1.Equals(value: s2, comparisonType: StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static IEnumerable<JToken> ProcessGrouping(
        JObject root,
        IEnumerable<JToken> query,
        JsonQueryRequest request
    )
    {
        var groupedData = query.GroupBy(item =>
            string.Join(
                "-",
                request.GroupBy!.Select(g => GetTokenValue(root, item, g)?.ToString() ?? "")
            )
        );

        return groupedData.Select(group =>
        {
            JObject resultObj = [];
            foreach (var key in request.GroupBy!)
            {
                resultObj[
                    key.Contains(value: '.')
                        ? key.Split(separator: '.')[^1]
                        : key.Replace(oldValue: "$.", newValue: "")
                ] = GetTokenValue(root: root, item: group.First(), path: key);
            }

            if (request.Select != null)
                ProcessGroupSelections(
                    root: root,
                    request: request,
                    group: group,
                    resultObj: resultObj
                );

            return (JToken)resultObj;

            static void ProcessGroupSelections(
                JObject root,
                JsonQueryRequest request,
                IGrouping<string, JToken> group,
                JObject resultObj
            )
            {
                if (request.Select is not null)
                    foreach (var selection in request.Select)
                    {
                        var parts = selection.Split(
                            separator: [" AS ", " as "],
                            options: StringSplitOptions.RemoveEmptyEntries
                        );
                        var expression = parts[0].Trim();
                        var alias =
                            parts.Length > 1
                                ? parts[1].Trim()
                                : expression.Replace(oldValue: "$.", newValue: "");

                        if (expression.Contains(value: '(') && expression.Contains(value: ')'))
                            resultObj[alias] = CalculateAggregate(
                                group: group,
                                expression: expression
                            );
                        else if (!request.GroupBy!.Contains(value: expression))
                            resultObj[alias] = GetTokenValue(
                                root: root,
                                item: group.First(),
                                path: expression
                            );
                    }
            }
        });
    }

    private static IEnumerable<JToken> ApplyOrdering(IEnumerable<JToken> query, string[] order)
    {
        IOrderedEnumerable<JToken>? orderedQuery = null;

        foreach (var orderClause in order)
        {
            // Split untuk memisahkan nama kolom dan arah pengurutan
            var parts = orderClause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string column = parts[0].Trim();
            bool isDescending =
                parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

            if (orderedQuery == null)
            {
                orderedQuery = isDescending
                    ? query.OrderByDescending(item => GetOrderValue(item, column))
                    : query.OrderBy(item => GetOrderValue(item, column));
            }
            else
            {
                orderedQuery = isDescending
                    ? orderedQuery.ThenByDescending(item => GetOrderValue(item, column))
                    : orderedQuery.ThenBy(item => GetOrderValue(item, column));
            }
        }

        return orderedQuery ?? query;
    }

    private static object? GetOrderValue(JToken item, string path)
    {
        try
        {
            // Coba ambil nilai sebagai angka dulu
            var token = item.SelectToken(path) ?? item[path];
            if (token == null)
                return null;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<double>();

            return token.ToString();
        }
        catch
        {
            return item[path]?.ToString();
        }
    }

    private static JToken CalculateAggregate(IEnumerable<JToken> group, string expression)
    {
        var openParen = expression.IndexOf(value: '(');
        var closeParen = expression.IndexOf(value: ')');
        if (openParen == -1 || closeParen == -1)
            return JValue.CreateNull();

        string func = expression[..openParen].ToUpper();
        string field = expression.Substring(
            startIndex: openParen + 1,
            length: closeParen - openParen - 1
        );

        var values = group
            .Select(selector: item => GetTokenValue(root: null!, item: item, path: field))
            .Where(predicate: v => v != null && v.Type != JTokenType.Null);
        if (!values.Any())
            return 0;

        return func switch
        {
            "SUM" => values.Sum(selector: v => ConvertToDecimalSafe(token: v) ?? 0),
            "COUNT" => values.Count(predicate: v => v is not null),
            "AVG" => values
                .Select(selector: v => ConvertToDecimalSafe(token: v))
                .Where(predicate: v => v.HasValue)
                .Average(selector: v => v!.Value),
            "MIN" => values
                .Select(selector: v => ConvertToDecimalSafe(token: v))
                .Where(predicate: v => v.HasValue)
                .Min(selector: v => v!.Value),
            "MAX" => values
                .Select(selector: v => ConvertToDecimalSafe(token: v))
                .Where(predicate: v => v.HasValue)
                .Max(selector: v => v!.Value),
            _ => JValue.CreateNull(),
        };

        static decimal? ConvertToDecimalSafe(JToken? token)
        {
            if (token == null)
                return null;

            try
            {
                return token.Type switch
                {
                    JTokenType.Integer => token.ToObject<long>(),
                    JTokenType.Float => (decimal)token.ToObject<double>(),
                    JTokenType.String when decimal.TryParse(s: token.ToString(), out var d) => d,
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
