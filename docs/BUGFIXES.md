# Bug fixes

All fixes belong to the FastFix module (plus a few in PerfPack and LoadGuard where noted) and have their own switch in the panel (sections Bugfixes and Mod fixes) and in the config file. A fix for a specific mod is declared and installed only when that mod is loaded, and it is hidden in the panel otherwise. Defaults and restart flags are in [CONFIG.md](CONFIG.md); all fixes are on by default.

## Vanilla game

| Option | Bug | Fix |
|---|---|---|
| `FastFix.TrainRestart` | Restarting the day while Express Train to Hell departs killed the employees of the reloaded day (they started dead and lost their EGO). | `HellTrainDeparture.CollisionCheck` does nothing while the game state is STOP. |
| `FastFix.VoidDream` | Employees put to sleep by Void Dream could stay frozen and invincible until day end. The cry hit only units before the first downed one. | Each employee is released by his own wake-up animation, or after 8 s if it never plays. The cry loop goes past downed units. |
| `FastFix.OrdealReset` | An ordeal still running when the day ends (retry, return to title) continued in the next day. | The ordeal state is reset at the end of the day. |
| `FastFix.PanicStuck` | A panicking agent sent to open a containment unit stopped after an interrupted walk. | The agent walks on. |
| `FastFix.ExpandOnce` | Cancel after confirming a department expansion left the flag set, so the same expansion could be confirmed again. | `ExpandUI.OnClickCancel` after Confirm keeps the "loading next scene" flag. |
| `FastFix.SelectClick` | A click with a small mouse move was taken for a drag; a drag cut off by switching windows blocked later clicks. | `UnitMouseEventManager.OnPointerClick` clears the drag flag first. |
| `FastFix.CameraDrag` | Dragging the map with the right or middle button froze and jumped while the camera shook. | `CameraMover.LateUpdate`: the drag starts and runs during a recoil shake. |
| `FastFix.BlackSwanReflect` | Black Swan reflected damage to herself endlessly (stack overflow crash). | `BlackSwanSister.CanTakeDamage` no longer reflects to herself. |
| `FastFix.FixerWhiteReflect` | The White fixer reflected damage endlessly while praying (game freeze). | `FixerWhite.CanTakeDamage` no longer reflects to herself. |
| `FastFix.StandingCG` | A story character picture with duplicate face names broke loading. | The picture loads. |
| `FastFix.UnknownEquipCount` | A mod granting equipment that no mod defines stopped the save load and skipped the Sephirah data. | The unknown equipment is counted and ignored, so the load continues. |

## Specific mods

Mod names are as shown in the mod list.

| Option | Mod | Fix |
|---|---|---|
| `FastFix.WhiteRabbit` | Celestial Bunny (WhiteRabbit) | Its normal time mode is turned off; it threw errors every frame and changed animation speeds in its room. |
| `FastFix.RiskPortrait` | FEW and Elysia risk levels | No error every frame for empty abnormality slots on the preparation screen (a placeholder creature is used). |
| `FastFix.ElysiaRiskUnit` | Elysia RiskLevel | No error every frame for a creature with a missing part. |
| `FastFix.Starflare` | Starflare | Suit notes (`CSArmor.CollisionCheck`) and creature movement (`CSCreature.MakeMovement`) no longer throw. |
| `FastFix.Bamboo` | Bamboo-hatted Kim | No double buff ticks, no repeated icon and sound loads, tassel buff fixed. |
| `FastFix.Cauldron` | Cauldron | Minion animation events no longer throw. |
| `FastFix.Chaofeng` | Chaofeng | The slowing wind no longer makes units walk backwards; its damage no longer throws on a kill. |
| `FastFix.OverBossBird` | Over Armor Twilight (OverWeapon BossBird) | Agent deaths no longer log an error. |
| `FastFix.AddRoom` | kekesi AddRoom | The department panel no longer logs a layer error. |
| `FastFix.AcaciaBullet` | Acacia | The bullet check no longer throws when the bullet touches something that is not a unit. |
| `FastFix.KingOfGreedPassage` | KingOfGreedWeapon Enhance | The weapon no longer throws when its wearer is hit between passages. |
| `FastFix.PinelWorkText` | Pinel Luti's Creature | The work text check no longer throws in every containment unit each tick (about 2 ms per frame). |
| `FastFix.ColoredFixer` | Colored Fixer 2.3 | An agent no longer stays stunned and out of control after Kali's EGO ends; EGO change and teleport effects no longer repeat. |
| `FastFix.CensoredChildren` | CENSORED Remake (kygi123) | Dead summoned children are removed after 3 s instead of at day end, so the day no longer slows down with every summon. |
| `FastFix.HellTrain` | Train Ego (Hell Train gift) | A hit is no longer lost when the target has a similar buff. |
| `FastFix.PersephoneGifts` | Persephone | Gifts of a dead agent in the full set vanish with the body instead of hanging in the air. |
| `FastFix.TieIcon` | TieTheTree | The armor buff icon is loaded once instead of from disk for every buff (a lag every 6-7 s). |
| `FastFix.HarmonyLabel` | Harmony (YD25 remake) | The weapon's note staff is hidden while its agent is dead. |
| `PerfPack.HpTrackerLoop` | HP Awareness | No allocations for every employee every frame. |
| `PerfPack.CatLazyExperimental` | ChildHood by JamesGreen1233455 | Cat animations load when a cat first appears instead of at start: faster start and less memory, a short stutter on the first appearance. |
| `FastFix.ForeignLeaks` | heavenLink / STARRRLink / KimLink EGO, GiftStacker, MyFormEmpties | Entries of earlier days are removed from their lists at day start. |
| `FastFix.UnknownEquipCount` | Supernova (ThePeekingBeater) | The save load no longer stops on equipment no mod defines. |

## Errors any mod can have

| Option | Error | Fix |
|---|---|---|
| `FastFix.SpecialAttackStuck` | A special EGO weapon with its own Spine attack whose animation never plays (kygi123 remakes and similar) left the agent standing still for about 100 s. | The attack ends after 1 s through `WorkerUnit.EndAttack`. A playing attack is not cut. |
| `FastFix.MecanimMissing` | A mod animator clip missing from its skeleton threw on every frame. | `MecanimTranslator.Apply` skips the clip. |
| `FastFix.BufSnapshot` | A buff that adds or removes buffs while damage is counted lost the hit. | The buff lists are walked as copies. |
| `FastFix.WeaponSplashArmor` | A creature's splash or piercing attack stopped at its first target. | The attack continues to the other targets. |
| `FastFix.ChildBufs` | Mod effects stayed on a killed child creature. | They are removed. |
| `FastFix.NoticeLeaks` | Timers and scripts of earlier days stayed subscribed to game events, staying in memory and running every tick. | They are unsubscribed at day start. |
| `FastFix.ForeignLeaks` | Mod lists that never clear their entries kept entries of earlier days. | The old entries are removed at day start. |
| `FastFix.EffectNames` | Two mods with the same effect folder name showed the same effect. | Each mod shows its own effect. |
| `FastFix.ExecOrderAttr` | A mod class with an attribute missing from this game logged an exception when added. | The error is avoided and the default order is used. |
| `FastFix.StartTooltip` | A tooltip left open at day start stayed on screen. | It is closed. |
| `LoadGuard.Enabled` | One broken xml or txt file of a mod stopped loading everything after it. | The file or entry is skipped, logged with mod and file name, and the rest loads. |
| `SaveGuard.Enabled` | A save that could not be read completely (for example after a mod was removed) was overwritten by the incomplete version. | The save is backed up and not overwritten. |

## Patch guard

`FastFix.PatchGuard` (restart) is not a fix for one bug. If one foreign Harmony patch method throws the same exception `FastFix.GuardCount` times (default 100) within 10 seconds, FastFix skips that patch method for the rest of the session and logs the owner, the patch and the exception. For a skipped bool Prefix the original method runs.
