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

  public DockerApiClient(ILogger<DockerApiClient> logger, HttpClient httpClient)
  {
    _logger = logger;
    _httpClient = httpClient;
  }

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
  public async Task<List<uint>?> GetAllContainerHostPortsAsync(string resourceName, CancellationToken cancellationToken)
  {
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
            // Either appeared after an empty poll (definitely new) or confirmed stable across two consecutive polls.
            if (_logger.IsEnabled(LogLevel.Information))
            {
              _logger.LogInformation("Resolved Docker port: {ResourceName}: {Port}", resourceName, string.Join(",", result.Ports));
            }

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

  private async Task<ContainerPorts?> TryQueryAllContainerHostPortsAsync(string resourceName, CancellationToken cancellationToken)
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
        if (name.Equals(resourceName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
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

      var portMappings = new List<uint>();
      foreach (var portElement in ports.EnumerateArray())
      {
        if (portElement.TryGetProperty("PublicPort", out var publicPort)
          && publicPort.TryGetUInt32(out var port)
          && port > 0)
        {
          portMappings.Add(port);
        }
      }

      if (portMappings.Count > 0)
      {
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
