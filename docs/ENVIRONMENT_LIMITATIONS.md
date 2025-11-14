# Environment Limitations

The automated environment currently blocks outbound HTTP(S) traffic to several Microsoft and Ubuntu package feeds. Attempts to install the .NET SDK using either the bundled `dotnet-install.sh` helper script or the official Microsoft APT repositories result in `403 Forbidden` responses from the proxy, preventing acquisition of the SDK artifacts.

## Observed Failures

- `dotnet-install.sh --channel 8.0` repeatedly fails with `403 Unable to download https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0/latest.version`.
- `apt-get update` against the Microsoft and Ubuntu repositories fails with `403 Forbidden` responses from the proxy, preventing package index refresh.

These restrictions currently block local execution of `dotnet build SqlDiagnostics.sln` inside the container.
