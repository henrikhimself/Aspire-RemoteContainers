using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Hj.RemoteContainers.Aspire.Ssh;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class SshTunnelLifecycleHookTests
{
  private static readonly DistributedApplicationExecutionContext ExecutionContext = new(DistributedApplicationOperation.Run);

  [Fact]
  public async Task SetUpTunnelOnReady_ContainerResource_RemovesOldTunnelsThenAddsNew()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(new ResourceReadyEvent(CreateContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None);

    // Assert
    Received.InOrder(() =>
    {
      f.TunnelManager.RemoveAllContainerPortForwards("my-resource");
      _ = f.TunnelManager.AddAllContainerPortForwardsAsync("my-resource", Arg.Any<CancellationToken>());
    });
  }

  [Fact]
  public async Task SetUpTunnelOnReady_NonContainerResource_DoesNotCallTunnelManager()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(new ResourceReadyEvent(CreateNonContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None);

    // Assert
    f.TunnelManager.DidNotReceive().RemoveAllContainerPortForwards(Arg.Any<string>());
    await f.TunnelManager.DidNotReceive().AddAllContainerPortForwardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task SetUpTunnelOnReady_WhenManagerThrows_ExceptionIsSwallowed()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    f.TunnelManager.AddAllContainerPortForwardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("boom"));
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    var exception = await Record.ExceptionAsync(
        () => handler(new ResourceReadyEvent(CreateContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None));

    // Assert
    Assert.Null(exception);
  }

  [Fact]
  public async Task TearDownTunnelAsync_ContainerResource_CallsRemoveAllContainerPortForwards()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceStoppedEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(CreateResourceStoppedEvent(CreateContainerResource("my-resource")), CancellationToken.None);

    // Assert
    f.TunnelManager.Received(1).RemoveAllContainerPortForwards("my-resource");
  }

  [Fact]
  public async Task TearDownTunnelAsync_NonContainerResource_DoesNotCallTunnelManager()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceStoppedEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(CreateResourceStoppedEvent(CreateNonContainerResource("my-resource")), CancellationToken.None);

    // Assert
    f.TunnelManager.DidNotReceive().RemoveAllContainerPortForwards(Arg.Any<string>());
  }

  [Fact]
  public async Task TearDownTunnelAsync_WhenManagerThrows_ExceptionIsSwallowed()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceStoppedEvent>();
    f.TunnelManager.When(m => m.RemoveAllContainerPortForwards(Arg.Any<string>()))
        .Throw(new InvalidOperationException("boom"));
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    var exception = await Record.ExceptionAsync(
        () => handler(CreateResourceStoppedEvent(CreateContainerResource("my-resource")), CancellationToken.None));

    // Assert
    Assert.Null(exception);
  }

  [Fact]
  public async Task SetUpTunnel_WhenContainerReady_SendsSuccessNotification()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(new ResourceReadyEvent(CreateContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None);

    // Assert
    _ = f.InteractionService.Received(1).PromptNotificationAsync(
      Arg.Any<string>(),
      Arg.Any<string>(),
      Arg.Is<NotificationInteractionOptions?>(o => o!.Intent == MessageIntent.Success),
      Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task SetUpTunnel_WhenAddPortForwardFails_SendsErrorNotification()
  {
    // Arrange
    var f = Fixture.Create();
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    f.TunnelManager.AddAllContainerPortForwardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("connection refused"));
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(new ResourceReadyEvent(CreateContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None);

    // Assert
    _ = f.InteractionService.Received(1).PromptNotificationAsync(
      Arg.Any<string>(),
      Arg.Is<string>(m => m.Contains("connection refused")),
      Arg.Is<NotificationInteractionOptions?>(o => o!.Intent == MessageIntent.Error),
      Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task SetUpTunnel_WhenInteractionServiceNotAvailable_DoesNotSendNotification()
  {
    // Arrange
    var f = Fixture.Create(interactionAvailable: false);
    var handler = f.CaptureHandler<ResourceReadyEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(new ResourceReadyEvent(CreateContainerResource("my-resource"), Substitute.For<IServiceProvider>()), CancellationToken.None);

    // Assert
    _ = f.InteractionService.DidNotReceive().PromptNotificationAsync(
      Arg.Any<string>(),
      Arg.Any<string>(),
      Arg.Any<NotificationInteractionOptions?>(),
      Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task TearDownTunnelAsync_WhenInteractionServiceNotAvailable_DoesNotSendNotification()
  {
    // Arrange
    var f = Fixture.Create(interactionAvailable: false);
    var handler = f.CaptureHandler<ResourceStoppedEvent>();
    await f.Sut.SubscribeAsync(f.Eventing, ExecutionContext, CancellationToken.None);

    // Act
    await handler(CreateResourceStoppedEvent(CreateContainerResource("my-resource")), CancellationToken.None);

    // Assert
    _ = f.InteractionService.DidNotReceive().PromptNotificationAsync(
      Arg.Any<string>(),
      Arg.Any<string>(),
      Arg.Any<NotificationInteractionOptions?>(),
      Arg.Any<CancellationToken>());
  }

  private static TestResource CreateContainerResource(string name)
  {
    var resource = new TestResource(name);
    resource.Annotations.Add(new ContainerImageAnnotation { Image = "test-image" });

    return resource;
  }

  private static TestResource CreateNonContainerResource(string name) => new TestResource(name);

  private static ResourceStoppedEvent CreateResourceStoppedEvent(IResource resource)
  {
    var snapshot = new CustomResourceSnapshot { ResourceType = "Container", Properties = [] };
    var resourceEvent = new ResourceEvent(resource, "resource-id", snapshot);

    return new ResourceStoppedEvent(resource, Substitute.For<IServiceProvider>(), resourceEvent);
  }

  private sealed class TestResource(string name) : IResource
  {
    public string Name { get; } = name;

    public ResourceAnnotationCollection Annotations { get; } = new();
  }

  private sealed record Fixture(
      SshTunnelLifecycleHook Sut,
      ISshTunnelManager TunnelManager,
      IDistributedApplicationEventing Eventing,
      IInteractionService InteractionService)
  {
    public static Fixture Create(bool interactionAvailable = true)
    {
      var tunnelManager = Substitute.For<ISshTunnelManager>();
      var logger = Substitute.For<ILogger<SshTunnelLifecycleHook>>();
      var interactionService = Substitute.For<IInteractionService>();
      interactionService.IsAvailable.Returns(interactionAvailable);

      return new Fixture(
          new SshTunnelLifecycleHook(logger, tunnelManager, interactionService),
          tunnelManager,
          Substitute.For<IDistributedApplicationEventing>(),
          interactionService);
    }

    public Func<T, CancellationToken, Task> CaptureHandler<T>()
        where T : IDistributedApplicationEvent
    {
      Func<T, CancellationToken, Task>? handler = null;
      Eventing.Subscribe(Arg.Do<Func<T, CancellationToken, Task>>(h => handler = h));

      return (evt, ct) => handler!(evt, ct);
    }
  }
}
