# Architecture

Nail Optimization is a core assembly (`NailOptimization.dll`) plus modules (`modules/*.dll`). The core installs no game patches of its own. Every optimization, fix and protection lives in a module.

## Runtime folder

All runtime files sit next to `NailOptimization.dll`, in the mod folder:

```
NailOptimization/
  NailOptimization.dll        core
  modules/*.dll               modules, loaded by the core
  Info/                       LMM description files
  Localization/<lang>/        NailUI.xml, LogViewer.xml
  Images/                     icons
  NailOptimization.cfg.txt    config (created on first start)
  NailOptimization.log        log of the current session
```

## Core and modules

The game (through BaseMod) instantiates `Harmony_Patch` from the core assembly. Its constructor runs once (a second instance is ignored) and does:

1. Open the log (`NailOptimization.log` is truncated at each start).
2. Load the config file, then the core settings (`Logging`, `Order`, `Gui.*`, `Detect.*`, `Config.*`).
3. Load all modules from `modules/`.
4. Create the driver object (a `MonoBehaviour`) that runs the per-frame work of the core: config flush and watch, the module tick, the conflict scan at frame 2 and a deferred phase, the built-in panel.

Each phase is wrapped so that an exception is logged and the next phase still runs.

### Module loading

- The loader reads every file in `modules/`, sorted by name. `Config` key `Order` (comma-separated file names without `.dll`) moves the named modules to the front.
- A file that is not a `.dll`, is not a valid assembly, or repeats the assembly name of a module already seen is skipped and logged. A failed module is recorded and does not stop the others.
- For each assembly the loader reads its types and creates every type that implements `IModule`, then calls `Init()`. A failure in `GetTypes`, the constructor or `Init` is isolated to that module.
- The loader prints `loader summary: files=N skipped=K loaded=L initialized=I errors=E`.

### IModule contract and API level

```csharp
public interface IModule { string Name { get; } void Init(); }
```

Modules normally derive from `ModuleBase`, which adds optional members with safe defaults:

| Member | Meaning |
|---|---|
| `Version` | module version string for the log |
| `RequiredApiLevel` | lowest core API level the module needs; the module is not initialized on an older core |
| `Targets` | `Namespace.Class.Method` strings of patch targets, used only by the conflict detector |
| `OnConfigChanged(key)` | called on the main thread after a flag or parameter of the module changed (panel edit or live reload); exceptions are isolated by the core |

`Core.ApiLevel` is 9 in this release and the contract is append-only (`Core.ContractVersion` = 1). A new level only adds members, so a module built against an older level stays valid. The levels are:

| Level | Addition |
|---|---|
| 3 | typed config parameters (`Flag`, `IntParam`, `FloatParam`), `OnConfigChanged` |
| 4 | option catalog (`Options`), module list, UI host registration (`UiHost`) |
| 5 | extra UI entry points of other modules (`UiActions`): title buttons, day screen slots, Escape handling |
| 6 | alert badge per `UiActions` entry |
| 7 | split warning and error counts in badges |
| 8 | `BadgeView` with the warning sign image |
| 9 | `PatchUtil.Foreign*`, `PatchUtil.GuardLate`, `ModHooks.WhenLoaded`, `Events.On`, `DepthBuffers<T>` |

Other core helpers available to modules: `ULog` (logging), `Config`, `Counters`, `ResetRegistry` and `BoundedCache<K,V>` (state cleared at day end), `TypeCache`, and `PatchUtil` (`Resolve`, `WithPriority`, `Install`).

NailUI requires API level 8 and LogViewer level 7, so they are initialized only with a matching core.

## Config system

- The file is `NailOptimization.cfg.txt`, plain text, one `Key=value` per line. Lines starting with `#` or `;` and lines without `=` are ignored. Keys are case-insensitive. Booleans accept `true/false`, `1/0`, `on/off`, `yes/no`.
- A module declares an option with `Config.BindFlag(key, default, comment)`, `BindInt(key, default, min, max, step, comment)` or `BindFloat(...)`. The returned object holds the current value; hot paths read a field. The core clamps every write to the declared range and rejects NaN.
- The key prefix before the first dot names the module (`PerfPack.FastSearch` belongs to `PerfPack`).
- Missing keys use the default. Declared keys that are absent from the file are appended with their comment when the file is saved. A file edited by hand keeps its own lines and order.
- **Saving.** A change made in the panel is applied at once and written to the file after a short delay (0.5 s). `Config.Save=false` keeps changes for the session only. The first write of a session also stores the previous content as `NailOptimization.cfg.txt.bak`. If a write fails it is retried and the panel shows the state "NOT SAVED".
- **Live reload.** Every `Config.WatchSeconds` seconds (default 10, 0 = off) the file timestamp is checked. Changed keys are re-read and passed to the module through `OnConfigChanged`. Options marked "restart" in the panel are read once at game start; changing them in the file has no effect until the next start.

## Logging

- `NailOptimization.log` is written next to the DLL and truncated at each game start. Each line is `[HH:mm:ss.fff f=<frame>] LEVEL source message`, with level INFO, WARN or ERROR.
- The `Logging` flag (default true) controls INFO and WARN lines. ERROR lines are always written.
- Log lines that prove each feature's behavior are listed in [LOG_LINES.md](LOG_LINES.md).
- **Advanced log.** With `LogViewer.AdvancedLog` on (read at game start, off by default) the LogViewer module keeps its own log in `Logs/` instead of relying on BaseMod `Log.txt`. It hooks `LobotomyBaseMod.ModDebug` and `Application.logMessageReceived`, imports the early BaseMod lines, rotates old files (5 are kept), and adds a mod file scan (malformed xml, missing creature info, duplicate mod ids, BaseMod loader errors). If the hooks cannot be installed it falls back to BaseMod `Log.txt`. `LogViewer.HideBenign` lowers known harmless Unity messages to INFO so they do not count as errors.

## Patch ownership and PatchGuard behavior

The mod is built to coexist with other Harmony mods.

1. **Check before patching.** Before a feature installs a Prefix, Postfix or Transpiler it asks the core (`PatchUtil.ForeignAny/ForeignPrefix/ForeignPostfix/ForeignTranspiler`) whether a patch with an owner id outside `Lobotomy.NailOptimization.` already exists on that method. If it does, the feature steps aside, logs which mod owns the method, and the panel shows the same. The game method then runs as before.
2. **Standalone duplicates.** If a standalone mod that does the same job is loaded (for example AnimatorParamCache), the matching feature is not installed.
3. **Late guard.** For a prefix that returns `false` (full replacement of a method) the module registers it with `PatchUtil.GuardLate`. At the deferred phase after start and at every day start the core checks the target again. A foreign Transpiler, or a foreign Prefix that runs after ours (Harmony 1 skips prefixes after a `false`), switches the feature flag off and logs an ERROR. One flag guards one target.
4. **Fail-open.** Patches are written so that an exception inside them lets the original method run.
5. **Harmony 1.0.9.1 limits.** Harmony 1 drops exception handlers of a patched method (the patched copy has no try/catch/using/foreach cleanup). Methods whose body relies on a handler are therefore either replaced completely with the same logic, patched on a caller, or not patched at all.
6. **FastFix patch guard** (`FastFix.PatchGuard`, on by default). If the same exception comes from one foreign patch method `FastFix.GuardCount` times (default 100) within 10 seconds, FastFix patches that method with a Prefix that skips it for the rest of the session (Harmony 1.0.9.1 has no Unpatch) and logs it. For a skipped Prefix that returns bool the original method runs.
7. **Conflict detector.** At frame 2 the core scans the Harmony patches and loaded assemblies. Names listed in `Detect.Names` (default `ALot,NZM,SomeOptimizer,some.optimizer,Charming`) are reported as third-party optimizers. `Detect.DumpPatches` writes every detected patch to the log.

## Localization layout

Localization files sit in `Localization/<lang>/`. The language folder name is the game's current language; a missing language falls back to `en`.

- `NailUI.xml`: UI strings, sections, groups and options. **`en` is the layout source.** Other languages override texts only; their `section`, `group`, `order` and similar attributes are ignored.
- `LogViewer.xml`: strings of the log window (`en` is the fallback).

Structure of `NailUI.xml`:

```xml
<NailUI title="NailOptimization" version="1.0">
  <ui>       <s id="Apply">Apply</s> ... </ui>
  <sections> <section id="General" side="top">General</section> ... </sections>
  <groups>   <group id="Display">Display</group> ... </groups>
  <options>
    <option key="PerfPack.FastSearch" section="Performance" group="PerfLogic" order="10" restart="1">
      <name>...</name>
      <desc>...</desc>
    </option>
  </options>
</NailUI>
```

Option attributes:

| Attribute | Meaning |
|---|---|
| `key` | config key |
| `section` | section id; `side="top"` or `side="bottom"` on the section places it in the panel's list |
| `group` | group id inside the section |
| `order` | sort order inside the group |
| `restart` | `1` = needs a game restart (red text with a warning sign; listed in the notice after Apply) |
| `hidden` | `1` = removed from the interface (the key still works in the file) |
| `control` | special control: `choice` (list of values), `key` (key binding) |
| `requires` | assembly or mod name; the option is shown only when that mod is loaded |

An option declared by a module but missing from `NailUI.xml` is shown under Advanced with its raw key. The current `en` file lists 179 options.

## Files the mod writes at runtime

All inside the mod folder unless noted.

| File | Written by | Content |
|---|---|---|
| `NailOptimization.log` | core | session log |
| `NailOptimization.cfg.txt` | core | config |
| `NailOptimization.cfg.txt.bak` | core | config content before the first write of a session |
| `Logs/` | LogViewer (advanced log), PerfTools | advanced log files, `Perf.log` (with a `.prev.log` copy of the previous one), `Profiler.log` |
| `SaveBackups/<file>/` | SaveGuard | copies of a save file made before it is overwritten; `pinned/` holds an old file pinned after a suspicious drop of entries |
| `SaveStash/` | SaveGuard | original save kept by SafeLoad and an index (`stash.txt`) of removed data |
| `PerfPack.sounds.txt` | PerfPack | names of vanilla sounds to load ahead at the next start |
| `NailOptimization.fontwarm.txt` | PerfPack | font sizes the info window used, per language and screen height |
| `PerfTools.targets.txt` | read by PerfTools | list of profiler targets (written by the user) |

The mod does not write into game save files, except that SaveGuard can decide not to write one (see MODULES.md).
