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
builder.Services.AddDbContext<ServiceRegistryDbContext>(opts =>
    opts.UseSqlite(connectionString));
builder.Services.AddScoped<IServiceRegistry, EfServiceRegistry>();

// HttpClient factory (used by schema module and health checks)
builder.Services.AddHttpClient();

// HotChocolate stitching — DynamicRemoteSchemaModule is a singleton type module.
// It subscribes to IServiceRegistry.Changes and fires TypesChanged to trigger re-stitching.
builder.Services
    .AddSingleton<DynamicRemoteSchemaModule>()
    .AddGraphQLServer()
    .AddTypeModule(sp => sp.GetRequiredService<DynamicRemoteSchemaModule>());

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
    await db.Database.MigrateAsync();

    var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistry>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseSeeder.SeedAsync(registry, logger);
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
