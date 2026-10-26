# Wallpaper App

A portable Windows app for your desktop wallpaper. There's no installer, no admin rights, and no console window.

The app draws your wallpaper in its own window behind the desktop icons, over the Windows wallpaper, instead of changing the Windows wallpaper setting. So it shows even where something else controls the Windows wallpaper, and your normal wallpaper comes back when the app exits. The wallpaper only shows while the app is running in the tray.

- **Picture:** one picture on every monitor, a different picture per monitor, or one picture stretched across all monitors.
- **Slideshow:** rotates through a folder of pictures, showing the same picture on every monitor.
- **Video:** plays an MP4 on a muted loop, on every monitor or stretched across all of them.
- **Next image:** skip to the next picture from the tray menu or a hotkey (Ctrl+Alt+N by default).
- **Start with Windows:** a checkbox that starts the app and shows your wallpaper when you sign in. It uses the per-user startup list, which you can also see in Task Manager > Startup.

## Using it
1. Put `WallpaperApp.exe` in its own folder, such as `Documents\WallpaperApp` or a USB stick.
2. Double-click it. The first time, Windows SmartScreen may say "Windows protected your PC" because the app isn't signed. Click **More info**, then **Run anyway**. No admin rights are needed.
3. Pick your pictures and click **Apply**. Closing the window keeps the app in the tray; right-click the tray icon to exit.

Settings and copies of your pictures are kept in `WallpaperApp Data` next to the exe, so you can move the folder anywhere. If that folder can't be written to, they go to `%APPDATA%\WallpaperApp` instead.

## Building
Requires the .NET 10 SDK.

```
dotnet publish -c Release -o publish
```

This produces a single self-contained `publish\WallpaperApp.exe` that runs without .NET installed.

## Code layout
- `Native/`: Windows API wrappers. `IDesktopWallpaper` lists the monitors and reads the Windows wallpaper for diagnostics; `DesktopHost` puts a window behind the desktop icons; `RegisterHotKey` handles the hotkey.
- `Core/`: settings, portable storage, the picture, slideshow and video modes (`IWallpaperMode`), the windows that draw them (`DesktopOverlay`, using WPF's `Image` and `MediaElement`), and the startup entry.
- `UI/`: the tray app, the settings window and the monitor preview.
