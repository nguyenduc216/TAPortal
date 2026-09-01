using TAPortal.DbMcp.Data;
using TAPortal.DbMcp.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddSingleton<SqlConnectionFactory>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TAPortal.DbMcp",
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapMcp("/mcp");

app.Run();
