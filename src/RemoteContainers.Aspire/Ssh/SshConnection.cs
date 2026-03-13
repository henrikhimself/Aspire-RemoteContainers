// <copyright file="SshConnection.cs" company="Henrik Jensen">
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
using Renci.SshNet;

namespace Hj.RemoteContainers.Aspire.Ssh;

[ExcludeFromCodeCoverage(Justification = "External dependencies")]
internal sealed class SshConnection : ISshConnection
{
  private readonly AppConfiguration _appConfiguration;

  private List<PrivateKeyFile>? _keyFiles;
  private SshClient? _sshClient;

  private bool _disposedValue;

  public SshConnection(AppConfiguration appConfiguration) => _appConfiguration = appConfiguration;

  [MemberNotNullWhen(true, nameof(_sshClient))]
  public bool IsConnected => _sshClient?.IsConnected ?? false;

  public void Connect()
  {
    var keyDir = _appConfiguration.SshKeyPath;
    if (!Directory.Exists(keyDir))
    {
      throw new InvalidOperationException($"SSH key path '{keyDir}' does not exist.");
    }

    _keyFiles = LoadSshKeyFiles(keyDir);
    if (_keyFiles.Count == 0)
    {
      throw new InvalidOperationException("No SSH private keys found.");
    }

    _sshClient = new SshClient(_appConfiguration.SshHost, _appConfiguration.SshUser, [.. _keyFiles]);
    try
    {
      _sshClient.Connect();
    }
    catch
    {
      Dispose();
      throw;
    }
  }

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "See Dispose()")]
  public ISshForwardedPort ForwardPort(uint port)
  {
    if (!IsConnected)
    {
      throw new InvalidOperationException("SSH client is not connected");
    }

    ForwardedPortLocal? forwardedPort = null;
    try
    {
      forwardedPort = new ForwardedPortLocal("127.0.0.1", port, "127.0.0.1", port);
      _sshClient.AddForwardedPort(forwardedPort);
      forwardedPort.Start();
    }
    catch
    {
      forwardedPort?.Dispose();
      throw;
    }

    return new SshForwardedPort(forwardedPort);
  }

  private static List<PrivateKeyFile> LoadSshKeyFiles(string keyDir)
  {
    var keyPaths = new[]
    {
      Path.Combine(keyDir, "id_rsa"),
      Path.Combine(keyDir, "id_ed25519"),
      Path.Combine(keyDir, "id_ecdsa"),
    };

    var keys = new List<PrivateKeyFile>();
    foreach (var keyPath in keyPaths)
    {
      if (File.Exists(keyPath))
      {
        try
        {
          keys.Add(new PrivateKeyFile(keyPath));
        }
        catch
        {
          // Key might be encrypted or invalid, skip it
        }
      }
    }

    return keys;
  }

  private void Dispose(bool disposing)
  {
    if (!_disposedValue)
    {
      if (disposing)
      {
        _sshClient?.Dispose();

        if (_keyFiles is not null)
        {
          foreach (var keyFile in _keyFiles)
          {
            keyFile.Dispose();
          }
        }
      }

      _disposedValue = true;
    }
  }
}
