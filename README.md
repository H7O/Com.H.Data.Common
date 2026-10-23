# Com.H.Data.Common
Run SQL on any ADO.NET connection and get the rows back as **strongly typed objects** (`ExecuteQuery<T>`, `ExecuteQueryAsync<T>`) or as **dynamic objects** (`ExecuteQuery`, `ExecuteQueryAsync`), streamed as `IEnumerable<T>` or `IAsyncEnumerable<T>`. The methods extend `DbConnection` and `DbCommand`, and `ExecuteCommand` / `ExecuteCommandAsync` run statements that return no rows. Parameters written as `{{name}}` are always sent as real database parameters, never concatenated into the SQL. Their names can hold any characters, so a JSON body's keys work as they are (`{{first name}}`, `{{e-mail}}`). An error the database raises after the rows (SQL Server's `THROW` after a `SELECT`) is thrown to you when you read the rows to their end; stopping earlier (`First`, `FirstOrDefault`, `Take`, a `foreach` left with `break`) can lose it.

Works with **every ADO.NET database** — SQL Server, PostgreSQL, MySQL, SQLite, Oracle, ODBC, OleDb, and more. ODBC/OleDb databases that normally require positional `?` parameters are transparently given the same friendly `{{named}}` parameter syntax, so you write one style of query for all providers.

Since the library executes arbitrary SQL, you can run anything your database supports: stored procedures, CTEs, window functions, user-defined functions, temp tables, dynamic SQL, and more.

## Supported runtimes

The package ships builds for:

| Target | Runtimes it covers |
|--------|---------------------|
| `net10.0` | .NET 10 |
| `net9.0` | .NET 9 |
| `net8.0` | .NET 8 |
| `netstandard2.0` | .NET Framework 4.6.1+ (including 4.8.1), .NET Core 2.0+, Mono 5.4+, Xamarin, Unity, UWP |

The full feature set — including `IAsyncEnumerable<dynamic>`, `IAsyncDisposable`, `await foreach`, typed-generic queries via `ExecuteQuery<T>`, nested JSON/XML parsing, and the named-parameter transform for ODBC/OleDb — is available on **every** target, including .NET Standard 2.0. Legacy .NET Framework 4.x consumers get the same async streaming API as .NET 10 consumers.

## Installation
Best way to install this library is via NuGet package manager [Com.H.Data.Common](https://www.nuget.org/packages/Com.H.Data.Common).

## Parameters

Write `{{name}}` in the SQL wherever a value goes. Each one is sent as a real database parameter (`@vxv_1_name` on SQL Server, `:vxv_1_name` on Oracle, `?` on ODBC and OleDb), so a value is never part of the SQL text.

- **Values** come from an anonymous object, an object's public properties, an `IDictionary<string, object>`, a `System.Text.Json.JsonElement` or JSON text. A JSON object's top-level keys are the names: a string arrives as `string`, a number as `double` (an integer from 2^53 on, of either sign, as `long` while it fits one: from there a double can't tell neighbouring integers apart, so `9007199254740993` keeps its last digit), `true` and `false` as `bool`, and a nested object or array as its JSON text.
- **Names can hold any characters** except a line break and `}}`, and can't end with `}` (the closing `}}` would start there), so a JSON body's keys work as they are: `{{first name}}`, `{{e-mail}}`, `{{prénom}}`. Spaces count too: `{{ name }}` is named ` name `, not `name`. The name you write is independent of the SQL parameter's name, which keeps only ASCII letters, digits and underscores and is made unique. You never rename an input to suit the database.
- **Names are matched ignoring case**: `{{Email}}` and `{{email}}` are the same value and one parameter. A name used several times is one parameter too (on ODBC and OleDb, one `?` per use, each bound to that value).
- **A missing or null value is `NULL`**: the query still runs, so test an optional input with `IS NULL`. This needs a parameters object, even an empty one (`new { }`): called without one (or with `null`), the SQL is sent as written, `{{name}}` included, and fails.
- **Other delimiters**: pass a regular expression with the groups `open_marker`, `param` and `close_marker`, such as `(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])` for `[[name]]`.
- **Several sources**: pass a `List<DbQueryParams>`, each with its own `DataModel` and `QueryParamsRegex`. A placeholder takes its value from the last source in the list whose pattern matches it and whose value isn't null. So a marker only one pattern matches (`{qs{city}}` below) reads only that source, and one several patterns match (`{{first name}}`) falls back to an earlier source when a later one has no value. Every source keeps its own place in the list. Sources with the exact same pattern string act as one source, though: when a later one has a name, even with a null value, the earlier ones with that pattern skip it, so its null falls back only to sources with other patterns. So give a source its own pattern when its nulls should fall back to an earlier source like it. A source listed between two that share a pattern takes a placeholder where its own pattern reads that same placeholder: with both markers, every use of its text, as its own pass would; with one marker or none, only the uses the shared pattern matches there too (as the text stands when that pattern reads it, not the uses it recovers from the text as written after a stray marker). It binds the plain spelling of its spaced placeholder (`{{id}}` for `{{ id }}`) only where its own pass still has that placeholder, or where they have a value for that spelling: not through one it takes from them under another name (` id `) where they have none (that use then gets what it would without this source), nor through one a stray marker pairs past. Where it reads a longer or shorter one at that spot (`:x-1` against `:x`), the earlier source's value is used. On ODBC and OleDb it takes nothing in one more case: see the ODBC & OleDb note under Supported databases.

```csharp
using Com.H.Data.Common;
using System.Text.Json;

// A request body, passed as it arrives: no DTO, no renamed keys.
var body = JsonDocument.Parse("""
    { "first name": "Ann", "e-mail": "ann@example.com", "age": 31 }
    """).RootElement;

await dc.ExecuteCommandAsync(@"
    INSERT INTO people (first_name, email, age)
    VALUES ({{first name}}, {{e-mail}}, {{age}})", body);

// The same body together with a query string.
var queryString = new Dictionary<string, object> { ["city"] = "Lisbon" };
var sources = new List<DbQueryParams>
{
    new() { DataModel = body, QueryParamsRegex = @"(?<open_marker>\{\{|\{j\{)(?<param>.*?)?(?<close_marker>\}\})" },
    new() { DataModel = queryString, QueryParamsRegex = @"(?<open_marker>\{\{|\{qs\{)(?<param>.*?)?(?<close_marker>\}\})" },
};
// {{first name}} comes from the body, because the query string has no value for it.
// {j{age}} reads only the body, and {qs{city}} only the query string.
await using var people = await dc.ExecuteQueryAsync(@"
    SELECT * FROM people
    WHERE first_name = {{first name}} AND age >= {j{age}} AND city = {qs{city}}", sources);
```

## Filling text templates

`Fill` puts values into plain text (a message, a URL, a JSON document) with the same placeholders, sources and precedence as the query methods. It inserts each value as text instead of sending it as a parameter, so never use it to build SQL.

```csharp
// The sources from the Parameters section: a JSON body, then a query string.
var message = "Hi {{first name}}, you are {{age}}. City: {qs{city}}".Fill(sources);
// "Hi Ann, you are 31. City: Lisbon"
```

- **A value is inserted once**: text inside a value that looks like a placeholder (`{{other}}`, or another source's marker) stays as it is, so a value someone else supplied can't pull in another source's value.
- **A missing or null value** becomes `nullReplacementString` (empty by default). A placeholder that no pattern matches stays as it is.
- **Where the text is replaced**: with an open and a close marker, the text a placeholder takes up (`{{ name }}` under a pattern that allows spaces) is replaced wherever it appears, once the pattern has matched it on its own somewhere in the text as written. A stray open marker before a placeholder's only use pairs with that use's close marker, and the whole span between them is then a placeholder of its own. When no source has a value for it, another source's stray open marker can take a use of it that way too. If the span holds a placeholder another source has filled, the span usually stays as written instead, and a placeholder the pattern never matched on its own there stays unfilled. With one marker (`:name`), the text is replaced wherever it doesn't go on into a longer name: a letter, digit or underscore after it, or a longer placeholder the pattern matched. So `:date` leaves `:date_from` alone, and `:date-from` too under a pattern that allows `-` in names. With neither marker, only the pattern's matches are replaced.
- **Names are matched ignoring case**, unless you pass `caseSensitive: true`: `{{NAME}}` is then filled only from a `NAME` value.
- **`valueConverter`** turns each value into text. It is called once for each name in each source that fills it, however many times and with whichever markers the name appears. Without it, `ToString()` is used.

## Sample 1
This sample demonstrates how to execute a simple query without parameters on a SQL Server Database.

To run this sample, you need to:
1) Create a new console application
2) Add NuGet package [Com.H.Data.Common](https://www.nuget.org/packages/Com.H.Data.Common)  
3) Add NuGet package [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient)
4) Copy and paste the following code into your Program.cs file:

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"connection string goes here";
using DbConnection dc = new SqlConnection(conStr);
// ^ note the use of DbConnection instead of SqlConnection. The extension methods 
// are defined on DbConnection
// Also note the use of 'using' keyword to ensure proper disposal of the connection

using var result = dc.ExecuteQuery("select 'John' as name, '123' as phone");
// ^ returns DbQueryResult<dynamic> which implements IEnumerable<dynamic> and IDisposable
// You can also return DbQueryResult<T> where T is your data model class by using the ExecuteQuery<T> method.
// The 'using' keyword ensures proper disposal of database resources (reader and optionally connection)

// Example: using var result = dc.ExecuteQuery<YourDataModelClass>("select 'John' as name, '123' as phone");
// Also, returns DbAsyncQueryResult<dynamic> when called asynchronously via dc.ExecuteQueryAsync() 
// or dc.ExecuteQueryAsync<T>()
// And for executing a command that does not return any data, you can use the ExecuteCommand() 
// or ExecuteCommandAsync() methods


foreach (var item in result)
{
    System.Console.WriteLine($"name = {item.name}, phone = {item.phone}");
}
```
> **Note**: The returned DbQueryResult (whether `DbQueryResult<dynamic>` or `DbQueryResult<T>`) implements the `IEnumerable<T>` interface and `IDisposable` interface, which means you can use it anywhere `IEnumerable<T>` is expected and should be disposed properly using the `using` keyword.<br/>
For example, you can use it in a foreach loop, or pass it to LINQ methods like Where, Select, etc.<br/>
The asynchronous version returns `DbAsyncQueryResult<T>` which implements `IAsyncEnumerable<T>` and `IAsyncDisposable`.

> **Note**: Each dynamic row is a `DbQueryRow`. Read a column as a member (`item.name`), or through `IDictionary<string, object?>` (`row["first name"]`) when its name isn't a C# identifier. Names are case-sensitive, and a database `NULL` is `null`. The rows of one result share their column names, so a row costs little more than its values. System.Text.Json writes a row as a JSON object of its columns.


## Sample 2
This sample demonstrates how to pass parameters to your SQL query

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"your connection string goes here";
using DbConnection dc = new SqlConnection(conStr);
// ^ note the use of 'using' keyword to ensure proper disposal of the connection

var queryParams = new { name = "Jane" };
// ^ queryParams could be an anonymous object (similar to the example above)
// or the following types:
// 1) IDictionary<string, object>
// 2) Normal object with properties that match the parameter names in the query
// 3) JSON string
// 4) System.Text.Json.JsonElement (useful when building Web APIs, allows passing 
//    JsonElement input directly from a web client)
// Example 1: var queryParams = new Dictionary<string, object> { { "name", "Jane" } }
// Example 2: var queryParams = new MyCustomParamClass { name = "John" }
// Example 3: var queryParams = "{\"name\":\"Jane\"}"
// Example 4: var queryParams = System.Text.Json.JsonDocument.Parse("{\"name\":\"John\"}").RootElement


using var result = dc.ExecuteQuery(@"
	select * from (values 
		('John', '55555'), 
		('Jane', '44444')) as t (name, phone)
	where name = {{name}}", queryParams
);
// ^ note the use of curly braces around the parameter name in the query. 
// This is a special syntax that allows you to pass parameters to your query.
// The parameter name is matched to the queryParams object's property names ignoring case
// (see Parameters above).
// It also protects you from SQL injection attacks and is configurable to use other 
// delimiters by passing a regular expression 
// Also note the use of 'using' keyword to ensure proper disposal of database resources

 
// Example 1: using `[[` and `]]` instead of `{{` and `}}` dc.ExecuteQuery(@"
//	select * from (values ('John', '55555'), ('Jane', '44444')) as t (name, phone)
//	where name = [[name]]", 
//  queryParams, @"(?<open_marker>\[\[)(?<param>.*?)?(?<close_marker>\]\])" );

// Example 2: using `|` instead of `{{` and `}}` dc.ExecuteQuery(@"
//	select * from (values ('John', '55555'), ('Jane', '44444')) as t (name, phone)
//	where name = |name|", 
//  queryParams, @"(?<open_marker>\|)(?<param>.*?)?(?<close_marker>\|)" );



foreach (var item in result)
{
    System.Console.WriteLine($"name = {item.name}, phone = {item.phone}");
}
```

## Sample 3
This sample demonstrates how to return nested hierarchical data from a query.

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"your connection string goes here";
using DbConnection dc = new SqlConnection(conStr);
// ^ note the use of 'using' keyword to ensure proper disposal of the connection


using var result = dc.ExecuteQuery(@"
SELECT 
    'John' as [name],
    (select * from (values 
		('55555', 'Mobile'), 
		('44444', 'Work')) 
        as t (number, [type]) for json path) AS {type{json{phones}}}");
// ^ note the use of 'using' keyword to ensure proper disposal of database resources

foreach (var person in result)
{
    Console.WriteLine($"name = {person.name}");
    Console.WriteLine("--- phones ---");
    foreach (var phone in person.phones)
    {
        System.Console.WriteLine($"{phone.type} = {phone.number}");
    }
    
}
```
Microsoft SQL Server natively supports returning JSON data from a query using the `FOR JSON` clause.

The normal example of returning JSON data from a query would look like this:
```sql
SELECT 
    'John' as [name],
    (select * from (values 
		('55555', 'Mobile'), 
		('44444', 'Work')) as t (number, [type]) for json path) AS phones
```

However, the above query returns a JSON string that you would have to parse in your application.

This library automatically takes care of that parsing process for you and returns a dynamic object that you can access using the property names in the query.

To tell the library to parse the nested JSON data, you just need to enclose the property name (that you expect to have json string) in the following syntax: `{type{json{your_property_name}}}`.

In our example above, we are filling the property `phones` with JSON string. Hence we used the syntax `{type{json{phones}}}` to tell the library to parse the JSON string and fill the `phones` property with the parsed JSON data.

> **Note**: Another syntax for parsing XML string is `{type{xml{your_property_name}}}`.

## Sample 4
This sample shows when a reader is still open, and how to close it before you run another query on the same connection.

A reader stays open while its rows are being read (an enumerator you hold, or a `foreach` that is still running) and for a result you haven't read. On SQL Server without MARS, and on other databases that allow one open reader per connection, another query on that connection then throws `There is already an open DataReader associated with this Connection which must be closed first`. Reading the rows to their end closes the reader, and so does an enumerator disposed before the end (`First`, `FirstOrDefault`, `Take`, a `foreach` left with `break`). To close it yourself, dispose the result or call `CloseReader`.

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"your connection string goes here";
using DbConnection dc = new SqlConnection(conStr);
// ^ note the use of 'using' keyword to ensure proper disposal of the connection

using var result = dc.ExecuteQuery("SELECT * FROM Users");
using var users = result.GetEnumerator();
users.MoveNext(); // the reader stays open while this enumerator is held

result.CloseReader(); // closes the reader; disposing the result does too

using var anotherResult = dc.ExecuteQuery("SELECT * FROM Orders"); // Safe! No reader is open on dc
// ^ note the use of 'using' keyword to ensure proper disposal of database resources
```

### Errors raised after the rows you read

Only the first result set's rows are returned. Reading them to the end also moves through the rest of the batch, so an error raised after them (SQL Server's `THROW` or `RAISERROR` after a `SELECT`, or a later statement that fails) is thrown to you. The later result sets' rows are skipped, not read: SQLite computes only a later result set's first row, so an error raised in one of its other rows is not raised, and System.Data.Odbc reports such an error in some batches and not in others, so don't rely on it. Stopping early does not do that: an enumerator disposed before it reports the end of the rows (a `foreach` left with `break`, `First` or `FirstOrDefault`, `Take`, even on the last row) closes the reader. Many providers, Microsoft.Data.SqlClient, System.Data.SqlClient, Microsoft.Data.Sqlite and System.Data.OleDb among them, discard the remaining rows, the later statements' results and their errors when the reader closes, so the call looks like a success. Some read the rest of the batch when the reader closes and throw from there, so stopping early can throw: Npgsql (PostgreSQL) throws the batch's error, and System.Data.Odbc throws when a later statement or its first row fails, while an error raised in a row not yet read is reported in some batches and dropped in others.

When you need only some of the rows but must not miss such an error, read the rest too, or raise the error before the statement that returns rows:

```csharp
await using var result = await dc.ExecuteQueryAsync(@"
    SELECT id, name FROM Users WHERE id = {{id}};
    UPDATE Users SET last_seen = GETDATE() WHERE id = {{id}};", new { id = 7 });

var rows = new List<dynamic>();
await foreach (var row in result) rows.Add(row); // reads to the end, so a failing UPDATE throws here
var user = rows.FirstOrDefault();
```

## Sample 5
This sample demonstrates how to use the library in an ASP.NET Core Web API controller with dependency injection.

**Key Benefits**: 
- **No DTOs needed**: Work directly with dynamic query results without creating DTOs (Data Transfer Objects) or entity classes. This eliminates boilerplate code and allows you to focus on your business logic rather than maintaining mapping classes.
- **Externalize queries**: Store your SQL queries in external files, allowing you to modify business logic and data retrieval without recompiling your application. This is especially useful for rapid iteration and production hotfixes.

First, register the `DbConnection` in your `Program.cs`:

```csharp
using Microsoft.Data.SqlClient;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

// Register DbConnection as scoped (new connection per request)
builder.Services.AddScoped<DbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string not found");
    return new SqlConnection(connectionString);
});

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

Then use it in your controller:

```csharp
using Com.H.Data.Common;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;
using System.Text.Json;

namespace YourApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly DbConnection _connection;

        public UsersController(DbConnection connection)
        {
            _connection = connection;
        }

        // GET: api/users
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            // ExecuteQueryAsync returns DbAsyncQueryResult which implements IAsyncEnumerable
            // Register it for disposal when the request completes
            var result = await _connection.ExecuteQueryAsync("SELECT id, name, email FROM Users");
            HttpContext.Response.RegisterForDisposeAsync(result);
            
            // Return the result directly - ASP.NET Core will stream it as JSON
            return Ok(result);
        }

        // GET: api/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var queryParams = new { id };
            await using var result = await _connection.ExecuteQueryAsync(
                "SELECT id, name, email FROM Users WHERE id = {{id}}", 
                queryParams);
            
            var user = await result.FirstOrDefaultAsync();
            
            if (user == null)
                return NotFound();
                
            return Ok(user);
        }

        // POST: api/users/search
        [HttpPost("search")]
        public async Task<IActionResult> SearchUsers([FromBody] JsonElement searchParams)
        {
            // JsonElement can be passed directly as query parameters
            // Client can send: { "name": "John", "minAge": 25 }
            var result = await _connection.ExecuteQueryAsync(@"
                SELECT id, name, email, age 
                FROM Users 
                WHERE name LIKE '%' + {{name}} + '%' 
                AND age >= {{minAge}}", 
                searchParams);
            
            // Register for disposal when the request completes
            HttpContext.Response.RegisterForDisposeAsync(result);
            
            // Return the result directly - it will be streamed to the client
            return Ok(result);
        }

        // POST: api/users
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] JsonElement userData)
        {
            // For insert/update/delete operations, use ExecuteCommandAsync
            await _connection.ExecuteCommandAsync(@"
                INSERT INTO Users (name, email, age) 
                VALUES ({{name}}, {{email}}, {{age}})", 
                userData);
            
            return Ok(new { message = "User created successfully" });
        }

        // GET: api/users/{id}/orders
        // Example with nested JSON data
        [HttpGet("{id}/orders")]
        public async Task<IActionResult> GetUserWithOrders(int id)
        {
            var queryParams = new { userId = id };
            await using var result = await _connection.ExecuteQueryAsync(@"
                SELECT 
                    u.id,
                    u.name,
                    u.email,
                    (SELECT o.orderId, o.orderDate, o.total 
                     FROM Orders o 
                     WHERE o.userId = u.id 
                     FOR JSON PATH) AS {type{json{orders}}}
                FROM Users u
                WHERE u.id = {{userId}}", 
                queryParams);
            
            var user = await result.FirstOrDefaultAsync();
            
            if (user == null)
                return NotFound();
                
            return Ok(user);
        }
    }
}
```

> **Note**: 
> - The `DbConnection` is registered as **scoped**, meaning a new connection is created per HTTP request and automatically disposed when the request completes
> - You don't need to manually dispose the connection - ASP.NET Core's DI container handles that
> - For production environments, also consider:
>   - Implementing proper error handling and logging
>   - Adding authentication and authorization
>   - Implementing request validation
>   - Using connection pooling (enabled by default in most ADO.NET providers)




## Sample 6
This sample demonstrates how to externalize your SQL queries in a configuration file, allowing you to modify queries without recompiling your application.

> **Note**: To use XML configuration files, you'll need to install the `Microsoft.Extensions.Configuration.Xml` NuGet package:
> ```
> dotnet add package Microsoft.Extensions.Configuration.Xml
> ```

First, create an XML file named `queries.xml` in your project root:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<settings>
  <queries>
    
    <!-- Get all users -->
    <get_all_users>
    <![CDATA[
      SELECT id, name, email, createdDate 
      FROM Users 
      ORDER BY createdDate DESC
    ]]>
    </get_all_users>
    
    <!-- Search users by name and age -->
    <search_users>
    <![CDATA[
      SELECT id, name, email, age 
      FROM Users 
      WHERE name LIKE '%' + {{name}} + '%' 
      AND age >= {{minAge}}
      ORDER BY name
    ]]>
    </search_users>
    
    <!-- Get user with orders (nested JSON) -->
    <get_user_with_orders>
    <![CDATA[
      SELECT 
          u.id,
          u.name,
          u.email,
          (SELECT o.orderId, o.orderDate, o.total 
           FROM Orders o 
           WHERE o.userId = u.id 
           FOR JSON PATH) AS {type{json{orders}}}
      FROM Users u
      WHERE u.id = {{userId}}
    ]]>
    </get_user_with_orders>
    
  </queries>
</settings>
```

Configure your `Program.cs` to load the queries file with hot-reload support:

```csharp
using Microsoft.Data.SqlClient;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

// Load queries from external XML file with hot-reload enabled
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddXmlFile("queries.xml", optional: false, reloadOnChange: true);

// Register DbConnection as scoped
builder.Services.AddScoped<DbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string not found");
    return new SqlConnection(connectionString);
});

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

Now use the queries in your controller:

```csharp
using Com.H.Data.Common;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;
using System.Text.Json;

namespace YourApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly DbConnection _connection;
        private readonly IConfiguration _configuration;

        public UsersController(DbConnection connection, IConfiguration configuration)
        {
            _connection = connection;
            _configuration = configuration;
        }

        // GET: api/users
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            // Load query from configuration file
            var query = _configuration["queries:get_all_users"] 
                ?? throw new InvalidOperationException("Query 'get_all_users' not found");
            
            var result = await _connection.ExecuteQueryAsync(query);
            HttpContext.Response.RegisterForDisposeAsync(result);
            
            return Ok(result);
        }

        // POST: api/users/search
        [HttpPost("search")]
        public async Task<IActionResult> SearchUsers([FromBody] JsonElement searchParams)
        {
            // Load query from configuration file
            var query = _configuration["queries:search_users"]
                ?? throw new InvalidOperationException("Query 'search_users' not found");
            
            var result = await _connection.ExecuteQueryAsync(query, searchParams);
            HttpContext.Response.RegisterForDisposeAsync(result);
            
            return Ok(result);
        }

        // GET: api/users/{id}/orders
        [HttpGet("{id}/orders")]
        public async Task<IActionResult> GetUserWithOrders(int id)
        {
            // Load query from configuration file
            var query = _configuration["queries:get_user_with_orders"]
                ?? throw new InvalidOperationException("Query 'get_user_with_orders' not found");
            
            var queryParams = new { userId = id };
            await using var result = await _connection.ExecuteQueryAsync(query, queryParams);
            
            var user = await result.FirstOrDefaultAsync();
            
            if (user == null)
                return NotFound();
                
            return Ok(user);
        }
    }
}
```

> **Note**: 
> - With `reloadOnChange: true`, you can modify queries in the `queries.xml` file and the changes will be picked up automatically without restarting the application
> - This is extremely useful for production hotfixes, query optimization, and rapid iteration
> - Make sure to set the XML file's "Copy to Output Directory" property to "Copy if newer" or "Copy always" in your project settings
> - **XML is recommended over JSON** for storing queries because XML's `CDATA` tags allow you to write queries without worrying about escaping special characters (quotes, backslashes, etc.) that would be required in JSON files
> - You can organize queries into multiple files for better maintainability (e.g., `users_queries.xml`, `orders_queries.xml`, `reports_queries.xml`) by calling `AddXmlFile()` multiple times:
>   ```csharp
>   builder.Configuration
>       .AddXmlFile("users_queries.xml", optional: false, reloadOnChange: true)
>       .AddXmlFile("orders_queries.xml", optional: false, reloadOnChange: true)
>       .AddXmlFile("reports_queries.xml", optional: false, reloadOnChange: true);
>   ```


## Sample 7
This sample demonstrates how to dynamically load multiple configuration files based on settings, allowing you to maintain a modular configuration structure.

First, create a `config` folder in your project root to organize your configuration files. This keeps your project structure clean and separates queries from application code.

Then, create your `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=myserver;Database=mydb;User Id=myuser;Password=mypass;"
  },
  "AdditionalConfigurations": {
    "Paths": [
      "config/users_queries.xml",
      "config/orders_queries.xml",
      "config/reports_queries.xml",
      "config/custom_settings.json"
    ]
  }
}
```

Configure your `Program.cs` to dynamically load all configuration files:

```csharp
using Microsoft.Data.SqlClient;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

// Dynamically load additional configuration files specified in appsettings.json
var additionalConfigsSection = builder.Configuration.GetSection("AdditionalConfigurations:Paths");
if (additionalConfigsSection.Exists())
{
    var additionalConfigPaths = additionalConfigsSection.Get<List<string>>();
    
    if (additionalConfigPaths?.Any() == true)
    {
        foreach (var configPath in additionalConfigPaths)
        {
            // Determine file type by extension and load accordingly
            var extension = Path.GetExtension(configPath);
            
            if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                builder.Configuration.AddXmlFile(configPath, optional: false, reloadOnChange: true);
            }
            else if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
            }
            // Add more file types as needed (e.g., .ini, .yaml, etc.)
        }
    }
}

// Register DbConnection as scoped
builder.Services.AddScoped<DbConnection>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string not found");
    return new SqlConnection(connectionString);
});

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

> **Note**: 
> - This pattern allows you to maintain a master configuration file that references other configuration files
> - You can add or remove query files by simply updating the `appsettings.json` without changing code
> - **Important**: If you add new configuration files to the `AdditionalConfigurations:Paths` list, you'll need to restart the application (or recycle the app pool in IIS) for the changes to take effect, since configuration files are loaded during application startup (before the `builder.Build()` call)
> - **However**, once files are loaded, you can freely add new queries or modify existing queries within those files without restarting - all changes are picked up automatically thanks to `reloadOnChange: true`. This means you can continuously update your queries in production without any downtime!
> - This is especially useful for large applications where different teams maintain different query sets
> - You can mix XML and JSON files based on your needs (remember: XML with CDATA is better for SQL queries)
> - Consider organizing files by feature or domain (e.g., `config/users_queries.xml`, `config/inventory_queries.xml`, `config/analytics_queries.xml`)
> - **Don't forget** to set the "Copy to Output Directory" property to "Copy if newer" or "Copy always" for all files in the `config` folder


## What other databases this library supports?
This library works with **any ADO.NET provider** that implements `DbConnection` and `DbCommand`. While the samples above use SQL Server, you can use the same code with PostgreSQL, MySQL, SQLite, Oracle, and many others—just swap out the connection class.

### Supported databases (tested and auto-configured)

The following databases are automatically recognized and require no additional configuration:

| Database | ADO.NET Provider | Works out of the box |
|----------|------------------|----------------------|
| SQL Server | Microsoft.Data.SqlClient, System.Data.SqlClient | ✅ |
| PostgreSQL | Npgsql | ✅ |
| MySQL / MariaDB | MySql.Data, MySqlConnector | ✅ |
| SQLite | Microsoft.Data.Sqlite | ✅ |
| Oracle | Oracle.ManagedDataAccess | ✅ |
| DB2 | IBM.Data.DB2 | ✅ |
| Firebird | FirebirdSql.Data.FirebirdClient | ✅ |
| SAP HANA | Sap.Data.Hana | ✅ |
| Snowflake | Snowflake.Data | ✅ |
| ClickHouse | ClickHouse.Client | ✅ |
| Teradata | Teradata.Client | ✅ |
| Informix | IBM.Data.Informix | ✅ |
| DuckDB | DuckDB.NET.Data | ✅ |
| Vertica | Vertica.Data | ✅ |
| SingleStore (MemSQL) | SingleStoreConnector | ✅ |
| SAP SQL Anywhere | Sap.Data.SQLAnywhere | ✅ |
| ODBC | System.Data.Odbc | ✅ |
| OleDb | System.Data.OleDb | ✅ |

> **PostgreSQL-compatible databases**: CockroachDB, TimescaleDB, YugabyteDB, CrateDB, and other PostgreSQL wire-compatible databases work automatically through the Npgsql provider — no additional configuration needed.

> **ODBC & OleDb — named parameters on positional-parameter databases**: ODBC and OleDb databases natively use positional `?` parameters, where parameter order matters and each occurrence requires its own parameter object. This library transparently handles the conversion — you write your queries using the same friendly `{{named}}` parameter syntax as any other database, and the library automatically transforms them into correctly ordered positional `?` parameters behind the scenes. This means you get the same clean, readable, named-parameter experience across **all** ADO.NET providers. Placeholders take their values by the same rules as on other databases, including with several parameter sources. One difference: here only the pattern's matches are replaced, while on other databases a placeholder that has a value is also replaced by its text where the pattern doesn't match it (a use after a stray `{{` in a string, which the pattern pairs with it, or a use the pattern skips). A NULL placeholder is replaced only where the pattern matches it, on every database. And a source listed between two that share a pattern takes nothing here where another source with a value has a placeholder that overlaps one of this source's pattern's matches without being the same one (a stray `{{` paired past one, `:date-x` where this source reads `:date`): the earlier source's value is used. That source counts if it is listed after this one and the last source with its exact pattern string is listed no later than the last of the two. The placeholders of a source whose last source with its exact pattern string is listed after the last of the two are filled before any of the three reads the query, so they don't count; those of the other sources (one listed before this one, say) are still as written then. The overlap is the one in the query as it then stands.

### Using a database not listed above?

If you're using a less common database provider that isn't in the list above, the library will still work. However, you may need to configure the **parameter prefix** manually.

**What is a parameter prefix?** When you use parameterized queries like `WHERE id = {{id}}`, the library converts your placeholder into a database-specific parameter (e.g., `@vxv_1_id` for SQL Server, `:vxv_1_id` for Oracle). Different databases use different prefix symbols.

The library automatically detects the correct prefix for all databases listed above. For unlisted providers, it defaults to `@` (which works for most databases). If your database uses a different prefix, set the fallback:

```csharp
// Only needed for unrecognized database providers that don't use '@'
Com.H.Data.Common.AdoNetExt.DefaultParameterPrefix = "<your database's parameter prefix>";
```

You can also check what prefix the library detected for any connection:

```csharp
string prefix = Com.H.Data.Common.AdoNetExt.GetParameterPrefix(connection);
Console.WriteLine($"Using parameter prefix: {prefix}");
```

## What other features this library has?
This small library has several other options that allow for more advanced features that might not be of much use to most, hence samples for those features have been left out in this quick `how to` documentation.