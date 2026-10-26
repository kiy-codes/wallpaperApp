# Wallpaper App

A portable Windows app for setting the desktop wallpaper. There's no installer, no admin rights, and no console window.

- **Picture:** one picture on every monitor, a different picture per monitor, or one picture stretched across all monitors.
- **Slideshow:** rotates through a folder of pictures. Windows runs the slideshow itself, so it keeps going after the app exits.
- **Video:** plays an MP4 on a muted loop behind the desktop icons, on every monitor or stretched across all of them. Unlike pictures, the video only plays while the app is running in the tray.
- **Next image:** skip to the next picture from the tray menu or a hotkey (Ctrl+Alt+N by default).
- **Start with Windows:** a checkbox that re-applies your wallpaper when you sign in. It uses the per-user startup list, which you can also see in Task Manager > Startup.

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
- `Native/`: Windows API wrappers. `IDesktopWallpaper` sets the wallpaper and slideshow; `DesktopHost` puts the video window behind the desktop icons; `RegisterHotKey` handles the hotkey.
- `Core/`: settings, portable storage, the picture, slideshow and video modes (`IWallpaperMode`), the video player (`VideoWallpaper`, using WPF's `MediaElement`), and the startup entry.
- `UI/`: the tray app, the settings window and the monitor preview.
