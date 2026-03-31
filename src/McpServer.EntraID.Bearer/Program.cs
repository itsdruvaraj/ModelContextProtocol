using System.Security.Claims;
using McpServer.EntraID.Bearer.Models;
using McpServer.EntraID.Bearer.Services;
using McpServer.EntraID.Bearer.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<EntraIDOptions>(builder.Configuration.GetSection("EntraID"));

var serverUrl = builder.Configuration["ServerUrl"]!;
var entraIdOptions = builder.Configuration.GetSection("EntraID").Get<EntraIDOptions>()!;

Console.WriteLine($"EntraID authentication enabled for tenant: {entraIdOptions.TenantId}");

string[] validAudiences = new[] { entraIdOptions.Audience, entraIdOptions.ClientId };
string[] validIssuers = new[]
{
    $"https://sts.windows.net/{entraIdOptions.TenantId}/",
    $"{entraIdOptions.Instance.TrimEnd('/')}/{entraIdOptions.TenantId}/v2.0",
    entraIdOptions.Authority
};

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = entraIdOptions.Authority;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudiences = validAudiences,
        ValidIssuers = validIssuers,
        NameClaimType = "name",
        RoleClaimType = "roles",
    };
    
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader))
            {
                Console.WriteLine($"Received Authorization header: {authHeader.Substring(0, Math.Min(50, authHeader.Length))}...");
            }
            else
            {
                Console.WriteLine("No Authorization header received");
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var name = context.Principal?.Identity?.Name ?? "unknown";
            var email = context.Principal?.FindFirstValue("preferred_username") ?? 
                       context.Principal?.FindFirstValue("upn") ?? 
                       context.Principal?.FindFirstValue("email") ?? "unknown";
            var oid = context.Principal?.FindFirstValue("oid") ?? "unknown";
            var aud = context.Principal?.FindFirstValue("aud") ?? "unknown";
            var iss = context.Principal?.FindFirstValue("iss") ?? "unknown";
            
            Console.WriteLine($"Token validated - Name: {name}, Email: {email}, OID: {oid}, Audience: {aud}, Issuer: {iss}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            if (context.Exception.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {context.Exception.InnerException.Message}");
            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"Authentication challenge issued. Error: {context.Error}, Description: {context.ErrorDescription}");
            return Task.CompletedTask;
        }
    };
});
// Note: Removed .AddMcp() OAuth metadata to allow MCP Inspector UI to work with manual token headers
// MCP Inspector doesn't support Entra ID's OAuth flow (no dynamic client registration)
// Clients can still authenticate by passing Bearer token in Authorization header

builder.Services.AddAuthorization();

// Register the Job Service for async request-reply pattern (singleton for in-memory storage)
builder.Services.AddSingleton<IJobService, InMemoryJobService>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<MultiplicationTool>()
    .WithTools<TemperatureConverterTool>()
    .WithTools<WeatherTools>()
    .WithTools<AsyncOperationTools>()
    .WithTools<LongRunningTools>()
    .WithTools<ValidateUserTool>()
    .WithTools<JsonFileTool>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Add early request logging middleware - logs EVERY request before any other middleware
app.Use(async (context, next) =>
{
    var timestamp = DateTime.UtcNow.ToString("o");
    Console.WriteLine($"[{timestamp}] REQUEST: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    Console.WriteLine($"[{timestamp}] Headers: Host={context.Request.Host}, Accept={context.Request.Headers.Accept}, Content-Type={context.Request.ContentType}");
    
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
        sw.Stop();
        Console.WriteLine($"[{timestamp}] RESPONSE: {context.Response.StatusCode} in {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"[{timestamp}] ERROR: {ex.GetType().Name}: {ex.Message} after {sw.ElapsedMilliseconds}ms");
        throw;
    }
});

// Log startup configuration
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"Server URL: {serverUrl}");
Console.WriteLine($"Authority: {entraIdOptions.Authority}");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Enable detailed errors in production for debugging deployment issues
    app.UseDeveloperExceptionPage();
}

app.UseCors();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp("/mcp").RequireAuthorization();

app.MapGet("/", () => new
{
    status = "running",
    server = "MCP Server with EntraID Authentication",
    authenticationEnabled = true,
    tenant = entraIdOptions.TenantId,
    audience = entraIdOptions.Audience
}).AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();
