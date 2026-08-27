using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FraudDetection.Api.Configuration;
using FraudDetection.Api.Data;
using FraudDetection.Api.HealthChecks;
using FraudDetection.Api.Middleware;
using FraudDetection.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Fraud Detection API",
        Version = "v1",
        Description = "Transactions are scored asynchronously (Kafka -> Scala Structured Streaming -> Postgres); " +
                      "POST /api/transactions returns immediately with Status=Pending."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT from POST /api/auth/login. Enter as: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// FraudDetectionService is no longer registered for live scoring as of Phase 3: the Scala
// Structured Streaming risk engine (spark-jobs/scala-risk-engine) owns real-time scoring,
// writing results straight to Postgres via JDBC. FraudDetectionService itself stays in use
// for SeedData's synchronous historical-import path and its own unit tests.
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.AddSingleton<KafkaPipelineMetrics>();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddHostedService<TransactionOutboxService>();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
// Configured via the options pattern (not read off builder.Configuration inline above) so it
// resolves AuthOptions from the DI container at the point the handler is first used -- that's
// what makes WebApplicationFactory's test config overrides (see ApiTestFactory) actually apply.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwtOptions, authOptionsAccessor) =>
    {
        var authOptions = authOptionsAccessor.Value;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSecret))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<KafkaHealthCheck>("kafka", tags: new[] { "ready" });

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

var resolvedAuthOptions = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
if (string.IsNullOrWhiteSpace(resolvedAuthOptions.JwtSecret))
{
    throw new InvalidOperationException(
        "Auth:JwtSecret is not set. It is deliberately excluded from appsettings.json -- " +
        "set it via the Auth__JwtSecret environment variable (see .env.example).");
}

// Skipped under the "Testing" environment: WebApplicationFactory-based integration tests
// swap in the EF InMemory provider, which doesn't support relational migrations, and each
// test controls its own data instead of relying on the ~500-row synthetic seed.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.SeedAsync(db);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// /health/live: process is up. /health/ready: dependencies (Postgres, Kafka) are reachable.
// Distinct from /api/health/pipeline, which reports business-level pipeline metrics.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program { }
