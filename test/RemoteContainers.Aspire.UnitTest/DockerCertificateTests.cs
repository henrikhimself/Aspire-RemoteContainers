using System.Collections;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hj.RemoteContainers.Aspire.Abstractions;
using Hj.RemoteContainers.Aspire.Docker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class DockerCertificateTests
{
  [Fact]
  public void TryGetCertificate_WhenTlsNotEnabled_ReturnsFalse()
  {
    // Arrange
    var f = Fixture.Create(tlsEnabled: false);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    Assert.False(result);
    Assert.Null(caCert);
    Assert.Null(clientCert);
  }

  [Fact]
  public void TryGetCertificate_WhenCaCertFileNotFound_ReturnsFalse()
  {
    // Arrange
    var f = Fixture.Create();
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(false);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    Assert.False(result);
    Assert.Null(caCert);
    Assert.Null(clientCert);
  }

  [Fact]
  public void TryGetCertificate_WhenCaCertFileEmpty_ReturnsFalse()
  {
    // Arrange
    var f = Fixture.Create();
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(true);
    f.FileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(string.Empty);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    Assert.False(result);
    Assert.Null(caCert);
    Assert.Null(clientCert);
  }

  [Fact]
  public void TryGetCertificate_WhenClientCertFileNotFound_ReturnsFalse()
  {
    // Arrange
    var f = Fixture.Create();
    var (caCertPem, _) = GenerateTestCertificate("CN=ca");
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(true);
    f.FileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(caCertPem);
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("cert.pem"))).Returns(false);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    Assert.False(result);
    Assert.Null(caCert);
    Assert.Null(clientCert);
  }

  [Fact]
  public void TryGetCertificate_WhenClientCertFileEmpty_ReturnsFalse()
  {
    // Arrange
    var f = Fixture.Create();
    var (caCertPem, _) = GenerateTestCertificate("CN=ca");
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(true);
    f.FileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(caCertPem);
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("cert.pem"))).Returns(true);
    f.FileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("cert.pem"))).Returns(string.Empty);
    f.FileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("key.pem"))).Returns(true);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    Assert.False(result);
    Assert.Null(caCert);
    Assert.Null(clientCert);
  }

  [Fact]
  public void TryGetCertificate_WhenAllCertsPresent_ReturnsTrueWithCertificates()
  {
    // Arrange
    var f = Fixture.Create();
    var (caCertPem, _) = GenerateTestCertificate("CN=ca");
    var (clientCertPem, clientKeyPem) = GenerateTestCertificate("CN=client");
    SetupAllCertFiles(f.FileSystem, caCertPem, clientCertPem, clientKeyPem);

    // Act
    var result = f.Sut.TryGetCertificate(out var caCert, out var clientCert);

    // Assert
    using (caCert)
    using (clientCert)
    {
      Assert.True(result);
      Assert.NotNull(caCert);
      Assert.NotNull(clientCert);
    }
  }

  [Fact]
  public void TryGetCertificate_WhenCalledTwice_ReturnsDifferentCertificateInstances()
  {
    // Arrange
    var f = Fixture.Create();
    var (caCertPem, _) = GenerateTestCertificate("CN=ca");
    var (clientCertPem, clientKeyPem) = GenerateTestCertificate("CN=client");
    SetupAllCertFiles(f.FileSystem, caCertPem, clientCertPem, clientKeyPem);
    f.Sut.TryGetCertificate(out var firstCaCert, out var firstClientCert);

    // Act
    var result = f.Sut.TryGetCertificate(out var secondCaCert, out var secondClientCert);

    // Assert
    using (firstCaCert)
    using (firstClientCert)
    using (secondCaCert)
    using (secondClientCert)
    {
      Assert.True(result);
      Assert.NotSame(firstCaCert, secondCaCert);
      Assert.NotSame(firstClientCert, secondClientCert);
    }
  }

  [Fact]
  public void TryGetCertificate_WhenCalledTwice_DoesNotReloadCertsFromFileSystem()
  {
    // Arrange
    var f = Fixture.Create();
    var (caCertPem, _) = GenerateTestCertificate("CN=ca");
    var (clientCertPem, clientKeyPem) = GenerateTestCertificate("CN=client");
    SetupAllCertFiles(f.FileSystem, caCertPem, clientCertPem, clientKeyPem);
    f.Sut.TryGetCertificate(out var firstCaCert, out var firstClientCert);
    firstCaCert?.Dispose();
    firstClientCert?.Dispose();

    // Act
    f.Sut.TryGetCertificate(out var secondCaCert, out var secondClientCert);
    secondCaCert?.Dispose();
    secondClientCert?.Dispose();

    // Assert
    f.FileSystem.Received(1).ReadAllText(Arg.Is<string>(p => p.EndsWith("ca.pem")));
    f.FileSystem.Received(1).ReadAllText(Arg.Is<string>(p => p.EndsWith("cert.pem")));
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

  private static (string CertPem, string KeyPem) GenerateTestCertificate(string subject)
  {
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
    using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    return (cert.ExportCertificatePem(), key.ExportECPrivateKeyPem());
  }

  private static void SetupAllCertFiles(IFileSystem fileSystem, string caCertPem, string clientCertPem, string clientKeyPem)
  {
    fileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(true);
    fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("ca.pem"))).Returns(caCertPem);
    fileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("cert.pem"))).Returns(true);
    fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("cert.pem"))).Returns(clientCertPem);
    fileSystem.FileExists(Arg.Is<string>(p => p.EndsWith("key.pem"))).Returns(true);
    fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith("key.pem"))).Returns(clientKeyPem);
  }

  private sealed record Fixture(DockerCertificate Sut, IFileSystem FileSystem)
  {
    public static Fixture Create(bool tlsEnabled = true)
    {
      var fileSystem = Substitute.For<IFileSystem>();
      var logger = Substitute.For<ILogger<DockerCertificate>>();

      var envVars = new Hashtable();
      if (tlsEnabled)
      {
        envVars["DOCKER_TLS_VERIFY"] = "1";
        envVars["DOCKER_CERT_PATH"] = "/certs";
        envVars["DOCKER_HOST"] = "tcp://remote:2376";
      }

      var config = new ConfigurationBuilder().Build();
      var appConfig = new AppConfiguration(config, "user", "/home/user", envVars);

      return new Fixture(new DockerCertificate(logger, appConfig, fileSystem), fileSystem);
    }
  }
}
