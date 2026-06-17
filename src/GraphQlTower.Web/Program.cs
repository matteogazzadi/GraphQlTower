using GraphQlTower.Web.Services;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

var gatewayApiUrl = builder.Configuration["GatewayApi:BaseUrl"]
    ?? "http://graphql-tower-api:8080";

builder.Services.AddHttpClient<GatewayApiClient>(client =>
{
    client.BaseAddress = new Uri(gatewayApiUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<GraphQlTower.Web.Components.App>()
   .AddInteractiveServerRenderMode();

await app.RunAsync();
