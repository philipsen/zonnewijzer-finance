using Backend.Configuration;
using Backend.Data;
using Backend.Models;
using Backend.Services.Sigenergy;
using Backend.Services.Smappee;
using IntegrationDaemon.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog.Events;
using Serilog;
using System.Text.Json;
using System.Net;

Directory.CreateDirectory("logs");

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:7071");

// Always load optional .env files first (if present), then map legacy SMAPPEE_*/SIGENERGY_* aliases.
// This makes local daemon runs work even when DOTNET_ENVIRONMENT is not explicitly set.
LocalSecretsBootstrap.Apply();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/integration-daemon-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();

builder.Services.Configure<SmappeeOptions>(builder.Configuration.GetSection(SmappeeOptions.SectionName));
builder.Services.Configure<SigenergyOptions>(builder.Configuration.GetSection(SigenergyOptions.SectionName));
builder.Services.Configure<SmappeeDaemonOptions>(builder.Configuration.GetSection(SmappeeDaemonOptions.SectionName));
builder.Services.Configure<SigenergyDaemonOptions>(builder.Configuration.GetSection(SigenergyDaemonOptions.SectionName));
builder.Services.Configure<DaemonApiOptions>(builder.Configuration.GetSection(DaemonApiOptions.SectionName));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient(SigenergyCloudClient.HttpClientName, (sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<SigenergyOptions>>().Value;
    var raw = (string.IsNullOrWhiteSpace(o.BaseUrl) ? "https://api-eu.sigencloud.com" : o.BaseUrl).Trim().TrimEnd('/');
    if (Uri.TryCreate(raw + "/", UriKind.Absolute, out var baseUri))
        client.BaseAddress = baseUri;
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHttpClient<ISmappeeCloudClient, SmappeeCloudClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddSingleton<ISigenergyCloudClient, SigenergyCloudClient>();
builder.Services.AddSingleton<IStatusPublisher, DbOnlyStatusPublisher>();
builder.Services.AddScoped<ISmappeeChargingSyncService, SmappeeChargingSyncService>();
builder.Services.AddScoped<ISmappeePollingService, SmappeePollingService>();
builder.Services.AddScoped<ISigenergyPollingService, SigenergyPollingService>();

builder.Services.AddHostedService<SmappeePollWorker>();
builder.Services.AddHostedService<SigenergyPollWorker>();

var app = builder.Build();
app.Logger.LogInformation("Integration daemon starting with URLs: {Urls}", string.Join(", ", app.Urls));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var smappee = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeOptions>>().Value;
    var sigenergy = scope.ServiceProvider.GetRequiredService<IOptions<SigenergyOptions>>().Value;
    var smappeeDaemon = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeDaemonOptions>>().Value;
    var sigenergyDaemon = scope.ServiceProvider.GetRequiredService<IOptions<SigenergyDaemonOptions>>().Value;
    var daemonApi = scope.ServiceProvider.GetRequiredService<IOptions<DaemonApiOptions>>().Value;

    var missingSmappee = smappee.GetMissingCredentialFields();
    if (smappeeDaemon.Enabled && smappee.ServiceLocationId > 0 && missingSmappee.Count > 0)
    {
        app.Logger.LogError(
            "Smappee daemon enabled but credentials are incomplete. Missing: {Missing}",
            string.Join(", ", missingSmappee));
    }
    else if (smappeeDaemon.Enabled && smappee.ServiceLocationId <= 0)
    {
        app.Logger.LogError("Smappee daemon enabled but ServiceLocationId is not configured.");
    }
    else
    {
        app.Logger.LogInformation(
            "Smappee settings: daemonEnabled={Enabled}, serviceLocationId={ServiceLocationId}, credentialsComplete={CredentialsComplete}",
            smappeeDaemon.Enabled,
            smappee.ServiceLocationId,
            missingSmappee.Count == 0);
    }

    var missingSigenergy = sigenergy.GetMissingSettings();
    if (sigenergyDaemon.Enabled && missingSigenergy.Count > 0)
    {
        app.Logger.LogError(
            "Sigenergy daemon enabled but settings are incomplete. Missing: {Missing}",
            string.Join(", ", missingSigenergy));
    }
    else
    {
        app.Logger.LogInformation(
            "Sigenergy settings: daemonEnabled={Enabled}, configured={Configured}",
            sigenergyDaemon.Enabled,
            missingSigenergy.Count == 0);
    }

    if (daemonApi.Enabled && string.IsNullOrWhiteSpace(daemonApi.ApiKey))
    {
        app.Logger.LogError("Daemon API is enabled but DaemonApi:ApiKey is empty; manual trigger endpoint is unsecured.");
    }
    else
    {
        app.Logger.LogInformation(
            "Daemon API settings: enabled={Enabled}, apiKeyConfigured={ApiKeyConfigured}",
            daemonApi.Enabled,
            !string.IsNullOrWhiteSpace(daemonApi.ApiKey));
    }
}

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "integration-daemon" }));

app.MapPost("/api/smappee/update", async (
    HttpContext httpContext,
    SmappeeUpdateRequest? body,
    IServiceScopeFactory scopeFactory,
    IOptions<DaemonApiOptions> apiOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ManualSmappeeUpdate");
    var opts = apiOptions.Value;
    if (!opts.Enabled)
        return Results.StatusCode(StatusCodes.Status404NotFound);

    if (ShouldEnforceApiKey(httpContext, opts, app.Environment))
    {
        var apiKey = httpContext.Request.Headers["x-api-key"].ToString();
        if (!string.Equals(apiKey, opts.ApiKey, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejected manual Smappee update due to invalid API key (provided={Provided}, providedLength={ProvidedLength}, configuredLength={ConfiguredLength})",
                !string.IsNullOrWhiteSpace(apiKey),
                apiKey.Length,
                opts.ApiKey?.Length ?? 0);
            return Results.Unauthorized();
        }
    }

    logger.LogInformation("Manual Smappee update requested");
    using var scope = scopeFactory.CreateScope();
    var sync = scope.ServiceProvider.GetRequiredService<ISmappeeChargingSyncService>();
    var poller = scope.ServiceProvider.GetRequiredService<ISmappeePollingService>();
    var from = body?.From;
    var to = body?.To;

    // When a range is provided, run exact calendar sync for that period.
    if (from.HasValue || to.HasValue)
    {
        if (!from.HasValue || !to.HasValue)
            return Results.BadRequest(new { ok = false, error = "Both 'from' and 'to' are required when specifying a range." });
        if (to.Value < from.Value)
            return Results.BadRequest(new { ok = false, error = "'to' date must be on or after 'from' date." });

        var syncResult = await sync.SyncSmappeeCalendarAsync(
            from.Value,
            to.Value,
            "Integration daemon manual range sync",
            cancellationToken);

        return Results.Ok(new
        {
            ok = syncResult.Success,
            fetchedAtUtc = DateTimeOffset.UtcNow,
            error = syncResult.ErrorMessage,
            chargeCards = new { added = syncResult.CardsAdded, updated = syncResult.CardsUpdated },
            sessions = new
            {
                added = syncResult.SessionsAdded,
                updated = syncResult.SessionsUpdated,
                skipped = syncResult.SessionsSkipped,
                errors = syncResult.SessionsErrors
            }
        });
    }

    var snapshot = await poller.PollSessionsAsync(cancellationToken);

    logger.LogInformation(
        "Manual Smappee update completed. Success={Success}, FetchedAtUtc={FetchedAtUtc}, Error={Error}",
        snapshot.IsSuccess,
        snapshot.FetchedAtUtc,
        snapshot.Error);

    return Results.Ok(new
    {
        ok = snapshot.IsSuccess,
        fetchedAtUtc = snapshot.FetchedAtUtc,
        error = snapshot.Error,
        chargeCards = new { added = 0, updated = 0 },
        sessions = new { added = 0, updated = 0, skipped = 0, errors = 0 }
    });
});

app.MapPost("/api/smappee/update-service-locations", async (
    HttpContext httpContext,
    IServiceScopeFactory scopeFactory,
    IOptions<DaemonApiOptions> apiOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ManualSmappeeServiceLocationsUpdate");
    var opts = apiOptions.Value;
    if (!opts.Enabled)
        return Results.StatusCode(StatusCodes.Status404NotFound);

    if (ShouldEnforceApiKey(httpContext, opts, app.Environment))
    {
        var apiKey = httpContext.Request.Headers["x-api-key"].ToString();
        if (!string.Equals(apiKey, opts.ApiKey, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejected manual Smappee service-location update due to invalid API key (provided={Provided}, providedLength={ProvidedLength}, configuredLength={ConfiguredLength})",
                !string.IsNullOrWhiteSpace(apiKey),
                apiKey.Length,
                opts.ApiKey?.Length ?? 0);
            return Results.Unauthorized();
        }
    }

    logger.LogInformation("Manual Smappee service-location update requested");
    using var scope = scopeFactory.CreateScope();
    var poller = scope.ServiceProvider.GetRequiredService<ISmappeePollingService>();
    await poller.RefreshServiceLocationsListAsync(cancellationToken);
    await poller.RefreshServiceLocationInfoAsync(cancellationToken);

    logger.LogInformation("Manual Smappee service-location update completed");
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/smappee/service-locations", async (
    HttpContext httpContext,
    IServiceScopeFactory scopeFactory,
    IOptions<DaemonApiOptions> apiOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ManualSmappeeServiceLocationsRead");
    var opts = apiOptions.Value;
    if (!opts.Enabled)
        return Results.StatusCode(StatusCodes.Status404NotFound);

    if (ShouldEnforceApiKey(httpContext, opts, app.Environment))
    {
        var apiKey = httpContext.Request.Headers["x-api-key"].ToString();
        if (!string.Equals(apiKey, opts.ApiKey, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejected manual Smappee service-location read due to invalid API key (provided={Provided}, providedLength={ProvidedLength}, configuredLength={ConfiguredLength})",
                !string.IsNullOrWhiteSpace(apiKey),
                apiKey.Length,
                opts.ApiKey?.Length ?? 0);
            return Results.Unauthorized();
        }
    }

    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var devices = await db.SmappeeServiceLocationDevices
        .AsNoTracking()
        .OrderBy(d => d.ServiceLocationId)
        .ThenBy(d => d.DeviceSerialNumber)
        .ToListAsync(cancellationToken);

    if (devices.Count == 0)
        return Results.Ok(new { serviceLocations = Array.Empty<SmappeeServiceLocationListItemResponse>(), fetchedAt = (DateTimeOffset?)null });

    var locIds = devices.Select(d => d.ServiceLocationId).Distinct().ToList();
    var locs = await db.SmappeeServiceLocations
        .AsNoTracking()
        .Where(s => locIds.Contains(s.ServiceLocationId))
        .ToDictionaryAsync(s => s.ServiceLocationId, cancellationToken);

    var items = new List<SmappeeServiceLocationListItemResponse>(devices.Count);
    foreach (var d in devices)
    {
        var item = new SmappeeServiceLocationListItemResponse
        {
            ServiceLocationId = d.ServiceLocationId,
            Name = d.Name,
            ServiceLocationUuid = d.ServiceLocationUuid,
            DeviceSerialNumber = string.IsNullOrWhiteSpace(d.DeviceSerialNumber) ? null : d.DeviceSerialNumber
        };
        if (locs.TryGetValue(d.ServiceLocationId, out var loc))
        {
            item.Longitude = loc.Longitude;
            item.Latitude = loc.Latitude;
            item.ElectricityCost = loc.ElectricityCost;
            item.ElectricityCurrency = loc.ElectricityCurrency;
            item.Timezone = loc.Timezone;
            if (!string.IsNullOrEmpty(loc.AppliancesJson))
            {
                try
                {
                    item.Appliances = JsonSerializer.Deserialize<List<SmappeeServiceLocationApplianceItemResponse>>(
                        loc.AppliancesJson,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Stored appliances JSON invalid for service location {Id}", item.ServiceLocationId);
                }
            }
        }
        items.Add(item);
    }

    var tDev = devices.Max(x => x.UpdatedAtUtc);
    DateTime? tLoc = null;
    if (locs.Count > 0)
        tLoc = locs.Values.Max(l => l.UpdatedAtUtc);
    var latest = tLoc.HasValue && tLoc.Value > tDev ? tLoc.Value : tDev;
    var fetchedAt = new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));

    return Results.Ok(new { serviceLocations = items, fetchedAt });
});

app.MapPost("/api/sigenergy/update", async (
    HttpContext httpContext,
    IServiceScopeFactory scopeFactory,
    IOptions<DaemonApiOptions> apiOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ManualSigenergyUpdate");
    var opts = apiOptions.Value;
    if (!opts.Enabled)
        return Results.StatusCode(StatusCodes.Status404NotFound);

    if (ShouldEnforceApiKey(httpContext, opts, app.Environment))
    {
        var apiKey = httpContext.Request.Headers["x-api-key"].ToString();
        if (!string.Equals(apiKey, opts.ApiKey, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejected manual Sigenergy update due to invalid API key (provided={Provided}, providedLength={ProvidedLength}, configuredLength={ConfiguredLength})",
                !string.IsNullOrWhiteSpace(apiKey),
                apiKey.Length,
                opts.ApiKey?.Length ?? 0);
            return Results.Unauthorized();
        }
    }

    logger.LogInformation("Manual Sigenergy update requested");
    using var scope = scopeFactory.CreateScope();
    var poller = scope.ServiceProvider.GetRequiredService<ISigenergyPollingService>();
    var snapshot = await poller.PollEnergyFlowAsync(cancellationToken);

    logger.LogInformation(
        "Manual Sigenergy update completed. Success={Success}, FetchedAtUtc={FetchedAtUtc}, Error={Error}",
        snapshot.IsSuccess,
        snapshot.FetchedAtUtc,
        snapshot.Error);

    return Results.Ok(new
    {
        ok = snapshot.IsSuccess,
        fetchedAtUtc = snapshot.FetchedAtUtc,
        error = snapshot.Error
    });
});

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("Integration daemon listening on {Urls}", string.Join(", ", app.Urls)));

await app.RunAsync();

static bool ShouldEnforceApiKey(HttpContext httpContext, DaemonApiOptions options, Microsoft.AspNetCore.Hosting.IWebHostEnvironment environment)
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
        return false;

    if (environment.IsDevelopment())
        return false;

    var remoteIp = httpContext.Connection.RemoteIpAddress;
    if (remoteIp == null)
        return false;

    if (IPAddress.IsLoopback(remoteIp))
        return false;

    if (remoteIp.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteIp.MapToIPv4()))
        return false;

    return true;
}

internal sealed class SmappeeUpdateRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
