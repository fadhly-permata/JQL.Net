# 🚀 JQL.Net (JSON Query Language for .NET)

[![NuGet](https://img.shields.io/nuget/v/JQL.Net.svg)](https://www.nuget.org/packages/JQL.Net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Bring the power of SQL to your JSON!** 🎯 

JQL.Net is a lightweight, high-performance query engine that lets you search, join, and aggregate raw JSON data using familiar SQL-like syntax. Perfect for those moments when you have complex JSON structures but don't want the overhead of a database.

---

## ✨ Features

- 🔍 **SQL-Like Syntax**: Use `SELECT`, `FROM`, `WHERE`, `JOIN`, `GROUP BY`, `HAVING`, and `ORDER BY`.
- 🤝 **Advanced Joins**: Support for multiple conditions in `ON` using `AND` / `OR` logic.
- 🧮 **Aggregations**: Built-in support for `SUM`, `COUNT`, `AVG`, `MIN`, and `MAX`.
- ☁️ **Case-Insensitive**: Keywords like `select` or `SELECT`? We don't judge. It just works.
- 🏷️ **Alias Support**: Use `AS` to keep your results clean and readable.
- ⚡ **Lightweight**: Zero database dependencies. Just you and your JSON.

---

## 📦 Installation

Grab it on **NuGet**:

```bash
dotnet add package JQL.Net
```

---

## 🚀 Quick Start

Using JQL.Net is as easy as ordering pizza. Check this out:

```csharp
using JQL.Net.Extensions;
using Newtonsoft.Json.Linq;

// 1. Your raw JSON data
var json = @"{
    'employees': [
        { 'id': 1, 'name': 'John Doe', 'dept_id': 10, 'salary': 8000 },
        { 'id': 2, 'name': 'Jane Smith', 'dept_id': 10, 'salary': 9500 }
    ],
    'departments': [
        { 'id': 10, 'name': 'IT', 'budget': 20000 }
    ]
}";

var data = JObject.Parse(json);

// 2. Write your 'SQL'
string query = @"
    SELECT e.name, d.name AS DeptName
    FROM $.employees AS e
    JOIN $.departments AS d ON e.dept_id == d.id
    WHERE e.salary > 5000";

// 3. Execute!
var results = data.Query(query);

foreach (var row in results) {
    Console.WriteLine($"{row["name"]} works in {row["DeptName"]}");
}
```

---

## 💡 Cool Examples

### Complex Joins (The 'AND/OR' Power) 🦾
Need to link data with multiple rules? No problem:
```sql
SELECT e.name, p.proj_name 
FROM $.employees AS e 
JOIN $.projects AS p ON e.proj_id == p.id AND p.status == 'Active'
```

### Aggregations & Having 📊
Want to find big-spending departments?
```sql
SELECT d.name, SUM(salary) AS total_cost 
FROM $.employees AS e 
JOIN $.departments AS d ON dept_id == id 
GROUP BY d.name 
HAVING total_cost > d.budget
```

---

## 🛠️ Supported Keywords

| Keyword | Description |
| :--- | :--- |
| `SELECT` | Choose which fields to return (supports Aliases). |
| `FROM` | Define the JSON path (default is `$`). |
| `JOIN` | Combine two JSON arrays with `ON` logic. |
| `WHERE` | Filter results with `==`, `!=`, `>`, `<`, etc. |
| `GROUP BY`| Bundle results by specific fields. |
| `HAVING` | Filter aggregated groups. |
| `ORDER BY`| Sort your output. |

---

## 🤝 Contributing

Got a cool idea or found a bug? 🐛
1. Fork it!
2. Create your feature branch (`git checkout -b feature/cool-stuff`)
3. Commit your changes (`git commit -m 'Add some cool stuff'`)
4. Push to the branch (`git push origin feature/cool-stuff`)
5. Open a Pull Request.

---

## 📄 License

This project is licensed under the **MIT License**.

---

**Made with ❤️ for the .NET Community.** *If you find JQL.Net useful, give it a ⭐ on GitHub!*