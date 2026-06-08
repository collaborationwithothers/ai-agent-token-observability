using AiAgentTokenObservability.Dashboard.Web.Components;
using AiAgentTokenObservability.Dashboard.Web.Insights;
using AiAgentTokenObservability.Dashboard.Web.Sessions;
using AiAgentTokenObservability.Dashboard.Web.Status;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents();
builder.Services.AddHttpClient<DashboardStatusClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["DashboardApi:BaseAddress"] ?? "https+http://dashboard-api");
});
builder.Services.AddHttpClient<DashboardSessionsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["DashboardApi:BaseAddress"] ?? "https+http://dashboard-api");
});
builder.Services.AddHttpClient<DashboardInsightsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["DashboardApi:BaseAddress"] ?? "https+http://dashboard-api");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapDefaultEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();
