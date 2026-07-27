using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using ReactiveUI.Builder;
using Sample.Dashboard.Client.Services;
using Sample.Dashboard.Client.ViewModels;

// ReactiveUI 23 must be initialised before anything touches WhenAnyValue, or the property-observation
// mixin dies inside a static initializer. This has to stay the first statement in the file.
//
// WithBlazor(), not WithBlazorWasm(). What is actually known:
//
//   * This app threw "The type initializer for 'WasmRuntime' threw an exception" in a real browser
//     while calling WithBlazorWasm(), and stopped once it called WithBlazor().
//   * The resolved package is Reactive.Wasm 3.0.1 (lib/net9.0), which is current - an earlier
//     comment here blamed a pre-.NET-7 package and that was simply wrong.
//   * UPnP.rx runs WithBlazorWasm() on the same versions in a real browser with no such error, so
//     the API is not broken outright.
//
// The switch was made in the same edit that stopped this app reading RxSchedulers.MainThreadScheduler
// for a timed operator, so which of the two silenced it was never isolated. The plausible reading is
// that WasmScheduler is resolved lazily and only that explicit read forced its initializer to run.
//
// Caveat if this is ever changed back: WithBlazor() supplies CurrentThreadScheduler, which honours a
// due time by BLOCKING the calling thread - the only thread WebAssembly has. Nothing here schedules
// timed work on the main-thread scheduler; keep it that way.
try
{
    RxAppBuilder.CreateReactiveUIBuilder()
        .WithBlazor()
        .BuildApp();
}
catch (Exception exception)
{
    // A type initializer failing here reports only "TypeInitialization_Type" through the browser's
    // error channel, which names nothing. Walking the chain ourselves prints the type that actually
    // failed, which is the one fact worth having.
    Console.Error.WriteLine("ReactiveUI initialisation failed.");

    for (var current = exception; current is not null; current = current.InnerException)
    {
        Console.Error.WriteLine($"  {current.GetType().FullName}: {current.Message}");

        if (current is TypeInitializationException typeInitialization)
        {
            Console.Error.WriteLine($"  ...while initialising {typeInitialization.TypeName}");
        }
    }

    Console.Error.WriteLine(exception.ToString());
    throw;
}

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();

// The hub connection and the message cache live here, so they survive page navigation.
builder.Services.AddSingleton<MqttStreamClient>();

// Transient, deliberately: ReactiveInjectableComponentBase disposes its injected view model when the
// component unmounts, so a singleton would come back disposed on the next navigation. Shared state
// belongs in MqttStreamClient, which is the singleton.
builder.Services.AddTransient<LiveViewModel>();
builder.Services.AddTransient<ConnectionViewModel>();

await builder.Build().RunAsync();
