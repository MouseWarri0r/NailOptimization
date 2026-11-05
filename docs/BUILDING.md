# Building Nail Optimization

The mod is built with the .NET SDK against the game's own assemblies. Nothing from the game is copied into the output or into this repository.

## Requirements

- Lobotomy Corporation (Steam) with BaseMod (Lobotomy Mod Manager) installed, so that `LobotomyCorp_Data/Managed` holds `0Harmony.dll` (Harmony 1.0.9.1) and `LobotomyBaseModLib.dll`.
- .NET SDK 6 or newer (any SDK that builds SDK-style projects; tested with 10.0).
- Internet access once, for the NuGet package `Microsoft.NETFramework.ReferenceAssemblies.net35` (lets MSBuild target .NET 3.5; it is not shipped with the mod).

## Build

```
dotnet build build/NailOptimization.sln -c Release
```

If the game is not in the default Steam folder, pass its `Managed` folder:

```
dotnet build build/NailOptimization.sln -c Release -p:GameManaged="D:/Games/LobotomyCorp/LobotomyCorp_Data/Managed"
```

The output is a ready mod folder:

```
bin/NailOptimization/
  NailOptimization.dll        core
  modules/<Module>.dll        10 modules
  Localization/  Info/  Images/
```

Copy `bin/NailOptimization` into `LobotomyCorp_Data/BaseMods/` (replace the old folder; your `NailOptimization.cfg.txt` and `SaveBackups/` in it are kept if you copy over it instead of deleting it first).

## Project layout

| File | Builds |
|---|---|
| `build/Common.props` | shared settings: `net35`, C# 7.3, compile against the game's `mscorlib` 2.0 (`NoStdLib`), references with `Private=false` |
| `build/NailOptimization.csproj` | `src/NailOptimization.cs` -> `NailOptimization.dll`, copies `assets/` next to it |
| `build/modules/<Module>/<Module>.csproj` | `src/modules/<Module>.cs` -> `modules/<Module>.dll`, references the core |

Notes on the settings:

- `Assembly-CSharp.dll` of the game references both `mscorlib` 2.0 and 4.0. Dependency analysis is switched off (`_FindDependencies=false`) so MSBuild still resolves it (otherwise warning MSB3258 and missing game types).
- `12Harmony.dll` (Harmony 2, shipped by some mod managers) must never be referenced: it uses the same `Harmony` namespace with a different API.
- Assembly info is not generated, so every assembly has version 0.0.0.0, as in the released package.

## Code rules (the game runs Unity 2017 Mono, .NET 3.5 profile)

Compiling is not proof that the code runs in the game. These constructs compile but fail or misbehave at run time and are not used in the sources:

- calls that compile to `Array.Empty<T>()` (for example `TrimStart()` / `TrimEnd()` without arguments): the game's `mscorlib` lacks it;
- `string.IsNullOrWhiteSpace`, `lock`, and other members missing from the game's `mscorlib`;
- `==` / `!=` on `Type`, `MethodInfo`, `FieldInfo` and other reflection objects (use `object.ReferenceEquals`);
- Harmony patches on methods that contain `try`/`catch`, `using` or `foreach`: Harmony 1 drops their exception handlers;
- allocation, logging or reflection in per-frame code.

## Released DLLs

The DLLs in the 0.9.0-beta package were built from these sources with a different toolchain (Roslyn through a dnlib-based rebuild tool), so a local build is not byte-identical to the release, but it compiles the same code.
