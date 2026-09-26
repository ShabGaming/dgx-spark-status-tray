# Setting up SSH access manually

If you don't use NVIDIA Sync, or want a dedicated key just for this app, here's how to set one up.

## 1. Generate a key (on Windows)

```
ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\spark-tray" -C "spark-tray"
```

Leave the passphrase empty - the tray app connects non-interactively, so a passphrase-protected key won't work with it.

## 2. Authorize it on the Spark

Copy the public key over (you'll need to type your password once for this):

```
type $env:USERPROFILE\.ssh\spark-tray.pub | ssh your-username@your-spark-hostname "cat >> ~/.ssh/authorized_keys"
```

## 3. Point the app at it

In the tray app's Settings, set:
- **Hostname**: your Spark's `.local` hostname or IP
- **SSH username**: the Linux username you used above
- **Private key file**: `C:\Users\<you>\.ssh\spark-tray`

## 4. Allow passwordless shutdown (optional, but needed for the shutdown feature)

```
sudo visudo -f /etc/sudoers.d/spark-tray
```

Add:

```
your-username ALL=(ALL) NOPASSWD: /usr/sbin/shutdown
```
