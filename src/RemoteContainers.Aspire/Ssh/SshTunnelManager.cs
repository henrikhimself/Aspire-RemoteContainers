// <copyright file="SshTunnelManager.cs" company="Henrik Jensen">
// Copyright 2026 Henrik Jensen
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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire.Ssh;

/// <summary>
/// Manages SSH port forwarding tunnels for remote Docker container access.
/// </summary>
internal sealed class SshTunnelManager : ISshTunnelManager, IDisposable
{
  private readonly ILogger<SshTunnelManager> _logger;
  private readonly IDockerApiClient _dockerApiClient;
  private readonly Lazy<ISshConnection> _sshConnection;

  private readonly ConcurrentDictionary<string, ConcurrentBag<ISshForwardedPort>> _forwardedPortsByResource = new();
  private bool _disposedValue;

  public SshTunnelManager(
    ILogger<SshTunnelManager> logger,
    ISshConnection sshConnection,
    IDockerApiClient dockerApiClient)
  {
    _logger = logger;
    _dockerApiClient = dockerApiClient;
    _sshConnection = new Lazy<ISshConnection>(() =>
    {
      sshConnection.Connect();
      return sshConnection;
    });
  }

  /// <summary>
  /// Queries the Docker API for all published ports of the named container and creates SSH tunnels for each one.
  /// </summary>
  /// <param name="resourceName">A resource name for which remote container ports will be retrieved.</param>
  /// <param name="cancellationToken">A cancellation token.</param>
  /// <returns>A task.</returns>
  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "See Dispose()")]
  public async Task AddAllContainerPortForwardsAsync(string resourceName, CancellationToken cancellationToken)
  {
    if (!_sshConnection.Value.IsConnected)
    {
      return;
    }

    var portMappings = await _dockerApiClient.GetAllContainerHostPortsAsync(resourceName, cancellationToken);
    if (portMappings is null)
    {
      return;
    }

    var resourcePorts = _forwardedPortsByResource.GetOrAdd(resourceName, _ => []);

    foreach (var port in portMappings)
    {
      var forwardedPort = _sshConnection.Value.ForwardPort(port);
      resourcePorts.Add(forwardedPort);

      if (_logger.IsEnabled(LogLevel.Information))
      {
        _logger.LogInformation("SSH tunnel created for {ResourceName} port {Port}", resourceName, forwardedPort.Port);
      }
    }
  }

  /// <summary>
  /// Stops and disposes all SSH tunnels associated with the specified resource.
  /// </summary>
  /// <param name="resourceName">The resource name whose tunnels should be removed.</param>
  public void RemoveAllContainerPortForwards(string resourceName)
  {
    if (!_forwardedPortsByResource.TryRemove(resourceName, out var ports))
    {
      return;
    }

    while (ports.TryTake(out var forwardedPort))
    {
      try
      {
        forwardedPort.Stop();
        forwardedPort.Dispose();

        if (_logger.IsEnabled(LogLevel.Information))
        {
          _logger.LogInformation("SSH tunnel removed for {ResourceName} port {Port}", resourceName, forwardedPort.BoundPort);
        }
      }
      catch (Exception ex)
      {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
          _logger.LogWarning(ex, "Failed to clean up SSH tunnel for {ResourceName} port {Port}", resourceName, forwardedPort.BoundPort);
        }
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
        foreach (var resourceName in _forwardedPortsByResource.Keys)
        {
          RemoveAllContainerPortForwards(resourceName);
        }

        if (_sshConnection.IsValueCreated)
        {
          _sshConnection.Value.Dispose();
        }
      }

      _disposedValue = true;
    }
  }
}
