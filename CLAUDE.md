# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`edl-ng` is a .NET 9 cross-platform CLI for Qualcomm Emergency Download (EDL) mode. It speaks the Sahara (PBL) and Firehose (APSS) protocols over USB to flash, read, and manage Qualcomm-based devices.

## Build / Format / Publish

- Build: `dotnet build` (solution file is `edl-ng.slnx`). Output binary: `QCEDL.CLI/bin/<Config>/net9.0/<Platform>/edl-ng`.
- Format check (matches CI): `dotnet format --verify-no-changes --verbosity diagnostic`. CI fails on any formatting drift — run `dotnet format` before committing.
- Publish self-contained (matches CI): `dotnet publish QCEDL.CLI/QCEDL.CLI.csproj -c Release -r <rid> --self-contained true` where `<rid>` is `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.
- There is no test project; don't run `dotnet test`.
- `Directory.Build.props` applies to all projects: `TreatWarningsAsErrors=true`, nullable enabled, `AllowUnsafeBlocks=true`, `AnalysisMode=Recommended`, doc generation on. Warnings break the build — fix them, don't suppress.

## Solution layout

Four C# projects plus a Nix flake:

- `QCEDL.CLI/` — entry point. `Program.cs` wires `System.CommandLine` global options (loader, vid/pid, memory, slot, loglevel, maxpayload, hostdev-as-target, img-size, radxa-wos-platform) and registers one command per file in `Commands/`. Targets `AnyCPU`/`x64`/`ARM64` (protocol logic in `QCEDL.NET` is `AnyCPU`-only — keep platform-specific code in CLI).
- `QCEDL.NET/` — protocol + transport library. No CLI/UI dependencies.
- `QCEDL.Analyzer/` — Roslyn source generator (`EnumExtensionsGenerator.cs`) consumed at build time.
- `QCEDL.GUI/` + `QCEDL.GUI.Helper/` — in-progress GUI (Assets only on disk currently; work happens on `features/gui`).

## Architecture (the parts that need reading multiple files to grasp)

**Connection lifecycle is owned by `QCEDL.CLI/Core/EdlManager.cs`.** Every command goes through it:

1. `EdlManager` discovers the device (Windows COM port via `ComPortGuid`, or cross-platform via `LibUsbDotNet` matched by VID/PID — defaults `0x05C6` / `0x9008|0x900E`).
2. Opens `QualcommSerial` (`QCEDL.NET/Transport/`).
3. Runs the Sahara handshake via `QualcommSahara` (`QCEDL.NET/Layers/PBL/Sahara/`). The loader file passed via `--loader` can be a raw `.elf`/`.melf` programmer **or** a `qsahara_device_programmer.xml` — `SaharaConfigParser.cs` selects the right ELF for the detected chip (HWID / PK hash in `ChipInfo/`).
4. Switches to Firehose (`QualcommFirehose` in `QCEDL.NET/Layers/APSS/Firehose/`), configures memory type (UFS/eMMC/SPINOR/NVMe/…), reads storage geometry, then hands a `IStorageBackend` to the command.

**Direct-mode bypass:** `--hostdev-as-target <path>` (SPI NOR / file) and `--radxa-wos-platform` skip the USB/Sahara/Firehose stack entirely and use `HostDeviceManager` / `RadxaWoSDeviceManager` as the `IStorageBackend`. Commands should never assume USB is live — check `EdlManager.IsDirectMode` / `IsFirehoseMode` before calling protocol-specific helpers.

**Adding a new CLI command:** create `QCEDL.CLI/Commands/FooCommand.cs` exposing `public static Command Create(GlobalOptionsBinder)`, register it in `Program.cs`. Use `GlobalOptionsBinder` to read shared options; get a configured `EdlManager` from the binder rather than connecting directly.

**Firehose commands vs. XML layer:** `QualcommFirehoseCommands.cs` is the strongly-typed command surface; `Layers/APSS/Firehose/Xml/` contains the on-wire XML element types (serialized via `System.Xml`) and `JSON/StorageInfo/` parses the storage-info blob. `StorageType` enum lives under `Xml/Elements` — this is what `--memory` binds to.

**Partition table:** `QCEDL.NET/PartitionTable/GPT*.cs` is a standalone GPT parser used by `printgpt`, `read-part`, `write-part`, `dump-rawprogram`. It works off a `IStorageBackend`, so it's reused by both Firehose and direct-mode paths.

## Conventions worth knowing

- Target is `net9.0` everywhere; `ImplicitUsings` is on — don't add redundant `using System;` etc.
- Solution uses the newer `.slnx` format (edit with Rider/VS 2022 17.10+; `dotnet` handles it fine).
- Namespaces follow `Qualcomm.EmergencyDownload.*` in `QCEDL.NET` and `QCEDL.CLI.*` in the CLI — match existing neighbors.
- `IDE0058` / `IDE0130` / `CS1591` are downgraded to warnings intentionally; everything else is an error.

## Nix

`flake.nix` exposes `packages.<system>.edl-ng` (derivation in `pkgs/edl-ng/`) and a devShell. Use `nix build` for a reproducible build; the CI workflow `nix-build.yml` gates this on Linux only (macOS runners were removed — see commit `acb89a0`).
