# 🚀 JQL.Net (JSON Query Language for .NET)

[![NuGet](https://img.shields.io/nuget/v/JQL.Net.svg)](https://www.nuget.org/packages/JQL.Net/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Bring the power of SQL to your JSON!** 🎯 

JQL.Net is a lightweight, high-performance query engine that lets you search, join, and aggregate raw JSON data using familiar SQL-like syntax. Perfect for those moments when you have complex JSON structures but don't want the overhead of a database.

---

## ✨ Features

- 🔍 **SQL-Like Syntax**: Use `SELECT`, `FROM`, `WHERE`, `JOIN`, `GROUP BY`, `HAVING`, and `ORDER BY`.
- 🤝 **Advanced Joins** <sup>1</sup>: Support for multiple conditions in `ON` using `AND` / `OR` logic. 
- 🧮 **Aggregations**: Built-in support for `SUM`, `COUNT`, `AVG`, `MIN`, and `MAX`.
- ☁️ **Case-Insensitive**: Keywords like `select` or `SELECT`? We don't judge. It just works.
- 🏷️ **Alias Support**: Use `AS` to keep your results clean and readable.
- ⚡ **Lightweight**: Zero database dependencies. Just you and your JSON.

**Notes:**
**<sup>1</sup>** *Stay tuned for JOIN capabilities in upcoming releases!*

---

## 📦 Installation

Grab it on **NuGet**:

```bash
dotnet add package JQL.Net
```

---

## 🚀 Quick Start

Ready to write your very first query with **JQL.Net**? It's as easy as ordering pizza! 🍕 Here are two ways to get started:

### Method 1: Object-Oriented Approach
```csharp
using JQL.Net;
using JQL.Net.Core;
using Newtonsoft.Json.Linq;

// Sample data
var json = @"
{
    'Transactions': [
        { 'Id': 1, 'CustomerName': 'Fadhly Permata', 'Category': 'Electronics', 'Amount': 5000000 },
        { 'Id': 2, 'CustomerName': 'Budi Santoso', 'Category': 'Electronics', 'Amount': 1500000 },
        { 'Id': 3, 'CustomerName': 'Sari Wijaya', 'Category': 'Clothing', 'Amount': 200000 }
    ]
}";

var data = JObject.Parse(json);

// Build query
var request = new JsonQueryRequest { 
    Select = new[] { "CustomerName", "Amount" },
    From = "$.Transactions",
    Conditions = new[] { "Amount > 1000000" },
    Data = data
};

// Execute!
var results = JsonQueryEngine.Execute(request);

// Process results
foreach (var result in results)
{
    Console.WriteLine($"Customer: {result["CustomerName"]}, Amount: {result["Amount"]}");
}
```

### Method 2: SQL String Approach
```csharp
using JQL.Net;
using JQL.Net.Extensions;
using Newtonsoft.Json.Linq;

// Sample data
var json = @"
{
    'Transactions': [
        { 'Id': 1, 'CustomerName': 'Fadhly Permata', 'Category': 'Electronics', 'Amount': 5000000 },
        { 'Id': 2, 'CustomerName': 'Budi Santoso', 'Category': 'Electronics', 'Amount': 1500000 },
        { 'Id': 3, 'CustomerName': 'Sari Wijaya', 'Category': 'Clothing', 'Amount': 200000 }
    ]
}";

var data = JObject.Parse(json);

var request = new JsonQueryRequest
{
    RawQuery = "SELECT CustomerName, Amount FROM $.Transactions WHERE Amount > 1000000",
    Data = data
};

var results = JsonQueryEngine.Execute(request.Parse());

// Process results
foreach (var result in results)
{
    Console.WriteLine($"Customer: {result["CustomerName"]}, Amount: {result["Amount"]}");
}
```

### Method 3: Extension Method Approach
```csharp
using JQL.Net.Extensions;
using Newtonsoft.Json.Linq;

// Sample data
var json = @"
{
    'Transactions': [
        { 'Id': 1, 'CustomerName': 'Fadhly Permata', 'Category': 'Electronics', 'Amount': 5000000 },
        { 'Id': 2, 'CustomerName': 'Budi Santoso', 'Category': 'Electronics', 'Amount': 1500000 },
        { 'Id': 3, 'CustomerName': 'Sari Wijaya', 'Category': 'Clothing', 'Amount': 200000 }
    ]
}";

var data = JObject.Parse(json);

// Using the extension method
var results = data.Query("SELECT CustomerName, Amount FROM $.Transactions WHERE Amount > 1000000");

// Process results
foreach (var result in results)
{
    Console.WriteLine($"Customer: {result["CustomerName"]}, Amount: {result["Amount"]}");
}
```

---

📚 **Want to see more examples and implementation details?**  
Check out our Wiki page:  
👉 [JQL.Net Wiki](https://github.com/fadhly-permata/JQL.Net/wiki)  
You'll find tons of examples, best practices, and complete guides to level up your JQL.Net game! 🚀


---

## 💡 Cool Examples

### Complex Joins (The 'AND/OR' Power) 🦾
```sql
SELECT u.name, o.order_date
FROM $.users AS u
JOIN $.orders AS o ON u.id == o.user_id AND o.status == 'completed'
```

### Aggregations & Having 📊
```sql
SELECT city, AVG(age) AS avg_age, COUNT(*) AS user_count
FROM $.users
GROUP BY city
HAVING user_count > 5
```

---

## 🛠️ Supported Keywords

| Keyword | Description | Example | Status |
| :--- | :--- | :--- | :--- |
| `SELECT` | Choose which fields to return (supports aliases with `AS`). | `SELECT name, age AS UserAge` | ✅ Fully Supported |
| `FROM` | Define the JSON path to query (supports aliases with `AS`). | `FROM $.users AS u` | ✅ Fully Supported |
| `WHERE` | Filter results using comparison operators (`==`, `!=`, `>`, `<`, `>=`, `<=`). | `WHERE age > 25 AND city == 'Jakarta'` | ✅ Fully Supported |
| `GROUP BY`| Group results by specified fields. | `GROUP BY department` | ✅ Fully Supported |
| `HAVING` | Filter grouped results (used after `GROUP BY`). | `HAVING COUNT(*) > 5` | ✅ Fully Supported |
| `ORDER BY`| Sort results in ascending order. | `ORDER BY name, age DESC` | ✅ Fully Supported |
| `JOIN` | Combine JSON arrays with `ON` conditions | `JOIN $.orders AS o ON u.id == o.user_id` | ⚠️ Experimental |
| Aggregate Functions | `SUM()`, `COUNT()`, `AVG()`, `MIN()`, `MAX()` | `SELECT AVG(age) AS AvgAge` | ✅ Fully Supported |

### 🔍 Operator Support:
- **Comparison**: `==`, `!=`, `>`, `<`, `>=`, `<=`
- **Logical**: `AND`, `OR` (in `WHERE` and `HAVING` clauses)
- **Aliasing**: `AS` keyword for field and table aliases

### ⚠️ Important Notes:
- String values must be enclosed in single quotes: `'value'`
- Field names with spaces or special characters should use bracket notation: `$.['field name']`
- Case-insensitive keywords (e.g., `select` or `SELECT` both work)
- Use `$.` prefix for root-relative paths
- JOIN functionality is currently experimental and may have limitations

---

## 🗺️ Roadmap & Future Fun

We're just getting started! Here's what's cooking for upcoming releases:

### ⚡ Core Query Magic
- [ ] **JOIN Improvements**: Enhanced JOIN capabilities with multiple conditions
- [ ] **DISTINCT**: Say goodbye to duplicate rows!
- [ ] **Pagination**: LIMIT & OFFSET support
- [ ] **Subqueries**: Queries inside queries
- [ ] **Set Operations**: UNION & UNION ALL

### 🔍 Smarter Filtering
- [ ] **Pattern Matching**: LIKE & REGEX support
- [ ] **Ranges & Sets**: IN and BETWEEN operators
- [ ] **Conditional Logic**: CASE WHEN expressions
- [ ] **Null Safety**: IS NULL & IS NOT NULL

### 🛠️ Functions Galore
- [ ] **String Functions**: CONCAT, UPPER, LOWER
- [ ] **Date & Time Functions**: YEAR(), MONTH(), DAY()
- [ ] **Type Casting**: CAST() function
- [ ] **Custom Functions**: Extend with your own C# logic

### 🚀 Performance & Integrations
- [ ] **Query Caching**: Faster repeated queries
- [ ] **Prepared Statements**: Parameterized queries
- [ ] **Async Execution**: ExecuteAsync support
- [ ] **Multi-Format Export**: CSV, XML, DataTable

---

## 🤝 Contributing

Got a cool idea or found a bug? 🐛
1. Fork it!
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request.

---

## 📄 License

This project is licensed under the **MIT License**.

---

**Made with ❤️ for the .NET Community.** *If you find JQL.Net useful, give it a ⭐ on GitHub!*