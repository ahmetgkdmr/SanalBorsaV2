using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SanalBorsa.API.Middleware;
using SanalBorsa.Application;
using SanalBorsa.Infrastructure;
using SanalBorsa.Infrastructure.Data;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
    {
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/sanalborsa-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    });

    // ── Application & Infrastructure layers ──────────────────────────────────
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── JWT Authentication ────────────────────────────────────────────────────
    var jwtSecret   = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret eksik.");
    var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "SanalBorsa";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SanalBorsa";

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer           = true,
                ValidIssuer              = jwtIssuer,
                ValidateAudience         = true,
                ValidAudience            = jwtAudience,
                ClockSkew                = TimeSpan.Zero,
            };
        });

    builder.Services.AddAuthorization();

    // ── CORS ─────────────────────────────────────────────────────────────────
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ??
        [
            "http://localhost:3000",
            "http://localhost:4200",
            "http://127.0.0.1:4200",
            "http://127.0.0.1:5500",
            "http://localhost:5500",
        ];
    var corsAllowAny = builder.Configuration.GetValue("Cors:AllowAnyOrigin", false);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("FrontendPolicy", policy =>
        {
            if (corsAllowAny)
            {
                policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
            else
            {
                policy
                    .WithOrigins(corsOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
        });
    });

    // ── Controllers & OpenAPI ─────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            opts.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "SanalBorsa API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new()
        {
            Name         = "Authorization",
            Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
        });
        c.AddSecurityRequirement(new()
        {
            {
                new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
                []
            }
        });
    });

    // ── Build app ─────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Auto-apply EF migrations on startup ──────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // CORS, HTTPS redirect’ten önce olmalı (preflight kırılmasın)
    app.UseCors("FrontendPolicy");

    // Render TLS’i dışarıda sonlandırır; konteyner HTTP:8080 dinler
    if (!app.Environment.IsProduction())
        app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("SanalBorsa API starting on {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
