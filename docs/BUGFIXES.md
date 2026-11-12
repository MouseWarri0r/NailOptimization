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
| `FastFix.ChildDeleteTwice` | A child creature that is suppressed twice (some mods do it) threw the second time. | No error the second time. |
| `FastFix.ChildElevatorStuck` | A child creature whose path ends on an elevator threw out of its update. | The error no longer escapes the update; it is handled as for other creatures. |
| `FastFix.WeaponReskin` | When a weapon was taken off an agent (for example Paulinus released), `AgentModel.OnReleaseWeapon` did not refresh the agent's weapon view, unlike equipping. The agent kept the old weapon's look and attacks; modded special weapons could deal no damage. | Postfix calls `AgentUnit.OnChangeWeapon()`, the same call the game makes on equip. Checked at run time. |
| `FastFix.CensoredVictimRelease` | When CENSORED escaped while its chosen sacrifice was walking to it, the employee stayed out of control until the day ended and the release ran every frame. | The employee gets control back once. |
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
| `FastFix.BlackBirdMove` | White and Black Bird | A black bird that escapes no longer throws on its first move. |
| `FastFix.PinocchioMove` | LCPD Pinocchio | A puppet that escapes no longer throws on its first move. |
| `FastFix.BoneFishView` | Rotten Whale Fall | A bone fish without its animation no longer throws when it appears. |
| `FastFix.DragonScaleView` | Yin Yang Dragon Scale | The abnormality without its animation no longer throws when the facility loads. |
| `FastFix.EternalFlowerAttack` | Flower of Eternity | A flower attack that lands at the end of the day no longer throws and stops other animation events of that frame. |
| `FastFix.IsomerEffect` | Isomer | The damage effect picture is read from the mod folder (the mod looks for a folder with another name). |
| `FastFix.ToyFactoryLog` | ToyFactory | An error that the mod's notice guard catches is written in full, with its stack, to ToyFactoryError.txt and once per kind to the NailOptimization log. |
| `FastFix.InvalidumSefira` | Invalidum | Lighting the departments back up no longer throws when a creature is outside every passage. |
| `FastFix.InvalidumPath` | Invalidum | Its sounds are read from the mod folder (the mod looks for a folder with another name). |
| `FastFix.PinocchioRange` | LCPD Pinocchio | An attack on a target removed at the end of the day no longer throws. |
| `FastFix.LadyInRainDraw` | Lady in Rain | Drawing stops when its view or painting tool is gone instead of throwing every frame. |
| `FastFix.QiuNiuCircle` | QiuNiu | Its circle no longer throws on units that are not agents, officers, creatures or children. |
| `FastFix.MephistoBuf` | Mephisto | Its debuff on an officer or another non-agent no longer throws when that unit is hit. |
| `FastFix.HookahCaterpillarAnim` | Hookah Caterpillar | Its animation is found even when the mod list lookup fails, instead of the creature staying without animation. |
| `FastFix.WhaleTailPull` | Rotten Whale Fall | Its tail skips the pull while the target is between rooms instead of throwing every frame. |
| `FastFix.CharlotteDamage` | Charlotte | Taking damage between rooms no longer throws. |
| `FastFix.KekesiWolfHit` | Alleyway Watchdog | Its area hit no longer stops when it kills someone in the room. |
| `FastFix.PuLaoChain` | PuLao | Its attack near a music creature no longer repeats itself without end and closes the game; each creature joins the chain once. |
| `FastFix.NothingWorkerNoModel` | It Goes By The Name of Dream | A NothingWorker object that the mod created without initializing it no longer throws an error every frame. |
| `FastFix.PinocchioEscape` | LCPD Pinocchio | Picking its defence target and chasing a target that just died no longer throw every tick. |
| `FastFix.InvalidumFlood` | Invalidum | The flooded passage no longer stops when a unit in it dies or leaves during the check. |
| `FastFix.HomelessBeastHit` | Homeless Beast | Its hit on the whole department no longer stops when it kills someone. |
| `FastFix.YaZiDeath` | YaZi | Suppression without a death animation no longer throws every frame; the creature is hidden as in vanilla. |
| `FastFix.BlackBirdDeath` | Black Bird | Suppression of the black bird without a death animation no longer throws every frame; it goes straight to the pose it takes after that animation. |
| `FastFix.RottenWhaleRoam` | Rotten Whale Fall | Without a target it keeps one roaming destination instead of choosing a new one every tick and turning back and forth. |
| `FastFix.QianRuEquip` | QianRu | Taking back its EGO from the inventory no longer stops after the first removed item. |
| `FastFix.CauldronLure` | Cauldron | Luring when every candidate is dead no longer throws; the lure is skipped. |
| `FastFix.CandeloroRibbon` | Candeloro | A ribbon whose end was destroyed no longer throws every frame; it stays where it was until removed. |
| `FastFix.Cloud9Panic` | Cloud9 | An agent panicking while the escaped cloud is between rooms no longer throws. |
| `FastFix.YaZiEscapeUI` | YaZi | A breach before its request window was ever opened no longer throws. |
| `FastFix.YaZiOnDie` | YaZi | Agent deaths while WhiteNight is in the facility no longer throw from YaZi's death patch (it looked up WhiteNight's script as YaZi). |
| `FastFix.GrunglyLure` | Grungly | A lure whose target no longer qualifies, or with no agent left to lure, no longer throws every tick; the lure stops. |
| `FastFix.BlackBirdPassageHit` | BlackBird_WhiteBird | Its hit on the hallway no longer stops when it kills someone there. Needs 12Harmony. |

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
| `FastFix.Covariance` (experimental, off by default since 0.9.4) | A mod built for a newer .NET passed a list of one unit type where a list of another is expected, and the game closed without an error. | Such places are found in the mod code at start and listed in the log. |
| `FastFix.CovarianceHandlers` | The same kind of place inside a method with foreach, using or try (for example Krau Deluge) stayed unfixed. | Also fixed. This loads 12Harmony, which then builds the patches of all mods. Needs `12Harmony.dll` in the game Managed folder; without it these places stay unfixed. |
| `FastFix.Hint12Harmony` | Fixes that are off because 12Harmony is not loaded were not visible to the player. | Once on the title screen they are listed. Switched off after it was shown. |
| `FastFix.StageEndGuard` | A creature that throws at the end of the day (for example Invalidum with an escaped creature outside the hallways) stopped the day end. | The result screen still opens and the error is logged once. |
| `FastFix.LoopGuard` | A method of another mod (not a patch) threw the same error many times within 10 s. | If it returns nothing, it is skipped until the game closes and logged. Can turn off a feature of that mod. |
| `LoadGuard.Enabled` | One broken xml or txt file of a mod stopped loading everything after it. | The file or entry is skipped, logged with mod and file name, and the rest loads. |
| `SaveGuard.Enabled` | A save that could not be read completely (for example after a mod was removed) was overwritten by the incomplete version. | The save is backed up and not overwritten. |

## Patch guard

`FastFix.PatchGuard` (restart) is not a fix for one bug. If one foreign Harmony patch method throws the same exception `FastFix.GuardCount` times (default 100) within 10 seconds, FastFix skips that patch method for the rest of the session and logs the owner, the patch and the exception. For a skipped bool Prefix the original method runs.
