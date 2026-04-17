using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;
using System.Text;
using Backend.Data;
using Backend.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Configure Serilog early
Directory.CreateDirectory("logs");

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
            .Enrich.FromLogContext();
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
            ClockSkew = TimeSpan.Zero
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
builder.Services.AddScoped<IJwtService, JwtService>();
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
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
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

    Log.Information("Finance App backend started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Finance App backend terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
