DotNetCore.GaussDB is the open source .NET data provider for PostgreSQL. It allows you to connect and interact with GaussDB server using .NET.

This package helps set up GaussDB in applications using dependency injection, notably .NET applications. It allows easy configuration of your GaussDB connections and registers the appropriate services in your DI container. 

For example, if using the ASP.NET minimal web API, simply use the following to register GaussDB:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGaussDBDataSource("Host={host};Username={username};Password={password};Database={database}");
```

```csharp
app.MapGet("/", async (GaussDBConnection connection) =>
{
    await connection.OpenAsync();
    await using var command = new GaussDBCommand("SELECT number FROM data LIMIT 1", connection);
    return "Hello World: " + await command.ExecuteScalarAsync();
});
```

But wait! If all you want is to execute some simple SQL, just use the singleton to execute a command directly:

```csharp
app.MapGet("/", async (GaussDBDataSource dataSource) =>
{
    await using var command = dataSource.CreateCommand("SELECT number FROM data LIMIT 1");
    return "Hello World: " + await command.ExecuteScalarAsync();
});
```

```csharp
app.MapGet("/", async (GaussDBDataSource dataSource) =>
{
    await using var connection1 = await dataSource.OpenConnectionAsync();
    await using var connection2 = await dataSource.OpenConnectionAsync();
    // Use the two connections...
});
```

The `AddGaussDBDataSource` method also accepts a lambda parameter allowing you to configure aspects of GaussDB beyond the connection string, e.g. to configure `UseLoggerFactory`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGaussDBDataSource(
    "Host=pg_server;Username=test;Password=test;Database=test",
    builder => builder
        .UseLoggerFactory(loggerFactory));
```

Finally, starting with DotNetCore.GaussDB and .NET 8.0, you can now register multiple data sources (and connections), using a service key to distinguish between them:

```c#
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGaussDBDataSource("Host=localhost;Database=CustomersDB;Username=test;Password=test", serviceKey: DatabaseType.CustomerDb)
    .AddGaussDBDataSource("Host=localhost;Database=OrdersDB;Username=test;Password=test", serviceKey: DatabaseType.OrdersDb);

var app = builder.Build();

app.MapGet("/", async ([FromKeyedServices(DatabaseType.OrdersDb)] GaussDBConnection connection)
    => connection.ConnectionString);

app.Run();

enum DatabaseType
{
    CustomerDb,
    OrdersDb
}
```