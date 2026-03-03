using Hj.RemoteContainers.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddSshTunneling();

_ = builder.AddContainer("whoami", "traefik/whoami:latest")
    .WithHttpEndpoint(port: 8080, targetPort: 80);

await builder.Build().RunAsync();
