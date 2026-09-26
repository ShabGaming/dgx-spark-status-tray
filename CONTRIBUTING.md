# Contributing

Thanks for taking a look at this. It's a small project so the bar for contributing is low - bug fixes, small features, and docs improvements are all welcome. For anything bigger, open an issue first so we're not both working on the same thing.

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build
dotnet test
```

To run it locally:

```
dotnet run --project src/SparkTray
```

To produce the same release artifacts CI does:

```
dotnet publish src/SparkTray -c Release -r win-x64 --self-contained false
dotnet publish src/SparkTray -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true
```

## Project layout

- `src/SparkTray/App` - the tray icon shell, autostart, single-instance guard
- `src/SparkTray/Monitoring` - the online/offline health check, mDNS resolution, TCP probe
- `src/SparkTray/Ssh` - SSH command execution, the GPU/process pre-shutdown checks
- `src/SparkTray/NvidiaSyncImport` - the best-effort import of NVIDIA Sync's SSH config
- `src/SparkTray/Config` - settings model and JSON persistence
- `src/SparkTray/UI` - the settings and shutdown-confirmation dialogs
- `src/SparkTray.Tests` - unit tests for the parts of this that don't need a live network or a real Spark

## Things worth knowing before you dive in

- **This app cannot use Native AOT.** WinForms and trimming don't mix - the .NET SDK hard-blocks it (`NETSDK1175`) because WinForms relies on COM marshalling that the trimmer can't safely analyze. This isn't a "not set up yet," it's a real constraint - don't spend time trying to re-enable `PublishAot` for this project.
- **Windows doesn't reliably resolve `.local` mDNS hostnames on its own.** `MdnsResolver` exists because I tested this directly - `ping`, `Resolve-DnsName`, and plain socket connects all failed to resolve a real Spark's `.local` hostname on my network, even though a raw mDNS query got answered fine. If you're touching hostname resolution, don't reroute it through `Dns.GetHostAddresses` for `.local` names.
- **The SSH connection is used for three things**: the pre-shutdown GPU/process check, and the shutdown command itself. `ISshCommandRunner` is the seam if you ever need to swap the transport (there's a `SshExeCommandRunner` that shells out to the OS's `ssh.exe` sitting there unused, kept as a fallback in case SSH.NET ever becomes a problem).
- Settings are plain JSON at `%AppData%\SparkTray\settings.json` and deliberately hand-editable - if you add a field, keep it something a person could type into a text editor without a schema doc.

## Code style

Follow `.editorconfig`. Nothing exotic - four-space indents, braces on new lines, `var` where the type is obvious.
