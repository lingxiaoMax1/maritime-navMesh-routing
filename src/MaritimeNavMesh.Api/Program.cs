using MaritimeNavMesh.Api.Endpoints;
using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Bind graph artifact paths from configuration
builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection(GraphOptions.Section));

// Register graph service as singleton; load graph at startup
builder.Services.AddSingleton<GraphService>();
builder.Services.AddHostedService<GraphLoaderHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();

// HTTPS redirect only in production — dev runs on plain HTTP (port 5000)
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// Map all endpoint groups
app.MapHealthEndpoints();
app.MapGraphEndpoints();
app.MapRuntimeEndpoints();
app.MapPortEndpoints();
app.MapRouteEndpoints();

app.Run();
