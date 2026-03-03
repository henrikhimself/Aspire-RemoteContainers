# Agent Instructions

Instructions for GitHub Copilot and other AI coding agents working with the Aspire repository.

## Repository Overview
**RemoteContainers.Aspire** contains an extension that creates SSH tunnels for container resources if a DOCKER_HOST is configured in launch settings. It allows using a remote container runtime such as Docker Engine with Aspire.

### Technology Stack
- .NET 10.0 (LTS)
- Aspire
- C#
- Multi-platform support (Windows, Linux, macOS, containers)

## General

* Make only high confidence suggestions when reviewing code changes.
* Always use the version of C# that matches the latest .NET LTS.
* Always use the latest released (stable) version of Aspire.
* Never change global.json unless explicitly asked to.
* Never change package.json or package-lock.json files unless explicitly asked to.
* Never change NuGet.config files unless explicitly asked to.

## Formatting

* Apply code-formatting style defined in `.editorconfig` and `.editorconfig` files in nested directories.
* Prefer file-scoped namespace declarations and single-line using directives.
* Insert a newline before the opening curly brace of any code block (e.g., after `if`, `for`, `while`, `foreach`, `using`, `try`, etc.).
* Ensure that the final return statement of a method is on its own line.
* Use pattern matching and switch expressions wherever possible.
* Use `nameof` instead of string literals when referring to member names.
* Place private class declarations at the bottom of the file.

### Nullable Reference Types

* Declare variables non-nullable, and check for `null` at entry points.
* Always use `is null` or `is not null` instead of `== null` or `!= null`.
* Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

## Project Layout and Architecture

### Directory Structure
- **`/src`**: Main source code for the Remote Containers Aspire extension
- **`/exampels`**: Source code for an example App Host that uses the Containers Aspire extension

### Key Configuration Files
- **`global.json`**: Pins .NET SDK version - never modify without explicit request
- **`.editorconfig`**: Code formatting rules, null annotations, diagnostic configurations
- **`Directory.Build.props`**: Shared MSBuild properties across all projects
- **`Directory.Build.targets`**: Shared project configuration
- **`Directory.Packages.props`**: Centralized package version management
- **`RemoteContainers.slnx`**: Main solution file (XML-based solution format)

### Dependencies and Hidden Requirements
- **Local .NET SDK**: Automatically uses local SDK when available after running restore due to paths configuration in global.json
- **Package References**: Centrally managed via Directory.Packages.props

## Markdown files

* Markdown files should not have multiple consecutive blank lines.
* Code blocks should be formatted with triple backticks (```) and include the language identifier for syntax highlighting.
* JSON code blocks should be indented properly.

## Available Skills

The following specialized skills are available in `.github/skills/`:

- **aspire**: Aspire skill covering the Aspire CLI, AppHost orchestration, service discovery, integrations, MCP server, VS Code extension, Dev Containers, templates, dashboard, and deployment

## Trust These Instructions

These instructions are comprehensive and tested. Only search for additional information if:
1. The instructions appear outdated or incorrect
2. You encounter specific errors not covered here
3. You need details about new features not yet documented
