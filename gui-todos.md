# QCEDL.GUI — Implementation Tracker

Living audit + phased plan for the Avalonia 12 + ReactiveUI GUI front-end.
Status legend: **Todo** / **In Progress** / **Done** / **Blocked** / **Deferred**.

---

## 1. CLI Feature Inventory (source of truth for scope)

Derived from `QCEDL.CLI/Program.cs` (globals) and `QCEDL.CLI/Commands/*`.

### Global options (bound by `GlobalOptionsBinder`)

| Option | CLI flag | Notes |
|---|---|---|
| Loader path | `--loader` / `-l` | Firehose programmer (`.elf` / `.melf` / `qsahara_device_programmer.xml`). |
| USB VID | `--vid` | Hex, defaults to `0x05C6`. |
| USB PID | `--pid` | Hex, defaults to `0x9008` or `0x900E`. |
| Memory type | `--memory` | `StorageType` enum (UFS, eMMC/Sdcc, SPINOR, NVMe, NAND). |
| Log level | `--loglevel` | Trace/Debug/Info/Warning/Error. |
| Max payload | `--maxpayload` | Firehose `configure` max payload bytes. |
| Slot | `--slot` / `-s` | 0 or 1 (eMMC / sdcard, etc.). |
| Host device target | `--hostdev-as-target` | Bypasses USB; reads/writes SPI NOR / file directly. |
| Image size | `--img-size` | Used with `--hostdev-as-target` file path (e.g. `32M`, `1G`). |
| Radxa WoS | `--radxa-wos-platform` | Windows-only SPI NOR backend. |

### Commands

| Command | Summary |
|---|---|
| `upload-loader` | Sahara handshake + programmer upload only. |
| `reset` | Firehose reset / power-off (`--mode`, `--delay`). |
| `printgpt` | Parse + print GPT; per-LUN or scan all (`--lun`). |
| `read-part` | Read a named partition to a file. |
| `read-sector` | Read N sectors from `--lun` + start LBA to a file. |
| `read-lun` | Read the whole LUN to a file. |
| `write-part` | Write a file to a named partition. |
| `write-sector` | Write a file to sectors at start LBA. |
| `erase-part` | Erase a named partition. |
| `erase-sector` | Erase N sectors at start LBA. |
| `dump-rawprogram` | Dump all partitions of a LUN + generate `rawprogram` XML (`--gen-xml-only`, `--skip`). |
| `rawprogram` | Execute `rawprogramN.xml` + `patchN.xml`. |
| `provision` | UFS provisioning via XML. |

### Orchestration layer

`EdlManager` owns: device discovery (Windows / Linux LibUsb / Linux TTY fallback), mode
probing (Sahara / SaharaMemoryDebug / Firehose), Sahara handshake + programmer upload,
Firehose configure, and dispatch to one of three `IStorageBackend` implementations:
`FirehoseStorageBackend`, `HostStorageBackend`, `RadxaWoSBackend`.

---

## 2. CLI → GUI Mapping

Single-window shell with left-side nav. Screens:

| View | Covers |
|---|---|
| **Overview** | App intro, detected device snapshot, connection status, quick actions. |
| **Connection** | Target mode (USB / Host-dev / Radxa WoS), VID/PID, memory type, loader file picker, slot, max payload, image size, connect / upload-loader / reset. |
| **Partitions** | `printgpt` listing, `read-part`, `write-part`, `erase-part`. Confirmation UX for writes/erases. |
| **Sectors** | `read-sector` / `read-lun`, `write-sector`, `erase-sector`. |
| **RawProgram** | `rawprogram` execute, `dump-rawprogram` export, `--gen-xml-only`, `--skip`. |
| **Advanced** | `provision`, `upload-loader`, `reset` mode+delay. |
| **Logs** | Live log stream from shared `Logging` sink, copy/save. |
| **Settings/About** | Log level, theme, version. |

Shared UX: progress rows with cancel (where the underlying op supports it), file pickers,
destructive-action confirmation dialog with target summary (LUN/partition/LBA range).

---

## 3. Refactor Notes

- `EdlManager` currently takes `GlobalOptionsBinder` (couples to `System.CommandLine`).
  → Extract a plain POCO `EdlOptions` used by both CLI and GUI; make `EdlManager` public.
- `Logging` is `internal static` + writes to `Console`.
  → Introduce a `ILogSink` abstraction; CLI keeps console sink, GUI adds an observable sink
  feeding the Logs view.
- Types the GUI must touch directly (`DeviceMode`, `StorageGeometry`, `IStorageBackend`,
  `EdlManager`, `EdlOptions`) need to become `public`.
- GUI references the `QCEDL.CLI` assembly as a library (Exe projects can still be
  referenced — we only consume public types). This avoids duplicating orchestration logic.

---

## 4. Phased Rollout

**Phase 0 — Foundations** *(this deliverable)*
- [Done] CLI audit document (this file).
- [Done] Refactor: `EdlOptions` POCO + public surface on `EdlManager`, `Logging`, `StorageGeometry`, `DeviceMode`.
- [Done] `QCEDL.GUI` project: Avalonia 12 + ReactiveUI.Avalonia, registered in `edl-ng.slnx`.
- [Done] Custom theme/token system derived from `DESIGN.md` (no external theme packs).
- [Done] Single-window shell + left-nav + placeholders for all sections.
- [Done] Observable log sink wired into Logs view.

**Phase 1 — Connect & Inspect**
- [Done] Connection view: options form, connect action, target/loader pickers, mode+storage readout.
- [Done] Overview view: live connection summary card.
- [Done] Partitions view: `printgpt` scan + table rendering.
- [Todo] `read-part` with file save picker + progress.

**Phase 2 — Read / Write / Erase**
- [Todo] `read-part`, `read-sector`, `read-lun` end-to-end with cancellable progress.
- [Todo] `write-part`, `write-sector` with file pickers + confirmation.
- [Todo] `erase-part`, `erase-sector` with double-confirmation for destructive ranges.

**Phase 3 — RawProgram & Advanced**
- [Todo] `rawprogram` execute (multi-file selection, progress aggregation).
- [Todo] `dump-rawprogram` export (output dir picker, `--gen-xml-only`, `--skip`).
- [Todo] `provision` XML runner.
- [Todo] `upload-loader` standalone action.
- [Todo] `reset` with mode + delay.

**Phase 4 — Polish**
- [Todo] Keyboard shortcuts, focus visuals audit, contrast audit.
- [Todo] Settings persistence (log level, last loader, last VID/PID).
- [Todo] About pane + version.
- [Deferred] Multi-device selection UX (CLI currently picks the first).
- [Deferred] Streaming probe / emergency-flash 9006 handling.

---

## 5. Implementation Status (per capability)

| Capability | Phase | Status | Notes |
|---|---|---|---|
| Custom theme tokens + styles | 0 | Done | `Themes/Tokens.axaml`, `Themes/Styles.axaml`. Parchment bg, terracotta CTA, ring shadows, serif headings. |
| Single-window shell | 0 | Done | `MainWindow.axaml` + `ShellViewModel` with section enum. |
| Log sink abstraction | 0 | Done | `ILogSink` in `QCEDL.NET.Logging` via existing `LibraryLogger`; GUI `ObservableLogSink`. |
| `EdlOptions` POCO | 0 | Done | `QCEDL.CLI/Core/EdlOptions.cs`; `GlobalOptionsBinder` projects onto it. |
| Device discovery / mode probe | 1 | Done (reused) | `EdlManager.DetectCurrentModeAsync` called from `EdlService`. |
| Connection view | 1 | Done | Loader/VID/PID/memory/slot/host-dev/radxa options; Connect + Probe actions. |
| Overview | 1 | Done | Connection summary + last-action row. |
| `printgpt` | 1 | Done | LUN scanner + partition table. |
| `read-part` | 2 | Todo | Needs save-file picker + progress binding. |
| `write-part` | 2 | Todo | Confirmation dialog w/ target summary. |
| `erase-part` | 2 | Todo | Double-confirm for destructive. |
| `read-sector` / `read-lun` | 2 | Todo | |
| `write-sector` | 2 | Todo | |
| `erase-sector` | 2 | Todo | |
| `rawprogram` | 3 | Todo | |
| `dump-rawprogram` | 3 | Todo | |
| `provision` | 3 | Todo | |
| `upload-loader` | 3 | Todo | (Reused via `EdlManager.UploadLoaderViaSaharaAsync`.) |
| `reset` | 3 | Todo | |
| Destructive-action confirmation dialog | 2 | Todo | Shared `ConfirmDialog` control. |
| Cancellable progress | 2 | Todo | Depends on underlying op support. |
| Settings persistence | 4 | Todo | |

---

## 6. Known Constraints / Open Items

- CLI's `Logging` still owns the console sink; GUI uses the same shared sink so logs
  appear both in the Logs view and console when the GUI is run from a terminal.
- `EdlManager` dispatches several blocking methods on `Task.Run`; progress for some paths
  (Sahara upload) is not granular — surface the log stream as the primary signal.
- Cross-platform USB detection requires LibUsb to be installed on Linux/macOS; GUI should
  surface a clear error rather than silently failing.
- Multi-device selection is not yet modelled in the CLI; the GUI currently mirrors
  "first-match wins" and logs a warning.
- GUI does not shell out to `edl-ng` — it links against `QCEDL.CLI` as a library and
  reuses the public orchestration surface directly.

---

## 7. Update Log

- **2026-04-19** — Initial audit + Phase 0 + Phase 1 (Overview, Connection, Partitions
  `printgpt`) implemented. Remaining work tracked in Section 5.
