using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.Secrets;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Configuration.AddEnvironmentVariables("Shared_");
builder.Configuration.AddEnvironmentVariables("DocumentImport_");

builder.Configuration.AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));

builder.Services.AddGrpc();

builder.AddNpgsqlDataSource("document_play");
builder.AddRedisClient("redis");

var host = builder.Build();

host.Run();
