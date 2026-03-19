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
            return Enumerable.Empty<JToken>();

        try
        {
            var fromParts = request.From.Split(
                [" AS ", " as "],
                StringSplitOptions.RemoveEmptyEntries
            );
            string fromPath = fromParts[0].Trim();
            string fromAlias = fromParts.Length > 1 ? fromParts[1].Trim() : string.Empty;

            JToken? targetToken = GetSourceToken(request.Data, fromPath);
            if (targetToken == null)
                return Enumerable.Empty<JToken>();

            IEnumerable<JToken> query = targetToken is JArray array ? array : [targetToken];

            if (!string.IsNullOrEmpty(fromAlias))
            {
                query = query.Select(item =>
                {
                    var wrapped = new JObject { [fromAlias] = item.DeepClone() };
                    if (item is JObject jo)
                        wrapped.Merge(jo);
                    return (JToken)wrapped;
                });
            }

            if (request.Join != null)
                foreach (var joinStmt in request.Join)
                    query = ApplyJoin(request.Data, query, joinStmt);

            if (request.Conditions?.Length > 0)
                query = query.Where(item =>
                    EvaluateConditions(request.Data, item, request.Conditions)
                );

            if (request.GroupBy?.Length > 0)
            {
                query = ProcessGrouping(request.Data, query, request);
                if (request.Having != null)
                    query = query.Where(item =>
                        EvaluateConditions(request.Data, item, request.Having)
                    );
            }
            else if (request.Select?.Length > 0)
            {
                query = query.Select(item => ProjectItem(request.Data, item, request.Select));
            }

            if (request.Order != null)
                query = ApplyOrdering(query, request.Order);

            return query;
        }
        catch (Exception ex)
        {
            throw new JsonQueryException(
                "Error executing JSON query. Check inner exception for details.",
                ex
            );
        }
    }

    private static JToken? GetSourceToken(JObject root, string path)
    {
        if (path == "$")
            return root;
        if (path.StartsWith("$."))
        {
            string cleanPath = path[2..];
            return root.SelectToken(cleanPath) ?? root[cleanPath];
        }
        return root.SelectToken(path) ?? root[path];
    }

    private static IEnumerable<JToken> ApplyJoin(
        JObject root,
        IEnumerable<JToken> mainQuery,
        string joinStatement
    )
    {
        var onParts = joinStatement.Split([" ON ", " on "], StringSplitOptions.RemoveEmptyEntries);
        if (onParts.Length < 2)
            return mainQuery;

        var tableParts = onParts[0]
            .Trim()
            .Split([" AS ", " as "], StringSplitOptions.RemoveEmptyEntries);
        string joinTablePath = tableParts[0].Trim();
        string rightAlias =
            tableParts.Length > 1 ? tableParts[1].Trim() : joinTablePath.Replace("$.", "");

        JToken? rightToken = GetSourceToken(root, joinTablePath);
        var rightArray = rightToken is JArray ja
            ? ja
            : (rightToken != null ? new JArray(rightToken) : null);
        if (rightArray == null)
            return mainQuery;

        string fullOnCondition = onParts[1].Trim();

        return mainQuery.Select(leftItem =>
        {
            var match = rightArray.FirstOrDefault(rightItem =>
            {
                JObject joinContext = new JObject { [rightAlias] = rightItem.DeepClone() };
                if (leftItem is JObject jo)
                    joinContext.Merge(jo);

                var joinConditions = fullOnCondition
                    .Split(
                        [" AND ", " OR ", " and ", " or "],
                        StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(c => c.Trim())
                    .ToArray();

                return EvaluateConditions(root, joinContext, joinConditions);
            });

            if (match == null)
                return leftItem;

            JObject combined = leftItem is JObject jo ? (JObject)jo.DeepClone() : new JObject();
            combined[rightAlias] = match.DeepClone();
            return (JToken)combined;
        });
    }

    private static JToken ProjectItem(JObject root, JToken item, string[] select)
    {
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

            projectedObj[aliasField] = GetTokenValue(root, item, sourceField);
        }
        return projectedObj;
    }

    private static JToken? GetTokenValue(JObject root, JToken item, string path)
    {
        if (path == "$")
            return root;
        if (path.StartsWith("$."))
            return root.SelectToken(path[2..]);
        return item.SelectToken(path) ?? item[path];
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

            bool currentCondResult = EvaluateSingleCondition(root, item, condition);
            if (currentOperator == "OR")
                finalResult = finalResult || currentCondResult;
            else if (currentOperator == "AND")
                finalResult = finalResult && currentCondResult;
        }
        return finalResult;
    }

    private static bool EvaluateSingleCondition(JObject root, JToken item, string condition)
    {
        var parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        string property = parts[0];
        string op = parts[1];
        string rawValue = parts[2];

        JToken? leftVal = GetTokenValue(root, item, property);
        if (leftVal == null)
            return false;

        JToken? rightVal =
            (rawValue.StartsWith('\'') && rawValue.EndsWith('\''))
                ? rawValue.Trim('\'')
                : GetTokenValue(root, item, rawValue) ?? rawValue;

        if (rightVal == null)
            return false;

        string s1 = leftVal.ToString();
        string s2 = rightVal.ToString();

        if (double.TryParse(s1, out double n1) && double.TryParse(s2, out double n2))
            return op switch
            {
                "==" => n1 == n2,
                "!=" => n1 != n2,
                ">" => n1 > n2,
                "<" => n1 < n2,
                ">=" => n1 >= n2,
                "<=" => n1 <= n2,
                _ => false,
            };

        return op switch
        {
            "==" => s1.Equals(s2, StringComparison.OrdinalIgnoreCase),
            "!=" => !s1.Equals(s2, StringComparison.OrdinalIgnoreCase),
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
                string cleanAlias = key.Contains('.')
                    ? key.Split('.').Last()
                    : key.Replace("$.", "");
                resultObj[cleanAlias] = GetTokenValue(root, group.First(), key);
            }

            if (request.Select != null)
            {
                foreach (var selection in request.Select)
                {
                    var parts = selection.Split(
                        [" AS ", " as "],
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    string expression = parts[0].Trim();
                    string alias =
                        parts.Length > 1 ? parts[1].Trim() : expression.Replace("$.", "");

                    if (expression.Contains('(') && expression.Contains(')'))
                        resultObj[alias] = CalculateAggregate(group, expression);
                    else if (!request.GroupBy!.Contains(expression))
                        resultObj[alias] = GetTokenValue(root, group.First(), expression);
                }
            }
            return (JToken)resultObj;
        });
    }

    private static IEnumerable<JToken> ApplyOrdering(IEnumerable<JToken> query, string[] order)
    {
        IOrderedEnumerable<JToken>? orderedQuery = null;
        for (int i = 0; i < order.Length; i++)
        {
            string column = order[i];
            if (i == 0)
                orderedQuery = query.OrderBy(item => item.SelectToken(column) ?? item[column]);
            else
                orderedQuery = orderedQuery!.ThenBy(item =>
                    item.SelectToken(column) ?? item[column]
                );
        }
        return orderedQuery ?? query;
    }

    private static JToken CalculateAggregate(IEnumerable<JToken> group, string expression)
    {
        var openParen = expression.IndexOf('(');
        var closeParen = expression.IndexOf(')');
        if (openParen == -1 || closeParen == -1)
            return JValue.CreateNull();

        string func = expression[..openParen].ToUpper();
        string field = expression.Substring(openParen + 1, closeParen - openParen - 1);

        var values = group
            .Select(item => GetTokenValue(null!, item, field))
            .Where(v => v != null && v.Type != JTokenType.Null);
        if (!values.Any())
            return 0;

        return func switch
        {
            "SUM" => values.Sum(v => (double)v),
            "COUNT" => values.Count(),
            "AVG" => values.Average(v => (double)v),
            "MIN" => values.Min(v => (double)v),
            "MAX" => values.Max(v => (double)v),
            _ => JValue.CreateNull(),
        };
    }
}
