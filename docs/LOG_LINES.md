# Log lines

Important lines of `NailOptimization.log` and what they mean. `...` stands for text that varies, `<x>` for a value. INFO and WARN lines need the `Logging` flag (default on); ERROR lines are always written. Lines marked "advanced log" go to the file in `Logs/` when `LogViewer.AdvancedLog` is on.

## Core and loader

| Line | Meaning |
|---|---|
| `core boot apiLevel=9 ...` | The core started. NailUI needs API level 8 and LogViewer level 7. |
| `loader summary: files=N skipped=K loaded=L initialized=I errors=E` | Module loading is done. `errors=0` means all modules loaded. |
| `loader module <name> (<file>) requires api X but core api is Y: Init not called` | The module needs a newer core and was not started. |
| `config saved N keys (...)` | The config file was written. |

## NailUI

| Line | Meaning |
|---|---|
| `nailui game hook installed 2/2 (PlaySpeedSettingUI.Update, CameraMover.Update)` | The game hooks of the panel are active. |
| `nailui camera held while typing` / `camera released after typing` | The camera was locked while a text field of the panel was focused and released afterwards. |
| `nailui camera held on a wheel frame over a mod window` | The mouse wheel over a mod window does not zoom the camera. |
| `nailui PlaySpeedSettingUI.Update skipped on Escape (action window)` | Esc closed a mod window; the game's Esc menu was not opened. |
| `nailui hud placed mode=header\|frame ... listBottomPx=<n>` | Placement of the day screen button (`-1` means the frame fallback). |
| `nailui hud action slots=1` / `hud placed ... box=28x56` | A slot (for example Logs) was added to the day screen box. |
| `nailui ui host dropped` (WARN) | The core dropped the host; the day screen button is hidden. |
| `title icon tint r,g,b from <name>` | Fallback path: the title button is a plain button tinted with this color. |
| `title clone ok from StoryButton animator=True ... font=<game>-><ours>` | The title button is a clone of the game's Story button (hover, colors). |
| `title moved other button <name> left by <dx> world ...` / `was put back` | A title button of another mod was moved out of the way of ours and restored. |
| `title moved NoPlagueButton left by <x> world ...` | The AbnoSeal button was moved. |
| `title sound <0 hover, 1 click> player=<bool>` | First sound call of each kind. |
| `title icon loaded 64x64 from ...warn_sign.png`, `title badge created on NailOptimizationAction_logs ...`, `nailui hud badge created for logs ...` | The warning sign image loaded and the badges were built. |

## LogViewer

| Line | Meaning |
|---|---|
| `ui action registered logs` / `logviewer action registered logs` | The Logs entry point was registered in the core. |
| `title action button logs created` | The title screen Logs button was built. |
| `title clone triggers of StoryButton: PointerEnter:AudioClipPlayer.OnPlayInList;` | The hover sound trigger of the cloned button was kept. |
| `logviewer built in <ms> rows pool=<N>` | The log window was built; the row pool is at most 40. |
| `logviewer opened tab=...` / `closed (<why>)` / `view cleared tab=...` | Window life cycle. |
| `logviewer advanced hook installed 6/6 handler=True file=...` / `advanced log writing ...` | The advanced log is active. Fewer than 6 hooks or `writer failed` means it fell back to BaseMod `Log.txt`. |
| `logviewer import N entries errors=0 from .../Log.txt` / `old logs deleted N` | Early BaseMod lines were imported; rotation keeps 5 files. |
| `logviewer benign patterns builtin=4 user=K on=True\|False` | The benign filter state. |
| `INFO unity benign: <first line>` (advanced log) | A known harmless Unity message was lowered to INFO. |
| `diag scan requested by first frame lang=.. mods=N` / `diag scan done ... problems=N ms=N` | The mod file scan ran (once per start and per language change). |
| `ERROR diag malformed-xml\|missing-creature-info\|duplicate-modid\|loader-error ...` (advanced log) | A mod file problem. `loader-error file=BaseMods/...` is a BaseMod error file of this session. |

## LoadGuard

| Line | Meaning |
|---|---|
| `loadguard hook installed 8/8` | LoadGuard is active (all patches in). |
| `loadguard disabled by LoadGuard.Enabled=false` | LoadGuard is off. |
| `loadguard repair on` | `LoadGuard.Repair` is on. |
| `WARN loadguard skipped <file> [entry=<id>] reason=<Exception>: <msg>` | A broken mod file or entry was skipped. The path is relative to the game folder. |
| `loadguard fallback <folder> <Localize\|Equipment xmls\|CreatureInfo> lang=<cur>-><used>` | The mod has no data for the current language; another language was used. |
| `loadguard summary <localize\|equipment\|creatures\|isolate> files=N skipped=K ...` | One line per loader run (with `texts=`, `equipment=` or `creatures=`). `repaired=N` is added when Repair is on. |
| `loadguard summary creatures (reload) files=N skipped=K creatures=M` | A language change re-read the creature lists. |
| `WARN loadguard repaired <file> entry=<id> field=<...> value=<old>-><new>` | Repair changed a mod value in memory. |
| `creature info index: N keys, M ms` | The creature file index was built. |
| `creature generate info skipped at load, built again later` | Expected at each start. |

## SaveGuard

| Line | Meaning |
|---|---|
| `saveguard hook installed 4/4` / `saveguard options protect=True safeload=False backups=3` | Patches and options are active. |
| `saveguard disabled by SaveGuard.Enabled=false` | SaveGuard is off; no patches. |
| `saveguard backup <file> -> SaveBackups/<file>/<name> kept=N` | A copy was made before a save write. |
| `saveguard <file> depends on <asm>` | The written save holds data of that mod assembly. |
| `ERROR saveguard read failed <file> missing <asm> (type ...)` | The save cannot be read without that mod; the file is marked unsafe. |
| `ERROR saveguard write blocked <file>: ...` | `SaveGuard.Protect` kept the unreadable file; nothing was written. |
| `saveguard safeload attempt N <Ex>` / `saveguard safeload retry N <file> stubs=K` | The SafeLoad retry loop is binding stubs. |
| `WARN saveguard stripped <file> <path> (<asm>)` / `saveguard stash <file> -> SaveStash/<copy> entries=N` | Data of a removed mod was taken out and kept. |
| `saveguard stash entry done <file> <path>` / `saveguard stash entries=N` | An entry was already stashed / the stash index was loaded. |
| `saveguard restored <file> <path> (<asm>)` | Stashed data was put back after the mod returned. |
| `ERROR saveguard shrink <file> ... pinned=SaveBackups/<file>/pinned/<name>` | Creatures, equipment or codex entries were dropped; the old file was pinned. |
| `WARN saveguard missing creature modid=<m> id=<id>` | The save holds a creature of a mod that is not loaded. |
| `saveguard bind <asm> <type> -> <stub>` / `saveguard stub type <type> in stub assembly <asm>` | SafeLoad diagnostics, written only with `Logging`. |

## FastFix

| Line | Meaning |
|---|---|
| `fastfix whiterabbit fix installed (...)` / `fastfix hook installed n/m` | Fix state. `0/0` means the mod is absent. |
| `fastfix whiterabbit not applied: <reason>` | The fix was not installed. |
| `fastfix riskportrait fix installed (SefiraPanel.Start)` | The placeholder for empty risk level slots is in. |
| `fastfix starflare fix installed`, `mecanim fix installed, lookups replaced 8`, `buf snapshot installed, lists copied 2` | Those fixes are active. |
| `mecanim: animator clip not in its skeleton` | A missing clip was skipped. |
| `effect names: Effect@<folder> loaded, calls redirected N` | Effect name clash fix applied. |
| `effect names: <key> loaded, calls redirected N, methods with handlers skipped M` | The same, with methods skipped because they contain exception handlers. |
| `effect name <name> in A and B: no lookup found ...` (WARN) | A clash was found but could not be redirected. |
| `exec order: attributes of <type> unreadable, default order used` | The attribute fix fell back to the default order. |
| `fastfix guard on count=N/10s` | The patch guard is active with this threshold. |
| `WARN fastfix guard blocked patch <Type.Method> (<Prefix\|Postfix>) of <Type.Method> owner=<id> reason=<Exception> count=N/10s` | One foreign patch method was skipped for the session. |
| `WARN fastfix guard could not resolve` / `block ineffective` / `ERROR fastfix guard block failed` | The guard could not act (name not matched, inlined method, internal error). |

## PerfPack

| Line | Meaning |
|---|---|
| `PerfPack <Feature> installed ...` | The feature's patch is in. Examples: `PathCheck installed, verify=<bool>`, `FastSearch installed, verify=<bool>`, `SkinCache installed (AtlasUtilities.ToAtlasRegionPMAClone)`, `UiRefresh installed (AgentUnitUI.activateUI, <n> per second)`, `CursorScale installed (CursorManager.SetCursorScale)`, `RequireCache installed (InventoryAgentSlot.SetRequireInfo)`, `MeshRelease installed (MeshRendererBuffers.Initialize)`, `InventoryTabs installed`. |
| `PerfPack RequireCache not installed: ...` / `MeshRelease not installed: ...` | A foreign Prefix or an error; vanilla behavior stays. |
| `PerfPack RequireCache prefix failed, slot built as usual (logged once): ...` / `PerfPack MeshRelease: ...` | The patch failed once and the original ran. |
| `PerfPack PathCheck verify: checked=<n> mismatches=<n> nodeTarget=<n> edgeTarget=<n>` | Written every 5000 checks, only with `PerfPack.PathCheckVerify`. |
| `ERROR PerfPack PathCheck mismatch: ...` | The own answer differs from the original (verify only). |
| `WARN PerfPack PathCheck switched off: ...` | A foreign patch appeared after install. |
| `PerfPack FastSearch verify: checked=<n> mismatches=<n>` / `ERROR PerfPack FastSearch mismatch: ...` | Every 5000 searches / the own path differs from the original (only with `FastSearchVerify`). |
| `PerfPack SkinCache entries=<n> hits=<n> misses=<n> materials=<n> textures=<n> meshes=<n>` | Every 10 s, only with `PerfPack.SkinCacheStats`. |
| `PerfPack CursorScale freed its first texture` | First release of a scaled cursor texture. |
| `ArmorAtlas installed (WorkerSpriteManager.GetArmorData[_Mod])`, `ArmorAtlas indexed N armor files` | ArmorAtlas is active. |
| `EffectTextures: N pictures, M shared with an identical one` / `EffectTextures done: N textures, X MB of readable copies freed, errors E, S s` | Result of the effect picture pass. |
| `AgentTextures: N agent animations, M cat animations` / `AgentTextures done: ...` / `AgentTextures: pixels read through the video card for <name>` | Agent pictures pass and a picture whose pixels a mod read. |
| `AgentLazy: N animators wait for first use, M pictures` / `AgentLazy: loaded <name>, N pictures, M ms; total ...` | Lazy agent pictures. |
| `CreatureTextures: N renderers, N textures, M MB of readable copies freed, T ms` | Creature pictures pass. |
| `EffectLazy: N pictures wait for first use, M MB of level 0 freed` / `CatLazy: N animations wait for first use` / `LazyPictures: loaded <name> WxH, T ms; total ...` | Lazy effect and cat pictures. |

## Counters

When the core resets module state (`core reset hook fired n=<n> registered=<n>`) it writes `counter <Module>.<Name>=<value>` lines (up to 80 non-zero counters) that show how often a feature acted.
