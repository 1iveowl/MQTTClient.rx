using Microsoft.FluentUI.AspNetCore.Components;
using Sample.Dashboard.Client.Models;
using Sample.Dashboard.Components;
using Sample.Dashboard.Hubs;
using Sample.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Registered on the server too, so any server-rendered Fluent component finds its services.
builder.Services.AddFluentUIComponents();

builder.Services.AddSignalR();

// One broker connection, shared by every browser. See BrokerConnection for why the server owns it.
builder.Services.AddSingleton<MessageLog>();
builder.Services.AddSingleton<BrokerConnection>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BrokerConnection>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No HTTPS redirection: this is a LAN dashboard, usually opened as http://<host>:<port> from
// another machine, and redirecting to a dev-cert HTTPS port would break that.
app.UseAntiforgery();
app.MapStaticAssets();

app.MapHub<MqttHub>(HubEvents.Path);

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Sample.Dashboard.Client._Imports).Assembly);

app.Run();
