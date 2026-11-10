# CDS TapeLab for macOS (and Linux)

Avalonia (C#) application with PortAudio (CoreAudio on macOS, ALSA on Linux). The measurement core (`Core`) is shared with the Windows edition: the project links the files from `../windows/CDSMeasure/Core`.

**Status: alpha.** What works today: start screen, sound card settings with an input test, the True RMS voltmeter. Wow & flutter, playback and recording frequency response, and head azimuth (with Lissajous) are being ported.

## Build

```sh
dotnet run --project CDSTapeLab.csproj              # run
packaging/make-app.sh osx-arm64 0.7.0 apple-silicon # .app + .dmg (macOS), or osx-x64 for Intel
```

GitHub Actions builds the `.dmg` files for Apple Silicon and Intel: see `.github/workflows/build.yml`.

Requires .NET 8 SDK. The app targets macOS 12 (Monterey) and newer.
