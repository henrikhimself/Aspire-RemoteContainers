using Hj.RemoteContainers.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddSshTunneling();

for (var i = 0; i < 3; i++)
{
  _ = builder.AddContainer("whoami-" + i, "traefik/whoami:latest")
      .WithHttpEndpoint(port: 8080 + i, targetPort: 80);
}

await builder.Build().RunAsync();
