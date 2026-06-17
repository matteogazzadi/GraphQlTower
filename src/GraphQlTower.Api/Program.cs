using GraphQlTower.Api.Data;
using GraphQlTower.Api.Health;
using GraphQlTower.Api.Stitching;
using GraphQlTower.Shared.Interfaces;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .WriteTo.Console());

// SQLite registry
var connectionString = builder.Configuration.GetConnectionString("Registry")
    ?? "Data Source=/data/registry.db";

// SQLite won't create missing parent directories — ensure they exist so the
// app works both locally and with a mounted volume (e.g. /data in Kubernetes).
EnsureSqliteDirectoryExists(connectionString);

builder.Services.AddDbContext<ServiceRegistryDbContext>(opts =>
    opts.UseSqlite(connectionString));
builder.Services.AddScoped<IServiceRegistry, EfServiceRegistry>();

// HttpClient factory (used by schema module and health checks)
builder.Services.AddHttpClient();

// HotChocolate stitching
// DynamicRemoteSchemaModule fires TypesChanged on registry changes so HC evicts
// the executor. Remote schemas are registered via AddRemoteSchema below.
builder.Services.AddSingleton<DynamicRemoteSchemaModule>();
builder.Services
    .AddGraphQLServer()
    .AddTypeModule(sp => sp.GetRequiredService<DynamicRemoteSchemaModule>())
    .AddHttpRequestInterceptor<GatewayRequestInterceptor>();

// Health checks
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ServiceRegistryDbContext>("registry_db")
    .AddCheck<UpstreamServicesHealthCheck>("upstream_services");

// Background health monitor
builder.Services.AddHostedService<UpstreamHealthMonitor>();

// REST API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "GraphQL Tower API", Version = "v1" });
});

// CORS for Blazor admin UI
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();

// Apply EF migrations and seed on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServiceRegistryDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var dbPath = db.Database.GetConnectionString() ?? "(unknown)";
    startupLogger.LogInformation("Database connection string: {ConnectionString}", dbPath);

    var pending = await db.Database.GetPendingMigrationsAsync();
    startupLogger.LogInformation("Pending migrations: {Count} ({Names})",
        pending.Count(), string.Join(", ", pending.Any() ? pending : new[] { "none" }));

    await db.Database.MigrateAsync();

    var applied = await db.Database.GetAppliedMigrationsAsync();
    startupLogger.LogInformation("Applied migrations after MigrateAsync: {Names}",
        string.Join(", ", applied.Any() ? applied : new[] { "none" }));

    var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistry>();
    await DatabaseSeeder.SeedAsync(registry, startupLogger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRouting();
app.MapControllers();

// GraphQL endpoint + BananaCakePop IDE at /graphql/ui
app.MapGraphQL("/graphql");

// K8s liveness probe — fast, just checks process is alive
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// K8s readiness probe — checks DB and upstream services
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    ResponseWriter = WriteHealthCheckResponse
});

await app.RunAsync();

// Creates the directory that holds the SQLite database file if it doesn't already
// exist. SQLite fails with "unable to open database file" when the parent dir is missing.
static void EnsureSqliteDirectoryExists(string connectionString)
{
    var sqlBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
    var dataSource = sqlBuilder.DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        return;

    var fullPath = System.IO.Path.GetFullPath(dataSource);
    var directory = System.IO.Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        Directory.CreateDirectory(directory);
}

static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var result = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            data = e.Value.Data
        })
    });
    await context.Response.WriteAsync(result);
}

// Forwards the Authorization header from the incoming request to all upstream GraphQL calls.
public class GatewayRequestInterceptor : HotChocolate.AspNetCore.IHttpRequestInterceptor
{
    public ValueTask OnCreateAsync(
        HttpContext context,
        HotChocolate.Execution.IRequestExecutor requestExecutor,
        HotChocolate.Execution.IQueryRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var auth))
            requestBuilder.SetGlobalState("Authorization", auth.ToString());

        return ValueTask.CompletedTask;
    }
}
