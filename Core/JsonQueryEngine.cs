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
    public static JToken Execute(JsonQueryRequest request)
    {
        if (request.Data == null)
            return JValue.CreateNull();

        try
        {
            var fromParts = request.From.Split(
                separator: [" AS ", " as "],
                options: StringSplitOptions.RemoveEmptyEntries
            );
            var fromPath = fromParts[0].Trim();
            var fromAlias = fromParts.Length > 1 ? fromParts[1].Trim() : string.Empty;

            var targetToken = GetSourceToken(root: request.Data, path: fromPath);
            if (targetToken == null)
                return JValue.CreateNull();

            IEnumerable<JToken> query = targetToken is JArray array ? array : [targetToken];

            // 1. Aliasing & Processing
            query = PerformAliasing(fromAlias: fromAlias, query: query);
            if (request.Join != null)
                foreach (var join in request.Join)
                    query = ApplyJoin(root: request.Data, mainQuery: query, joinStatement: join);

            if (request.Conditions != null)
                query = query.Where(predicate: item =>
                    EvaluateConditions(
                        root: request.Data,
                        item: item,
                        conditions: request.Conditions
                    )
                );

            if (request.Order != null)
                query = ApplySorting(root: request.Data, query: query, orderClauses: request.Order);

            // 5. Grouping & Selection
            var queryResult = PerformGroupingAndSelection(request: request, query: query).ToList();

            if (request.Distinct)
                queryResult = queryResult.Distinct(comparer: new JTokenEqualityComparer()).ToList();

            // --- LOGIKA OUTPUT MASTER-DETAIL OTOMATIS ---

            var globalKeys = request.Data.Properties().Select(selector: p => p.Name).ToList();
            var columnMap = new Dictionary<string, bool>(); // Key: AliasColumn, Value: IsGlobal
            bool hasGlobalInSelect = false;
            bool hasDetailInSelect = false;

            if (request.Select != null)
            {
                foreach (var s in request.Select)
                {
                    var p = s.Split(
                        separator: [" AS ", " as "],
                        options: StringSplitOptions.RemoveEmptyEntries
                    );
                    var source = p[0].Trim();
                    var alias =
                        p.Length > 1
                            ? p[1].Trim()
                            : (
                                source.Contains(value: '.')
                                    ? source.Split(separator: '.').Last()
                                    : source.Replace(oldValue: "$.", newValue: "")
                            );

                    bool isGlobal =
                        source.StartsWith(value: "$.")
                        || (
                            globalKeys.Contains(item: source)
                            && !source.StartsWith(value: fromAlias + ".")
                        );

                    columnMap[key: alias] = isGlobal;
                    if (isGlobal)
                        hasGlobalInSelect = true;
                    else
                        hasDetailInSelect = true;
                }
            }

            // Jika campuran (Master-Detail), bungkus koleksi ke dalam object
            if (hasGlobalInSelect && hasDetailInSelect && queryResult.Count > 0)
            {
                var masterObj = new JObject();
                var detailMap = new Dictionary<string, JArray>();

                foreach (var item in queryResult)
                {
                    if (item is not JObject jo)
                        continue;

                    foreach (var prop in jo.Properties())
                    {
                        if (
                            columnMap.TryGetValue(key: prop.Name, value: out bool isGlobal)
                            && isGlobal
                        )
                        {
                            masterObj[propertyName: prop.Name] ??= prop.Value;
                        }
                        else
                        {
                            if (!detailMap.ContainsKey(key: prop.Name))
                                detailMap[key: prop.Name] = new JArray();
                            detailMap[key: prop.Name].Add(item: prop.Value);
                        }
                    }
                }

                foreach (var detail in detailMap)
                    masterObj[propertyName: detail.Key] = detail.Value;

                return masterObj;
            }

            // --- LOGIKA OUTPUT STANDAR ---
            if (IsAggregateQuery(request: request))
                return queryResult.FirstOrDefault() ?? JValue.CreateNull();

            if (
                queryResult.Count == 1
                && targetToken is not JArray
                && string.IsNullOrEmpty(value: fromAlias)
            )
                return queryResult.First();

            return JArray.FromObject(o: queryResult);
        }
        catch (Exception ex)
        {
            throw new JsonQueryException(
                message: "Error executing JSON query.",
                innerException: ex
            );
        }
    }

    private static JToken? GetSourceToken(JObject root, string path)
    {
        if (!path.StartsWith(value: '$') && !path.StartsWith(value: '['))
            path = "$." + path;
        if (path == "$")
            return root;
        return root.SelectToken(path: path)
            ?? (
                path.StartsWith(value: "$.")
                    ? root[propertyName: path[2..]]
                    : root[propertyName: path]
            );
    }

    private static IEnumerable<JToken> PerformAliasing(string fromAlias, IEnumerable<JToken> query)
    {
        if (string.IsNullOrEmpty(value: fromAlias))
            return query;

        return query.Select(selector: item =>
        {
            return (JToken)new JObject { [propertyName: fromAlias] = item.DeepClone() };
        });
    }

    private static IEnumerable<JToken> PerformGroupingAndSelection(
        JsonQueryRequest request,
        IEnumerable<JToken> query
    )
    {
        bool hasAggregate =
            request.Select?.Any(predicate: s => s.Contains(value: '(') && s.Contains(value: ')'))
            ?? false;

        // Jika ada GROUP BY
        if (request.GroupBy?.Length > 0)
        {
            var grouped = ProcessGrouping(root: request.Data!, query: query, request: request);
            if (request.Having != null)
                grouped = grouped.Where(predicate: item =>
                    EvaluateConditions(root: request.Data!, item: item, conditions: request.Having)
                );
            return grouped;
        }
        // Jika Agregasi Global (e.g SELECT COUNT(*))
        else if (hasAggregate)
        {
            var list = query.ToList();
            var resultObj = new JObject();
            var fakeGroup = list.GroupBy(keySelector: _ => 1).FirstOrDefault();

            if (fakeGroup == null)
                return [];

            foreach (var selection in request.Select!)
            {
                var parts = selection.Split(
                    separator: [" AS ", " as "],
                    options: StringSplitOptions.RemoveEmptyEntries
                );
                var expr = parts[0].Trim();
                var alias =
                    parts.Length > 1 ? parts[1].Trim() : expr.Replace(oldValue: "$.", newValue: "");

                if (expr.Contains(value: '('))
                    resultObj[propertyName: alias] = CalculateAggregate(
                        group: fakeGroup,
                        expression: expr,
                        root: request.Data!
                    );
                else
                    resultObj[propertyName: alias] = JValue.CreateNull();
            }
            return new List<JToken> { resultObj };
        }
        // Proyeksi SELECT biasa
        else if (request.Select?.Length > 0)
        {
            return query.Select(selector: item =>
                ProjectItem(root: request.Data!, item: item, select: request.Select)
            );
        }

        return query;
    }

    private static JToken ProjectItem(JObject root, JToken item, string[] select)
    {
        // Handle SELECT * atau SELECT $.*
        if (select.Length == 1 && (select[0] == "*" || select[0] == "$.*"))
        {
            if (item is JObject jo && jo.Properties().Count() == 1)
                return jo.Properties().First().Value.DeepClone();
            return item.DeepClone();
        }

        var projectedObj = new JObject();
        foreach (var selection in select)
        {
            var parts = selection.Split(
                separator: [" AS ", " as "],
                options: StringSplitOptions.RemoveEmptyEntries
            );
            string sourceField = parts[0].Trim();
            string aliasField =
                parts.Length > 1
                    ? parts[1].Trim()
                    : (
                        sourceField.Contains(value: '.')
                            ? sourceField.Split(separator: '.').Last()
                            : sourceField.Replace(oldValue: "$.", newValue: "")
                    );

            projectedObj[propertyName: aliasField] =
                GetTokenValue(root: root, item: item, path: sourceField)?.DeepClone()
                ?? JValue.CreateNull();
        }
        return projectedObj;
    }

    private static JToken? GetTokenValue(JObject root, JToken item, string path)
    {
        if (string.IsNullOrEmpty(value: path) || path == "$" || path == "*")
            return path == "$" ? root : item;

        // 1. Handle Global Path
        if (path.StartsWith(value: "$."))
            return root.SelectToken(path: path);

        // 2. Handle Aliasing (Contoh: "m.FullName")
        if (path.Contains(value: '.'))
        {
            var parts = path.Split(separator: '.');
            string alias = parts[0];
            string field = string.Join(separator: ".", values: parts.Skip(count: 1));

            if (
                item is JObject jo
                && jo.TryGetValue(
                    propertyName: alias,
                    comparison: StringComparison.OrdinalIgnoreCase,
                    value: out var aliasToken
                )
            )
            {
                if (field == "*" || string.IsNullOrEmpty(value: field))
                    return aliasToken;

                // Gunakan SelectTokens().FirstOrDefault() untuk menghindari exception multiple tokens
                return aliasToken.SelectTokens(path: field).FirstOrDefault();
            }
        }

        // 3. Direct Access / Fallback
        try
        {
            return item.SelectTokens(path: path).FirstOrDefault() ?? item[key: path];
        }
        catch
        {
            return item[key: path];
        }
    }

    private static bool EvaluateConditions(JObject root, JToken item, string[] conditions)
    {
        if (conditions == null || conditions.Length == 0)
            return true;

        // Gabungkan kembali menjadi string utuh untuk dianalisis ulang secara benar
        var fullConditionString = string.Join(separator: " ", value: conditions);

        // Split berdasarkan operator dengan case-insensitive
        var parts = System.Text.RegularExpressions.Regex.Split(
            input: fullConditionString,
            pattern: @"\s+(?i)(AND|OR)\s+"
        );

        if (parts.Length == 0)
            return true;

        // Ambil hasil kondisi pertama
        bool result = EvaluateSingleCondition(root: root, item: item, condition: parts[0]);

        for (int i = 1; i < parts.Length; i += 2)
        {
            string op = parts[i].ToUpper();
            if (i + 1 < parts.Length)
            {
                bool nextRes = EvaluateSingleCondition(
                    root: root,
                    item: item,
                    condition: parts[i + 1]
                );
                if (op == "AND")
                    result = result && nextRes;
                else if (op == "OR")
                    result = result || nextRes;
            }
        }

        return result;
    }

    private static bool EvaluateSingleCondition(JObject root, JToken item, string condition)
    {
        string[] operators = ["==", "!=", ">=", "<=", ">", "<"];
        string selectedOp = "";
        string[] parts = [];

        foreach (var op in operators)
        {
            if (condition.Contains(value: op))
            {
                selectedOp = op;
                parts = condition.Split(separator: new[] { op }, options: StringSplitOptions.None);
                break;
            }
        }

        if (parts.Length < 2)
            return false;

        string leftPath = parts[0].Trim();
        // Membersihkan kutipan satu per satu secara eksplisit
        string rightValueRaw = parts[1].Trim().Trim(trimChar: '\'').Trim(trimChar: '"');

        JToken? leftToken = GetTokenValue(root: root, item: item, path: leftPath);
        if (leftToken == null)
            return false;

        // Jika tipe data di JSON adalah string, JToken.ToString() terkadang memberikan hasil yang berbeda
        // tergantung versi Newtonsoft. Cara paling aman:
        string leftValue =
            leftToken.Type == JTokenType.String ? (string)leftToken! : leftToken.ToString();

        return selectedOp switch
        {
            "==" => leftValue.Equals(
                value: rightValueRaw,
                comparisonType: StringComparison.OrdinalIgnoreCase
            ),
            "!=" => !leftValue.Equals(
                value: rightValueRaw,
                comparisonType: StringComparison.OrdinalIgnoreCase
            ),
            ">" => IsNumeric(token: leftToken)
                && TryCompare(left: leftValue, right: rightValueRaw, op: (l, r) => l > r),
            "<" => IsNumeric(token: leftToken)
                && TryCompare(left: leftValue, right: rightValueRaw, op: (l, r) => l < r),
            ">=" => IsNumeric(token: leftToken)
                && TryCompare(left: leftValue, right: rightValueRaw, op: (l, r) => l >= r),
            "<=" => IsNumeric(token: leftToken)
                && TryCompare(left: leftValue, right: rightValueRaw, op: (l, r) => l <= r),
            _ => false,
        };
    }

    private static bool TryCompare(string left, string right, Func<double, double, bool> op)
    {
        if (
            double.TryParse(s: left, result: out double l)
            && double.TryParse(s: right, result: out double r)
        )
            return op(arg1: l, arg2: r);
        return false;
    }

    private static bool IsNumeric(JToken token)
    {
        return token.Type is JTokenType.Integer or JTokenType.Float;
    }

    private static IEnumerable<JToken> ProcessGrouping(
        JObject root,
        IEnumerable<JToken> query,
        JsonQueryRequest request
    )
    {
        var groupedData = query.GroupBy(keySelector: item =>
            string.Join(
                separator: "-",
                values: request.GroupBy!.Select(selector: g =>
                    GetTokenValue(root: root, item: item, path: g)?.ToString() ?? ""
                )
            )
        );

        return groupedData.Select(selector: group =>
        {
            var resultObj = new JObject();
            foreach (var key in request.GroupBy!)
            {
                resultObj[
                    propertyName: key.Contains(value: '.')
                        ? key.Split(separator: '.').Last()
                        : key.Replace(oldValue: "$.", newValue: "")
                ] = GetTokenValue(root: root, item: group.First(), path: key);
            }
            if (request.Select != null)
            {
                foreach (var selection in request.Select)
                {
                    var parts = selection.Split(
                        separator: [" AS ", " as "],
                        options: StringSplitOptions.RemoveEmptyEntries
                    );
                    var expr = parts[0].Trim();
                    var alias =
                        parts.Length > 1
                            ? parts[1].Trim()
                            : expr.Replace(oldValue: "$.", newValue: "");
                    if (expr.Contains(value: '('))
                        resultObj[propertyName: alias] = CalculateAggregate(
                            group: group,
                            expression: expr,
                            root: root
                        );
                    else if (!request.GroupBy!.Contains(value: expr))
                        resultObj[propertyName: alias] = GetTokenValue(
                            root: root,
                            item: group.First(),
                            path: expr
                        );
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
        var open = expression.IndexOf(value: '(');
        var close = expression.IndexOf(value: ')');
        if (open == -1 || close == -1)
            return JValue.CreateNull();

        string func = expression[..open].ToUpper();
        string field = expression.Substring(startIndex: open + 1, length: close - open - 1);

        // Filter v agar tidak null sebelum masuk ke kalkulasi
        var values = group
            .Select(selector: item => GetTokenValue(root: root, item: item, path: field))
            .Where(predicate: v => v != null && v.Type != JTokenType.Null);

        if (!values.Any())
            return func == "COUNT" ? 0 : JValue.CreateNull();

        // Menggunakan operator ! (null-forgiving) karena sudah difilter di .Where()
        // atau mengubah ConvertToDecimal untuk menerima nullable
        return func switch
        {
            "SUM" => values.Sum(selector: v => ConvertToDecimal(v: v)),
            "COUNT" => values.Count(),
            "AVG" => values.Average(selector: v => ConvertToDecimal(v: v)),
            "MIN" => values.Min(selector: v => ConvertToDecimal(v: v)),
            "MAX" => values.Max(selector: v => ConvertToDecimal(v: v)),
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
            _ => decimal.TryParse(s: v.ToString(), result: out var d) ? d : 0,
        };
    }

    private static IEnumerable<JToken> ApplyOrdering(IEnumerable<JToken> query, string[] order)
    {
        IOrderedEnumerable<JToken>? ordered = null;
        foreach (var clause in order)
        {
            var parts = clause.Trim().Split(separator: ' ');
            string col = parts[0];
            bool desc =
                parts.Length > 1
                && parts[1]
                    .Equals(value: "DESC", comparisonType: StringComparison.OrdinalIgnoreCase);

            Func<JToken, object?> selector = item =>
            {
                var val = GetTokenValue(root: null!, item: item, path: col); // Cari di item lokal
                return val?.Type switch
                {
                    JTokenType.Integer => val.Value<long>(),
                    JTokenType.Float => val.Value<double>(),
                    _ => val?.ToString(),
                };
            };

            if (ordered == null)
                ordered = desc
                    ? query.OrderByDescending(keySelector: selector)
                    : query.OrderBy(keySelector: selector);
            else
                ordered = desc
                    ? ordered.ThenByDescending(keySelector: selector)
                    : ordered.ThenBy(keySelector: selector);
        }
        return ordered ?? query;
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
        var joinPath = tableParts[0].Trim();
        var rightAlias =
            tableParts.Length > 1
                ? tableParts[1].Trim()
                : joinPath.Replace(oldValue: "$.", newValue: "");

        JToken? rightToken = GetSourceToken(root: root, path: joinPath);
        var rightArray = rightToken is JArray ja
            ? ja
            : (rightToken != null ? new JArray(content: rightToken) : null);
        if (rightArray == null)
            return mainQuery;

        return mainQuery.Select(selector: leftItem =>
        {
            var match = rightArray.FirstOrDefault(predicate: rightItem =>
            {
                JObject context = new() { [propertyName: rightAlias] = rightItem.DeepClone() };
                if (leftItem is JObject jo)
                    context.Merge(content: jo);
                return EvaluateConditions(
                    root: root,
                    item: context,
                    conditions: onParts[1]
                        .Split(
                            separator: [" AND ", " OR "],
                            options: StringSplitOptions.RemoveEmptyEntries
                        )
                );
            });
            if (match == null)
                return leftItem;
            JObject combined = leftItem is JObject jo ? (JObject)jo.DeepClone() : [];
            combined[propertyName: rightAlias] = match.DeepClone();
            return combined;
        });
    }

    private static IEnumerable<JToken> ApplySorting(
        JObject root,
        IEnumerable<JToken> query,
        string[] orderClauses
    )
    {
        IOrderedEnumerable<JToken>? orderedQuery = null;

        for (int i = 0; i < orderClauses.Length; i++)
        {
            var clause = orderClauses[i].Trim();
            var parts = clause.Split(
                separator: ' ',
                options: StringSplitOptions.RemoveEmptyEntries
            );
            var path = parts[0];
            var descending =
                parts.Length > 1
                && parts[1]
                    .Equals(value: "DESC", comparisonType: StringComparison.OrdinalIgnoreCase);

            if (i == 0)
            {
                orderedQuery = descending
                    ? query.OrderByDescending(
                        keySelector: item => GetTokenValue(root: root, item: item, path: path),
                        comparer: new JTokenComparer()
                    )
                    : query.OrderBy(
                        keySelector: item => GetTokenValue(root: root, item: item, path: path),
                        comparer: new JTokenComparer()
                    );
            }
            else
            {
                orderedQuery = descending
                    ? orderedQuery!.ThenByDescending(
                        keySelector: item => GetTokenValue(root: root, item: item, path: path),
                        comparer: new JTokenComparer()
                    )
                    : orderedQuery!.ThenBy(
                        keySelector: item => GetTokenValue(root: root, item: item, path: path),
                        comparer: new JTokenComparer()
                    );
            }
        }

        return orderedQuery ?? query;
    }

    private static bool IsAggregateQuery(JsonQueryRequest request)
    {
        if (request.Select == null)
            return false;
        string[] aggregates = ["COUNT(", "SUM(", "AVG(", "MIN(", "MAX("];
        return request.Select.Any(predicate: s =>
            aggregates.Any(predicate: a =>
                s.Contains(value: a, comparisonType: StringComparison.OrdinalIgnoreCase)
            )
        );
    }
}

internal class JTokenComparer : IComparer<JToken?>
{
    public int Compare(JToken? x, JToken? y)
    {
        if (ReferenceEquals(objA: x, objB: y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        if (x is JValue vx && y is JValue vy)
        {
            var valX = vx.Value;
            var valY = vy.Value;

            if (valX == null && valY == null)
                return 0;
            if (valX == null)
                return -1;
            if (valY == null)
                return 1;

            if (IsNumeric(obj: valX) && IsNumeric(obj: valY))
                return Convert
                    .ToDouble(value: valX)
                    .CompareTo(value: Convert.ToDouble(value: valY));

            return string.Compare(
                strA: valX.ToString(),
                strB: valY.ToString(),
                comparisonType: StringComparison.OrdinalIgnoreCase
            );
        }
        return string.Compare(
            strA: x.ToString(),
            strB: y.ToString(),
            comparisonType: StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsNumeric(object obj) =>
        obj
            is sbyte
                or byte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal;
}
