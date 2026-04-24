using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;
using System.Text;
using System.Security.Claims;
using Backend.Configuration;
using Backend.Data;
using Backend.Services;
using Backend.Services.GnuCash;
using Backend.Services.Smappee;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Options;

// Configure Serilog early
Directory.CreateDirectory("logs");

if (LocalSecretsBootstrap.IsDevelopmentEnvironment())
    LocalSecretsBootstrap.Apply();
else
    LocalSecretsBootstrap.MapSmappeeAliases();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        new CompactJsonFormatter(),
        "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Finance App backend");

    var builder = WebApplication.CreateBuilder(args);

    // Replace default logging with Serilog
    builder.Host.UseSerilog((context, services, configuration) => {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "backend");
    });

    // Add services to the container.
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
// Temporarily commented out due to .NET 10 compatibility issue
// builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",      // Development frontend
                "http://localhost:3001",      // Alternative dev port
                "http://localhost:5000",      // Docker frontend external
                "http://localhost:5293",      // Backend port (in case of frontend calling backend)
                "http://frontend",            // Docker frontend internal
                "http://frontend:80"          // Docker frontend internal with port
              )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add Entity Framework
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured"))),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // Match claims issued by IJwtService (ClaimTypes.*) so [Authorize(Roles=…)] is reliable
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

// Add health checks
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy().LoadFromMemory(
    new[]
    {
        new RouteConfig
        {
            RouteId = "vite-dev-catchall",
            ClusterId = "vite-dev",
            Match = new RouteMatch { Path = "/{**catch-all}" }
        }
    },
    new[]
    {
        new ClusterConfig
        {
            ClusterId = "vite-dev",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                { "vite", new DestinationConfig { Address = "http://localhost:3000/" } }
            }
        }
    });

// Add custom services
builder.Services.Configure<GnuCashSyncOptions>(builder.Configuration.GetSection(GnuCashSyncOptions.SectionName));
builder.Services.Configure<SmappeeOptions>(builder.Configuration.GetSection(SmappeeOptions.SectionName));
builder.Services.AddHttpClient<IGnuCashSyncClient, GnuCashSyncClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<ISmappeeCloudClient, SmappeeCloudClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.Configure<SmappeeBackgroundSyncOptions>(
    builder.Configuration.GetSection(SmappeeBackgroundSyncOptions.SectionName));
builder.Services.AddScoped<ISmappeeChargingSyncService, SmappeeChargingSyncService>();
builder.Services.AddHostedService<SmappeeHourlySyncBackgroundService>();

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IInvoiceComputationService, InvoiceComputationService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddHttpClient("Mollie");
builder.Services.AddScoped<IMollieService, MollieService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

var app = builder.Build();

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, _, ex) =>
        {
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;

            if (httpContext.Request.Path.StartsWithSegments("/health"))
                return LogEventLevel.Verbose; // filtered by default minimum level

            return LogEventLevel.Information;
        };
    });

    // Ensure database migrations are applied
    using (var scope = app.Services.CreateScope())
    {
        TryBackupSqliteBeforeMigrate(app.Environment, builder.Configuration.GetConnectionString("DefaultConnection"));
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        DevelopmentAdminSeed.Apply(builder.Configuration, context);

        var smappeeOpts = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeOptions>>().Value;
        var smappeeBg = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeBackgroundSyncOptions>>().Value;
        var missingSmappee = smappeeOpts.GetMissingCredentialFields();
        if (smappeeBg.Enabled && smappeeOpts.ServiceLocationId > 0 && missingSmappee.Count > 0)
        {
            Log.Error(
                "Smappee: BackgroundSync is enabled and ServiceLocationId is set, but required credentials are null or empty. Missing: {Missing}.",
                string.Join(", ", missingSmappee));
        }

        Log.Information(
            "Smappee: credentials {CredentialStatus} (ServiceLocationId={ServiceLocationId}, BackgroundSync={BgEnabled})",
            missingSmappee.Count == 0
                ? "OK — ClientId, ClientSecret, Username, Password are all non-empty"
                : "incomplete — missing: " + string.Join(", ", missingSmappee),
            smappeeOpts.ServiceLocationId,
            smappeeBg.Enabled);
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        // Temporarily commented out due to .NET 10 compatibility issue
        // app.UseSwagger();
        // app.UseSwaggerUI();
    }

    app.MapHealthChecks("/health");
    app.UseCors("AllowVueApp");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    if (app.Environment.IsDevelopment())
    {
        app.MapReverseProxy();
    }
    else
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

    app.Lifetime.ApplicationStarted.Register(() =>
        Log.Information("Finance App backend listening on {Urls}", string.Join(", ", app.Urls)));

    try
    {
        app.Run();
    }
    catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                                 || ex.InnerException?.Message?.Contains("address already in use", StringComparison.OrdinalIgnoreCase) == true)
    {
        Log.Fatal(
            ex,
            "HTTP port is already in use (often a leftover `dotnet` / Backend process). "
            + "Stop other backend instances or run: lsof -nP -iTCP:5293 -sTCP:LISTEN");
        Environment.ExitCode = 1;
        return;
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Finance App backend terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static void TryBackupSqliteBeforeMigrate(IHostEnvironment env, string? connectionString)
{
    if (!env.IsDevelopment() || string.IsNullOrWhiteSpace(connectionString))
        return;

    try
    {
        var cb = new SqliteConnectionStringBuilder(connectionString);
        var path = cb.DataSource;
        if (string.IsNullOrEmpty(path))
            return;

        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

        if (!File.Exists(path))
            return;

        var backupPath = path + ".pre-migrate.bak";
        File.Copy(path, backupPath, overwrite: true);
        Log.Information("Development: backed up SQLite to {BackupPath} before applying migrations", backupPath);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Development: could not back up SQLite before migrations");
    }
}
