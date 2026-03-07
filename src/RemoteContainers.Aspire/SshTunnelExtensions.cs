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

using Aspire.Hosting;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
    if (string.IsNullOrWhiteSpace(dockerHost))
    {
      return builder;
    }

    if (!builder.Services.Any(d => d.ServiceType == typeof(DockerApiClient)))
    {
      var tlsVerify = string.Equals(
        Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY"), "1", StringComparison.Ordinal);

      builder.Services.AddHttpClient<DockerApiClient>(client =>
      {
        if (!string.IsNullOrEmpty(dockerHost)
          && dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
          var scheme = tlsVerify ? "https://" : "http://";
          client.BaseAddress = new Uri(
            dockerHost.Replace("tcp://", scheme, StringComparison.OrdinalIgnoreCase));
        }
      })
      .ConfigurePrimaryHttpMessageHandler(() => DockerApiClient.CreateTlsHandler(tlsVerify))
      .AddStandardResilienceHandler();
    }

    builder.Services.TryAddSingleton<SshTunnelManager>();
    builder.Services.TryAddEventingSubscriber<SshTunnelLifecycleHook>();

    return builder;
  }
}
