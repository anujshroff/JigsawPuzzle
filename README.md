# Jigsaw Puzzle

A cross-platform jigsaw puzzle game (.NET 10 + Avalonia). Pick a folder of images —
the game cuts a random one into classic interlocking pieces. Drag pieces together;
matching neighbors snap and link, and linked groups drag as one. When every piece is
connected, the puzzle is solved and the next random image starts automatically.

## Play

- **Launch** → choose a folder; every `.jpg` / `.jpeg` / `.png` in it **and all its subfolders** joins the rotation
- Pieces start lined up in a snug tray on the left (~75% of the window), leaving working room on the right
- **Drag** pieces with the left mouse button; the moment matching neighbors get close enough they snap together
- **Difficulty** (Easy ~20 / Medium ~50 / Hard ~100 pieces) re-cuts the current image
- **Tidy** re-deals all still-loose pieces into a fresh grid, keeping your assembled clusters clear
- **New puzzle** jumps to another random image; **Folder…** switches folders

## Download

Grab the latest signed Windows executable from
[Releases](https://github.com/anujshroff/JigsawPuzzle/releases) — a single
self-contained `JigsawPuzzle-{version}-win-x64.exe`, Authenticode-signed via Azure
Trusted Signing. No .NET installation required; just run it.

Other platforms (Linux, macOS) are supported by the codebase but not published as
binaries — build from source below.

On startup the app checks the latest GitHub release (release builds only); when a
newer version exists, an "Update vX.Y.Z available" button appears in the toolbar
linking to the downloads page. The check is silent when offline.

## Run from source

```
dotnet run
```

Requires the .NET 10 SDK. Optionally pass an image folder to skip the picker:

```
dotnet run -- "C:\path\to\images"
```

## Build self-contained binaries

```
dotnet publish -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The single-file executable lands in `bin/Release/net10.0/<rid>/publish/`.

Notes:
- **Linux**: needs a desktop session (X11/Wayland); mark the file executable (`chmod +x`)
- **macOS**: unsigned binary — right-click → Open the first time (Gatekeeper)

## Releases & CI

- Every push and pull request against `main` or `release/*` runs
  [.github/workflows/ci.yml](.github/workflows/ci.yml) on `ubuntu-latest`: build,
  CodeQL analysis, and a vulnerable-NuGet dependency review.
- Pushing to a `release/#.#` branch runs
  [.github/workflows/build.yml](.github/workflows/build.yml) (gated by the
  `code-signing` environment): it versions the build from the branch name with an
  auto-incrementing patch (`release/1.2` → `v1.2.0`, `v1.2.1`, …), publishes the
  self-contained win-x64 executable, signs it with Azure Trusted Signing, and creates
  a GitHub Release with the exe attached.

## Credits

App icon: puzzle piece from [Google Noto Emoji](https://github.com/googlefonts/noto-emoji) (Apache-2.0).

## AI Notice

This project was entirely generated using AI, leveraging **Claude Code** by Anthropic. It serves as a testament to the capabilities of modern AI in automating complex development tasks and streamlining the software creation process.
