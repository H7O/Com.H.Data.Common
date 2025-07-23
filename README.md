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
DbConnection dc = new SqlConnection(conStr);
// ^ note the use of DbConnection instead of SqlConnection. The extension methods 
// are defined on DbConnection

var result = dc.ExecuteQuery("select 'John' as name, '123' as phone");
// ^ returns IEnumerable<dynamic>, you can also return IEnumerable<T> where T is your data model class by using the ExecuteQuery<T> method.

// Example: var result = dc.ExecuteQuery<YourDataModelClass>("select 'John' as name, '123' as phone");
// Also, returns IAsyncEnumerable when called asynchronously via dc.ExecuteQueryAsync() 
// or dc.ExecuteQueryAsync<T>()
// And for executing a command that does not return any data, you can use the ExecuteCommand() 
// or ExecuteCommandAsync() methods


foreach (var item in result)
{
    System.Console.WriteLine($"name = {item.name}, phone = {item.phone}");
}
```
> **Note**: The returned IEnumerable (whether `IEnumerable<dynamic>` or `IEnumerable<T>`) is an an IEnumerable derived class that implements the `IEnumerable<T>` interface, which means you can use it anywhere    `IEnumerable<T>` is expected.<br/>
For example, you can use it in a foreach loop, or pass it to LINQ methods like Where, Select, etc.


## Sample 2
This sample demonstrates how to pass parameters to your SQL query

```csharp
using Com.H.Data.Common;
using System.Data.Common;
using Microsoft.Data.SqlClient;

string conStr = @"your connection string goes here";
DbConnection dc = new SqlConnection(conStr);

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


var result = dc.ExecuteQuery(@"
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
DbConnection dc = new SqlConnection(conStr);


var result = dc.ExecuteQuery(@"
SELECT 
    'John' as [name],
    (select * from (values 
		('55555', 'Mobile'), 
		('44444', 'Work')) 
        as t (number, [type]) for json path) AS {type{json{phones}}}");

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
DbConnection dc = new SqlConnection(conStr);

var result = dc.ExecuteQuery("SELECT * FROM Users");
var firstUser = result.FirstOrDefault();

result.Dispose(); // Closes any open reader on `dc`

var anotherResult = dc.ExecuteQuery("SELECT * FROM Orders"); // Safe! No reader exception from previous query
```


## What other databases this library supports?
Any ADO.NET provider that implements DbConnection and DbCommand classes should work with this library.

> **Note**: Be mindful of setting the correct parameter prefix for your database provider. 
>
> For example, for SQL Server, the parameter prefix is `@` and for Oracle, it is `:`. 
>
> By default, the library uses `@` as the parameter prefix. 
> To change that, you can change the default symbol by setting the static `DefaultParameterPrefix` property of the `Com.H.Data.Common.AdoNetExt` class.
>
> Oracle example:
> ```csharp
> Com.H.Data.Common.AdoNetExt.DefaultParameterPrefix = ":"; // for Oracle
> ```
>
> SQL Server example (or any other database that uses `@` as the parameter prefix like PostgreSQL, MySQL, etc):
> ```csharp
> Com.H.Data.Common.AdoNetExt.DefaultParameterPrefix = "@";
> ```
> Note that there is no need to set the parameter prefix for SQL Server (or any other database that uses `@` as the parameter prefix) as it is already the default set value.
>

## What other features this library has?
This small library has several other options that allow for more advanced features that might not be of much use to most, hence samples for those features have been left out in this quick `how to` documentation.