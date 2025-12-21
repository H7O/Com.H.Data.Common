# Com.H.Data.Common
Adds ExecuteQuery and ExecuteQueryAsync extension methods to DbConntion and DbCommand that returns dynamic data results `IEnumerable<dynamic>` and `IAsyncEnumerable<dynamic>`.

## Installation
Best way to install this library is via NuGet package manager [Com.H.Data.Common](https://www.nuget.org/packages/Com.H.Data.Common).

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
// The parameter name must match the property name in the queryParams object.
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
This sample demonstrates how to close a reader if you're using `ExecuteQuery` or `ExecuteQueryAsync` and partially retrieving records (e.g., using `.FirstOrDefault()` or `.Take(n)`) and wants to execute
another query on the same connection while the reader is still open on the first query (i.e., items of the first query are still being enumerated).
In such cases, without closing the reader of the first query, the second query will throw an exception indicating `There is already an open DataReader associated with this Connection which must be closed first`.


```csharp
using Com.H.Data.Common; 
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Linq;

string conStr = @"your connection string goes here";
using DbConnection dc = new SqlConnection(conStr);
// ^ note the use of 'using' keyword to ensure proper disposal of the connection

using var result = dc.ExecuteQuery("SELECT * FROM Users");
var firstUser = result.FirstOrDefault();

result.Dispose(); // Closes any open reader on `dc`

using var anotherResult = dc.ExecuteQuery("SELECT * FROM Orders"); // Safe! No reader exception from previous query
// ^ note the use of 'using' keyword to ensure proper disposal of database resources
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

> **Note on ODBC, OleDb, and Teradata**: These databases use positional parameters (`?`) instead of named parameters. The current version of this library only supports named parameters. Support for positional parameters is planned for a future release.

### Using a database not listed above?

If you're using a less common database provider that isn't in the list above, the library will still work. However, you may need to configure the **parameter prefix** manually.

**What is a parameter prefix?** When you use parameterized queries like `WHERE id = {{id}}`, the library converts your placeholder into a database-specific parameter (e.g., `@id` for SQL Server, `:id` for Oracle). Different databases use different prefix symbols.

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