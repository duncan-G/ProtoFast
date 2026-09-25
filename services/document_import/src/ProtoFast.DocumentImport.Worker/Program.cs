using ProtoFast.DocumentImport.Worker;
using ProtoFast.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("DocumentImport_");

builder.Services.AddHostedService<Worker>();

builder.Build().Run();
