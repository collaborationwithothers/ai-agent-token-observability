using AiAgentTokenObservability.Dashboard.Api.Status;
using AiAgentTokenObservability.Dashboard.Api.Insights;
using AiAgentTokenObservability.Dashboard.Api.Sessions;
using AiAgentTokenObservability.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddTelemetryStorage(builder.Configuration);
builder.Services
    .AddOptions<LocalPlatformStatusOptions>()
    .Bind(builder.Configuration.GetSection("LocalPlatform"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<LocalPlatformStatusOptions>>().Value;
    var environmentName = sp.GetRequiredService<IHostEnvironment>().EnvironmentName;

    return new DashboardStatusService(options, environmentName);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGet("/status", (DashboardStatusService statusService) => statusService.GetStatus())
    .WithName("GetDashboardStatus");

app.MapDashboardSessionEndpoints();
app.MapDashboardInsightEndpoints();

app.Run();
