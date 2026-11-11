# Configuration reference

This file lists every option shown in the NailUI panel (F10), generated from `assets/Localization/en/NailUI.xml` and the `Bind*` calls in the sources. The panel has 219 options.

Options are stored in `NailOptimization.cfg.txt` as `Key=value`. Flags use `true`/`false`. A file edited outside the game is applied live after `Config.WatchSeconds` (default 10 s). Options marked **Restart: yes** apply after a game restart.

Columns: **Key**, **Default** (from the source; `see config file` where the source does not state a literal), **Restart**, **Description** (name and tooltip text of the panel; numeric range, required mod and control type are appended). `Requires mod` means the option is declared only when that mod is loaded and is hidden otherwise.

Options in the Interface, Experimental and Debug sections are shown at the bottom of the panel; the others at the top.

## General

### Display

| Key | Default | Restart | Description |
|---|---|---|---|
| `Display.Resolution` | `0` | no | **Screen resolution**. Click to cycle 16:9 resolutions. Takes effect after Apply. Range 0..99999999. Choice list. |
| `Display.WindowMode` | `current game setting` | no | **Window mode**. Fullscreen, borderless (instant alt-tab, resolution ignored) or window. Range 0..2. Choice list. |
| `Display.VSync` | `current game setting` | no | **V-sync**. Frames wait for the monitor. Off: a bit more FPS below the refresh rate, tearing possible. |
| `Display.FpsCap` | `0` | no | **FPS limit**. Most frames per second. 0 = no limit. Ignored with V-sync. Range 0..300. |
| `Display.FpsSmooth` | `false` | no | **Smooth motion (50/25 FPS)**. Holds 50 FPS, one game tick per frame; drops to 25 when the game cannot keep up. Replaces the FPS limit. |
| `Display.TextureQuality` | `current game setting` | no | **Texture quality**. Lower saves video memory; little FPS change. Range 0..2. Choice list. |

### Game start

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.SkipIntroLogos` | `false` | yes | **Skip intro logos**. The two logo screens before the title are skipped (about 12 s). |

## Performance

### Optimization modules

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.Enabled` | `true` | yes | **Engine optimizations**. All optimizations of the game's code; each has its own switch. |

### Animations

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.AnimatorParams` | `true` | yes | **Animator parameter cache**. Animator parameter checks of agents and clerks are cached instead of building a list each time. |
| `PerfPack.ModSkeletonCache` | `true` | yes | **Mod skeleton cache**. Mod weapon and animation skeletons are parsed once, not on each animation change (10-100 ms each). |
| `PerfPack.SkeletonPrewarm` | `true` | yes | **Prepare special weapon skeletons**. Special weapon skeletons load at day start, not in the first fight. |
| `PerfPack.CorpseFreeze` | `true` | yes | **Freeze dead bodies**. Dead agents and clerks stop animating after a short delay and wake up if revived. |
| `PerfPack.CorpseDelaySeconds` | `6` | no | **Freeze dead bodies: delay (s)**. Game seconds after death before a body is frozen. Range 0..60. |

### Loading

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.SoundPrewarm` | `true` | yes | **Sound preload**. Sounds played before are loaded ahead at game start, so their first play does not freeze a fight. |
| `PerfPack.SoundPrewarmBudgetMs` | `6` | no | **Sound preload: ms per frame**. Most ms per frame spent preloading sounds. Range 1..50. |
| `PerfPack.SoundKeepMax` | `600` | no | **Sound preload: sounds kept**. Most sounds kept loaded; more use more memory. Range 0..5000. |
| `PerfPack.ScriptTypes` | `true` | yes | **Mod weapon and gift scripts found through one list**. Weapons and gifts find their mod script through a list built once instead of scanning the types of every mod each time. |
| `PerfPack.WeaponModScript` | `true` | no | **Mod weapon scripts without a game namesake**. With the script list on: a mod weapon whose script class has no game class of the same name gets its script. The game gives it none, so its special effect never runs. |
| `PerfPack.FastScale` | `true` | no | **Custom sprite scaling**. Custom agent sprites are scaled on the video card at start; full-size copies are freed. |
| `PerfPack.AttachMiss` | `true` | yes | **Missing gift sprite looked up once**. A gift sprite that no mod provides is looked up once instead of in every mod folder on each appearance update. |
| `PerfPack.PortraitCache` | `true` | yes | **Portraits looked up once**. Vanilla abnormality portraits and missing custom portraits are looked up once per scene instead of on every call. |
| `LoadGuard.BootFiles` | `true` | yes | **Shared BaseMods file list at start**. Some mods read the list of all BaseMods files when the game starts (Train Ego Mod: about 3 s). The list is built in the background while other mods load and handed to them. The log compares it with a fresh list after start. |
| `LoadGuard.CreatureIndex` | `true` | yes | **Abnormality file index**. Abnormality stat files are found through an index built once instead of searching all mod folders each time. Needs the load guard. |

### Mass events

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.PanicThrottle` | `true` | yes | **Spread mass panic**. Mass panic starts over several frames instead of one, so the game does not freeze. |
| `PerfPack.PanicBudgetMs` | `25` | no | **Spread mass panic: ms per frame**. Most ms per frame spent starting panic. Range 5..100. |
| `PerfPack.WitnessSpread` | `true` | yes | **Spread witness reactions**. Witness reactions to a death spread over a few ticks; same damage. |
| `PerfPack.WitnessBudgetMs` | `2` | no | **Witness reaction budget (ms)**. Most ms per tick spent on witness reactions. Range 1..20. |
| `PerfPack.DeathBubbleCap` | `true` | yes | **Fewer bubbles after a death**. Limits speech bubbles per frame after a death. |
| `PerfPack.DeathBubbleMax` | `8` | no | **Death speech bubbles per frame**. Speech bubbles per frame after a death. Range 1..60. |
| `PerfPack.BossTextSpread` | `true` | yes | **Build boss texts gradually**. Boss texts build over several frames. |

### Interface and EGO list

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.UiRefresh` | `true` | yes | **Slower agent label refresh**. Agent names and titles refresh a few times per second instead of every frame. |
| `PerfPack.UiRefreshHz` | `4` | no | **Agent label refreshes per second**. Label refreshes per second. Range 1..60. |
| `PerfPack.SlotCull` | `true` | yes | **Inventory: update visible slots only**. With the inventory open, slots out of view skip their update. |
| `PerfPack.RequireCache` | `true` | yes | **E.G.O list opening speed**. The EGO list keeps its requirement plates between openings. |
| `PerfPack.InventoryDefer` | `true` | no | **EGO list build**. The EGO list builds gradually on the preparation screen; the day loads up to twice as fast. |
| `PerfPack.InventoryZayinFirst` | `false` | no | **EGO list opens on ZAYIN**. The EGO list opens on ZAYIN each day; click ZAYIN again for all grades. |
| `PerfPack.InventoryTabs` | `true` | no | **Fast EGO tabs**. EGO tabs switch without toggling every slot. |
| `PerfPack.DescReuse` | `true` | no | **Info window lines**. The abnormality info window reuses its description lines. |
| `PerfPack.FontWarm` | `true` | no | **Font warm-up**. Info window text sizes are prepared at day start. |
| `PerfPack.MouseLists` | `true` | yes | **Mouse hover without new lists**. The check of what is under the mouse cursor reuses its lists instead of creating new ones every frame. |
| `PerfPack.OptionSave` | `true` | yes | **Mod option sliders save once**. Dragging a mod option slider writes the options file once, 0.5 s after the last change, instead of on every step. |

### Movement and game logic

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.FastSearch` | `true` | no | **Fast path search**. Path search without allocations; same paths. |
| `PerfPack.PathCheck` | `true` | yes | **Room connection check without a path search**. Room connection checks skip the full path search; same answers. |
| `PerfPack.RoomScan` | `true` | yes | **Skip enemy search in calm rooms**. Units skip the enemy search in rooms with only controlled workers. |
| `PerfPack.IdleSpeed` | `true` | yes | **No speed calculation for standing agents**. Standing agents skip the speed calculation each tick. |
| `PerfPack.FastFindCreature` | `true` | no | **Fast abnormality search**. Abnormality search by id without allocations. |
| `PerfPack.SefiraLookup` | `true` | yes | **Department lookup**. Department lookup creates no garbage. |
| `PerfPack.OrdealIdle` | `true` | yes | **Skip ordeal update without ordeals**. The ordeal update is skipped while no ordeal is running, instead of creating a list every tick. |
| `PerfPack.OrchestraWorkers` | `true` | yes | **Silent Orchestra worker list**. The abnormality reuses its employee list while breached. |
| `PerfPack.FrameStepCap` | `false` | yes | **Cap slow frame step**. A slow frame runs at most 2-3 game ticks instead of about 16. Below 20 FPS the game runs slower than real time. |

### Drawing and off-screen

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.DistortionGrab` | `true` | no | **Dimensional Refraction render**. The distortion bends the last frame instead of drawing the scene twice. |
| `PerfPack.CanvasCull` | `true` | yes | **Hide off-screen interface**. World interface outside the view is switched off. |
| `PerfPack.AgentCull` | `true` | no | **Agents outside the view**. Calm agents out of view stop animating and drawing. |
| `PerfPack.ClerkCull` | `true` | no | **Clerks outside the view**. Calm clerks out of view stop animating and drawing. Off while clerk pictures are on. |
| `PerfPack.ClerkImpostor` | `true` | yes | **Clerks as pictures at wide zoom**. With 20 or more clerks at wide zoom, calm clerks are drawn as still pictures. |
| `PerfPack.AgentAnimLod` | `true` | no | **Agent animation LOD**. Zoomed out, agents animate a little less often. |
| `PerfPack.PauseSkeletons` | `true` | no | **Skeletons on pause**. On pause, characters refresh their pose rarely. |
| `PerfPack.DoorAnimCull` | `true` | yes | **Stop off-screen door animators**. Door animators stop out of view. A door changed off-screen plays its old transition when seen. |
| `PerfPack.AgentUiHideZoom` | `60` | no | **Hide employee bars from zoom**. From this camera size the bars, grade, title, buffs and speech of employees are hidden. 0 = never. Range 0..90. |
| `PerfPack.OrdealUiHideZoom` | `14` | no | **Hide ordeal unit bars from zoom**. From this camera size, bars and names of ordeal units are hidden when 20 or more are out. 0 = never. Range 0..60. |
| `PerfPack.CameraCache` | `true` | yes | **Main camera cache**. The main camera is found once per frame. |
| `PerfPack.CameraRange` | `true` | yes | **Camera zoom range read once**. The camera reads its zoom range once instead of rebuilding the list of open departments several times per frame. |

### Fewer garbage collections

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.UnitLoops` | `true` | yes | **Unit loops without allocations**. Buff and gift loops of employees and clerks without allocations. |
| `PerfPack.AgentStats` | `true` | yes | **Employee stats without allocations**. Reading employee stats does not allocate memory. |
| `PerfPack.BonusPool` | `true` | yes | **Bonus pool**. Equipment and gift bonuses are summed without new objects. |
| `PerfPack.NoticePool` | `true` | no | **Notice pool**. Game notices no longer copy the listener list on each send. |
| `PerfPack.BufNameCache` | `true` | yes | **Buff name cache**. Buff search by name without allocations. |
| `PerfPack.LcIdCache` | `true` | yes | **Equipment id cache**. Equipment ids are not created again on each request. |

## Memory

### Memory use

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.EffectTextures` | `true` | yes | **Effect pictures in memory**. Effect pictures keep one copy in memory; identical ones are shared. |
| `PerfPack.ArmorAtlas` | `true` | yes | **Fast mod armor loading**. Mod armor pictures load once into one texture instead of from disk on every use. |
| `PerfPack.SkinCache` | `true` | yes | **Shared armor textures**. Agents share armor textures and materials instead of creating new ones on each animation change. |
| `PerfPack.MeshRelease` | `true` | yes | **Character mesh memory fix**. At each day start, character meshes that nothing uses any more are destroyed; meshes held by any component field are kept. |
| `PerfPack.CursorScale` | `true` | yes | **Aiming cursor memory fix**. The aiming cursor frees its old texture. |
| `PerfPack.GcQuiet` | `true` | no | **Memory cleaning in quiet moments**. Memory is cleaned on pause, in the info window and at day end. |
| `PerfPack.GcQuietGrowMb` | `48` | no | **Quiet cleaning threshold (MB)**. Memory growth in MB before a quiet cleaning. Range 8..256. |

## Effects

### Optimization modules

| Key | Default | Restart | Description |
|---|---|---|---|
| `EffectPool.Enabled` | `true` | no | **Effect pooling**. Damage popups are reused instead of created and destroyed. |
| `DamageEffectThrottle.Enabled` | `true` | no | **Damage effect limiter**. Limits damage popups and removes old blood splashes. |
| `RecoverEffectThrottle.Enabled` | `true` | no | **Heal effect limiter**. Limits heal and sanity recovery effects. |

### Heal effects

| Key | Default | Restart | Description |
|---|---|---|---|
| `RecoverEffectThrottle.CooldownPerAgent` | `0.5` | no | **Heal effect cooldown**. Game seconds between heal effects of one type on one agent. 0 = no cooldown. Range 0..5. |
| `RecoverEffectThrottle.MaxPerFrame` | `4` | no | **Heal effects per frame**. Most heal effects in one frame. Range 1..50. |
| `RecoverEffectThrottle.MaxPerSecond` | `30` | no | **Heal effects per second**. Most heal effects per second. Range 1..500. |
| `RecoverEffectThrottle.SkipWhenNothingToHeal` | `true` | no | **Skip empty heals**. No effect when the heal changes nothing. |

### Damage popups

| Key | Default | Restart | Description |
|---|---|---|---|
| `DamageEffectThrottle.MaxPopupsPerFrame` | `6` | no | **Damage popups per frame**. Most damage popups in one frame. Range 1..50. |
| `DamageEffectThrottle.MaxPopupsPerSecond` | `60` | no | **Damage popups per second**. Most damage popups per second. Range 1..500. |

### Object pools

| Key | Default | Restart | Description |
|---|---|---|---|
| `EffectPool.MaxPool` | `150` | no | **Damage popup pool size**. Most pooled damage popups. 0 = no pooling. Range 0..1000. |

### Blood splashes and cleanup

| Key | Default | Restart | Description |
|---|---|---|---|
| `DamageEffectThrottle.MaxBloodPerPassage` | `10` | no | **Blood splashes per passage**. Blood splashes at once in a passage; the oldest is removed first. 0 = no limit. Range 0..100. |
| `DamageEffectThrottle.BloodLifetimeSeconds` | `60` | no | **Blood lifetime**. Game seconds before a splash is removed. 0 = never. Range 0..600. |
| `DamageEffectThrottle.SweepInterval` | `5` | no | **Cleanup interval**. Seconds between checks for expired splashes. Range 0.5..60. |
| `DamageEffectThrottle.DestroyInsteadOfHide` | `true` | no | **Destroy removed splashes**. On: removed splashes are destroyed. Off: they are hidden (use it if MissingReferenceException appears). |

### Damage popup animation

| Key | Default | Restart | Description |
|---|---|---|---|
| `EffectPool.RebindAnimators` | `true` | no | **Restart popup animation**. A reused popup plays its animation from the start. |

## Protection

### Crash and save protection

| Key | Default | Restart | Description |
|---|---|---|---|
| `LoadGuard.Enabled` | `true` | yes | **Mod data load guard**. A broken mod data file is skipped and logged instead of breaking the whole load. A missing mod language falls back to English. |
| `LoadGuard.Repair` | `false` | yes | **Repair broken mod data**. Instead of skipping, fixes common errors in memory (a non-number level becomes 0, a gift without a position gets an empty one). Files on disk stay unchanged. Needs the load guard. |
| `LoadGuard.TypeLoad` | `true` | yes | **Partial loading of broken mod code**. A mod DLL that needs a missing or later-loaded library loads what it can instead of stopping the code of every later mod. The mod is named in the warning list. Works for mods loaded after NailOptimization. |
| `LoadGuard.CreatureLoad` | `true` | yes | **Day load survives broken abnormalities**. An abnormality whose mod script is missing or crashes is skipped (or kept with an error) at day load instead of stopping the load. The mod and abnormality are named in the warning list. |
| `LoadGuard.BaseModCheck` | `true` | yes | **Warn about an unsupported BaseMod version**. If BaseMod (LMM) is not version 1.3.9, the warning list names it and opens once on the title screen. |
| `LoadGuard.CrashTrail` | `true` | yes | **Name the cause of the last crash**. The last step before the game closed with an error (mod being loaded, scene, day, last error) is kept in `Logs/LastSession.txt`. At the next start it is named in the warning list, which opens once on the title screen. |
| `LoadGuard.Alerts` | `true` | yes | **Mod problem warnings**. A warning button on the title and in the day lists mod problems: code that failed to load or start, data files that had to be skipped, mod patches or methods that PatchGuard/LoopGuard skipped for the session, and fixes switched off because another mod's patch runs after them. Shows the mod folder. |
| `SaveGuard.Enabled` | `true` | yes | **Save guard**. Save files are copied to SaveBackups before they are overwritten; a save that failed to load is not overwritten. The log names missing mods. |
| `SaveGuard.Protect` | `true` | yes | **No overwrite after a failed load**. After a failed load the save file is not written until it loads again; progress of that session is not saved. Needs the save guard. |
| `SaveGuard.SafeLoad` | `false` | yes | **Load without removed mods**. Data of removed mods is taken out on load, so the rest of the save loads. The original stays in SaveStash; the data returns with the mod. Needs the save guard. |
| `SaveGuard.Backups` | `3` | no | **Backups per save file**. Copies of each save file kept in SaveBackups. 0 = none. A copy made after a sudden loss of data is never deleted. Range 0..20. |
| `FastFix.UnknownEquipCount` | `true` | yes | **Save load: ignore equipment no mod defines**. A mod that grants equipment no mod defines gets nothing instead of an error that stops the save load (Sephirah data). |

### Patch guard

| Key | Default | Restart | Description |
|---|---|---|---|
| `FastFix.PatchGuard` | `true` | yes | **Patch guard**. A mod patch that throws the same error many times within 10 s is skipped until the game closes and logged with the mod id. Can turn off a feature of that mod. |
| `FastFix.GuardCount` | `100` | no | **Patch guard: errors before skip**. Identical errors within 10 s before a patch is skipped. Range 10..1000. |
| `FastFix.LoopGuard` | `true` | yes | **Loop guard**. A method of another mod (not a patch) that throws the same error many times within 10 s is skipped until the game closes, if it returns nothing, and logged. Can turn off a feature of that mod. |

## Bugfixes

### Fixes of the game

| Key | Default | Restart | Description |
|---|---|---|---|
| `FastFix.PanicStuck` | `true` | yes | **Panicking agent stuck on the way**. A panicking agent sent to open a containment unit walks on after an interrupted walk. |
| `FastFix.TrainRestart` | `true` | yes | **Day restart during the Hell Train kills employees**. Restarting the day while Express Train to Hell departs no longer kills employees of the reloaded day: they no longer start dead with their EGO lost. |
| `FastFix.TwilightArmorNull` | `true` | yes | **Twilight suit error before day setup**. The Twilight suit's damage bonus no longer throws an error when it is asked before the day set the suit up (a suit given during the day by a mod). |
| `FastFix.IronMaidenExit` | `true` | yes | **We Can Change Anything: agent invisible after work**. An agent who leaves We Can Change Anything alive is visible again and is no longer killed at the end of the day; the machine closes as after a death. |
| `FastFix.VoidDream` | `true` | yes | **Void Dream: employees stay asleep or frozen**. Each employee put to sleep by Void Dream is released by his own wake-up animation, or after 8 s if it never plays, instead of staying frozen and invincible until he dies at day end. The cry hits every unit in the hallway, not only those before the first downed one. |
| `FastFix.OrdealReset` | `true` | yes | **Ordeal does not carry over to the next day**. An ordeal still running when the day ends (retry, return to title) no longer continues in the next day. |
| `FastFix.ExpandOnce` | `true` | yes | **Department expanded twice in one day**. Cancel pressed after confirming a department expansion no longer lets the same expansion be confirmed again. |
| `FastFix.SelectClick` | `true` | yes | **Clicks taken for a drag**. A click that moved the mouse a little still selects or gives the move order instead of being taken for a drag. A drag cut off by switching windows no longer blocks later clicks. |
| `FastFix.AgentHoverJitter` | `true` | yes | **Agent plate flicker on hover**. The agent name plate no longer flickers when the mouse is over an agent and the enlarged plate covers the cursor. |
| `FastFix.CameraDrag` | `true` | yes | **Map drag freezes during camera shake**. Dragging the map with the right or middle mouse button keeps following the mouse while the camera shakes from hits, instead of freezing and jumping. |
| `FastFix.BlackSwanReflect` | `true` | yes | **Black Swan reflection**. Black Swan no longer reflects damage to herself endlessly (a stack overflow). |
| `FastFix.FixerWhiteReflect` | `true` | yes | **White fixer reflection**. The White fixer no longer reflects damage endlessly while praying (a game freeze). |
| `FastFix.StandingCG` | `true` | yes | **Story character pictures**. A story picture with duplicate face names loads instead of breaking. |
| `FastFix.ChildDeleteTwice` | `true` | yes | **Child creature removed twice**. A child creature that is suppressed twice (some mods do it) no longer throws the second time. |
| `FastFix.WeaponReskin` | `true` | no | **Riot stick look after unequip**. After a weapon is unequipped the agent shows and swings the default weapon as it should, instead of keeping the old weapon's look and attacks. |
| `FastFix.ChildElevatorStuck` | `true` | yes | **Child creature stuck at an elevator**. A child creature whose path ends on an elevator no longer throws out of its update; it is handled as for other creatures. |

### Rule fixes (change the game)

Module GameFix. Every option changes a rule of the game and is off by default.

| Key | Default | Restart | Description |
|---|---|---|---|
| `GameFix.TeddyDayEnd` | `false` | yes | **Happy Teddy: agent dies if the day ends during the hug**. Rule change. An agent caught in Happy Teddy's hug when the day ends dies, as the hug would have ended. In the game he survives. Restart and return to title are not affected. |
| `GameFix.TipherethLower` | `false` | yes | **Lower Central Command: continued-service bonus**. Rule change. Agents of the lower Central Command team get the continued-service stat bonus that the upper team gets. |
| `GameFix.ApostleDragon` | `false` | yes | **Yin and Yang dragon passes the apostles**. Rule change. The Yin and Yang dragon no longer flips the health of WhiteNight's apostles, as it already spares WhiteNight. |

## Mod fixes

### Specific mods

| Key | Default | Restart | Description |
|---|---|---|---|
| `FastFix.WhiteRabbit` | `true` | yes | **Celestial Bunny**. The normal time mode is turned off: it threw errors every frame and changed animation speeds in its room. |
| `FastFix.RiskPortrait` | `true` | yes | **FEW and Elysia risk levels**. No error every frame for empty abnormality slots on the preparation screen. |
| `FastFix.Starflare` | `true` | yes | **Starflare**. Suit notes and creature movement no longer throw. |
| `FastFix.Bamboo` | `true` | yes | **Bamboo-hatted Kim**. Fixes double buff ticks, repeated icon and sound loads and the tassel buff. |
| `FastFix.Cauldron` | `true` | yes | **Cauldron**. Minion animation events no longer throw errors. |
| `FastFix.Chaofeng` | `true` | yes | **Chaofeng**. The slowing wind no longer makes units walk backwards; its damage no longer throws on a kill. |
| `FastFix.OverBossBird` | `true` | yes | **Over Armor Twilight**. Agent deaths no longer log an error. |
| `FastFix.AddRoom` | `true` | yes | **kekesi_AddRoom**. The department panel no longer logs a layer error. |
| `FastFix.AcaciaBullet` | `true` | yes | **Acacia**. The bullet check no longer throws when the bullet touches something that is not a unit. |
| `FastFix.ElysiaRiskUnit` | `true` | yes | **Elysia RiskLevel**. The creature check no longer throws every frame for a creature with a missing part. |
| `FastFix.KingOfGreedPassage` | `true` | yes | **KingOfGreedWeapon Enhance**. The weapon no longer throws when its wearer is hit between passages. |
| `FastFix.PinelWorkText` | `true` | yes | **Pinel Luti's Creature**. The work text check no longer throws in every containment unit each tick (about 2 ms per frame). |
| `FastFix.ColoredFixer` | `true` | yes | **Colored Fixer: stuck agent after Kali EGO**. An agent no longer stays stunned and out of control after Kali's EGO ends; EGO change and teleport effects no longer repeat on later animations. |
| `FastFix.CensoredChildren` | `true` | yes | **CENSORED Remake: dead children removed**. Summoned children that died are removed 3 s later instead of at the end of the day, so the day no longer slows down with every summon. Requires mod: kygi123_Censored_Remake. |
| `FastFix.HellTrain` | `true` | no | **Train Ego: Hell Train gift**. A hit is no longer lost when the target has a similar buff. |
| `FastFix.PersephoneGifts` | `true` | no | **Persephone gifts on death**. Gifts of a dead agent in the full set vanish with the body instead of hanging in the air. Requires mod: PersephoneCreature. |
| `FastFix.TieIcon` | `true` | no | **TieTheTree armor icon**. The armor buff icon is loaded once instead of from disk for every buff (a lag every 6-7 s). Requires mod: TieTheTree. |
| `FastFix.HarmonyLabel` | `true` | no | **Harmony label on dead agents**. The weapon's note staff is hidden while its agent is dead. Requires mod: YD25_HarmonyRemake_Mod. |
| `PerfPack.HpTrackerLoop` | `true` | yes | **HP Awareness**. No allocations for every employee every frame. Requires mod: TheNamesan_HPAwareness_MOD. |
| `PerfPack.CatLazyExperimental` | `true` | yes | **ChildHood: cat animations on first appearance**. Cat animations load when a cat first appears: faster start, a short stutter later. Experimental. |
| `FastFix.BlackBirdMove` | `true` | yes | **White and Black Bird: first move**. A black bird that escapes no longer throws on its first move. |
| `FastFix.PinocchioMove` | `true` | yes | **LCPD Pinocchio: first move**. A puppet that escapes no longer throws on its first move. |
| `FastFix.BoneFishView` | `true` | yes | **Rotten Whale Fall: bone fish**. A bone fish without its animation no longer throws when it appears. |
| `FastFix.DragonScaleView` | `true` | yes | **Yin Yang Dragon Scale**. The abnormality without its animation no longer throws when the facility loads. |
| `FastFix.EternalFlowerAttack` | `true` | yes | **Flower of Eternity: attack at day end**. A flower attack that lands at the end of the day no longer throws and stops other animation events of that frame. |
| `FastFix.IsomerEffect` | `true` | yes | **Isomer: damage effect**. The damage effect picture is read from the mod folder (the mod looks for a folder with another name). |
| `FastFix.ToyFactoryLog` | `true` | yes | **ToyFactory: full error text**. An error that the mod's notice guard catches is written in full, with its stack, to ToyFactoryError.txt and once per kind to the NailOptimization log. |
| `FastFix.InvalidumSefira` | `true` | yes | **Invalidum: departments at day end**. Lighting the departments back up no longer throws when a creature is outside every passage. |
| `FastFix.InvalidumPath` | `true` | yes | **Invalidum: sounds**. Its sounds are read from the mod folder (the mod looks for a folder with another name). |
| `FastFix.PinocchioRange` | `true` | yes | **LCPD Pinocchio: removed target**. An attack on a target removed at the end of the day no longer throws. |
| `FastFix.LadyInRainDraw` | `true` | yes | **Lady in Rain: drawing**. Drawing stops when its view or painting tool is gone instead of throwing every frame. |
| `FastFix.QiuNiuCircle` | `true` | yes | **QiuNiu: circle**. Its circle no longer throws on units that are not agents, officers, creatures or children. |
| `FastFix.MephistoBuf` | `true` | yes | **Mephisto: debuff on officers**. Its debuff on an officer or another non-agent no longer throws when that unit is hit. |
| `FastFix.HookahCaterpillarAnim` | `true` | yes | **Hookah Caterpillar: animation**. Its animation is found even when the mod list lookup fails, instead of the creature staying without animation. |
| `FastFix.WhaleTailPull` | `true` | yes | **Rotten Whale Fall: tail pull**. Its tail skips the pull while the target is between rooms instead of throwing every frame. |
| `FastFix.CharlotteDamage` | `true` | yes | **Charlotte: damage between rooms**. Taking damage between rooms no longer throws. |
| `FastFix.KekesiWolfHit` | `true` | yes | **Alleyway Watchdog: area hit**. Its area hit no longer stops when it kills someone in the room. |
| `FastFix.PuLaoChain` | `true` | yes | **PuLao: endless damage chain**. Its attack near a music creature no longer repeats itself without end and closes the game; each creature joins the chain once. |
| `FastFix.NothingWorkerNoModel` | `true` | yes | **It Goes By The Name of Dream: empty worker**. A NothingWorker object that the mod created without initializing it no longer throws an error every frame. |
| `FastFix.PinocchioEscape` | `true` | yes | **LCPD Pinocchio: escape tick**. Picking its defence target and chasing a target that just died no longer throw every tick. |
| `FastFix.InvalidumFlood` | `true` | yes | **Invalidum: flooded passage**. The flooded passage no longer stops when a unit in it dies or leaves during the check. |
| `FastFix.HomelessBeastHit` | `true` | yes | **Homeless Beast: department hit**. Its hit on the whole department no longer stops when it kills someone. |
| `FastFix.YaZiDeath` | `true` | yes | **YaZi: death animation**. Suppression without a death animation no longer throws every frame; the creature is hidden as in vanilla. |
| `FastFix.BlackBirdDeath` | `true` | yes | **Black Bird: death animation**. Suppression of the black bird without a death animation no longer throws every frame; it goes straight to the pose it takes after that animation. |
| `FastFix.RottenWhaleRoam` | `true` | yes | **Rotten Whale Fall: roaming**. Without a target it keeps one roaming destination instead of choosing a new one every tick and turning back and forth. |
| `FastFix.QianRuEquip` | `true` | yes | **QianRu: EGO take-back**. Taking back its EGO from the inventory no longer stops after the first removed item. |
| `FastFix.CauldronLure` | `true` | yes | **Cauldron: lure**. Luring when every candidate is dead no longer throws; the lure is skipped. |
| `FastFix.CandeloroRibbon` | `true` | yes | **Candeloro: ribbons**. A ribbon whose end was destroyed no longer throws every frame; it stays where it was until removed. |
| `FastFix.Cloud9Panic` | `true` | yes | **Cloud9: panic outside rooms**. An agent panicking while the escaped cloud is between rooms no longer throws. |
| `FastFix.YaZiEscapeUI` | `true` | yes | **YaZi: breach without request window**. A breach before its request window was ever opened no longer throws. |
| `FastFix.GrunglyLure` | `true` | yes | **Grungly: lure**. A lure whose target no longer qualifies, or with no agent left to lure, no longer throws every tick; the lure stops. |
| `FastFix.BlackBirdPassageHit` | `true` | yes | **Black Bird: hallway hit**. Its hit on the hallway no longer stops when it kills someone there. Needs 12Harmony. |

### Errors of any mod

| Key | Default | Restart | Description |
|---|---|---|---|
| `FastFix.SpecialAttackStuck` | `true` | no | **Special EGO attack stuck (remake weapons)**. EGO weapons with their own Spine attack (kygi123 remakes and similar): when the attack animation never plays, the agent no longer stands still for about 100 s and then walks in battle stance; the attack ends after 1 s. A playing attack is never touched. |
| `FastFix.MecanimMissing` | `true` | yes | **Missing animation clips**. A mod animation clip missing from its skeleton is skipped instead of throwing every frame. |
| `FastFix.ExecOrderAttr` | `true` | yes | **Missing attribute in a mod class**. A mod class with an attribute this game lacks no longer logs an error when added. |
| `FastFix.BufSnapshot` | `true` | yes | **Buffs changed during a hit**. A buff that adds or removes buffs while damage is counted no longer loses the hit. |
| `FastFix.WeaponSplashArmor` | `true` | yes | **Creature splash attacks**. A creature's splash or piercing attack no longer stops at its first target. |
| `FastFix.ChildBufs` | `true` | no | **Effects on killed child creatures**. Mod effects on a killed child creature are removed. |
| `FastFix.NoticeLeaks` | `true` | no | **Old days in game events**. Timers and scripts of earlier days are unsubscribed at day start, so they no longer stay in memory or run every tick. |
| `FastFix.ForeignLeaks` | `true` | no | **Old days in mod lists**. At day start, entries of earlier days are removed from mod lists that never clear them. |
| `FastFix.EffectNames` | `true` | yes | **Same effect names**. Two mods with the same effect folder name each show their own effect. |
| `FastFix.StartTooltip` | `true` | no | **Tooltip at the start of the day**. A tooltip shown at day start is closed. |
| `FastFix.Covariance` | `true` | yes | **List type crash**. A mod built for a newer .NET that passes a list of one unit type where a list of another is expected no longer closes the game without an error. Such places are found in the mod code at start and listed in the log. |
| `FastFix.CovarianceHandlers` | `true` | yes | **List type crash: all methods**. Also fixes such places inside a method with foreach, using or try (for example Krau Deluge). This loads 12Harmony, which then builds the patches of all mods. Needs 12Harmony.dll in the game Managed folder; without it these places stay unfixed. On by default; it also lets the ToyFactory log and Invalidum sound fixes apply. |
| `FastFix.Hint12Harmony` | `true` | no | **12Harmony hint on the title**. Once on the title screen: lists the fixes that are off because 12Harmony is not loaded. Switched off after it was shown. |
| `FastFix.StageEndGuard` | `true` | yes | **Day end guard**. A creature that throws at the end of the day (for example Invalidum with an escaped creature outside the hallways) no longer stops the day end: the result screen still opens and the error is logged once. |

### Module

| Key | Default | Restart | Description |
|---|---|---|---|
| `FastFix.Enabled` | `true` | yes | **Fixes for other mods**. Fixes known errors of installed mods; each fix works only when its mod is loaded. The list appears in Mod fixes after a restart. |

## Interface

### Hotkey

| Key | Default | Restart | Description |
|---|---|---|---|
| `NailUI.HotkeyEnabled` | `true` | no | **Open with a key**. The hotkey opens and closes this window. |
| `NailUI.Hotkey` | `F10 (KeyCode 291)` | no | **Interface key**. Click, then press a key. Esc cancels. Takes effect after Apply. Range 1..322. Key binding. |

### Hints

| Key | Default | Restart | Description |
|---|---|---|---|
| `NailUI.Tooltips` | `true` | no | **Show tooltips**. A description appears when the mouse rests on an option. |
| `NailUI.TooltipDelay` | `0.4` | no | **Tooltip delay**. Seconds before a tooltip appears. Range 0..2. |

### Log window

| Key | Default | Restart | Description |
|---|---|---|---|
| `LogViewer.Enabled` | `true` | yes | **Log window**. Log window, opened by the Logs button on the title and day screens. |
| `LogViewer.MaxLines` | `100000` | no | **Lines kept per tab**. Lines kept per tab; more lines use more memory. Range 10000..500000. |
| `LogViewer.PollMs` | `500` | no | **File check interval (ms)**. Milliseconds between checks for new log lines. Range 100..5000. |
| `LogViewer.HideBenign` | `true` | no | **Hide harmless errors**. Known harmless Unity errors are written as short INFO lines and are not counted. |

### Day screen button

| Key | Default | Restart | Description |
|---|---|---|---|
| `NailUI.ButtonEnabled` | `true` | no | **Day screen button**. Window button in the day screen under the mission panel. |
| `NailUI.ButtonSize` | `20` | no | **Day button size**. Size of the day screen button. Range 12..96. |

### On screen

| Key | Default | Restart | Description |
|---|---|---|---|
| `NailUI.FpsCounter` | `0` | no | **FPS counter**. FPS above the energy bar: simple, with average/min/max, or full. Range 0..3. Choice list. |
| `NailUI.HideAgentTitles` | `false` | no | **Hide agent titles**. Hides the title under agents. Visual only. |
| `NailUI.HideClerkNames` | `false` | no | **Hide clerk names**. Hides clerk names. |

## Experimental

### Hit particle pool

| Key | Default | Restart | Description |
|---|---|---|---|
| `EffectPool.PoolParticles` | `false` | no | **Pool hit particles**. Hit particles are reused. Check hit effects by eye. |
| `EffectPool.MaxParticlePerKey` | `12` | no | **Particles pooled per variant**. Pooled particles per damage type and defense level. 0 = no pooling. Range 0..100. |

### Engine optimizations

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.AgentTexturesExperimental` | `false` | yes | **Agent pictures: one copy**. Agent animation pictures keep one copy in memory. Mods that read them may break. |
| `PerfPack.CreatureTexturesExperimental` | `false` | yes | **Abnormality pictures: one copy**. Abnormality pictures keep one copy in memory from day start. Needs Agent pictures: one copy. |
| `PerfPack.AgentLazyExperimental` | `false` | yes | **Agent animations on first use**. Agent animation pictures load on first use: faster start, a short stutter later. |
| `PerfPack.EffectLazyExperimental` | `false` | yes | **Effect pictures on first show**. Effect pictures are freed at start and load when first shown. |

## Debug

### Logging

| Key | Default | Restart | Description |
|---|---|---|---|
| `Logging` | `true` | no | **Write log**. Info and warnings go to NailOptimization.log. Errors are always written. |
| `PerfPack.QuietLog` | `true` | yes | **Quiet log**. Ordinary log lines are not written; warnings and errors stay. |
| `LogViewer.AdvancedLog` | `false` | yes | **Advanced log**. Own log in the Logs folder instead of Log.txt, with a column of warnings, errors and mod file problems. Needs the log window. |
| `EffectPool.ParticleDump` | `false` | no | **Dump particle prefabs**. Writes the components of hit particle prefabs to the log once. |

### Interface debugging

| Key | Default | Restart | Description |
|---|---|---|---|
| `NailUI.ForceOpen` | `false` | no | **Force open with hotkey**. The hotkey opens this window even during loading and day transitions. Debug only. |

### Performance overlay

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfTools.Enabled` | `true` | yes | **Performance overlay and profiler**. F3 shows FPS, frame time and memory graphs; Shift+F3 records a profiler session to Logs/Profiler.log. |
| `PerfTools.Corner` | `3` | no | **Overlay corner**. 0 top left, 1 top right, 2 bottom left, 3 bottom right. Range 0..3. |
| `PerfTools.Scale` | `1` | no | **Overlay size**. Overlay size multiplier. Range 0.6..2. |
| `PerfTools.LogPerf` | `true` | no | **Write Logs/Perf.log**. While the overlay or profiler runs, one line per second and a SPIKE line per slow frame go to Logs/Perf.log. |
| `PerfTools.SpikeMs` | `50` | no | **Spike threshold (ms)**. A frame this long or longer is logged as SPIKE. Range 20..1000. |

### Profiler

| Key | Default | Restart | Description |
|---|---|---|---|
| `LoadGuard.BootProfile` | `false` | yes | **Boot profile (diagnostic)**. Logs the time of every assembly load until the first frame, to measure how long each mod takes to start. Off for normal play. |
| `PerfTools.ProfSummarySec` | `10` | no | **Summary interval (s)**. Seconds between profiler summary tables. Range 1..120. |
| `PerfTools.ProfMaxSec` | `120` | no | **Session time limit (s)**. A session stops after this many seconds. 0 = no limit. Range 0..3600. |
| `PerfTools.ProfMaxEvents` | `500` | no | **Slow call lines per session**. Most slow-call lines per session. Range 0..20000. |
| `PerfTools.ProfMaxKB` | `1024` | no | **Profiler log size limit (KB)**. Above this size only the final summary is written. Range 64..16384. |

### Test modes

| Key | Default | Restart | Description |
|---|---|---|---|
| `PerfPack.PathCheckVerify` | `false` | yes | **Path check: verify mode**. Runs the game's check too and logs differences; no speed gain. |
| `PerfPack.FastSearchVerify` | `false` | yes | **Fast path search: verify mode**. Runs the game's search too and logs differences; no speed gain. |
| `PerfPack.SkinCacheStats` | `false` | yes | **Skin cache: statistics**. Logs material, texture and mesh counts every 10 s. |

## Advanced

### Configuration file

| Key | Default | Restart | Description |
|---|---|---|---|
| `Config.Save` | `true` | no | **Save changes to the file**. Changes are written to NailOptimization.cfg.txt. Off: they last until the game closes. |
| `Config.WatchSeconds` | `10` | no | **Check file for edits**. Seconds between checks of the config file for outside edits. 0 = never. Range 0..300. |

## Other keys in the config file

Keys that exist in the sources but have no entry in the panel layout. They are shown under Advanced with their raw key if the module declares them, or are read from the file only.

| Key | Default | Range |
|---|---|---|

## Core keys (not in the panel)

Read by the core assembly (`src/NailOptimization.cs`, `CoreSettings.Load`). They appear in the config file only.

| Key | Default | Description |
|---|---|---|
| `Order` | `empty` | Module load order: comma-separated file names (without .dll) loaded first; the rest load by name. |
| `Gui.Hotkey` | `F10` | Panel hotkey (UnityEngine.KeyCode name). |
| `Gui.Prewarm` | `false` | Build the panel and rasterize glyphs at startup instead of on first open. |
| `Gui.LogEnvironment` | `true` | Log a canvas scan on every panel open. Diagnostic. |
| `Gui.OpenTimingFrames` | `12` | Frames of unscaled delta time recorded after each panel open (0 = off, max 32). |
| `Gui.TitleButton` | `true` | Icon button on the title screens that opens the panel. |
| `Gui.TitleIcon` | `Images/config_icon.png` | Title icon PNG, path relative to the NailOptimization folder. |
| `Detect.Names` | `ALot,NZM,SomeOptimizer,some.optimizer,Charming` | Comma-separated substrings of assembly names / Harmony ids reported as third-party optimizers (warning only). |
| `Detect.DumpPatches` | `false` | Write every detected Harmony patch to the log. |
