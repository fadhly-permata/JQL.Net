using JQL.Net.Core;
using Newtonsoft.Json.Linq;

namespace JQL.Net.Extensions;

public static class JsonQueryExtensions
{
    /// <summary>
    ///     Executes a SQL-like query against a JObject
    /// </summary>
    /// <param name="data">
    ///     The JObject to query
    /// </param>
    /// <param name="sqlQuery">
    ///     The SQL-like query
    /// </param>
    /// <returns>
    ///     The results of the query
    /// </returns>
    public static IEnumerable<JToken> Query(this JObject data, string sqlQuery) =>
        JsonQueryEngine.Execute(new JsonQueryRequest { Data = data }.Parse(sqlQuery));
}
