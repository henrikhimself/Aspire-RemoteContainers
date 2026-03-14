using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hj.RemoteContainers.Aspire.Docker;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class DockerMessageHandlerTests
{
  [Fact]
  public void Init_WhenTlsNotEnabled_ReturnsHandlerWithNoClientCertificates()
  {
    // Arrange
    var f = Fixture.Create(withCertificate: false);

    // Act
    f.Sut.Init();

    // Assert
    Assert.Empty(f.Sut.ClientCertificates);
  }

  [Fact]
  public void Init_WhenTlsNotEnabled_ReturnsHandlerWithNoValidationCallback()
  {
    // Arrange
    var f = Fixture.Create(withCertificate: false);

    // Act
    f.Sut.Init();

    // Assert
    Assert.Null(f.Sut.ServerCertificateCustomValidationCallback);
  }

  [Fact]
  public void Init_WhenCertificateConfigured_AddsClientCertificate()
  {
    // Arrange
    using var caCert = GenerateTestCertificate("CN=ca");
    using var clientCert = GenerateTestCertificate("CN=client");
    var f = Fixture.Create(withCertificate: true, caCert, clientCert);

    // Act
    f.Sut.Init();

    // Assert
    Assert.Single(f.Sut.ClientCertificates);
  }

  [Fact]
  public void Init_WhenCertificateConfigured_SetsServerCertificateCustomValidationCallback()
  {
    // Arrange
    using var caCert = GenerateTestCertificate("CN=ca");
    using var clientCert = GenerateTestCertificate("CN=client");
    var f = Fixture.Create(withCertificate: true, caCert, clientCert);

    // Act
    f.Sut.Init();

    // Assert
    Assert.NotNull(f.Sut.ServerCertificateCustomValidationCallback);
  }

  [Fact]
  public void Init_ReturnsThisInstance()
  {
    // Arrange
    var f = Fixture.Create(withCertificate: false);

    // Act
    var result = f.Sut.Init();

    // Assert
    Assert.Same(f.Sut, result);
  }

  [Fact]
  public void ServerCertificateValidation_WhenServerCertIsNull_ReturnsFalse()
  {
    // Arrange
    using var caCert = GenerateTestCertificate("CN=ca");
    using var clientCert = GenerateTestCertificate("CN=client");
    var f = Fixture.Create(withCertificate: true, caCert, clientCert);
    f.Sut.Init();
    var callback = f.Sut.ServerCertificateCustomValidationCallback!;
    using var chain = new X509Chain();

    // Act
    var result = callback(null!, null, chain, SslPolicyErrors.None);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void ServerCertificateValidation_WhenChainIsNull_ReturnsFalse()
  {
    // Arrange
    using var caCert = GenerateTestCertificate("CN=ca");
    using var clientCert = GenerateTestCertificate("CN=client");
    var f = Fixture.Create(withCertificate: true, caCert, clientCert);
    f.Sut.Init();
    var callback = f.Sut.ServerCertificateCustomValidationCallback!;

    // Act
    var result = callback(null!, caCert, null, SslPolicyErrors.None);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void ServerCertificateValidation_WhenChainBuildSucceeds_ReturnsTrue()
  {
    // Arrange — use the same self-signed cert as both CA and server cert
    using var cert = GenerateTestCertificate("CN=server");
    var f = Fixture.Create(withCertificate: true, cert, cert);
    f.Sut.Init();
    var callback = f.Sut.ServerCertificateCustomValidationCallback!;
    using var serverCert = X509Certificate2.CreateFromPem(cert.ExportCertificatePem());
    using var chain = new X509Chain();

    // Act
    var result = callback(null!, serverCert, chain, SslPolicyErrors.None);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public void Dispose_WhenCalledTwice_DoesNotThrow()
  {
    // Arrange
    var f = Fixture.Create(withCertificate: false);

    // Act & Assert
    var exception = Record.Exception(() =>
    {
      f.Sut.Dispose();
      f.Sut.Dispose();
    });
    Assert.Null(exception);
  }

  private static X509Certificate2 GenerateTestCertificate(string subject)
  {
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
    return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
  }

  private sealed record Fixture(DockerMessageHandler Sut, IDockerCertificate Certificate)
  {
    public static Fixture Create(bool withCertificate, X509Certificate2? caCert = null, X509Certificate2? clientCert = null)
    {
      var certificate = Substitute.For<IDockerCertificate>();

      if (withCertificate && caCert is not null && clientCert is not null)
      {
        var capturedCaCert = caCert;
        var capturedClientCert = clientCert;

        certificate
          .TryGetCertificate(out Arg.Any<X509Certificate2?>(), out Arg.Any<X509Certificate2?>())
          .Returns(x =>
          {
            x[0] = capturedCaCert;
            x[1] = capturedClientCert;
            return true;
          });
      }
      else
      {
        certificate
          .TryGetCertificate(out Arg.Any<X509Certificate2?>(), out Arg.Any<X509Certificate2?>())
          .Returns(false);
      }

      return new Fixture(new DockerMessageHandler(certificate), certificate);
    }
  }
}
