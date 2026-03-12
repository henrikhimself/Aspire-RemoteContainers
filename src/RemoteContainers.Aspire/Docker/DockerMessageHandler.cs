// <copyright file="DockerMessageHandler.cs" company="Henrik Jensen">
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

using System.Security.Cryptography.X509Certificates;

namespace Hj.RemoteContainers.Aspire.Docker;

internal sealed class DockerMessageHandler : HttpClientHandler, IDisposable
{
  private readonly DockerCertificate _dockerCertificate;

  private X509Certificate2? _caCert;
  private X509Certificate2? _clientCert;

  private volatile bool _disposed;

  public DockerMessageHandler(DockerCertificate dockerCertificate) => _dockerCertificate = dockerCertificate;

  public HttpMessageHandler Init()
  {
    if (_dockerCertificate.TryGetCertificate(out _caCert, out _clientCert))
    {
      ClientCertificates.Add(_clientCert);
      ServerCertificateCustomValidationCallback = (_, serverCert, chain, _) =>
      {
        if (serverCert is null || chain is null)
        {
          return false;
        }

        chain.ChainPolicy.CustomTrustStore.Add(_caCert);
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        return chain.Build(serverCert);
      };
    }

    return this;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing && !_disposed)
    {
      _disposed = true;
      _caCert?.Dispose();
      _clientCert?.Dispose();
    }

    base.Dispose(disposing);
  }
}
