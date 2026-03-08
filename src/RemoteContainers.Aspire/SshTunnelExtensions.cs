// <copyright file="SshTunnelExtensions.cs" company="Henrik Jensen">
// Copyright 2025 Henrik Jensen
//
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Hj.RemoteContainers.Aspire;

public static class SshTunnelExtensions
{
  /// <summary>
  /// Enables automatic SSH port forwarding for all container resources when DOCKER_HOST points to
  /// a remote Docker engine. After DCP allocates endpoints, every resource with at least one endpoint
  /// is matched against running Docker containers and tunnelled automatically.
  /// </summary>
  /// <param name="builder">An application builder instance.</param>
  /// <returns>The application builder instance.</returns>
  public static IDistributedApplicationBuilder AddSshTunneling(this IDistributedApplicationBuilder builder)
  {
    var environmentUserName = Environment.UserName;
    var environmentUserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var environmentVariables = Environment.GetEnvironmentVariables();

    AppConfiguration appConfiguration = new(builder.Configuration, environmentUserName, environmentUserProfilePath, environmentVariables);
    if (!appConfiguration.HasDockerHost)
    {
      // Skip setting up SSH tunnels.
      return builder;
    }

    var services = builder.Services;
    services.AddSingleton(appConfiguration);

    builder.Services.AddHttpClient<DockerApiClient>(httpClient =>
      {
        httpClient.BaseAddress = appConfiguration.DockerHost.Uri;
      })
      .ConfigurePrimaryHttpMessageHandler(() =>
      {
        var handler = new HttpClientHandler();
        if (appConfiguration.TryGetDockerCertificate(out var caCert, out var clientCert))
        {
          handler.ClientCertificates.Add(clientCert);
          handler.ServerCertificateCustomValidationCallback = (_, serverCert, chain, _) =>
          {
            if (serverCert is null || chain is null)
            {
              return false;
            }

            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            return chain.Build(serverCert);
          };
        }

        return handler;
      })
      .AddStandardResilienceHandler();

    builder.Services.AddSingleton<SshTunnelManager>();
    builder.Services.AddEventingSubscriber<SshTunnelLifecycleHook>();

    return builder;
  }
}
