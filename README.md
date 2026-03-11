# Aspire Remote Containers

Use Aspire with a remote Docker or Podman engine via automatic SSH port forwarding.

## The Problem

When Aspire runs container resources, its orchestrator (DCP) allocates ports on the Docker host. If Docker runs on a **remote machine** (pointed to by `DOCKER_HOST=tcp://…`), those ports are only reachable on the remote host - not on `localhost` where your Aspire AppHost is running. This means the Aspire dashboard, service discovery, and any local tooling cannot reach the containers.

## How It Works

This extension detects that `DOCKER_HOST` points to a remote `tcp://` address and **automatically creates SSH tunnels** for every container resource:

1. You call `builder.AddSshTunneling()` in your AppHost.
2. When Aspire allocates endpoints for a container resource, the extension queries the **Docker REST API** on the remote host to discover the published port mappings.
3. For each discovered port, an **SSH port forward** (`localhost:port → remote:port`) is created through an SSH connection to the Docker host.
4. When the AppHost shuts down, all tunnels and the SSH connection are cleaned up automatically.


## Getting Started

### Install the NuGet Package

```shell
dotnet add package HenrikJensen.RemoteContainers.Aspire
```

### Usage

Add a single line to your Aspire AppHost:

```csharp
using Hj.RemoteContainers.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddSshTunneling();

// ...

await builder.Build().RunAsync();
```

No changes are needed to your container resource definitions - the tunneling is fully transparent once enabled.

## Configuration

### Required Environment Variables

Add this in your Aspire AppHost `launchSettings.json`:

| Variable | Description |
|---|---|
| `DOCKER_HOST` | Remote Docker engine address, e.g. `tcp://my-server:2376`. Must use the `tcp://` scheme. |

### Optional Environment Variables (TLS)

Set these if your remote Docker endpoint requires TLS:

| Variable | Description |
|---|---|
| `DOCKER_TLS_VERIFY` | Set to `1` to enable TLS when connecting to `DOCKER_HOST`. |
| `DOCKER_CERT_PATH` | Path to a directory containing `ca.pem`, `cert.pem`, and `key.pem` for mutual TLS authentication with the Docker API. |

Example `launchSettings.json` with TLS enabled:

```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "environmentVariables": {
        "DOCKER_HOST": "tcp://my-server:2376",
        "DOCKER_TLS_VERIFY": "1",
        "DOCKER_CERT_PATH": "/path/to/certs/client"
      }
    }
  }
}
```

### Optional Settings

These can be set via any .NET configuration source (environment variables, `appsettings.json`, etc.):

| Key | Default | Description |
|---|---|---|
| `RemoteContainers:SshHost` | Hostname from `DOCKER_HOST` | Override the SSH connection target if it differs from the Docker API host. |
| `RemoteContainers:SshUser` | Current OS username | Override the SSH username. |
| `RemoteContainers:ContainerStartTimeout` | `00:05:00` | How long to wait for a container's ports to become available before giving up. Expressed as a `TimeSpan` string (e.g. `00:05:00`). |
| `RemoteContainers:ContainerPollInterval` | `00:00:05` | How often to poll the Docker API while waiting for a container's ports. Expressed as a `TimeSpan` string (e.g. `00:00:05`). |

### SSH Key Authentication

The extension automatically loads private keys from `~/.ssh/` and tries the following files in order:

- `id_rsa`
- `id_ed25519`
- `id_ecdsa`

At least one unencrypted key must be present and authorized on the remote host.

Only key-based authentication (authorized_keys) is supported!

## When Tunneling Is Inactive

If `DOCKER_HOST` is not set or does not start with `tcp://`, the extension does nothing - `AddSshTunneling()` is safe to call unconditionally and will silently no-op when Docker is local.

## Prerequisites

- .NET 10 SDK (LTS)
- Aspire
- A remote Docker or Podman engine exposed over TCP with TLS
- SSH access to the remote host with key-based authentication
