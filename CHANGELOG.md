# Changelog

## 0.9.8-beta - 2026-11-25

Protection against hidden or stuck characters, three community-mod fixes and a safer deferred patch build. Includes the 0.9.7.1 hotfix. PerfPack 1.75, FastFix 1.77, LoadGuard 1.16 with LoadGuardH1, NailUI 1.17; the other modules are unchanged.

- PerfPack 1.75: `CullKeepAnimState` (on): agents and clerks hidden outside the view keep their animator running state; before, the animator was switched off and came back at the default pose. `VisibilityGuard` (on): a unit in view still marked hidden is shown again within a second; a feature repaired 3 times in a day is switched off for the session; a unit that vanished after it was drawn is named in the log. `ControlWatch` (on): an agent uncontrollable for over 120 s is named in the log (no automatic release). These lines also go to `Logs/NailReport.txt`.
- LoadGuardH1: `HarmonyDefer` registers a method only after its jump is written, a method left without a wrapper throws an error instead of crashing the game, and a failed rebuild names the mods whose patches may be lost. The build now has a `LoadGuardH1` project (`modules/h1/LoadGuardH1.dll`).
- Mod fixes (FastFix 1.76-1.77): `VbfYield` (Vanilla Bug Fixes Void Dream fix skipped while ours is on), `PeiPeiLogWrite` (PeiPeiEGO stun file write per tick), `FengYunMoveLoop` (FengYun knock-back freeze), `HengMingAttack` (HengMing weapon attack error).
- 0.9.7.1 hotfix: LoadGuard 1.14-1.16: base-game abnormalities no longer disappear on a day restart when another mod patches the abnormality build; `ModId` keeps a mod's GlobalInfo ID in every language (Incubator no longer overwrites Mimicry); abnormality ids without data are dropped from the extraction screen (Pinel Luti). NailUI 1.17: `HideAgentTitles` keeps titles hidden.
- Known: a red TV/scanline screen filter seen in one test run is not caused by Nail; cause open.

## 0.9.7-beta - 2026-11-23

Faster start with many mods, a faster abnormality window with EGO readers, character depth and face fixes, two vanilla fixes and Incubator fixes. LoadGuard 1.13, FastFix 1.75, PerfPack 1.74, NailUI 1.16, GameFix 1.0 (new option); the other modules are unchanged.

- LoadGuard 1.13: `HarmonyDefer`: Harmony rebuilt the patched copy of a method after every single patch; with many mods one method can be patched dozens of times. Nail now builds it once, at the method's first call or shortly after patching stops. Restart required.
- FastFix 1.75: `EgoReaderButtons`: the EGO switch buttons of ComEGO readers (EGO packs, Tiamat, Fairy Long Legs, Bamboo-hatted Kim, The King in Binds and their copies) no longer freeze the abnormality window for up to 1.5 s on each open (the button picture folder is looked up once), and no longer log a layer error. An EGO pack written for a game abnormality no longer adds its buttons to, or changes the EGO list of, a mod abnormality with the same id (Incubator's T-01-02, Mimicry).
- PerfPack 1.73-1.74: `SkeletonFrameSync`: characters build their picture on the same frames as their pose, so the face no longer slips on the head at medium zoom (game bug). `UnitDepth`: agents and clerks get separate depths, so overlapping characters no longer clip through each other (game bug; applies from the next day). `ModTexturesExperimental` (off): large mod textures that never change drop their readable copy in memory (about 700 MB on a day-49 test save). The RenderDiag diagnostic was removed.
- NailUI 1.16: borderless fullscreen uses the monitor the window is on.
- Vanilla: `DeadAgentSlotStats` (FastFix 1.74, on): work and suppression windows show 0 HP and 0 SP for a dead agent (display only). `GameFix.BigBirdTrumpet` (off): after Big Bird is suppressed, the second trumpet lets it out again only when the alarm has left that level.
- Mod fixes (FastFix 1.71-1.73, each only active with its mod): Incubator escaped witches no longer pick, keep or hit dead agents and clerks and drop a target that left their room (`IncubatorWitchTarget`); pooled Incubator animations destroyed by a day restart are dropped (`IncubatorPoolDestroy`, error in Incubator_Child.Leave). `ForeignLeaks` cleans more mod memory leaks at day change (Link EGO, Blood Rose, Knight's Tears and others).

## 0.9.6-beta - 2026-11-19

Agents frozen after death, faster loading of large mod lists, configurable panel keys, six vanilla fixes and fixes for many abnormality mods. FastFix 1.70, PerfPack 1.72, LoadGuard 1.12, LogViewer 1.9, core panel options; the other modules are unchanged.

- PerfPack 1.72 (agents frozen in a standing pose after death): an agent killed while off-screen no longer stays standing, `AgentCull` replays the death when the agent comes back into view. An agent whose animator was reset after the death (Walpurgis Night, special weapons that switch back such as Justitia, Da Capo, Acacia) gets the death animation again instead of a standing or walking pose. The body pose is fully updated before the corpse is frozen, so bodies no longer freeze in a half-drawn pose. Death animations of mods are left to the mod.
- LoadGuard 1.12 (loading): on old saves with many mod items the inventory no longer searches every mod class for each item (several minutes on a save with about 300 mod items). `CustomDamageCache`: kekesi_CustomDamageBase finds custom damage types through a list built once (about 6 s less at start with many mods). `DisableLoadErrorCheck`: the LoadErrorCheck mod is turned off for the session (its start-up error screen could not be left); load problems go to Nail's warning list instead.
- Interface: the panel key (`Gui.Hotkey`) and the title screen button (`Gui.TitleButton`, `Gui.TitleIcon`) can be changed. `Gui.Prewarm`: the panel is built at start, so the first F10 is faster. The overlay and profiler keys of PerfTools can be changed.
- Vanilla (FastFix 1.70, all on): `AttackEndNull` (an attack with no damage event no longer throws at its end), `InvalidNodeLog` and `MovableStateLog` (a unit moved by a mod while it walks no longer fills the log with invalid node or movable state messages), `ChildViewWait` (a child creature that escapes on the tick it is made waits for its view; The Crying Children, ReUse threw here), `CreatureNoNameLog` and `AddComponentNullQuiet` (less log spam from mod creatures without a name and from AddComponent with no type).
- Mod fixes (FastFix 1.70, each only active with its mod): Incubator (mod id lookup, so Walpurgis Night lasers, witches and EGO checks are found again; Walpurgis Night at day start; witch animation pool; agents between rooms; EGO gift script via GiftFix). Uncertain Page: no more freeze on use; the abnormality picture was searched in every file of every mod on each use, the search index is now built in the background at game start. Four Fools: abnormality choice screen error, Zkbuf icon. Suppress window of escaped child creatures: Goddess of the Stars (FEW RiskLevel), Souls Pawn Shop (HSLEB RiskLevel). Tiamat EGO reader: layer error of its EGO buttons, in every copy of the DLL that mods ship. Others: The Lady in Rain (missing special death animation), I-1236 Branch (abnormality window hover), Ling (area select), Penitent Clown (sheep attacks), Blood Shoes (bleed end, missing LOG folder), Nobody Is (disguised agent title), Magical Trio (weapon with no owner), Celestial Bunny (damage with no attacker), Endlessgyro (EGO limit), GetPassageRect error spam of several mods.
- LogViewer 1.9: the Four Fools sprite path warning at start is filtered as known noise.

## 0.9.5-beta - 2026-11-16

Start-up crash with Many Winged Angel, 17 new fixes for abnormality mods, six vanilla fixes and allocation cuts. FastFix 1.42, PerfPack 1.62, LoadGuard 1.8; the other modules are unchanged.

- FastFix 1.36: `FastFix.Covariance` checks each mod DLL file for references this game's runtime does not have (`System.ValueTuple`, `Array.Empty` and others) before it reads the DLL; such methods are skipped and named in the log instead of closing the game (Many Winged Angel / DIYMOD.dll closed the game at the first start after an update). LoadGuard 1.8: after a crash inside Covariance the crash report names the DLL or method it was working on.
- Vanilla (FastFix, all on): `CorpseSelectRing` (an agent who dies while selected no longer keeps the selection ring), `CreatureNoAnimState` (a creature without an animation script no longer throws when suppressed or returned), `DamageFactor` (a unit with a weapon and no armor no longer loses its gift damage factor to an error), `SoundSequenceEmpty` (a sound sequence with missing files no longer throws), `BulletWindowNull` (bullet count update before the bullet panel exists no longer throws). `FastFix.BufDamageFactor` (off): applies damage factors of mod buffs, which vanilla drops; changes the balance of such mods.
- Mod fixes (FastFix, each on and only active with its mod): `FDTick` (Krau Deluge crash in its room, now also without 12Harmony), `CustomRiskChild`, `FeatherHardMode` (Bloody Past read its settings file every tick: 0.6 ms -> 0 per tick), `BloodShoesFix`, `FairyQueenFix`, `RaspberryWork`, `CharmingPond`, `TurbidityParalyze`, `BurrowingHeavenLure`, `ScytheClockCount`, `WhiteRabbitReset`, `BloodRabbitClear`, `EternalFlowerThornCap` (Flower of Eternity 1.0.1; thorns per 4 s in `EternalFlowerThorns`, 12), `IntroChain` (IntroAdder pictures, then the Wonderlab intro), `BaxiaObserve`, `TiamatFix` (also repairs saves), `SignOfRosesTarget`; `KekesiWolfHit` updated.
- PerfPack 1.61: `CreatureUiHideZoom` (60): from this camera size the bars and labels of escaped abnormalities and their spawns are hidden (measured -5 ms canvas time per frame in a full breach at far zoom).
- PerfPack 1.62: `PassageLookup` (hallway lookup used by water, light and other mod effects every tick no longer allocates; TurbidityCurrent 3 KB per tick), `CounterText` (room Qliphoth counters no longer create a new text every tick). `NoticePool`: one abnormality or mod that throws in its per-frame update no longer stops every later one in that frame (logged once per type and method). `AgentLazy`: two mods with the same agent animation folder no longer stop all agent animations from loading (the first one wins, a warning names both).

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
