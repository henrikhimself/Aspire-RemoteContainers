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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Hj.RemoteContainers.Aspire;

/// <summary>
/// Manages SSH port forwarding tunnels for remote Docker container access.
/// </summary>
internal sealed class SshTunnelManager : IDisposable
{
  private readonly ILogger<SshTunnelManager> _logger;
  private readonly SshClient? _sshClient;
  private readonly List<ForwardedPortLocal> _forwardedPorts = [];
  private readonly object _lock = new();
  private readonly PrivateKeyFile[]? _keyFiles;
  private readonly DockerApiClient _dockerApiClient;

  public SshTunnelManager(ILogger<SshTunnelManager> logger, IConfiguration configuration, DockerApiClient dockerApiClient)
  {
    _logger = logger;
    _dockerApiClient = dockerApiClient;

    var remoteHost = dockerApiClient.RemoteHost;
    var sshHost = configuration["SSH_HOST"];
    var sshUser = configuration["SSH_USER"];

    if (remoteHost is null)
    {
      _logger.LogInformation("SSH tunneling disabled - DOCKER_HOST is not a remote tcp:// address");
      return;
    }

    if (string.IsNullOrEmpty(sshHost))
    {
      sshHost = remoteHost;
    }

    if (string.IsNullOrEmpty(sshUser))
    {
      sshUser = Environment.UserName;
    }

    try
    {
      if (_logger.IsEnabled(LogLevel.Information))
      {
        _logger.LogInformation("Connecting to SSH host {SshHost} as {SshUser}", sshHost, sshUser);
      }

      _keyFiles = LoadSshKeyFiles();
      _sshClient = new SshClient(sshHost, sshUser, _keyFiles);
      _sshClient.Connect();

      if (_logger.IsEnabled(LogLevel.Information))
      {
        _logger.LogInformation("SSH connection established");
      }
    }
    catch (Exception ex)
    {
      if (_logger.IsEnabled(LogLevel.Warning))
      {
        _logger.LogWarning(ex, "Failed to establish SSH connection - port forwarding disabled");
      }

      _sshClient?.Dispose();
      _sshClient = null;
    }
  }

  /// <summary>
  /// Queries the Docker API for all published ports of the named container and creates SSH tunnels for each one.
  /// </summary>
  /// <param name="resourceName">A resource name for which remote container ports will be retrieved.</param>
  /// <param name="cancellationToken">A cancellation token.</param>
  /// <returns>A task.</returns>
  public async Task AddAllContainerPortForwardsAsync(string resourceName, CancellationToken cancellationToken)
  {
    if (_sshClient is null || !_sshClient.IsConnected)
    {
      return;
    }

    var portMappings = await _dockerApiClient.GetAllContainerHostPortsAsync(resourceName, cancellationToken);

    if (portMappings is null)
    {
      return;
    }

    foreach (var (containerPort, hostPort) in portMappings)
    {
      AddPortForward((uint)hostPort, $"{resourceName} (container:{containerPort})");
    }
  }

  public void Dispose()
  {
    ForwardedPortLocal[] portsSnapshot;
    lock (_lock)
    {
      portsSnapshot = [.. _forwardedPorts];
      _forwardedPorts.Clear();
    }

    foreach (var port in portsSnapshot)
    {
      try
      {
        port.Stop();
        port.Dispose();
      }
      catch
      {
        // Best effort cleanup
      }
    }

    _sshClient?.Disconnect();
    _sshClient?.Dispose();

    if (_keyFiles is not null)
    {
      foreach (var keyFile in _keyFiles)
      {
        keyFile.Dispose();
      }
    }

    if (_logger.IsEnabled(LogLevel.Information))
    {
      _logger.LogInformation("SSH tunnels closed");
    }
  }

  private static PrivateKeyFile[] LoadSshKeyFiles()
  {
    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var sshDir = Path.Combine(homeDir, ".ssh");

    // Try common SSH key locations
    var keyPaths = new[]
    {
      Path.Combine(sshDir, "id_rsa"),
      Path.Combine(sshDir, "id_ed25519"),
      Path.Combine(sshDir, "id_ecdsa"),
    };

    var keys = new List<PrivateKeyFile>();

    foreach (var keyPath in keyPaths)
    {
      if (File.Exists(keyPath))
      {
        try
        {
          keys.Add(new PrivateKeyFile(keyPath));
        }
        catch
        {
          // Key might be encrypted or invalid, skip it
        }
      }
    }

    if (keys.Count == 0)
    {
      throw new InvalidOperationException(
        $"No SSH private keys found in {sshDir}. Ensure you have id_rsa, id_ed25519, or id_ecdsa.");
    }

    return [.. keys];
  }

  /// <summary>
  /// Creates a port forward from local port to remote localhost:port through the SSH tunnel.
  /// </summary>
  private void AddPortForward(uint port, string description)
  {
    if (_sshClient is null || !_sshClient.IsConnected)
    {
      return;
    }

    lock (_lock)
    {
      ForwardedPortLocal? forwardedPort = null;
      try
      {
        forwardedPort = new ForwardedPortLocal("127.0.0.1", port, "127.0.0.1", port);
        _sshClient.AddForwardedPort(forwardedPort);
        forwardedPort.Start();

        _forwardedPorts.Add(forwardedPort);
        if (_logger.IsEnabled(LogLevel.Information))
        {
          _logger.LogInformation(
            "Port forward established: localhost:{LocalPort} → remote:{RemotePort} ({Description})",
            port,
            port,
            description);
        }
      }
      catch (Exception ex)
      {
        forwardedPort?.Dispose();
        if (_logger.IsEnabled(LogLevel.Warning))
        {
          _logger.LogWarning(ex, "Failed to create port forward for {Port} ({Description})", port, description);
        }
      }
    }
  }
}
