// <copyright file="DockerCertificate.cs" company="Henrik Jensen">
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

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire.Docker;

internal sealed class DockerCertificate : IDisposable
{
  private readonly ILogger<DockerCertificate> _logger;
  private readonly AppConfiguration _appConfiguration;
  private readonly IFileSystem _fileSystem;

  private X509Certificate2? _caCert;
  private X509Certificate2? _clientCert;
  private bool _disposedValue;

  public DockerCertificate(ILogger<DockerCertificate> logger, AppConfiguration appConfiguration, IFileSystem fileSystem)
  {
    _logger = logger;
    _appConfiguration = appConfiguration;
    _fileSystem = fileSystem;
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by caller")]
  public bool TryGetCertificate([NotNullWhen(true)] out X509Certificate2? caCert, [NotNullWhen(true)] out X509Certificate2? clientCert)
  {
    caCert = null;
    clientCert = null;

    if (!_appConfiguration.IsDockerTls)
    {
      return false;
    }

    var dockerCertPath = _appConfiguration.DockerCertPath;

    if (_caCert is null
      && !TryCreateCertificate(Path.Combine(dockerCertPath, "ca.pem"), null, out _caCert))
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError("Failed to get CA");
      }

      return false;
    }

    if (_clientCert is null
      && !TryCreateCertificate(Path.Combine(dockerCertPath, "cert.pem"), Path.Combine(dockerCertPath, "key.pem"), out _clientCert))
    {
      if (_logger.IsEnabled(LogLevel.Error))
      {
        _logger.LogError("Failed to get client certificate");
      }

      _caCert.Dispose();
      _caCert = null;
      return false;
    }

    caCert = Clone(_caCert);
    clientCert = Clone(_clientCert);
    return true;
  }

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  private static X509Certificate2 Clone(X509Certificate2 cert)
  {
    var keyStorageFlags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet;
    var pfxBytes = cert.Export(X509ContentType.Pkcs12, string.Empty);
    return X509CertificateLoader.LoadPkcs12(pfxBytes, string.Empty, keyStorageFlags);
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by caller")]
  private bool TryCreateCertificate(string certPath, string? keyPath, [NotNullWhen(true)] out X509Certificate2? cert)
  {
    var certPem = _fileSystem.FileExists(certPath) ? _fileSystem.ReadAllText(certPath) : null;
    var keyPem = keyPath is not null && _fileSystem.FileExists(keyPath) ? _fileSystem.ReadAllText(keyPath) : null;

    if (keyPem is null)
    {
      cert = string.IsNullOrWhiteSpace(certPem) ? null : X509Certificate2.CreateFromPem(certPem);
      return cert is not null;
    }

    cert = string.IsNullOrWhiteSpace(certPem) ? null : X509Certificate2.CreateFromPem(certPem, keyPem);
    return cert is not null;
  }

  private void Dispose(bool disposing)
  {
    if (!_disposedValue)
    {
      if (disposing)
      {
        _caCert?.Dispose();
        _clientCert?.Dispose();
      }

      _disposedValue = true;
    }
  }
}
