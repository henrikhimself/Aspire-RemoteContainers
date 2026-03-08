// <copyright file="SshTunnelManager.cs" company="Henrik Jensen">
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

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Hj.RemoteContainers.Aspire;

/// <summary>
/// Manages SSH port forwarding tunnels for remote Docker container access.
/// </summary>
internal sealed class SshTunnelManager : IDisposable
{
  private readonly ILogger<SshTunnelManager> _logger;
  private readonly AppConfiguration _appConfiguration;
  private readonly DockerApiClient _dockerApiClient;
  private readonly Lazy<SshTunnelClient> _sshTunnelClient;

  private readonly ConcurrentBag<ForwardedPortLocal> _forwardedPorts = [];
  private bool _disposedValue;

  public SshTunnelManager(ILogger<SshTunnelManager> logger, AppConfiguration appConfiguration, DockerApiClient dockerApiClient)
  {
    _logger = logger;
    _appConfiguration = appConfiguration;
    _dockerApiClient = dockerApiClient;
    _sshTunnelClient = new Lazy<SshTunnelClient>(ConnectSshTunnelClient);
  }

  /// <summary>
  /// Queries the Docker API for all published ports of the named container and creates SSH tunnels for each one.
  /// </summary>
  /// <param name="resourceName">A resource name for which remote container ports will be retrieved.</param>
  /// <param name="cancellationToken">A cancellation token.</param>
  /// <returns>A task.</returns>
  public async Task AddAllContainerPortForwardsAsync(string resourceName, CancellationToken cancellationToken)
  {
    if (!_sshTunnelClient.Value.IsConnected)
    {
      return;
    }

    var portMappings = await _dockerApiClient.GetAllContainerHostPortsAsync(resourceName, cancellationToken);
    if (portMappings is null)
    {
      return;
    }

    foreach (var port in portMappings)
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
      var isPortForwarded = _sshTunnelClient.Value.TryForwardPort(port, out var forwardedPort);
#pragma warning restore CA2000 // Dispose objects before losing scope
      if (forwardedPort is not null)
      {
        _forwardedPorts.Add(forwardedPort);
      }

      if (isPortForwarded)
      {
        if (_logger.IsEnabled(LogLevel.Information))
        {
          _logger.LogInformation(
            "Port forwarded: localhost:{Port} → remote:{Port}",
            port,
            port);
        }
      }
      else if (_logger.IsEnabled(LogLevel.Warning))
      {
        _logger.LogWarning(
            "Failed to forward port: localhost:{Port} → remote:{Port}",
            port,
            port);
      }
    }
  }

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing)
  {
    if (!_disposedValue)
    {
      if (disposing)
      {
        while (_forwardedPorts.TryTake(out var forwardedPort))
        {
          try
          {
            forwardedPort.Stop();
            forwardedPort.Dispose();
          }
          catch
          {
            // Best effort cleanup
          }
        }

        if (_sshTunnelClient.IsValueCreated)
        {
          _sshTunnelClient.Value.Dispose();
        }
      }

      _disposedValue = true;
    }
  }

  private SshTunnelClient ConnectSshTunnelClient()
    => SshTunnelClient.Connect(_appConfiguration.SshKeyPath, _appConfiguration.SshHost, _appConfiguration.SshUser);
}
