using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SonnetArt.ImageStudio;
using SonnetArt.ImageStudio.Services;
using AntDesign;
using AntDesign.X;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(10),
});
builder.Services.AddScoped<ImageStudioStorage>();
builder.Services.AddScoped<ImageGenerationClient>();
builder.Services.AddScoped<PromptChatClient>();
builder.Services.AddScoped<PromptLibraryService>();
builder.Services.AddScoped<SonnetAccountClient>();
builder.Services.AddAntDesign();
builder.Services.AddAntDesignX();

await builder.Build().RunAsync();
