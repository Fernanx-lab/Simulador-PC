using System;
using System.Linq;
using System.Net.Http;
using ProjetoSimuladorPC.Components;
using ProjetoSimuladorPC.Utilidades;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registrar SimulationState como singleton para injeção em componentes e controllers
builder.Services.AddSingleton<SimulationState>();

// Habilitar controllers para endpoints REST usados pela UI
builder.Services.AddControllers();

// Registrar HttpClient com BaseAddress razoável para chamadas ao próprio backend.
// Tenta ler uma URL em "ServerBaseUrl" ou "ASPNETCORE_URLS", caso contrário usa um fallback.
builder.Services.AddHttpClient("server", client =>
{
    var baseUrl = builder.Configuration["ServerBaseUrl"];
    if (string.IsNullOrEmpty(baseUrl))
    {
        var urls = builder.Configuration["ASPNETCORE_URLS"];
        if (!string.IsNullOrEmpty(urls))
            baseUrl = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).First();
        else
            baseUrl = "https://localhost:5001";
    }

    // garante que termine com barra
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
});

// Resolve HttpClient in components when injecting HttpClient (scoped)
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("server"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Mapear controllers (API)
app.MapControllers();

// Mapear Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
