using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SonnetArt.Services;
using AntDesign;
using AntDesign.X;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<SonnetArt.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(10),
});
builder.Services.AddScoped<SonnetArtStorage>();
builder.Services.AddScoped<ImageGenerationClient>();
builder.Services.AddScoped<PromptChatClient>();
builder.Services.AddScoped<PromptLibraryService>();
builder.Services.AddScoped<SonnetAccountClient>();
builder.Services.AddScoped<SiteConfigurationClient>();
builder.Services.AddAntDesign();
builder.Services.AddAntDesignX();

await builder.Build().RunAsync();
