using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "SonarLint",
    "S1192",
    Justification = "SQL keywords are standard and unlikely to change",
    Scope = "namespace",
    Target = "~N:JQL.Net.Core"
)]
