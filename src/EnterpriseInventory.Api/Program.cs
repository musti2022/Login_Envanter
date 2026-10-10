using System.Globalization;
using System.Text.Json.Serialization;
using EnterpriseInventory.Api.Assets;
using EnterpriseInventory.Api.Auditing;
using EnterpriseInventory.Api.Auth;
using EnterpriseInventory.Api.Dashboard;
using EnterpriseInventory.Api.Employees;
using EnterpriseInventory.Api.Health;
using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Api.Lookups;
using EnterpriseInventory.Api.Realtime;
using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Application.Exports;
using EnterpriseInventory.Infrastructure;
using EnterpriseInventory.Infrastructure.Persistence.Seed;
using Serilog;
using Serilog.Events;

// Logs startup failures until the host's own logger, configured from appsettings, takes over.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // The host gets its own logger instead of sharing the static one, so several hosts in one process
    // (integration tests) never touch each other's logger.
    builder.Host.UseSerilog(
        (context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    builder.Services.AddApiProblemDetails();

    // Enums travel as their names ("Laptop", "Available"), which stay meaningful if values are ever added.
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddApiSecurity();
    builder.Services.AddApiDataProtection();
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
    builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
    builder.Services.AddScoped<BackgroundOperation>();
    builder.Services.AddRealtime();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddOptions<ReportingOptions>()
        .BindConfiguration(ReportingOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // No CORS: the React app, /api and /hubs are served from the same origin, so cross-origin calls are refused.
    var app = builder.Build();

    // `dotnet run -- seed-development-data` adds sample lookups to a development database and exits.
    if (args is [DevelopmentSeed.Command])
    {
        var added = await DevelopmentSeed.RunAsync(app.Services, CancellationToken.None);
        Log.Information("Development seed added {RowCount} rows", added);
        return;
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        // Without this the middleware writes to the static startup logger, which has no file sink.
        options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        options.GetLevel = RequestLogLevel;
    });

    // Before authentication: a hub request from another site is refused before its cookie is even looked at.
    app.UseMiddleware<SameOriginHubMiddleware>();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    // After authorization, so a request that is refused anyway gets its 401 or 403 rather than a CSRF error.
    app.UseMiddleware<CsrfProtectionMiddleware>();

    app.MapApiHealthChecks();
    app.MapAuthEndpoints();
    app.MapAssetEndpoints();
    app.MapDashboardEndpoints();
    app.MapLookupEndpoints();
    app.MapEmployeeEndpoints();
    app.MapAuditLogEndpoints();
    app.MapRealtime();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Successful health probes run every few seconds; logging them at Verbose keeps them out of the normal log.
// Rejected health requests (401, 403, 429) stay visible.
static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMilliseconds, Exception? exception) =>
    exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/api/health") && context.Response.StatusCode < StatusCodes.Status400BadRequest ? LogEventLevel.Verbose
    : LogEventLevel.Information;

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
