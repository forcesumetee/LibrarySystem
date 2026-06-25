# LibraryKiosk

Portrait (1080×1920) touchscreen kiosk for the school library. Read-only client of
the existing `LibraryApiServer` over HTTP + SignalR — it never writes to the server.
Replaces the legacy Android APK.

## Run

```
dotnet run --project admin-pc/LibraryKiosk/LibraryKiosk.csproj
```

The first run creates `%ProgramData%\LibrarySystem\kiosk-settings.json` with defaults.
Set the server address in **Settings → การเชื่อมต่อ → Base URL**, or pre-seed the file.

## Settings & PIN

Open Settings with the gear (top-right). It is protected by an admin **PIN**
(default `1234`; PBKDF2-HMAC-SHA256, 120 000 iterations). Five wrong attempts lock
entry for 60 seconds; the lock survives an app restart.

Settings cover: server URL + resync, display name, hiding the logo/background on this
device, UI scale (80–120 %), display mode, change PIN, **auto-start**, and **exit**.

State lives in `%ProgramData%\LibrarySystem\kiosk-settings.json`; logs in
`%ProgramData%\LibrarySystem\logs\kiosk.log`. No secrets are stored in the repo.

## Display mode & lockdown

- **เต็มจอ (Kiosk)** — borderless, maximised, always-on-top. Alt+F4 and other close
  shortcuts are blocked; the **only** way out is *Settings → ระบบ → ปิดโปรแกรม*
  (i.e. behind the PIN). Idle reset is active.
- **หน้าต่าง (ทดสอบ)** — normal resizable window, freely closable, no idle reset.
  Use this during development so you never lock yourself out.

The mode is persisted and re-applied on startup.

### Idle reset

In fullscreen, after `IdleResetSeconds` (default 180; `0` disables) with no input the
browse view resets — closes the detail/settings overlays, clears the search and
category, and scrolls back to the top — ready for the next person.

## Auto-start (app-level, optional)

The **เริ่มอัตโนมัติเมื่อเปิดเครื่อง** toggle writes a per-user Run key:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LibraryKiosk = "<path>\LibraryKiosk.exe"
```

This launches the app after the user signs in (no admin rights needed). Turning it
off removes the value.

## OS-level kiosk lockdown (configure on the device, not in code)

For a true single-purpose terminal, configure Windows on the actual machine — this is
deliberately **not** done in code:

- **Assigned Access (single-app kiosk):** Settings → Accounts → *Other users* → *Set
  up a kiosk*, or PowerShell:
  ```powershell
  Set-AssignedAccess -AppName "LibraryKiosk" -UserName "kioskuser"
  ```
  Creates a locked-down local account that boots straight into the app; Windows blocks
  Alt+Tab, Win, Ctrl+Alt+Del menus, etc.
- **Shell Launcher (replace explorer.exe):** enable the *Shell Launcher* optional
  feature and point the shell at `LibraryKiosk.exe` for the kiosk account, with a
  restart-on-exit action.
- Disable Windows Update auto-reboots during open hours, and pin power settings so the
  screen never sleeps.

The in-app fullscreen lockdown above is the application-level layer; Assigned Access /
Shell Launcher is the OS layer. Use both for an unattended public kiosk.
