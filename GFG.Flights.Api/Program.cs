using Oracle.ManagedDataAccess.Client;
using System.Data;
using GFG.Flights.Api.Data;
using GFG.Flights.Api.Infrastructure;
using GFG.Flights.Api.Services;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Enhanced logging configuration
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    });
    loggingBuilder.AddDebug();
    
    // Set specific log levels for different components
    loggingBuilder.SetMinimumLevel(LogLevel.Information);
    loggingBuilder.AddFilter("GFG.Flights.Api.Services.AzureCommunicationService", LogLevel.Debug);
    loggingBuilder.AddFilter("GFG.Flights.Api.Controllers.WhatsAppController", LogLevel.Debug);
    loggingBuilder.AddFilter("GFG.Flights.Api.Services.OagService", LogLevel.Debug);
    loggingBuilder.AddFilter("Azure.Communication", LogLevel.Debug);
    loggingBuilder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    loggingBuilder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers(o => 
{
    o.ModelBinderProviders.Insert(0, new DateOnlyModelBinderProvider());
})
.AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new DateOnlyJsonConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => 
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "GFG Flights API", 
        Version = "v1",
        Description = "API for flight information and WhatsApp messaging services"
    });
    
    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
    
    // Custom schema mappings
    c.MapType<DateOnly>(() => new OpenApiSchema { Type = "string", Format = "date" });
    
    // Enable annotations for better Swagger documentation
    c.EnableAnnotations();
    
    // Use full type names for schema IDs to avoid conflicts
    c.CustomSchemaIds(type => type.FullName);
});

// Enhanced connection string handling with better error messages
builder.Services.AddScoped<Func<string, IDbConnection>>(sp => (name) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    
    var cs = cfg.GetConnectionString(name);
    if (string.IsNullOrEmpty(cs))
    {
        logger.LogError("Missing connection string: {ConnectionStringName}", name);
        throw new InvalidOperationException($"Missing connection string: {name}. Check Azure App Service Configuration.");
    }
    
    logger.LogInformation("Creating connection for: {ConnectionStringName}", name);
    return new OracleConnection(cs);
});

// Register HttpClient for OAG service
builder.Services.AddHttpClient<IOagService, OagService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "GFG-Flights-API/1.0");
});

builder.Services.AddScoped<ICddRepository, CddRepository>();
builder.Services.AddScoped<IAirportRepository, AirportRepository>();
builder.Services.AddScoped<AzureCommunicationService>();
builder.Services.AddHttpClient();
var app = builder.Build();

// Log application startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("GFG Flights API starting up...");

// Log configuration values (sanitized)
var config = app.Services.GetRequiredService<IConfiguration>();
var acsConnectionString = config["AcsConnectionString"];
var channelId = config["AcsChannelRegistrationId"];
var oagSubscriptionKey = config["OagSubscriptionKey"];

logger.LogInformation("Configuration check - ACS Connection String: {HasConnectionString}, Channel ID: {ChannelId}, OAG Key: {HasOagKey}", 
    !string.IsNullOrEmpty(acsConnectionString) ? "Present" : "Missing", 
    channelId ?? "Missing",
    !string.IsNullOrEmpty(oagSubscriptionKey) ? "Present" : "Missing");

if (!string.IsNullOrEmpty(acsConnectionString))
{
    logger.LogInformation("ACS Connection String length: {Length} characters", acsConnectionString.Length);
    // Log the endpoint part (safe to log)
    if (acsConnectionString.Contains("endpoint="))
    {
        var endpointPart = acsConnectionString.Split(';')[0];
        logger.LogInformation("ACS Endpoint: {Endpoint}", endpointPart);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GFG Flights API v1");
        c.RoutePrefix = "swagger";
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        c.DefaultModelsExpandDepth(-1); // Hide schemas section by default
        c.DisplayRequestDuration();
    });
    
    logger.LogInformation("Swagger UI enabled at /swagger");
}

// Add global exception handling with logging
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        
        if (exceptionFeature != null)
        {
            exceptionLogger.LogError(exceptionFeature.Error, "Unhandled exception occurred: {Message}", exceptionFeature.Error.Message);
        }
        
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync("An error occurred. Check the logs for details.");
    });
});

app.UseHttpsRedirection();
app.MapControllers();

// Add a basic error endpoint
app.Map("/error", () => "An error occurred. Check the logs for details.");

logger.LogInformation("GFG Flights API started successfully");

app.Run();

