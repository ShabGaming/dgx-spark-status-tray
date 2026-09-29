# DGX Spark Status Tray

A small Windows system tray app that watches an [NVIDIA DGX Spark](https://www.nvidia.com/) on your local network and shows whether it's online or offline, right from the tray icon. It can also shut the Spark down for you over SSH, with a warning if it looks like something's still running on it.

I built this because I kept forgetting whether my Spark was powered on, and wanted a one-click way to shut it down without SSH-ing in and typing the command myself - with a safety check first, since I've accidentally shut it down mid-training-run more than once.

## Features

- **Live status in the tray icon** - green when the Spark is reachable, gray when it isn't, with a short debounce so one dropped packet doesn't flip the icon back and forth.
- **Shut down from the tray**, with a confirmation dialog that checks for active GPU work (`nvidia-smi`) and other running processes before you commit to it.
- **Start with Windows**, toggleable from Settings.
- **Auto-imports your SSH connection details from NVIDIA Sync** if you already have it set up, so there's nothing to configure for most people. You can also point it at a custom key/host manually.
- Small and quiet: a background timer and a couple of async network calls, nothing more. No telemetry, no background services.

## Installing

Grab the latest release from the [Releases page](../../releases). Two options are published for each version:

- **`SparkTray-win-x64.zip`** (recommended, ~15 MB) - requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to already be installed. Most recent Windows machines have some .NET runtime already, and dotnet.org will prompt you if you're missing this specific one when you try to run it.
- **`SparkTray-win-x64-standalone.zip`** (~150 MB)

Unzip, run `SparkTray.exe`, done. There's no installer - it's a single exe you can put wherever you like and delete whenever you like.

## Setting it up

1. Right-click the tray icon → **Settings…**
2. If you use [NVIDIA Sync](https://docs.nvidia.com/dgx/dgx-spark/nvidia-sync.html) to manage your Spark, click **Import from NVIDIA Sync** - this reads the SSH connection details Sync already set up (host, username, private key) and fills them in for you.
3. If you don't use Sync, or want to point at a different key, fill in the Hostname, SSH username, and private key file yourself. The `.local` hostname your Spark announces via mDNS works fine - this app resolves it directly rather than relying on Windows' name resolution, which in my experience doesn't reliably handle `.local` names.
4. Adjust the check interval, shutdown command, or the process ignore-list if you want - the defaults work for a stock DGX OS setup.

### A note on shutting down over SSH

The shutdown command (`sudo shutdown -h now` by default) needs `sudo` to not block on an interactive password prompt, since there's no TTY attached to a command run this way. By default, the app handles this for you: **"Ask for sudo password each time"** is on in Settings, so you get a small password prompt right in the shutdown confirmation dialog. The password is only ever held in memory for that one shutdown attempt - it's never written to `settings.json` or logged anywhere, and it travels over the already-encrypted SSH connection.

If you'd rather not be prompted at all, turn that setting off and set up passwordless sudo for just this command instead:

```
your-username ALL=(ALL) NOPASSWD: /usr/sbin/shutdown
```

(`sudo visudo -f /etc/sudoers.d/spark-tray` and add the line above, adjusted for your username.)

Either way, this only applies if your shutdown command actually starts with `sudo `; if you're using a custom wrapper script instead, you'll need the passwordless-sudo route (or your own script's own privilege handling) since there's nothing to prompt for otherwise.

## Icons

The application and tray status icons were generated using ChatGPT's image generation capabilities.

## Building from source

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Why the tray icon uses TCP instead of ping

I originally assumed a plain ICMP ping would work fine for the online/offline check, but Linux boxes (the Spark included) often have ICMP echo disabled by their firewall, which would make the app think it's offline when it's actually just not answering pings. Instead, the health check does a TCP connect to the SSH port, which is a much more reliable "is this thing actually up" signal since we need that port reachable anyway for the shutdown feature. A basic ICMP check still runs alongside it, purely as an extra data point, not as the actual online/offline signal.

## License

[MIT](LICENSE)
