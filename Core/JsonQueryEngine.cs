using JQL.Net.Core.Utilities;
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
            var fromClauseParts = request.From.Split( // Ganti nama variabel dari fromParts
                separator: [" AS ", " as "],
                options: StringSplitOptions.RemoveEmptyEntries
            );
            var fromPath = fromClauseParts[0].Trim();
            var fromAlias = fromClauseParts.Length > 1 ? fromClauseParts[1].Trim() : string.Empty;

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
            var hasGlobalInSelect = false;
            var hasDetailInSelect = false;

            if (request.Select != null)
            {
                foreach (var s in request.Select)
                {
                    var selectParts = s.Split( // Ganti nama variabel dari p
                        separator: [" AS ", " as "],
                        options: StringSplitOptions.RemoveEmptyEntries
                    );
                    var source = selectParts[0].Trim();
                    var alias =
                        selectParts.Length > 1
                            ? selectParts[1].Trim()
                            : (
                                source.Contains(value: '.')
                                    ? source.Split(separator: '.').Last()
                                    : source.Replace(oldValue: "$.", newValue: "")
                            );

                    var isGlobal =
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
                var detailList = new JArray();

                // Ambil global fields (hanya diambil sekali)
                foreach (var item in queryResult)
                {
                    if (item is not JObject jo)
                        continue;

                    var detailItem = new JObject();
                    var hasDetail = false;

                    foreach (var prop in jo.Properties())
                    {
                        if (columnMap.TryGetValue(prop.Name, out var isGlobal) && isGlobal)
                        {
                            // Global field (seperti Organization)
                            masterObj[prop.Name] ??= prop.Value;
                        }
                        else
                        {
                            // Detail field (seperti Nama, Gaji)
                            detailItem[prop.Name] = prop.Value.DeepClone();
                            hasDetail = true;
                        }
                    }

                    if (hasDetail && detailItem.HasValues)
                    {
                        detailList.Add(detailItem);
                    }
                }

                // Jika ada data detail, tambahkan ke master object
                if (detailList.Count > 0)
                {
                    // Cari nama yang cocok untuk group detail (misal: Karyawan)
                    var detailGroupName = "Detail";
                    var fromAliasParts = request.From.Split(
                        [" AS ", " as "],
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (fromAliasParts.Length > 1)
                    {
                        detailGroupName = fromAliasParts[1].Trim();
                    }

                    masterObj[detailGroupName] = detailList;
                }

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

        return query.Select(
            selector: JToken (item) => new JObject { [propertyName: fromAlias] = item.DeepClone() }
        );
    }

    private static IEnumerable<JToken> PerformGroupingAndSelection(
        JsonQueryRequest request,
        IEnumerable<JToken> query
    )
    {
        var hasAggregate =
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

        if (hasAggregate)
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
            return (List<JToken>)[resultObj];
        }
        // Proyeksi SELECT biasa

        if (request.Select?.Length > 0)
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
            var selectParts = selection.Split( // Ganti nama variabel dari parts
                separator: [" AS ", " as "],
                options: StringSplitOptions.RemoveEmptyEntries
            );
            var sourceField = selectParts[0].Trim();
            var aliasField =
                selectParts.Length > 1 ? selectParts[1].Trim() : sourceField.Split('.').Last();

            // Handle field dengan notasi dot (misal: Karyawan.FullName)
            JToken? tokenValue;
            if (sourceField.Contains('.'))
            {
                // Jika field mengandung titik, gunakan GetTokenValue
                tokenValue = GetTokenValue(root, item, sourceField);
            }
            else
            {
                // Jika tidak, cari di root atau item
                tokenValue =
                    GetTokenValue(root, item, sourceField)
                    ?? GetTokenValue(root, item, "$." + sourceField);
            }

            projectedObj[aliasField] = tokenValue?.DeepClone() ?? JValue.CreateNull();
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
            var alias = parts[0];
            var field = string.Join(separator: ".", values: parts.Skip(count: 1));

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
        if (conditions.Length == 0)
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
        var result = EvaluateSingleCondition(root: root, item: item, condition: parts[0]);

        for (var i = 1; i < parts.Length; i += 2)
        {
            var op = parts[i].ToUpper();
            if (i + 1 < parts.Length)
            {
                var nextRes = EvaluateSingleCondition(
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
        var selectedOp = "";
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

        var leftPath = parts[0].Trim();
        // Membersihkan kutipan satu per satu secara eksplisit
        var rightValueRaw = parts[1].Trim().Trim(trimChar: '\'').Trim(trimChar: '"');

        var leftToken = GetTokenValue(root: root, item: item, path: leftPath);
        if (leftToken == null)
            return false;

        // Jika tipe data di JSON adalah string, JToken.ToString() terkadang memberikan hasil yang berbeda
        // tergantung versi Newtonsoft. Cara paling aman:
        var leftValue =
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
            double.TryParse(s: left, result: out var l)
            && double.TryParse(s: right, result: out var r)
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

        var func = expression[..open].ToUpper();
        var field = expression.Substring(startIndex: open + 1, length: close - open - 1);

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

        var rightToken = GetSourceToken(root: root, path: joinPath);
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
            var combined = leftItem is JObject jo ? (JObject)jo.DeepClone() : [];
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

        for (var i = 0; i < orderClauses.Length; i++)
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
