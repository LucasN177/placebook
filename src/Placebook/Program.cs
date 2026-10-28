using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Placebook;
using Placebook.Services;
using Placebook.Services.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<VisitService>();
builder.Services.AddScoped<BrowserService>();
builder.Services.AddScoped<CategoryState>();

builder.Services.AddSingleton(TimeProvider.System);
// Direkt auslesen statt Get<T>(): das reflection-basierte Binden kann im getrimmten Release-Build leer bleiben
var supabase = new SupabaseOptions
{
    Url = builder.Configuration["Supabase:Url"],
    PublishableKey = builder.Configuration["Supabase:PublishableKey"]
};
builder.Services.AddScoped(_ => new SupabaseVisitsClient(new HttpClient(), supabase));
builder.Services.AddScoped(_ => new SupabaseAuthClient(new HttpClient(), supabase));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<Placebook.Components.Account.AccountDialogs>();

await builder.Build().RunAsync();
