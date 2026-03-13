using Hj.RemoteContainers.Aspire.Abstractions;
using Hj.RemoteContainers.Aspire.Docker;
using Hj.RemoteContainers.Aspire.Ssh;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class SshTunnelManagerTests
{
  [Fact]
  public async Task AddAllContainerPortForwardsAsync_WhenNotConnected_DoesNotForwardAnyPorts()
  {
    // Arrange
    var f = Fixture.Create(connected: false);

    // Act
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Assert
    f.SshConnection.DidNotReceive().ForwardPort(Arg.Any<uint>());
  }

  [Fact]
  public async Task AddAllContainerPortForwardsAsync_WhenDockerApiReturnsNull_DoesNotForwardAnyPorts()
  {
    // Arrange
    var f = Fixture.Create();
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>(null));

    // Act
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Assert
    f.SshConnection.DidNotReceive().ForwardPort(Arg.Any<uint>());
  }

  [Fact]
  public async Task AddAllContainerPortForwardsAsync_WhenDockerApiReturnsEmptyList_DoesNotForwardAnyPorts()
  {
    // Arrange
    var f = Fixture.Create();
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>([]) );

    // Act
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Assert
    f.SshConnection.DidNotReceive().ForwardPort(Arg.Any<uint>());
  }

  [Fact]
  public async Task AddAllContainerPortForwardsAsync_WhenPortsAvailable_ForwardsEachPort()
  {
    // Arrange
    var f = Fixture.Create();
    var port1 = CreateForwardedPort(8080u);
    var port2 = CreateForwardedPort(5432u);
    f.SshConnection.ForwardPort(8080u).Returns(port1);
    f.SshConnection.ForwardPort(5432u).Returns(port2);
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>([8080u, 5432u]));

    // Act
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Assert
    f.SshConnection.Received(1).ForwardPort(8080u);
    f.SshConnection.Received(1).ForwardPort(5432u);
  }

  [Fact]
  public async Task AddAllContainerPortForwardsAsync_WhenCalledMultipleTimes_AccumulatesPorts()
  {
    // Arrange
    var f = Fixture.Create();
    f.SshConnection.ForwardPort(Arg.Any<uint>()).Returns(info => CreateForwardedPort(info.Arg<uint>()));
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(
        Task.FromResult<List<uint>?>([8080u]),
        Task.FromResult<List<uint>?>([5432u]));

    // Act
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Assert
    f.SshConnection.Received(2).ForwardPort(Arg.Any<uint>());
  }

  [Fact]
  public async Task RemoveAllContainerPortForwards_WhenPortsExist_StopsAndDisposesEachPort()
  {
    // Arrange
    var f = Fixture.Create();
    var port1 = CreateForwardedPort(8080u);
    var port2 = CreateForwardedPort(5432u);
    f.SshConnection.ForwardPort(8080u).Returns(port1);
    f.SshConnection.ForwardPort(5432u).Returns(port2);
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>([8080u, 5432u]));
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Act
    f.Sut.RemoveAllContainerPortForwards("my-resource");

    // Assert
    port1.Received(1).Stop();
    port1.Received(1).Dispose();
    port2.Received(1).Stop();
    port2.Received(1).Dispose();
  }

  [Fact]
  public void RemoveAllContainerPortForwards_WhenResourceNotFound_DoesNotThrow()
  {
    // Arrange
    var f = Fixture.Create();

    // Act
    var exception = Record.Exception(() => f.Sut.RemoveAllContainerPortForwards("unknown-resource"));

    // Assert
    Assert.Null(exception);
  }

  [Fact]
  public async Task RemoveAllContainerPortForwards_WhenPortStopThrows_ExceptionIsSwallowed()
  {
    // Arrange
    var f = Fixture.Create();
    var port = CreateForwardedPort(8080u);
    port.When(p => p.Stop()).Throw(new InvalidOperationException("stop failed"));
    f.SshConnection.ForwardPort(8080u).Returns(port);
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>([8080u]));
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Act
    var exception = Record.Exception(() => f.Sut.RemoveAllContainerPortForwards("my-resource"));

    // Assert
    Assert.Null(exception);
  }

  [Fact]
  public async Task Dispose_WhenPortsForwarded_StopsAndDisposesAllPortsAndSshConnection()
  {
    // Arrange
    var f = Fixture.Create();
    var port1 = CreateForwardedPort(8080u);
    var port2 = CreateForwardedPort(5432u);
    f.SshConnection.ForwardPort(8080u).Returns(port1);
    f.SshConnection.ForwardPort(5432u).Returns(port2);
    f.DockerApiClient
      .GetAllContainerHostPortsAsync("my-resource", Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<List<uint>?>([8080u, 5432u]));
    await f.Sut.AddAllContainerPortForwardsAsync("my-resource", CancellationToken.None);

    // Act
    f.Sut.Dispose();

    // Assert
    port1.Received(1).Stop();
    port1.Received(1).Dispose();
    port2.Received(1).Stop();
    port2.Received(1).Dispose();
    f.SshConnection.Received(1).Dispose();
  }

  [Fact]
  public void Dispose_WhenSshConnectionNeverUsed_DoesNotDisposeSshConnection()
  {
    // Arrange
    var f = Fixture.Create();

    // Act
    f.Sut.Dispose();

    // Assert
    f.SshConnection.DidNotReceive().Dispose();
  }

  [Fact]
  public void Dispose_WhenCalledTwice_DoesNotThrow()
  {
    // Arrange
    var f = Fixture.Create();

    // Act
    var exception = Record.Exception(() =>
    {
      f.Sut.Dispose();
      f.Sut.Dispose();
    });

    // Assert
    Assert.Null(exception);
  }

  private static ISshForwardedPort CreateForwardedPort(uint port)
  {
    var forwardedPort = Substitute.For<ISshForwardedPort>();
    forwardedPort.Port.Returns(port);

    return forwardedPort;
  }

  private sealed record Fixture(
    SshTunnelManager Sut,
    ISshConnection SshConnection,
    IDockerApiClient DockerApiClient)
  {
    public static Fixture Create(bool connected = true)
    {
      var sshConnection = Substitute.For<ISshConnection>();
      sshConnection.IsConnected.Returns(connected);

      var dockerApiClient = Substitute.For<IDockerApiClient>();
      var logger = Substitute.For<ILogger<SshTunnelManager>>();

      return new Fixture(
        new SshTunnelManager(logger, sshConnection, dockerApiClient),
        sshConnection,
        dockerApiClient);
    }
  }
}
