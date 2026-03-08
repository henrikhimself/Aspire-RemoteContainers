// <copyright file="AppConfiguration.cs" company="Henrik Jensen">
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

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace Hj.RemoteContainers.Aspire;

internal sealed class AppConfiguration
{
  private readonly IConfiguration _configuration;
  private readonly string _environmentUserName;
  private readonly string _environmentUserProfilePath;
  private readonly IDictionary _environmentVariables;

  public AppConfiguration(IConfiguration configuration, string environmentUserName, string environmentUserProfilePath, IDictionary environmentVariables)
  {
    _configuration = configuration;
    _environmentUserName = environmentUserName;
    _environmentUserProfilePath = environmentUserProfilePath;
    _environmentVariables = environmentVariables;
  }

  public string SshKeyPath => Path.Combine(_environmentUserProfilePath, ".ssh");

  public string SshHost
  {
    get
    {
      var sshHost = _configuration["SSH_HOST"];
      return string.IsNullOrWhiteSpace(sshHost)
        ? (DockerHost?.Host ?? throw new InvalidOperationException("No SSH host found."))
        : sshHost;
    }
  }

  public string SshUser
  {
    get
    {
      var sshUser = _configuration["SSH_USER"];
      return string.IsNullOrWhiteSpace(sshUser)
        ? _environmentUserName
        : sshUser;
    }
  }

  [MemberNotNullWhen(true, nameof(DockerHost))]
  public bool HasDockerHost => DockerHost is not null;

  public UriBuilder? DockerHost
  {
    get
    {
      var dockerHost = (string?)_environmentVariables["DOCKER_HOST"];
      if (dockerHost?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ?? false)
      {
        var uriBuilder = new UriBuilder(dockerHost)
        {
          Scheme = IsDockerTls ? "https://" : "http://",
        };
        return uriBuilder;
      }

      return null;
    }
  }

  [MemberNotNullWhen(true, nameof(DockerCertPath))]
  public bool IsDockerTls =>
    string.Equals((string?)_environmentVariables["DOCKER_TLS_VERIFY"], "1", StringComparison.Ordinal)
    && !string.IsNullOrWhiteSpace(DockerCertPath);

  public string? DockerCertPath => (string?)_environmentVariables["DOCKER_CERT_PATH"];

  public bool TryGetDockerCertificate([NotNullWhen(true)] out X509Certificate2? caCert, [NotNullWhen(true)] out X509Certificate2? clientCert)
  {
    caCert = null;
    clientCert = null;

    if (!IsDockerTls)
    {
      return false;
    }

    var caCertPath = Path.Combine(DockerCertPath, "ca.pem");
    if (File.Exists(caCertPath))
    {
      caCert = X509Certificate2.CreateFromPem(File.ReadAllText(caCertPath));
    }

    var clientCertPath = Path.Combine(DockerCertPath, "cert.pem");
    var clientKeyPath = Path.Combine(DockerCertPath, "key.pem");
    if (File.Exists(clientCertPath) && File.Exists(clientKeyPath))
    {
      clientCert = X509Certificate2.CreateFromPem(File.ReadAllText(clientCertPath), File.ReadAllText(clientKeyPath));
    }

    return caCert is not null && clientCert is not null;
  }
}
