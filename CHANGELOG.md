# Changelog

## 0.9.0-beta - 2026-11-05: first public release

First public release. Core assembly and ten modules.

- Core: module loader (`modules/*.dll`), public API level 9, key=value config with live reload and delayed save, log file, patch ownership checks and guards, built-in fallback panel.
- NailUI: settings panel (F10, title button, day screen button) with 179 options, tooltips, search, restart notices, English and Russian texts; display settings; optional FPS counter.
- PerfPack: about 60 engine optimizations covering rendering and off-screen culling, animator lookups, mod skeleton caching, path finding, mass panic spreading, interface and EGO list, loading, sounds, memory (mod pictures, armor sprites), and garbage-collection pressure. Each has its own switch.
- LoadGuard: broken mod data files are skipped and logged; missing mod languages fall back to English; creature file index; optional repair of common data errors; warning button for mods that broke loading.
- SaveGuard: save backups, protection against overwriting a save that failed to load, optional load without removed mods.
- FastFix: about 40 fixes for vanilla bugs, specific mods and errors that any mod can have; optional patch guard.
- LogViewer: in-game log window with badges; optional advanced log with mod file diagnostics.
- EffectPool, DamageEffectThrottle, RecoverEffectThrottle: reuse and caps for damage popups, blood splashes and heal effects.
- PerfTools: optional performance overlay, `Logs/Perf.log` and profiler.
- New in this beta and on by default: `PerfPack.OrdealIdle`, `PerfPack.MouseLists`, `PerfPack.OptionSave`, `PerfPack.AttachMiss`, `PerfPack.PortraitCache`, `FastFix.OrdealReset`, `FastFix.UnknownEquipCount`. Earlier experimental features now on by default: `PerfPack.ClerkImpostor`, `PerfPack.IdleSpeed`, `PerfPack.DoorAnimCull`, `FastFix.PatchGuard`, `PerfPack.CatLazyExperimental`.

Known limits: tested on one PC and one heavily modded install. Mods that patch the game after the title screen are not detected by the start-up check of a few features.
