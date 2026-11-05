# Nail Optimization

Version 0.9.0-beta. A modular performance and stability mod for Lobotomy Corporation. It is built on BaseMod (Lobotomy Mod Manager, LMM) and Harmony 1.0.9.1 and runs on the game's Unity 2017 Mono runtime.

It targets large mod lists, where the game starts slowly, runs out of memory, stutters on late days and can lose a save because of one broken mod file. Most optimizations work on the game's own code, so they also help a vanilla game. Every feature has its own switch, and the mod writes what it installed, skipped or fixed to `NailOptimization.log`.

The mod does not change game rules, damage, stats, work results or the save format. Where an optimization could return a different result from the game, the feature compares itself with the game first and switches itself off if they disagree.

## Contents

- [Modules](#modules)
- [Measurements](#measurements)
- [Installation](#installation)
- [Settings](#settings)
- [Compatibility](#compatibility)
- [Building](#building)
- [Repository layout](#repository-layout)
- [Documentation](#documentation)
- [License and credits](#license-and-credits)

## Modules

The mod is one core assembly plus ten modules loaded from `modules/*.dll`. See [docs/MODULES.md](docs/MODULES.md) for details and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how they fit together.

| Module | Purpose |
|---|---|
| NailUI | Settings panel (F10, a button on the title screen and in the day screen) with every option of every module, tooltips, English and Russian texts. Display settings (resolution, window mode, V-sync, FPS limit, texture quality) and an optional FPS counter. |
| PerfPack | Engine optimizations, about 60 features: rendering and off-screen culling, animations, interface, path finding, mass events, sounds, loading, memory, garbage-collection pressure. |
| LoadGuard | A broken mod data file is skipped and logged instead of stopping the load. A missing mod language falls back to English. An index of creature files speeds up start-up. Optional repair of common data errors. |
| SaveGuard | Backups of save files before they are overwritten. A save that failed to load completely is not overwritten by the incomplete version. Optional load without removed mods. |
| FastFix | About 40 fixes for vanilla bugs, for bugs of specific popular mods and for errors that any mod can have. An optional patch guard that skips a foreign patch method which keeps throwing. |
| LogViewer | In-game log window with warning and error badges. Optional advanced log with mod file diagnostics. |
| EffectPool | Damage popups are reused instead of created and destroyed. |
| DamageEffectThrottle | Damage popups per frame and per second are capped. Old blood splashes are removed. |
| RecoverEffectThrottle | Heal and sanity recovery effects are capped by a cooldown and a per-frame and per-second budget. |
| PerfTools | Optional performance overlay, `Logs/Perf.log` and a targeted profiler for people who want to measure. |

## Measurements

**Test PC.** AMD Ryzen 5 5600H (laptop), NVIDIA GeForce GTX 1650 4 GB, 16 GB RAM, 1920x1080. All numbers come from this one PC. The modded numbers come from one install with about 275 mods and a day 49 save. On another PC, mod list or save the absolute numbers differ; compare the direction of the change.

### Modded: about 275 mods, day 49 save

The comparison run keeps only the LoadGuard module of Nail Optimization, because several mods in the list have no English localization and do not load without it. One run each. Frame time is the average over two 12 s windows.

| | LoadGuard only | Full Nail |
|---|---|---|
| Game start to title | 114-124 s | 69 s |
| Memory (private) at title | 25.8 GB | 4.9 GB |
| Continue to preparation | over 25 min (the game did not respond most of that time) | 24 s |
| Day start | 5.5 s | 18 s |
| Memory (private) at preparation / day / after the panic | 28.2 / 28.8 / 28.9 GB | 7.4 / 7.7 / 7.8 GB |
| Frame time, zoom 5 / 15 / 30 / 60 / 90 | 645 / 623 / 643 / 628 / 620 ms (about 1.6 fps) | 15.7 / 14.6 / 16.7 / 16.3 / 19.3 ms (64 / 68 / 60 / 61 / 52 fps) |
| All agents in one room, zoom 8 / 30 | 604 / 602 ms (1.7 fps) | 15.4 / 15.7 ms (65 / 64 fps) |
| Mass panic of all agents, 15 s | 671 ms average, worst frame 1311 ms | 22.1 ms average, worst frame 118 ms |

Without Nail Optimization this mod list needs about 26-29 GB of memory, so on a 16 GB PC the game lives in the page file: Continue took over 25 minutes and the day runs below 2 fps. With Nail Optimization the same list fits in under 8 GB.

### Vanilla: no content mods, day 46 save

BaseMod and Nail Optimization only (plus a test bridge used to drive the game). One run with Nail Optimization and one without. Frame time is the average over two 12 s windows; lower is better. Start times include the mod manager's own start sequence.

| | Without Nail | With Nail |
|---|---|---|
| Game start to title | 29.9 s | 13.0 s |
| Continue to preparation | 27.3 s | 17.6 s |
| Frame time, zoom 5 (closest) | 23.0 ms (43 fps) | 11.0 ms (91 fps) |
| Frame time, zoom 15 | 23.2 ms (43 fps) | 12.7 ms (79 fps) |
| Frame time, zoom 30 | 22.8 ms (44 fps) | 15.0 ms (67 fps) |
| Frame time, zoom 60 | 24.5 ms (41 fps) | 14.8 ms (68 fps) |
| Frame time, zoom 90 (whole facility) | 25.1 ms (40 fps) | 15.7 ms (64 fps) |
| All agents in one room, zoom 8 | 23.2 ms (43 fps) | 10.5 ms (95 fps) |
| All agents in one room, zoom 30 | 25.6 ms (39 fps) | 12.7 ms (79 fps) |
| Mass panic of all agents, 15 s | 32.6 ms average, worst frame 556 ms | 16.9 ms average, worst frame 79 ms |
| After the panic | 37.5 ms (27 fps) | 14.8 ms (67 fps) |
| Memory (private) at title / preparation / day | 1.26 / 2.98 / 3.15 GB | 1.74 / 3.35 / 3.52 GB |

On vanilla the mod uses about 0.4-0.5 GB more memory, most likely because it keeps sounds, skeletons and its caches loaded so they are not loaded again during the day. The large memory savings apply to mod pictures, so they show only with content mods.

## Installation

Requirements: Lobotomy Corporation (Steam) with BaseMod (Lobotomy Mod Manager).

1. Extract the `NailOptimization` folder into `LobotomyCorp_Data/BaseMods/`.
2. Enable NailOptimization in LMM and put it at the top of the mod list.
3. Start the game. The files `NailOptimization.log` and `NailOptimization.cfg.txt` are created in the mod folder on the first start.

Uninstall: disable the mod and delete the folder. The mod writes no data of its own into your saves. If SaveGuard made backups, they are in `SaveBackups/` inside the mod folder.

If you used standalone versions of DamageEffectThrottle, EffectPool or RecoverEffectThrottle, remove them from BaseMods: the matching modules replace them and two patches on one method conflict.

## Settings

- Press **F10** in game, or use the button on the title screen. The panel lists all 179 options in sections (General, Performance, Memory, Effects, Protection, Bugfixes, Mod fixes, Interface, Experimental, Debug, Advanced). Values are staged and written with **Apply**.
- The config file is `NailOptimization.cfg.txt` in the mod folder, one `Key=value` per line. It is checked for outside edits every 10 seconds (`Config.WatchSeconds`) and applied live where the option allows.
- Options marked "restart" in the panel (red text with a warning sign) apply after a game restart. The panel shows a notice after Apply that lists them.
- A fix for a specific mod is declared only when that mod is loaded and is hidden in the panel otherwise.
- Full list with defaults: [docs/CONFIG.md](docs/CONFIG.md).

Experimental options (`PerfPack.AgentTexturesExperimental`, `PerfPack.AgentLazyExperimental`, `PerfPack.CreatureTexturesExperimental`, `PerfPack.EffectLazyExperimental`) are off by default: a picture loaded on first use can stutter once, and a few mods read pixels of agent sprites. Turn them on if the game runs out of memory.

## Compatibility

- Before a feature patches a game method it checks whether another mod already patches it. If one does, the feature steps aside and the log and the panel name that mod. Most mods work with Nail Optimization without changes.
- The few features that fully replace a game method do so only when no other mod touches that method.
- Content mods (abnormalities, EGO, events, translations) are compatible. Saves stay compatible with and without the mod.
- Other optimization mods patch the same methods. Remove them: Nail Optimization contains its own version of what they do. If one stays, the matching Nail feature switches itself off, so nothing breaks twice, but you lose that feature. The known cases are NZM Optimization MOD+, SomeOptimizer, abcdcode AgentOptimize, VBlankFF Optimization, mindedness ReplaceTargetAnim (cursor) and the standalone AnimatorParamCache, SoundClipCache, CorpseFreeze, PanicThrottle, PathfindingPerf, CreaturePortraitPerf and StoryStandingCGFix.
- Mods that patch the game after the title screen are not detected by the start-up check of a few features.
- FastFix repairs bugs of a number of specific mods. See [docs/BUGFIXES.md](docs/BUGFIXES.md).

## Building

See [docs/BUILDING.md](docs/BUILDING.md).

## Repository layout

```
README.md
LICENSE
CHANGELOG.md
src/
  NailOptimization.cs      core: Harmony_Patch entry, public API, module loader,
                           config, logging, built-in fallback panel
  modules/                 one source file per module (compiled to modules/*.dll)
assets/
  Info/                    mod description files for LMM (GlobalInfo.xml, en, ru)
  Localization/<lang>/     NailUI.xml and LogViewer.xml (en and ru)
  Images/                  panel and button icons
docs/
  ARCHITECTURE.md          core, module loading, config, logging, patch guard
  MODULES.md               per-module description
  CONFIG.md                every option with default and restart flag
  BUGFIXES.md              bugs fixed by FastFix
  LOG_LINES.md             important log lines and their meaning
  BUILDING.md              build instructions
build/                     MSBuild projects
```

## Documentation

| File | Content |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the core and the modules work together |
| [docs/MODULES.md](docs/MODULES.md) | What each module does |
| [docs/CONFIG.md](docs/CONFIG.md) | All options |
| [docs/BUGFIXES.md](docs/BUGFIXES.md) | Fixed bugs |
| [docs/LOG_LINES.md](docs/LOG_LINES.md) | Log reference |
| [CHANGELOG.md](CHANGELOG.md) | Release history |

When reporting a problem, attach `NailOptimization.log` and `BaseMods/Error.txt`.

## License and credits

MIT License, Copyright (c) 2026 MouseWarri0r. See [LICENSE](LICENSE).

Code written by Claude (Anthropic AI model) in Claude Code, directed, tested in game and published by MouseWarri0r.

Ideas come from earlier optimization mods by NZM, sjksjrjr (SomeOptimizer), abcdcode, VBlankFF and mindedness. Their code is not included.
