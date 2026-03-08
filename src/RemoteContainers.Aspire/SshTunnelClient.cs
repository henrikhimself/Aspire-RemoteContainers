// <copyright file="SshTunnelClient.cs" company="Henrik Jensen">
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

using Renci.SshNet;

namespace Hj.RemoteContainers.Aspire;

internal sealed class SshTunnelClient : IDisposable
{
  private readonly List<PrivateKeyFile> _keyFiles;

  private readonly SshClient _sshClient;

  private bool _disposedValue;

  private SshTunnelClient(List<PrivateKeyFile> keyFiles, SshClient sshClient)
  {
    _keyFiles = keyFiles;
    _sshClient = sshClient;
  }

  internal bool IsConnected => _sshClient.IsConnected;

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  internal static SshTunnelClient Connect(string keyDir, string sshHost, string sshUser)
  {
    if (!Directory.Exists(keyDir))
    {
      throw new InvalidOperationException($"SSH key path '{keyDir}' does not exist.");
    }

    var keyFiles = LoadSshKeyFiles(keyDir);
    if (keyFiles.Count == 0)
    {
      throw new InvalidOperationException("No SSH private keys found.");
    }

    var sshClient = ConnectSshClient(keyFiles, sshHost, sshUser);
    return new(keyFiles, sshClient);
  }

  internal bool TryForwardPort(uint port, out ForwardedPortLocal? forwardedPort)
  {
    forwardedPort = null;
    try
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
      forwardedPort = new ForwardedPortLocal("127.0.0.1", port, "127.0.0.1", port);
#pragma warning restore CA2000 // Dispose objects before losing scope
      _sshClient.AddForwardedPort(forwardedPort);
      forwardedPort.Start();
      return true;
    }
    catch (Exception)
    {
      forwardedPort?.Dispose();
      forwardedPort = null;
      return false;
    }
  }

  private static SshClient ConnectSshClient(List<PrivateKeyFile> keyFiles, string sshHost, string sshUser)
  {
    var sshClient = new SshClient(sshHost, sshUser, [.. keyFiles]);
    try
    {
      sshClient.Connect();
      return sshClient;
    }
    catch (Exception)
    {
      sshClient.Dispose();
      throw;
    }
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
        _sshClient.Disconnect();
        _sshClient.Dispose();

        foreach (var keyFile in _keyFiles)
        {
          keyFile.Dispose();
        }
      }

      _disposedValue = true;
    }
  }
}
