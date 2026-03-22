using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    category: "SonarLint",
    checkId: "S1192",
    Justification = "SQL keywords in Parse method are standard and need to be literals for clarity",
    Scope = "member",
    Target = "~M:JQL.Net.JsonQueryRequest.Parse(System.String)"
)]

[assembly: SuppressMessage(
    category: "SonarLint",
    checkId: "S4055",
    Justification = "Static method required for IComparer implementation",
    Scope = "member",
    Target = "~M:JQL.Net.Core.Utilities.JTokenComparer.Compare(Newtonsoft.Json.Linq.JToken,Newtonsoft.Json.Linq.JToken)"
)]
