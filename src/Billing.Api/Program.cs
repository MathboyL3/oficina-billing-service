using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Billing.Api.Middleware;
using Billing.Application;
using Billing.Infrastructure;
using Billing.Infrastructure.Auth;
using Billing.Infrastructure.Messaging;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MongoDB.Driver;
using RabbitMQ.Client;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);
var testing = builder.Environment.IsEnvironment("Testing");

// Logging estruturado JSON (New Relic / observabilidade).
// Emite uma linha JSON por evento, com TraceId/SpanId para correlacao com o APM.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service.name", "oficina-billing-service")
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

// swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Oficina — Billing",
        Version = "v1",
        Description = "Orçamento e pagamento (Mercado Pago) da OS (SOAT/FIAP — Fase 4)."
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT — informe apenas o token (sem o prefixo \"Bearer \")."
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// DI
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, testing);
builder.Services.AddMessaging(builder.Configuration, testing);

// Health checks: usados pelas probes do Kubernetes.
// /health/live  -> liveness  (o processo está de pé)
// /health/ready -> readiness (dependências prontas: banco e broker)
var health = builder.Services.AddHealthChecks();

if (!testing)
{
    health.AddMongoDb(sp => sp.GetRequiredService<IMongoClient>(), name: "mongo", tags: new[] { "ready" });

    var rabbit = RabbitMqOptions.FromConfiguration(builder.Configuration);
    // O health check do pacote 9.0.0 não fecha a conexão que a factory dele cria (uma por probe),
    // então a conexão vira singleton resolvido pelo DI e é fechada quando o host encerra.
    builder.Services.AddSingleton<IConnection>(_ =>
        rabbit.CreateConnectionFactory().CreateConnectionAsync().GetAwaiter().GetResult());
    health.AddRabbitMQ(name: "rabbitmq", tags: new[] { "ready" });
}

// fluent validation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// JWT (este serviço só valida: o token vem do monólito/os-service)
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Seção Jwt ausente no appsettings.");

if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret deve ter ao menos 32 caracteres.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        o.SaveToken = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret))
        };
    });

builder.Services.AddAuthorization();

// cors
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Correlacao: injeta trace.id/span.id (padrao New Relic) no contexto de log.
app.Use(async (context, next) =>
{
    var activity = Activity.Current;
    using (LogContext.PushProperty("trace.id", activity?.TraceId.ToString()))
    using (LogContext.PushProperty("span.id", activity?.SpanId.ToString()))
    {
        await next();
    }
});

// Log de requests HTTP (metodo, rota, status, latencia) em JSON.
app.UseSerilogRequestLogging();

// Tratamento global de erros (mapeia DomainException/AppException para HTTP)
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Oficina — Billing v1");
    c.RoutePrefix = string.Empty; // Swagger na raiz
});

app.UseCors();

// Autenticação e autorização (ordem importa)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Endpoints de health para as probes do Kubernetes.
// Liveness: sempre responde se o processo está vivo (não checa dependências).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
// Readiness: só fica pronto quando as dependências marcadas com a tag "ready" estão OK.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

[ExcludeFromCodeCoverage]
public partial class Program { }