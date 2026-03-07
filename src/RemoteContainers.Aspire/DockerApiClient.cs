// <copyright file="DockerApiClient.cs" company="Henrik Jensen">
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

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Hj.RemoteContainers.Aspire.Models;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire;

/// <summary>
/// Queries the Docker REST API to resolve container port bindings.
/// </summary>
internal sealed class DockerApiClient
{
  private static readonly TimeSpan _containerStartTimeout = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan _containerPollInterval = TimeSpan.FromSeconds(5);

  private readonly ILogger<DockerApiClient> _logger;
  private readonly HttpClient _httpClient;
  private readonly bool _isEnabled;

  public DockerApiClient(ILogger<DockerApiClient> logger, HttpClient httpClient)
  {
    _logger = logger;
    _httpClient = httpClient;
    _isEnabled = httpClient.BaseAddress is not null;
  }

  internal string? RemoteHost => _httpClient.BaseAddress?.Host;

  /// <summary>
  /// Polls until the named container is running and returns ALL its published port mappings, or null if the container
  /// is not found within the timeout.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ResourceEndpointsAllocatedEvent fires before DCP actually starts containers on the remote Docker host, so an
  /// immediate query would return an empty list. Polling handles this toctou.
  /// </para>
  /// <para>
  /// A stale container from a previous Aspire run may still be running when the event fires. To avoid tunnelling the wrong ports,
  /// the method requires the same container ID to appear on two consecutive polls before accepting the result. If DCP recycles
  /// the container between polls the ID changes and polling continues until the replacement stabilises.
  /// </para>
  /// </remarks>
  /// <param name="resourceName">A resource name for which remote container ports will be retrieved.</param>
  /// <param name="cancellationToken">A cancellation token.</param>
  /// <returns>
  /// A dictionary mapping container port → Docker host port, or null when disabled or timed out.
  /// </returns>
  public async Task<IReadOnlyDictionary<int, int>?> GetAllContainerHostPortsAsync(
    string resourceName,
    CancellationToken cancellationToken = default)
  {
    if (!_isEnabled)
    {
      return null;
    }

    var deadline = DateTime.UtcNow + _containerStartTimeout;
    string? lastSeenId = null;
    var isFirstPoll = true;

    while (DateTime.UtcNow < deadline)
    {
      try
      {
        var result = await TryQueryAllContainerHostPortsAsync(resourceName, cancellationToken);

        if (result is not null)
        {
          if (isFirstPoll)
          {
            // Found on the very first attempt — might be a stale container from a previous Aspire run. Record the ID and re-poll to confirm.
            lastSeenId = result.Id;
            isFirstPoll = false;
            await Task.Delay(_containerPollInterval, cancellationToken);
            continue;
          }

          if (lastSeenId is null || result.Id == lastSeenId)
          {
            // Either appeared after an empty poll (definitely new) or confirmed stable across two consecutive polls. Accept.
            return result.Ports;
          }

          // Container ID changed between polls — track the new one and re-confirm.
          lastSeenId = result.Id;
          await Task.Delay(_containerPollInterval, cancellationToken);
          continue;
        }

        // Container not found — any future find is definitely from the current run.
        lastSeenId = null;
        isFirstPoll = false;
      }
      catch (HttpRequestException ex)
      {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
          _logger.LogDebug(ex, "Docker API request failed for {ResourceName}; retrying…", resourceName);
        }

        lastSeenId = null;
        isFirstPoll = false;
        await Task.Delay(_containerPollInterval, cancellationToken);
        continue;
      }

      if (_logger.IsEnabled(LogLevel.Debug))
      {
        _logger.LogDebug(
          "Container {ResourceName} not yet visible in Docker; retrying in {Interval}ms…",
          resourceName,
          _containerPollInterval.TotalMilliseconds);
      }

      await Task.Delay(_containerPollInterval, cancellationToken);
    }

    if (_logger.IsEnabled(LogLevel.Warning))
    {
      _logger.LogWarning(
        "Container {ResourceName} not found in Docker after {Timeout}s — no SSH tunnels created",
        resourceName,
        _containerStartTimeout.TotalSeconds);
    }

    return null;
  }

  /// <summary>
  /// Creates the <see cref="HttpClientHandler"/> for the Docker API. Loads mutual TLS certificates
  /// from DOCKER_CERT_PATH when TLS verification is enabled.
  /// Throws <see cref="InvalidOperationException"/> if DOCKER_CERT_PATH is not set.
  /// </summary>
  /// <param name="tlsVerify">Whether DOCKER_TLS_VERIFY is enabled.</param>
  /// <returns>A http client handler configured to use a client certificate.</returns>
  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Certificates are app-lifetime objects: clientCert is owned by ClientCertificates, caCert is captured by the validation callback. Both live until process exit.")]
  internal static HttpClientHandler CreateTlsHandler(bool tlsVerify)
  {
    if (!tlsVerify)
    {
      return new HttpClientHandler();
    }

    var certPath = Environment.GetEnvironmentVariable("DOCKER_CERT_PATH")
      ?? throw new InvalidOperationException(
        "DOCKER_CERT_PATH must be set in the environment when DOCKER_TLS_VERIFY=1.");

    var caCert = X509Certificate2.CreateFromPem(
      File.ReadAllText(Path.Combine(certPath, "ca.pem")));

    var clientCert = X509Certificate2.CreateFromPem(
      File.ReadAllText(Path.Combine(certPath, "cert.pem")),
      File.ReadAllText(Path.Combine(certPath, "key.pem")));

    var handler = new HttpClientHandler();
    handler.ClientCertificates.Add(clientCert);
    handler.ServerCertificateCustomValidationCallback = (_, serverCert, chain, _) =>
    {
      chain!.ChainPolicy.CustomTrustStore.Add(caCert);
      chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      return chain.Build(serverCert!);
    };

    return handler;
  }

  private async Task<ContainerPorts?> TryQueryAllContainerHostPortsAsync(
    string resourceName,
    CancellationToken cancellationToken)
  {
    var json = await _httpClient.GetStringAsync("containers/json", cancellationToken);

    if (_logger.IsEnabled(LogLevel.Debug))
    {
      _logger.LogDebug("Docker containers/json for {ResourceName}: {Json}", resourceName, json);
    }

    using var doc = JsonDocument.Parse(json);
    var resourcePrefix = resourceName + "-";

    foreach (var container in doc.RootElement.EnumerateArray())
    {
      if (!container.TryGetProperty("Names", out var names))
      {
        continue;
      }

      var matched = false;
      foreach (var nameEl in names.EnumerateArray())
      {
        var rawName = nameEl.GetString();
        if (rawName is null)
        {
          continue;
        }

        // Docker API returns names with a leading "/" — strip it before comparing.
        var name = rawName.AsSpan().TrimStart('/');
        if (name.Equals(resourceName, StringComparison.OrdinalIgnoreCase)
          || name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
          matched = true;
          break;
        }
      }

      if (!matched)
      {
        continue;
      }

      var containerId = container.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;

      if (!container.TryGetProperty("Ports", out var ports))
      {
        continue;
      }

      var portMappings = new Dictionary<int, int>();

      foreach (var port in ports.EnumerateArray())
      {
        if (port.TryGetProperty("PrivatePort", out var priv)
          && priv.TryGetInt32(out var privPort)
          && port.TryGetProperty("PublicPort", out var pub)
          && pub.TryGetInt32(out var hostPort)
          && hostPort > 0)
        {
          portMappings[privPort] = hostPort;
        }
      }

      if (portMappings.Count > 0)
      {
        if (_logger.IsEnabled(LogLevel.Information))
        {
          foreach (var (containerPort, hostPort) in portMappings)
          {
            _logger.LogInformation(
              "Resolved Docker host port: {ResourceName} container:{ContainerPort} → host:{HostPort}",
              resourceName,
              containerPort,
              hostPort);
          }
        }

        return new ContainerPorts()
        {
          Id = containerId,
          Ports = portMappings,
        };
      }
    }

    return null;
  }
}
