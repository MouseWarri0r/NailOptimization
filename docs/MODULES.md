# Modules

Each module is a separate assembly in `modules/`, built from one file in `src/modules/`. Every module has a master switch (`<Module>.Enabled`) and, inside, a switch per feature. Target names below are taken from the source; "class.method" is the Harmony target. Option keys are explained in [CONFIG.md](CONFIG.md).

Common safety rules of all modules:

- Fail-open: an exception in a patch is caught or avoided and the original game code runs.
- Harmony 1.0.9.1 drops exception handlers of a patched method. A method whose body has try/catch, using or foreach cleanup is replaced as a whole with the same logic, patched on a caller, or left alone.
- A feature does not install over a method that another mod already patches (see [ARCHITECTURE.md](ARCHITECTURE.md)).
- Optimizations return the same result as the game. Where this is not provable by reading the code, a verify option exists (`FastSearchVerify`, `PathCheckVerify`) and some self-checks run automatically for the first calls.

## NailUI

**Purpose.** The settings interface for all modules.

**Features.**
- Panel opened with F10 (`NailUI.Hotkey`), a button on the title screens and a button box in the day screen. Sections: General, Performance, Memory, Effects, Protection, Bugfixes, Mod fixes, Interface, Experimental, Debug, Advanced.
- Values are staged and written with Apply; Done or a click outside discards staged values. Search field, tooltips (`NailUI.Tooltips`, `NailUI.TooltipDelay`), per-option default and range, "Reset all options to defaults" with confirmation, notice listing the restart-required options that were applied, save state indicator.
- Display settings: resolution, window mode (fullscreen, borderless, window), V-sync, FPS limit, smooth motion (50/25 FPS), texture quality.
- Optional FPS counter (`NailUI.FpsCounter`: off, simple, average/min/max, full) and options to hide agent titles and clerk names.
- Layout and texts come from `Localization/<lang>/NailUI.xml`; an option not listed there appears under Advanced.

**Harmony targets.** Optional and fail-open: `PlaySpeedSettingUI.Update` Prefix (skipped for the single frame of an Escape press that belongs to the panel) and `CameraMover.Update` (the camera is held while typing in the panel or while the mouse wheel is over a mod window), and `OptionUI.OnOpen` (Postfix that hides the game's own video panel, replaced by the Display group).

**Notes.** Requires core API level 8. If the host module is unavailable the core shows a built-in read-only fallback panel.

## PerfPack (Engine optimizations)

**Purpose.** The main performance work: about 60 features, one switch each (`PerfPack.<Feature>`), all under the master switch `PerfPack.Enabled`.

| Area | Features | Notes |
|---|---|---|
| Drawing and off-screen | `DistortionGrab`, `CanvasCull`, `AgentCull`, `ClerkCull`, `ClerkImpostor`, `DoorAnimCull`, `PauseSkeletons`, `CorpseFreeze`, `AgentUiHideZoom`, `OrdealUiHideZoom`, `AgentAnimLod` | Distortion effects use the previous finished frame instead of drawing the scene again. World-space canvases outside the camera are switched off about 4 times a second. Calm units outside the camera are paused and restored before any order; panicking, fighting and working units are never paused. Skeletons are not recomputed at time scale 0. Dead workers stop updating after a delay. |
| Animations | `AnimatorParams`, `ModSkeletonCache`, `SkeletonPrewarm`, `SkinCache`, `ArmorAtlas`, `MeshRelease`, `CursorScale`, `BufNameCache`, `LcIdCache` | Animator parameter names are cached per controller (`AnimatorUtil.HasParameter`). Mod Spine skeletons are parsed once. One texture and material per armor sprite is shared. Meshes dropped by `MeshRendererBuffers.Initialize` are destroyed. |
| Movement and logic | `FastSearch`, `PathCheck`, `FastFindCreature`, `IdleSpeed`, `RoomScan`, `BonusPool`, `SefiraLookup`, `AgentStats`, `UnitLoops`, `NoticePool`, `OrchestraWorkers`, `HpTrackerLoop`, `OrdealIdle`, `CameraCache`, `UiRefresh` | `GraphAstar.SearchPath` runs over reused arrays with the same heap order; `MovableObjectNode.CheckConnectedInPassage` is answered without a full A*. Several per-tick allocations are replaced by reused objects. |
| Mass events | `PanicThrottle`, `WitnessSpread`, `DeathBubbleCap`, `BossTextSpread` | `PanicReady.StartPanicAction` gets a per-frame time budget; skipped calls are retried by `PanicReady.Execute` on the next tick. Reactions of witnesses to a death are spread over ticks under a time budget with the same inputs. |
| Interface and EGO list | `InventoryZayinFirst`, `InventoryDefer`, `InventoryTabs`, `SlotCull`, `RequireCache`, `DescReuse`, `FontWarm`, `MouseLists`, `OptionSave`, `AttachMiss`, `PortraitCache` | The EGO list can open on ZAYIN (off by default; faster first opening). Requirement plates and description rows are reused. |
| Loading | `ScriptTypes`, `FastScale`, `SoundPrewarm`, `SkipIntroLogos`, `ReadAheadExperimental` | Script types of mod weapons and gifts are listed once. Custom sprites are resized on the video card and the full-size copies are freed. |
| Memory | `EffectTextures`, `AgentTexturesExperimental`, `AgentLazyExperimental`, `CreatureTexturesExperimental`, `EffectLazyExperimental`, `CatLazyExperimental` | Readable CPU copies of mod pictures are dropped, identical files are stored once, and some pictures load on first use. |
| Garbage collection | `GcQuiet`, `GcQuietGrowMb` | No Harmony patch. A 10 Hz driver runs a collection while the game is paused, an abnormality window is open or the day ends, and only after the heap grew by the configured amount. |
| Test modes | `FastSearchVerify`, `PathCheckVerify`, `SkinCacheStats` | Run the original code too and log differences, or log cache statistics. |

**Other Harmony targets named in the source.** `AgentUnitUI.activateUI` (UiRefresh), `AtlasUtilities.ToAtlasRegionPMAClone` (SkinCache), `WorkerSpriteManager.GetArmorData` / `GetArmorData_Mod` (ArmorAtlas), `CursorManager.SetCursorScale`, `InventoryAgentSlot.SetRequireInfo` (RequireCache), `MeshRendererBuffers.Initialize`, `UnitEquipSpace.GetBonus` (BonusPool), `UnitModel.GetUnitBufByName` (BufNameCache), `EquipmentTypeInfo.GetLcId`, `CreatureManager.FindCreature` / `GetCreature`, `Notice.Send`, `SilentOrchestra.UniqueEscape`, `CreatureInfoWindow.SetDesc`, `RenderTextureCamera` (DistortionGrab, `OnDestroy`), `SoundEffectPlayer.PlayOnce_Mod` / `Play_Mod` (SoundPrewarm), and `AgentModel.get_movement` (IdleSpeed).

**Safety rules.**
- `AgentCull` and `ClerkCull` do not disable the skeleton animator of a unit that is panicking, in combat, working or ordered; hiding a unit that waits for an animation would hang attacks and teleports of mods.
- `InventoryTabs` and caches are cleared at day end; caches are bounded.
- Sound lookup of the game (mod override first, then Resources) is never replaced, so mod sound overrides keep working.
- `FastSearch` and `PathCheck` switch off if a foreign patch appears on their target after install.
- Experimental options are off by default: a picture loaded on first use can stutter once, and a few mods read pixels of agent sprites.

## LoadGuard

**Purpose.** Keep mod data loading alive when a mod file is broken, and make start-up faster.

**Features.**
- Per-file and per-entry isolation for mod localization, equipment, creature and static data loading. A broken file or entry is skipped, its mod and file are logged, list additions of a failed entry are rolled back, and the rest loads.
- Language fallback: a mod without the current language uses English, then any available language (Localize, equipment xmls, CreatureInfo).
- `LoadGuard.CreatureIndex`: one index of creature files instead of a search through every mod folder for each abnormality.
- `LoadGuard.Repair` (off by default): fixes common data errors in memory only (a non-numeric `openLevel` becomes 0, a gift without `attachPos`/`attachType` gets defaults, a non-integer bonus gets a default). Files on disk are not changed.
- `LoadGuard.Alerts`: a warning button on the title screen lists mods that broke loading.

**Harmony targets (full replacement, Prefix returning false).** `LocalizeTextDataLoader.LoadLocalizeTextData`, `CreatureDataLoader.Load`, `CreatureDataLoader.Loading`, `Loading_Mod`, `LoadDoc`, `CreatureDataLoader.ReLoad`, `EquipmentDataLoader.Load`, `GameStaticDataLoader.LoadModIsolate`.

**Safety rules.** The vanilla methods have whole-method catches that Harmony 1 would drop, so the replacements reproduce the vanilla body with the same lists, order and override rules and add a try per mod file or entry. Mod files are never modified; vanilla game files keep the vanilla outcome.

## SaveGuard

**Purpose.** Protect save files.

**Features.**
- `SaveGuard.Backups` (default 3): the old file is copied to `SaveBackups/<file>/` before the game overwrites it; the newest N copies are kept (0 = none).
- `SaveGuard.Protect`: a save file marked unsafe is not written. A day file is unsafe when `GlobalGameManager.LoadDay` started and did not finish or its last read failed; the global file is unsafe when its last read failed or `BaseMods/Glerror.txt` was written after the last read. Progress of that session is not saved until the file loads again.
- A drop of creatures, equipment or codex entries pins the old file in `SaveBackups/<file>/pinned/`.
- `SaveGuard.SafeLoad` (off by default): data of removed mods is taken out on load so that the rest loads. The original is kept in `SaveStash/` and the data returns when the mod is loaded again.
- A report names the mods whose entries are missing (creatures, codex, equipment), which the game would drop silently.

**Harmony targets.** `SaveUtil.ReadSerializableFile` (replaced; a binder names the missing assembly) and `SaveUtil.WriteSerializableFile` (Prefix).

**Safety rules.** On the Mono runtime of the game a serializer resolves some generic member types without the binder; during a SafeLoad read the module supplies stub assemblies for missing types with a bounded retry. Without SafeLoad, reading follows the game's own logic (including the `.backup` fallback).

## FastFix

**Purpose.** Fixes for vanilla bugs, for bugs of specific mods and for errors any mod can have. The list is in [BUGFIXES.md](BUGFIXES.md).

**Features.** Every fix has its own switch. A fix for a specific mod is declared and installed only when that mod is loaded. `FastFix.PatchGuard` skips a foreign patch method that keeps throwing the same exception.

**Harmony targets named in the source.** `BlackSwanSister.CanTakeDamage`, `FixerWhite.CanTakeDamage`, `HellTrainDeparture.CollisionCheck`, `ExpandUI.OnClickCancel`, `UnitMouseEventManager.OnPointerClick`, `CameraMover.LateUpdate`, `MecanimTranslator.Apply`, `WorkerUnit.EndAttack` (special attack stuck), `SefiraPanel.Start` (risk-level portraits), `StoryStandingCGUI`, and methods of the listed mods (for example `CSArmor.CollisionCheck`, `CSCreature.MakeMovement`, `KingOfGreedWeapon.OnTakeDamage`, `FreischutzBullet.CollisionCheck`).

**Safety rules.** 0Harmony 1.0.9.1 has no Unpatch, so a foreign patch method is turned off by patching it with a Prefix that skips it. Fixes that replace a method do so only when no other mod touches it.

## GameFix

**Purpose.** Opt-in fixes of game rules that the game gets wrong. They change gameplay, so every option is off by default and is read at game start; a patch is installed only when its option is on.

**Features.** `GameFix.TeddyDayEnd`: an agent caught in Happy Teddy's hug when the day ends dies, as the hug would have ended (only on a normal day end; restart and return to title are not affected). `GameFix.TipherethLower`: the lower Central Command team gets the continued-service stat bonus of the upper team. `GameFix.ApostleDragon`: the Yin and Yang dragon passes WhiteNight's apostles.

**Harmony targets.** `GameManager.ExitStage` (Prefix, frame mark), `HappyTeddy.OnStageRelease` (Prefix), `AgentModel.Get{Fortitude,Prudence,Temperance,Justice}StatBySefiraAbility` (Postfix), `YinAndYangDragonCollider.OnTriggerEnter2D` (Prefix).

## LogViewer

**Purpose.** Show the logs in game.

**Features.** A log window with tabs for `NailOptimization.log`, the BaseMod `Log.txt` (or the advanced log when enabled) and the profiler log of PerfTools. Entry points are a title button next to the settings button and a slot in the day screen box, both with warning/error badges. `LogViewer.MaxLines` and `LogViewer.PollMs` bound the work. With `LogViewer.AdvancedLog` the module writes its own log with a column of warnings and errors and a scan of mod file problems. `LogViewer.HideBenign` and `LogViewer.BenignPatterns` lower known harmless Unity messages to INFO.

**Harmony targets.** Only with the advanced log: `LobotomyBaseMod.ModDebug` handler hooks and `Application.logMessageReceived` (6 hooks). Off: no patch.

**Safety rules.** Fail-open: if the advanced log hooks do not install completely, logging stays with BaseMod `Log.txt`. The window uses a pool of at most 40 rows.

## EffectPool

**Purpose.** Reuse damage popups instead of creating and destroying them.

**Features.** Pool of damage popups (`EffectPool.MaxPool`, default 150), optional rebind of animators of a reused popup, optional pool for hit particles (`EffectPool.PoolParticles`, off by default; a particle variant is pooled only if its self-destroy scripts live at least as long as the parent).

**Harmony targets.** `EffectInvoker.Invoker` (movable and position variants, Prefix and Postfix) and `EffectLayer.Delete` (Prefix); with the particle pool also `DamageParticleEffect`.

**Safety rules.** Replaces the standalone EffectPool mod: remove that mod. Every patch passes through to vanilla when the module is off.

## DamageEffectThrottle

**Purpose.** Cap damage popups and blood splashes.

**Features.** `MaxPopupsPerFrame` (default 6) and `MaxPopupsPerSecond` (default 60); over budget the popup is not created. `MaxBloodPerPassage` (default 10; the oldest splash is removed over the limit), `BloodLifetimeSeconds` (default 60), sweep interval and destroy-or-hide choice for removed splashes.

**Harmony targets.** `UnitModel.MakeDamageEffect(RwbpType, float, DefenseInfo.Type)` (Prefix, priority Last; since 1.1 the limit sits here, so mods that call `DamageEffect.Invoker` themselves always get a popup), `PassageObject.AddBloodMapObject` (Prefix and Postfix), `EffectLayer.Update` (Postfix).

**Safety rules.** Only the outer overloads are patched; skipping an inner call would return null into the outer one. Replaces the standalone mod of the same name.

## RecoverEffectThrottle

**Purpose.** Cap heal and sanity recovery effects.

**Features.** A cooldown per agent and per type, a per-frame and a per-second budget, and a skip when there is nothing to heal.

**Harmony targets.** `AgentModel.MakeRecoverEffect` (Prefix), `AgentModel.RecoverHP` and `RecoverMental` (Prefix and Postfix). Parameter names are bound by Harmony (`isPhyiscal` is spelled that way in the game).

**Safety rules.** Replaces the standalone mod of the same name: remove that mod.

## PerfTools

**Purpose.** Measurement tools for people who want to check the effect of the mod.

**Features.** A performance overlay (FPS, frame and tick time, graph; default key F3, `PerfTools.Key`), `Logs/Perf.log` with one line per second while the overlay or a profiler session is active and with spike lines (`PerfTools.SpikeMs`), and a targeted profiler (default key Shift+F3, `PerfTools.ProfKey`) whose targets are read from `PerfTools.targets.txt`. Limits for profiler summary time, duration, events and file size are options.

**Harmony targets.** `GameManager.FixedUpdate` (tick time, Prefix priority 800 and Postfix priority 0) and the profiler targets.

**Safety rules.** The overlay canvas has no raycaster and never blocks clicks. The profiler uses 64 static slots and skips targets with exception handlers (the patched copy would lose them), except finally blocks that only dispose a generic enumerator. File writes happen in the driver, outside measured code.
