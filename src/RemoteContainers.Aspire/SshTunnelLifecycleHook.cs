// <copyright file="SshTunnelLifecycleHook.cs" company="Henrik Jensen">
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

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire;

internal sealed class SshTunnelLifecycleHook : IDistributedApplicationEventingSubscriber
{
  private readonly ILogger<SshTunnelLifecycleHook> _logger;
  private readonly ISshTunnelManager _tunnelManager;

  public SshTunnelLifecycleHook(ILogger<SshTunnelLifecycleHook> logger, ISshTunnelManager tunnelManager)
  {
    _logger = logger;
    _tunnelManager = tunnelManager;
  }

  public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
  {
    eventing.Subscribe<ResourceEndpointsAllocatedEvent>(SetUpTunnelAsync);
    eventing.Subscribe<ResourceReadyEvent>(SetUpTunnelOnReadyAsync);
    eventing.Subscribe<ResourceStoppedEvent>(TearDownTunnel);

    return Task.CompletedTask;
  }

  private static bool IsContainerResource(IResource resource) =>
    resource.Annotations.OfType<ContainerImageAnnotation>().Any();

  private async Task SetUpTunnelAsync(ResourceEndpointsAllocatedEvent evt, CancellationToken cancellationToken)
  {
    if (!IsContainerResource(evt.Resource))
    {
      return;
    }

    try
    {
      await _tunnelManager.AddAllContainerPortForwardsAsync(evt.Resource.Name, cancellationToken);
    }
    catch (Exception ex)
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError(ex, "Failed to set up SSH tunnel for resource {ResourceName}", evt.Resource.Name);
      }
    }
  }

  private async Task SetUpTunnelOnReadyAsync(ResourceReadyEvent evt, CancellationToken cancellationToken)
  {
    if (!IsContainerResource(evt.Resource))
    {
      return;
    }

    try
    {
      // Remove stale tunnels from the previous run of this resource before creating new ones.
      _tunnelManager.RemoveAllContainerPortForwards(evt.Resource.Name);
      await _tunnelManager.AddAllContainerPortForwardsAsync(evt.Resource.Name, cancellationToken);
    }
    catch (Exception ex)
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError(ex, "Failed to re-establish SSH tunnel for resource {ResourceName}", evt.Resource.Name);
      }
    }
  }

  private Task TearDownTunnel(ResourceStoppedEvent evt, CancellationToken cancellationToken)
  {
    if (!IsContainerResource(evt.Resource))
    {
      return Task.CompletedTask;
    }

    try
    {
      _tunnelManager.RemoveAllContainerPortForwards(evt.Resource.Name);
    }
    catch (Exception ex)
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError(ex, "Failed to tear down SSH tunnel for resource {ResourceName}", evt.Resource.Name);
      }
    }

    return Task.CompletedTask;
  }
}
