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
  private static readonly TimeSpan _notificationAutoDismissDelay = TimeSpan.FromSeconds(5);

  private readonly ILogger<SshTunnelLifecycleHook> _logger;
  private readonly ISshTunnelManager _tunnelManager;
  private readonly IInteractionService _interactionService;

  public SshTunnelLifecycleHook(ILogger<SshTunnelLifecycleHook> logger, ISshTunnelManager tunnelManager, IInteractionService interactionService)
  {
    _logger = logger;
    _tunnelManager = tunnelManager;
    _interactionService = interactionService;
  }

  public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
  {
    eventing.Subscribe<ResourceReadyEvent>(SetUpTunnelOnReadyAsync);
    eventing.Subscribe<ResourceStoppedEvent>(TearDownTunnelAsync);

    return Task.CompletedTask;
  }

  private static bool IsContainerResource(IResource resource) =>
    resource.Annotations.OfType<ContainerImageAnnotation>().Any();

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
      await PromptNotificationAsync($"Port forwarding started for {evt.Resource.Name}.", MessageIntent.Success, cancellationToken);
    }
    catch (Exception ex)
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError(ex, "Failed to re-establish SSH tunnel for resource {ResourceName}", evt.Resource.Name);
      }

      await PromptNotificationAsync($"Port forward creation failed for {evt.Resource.Name}: {ex.Message}", MessageIntent.Error, cancellationToken);
    }
  }

  private async Task TearDownTunnelAsync(ResourceStoppedEvent evt, CancellationToken cancellationToken)
  {
    if (!IsContainerResource(evt.Resource))
    {
      return;
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

      await PromptNotificationAsync($"Port forwarding removal failed for {evt.Resource.Name}.", MessageIntent.Error, cancellationToken);
    }
  }

  private async Task PromptNotificationAsync(string message, MessageIntent intent, CancellationToken cancellationToken)
  {
    if (!_interactionService.IsAvailable)
    {
      return;
    }

    var options = new NotificationInteractionOptions { Intent = intent };

    if (intent == MessageIntent.Error)
    {
      await _interactionService.PromptNotificationAsync("SSH Tunnel", message, options, cancellationToken);
    }
    else
    {
      // Auto-dismiss non-error notifications after a short delay so they don't linger.
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(_notificationAutoDismissDelay);
      await _interactionService.PromptNotificationAsync("SSH Tunnel", message, options, cts.Token);
    }
  }
}
