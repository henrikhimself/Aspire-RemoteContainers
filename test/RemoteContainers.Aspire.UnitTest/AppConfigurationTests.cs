using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class AppConfigurationTests
{
  [Fact]
  public void ContainerStartTimeout_WhenNotConfigured_ReturnsFiveMinutes()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act
    var result = sut.ContainerStartTimeout;

    // Assert
    Assert.Equal(TimeSpan.FromMinutes(5), result);
  }

  [Fact]
  public void ContainerStartTimeout_WhenConfigured_ReturnsConfiguredValue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:ContainerStartTimeout", "00:02:30")
        .Build();

    // Act
    var result = sut.ContainerStartTimeout;

    // Assert
    Assert.Equal(TimeSpan.FromMinutes(2.5), result);
  }

  [Fact]
  public void ContainerPollInterval_WhenNotConfigured_ReturnsOneSecond()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act
    var result = sut.ContainerPollInterval;

    // Assert
    Assert.Equal(TimeSpan.FromSeconds(1), result);
  }

  [Fact]
  public void ContainerPollInterval_WhenConfigured_ReturnsConfiguredValue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:ContainerPollInterval", "00:00:01")
        .Build();

    // Act
    var result = sut.ContainerPollInterval;

    // Assert
    Assert.Equal(TimeSpan.FromSeconds(1), result);
  }

  [Fact]
  public void SshKeyPath_ReturnsDotSshUnderUserProfile()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithUserProfilePath("/home/user")
        .Build();

    // Act
    var result = sut.SshKeyPath;

    // Assert
    Assert.Equal("/home/user/.ssh", result);
  }

  [Fact]
  public void SshUser_WhenConfigured_ReturnsConfiguredValue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:SshUser", "deploy")
        .Build();

    // Act
    var result = sut.SshUser;

    // Assert
    Assert.Equal("deploy", result);
  }

  [Fact]
  public void SshUser_WhenNotConfigured_ReturnsEnvironmentUserName()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithUserName("system-user")
        .Build();

    // Act
    var result = sut.SshUser;

    // Assert
    Assert.Equal("system-user", result);
  }

  [Fact]
  public void SshUser_WhenConfiguredWithWhitespace_ReturnsEnvironmentUserName()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:SshUser", "   ")
        .WithUserName("system-user")
        .Build();

    // Act
    var result = sut.SshUser;

    // Assert
    Assert.Equal("system-user", result);
  }

  [Fact]
  public void SshHost_WhenConfigured_ReturnsConfiguredValue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:SshHost", "my-server")
        .Build();

    // Act
    var result = sut.SshHost;

    // Assert
    Assert.Equal("my-server", result);
  }

  [Fact]
  public void SshHost_WhenNotConfigured_FallsBackToDockerHostname()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_HOST", "tcp://remote-host:2376")
        .Build();

    // Act
    var result = sut.SshHost;

    // Assert
    Assert.Equal("remote-host", result);
  }

  [Fact]
  public void SshHost_WhenNotConfiguredAndNoDockerHost_Throws()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act & Assert
    Assert.Throws<InvalidOperationException>(() => sut.SshHost);
  }

  [Fact]
  public void SshHost_WhenConfiguredWithWhitespace_FallsBackToDockerHostname()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithConfig("RemoteContainers:SshHost", "   ")
        .WithEnvVar("DOCKER_HOST", "tcp://remote-host:2376")
        .Build();

    // Act
    var result = sut.SshHost;

    // Assert
    Assert.Equal("remote-host", result);
  }

  [Fact]
  public void HasDockerHost_WhenDockerHostIsTcp_ReturnsTrue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_HOST", "tcp://remote-host:2376")
        .Build();

    // Act
    var result = sut.HasDockerHost;

    // Assert
    Assert.True(result);
  }

  [Fact]
  public void HasDockerHost_WhenDockerHostIsNotTcp_ReturnsFalse()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_HOST", "unix:///var/run/docker.sock")
        .Build();

    // Act
    var result = sut.HasDockerHost;

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void HasDockerHost_WhenDockerHostIsNotSet_ReturnsFalse()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act
    var result = sut.HasDockerHost;

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void DockerHost_WhenTlsEnabled_UsesHttpsScheme()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_HOST", "tcp://remote-host:2376")
        .WithEnvVar("DOCKER_TLS_VERIFY", "1")
        .WithEnvVar("DOCKER_CERT_PATH", "/certs")
        .Build();

    // Act
    var result = sut.DockerHost;

    // Assert
    Assert.Equal("https", result?.Scheme);
  }

  [Fact]
  public void DockerHost_WhenTlsNotEnabled_UsesHttpScheme()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_HOST", "tcp://remote-host:2375")
        .Build();

    // Act
    var result = sut.DockerHost;

    // Assert
    Assert.Equal("http", result?.Scheme);
  }

  [Fact]
  public void DockerHost_WhenDockerHostNotSet_ReturnsNull()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act
    var result = sut.DockerHost;

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public void IsDockerTls_WhenVerifyAndCertPathSet_ReturnsTrue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_TLS_VERIFY", "1")
        .WithEnvVar("DOCKER_CERT_PATH", "/certs")
        .Build();

    // Act
    var result = sut.IsDockerTls;

    // Assert
    Assert.True(result);
  }

  [Fact]
  public void IsDockerTls_WhenVerifySetButNoCertPath_ReturnsFalse()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_TLS_VERIFY", "1")
        .Build();

    // Act
    var result = sut.IsDockerTls;

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void IsDockerTls_WhenCertPathSetButVerifyNotOne_ReturnsFalse()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_CERT_PATH", "/certs")
        .Build();

    // Act
    var result = sut.IsDockerTls;

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void DockerCertPath_WhenSet_ReturnsValue()
  {
    // Arrange
    var sut = Fixture.Create()
        .WithEnvVar("DOCKER_CERT_PATH", "/certs/client")
        .Build();

    // Act
    var result = sut.DockerCertPath;

    // Assert
    Assert.Equal("/certs/client", result);
  }

  [Fact]
  public void DockerCertPath_WhenNotSet_ReturnsNull()
  {
    // Arrange
    var sut = Fixture.Create().Build();

    // Act
    var result = sut.DockerCertPath;

    // Assert
    Assert.Null(result);
  }

  private sealed record Fixture(
    Dictionary<string, string?> ConfigValues,
    Hashtable EnvVars,
    string UserName,
    string UserProfilePath)
  {
    public static Fixture Create() => new([], [], "test-user", "/home/test-user");

    public Fixture WithConfig(string key, string value) =>
      this with { ConfigValues = new Dictionary<string, string?>(ConfigValues) { [key] = value } };

    public Fixture WithEnvVar(string key, string value) =>
      this with { EnvVars = new Hashtable(EnvVars) { [key] = value } };

    public Fixture WithUserName(string userName) => this with { UserName = userName };

    public Fixture WithUserProfilePath(string path) => this with { UserProfilePath = path };

    public AppConfiguration Build()
    {
      var config = new ConfigurationBuilder()
          .AddInMemoryCollection(ConfigValues)
          .Build();

      return new AppConfiguration(config, UserName, UserProfilePath, EnvVars);
    }
  }
}
