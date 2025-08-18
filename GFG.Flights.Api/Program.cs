using Oracle.ManagedDataAccess.Client;
using System.Data;
using GFG.Flights.Api.Data;
using GFG.Flights.Api.Infrastructure;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

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
    c.MapType<DateOnly>(() => new OpenApiSchema { Type = "string", Format = "date" });
});
builder.Services.AddScoped<Func<string, IDbConnection>>(sp => (name) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cs = cfg.GetConnectionString(name)
             ?? throw new InvalidOperationException($"Missing conn string: {name}");
    return new OracleConnection(cs);
});
builder.Services.AddScoped<ICddRepository, CddRepository>();
builder.Services.AddScoped<IAirportRepository, AirportRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

