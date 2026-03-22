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

            // 1. Aliasing
            query = PerformAliasing(fromAlias, query);

            // 2. Join
            if (request.Join != null)
            {
                foreach (var joinStmt in request.Join)
                    query = ApplyJoin(request.Data, query, joinStmt);
            }

            // 3. Filter (WHERE)
            if (request.Conditions?.Length > 0)
            {
                query = query.Where(item =>
                    EvaluateConditions(request.Data, item, request.Conditions)
                );
            }

            // 4. Sorting (PENTING: Dilakukan sebelum Grouping/Select)
            if (request.Order != null)
            {
                query = ApplyOrdering(query, request.Order);
            }

            // 5. Grouping & Selection
            query = PerformGroupingAndSelection(request, query);

            // 6. Distinct
            if (request.Distinct)
                query = query.Distinct(new JTokenEqualityComparer());

            return query;
        }
        catch (Exception ex)
        {
            throw new JsonQueryException("Error executing JSON query.", ex);
        }
    }

    private static JToken? GetSourceToken(JObject root, string path)
    {
        if (!path.StartsWith('$') && !path.StartsWith('['))
            path = "$." + path;
        if (path == "$")
            return root;
        return root.SelectToken(path) ?? (path.StartsWith("$.") ? root[path[2..]] : root[path]);
    }

    private static IEnumerable<JToken> PerformAliasing(string fromAlias, IEnumerable<JToken> query)
    {
        if (string.IsNullOrEmpty(fromAlias))
            return query;
        return query.Select(item =>
        {
            var wrapped = new JObject { [fromAlias] = item.DeepClone() };
            if (item is JObject jo)
                wrapped.Merge(jo);
            return (JToken)wrapped;
        });
    }

    private static IEnumerable<JToken> PerformGroupingAndSelection(
        JsonQueryRequest request,
        IEnumerable<JToken> query
    )
    {
        bool hasAggregate = request.Select?.Any(s => s.Contains('(') && s.Contains(')')) ?? false;

        // Jika ada GROUP BY
        if (request.GroupBy?.Length > 0)
        {
            var grouped = ProcessGrouping(request.Data!, query, request);
            if (request.Having != null)
                grouped = grouped.Where(item =>
                    EvaluateConditions(request.Data!, item, request.Having)
                );
            return grouped;
        }
        // Jika Agregasi Global (e.g SELECT COUNT(*))
        else if (hasAggregate)
        {
            var list = query.ToList();
            var resultObj = new JObject();
            var fakeGroup = list.GroupBy(_ => 1).FirstOrDefault();

            if (fakeGroup == null)
                return [];

            foreach (var selection in request.Select!)
            {
                var parts = selection.Split(
                    [" AS ", " as "],
                    StringSplitOptions.RemoveEmptyEntries
                );
                var expr = parts[0].Trim();
                var alias = parts.Length > 1 ? parts[1].Trim() : expr.Replace("$.", "");

                if (expr.Contains('('))
                    resultObj[alias] = CalculateAggregate(fakeGroup, expr, request.Data!);
                else
                    resultObj[alias] = JValue.CreateNull();
            }
            return new List<JToken> { resultObj };
        }
        // Proyeksi SELECT biasa
        else if (request.Select?.Length > 0)
        {
            return query.Select(item => ProjectItem(request.Data!, item, request.Select));
        }

        return query;
    }

    private static JToken ProjectItem(JObject root, JToken item, string[] select)
    {
        if (select.Length == 1 && (select[0] == "*" || select[0] == "$.*"))
            return item.DeepClone();

        var projectedObj = new JObject();
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

    private static JToken? GetTokenValue(JObject root, JToken item, string path)
    {
        if (path == "$")
            return root;
        if (path.StartsWith("$."))
            return root.SelectToken(path);

        if (path.Contains('.'))
        {
            var parts = path.Split('.');
            string alias = parts[0];
            string field = string.Join(".", parts.Skip(1));
            if (item[alias] != null)
                return item[alias]!.SelectToken(field) ?? item[alias]![field];
        }

        return item.SelectToken(path) ?? item[path];
    }

    private static bool EvaluateConditions(JObject root, JToken item, string[] conditions)
    {
        bool finalResult = false;
        string currentOperator = "OR";

        foreach (var condition in conditions)
        {
            string upper = condition.Trim().ToUpper();
            if (upper == "AND" || upper == "OR")
            {
                currentOperator = upper;
                continue;
            }

            bool res = EvaluateSingleCondition(root, item, condition);
            if (currentOperator == "OR")
                finalResult = finalResult || res;
            else
                finalResult = finalResult && res;
        }
        return finalResult;
    }

    private static bool EvaluateSingleCondition(JObject root, JToken item, string condition)
    {
        string[] ops = ["==", "!=", ">=", "<=", ">", "<"];
        string op = ops.FirstOrDefault(condition.Contains) ?? "";
        if (string.IsNullOrEmpty(op))
            return false;

        var parts = condition.Split(op);
        string prop = parts[0].Trim();
        string valRaw = parts[1].Trim();

        JToken? left = GetTokenValue(root, item, prop);
        if (left == null)
            return false;

        JToken? right =
            (valRaw.StartsWith('\'') && valRaw.EndsWith('\''))
                ? valRaw.Trim('\'')
                : GetTokenValue(root, item, valRaw) ?? valRaw;

        string s1 = left.ToString();
        string s2 = right?.ToString() ?? "";

        if (double.TryParse(s1, out double n1) && double.TryParse(s2, out double n2))
        {
            return op switch
            {
                "==" => Math.Abs(n1 - n2) < 0.000001,
                "!=" => Math.Abs(n1 - n2) >= 0.000001,
                ">" => n1 > n2,
                "<" => n1 < n2,
                ">=" => n1 >= n2,
                "<=" => n1 <= n2,
                _ => false,
            };
        }
        return op == "=="
            ? s1.Equals(s2, StringComparison.OrdinalIgnoreCase)
            : !s1.Equals(s2, StringComparison.OrdinalIgnoreCase);
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
            var resultObj = new JObject();
            foreach (var key in request.GroupBy!)
            {
                resultObj[key.Contains('.') ? key.Split('.').Last() : key.Replace("$.", "")] =
                    GetTokenValue(root, group.First(), key);
            }
            if (request.Select != null)
            {
                foreach (var selection in request.Select)
                {
                    var parts = selection.Split(
                        [" AS ", " as "],
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    var expr = parts[0].Trim();
                    var alias = parts.Length > 1 ? parts[1].Trim() : expr.Replace("$.", "");
                    if (expr.Contains('('))
                        resultObj[alias] = CalculateAggregate(group, expr, root);
                    else if (!request.GroupBy!.Contains(expr))
                        resultObj[alias] = GetTokenValue(root, group.First(), expr);
                }
            }
            return (JToken)resultObj;
        });
    }

    private static JToken CalculateAggregate(
        IEnumerable<JToken> group,
        string expression,
        JObject root
    )
    {
        var open = expression.IndexOf('(');
        var close = expression.IndexOf(')');
        if (open == -1 || close == -1)
            return JValue.CreateNull();

        string func = expression[..open].ToUpper();
        string field = expression.Substring(open + 1, close - open - 1);

        // Filter v agar tidak null sebelum masuk ke kalkulasi
        var values = group
            .Select(item => GetTokenValue(root, item, field))
            .Where(v => v != null && v.Type != JTokenType.Null);

        if (!values.Any())
            return func == "COUNT" ? 0 : JValue.CreateNull();

        // Menggunakan operator ! (null-forgiving) karena sudah difilter di .Where()
        // atau mengubah ConvertToDecimal untuk menerima nullable
        return func switch
        {
            "SUM" => values.Sum(v => ConvertToDecimal(v)),
            "COUNT" => values.Count(),
            "AVG" => values.Average(v => ConvertToDecimal(v)),
            "MIN" => values.Min(v => ConvertToDecimal(v)),
            "MAX" => values.Max(v => ConvertToDecimal(v)),
            _ => JValue.CreateNull(),
        };
    }

    // Perbaikan: Tambahkan tanda ? pada JToken agar menerima null
    private static decimal ConvertToDecimal(JToken? v)
    {
        if (v == null || v.Type == JTokenType.Null)
            return 0;

        return v.Type switch
        {
            JTokenType.Integer => v.Value<decimal>(),
            JTokenType.Float => v.Value<decimal>(),
            _ => decimal.TryParse(v.ToString(), out var d) ? d : 0,
        };
    }

    private static IEnumerable<JToken> ApplyOrdering(IEnumerable<JToken> query, string[] order)
    {
        IOrderedEnumerable<JToken>? ordered = null;
        foreach (var clause in order)
        {
            var parts = clause.Trim().Split(' ');
            string col = parts[0];
            bool desc =
                parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

            Func<JToken, object?> selector = item =>
            {
                var val = GetTokenValue(null!, item, col); // Cari di item lokal
                return val?.Type switch
                {
                    JTokenType.Integer => val.Value<long>(),
                    JTokenType.Float => val.Value<double>(),
                    _ => val?.ToString(),
                };
            };

            if (ordered == null)
                ordered = desc ? query.OrderByDescending(selector) : query.OrderBy(selector);
            else
                ordered = desc ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
        }
        return ordered ?? query;
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
        var joinPath = tableParts[0].Trim();
        var rightAlias = tableParts.Length > 1 ? tableParts[1].Trim() : joinPath.Replace("$.", "");

        JToken? rightToken = GetSourceToken(root, joinPath);
        var rightArray = rightToken is JArray ja
            ? ja
            : (rightToken != null ? new JArray(rightToken) : null);
        if (rightArray == null)
            return mainQuery;

        return mainQuery.Select(leftItem =>
        {
            var match = rightArray.FirstOrDefault(rightItem =>
            {
                JObject context = new() { [rightAlias] = rightItem.DeepClone() };
                if (leftItem is JObject jo)
                    context.Merge(jo);
                return EvaluateConditions(
                    root,
                    context,
                    onParts[1].Split([" AND ", " OR "], StringSplitOptions.RemoveEmptyEntries)
                );
            });
            if (match == null)
                return leftItem;
            JObject combined = leftItem is JObject jo ? (JObject)jo.DeepClone() : [];
            combined[rightAlias] = match.DeepClone();
            return combined;
        });
    }
}
