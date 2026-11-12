# Changelog

## 0.9.4-beta - 2026-11-12

Start-up crash mitigation, language switch, a vanilla language-change bug, three fixes and one GC fix. FastFix 1.34, LoadGuard 1.7, PerfPack 1.60, NailUI 1.16, LogViewer 1.8, core title localization; the other modules are unchanged.

- FastFix 1.34: `FastFix.Covariance` is off by default and marked experimental (existing config files keep their value). It no longer reads a mod DLL whose reference cannot be loaded (built for .NET Core, .NET 5 or .NET Standard); each one is logged as a warning. A `switch` with an impossible case count is rejected before memory is reserved. The log line shows heap and private memory before and after the pass.
- FastFix 1.34: `FastFix.CensoredVictimRelease` (on): CENSORED's sacrifice gets control back when CENSORED escapes before the employee arrives. `FastFix.YaZiOnDie` (on, only with YaZi): no error from YaZi's `AgentModel.OnDie` patch when WhiteNight is present. `FastFix.WeaponReskin` skips dead agents at day end (no error).
- LoadGuard 1.7: `CreatureDataLoader.ReLoad` (language change) reads `.xml` creature lists as the start-up load does. Vanilla read only `.txt`, so abnormalities from such mods vanished after a language change and the abnormality info window threw (`CreatureInfoCodex.Init`).
- Language: the title button, its menu, the F10 panel and the log window follow a language change without a restart. Japanese, Chinese, Traditional Chinese, Vietnamese and Bulgarian are detected from the game language (the font set reports them as English). Title texts can be translated in `Localization/<language>/NailUI.xml` (`TitleButton`, `Action.<id>`).
- PerfPack 1.60: `PerfPack.TimerNoGc` (on): `AutoTimer.Destroy` no longer runs a full garbage collection.

## 0.9.3-beta - 2026-11-11

Broken-mod guards, crash cause report, a self-healing start-up repair, three vanilla fixes and the new opt-in module GameFix. Core API level 10 (Alerts); FastFix 1.33, LoadGuard 1.6, PerfPack 1.59, NailUI 1.15, GameFix 1.0 (new); the other modules are unchanged.

- LoadGuard 1.6: `LoadGuard.TypeLoad` (on) loads what it can of a mod DLL that needs a missing or later-loaded library instead of stopping the code of every later mod; `LoadGuard.CreatureLoad` (on) skips an abnormality whose mod script is missing or crashes at day load; `LoadGuard.CrashTrail` (on) keeps the last step before a native crash in `Logs/LastSession.txt` and names it in the warning list at the next start; `LoadGuard.BaseModCheck` (on) warns once when BaseMod is not 1.3.9. Guard blocks (PatchGuard, LoopGuard, GuardLate) now show in the warning list through the core `Alerts` API.
- FastFix 1.33: `FastFix.Covariance` writes each step (assembly read, method patch) to `Logs/Covariance.step.txt` before it runs; a step left by a crashed session goes to `Logs/Covariance.skip.txt`, is skipped from then on and is named in the warning list (keys include the DLL write time, so an updated mod is tried again). Reported: Japanese install closed at start with 0.9.1+.
- Vanilla: `FastFix.AgentHoverJitter` (on): the enlarged agent plate no longer blocks uGUI rays, so hovering an agent whose plate covers the cursor no longer flickers. `FastFix.IronMaidenExit` (on): an agent who leaves We Can Change Anything alive is visible again and is not killed at day end. `FastFix.TwilightArmorNull` (on): the Twilight suit's damage factor before its setup returns 1.
- PerfPack 1.59: `PerfPack.WeaponModScript` (on, with ScriptTypes): `MakeWeapon` gives a mod weapon its script when no game class has that name. `PerfPack.InventoryZayinFirst` now defaults to off.
- GameFix 1.0 (new module, every option off by default, read at game start): `GameFix.TeddyDayEnd`, `GameFix.TipherethLower`, `GameFix.ApostleDragon`.
- Docs: uninstall text warns about `SaveBackups`; README no longer says the mod never changes gameplay. The panel now has 230 options.

## 0.9.2-beta - 2026-11-10

Title button placement, one vanilla fix, safety fixes for caches and spreads. FastFix 1.31, PerfPack 1.58, DamageEffectThrottle 1.1; core TitleWatch; the other modules are unchanged.

- Title: on custom title screens that move the menu icons (BlueArchive) the Nail and Logs buttons were off the screen. They are now placed on screen in one row with the other title icons, with the same spacing.
- Vanilla: `FastFix.WeaponReskin` (on). After a weapon was taken off an agent (for example Paulinus released) the agent kept the old weapon's look and attacks, and modded special weapons could deal no damage. A Postfix on `AgentModel.OnReleaseWeapon` calls `OnChangeWeapon()` as equipping does.
- PerfPack 1.58: `ModSkeletonCache` reuses a skeleton only from the same source and drops destroyed assets at day start; `AnimatorParams` no longer stores empty or uninitialized parameter sets; `MeshRelease` frees unused meshes only at day start and keeps meshes held by any component field; `WitnessSpread` falls back to the game's code on an error and clears its queue each day; `BossTextSpread` keeps the display time of boss texts (Binah, Colored Fixer, Red Mist); `FastScale` applies only inside the custom sprite load.
- DamageEffectThrottle 1.1: the popup limit moved from `DamageEffect.Invoker` to `UnitModel.MakeDamageEffect`, so mods that create popups themselves (BambooHattedKim critical hits) no longer get null.

## 0.9.1-beta - 2026-11-09

Crash fixes, many fixes for specific mods, one performance change. FastFix 1.30, PerfPack 1.57, NailUI 1.14; the other modules are unchanged.

- Crash: `FastFix.Covariance` (on) repairs mods built for a newer .NET that pass a list of one unit type where the game expects another (game closed with no error, for example after the Binah core suppression). `FastFix.CovarianceHandlers` (on) does the same inside methods with foreach, using or try (Krau Deluge); it needs `12Harmony.dll` in `LobotomyCorp_Data/Managed`. `FastFix.Hint12Harmony` lists once on the title screen the fixes that are off without 12Harmony.
- Guards: `FastFix.LoopGuard` (on) skips a non-patch method of another mod that keeps throwing the same error. `FastFix.StageEndGuard` (on) keeps a creature that throws at the end of the day from blocking the result screen.
- Vanilla: `FastFix.ChildDeleteTwice` (a child creature suppressed twice), `FastFix.ChildElevatorStuck` (a child creature whose path ends on an elevator).
- Specific mods, each with its own switch in Mod fixes (on; they apply only when the mod is installed): `BlackBirdMove`, `BlackBirdDeath`, `BlackBirdPassageHit` (needs 12Harmony), `GrunglyLure`, `PinocchioMove`, `PinocchioEscape`, `PinocchioRange`, `BoneFishView`, `WhaleTailPull`, `RottenWhaleRoam`, `DragonScaleView`, `EternalFlowerAttack`, `IsomerEffect`, `InvalidumPath`, `InvalidumSefira`, `InvalidumFlood`, `LadyInRainDraw`, `QiuNiuCircle`, `MephistoBuf`, `HookahCaterpillarAnim`, `CharlotteDamage`, `KekesiWolfHit`, `HomelessBeastHit`, `PuLaoChain`, `YaZiDeath`, `YaZiEscapeUI`, `QianRuEquip`, `CauldronLure`, `CandeloroRibbon`, `Cloud9Panic`, `NothingWorkerNoModel`, `ToyFactoryLog` (all `FastFix.` keys; `ToyFactoryLog` writes the full error to `ToyFactoryError.txt`).
- Performance: `PerfPack.PathCheck` now stays on with Candeloro installed; while Candeloro's edges are blocked the game's own path search runs. Before, the check switched itself off for the whole session.
- The panel now has 218 options.

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
