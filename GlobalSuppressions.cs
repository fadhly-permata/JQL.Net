using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    category: "SonarLint",
    checkId: "S1192",
    Justification = "SQL keywords in Parse method are standard and need to be literals for clarity",
    Scope = "member",
    Target = "~M:JQL.Net.JsonQueryRequest.Parse(System.String)"
)]
