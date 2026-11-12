// PerfPack: NailOptimization module (api level 3). Ports of the user's standalone optimization mods, one switch per feature.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine. Output goes to <NailOptimization>/modules/.
//
// AnimatorParams (from AnimatorParamCache v2): AnimatorUtil.HasParameter(Animator, string) reads animator.parameters,
// which allocates a new array on every call (about 4800 calls/s on day 49, measured 2026-10-28 with PerfTools).
// The parameter names are cached per controller; each Animator remembers its controller and re-reads it at most once
// per frame. A Unity-null animator runs the original (vanilla throws there). Caches are cleared at day end.
// The feature is not installed when the standalone AnimatorParamCache or a foreign Prefix already owns the method.
//
// SoundPrewarm (from SoundClipCache, behavior-neutral part only): the first Resources.Load of a vanilla sound costs
// 5-40 ms in a fight (measured). Postfixes on SoundEffectPlayer.PlayOnce_Mod / Play_Mod remember each vanilla sound
// (modid empty) that played, keep a reference to its clip (Unity cannot unload it) and list its name in
// PerfPack.sounds.txt; at the next game start the listed clips are loaded in small per-frame steps. The game's own
// lookup (ModAudioClipManager first, then Resources) is never replaced, so mod sound overrides keep working.
//
// CorpseFreeze (agents and clerks part of the CorpseFreeze mod, no Harmony patch): twice a second (unscaled time) the
// agent and clerk lists are scanned; a worker dead for PerfPack.CorpseDelaySeconds of game time gets the per-frame
// components of its unit switched off (Spine SkeletonRenderer, Animator, WorkerSpriteSetter, the unit script,
// AgentSpeech): the body stays drawn as it lies, Unity stops updating it. Only components that were on are switched
// off, and exactly those are switched back on if the worker is alive again. AgentModel.OnDie / OfficerModel.OnDie
// are not patched (they contain a catch, GL-53). Ordeal creatures are not handled.
//
// PanicThrottle (from the PanicThrottle mod): a mass panic starts every panic action in one frame (0.5 s freezes).
// PanicReady.StartPanicAction gets a per-frame time budget; over it, the call is skipped and PanicReady.Execute retries
// it on the next tick. The throttle sits on StartPanicAction (not PanicReadyComplete as in the old mod), so the
// weapon change before it is not repeated by the retries.
// SlotCull (from InventoryLoadPerf): inventory slots outside the scroll view skip their per-frame texture update.
// InventoryTabs: switching on or off one EGO slot costs about 6 ms, 120 slots in all-grades mode cost about a second per
// tab click and per opening. Tabs and the closed window are hidden by a Canvas, slots stay as they are until the day starts.
// FastSearch (same paths as vanilla): GraphAstar.SearchPath allocates a queue, sets, boxed lists and one object per node
// reached; machine noons search on every tick while chasing. Prefix runs the same search over reused arrays, with the same
// heap order and the same quirks. PerfPack.FastSearchVerify runs the original too and logs differences.
// PathCheck (replaces PathfindingPerf and the former FastDistance, same answers as vanilla): MovableObjectNode.
// CheckConnectedInPassage builds a temp node and runs a full A* (about 950 calls/s in a fight, measured 2026-10-30).
// With the target on a node the answer never depends on the search, because SearchPath never returns null; with the
// target on an edge a BFS gives it. PerfPack.PathCheckVerify runs the original too and logs differences.
// IdleSpeed: AgentModel.get_movement allocates on every tick of every agent; the tick of a standing agent does not use it.
// SkinCache (from AgentOptimize and VBlankFF): every animator change clones a texture and a material per armor sprite.
// One texture and material per sprite and settings are shared; VBlankFF destroying "-pma-" textures cannot match the
// "-pmac-" name, and that destruction is what breaks it together with AgentOptimize's shared textures.
// UiRefresh (from NZM): AgentUnitUI.activateUI rewrites the same labels each frame (951 ms of 1030 ms AgentUnit.Update).
// CursorScale (from Reticle): SetCursorScale makes a texture per frame of the aiming animation and never frees it.
// RequireCache: every EGO list opening destroys and rebuilds the requirement plates of all slots (1.6 s of 2.2 s on day 49);
// a slot whose plates already show the same equipment keeps them. InventoryRequireLayout.Init has try/finally (GL-53),
// so the patch sits on its only caller, InventoryAgentSlot.SetRequireInfo.
// MeshRelease: MeshRendererBuffers.Initialize drops its two meshes without destroying them, on every animator change;
// the day-start sweep destroys the unreferenced ones (no Dispose at the change: a mod may still hold the mesh).
// NZM's off-screen unload is not ported: disabling SkeletonAnimator stops AnimationState.Update, so custom-animator
// attacks and animations hang off-screen, and vanilla already updates off-screen units at 1/8. Measured on day 49 at
// full zoom it also gains nothing; the world canvases are what costs (10-12 of 30-50 ms per frame), see CanvasCull.
// CanvasCull: world space canvases (about 330 on day 49: rooms, unit labels, sniping marks) cost even when unchanged.
// Those outside the view are switched off 4 times a second, and at once when the camera jumps.
// DoorAnimCull (experimental, off by default): door and room damage animators get CullCompletely. Their controllers have no
// animation events, but a door changed off-screen plays its stale transition when scrolled to.
// CameraCache (from NZM): Camera.main runs about 200 times per frame on day 49; it is read once per frame instead.
// BonusPool: UnitEquipSpace.GetBonus built about 5 new objects per call, about 6000 calls/s on day 49 (maxHp, maxMental and
// movement read it on every frame and tick), 1.5 MB/s of the 4.7 MB/s heap growth behind the GC spikes (measured 2026-10-30).
// Same sum in the same order into one object per equipment space. Vanilla callers read a field of the result at once.
// SefiraLookup: Dictionary<SefiraEnum, Sefira> boxes its enum key on Mono, about 70 bytes per lookup, about 9400 lookups/s
// on day 49 (0.64 MB/s measured 2026-10-30). The same objects are read from an array; Init (Clear) rebuilds it.
// PauseSkeletons: at timeScale 0 poses cannot change, yet every skeleton recomputes them and rebuilds its mesh each frame, about
// 2.3 ms per frame on day 49 (measured 2026-10-30). Idea: SomeOptimizer, without its reflection per call and full freeze.
// FastFindCreature: mods' movement patches call CreatureManager.FindCreature for every unit every tick, about 185 calls
// per tick and 9 of 11 ms on a day 27 save, 18+ MB/s of garbage (measured 2026-11-01). The original allocates a LcIdLong and
// calls GetModId per creature; the replacement compares metadataId first. GetCreature(long) has the same loop (Blue Star Altar
// calls it up to 3 times per tick, 10 MB per 60 s at 96 creatures, measured 2026-11-03) and gets the same replacement.
// NoticePool: Notice.Send copies the observer list with ToArray on every call, 12 MB per 60 s in a 96-creature breach
// (measured 2026-11-03). The copy goes into a reused array per nesting depth; same snapshot, same order.
// OrchestraWorkers: SilentOrchestra.UniqueEscape builds a new worker list every tick and reads it only every 5 s; the list is reused.
// DescReuse: CreatureInfoWindow.SetDesc destroys and instantiates one Text row per description line on every open of the
// abnormality info window (measured 2026-11-03: about 2 ms of the 11 ms SetDesc, the rest is text layout); the rows are reused.
// FontWarm: the first opens of the info window rebuild the dynamic font atlases for new sizes (76-108 ms in one frame, measured
// 2026-11-03); the sizes the window used are recorded per language and screen height and requested at day start.
// GcQuiet (on by default since 1.48): a full collection in a quiet moment (pause, info window, day end) once the heap grew enough,
// so Boehm's automatic one (177-276 ms on day 49) is pushed later; no patch, a 10 Hz driver.
// BufNameCache: UnitModel.GetUnitBufByName reads GetType().Name for every buff; BloodRose's move patches reach it for every
// unit every step: with 64 clerks 36.7 MB per 30 s fell to 4.3 (measured 2026-11-01). Type names are kept; the body has a foreach (GL-53), so it never runs.
// LcIdCache: EquipmentTypeInfo.GetLcId makes a new LcId per call, 13000 calls/s with 64 clerks; LcId is immutable, so one is kept per info.
// HpTrackerLoop: HP Awareness' HPTrackerUI.ShowElement makes a closure and delegate per agent per frame; replaced by a plain loop.
// AgentStats: AgentModel stat getters build two WorkerPrimaryStatBonus per read (GetPrimaryStatBuf, titleBonus); inside them both go to reused instances (r188).
// UnitLoops: foreach over a List costs 40 B per loop on this Mono (measured 40 and 81 B/call), and UpdateBufState copies the list; these run for every unit every step.
// DistortionGrab: RenderTextureCamera (Dimensional Refraction) draws the whole scene a second time into its own texture every
// frame, 13 ms of a 50 ms frame on day 49 (measured 2026-10-30). The shader gets the previous finished frame instead, copied by
// the GPU. DestroyAll has a handler (GL-53) and is not patched; OnDestroy is. Idea: SomeOptimizer.
// ModSkeletonCache: a runtime asset (mods: no skeletonJSON) parses its skeleton on every GetSkeletonData, so each animator
// change of an agent with a mod weapon costs 10-100 ms. Parsed data is kept, as vanilla assets do. GetSkeletonData has a catch
// (GL-53), so the patch sits on its two loaders.
// WitnessSpread: a death in a crowd computes every witness's horror reaction in one tick; the calls run over the next
// ticks under a time budget with the same inputs. A witness the reaction could panic keeps vanilla timing: its panic
// changes what its own death does to others in the same tick. A witness that died in between is skipped.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Harmony;
using Inventory;
using nightowl.DistortionShaderPack;
using Spine.Unity.Modules.AttachmentTools;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using N = NailOptimization;

namespace PerfPack
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class PerfPackModule : N.ModuleBase
	{
		public const string Id = "PerfPack";
		public const string HarmonyId = "Lobotomy.NailOptimization.PerfPack";

		public override string Name
		{
			get
			{
				return Id;
			}
		}

		public override string Version
		{
			get
			{
				return "1.60";
			}
		}

		// Literal on purpose: a const from Core would be inlined at compile time.
		public override int RequiredApiLevel
		{
			get
			{
				return 9;
			}
		}

		public override string[] Targets
		{
			get
			{
				return new string[] { "AnimatorUtil.HasParameter", "SoundEffectPlayer.PlayOnce_Mod", "SoundEffectPlayer.Play_Mod", "PanicReady.StartPanicAction", "Inventory.InventorySlot.LateUpdate", "MovableObjectNode.CheckConnectedInPassage", "AgentModel.GetMovementValue", "GameManager.FixedUpdate", "GameManager.Update", "AtlasUtilities.ToAtlasRegionPMAClone", "AgentUnitUI.activateUI", "CursorManager.SetCursorScale", "WorkerSpriteManager.ScaleTexture", "Inventory.InventoryAgentSlot.SetRequireInfo", "Spine.Unity.MeshRendererBuffers.Initialize", "AgentModel.HorrorDamageByDead", "Spine.Unity.SkeletonDataAsset.GetSkeletonData_string", "Spine.Unity.SkeletonDataAsset.GetSkeletonData_Byte", "UnitEquipSpace.GetBonus", "SefiraManager.GetSefira", "SefiraManager.Init", "CreatureManager.FindCreature", "CreatureManager.GetCreature", "Notice.Send", "SilentOrchestra.UniqueEscape", "CreatureInfoWindow.SetDesc", "CreatureInfoWindow.set_CurrentMetaIdMod" };
			}
		}

		public override void Init()
		{
			N.Flag enabled = N.Config.BindFlag("PerfPack.Enabled", true, "Optimizations ported from standalone mods; each has its own switch. Read at game start.");
			if (!enabled.On)
			{
				N.ULog.Info(Id, "disabled by PerfPack.Enabled=false");
				return;
			}
			N.Flag anim = N.Config.BindFlag("PerfPack.AnimatorParams", true, "Cache of animator parameter names (replaces the AnimatorParamCache mod). Read at game start.");
			N.Flag sound = N.Config.BindFlag("PerfPack.SoundPrewarm", true, "Sounds played before, also those of mods, are loaded ahead at game start and kept loaded (replaces the prewarm of the SoundClipCache mod). Read at game start.");
			SoundPrewarm.Budget = N.Config.BindInt("PerfPack.SoundPrewarmBudgetMs", 6, 1, 50, 1, "Most milliseconds per frame spent loading listed sounds ahead.");
			SoundPrewarm.KeepMax = N.Config.BindInt("PerfPack.SoundKeepMax", 600, 0, 5000, 50, "Most sounds kept loaded and listed in PerfPack.sounds.txt. 0 = only load the list, keep nothing new.");
			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			int ok = 0;
			int want = 0;
			if (anim.On)
			{
				want++;
				ok += AnimatorParams.Install(harmony);
			}
			if (sound.On)
			{
				want++;
				ok += SoundPrewarm.Install(harmony);
			}
			N.Flag corpse = N.Config.BindFlag("PerfPack.CorpseFreeze", true, "Dead agents and clerks stop updating their animation after a delay (replaces the agent/clerk part of the CorpseFreeze mod). Read at game start.");
			CorpseFreeze.Delay = N.Config.BindFloat("PerfPack.CorpseDelaySeconds", 6f, 0f, 60f, 1f, "Seconds of game time after death before a body is frozen.");
			if (corpse.On)
			{
				want++;
				ok += CorpseFreeze.Install();
			}
			N.Flag panic = N.Config.BindFlag("PerfPack.PanicThrottle", true, "A mass panic is spread over several frames instead of freezing one (replaces the PanicThrottle mod). Read at game start.");
			PanicThrottle.Budget = N.Config.BindInt("PerfPack.PanicBudgetMs", 25, 5, 100, 5, "Most milliseconds per frame spent starting panic actions; the rest start on the next frames.");
			if (panic.On)
			{
				want++;
				ok += PanicThrottle.Install(harmony);
			}
			N.Flag cull = N.Config.BindFlag("PerfPack.SlotCull", true, "Inventory slots outside the visible list skip their per-frame update (replaces that part of InventoryLoadPerf). Read at game start.");
			if (cull.On)
			{
				want++;
				ok += SlotCull.Install(harmony);
			}
			N.Flag path = N.Config.BindFlag("PerfPack.PathCheck", true, "The check whether two units in one room are connected runs without a path search, same answers as the game (replaces PathfindingPerf). Read at game start.");
			PathCheck.Verify = N.Config.BindFlag("PerfPack.PathCheckVerify", false, "Test mode: runs the game's check too and logs every difference; no speed gain. Read at game start.").On;
			if (path.On)
			{
				want++;
				ok += PathCheck.Install(harmony);
			}
			N.Flag fastSearch = N.Config.BindFlag("PerfPack.FastSearch", true, "The path search runs without allocating memory and finds the same paths as the game. Applied at once if it was on at game start.");
			FastSearch.Verify = N.Config.BindFlag("PerfPack.FastSearchVerify", false, "Test mode: runs the game's search too and logs every difference; no speed gain. Read at game start.").On;
			if (fastSearch.On)
			{
				want++;
				FastSearch.Live = fastSearch;
				ok += FastSearch.Install(harmony);
			}
			N.Flag idle = N.Config.BindFlag("PerfPack.IdleSpeed", true, "Standing agents skip the speed calculation on each tick. Read at game start.");
			if (idle.On)
			{
				want++;
				ok += IdleSpeed.Install(harmony);
			}
			N.Flag armorAtlas = N.Config.BindFlag("PerfPack.ArmorAtlas", true, "Mod armor pictures are loaded once and share one texture instead of being read from disk on every use with a texture copy per sprite. Read at game start.");
			N.Flag skin = N.Config.BindFlag("PerfPack.SkinCache", true, "Armor textures and materials are shared between agents instead of created on every animation change (replaces AgentOptimize and VBlankFF). Read at game start.");
			SkinCache.Stats = N.Config.BindFlag("PerfPack.SkinCacheStats", false, "Test mode: logs the counts of Unity materials, textures and meshes every 10 s during animation changes. Read at game start.").On;
			int skinOk = 0;
			if (skin.On)
			{
				want++;
				skinOk = SkinCache.Install(harmony);
				ok += skinOk;
			}
			N.Flag stepCap = N.Config.BindFlag("PerfPack.FrameStepCap", false, "A slow frame runs at most 2-3 game ticks instead of about 16. Much higher fps with very many clerks, but below 20 fps the game itself runs slower than real time. Read at game start.");
			if (stepCap.On)
			{
				want++;
				Time.maximumDeltaTime = 0.05f;
				ok++;
				N.ULog.Info("PerfPack", "FrameStepCap installed (maximumDeltaTime 0.05)");
			}
			N.Flag impostor = N.Config.BindFlag("PerfPack.ClerkImpostor", true, "With 20 or more clerks at a wide zoom, clerks are drawn as still pictures without animation; panicking and dead clerks stay normal. Read at game start.");
			if (impostor.On)
			{
				want++;
				ok += ClerkImpostor.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.RoomScan", true, "Clerks and agents skip looking for enemies in a room that holds only controlled workers. Read at game start.").On)
			{
				want++;
				ok += RoomScan.Install(harmony);
			}
			if (armorAtlas.On)
			{
				want++;
				ok += ArmorAtlas.InstallPlain(harmony);
				if (skinOk > 0)
				{
					want++;
					ok += ArmorAtlas.Install(harmony);
				}
				else
				{
					N.ULog.Info("PerfPack", "ArmorAtlas: GetArmorData_Mod left as is, needs SkinCache");
				}
			}
			N.Flag prewarm = N.Config.BindFlag("PerfPack.SkeletonPrewarm", true, "Skeletons of equipped special weapons load at the start of the day instead of when agents first draw them. Read at game start.");
			if (prewarm.On)
			{
				want++;
				ok += SkeletonPrewarm.Install(harmony);
			}
			N.Flag bubbles = N.Config.BindFlag("PerfPack.DeathBubbleCap", true, "Limits the speech bubble calls per frame when a colleague dies. Read at game start.");
			DeathBubbleCap.Max = N.Config.BindInt("PerfPack.DeathBubbleMax", 8, 1, 60, 1, "Speech bubble calls per frame after a death.");
			if (bubbles.On)
			{
				want++;
				ok += DeathBubbleCap.Install(harmony);
			}
			N.Flag bossText = N.Config.BindFlag("PerfPack.BossTextSpread", true, "Floating boss texts build their letters over several frames instead of all in one. They appear a moment later. Read at game start.");
			if (bossText.On)
			{
				want++;
				ok += BossTextSpread.Install(harmony);
			}
			N.Flag ui = N.Config.BindFlag("PerfPack.UiRefresh", true, "Agent name and title labels refresh a few times per second instead of every frame. Read at game start.");
			UiRefresh.Hz = N.Config.BindInt("PerfPack.UiRefreshHz", 4, 1, 60, 1, "Refreshes per second of each agent's name and title labels.");
			if (ui.On)
			{
				want++;
				ok += UiRefresh.Install(harmony);
			}
			N.Flag cursor = N.Config.BindFlag("PerfPack.CursorScale", true, "The aiming cursor animation frees its previous texture (replaces the Reticle mod). Read at game start.");
			if (cursor.On)
			{
				want++;
				ok += CursorScale.Install(harmony);
			}
			N.Flag require = N.Config.BindFlag("PerfPack.RequireCache", true, "The EGO list keeps its requirement plates between openings instead of rebuilding them. Read at game start.");
			if (require.On)
			{
				want++;
				ok += RequireCache.Install(harmony);
			}
			N.Flag meshes = N.Config.BindFlag("PerfPack.MeshRelease", true, "At each day start, character meshes that nothing uses any more are destroyed. Read at game start.");
			if (meshes.On)
			{
				want++;
				ok += MeshRelease.Install(harmony);
			}
			N.Flag canvases = N.Config.BindFlag("PerfPack.CanvasCull", true, "World interface canvases outside the view are switched off. Read at game start.");
			CanvasCull.AgentUiZoom = N.Config.BindFloat("PerfPack.AgentUiHideZoom", 60f, 0f, 90f, 1f, "Camera size from which the bars, grade, title, buffs and speech above employees are hidden (needs CanvasCull). 0 = never.");
			if (canvases.On)
			{
				want++;
				ok += CanvasCull.Install(harmony);
			}
			OrdealUiCull.HideZoom = N.Config.BindFloat("PerfPack.OrdealUiHideZoom", 14f, 0f, 60f, 1f, "Camera size from which the health bars and names of ordeal units are hidden when 20 or more units are out. 0 = never.");
			want++;
			ok += OrdealUiCull.Install(harmony);
			N.Flag doors = N.Config.BindFlag("PerfPack.DoorAnimCull", true, "Door and room damage animators stop while they are outside the view. Read at game start.");
			if (doors.On)
			{
				want++;
				ok += DoorAnimCull.Install(harmony);
			}
			N.Flag camera = N.Config.BindFlag("PerfPack.CameraCache", true, "The main camera is looked up once per frame instead of about 200 times. Read at game start.");
			if (camera.On)
			{
				want++;
				ok += CameraCache.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.CameraRange", true, "The camera reads its zoom range level once instead of rebuilding the list of open departments several times per frame. Read at game start.").On)
			{
				want++;
				ok += CameraRange.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.OrdealIdle", true, "The ordeal update is skipped while no ordeal is running, instead of creating a list every tick. Read at game start.").On)
			{
				want++;
				ok += OrdealIdle.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.MouseLists", true, "The check of what is under the mouse cursor reuses its lists instead of creating five new ones every frame. Read at game start.").On)
			{
				want++;
				ok += MouseLists.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.OptionSave", true, "Dragging a mod option slider writes the options file once, 0.5 s after the last change, instead of on every step. Read at game start.").On)
			{
				want++;
				ok += OptionSave.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.AttachMiss", true, "A gift sprite that no mod provides is looked up on disk once instead of in every mod folder on each appearance update. Read at game start.").On)
			{
				want++;
				ok += AttachMiss.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.PortraitCache", true, "Vanilla abnormality portraits and missing custom portraits are looked up once instead of on every call. Read at game start.").On)
			{
				want++;
				ok += PortraitCache.Install(harmony);
			}
			N.Flag bonusPool = N.Config.BindFlag("PerfPack.BonusPool", true, "Equipment and gift bonuses are summed without creating new objects, so garbage collection pauses come less often. Read at game start.");
			if (bonusPool.On)
			{
				want++;
				ok += BonusPool.Install(harmony);
			}
			N.Flag sefiraLookup = N.Config.BindFlag("PerfPack.SefiraLookup", true, "Departments are found through an array, so looking one up creates no garbage. Read at game start.");
			if (sefiraLookup.On)
			{
				want++;
				ok += SefiraLookup.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.ScriptTypes", true, "Weapons and gifts find their mod script through a list built once instead of scanning the types of every mod each time. Read at game start.").On)
			{
				want++;
				ScriptTypes.WeaponModScript = N.Config.BindFlag("PerfPack.WeaponModScript", true, "With ScriptTypes on: a mod weapon whose script class has no game class of the same name gets its script (the game gives it none, so its special effect never runs).");
				ok += ScriptTypes.Install(harmony);
			}
			N.Flag pauseSkeletons = N.Config.BindFlag("PerfPack.PauseSkeletons", true, "While the game is paused, characters refresh their pose rarely instead of every frame; cutscenes are not affected.");
			want++;
			ok += PauseSkeletons.Install(harmony, pauseSkeletons);
			N.Flag agentAnimLod = N.Config.BindFlag("PerfPack.AgentAnimLod", true, "Zoomed out, agents refresh their animation one step less often than the game already does.");
			want++;
			ok += AgentAnimLod.Install(harmony, agentAnimLod);
			N.Flag inventoryDefer = N.Config.BindFlag("PerfPack.InventoryDefer", true, "The EGO list is built a little at a time on the preparation screen instead of during the day load.");
			want++;
			InventoryDefer.ZayinFirst = N.Config.BindFlag("PerfPack.InventoryZayinFirst", false, "The EGO list opens on ZAYIN each day instead of all grades; clicking ZAYIN again shows all.");
			ok += InventoryDefer.Install(harmony, inventoryDefer);
			if (N.Config.BindFlag("PerfPack.InventoryTabs", true, "EGO tabs and the closed EGO window are hidden without switching off every slot. Read at game start.").On)
			{
				want++;
				ok += InventoryTabs.Install(harmony);
			}
			N.Flag fastScale = N.Config.BindFlag("PerfPack.FastScale", true, "Custom worker sprites are scaled on the video card at game start, and their full-size sources are freed. Read at game start.");
			if (fastScale.On)
			{
				want++;
				ok += FastScale.Install(harmony);
			}
			N.Flag distortion = N.Config.BindFlag("PerfPack.DistortionGrab", true, "Dimensional Refraction bends the previous finished frame instead of drawing the whole scene a second time.");
			want++;
			ok += DistortionGrab.Install(harmony, distortion);
			N.Flag findCreature = N.Config.BindFlag("PerfPack.FastFindCreature", true, "Searching for an abnormality by id does not allocate. Mods call it for every unit on every tick.");
			want++;
			ok += FastFindCreature.Install(harmony, findCreature);
			N.Flag noticePool = N.Config.BindFlag("PerfPack.NoticePool", true, "Game notices to many listeners no longer copy the listener list on every send.");
			want++;
			ok += NoticePool.Install(harmony, noticePool);
			N.Flag descReuse = N.Config.BindFlag("PerfPack.DescReuse", true, "The abnormality info window reuses its description lines instead of creating them again on every open.");
			want++;
			ok += DescReuse.Install(harmony, descReuse);
			N.Flag fontWarm = N.Config.BindFlag("PerfPack.FontWarm", true, "Text sizes of the abnormality info window are prepared in the font atlases at day start, so its first opens do not stall.");
			want++;
			ok += FontWarm.Install(harmony, fontWarm);
			N.Flag orchestra = N.Config.BindFlag("PerfPack.OrchestraWorkers", true, "The Silent Orchestra reuses its list of employees instead of building a new one every tick. Read at game start.");
			if (orchestra.On)
			{
				want++;
				ok += OrchestraWorkers.Install(harmony);
			}
			N.Flag bufNames = N.Config.BindFlag("PerfPack.BufNameCache", true, "Searching for a buff by name does not allocate. Mods call it for every unit on every step. Read at game start.");
			if (bufNames.On)
			{
				want++;
				ok += BufNameCache.Install(harmony);
			}
			N.Flag lcIds = N.Config.BindFlag("PerfPack.LcIdCache", true, "Equipment ids are not created again on every request. Read at game start.");
			if (lcIds.On)
			{
				want++;
				ok += LcIdCache.Install(harmony);
			}
			N.Flag hpLoop = N.Config.BindFlag("PerfPack.HpTrackerLoop", true, "HP Awareness does not allocate memory for every employee every frame. Read at game start.");
			if (hpLoop.On)
			{
				want++;
				ok += HpTrackerLoop.Install(harmony);
			}
			N.Flag clerkCull = N.Config.BindFlag("PerfPack.ClerkCull", true, "Calm clerks outside the view stop animating and drawing; clerks in panic, in combat or on a task stay normal. Applied at once; clerks stay normal while ClerkImpostor is on.");
			N.Flag agentCull = N.Config.BindFlag("PerfPack.AgentCull", true, "Calm agents outside the view stop animating and drawing; agents in panic, in combat or at work stay normal. Applied at once.");
			want++;
			ok += ClerkCull.Install(harmony, clerkCull, agentCull, !impostor.On);
			N.Flag advanced = N.Config.BindFlag("LogViewer.AdvancedLog", false, "Own log in Logs/ next to NailOptimization.dll instead of BaseMod Log.txt (5 files kept), a column of warnings, errors and mod file diagnostics, an alert badge on the Logs buttons. Needs LogViewer.Enabled. Read at game start.");
			N.Flag quietLog = N.Config.BindFlag("PerfPack.QuietLog", true, "Ordinary game and BaseMod log lines are not written; warnings and errors stay. Does nothing while the advanced log is on. Read at game start.");
			if (quietLog.On && !advanced.On)
			{
				want++;
				ok += QuietLog.Install(harmony);
			}
			else if (quietLog.On)
			{
				N.ULog.Info("PerfPack", "QuietLog off: LogViewer.AdvancedLog is on");
			}
			N.Flag unitLoops = N.Config.BindFlag("PerfPack.UnitLoops", true, "Buff and gift loops of every unit do not allocate memory. Read at game start.");
			if (unitLoops.On)
			{
				want++;
				ok += UnitLoops.Install(harmony);
			}
			N.Flag agentStats = N.Config.BindFlag("PerfPack.AgentStats", true, "Employee stat reads (health, sanity, speed and the others, read many times per frame) do not allocate memory. Read at game start.");
			if (agentStats.On)
			{
				want++;
				ok += AgentStats.Install(harmony);
			}
			N.Flag modSkeleton = N.Config.BindFlag("PerfPack.ModSkeletonCache", true, "Skeletons of mod weapons and animations are parsed once instead of on every animation change. Read at game start.");
			if (modSkeleton.On)
			{
				want++;
				ok += ModSkeletonCache.Install(harmony);
			}
			N.Flag witness = N.Config.BindFlag("PerfPack.WitnessSpread", true, "When a colleague dies, witnesses react over the next few ticks instead of all in one. Same damage, a moment later. Read at game start.");
			WitnessSpread.Budget = N.Config.BindInt("PerfPack.WitnessBudgetMs", 2, 1, 20, 1, "Most milliseconds per tick spent on witness reactions.");
			if (witness.On)
			{
				want++;
				ok += WitnessSpread.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.EffectTextures", true, "Mod effect pictures keep one copy in memory instead of two, and identical pictures are shared. Read at game start.").On)
			{
				if (N.Config.BindFlag("PerfPack.EffectLazyExperimental", false, "EXPERIMENTAL. Mod effect pictures are freed at game start and load again when an effect is first shown. Less memory, a short stutter on first use. Read at game start.").On)
				{
					want++;
					ok += LazyPictures.InstallEffects(harmony);
				}
				want++;
				ok += EffectTextures.Install();
			}
			if (N.Config.BindFlag("PerfPack.AgentTexturesExperimental", false, "EXPERIMENTAL. Agent and cat animation pictures keep one copy in memory instead of two. Mods that read these pictures may break. Read at game start.").On)
			{
				want++;
				ok += AgentTextures.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.AgentLazyExperimental", false, "EXPERIMENTAL. Agent animation pictures load when an agent first uses them, not at game start. Faster start, less memory, a short stutter on first use. Read at game start.").On)
			{
				want++;
				ok += AgentLazy.Install(harmony);
			}
			if (N.Config.BindFlag("PerfPack.CreatureTexturesExperimental", false, "EXPERIMENTAL. Pictures of abnormalities in the facility keep one copy in memory instead of two, from day start. Needs AgentTexturesExperimental. Mods that read these pictures may break. Read at game start.").On)
			{
				want++;
				ok += CreatureTextures.Install(harmony);
			}
			if (LazyPictures.ChildHoodPresent() && N.Config.BindFlag("PerfPack.CatLazyExperimental", true, "EXPERIMENTAL. ChildHood cat animations load when a cat first appears, not at game start. Faster start, less memory, a short stutter on first use. Read at game start.").On)
			{
				want++;
				ok += LazyPictures.InstallCats(harmony);
			}
			if (N.Config.BindFlag("PerfPack.SkipIntroLogos", false, "The two logo screens before the title are skipped (about 12 s). Restart required.").On)
			{
				want++;
				ok += IntroSkip.Install(harmony);
			}
			GcQuiet.Flag = N.Config.BindFlag("PerfPack.GcQuiet", true, "Memory is cleaned while the game is paused, an abnormality info window is open, or the day ends, so the cleaning pause during play comes later or not at all. Helps players who pause often.");
			GcQuiet.GrowMb = N.Config.BindInt("PerfPack.GcQuietGrowMb", 48, 8, 256, 8, "Megabytes of memory growth since the last cleaning before a quiet moment cleans.");
			want++;
			ok += GcQuiet.Install();
			if (N.Config.BindFlag("PerfPack.TimerNoGc", true, "Some abnormalities (1.76 MHz, Snow Queen, Queen Bee and others) force a full memory cleaning when their timer ends, a pause at day end, restart and during play. The forced cleaning is skipped; memory is cleaned as usual. Read at game start.").On)
			{
				want++;
				ok += TimerNoGc.Install(harmony);
			}
			N.ULog.Info(Id, "PerfPack 1.10 installed " + ok + "/" + want + " features");
		}
	}

	internal static class Owners
	{
		// Name of a foreign owner of any patch on 'm', or null.
		internal static string ForeignAny(MethodBase m)
		{
			try
			{
				Patches ps = PatchProcessor.IsPatched(m);
				if (ps == null)
				{
					return null;
				}
				string owner = ForeignIn(ps.Prefixes);
				if (owner == null)
				{
					owner = ForeignIn(ps.Postfixes);
				}
				return owner != null ? owner : ForeignIn(ps.Transpilers);
			}
			catch (Exception e)
			{
				N.ULog.Warn(PerfPackModule.Id, "patch info of " + m.Name + " unreadable: " + e.Message);
			}
			return null;
		}

		private static string ForeignIn(IEnumerable<Patch> patches)
		{
			foreach (Patch p in patches)
			{
				if (p.owner == null || !p.owner.StartsWith("Lobotomy.NailOptimization.", StringComparison.Ordinal))
				{
					return p.owner == null ? "?" : p.owner;
				}
			}
			return null;
		}

		// Name of a foreign owner already patching 'm' with a Postfix or a Transpiler, or null.
		internal static string ForeignPostOrTrans(MethodBase m)
		{
			try
			{
				Patches ps = PatchProcessor.IsPatched(m);
				if (ps == null)
				{
					return null;
				}
				string owner = ForeignIn(ps.Postfixes);
				return owner != null ? owner : ForeignIn(ps.Transpilers);
			}
			catch (Exception e)
			{
				N.ULog.Warn(PerfPackModule.Id, "patch info of " + m.Name + " unreadable: " + e.Message);
			}
			return null;
		}

		// Name of a foreign owner already patching 'm' with a Prefix, or null.
		internal static string ForeignPrefix(MethodBase m)
		{
			try
			{
				Patches ps = PatchProcessor.IsPatched(m);
				if (ps == null)
				{
					return null;
				}
				foreach (Patch p in ps.Prefixes)
				{
					if (p.owner == null || !p.owner.StartsWith("Lobotomy.NailOptimization.", StringComparison.Ordinal))
					{
						return p.owner == null ? "?" : p.owner;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(PerfPackModule.Id, "patch info of " + m.Name + " unreadable: " + e.Message);
			}
			return null;
		}
	}

	// ---------- AnimatorParams ----------
	internal sealed class AnimEntry
	{
		internal RuntimeAnimatorController Controller;
		internal HashSet<string> Names;
		internal int CheckedFrame;
	}

	public static class AnimatorParams
	{
		private const string Tag = "PerfPack";
		private const int MaxAnimators = 8192;
		private static readonly Dictionary<int, AnimEntry> _byAnimator = new Dictionary<int, AnimEntry>();
		private static readonly Dictionary<int, HashSet<string>> _byController = new Dictionary<int, HashSet<string>>();
		private static N.Counter _hits;
		private static N.Counter _reads;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			if (!object.ReferenceEquals(N.TypeCache.Resolve("AnimatorParamCache.Harmony_Patch"), null))
			{
				N.ULog.Warn(Tag, "AnimatorParams not installed: the standalone AnimatorParamCache mod is loaded (disable one of them)");
				return 0;
			}
			Type t = N.TypeCache.Resolve("AnimatorUtil");
			MethodBase m = N.PatchUtil.Resolve(Tag, t, "HasParameter", new Type[] { typeof(Animator), typeof(string) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPrefix(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "AnimatorParams not installed: AnimatorUtil.HasParameter already has a Prefix by " + owner + " (GL-01)");
				return 0;
			}
			_hits = N.Counters.Get(Tag, "animParamHits");
			_reads = N.Counters.Get(Tag, "animParamReads");
			N.ResetRegistry.Register(new AnimResetter());
			HarmonyMethod pre = new HarmonyMethod(typeof(AnimatorParams).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			if (!N.PatchUtil.Install(harmony, Tag, m, pre, null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "AnimatorParams installed (AnimatorUtil.HasParameter cached per controller)");
			return 1;
		}

		public static bool Prefix(Animator animator, string name, ref bool __result)
		{
			try
			{
				if (animator == null)
				{
					return true;
				}
				int id = animator.GetInstanceID();
				int frame = Time.frameCount;
				AnimEntry e;
				if (!_byAnimator.TryGetValue(id, out e))
				{
					if (_byAnimator.Count >= MaxAnimators)
					{
						Clear();
					}
					e = new AnimEntry();
					e.CheckedFrame = -1;
					_byAnimator[id] = e;
				}
				if (e.CheckedFrame != frame)
				{
					e.CheckedFrame = frame;
					RuntimeAnimatorController c = animator.runtimeAnimatorController;
					if (e.Names == null || !object.ReferenceEquals(c, e.Controller))
					{
						bool keep;
						HashSet<string> s = Names(animator, c, out keep);
						e.Controller = c;
						e.Names = keep ? s : null;
						__result = name != null && s.Contains(name);
						_hits.Value++;
						return false;
					}
				}
				if (e.Names == null)
				{
					// Only reachable when the same frame already read an uncached set; read it again.
					bool keep2;
					__result = name != null && Names(animator, animator.runtimeAnimatorController, out keep2).Contains(name);
					_hits.Value++;
					return false;
				}
				__result = name != null && e.Names.Contains(name);
				_hits.Value++;
				return false;
			}
			catch (Exception ex)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "AnimatorParams prefix failed, original used for this call (logged once): " + ex);
				}
				return true;
			}
		}

		private static HashSet<string> Names(Animator animator, RuntimeAnimatorController c, out bool keep)
		{
			int key = c == null ? 0 : c.GetInstanceID();
			HashSet<string> set;
			if (key != 0 && _byController.TryGetValue(key, out set))
			{
				keep = true;
				return set;
			}
			set = new HashSet<string>(StringComparer.Ordinal);
			AnimatorControllerParameter[] ps = animator.parameters;
			for (int i = 0; i < ps.Length; i++)
			{
				set.Add(ps[i].name);
			}
			_reads.Value++;
			// An empty set from a not yet initialized Animator would hide the controller's params until the reset.
			keep = ps.Length > 0 && animator.isInitialized;
			if (key != 0 && keep)
			{
				_byController[key] = set;
			}
			return set;
		}

		internal static void Clear()
		{
			_byAnimator.Clear();
			_byController.Clear();
		}
	}

	internal sealed class AnimResetter : N.IResettable
	{
		public void ResetState()
		{
			AnimatorParams.Clear();
		}
	}

	// ---------- SkeletonPrewarm ----------
	// The first draw of a special weapon parses its skeleton inside SkeletonRenderer.Initialize (20-110 ms per type),
	// all at once when a crowd starts fighting. Assets with a skeletonJSON cache the result, so they parse on the first playing tick instead.
	public static class SkeletonPrewarm
	{
		private const string Tag = "PerfPack";
		private static bool _armed = true;
		private static bool _pumped;
		private static int _next;
		private static float _pumpMs;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(GameManager), "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, tick, new HarmonyMethod(typeof(SkeletonPrewarm).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "SkeletonPrewarm installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			if (__instance.state == GameState.STOP)
			{
				_armed = true;
				// ManageStarted stays true from StartGame until the next preparation: the day-end STOP must not use up the pump.
				if (!_pumped && !__instance.ManageStarted)
				{
					Pump();
				}
			}
			else if (_armed && __instance.state == GameState.PLAYING)
			{
				_armed = false;
				_pumped = false;
				_next = 0;
				_pumpMs = 0f;
				Run();
			}
		}

		// Preparation screen: one agent per tick, so the parse never stacks into one frame.
		private static void Pump()
		{
			try
			{
				IList<AgentModel> agents = AgentManager.instance.GetAgentList();
				if (agents.Count == 0)
				{
					return;
				}
				if (_next >= agents.Count)
				{
					_pumped = true;
					return;
				}
				float start = Time.realtimeSinceStartup;
				AgentModel agent = agents[_next];
				_next++;
				LoadSafe(agent);
				_pumpMs += Time.realtimeSinceStartup - start;
				if (_next >= agents.Count)
				{
					_pumped = true;
					N.ULog.Info(Tag, "SkeletonPrewarm prepare: " + agents.Count + " agents, " + (int)(_pumpMs * 1000f) + " ms");
				}
			}
			catch (Exception e)
			{
				_pumped = true;
				N.ULog.Warn(Tag, "SkeletonPrewarm prepare failed: " + e);
			}
		}

		// Day start: catches whatever preparation missed or changed; parsed skeletons return from their cache.
		private static void Run()
		{
			try
			{
				float start = Time.realtimeSinceStartup;
				int loaded = 0;
				IList<AgentModel> agents = AgentManager.instance.GetAgentList();
				for (int i = 0; i < agents.Count; i++)
				{
					if (LoadSafe(agents[i]))
					{
						loaded++;
					}
				}
				N.ULog.Info(Tag, "SkeletonPrewarm: " + loaded + " agents, " + (int)((Time.realtimeSinceStartup - start) * 1000f) + " ms");
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "SkeletonPrewarm failed: " + e);
			}
		}

		// One agent's failure (a mod weapon with broken data) skips that agent only.
		private static int _loadErrors;

		private static bool LoadSafe(AgentModel agent)
		{
			try
			{
				return Load(agent);
			}
			catch (Exception e)
			{
				_loadErrors++;
				if (_loadErrors <= 5)
				{
					// Full trace for the first one only; later ones are counted in the line below.
					N.ULog.Warn(Tag, "SkeletonPrewarm skipped agent " + (agent == null ? "null" : agent.GetUnitName()) + " (" + _loadErrors + "/5 logged): "
						+ (_loadErrors == 1 ? e.ToString() : e.Message));
				}
				else if (_loadErrors % 50 == 0)
				{
					N.ULog.Warn(Tag, "SkeletonPrewarm: " + _loadErrors + " agents skipped so far this session");
				}
				return false;
			}
		}

		private static bool Load(AgentModel agent)
		{
			WeaponModel weapon = agent.Equipment.weapon;
			if (weapon == null)
			{
				return false;
			}
			LoadEffect(weapon);
			if (weapon.metaInfo.weaponClassType != WeaponClassType.SPECIAL)
			{
				return false;
			}
			WorkerSpine.WorkerSpineAnimatorData data;
			LobotomyBaseMod.KeyValuePairSS name = new LobotomyBaseMod.KeyValuePairSS(EquipmentTypeInfo.GetLcId(weapon.metaInfo).packageId, weapon.metaInfo.specialWeaponAnim);
			if (!WorkerSpine.WorkerSpineAnimatorManager.instance.GetDataWithCheck_Mod(name, out data) || data == null)
			{
				return false;
			}
			if (data.IsLoaded && data.skeletonData != null)
			{
				data.skeletonData.GetSkeletonData(false);
				return true;
			}
			return false;
		}

		// Persephone's weapon creates this effect on the owner's first room change: 310-340 ms in one tick.
		private static bool _effectLoaded;
		private static void LoadEffect(WeaponModel weapon)
		{
			if (_effectLoaded || weapon.script == null || weapon.script.GetType().FullName != "PersephoneCreature.PersephoneWeapon")
			{
				return;
			}
			Spine.Unity.SkeletonDataAsset asset;
			if (Add_On.instance != null && Add_On.instance.EffectList != null && Add_On.instance.EffectList.TryGetValue("PersephoneCurtainAnim", out asset) && asset != null)
			{
				_effectLoaded = true;
				float start = Time.realtimeSinceStartup;
				GameObject go = Spine.Unity.SkeletonAnimation.NewSkeletonAnimationGameObject(asset).gameObject;
				go.SetActive(false);
				UnityEngine.Object.Destroy(go);
				N.ULog.Info(Tag, "SkeletonPrewarm effect: " + (int)((Time.realtimeSinceStartup - start) * 1000f) + " ms");
			}
		}
	}

	// ---------- DeathBubbleCap ----------
	// A death in a crowd calls HorrorDamageByDead for every witness; each bubble lays out its text in the same tick.
	public static class DeathBubbleCap
	{
		private const string Tag = "PerfPack";
		internal static N.IntParam Max;
		private static int _frame = -1;
		private static int _count;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(AgentUnit), "SpeechOtherdeadLyric", new Type[] { typeof(int), typeof(string) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(DeathBubbleCap).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "DeathBubbleCap installed");
			return 1;
		}

		public static bool Prefix()
		{
			int frame = Time.frameCount;
			if (frame != _frame)
			{
				_frame = frame;
				_count = 0;
			}
			_count++;
			return _count <= Max.Value;
		}
	}

	// ---------- BossTextSpread ----------
	// SefiraBossBase.FixedUpdate makes a SefiraBossDescUI every few seconds on every boss day; InitText clones about
	// 100 Text + Outline letters in one tick (45-100 ms). Letters are cloned here under a frame budget, then the
	// original InitText runs with an empty text for its timers and positions, and receives the finished letters.
	public static class BossTextSpread
	{
		private const string Tag = "PerfPack";
		private const float BudgetSeconds = 0.003f;
		private const int MaxJobs = 8;

		private sealed class Job
		{
			public SefiraBossDescUI Ui;
			public string Text;
			public int Index;
			public bool PrevIsSpace = true;
			public bool Captured;
			public float EnableTime = -1f;
			public float ActivateTime = -1f;
			public readonly List<SefiraBossDescUI.TextUnit> Letters = new List<SefiraBossDescUI.TextUnit>();
		}

		private static readonly List<Job> _jobs = new List<Job>();
		private static bool _finishing;
		private static FieldInfo _textList;
		private static FieldInfo _enableTimer;
		private static FieldInfo _activateTimer;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			BindingFlags inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			_textList = typeof(SefiraBossDescUI).GetField("textList", inst);
			_enableTimer = typeof(SefiraBossDescUI).GetField("_enableTimer", inst);
			_activateTimer = typeof(SefiraBossDescUI).GetField("_activateTimer", inst);
			MethodBase init = N.PatchUtil.Resolve(Tag, typeof(SefiraBossDescUI), "InitText", Type.EmptyTypes);
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(_textList, null) || object.ReferenceEquals(_enableTimer, null) || object.ReferenceEquals(_activateTimer, null)
				|| object.ReferenceEquals(init, null) || object.ReferenceEquals(update, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, update, Method("UpdatePrefix"), null) || !N.PatchUtil.Install(harmony, Tag, init, Method("InitPrefix"), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "BossTextSpread installed");
			return 1;
		}

		private static HarmonyMethod Method(string name)
		{
			return new HarmonyMethod(typeof(BossTextSpread).GetMethod(name, BindingFlags.Public | BindingFlags.Static));
		}

		public static bool InitPrefix(SefiraBossDescUI __instance)
		{
			if (_finishing || _jobs.Count >= MaxJobs)
			{
				return true;
			}
			try
			{
				Job job = new Job();
				job.Ui = __instance;
				job.Text = __instance.text;
				_jobs.Add(job);
				return false;
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "BossTextSpread queue failed: " + e);
				return true;
			}
		}

		public static void UpdatePrefix()
		{
			if (_jobs.Count == 0)
			{
				return;
			}
			Job job = _jobs[0];
			try
			{
				for (int i = 0; i < _jobs.Count; i++)
				{
					if (!_jobs[i].Captured)
					{
						Capture(_jobs[i]);
					}
				}
				if (job.Ui == null)
				{
					_jobs.RemoveAt(0);
					return;
				}
				float start = Time.realtimeSinceStartup;
				while (job.Index < job.Text.Length)
				{
					AddLetter(job, job.Text[job.Index++]);
					if (Time.realtimeSinceStartup - start >= BudgetSeconds)
					{
						return;
					}
				}
				_jobs.RemoveAt(0);
				Finish(job);
			}
			catch (Exception e)
			{
				_jobs.Remove(job);
				_finishing = false;
				N.ULog.Warn(Tag, "BossTextSpread failed: " + e);
				if (job.Ui != null)
				{
					if (job.Ui.baseScript != null)
					{
						job.Ui.baseScript.OnRemoveDesc(job.Ui);
					}
					UnityEngine.Object.Destroy(job.Ui.gameObject);
				}
			}
		}

		// The caller's SetEnableTime/SetActivateTime run right after the queued InitText. Their durations are kept and the
		// timers stopped, so they cannot expire (and close the empty UI) while the letters are built.
		private static void Capture(Job job)
		{
			job.Captured = true;
			if (job.Ui == null)
			{
				return;
			}
			UnscaledTimer enable = (UnscaledTimer)_enableTimer.GetValue(job.Ui);
			UnscaledTimer activate = (UnscaledTimer)_activateTimer.GetValue(job.Ui);
			if (enable != null && enable.started)
			{
				job.EnableTime = enable.maxTime;
				enable.StopTimer();
			}
			if (activate != null && activate.started)
			{
				job.ActivateTime = activate.maxTime;
				activate.StopTimer();
			}
		}

		// Same steps as the loop of SefiraBossDescUI.InitText.
		private static void AddLetter(Job job, char c)
		{
			if (c == ' ')
			{
				job.PrevIsSpace = true;
				return;
			}
			GameObject letter = UnityEngine.Object.Instantiate<GameObject>(job.Ui.refText.gameObject);
			letter.name = "Text :" + c;
			letter.SetActive(true);
			Text text = letter.GetComponent<Text>();
			SefiraBossDescUI.TextUnit unit = new SefiraBossDescUI.TextUnit();
			unit.text = text;
			unit.outline = letter.GetComponent<Outline>();
			unit.prevIsSpace = job.PrevIsSpace;
			job.PrevIsSpace = false;
			text.text = c.ToString();
			letter.transform.SetParent(job.Ui.transform);
			job.Letters.Add(unit);
			letter.SetActive(false);
		}

		private static void Finish(Job job)
		{
			_finishing = true;
			job.Ui.text = string.Empty;
			job.Ui.InitText();
			job.Ui.text = job.Text;
			_finishing = false;
			// InitText restarted both timers with the prefab values; the caller's durations go back.
			if (job.EnableTime >= 0f)
			{
				((UnscaledTimer)_enableTimer.GetValue(job.Ui)).StartTimer(job.EnableTime);
			}
			if (job.ActivateTime >= 0f)
			{
				((UnscaledTimer)_activateTimer.GetValue(job.Ui)).StartTimer(job.ActivateTime);
			}
			((List<SefiraBossDescUI.TextUnit>)_textList.GetValue(job.Ui)).AddRange(job.Letters);
			job.Ui.SetList();
		}
	}

	// ---------- CanvasCull ----------
	// Amber midnight: 86 units. Their health bars and names (EscapeUIRoot) cost about 4-13 ms (r117) and cannot be read at far zoom.
	// Only those graphics go: the body is a SkeletonGraphic on the same canvas, and the canvas also takes the clicks.
	public static class OrdealUiCull
	{
		private const string Tag = "PerfPack";
		private const float Interval = 0.25f;
		private const int MinUnits = 20;
		internal static N.FloatParam HideZoom;
		private static readonly HashSet<OrdealCreatureModel> _seen = new HashSet<OrdealCreatureModel>();
		private static readonly List<Graphic> _graphics = new List<Graphic>();
		private static readonly HashSet<Graphic> _hidden = new HashSet<Graphic>();
		private static float _next;
		private static bool _failed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(update, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, update, new HarmonyMethod(typeof(OrdealUiCull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "OrdealUiCull installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			try
			{
				if (__instance.state == GameState.STOP)
				{
					if (_seen.Count > 0)
					{
						Show();
						_seen.Clear();
						_graphics.Clear();
					}
					return;
				}
				float now = Time.realtimeSinceStartup;
				if (now < _next)
				{
					return;
				}
				_next = now + Interval;
				Camera cam = Camera.main;
				OrdealManager manager = OrdealManager.instance;
				if (cam == null || manager == null)
				{
					return;
				}
				float zoom = HideZoom.Value;
				if (zoom <= 0f || cam.orthographicSize < zoom)
				{
					Show();
					return;
				}
				OrdealCreatureModel[] units = manager.GetOrdealCreatureList();
				if (units.Length < MinUnits)
				{
					Show();
					return;
				}
				for (int i = 0; i < units.Length; i++)
				{
					CreatureUnit unit = units[i].Unit;
					if (unit != null && unit.escapeUIRoot != null && _seen.Add(units[i]))
					{
						_graphics.AddRange(unit.escapeUIRoot.GetComponentsInChildren<Graphic>(true));
					}
				}
				for (int i = _graphics.Count - 1; i >= 0; i--)
				{
					Graphic g = _graphics[i];
					if (g == null)
					{
						_graphics.RemoveAt(i);
					}
					else if (g.enabled)
					{
						g.enabled = false;
						_hidden.Add(g);
					}
				}
			}
			catch (Exception e)
			{
				if (!_failed)
				{
					_failed = true;
					N.ULog.Warn(Tag, "OrdealUiCull failed: " + e.Message);
				}
			}
		}

		private static void Show()
		{
			if (_hidden.Count == 0)
			{
				return;
			}
			foreach (Graphic g in _hidden)
			{
				if (g != null)
				{
					g.enabled = true;
				}
			}
			_hidden.Clear();
		}
	}

	public static class CanvasCull
	{
		private const string Tag = "PerfPack";
		private const float Interval = 0.25f;
		private const float ListInterval = 30f;
		// Children overflow their canvas (speech bubbles, labels), so the view gets a fixed margin in world units too.
		private const float MarginWorld = 6f;
		private const float MarginView = 1.2f;
		private static Canvas[] _list = new Canvas[0];
		// true where _list[i] is an employee's AgentUnit.uiRoot: hidden at camera size >= AgentUiZoom (r203/r204: about 3 ms at size 90)
		private static bool[] _agentUi = new bool[0];
		internal static N.FloatParam AgentUiZoom;
		private static readonly HashSet<Canvas> _hidden = new HashSet<Canvas>();
		private static readonly Vector3[] _corners = new Vector3[4];
		private static float _nextTick;
		private static float _nextList;
		private static Vector3 _lastPos;
		private static float _lastSize;
		private static bool _failed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(update, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, update, new HarmonyMethod(typeof(CanvasCull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "CanvasCull installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			try
			{
				if (__instance.state == GameState.STOP)
				{
					if (_hidden.Count > 0 || _list.Length > 0)
					{
						Restore();
					}
					return;
				}
				Camera cam = Camera.main;
				if (cam == null)
				{
					return;
				}
				float now = Time.realtimeSinceStartup;
				Vector3 pos = cam.transform.position;
				float size = cam.orthographicSize;
				float jump = size * 0.2f;
				if (now < _nextTick && Mathf.Abs(pos.x - _lastPos.x) < jump && Mathf.Abs(pos.y - _lastPos.y) < jump && Mathf.Abs(size - _lastSize) < jump)
				{
					return;
				}
				_nextTick = now + Interval;
				_lastPos = pos;
				_lastSize = size;
				if (now >= _nextList)
				{
					_nextList = now + ListInterval;
					Refresh(cam);
				}
				Tick(pos, size, cam.aspect);
			}
			catch (Exception e)
			{
				if (!_failed)
				{
					_failed = true;
					N.ULog.Warn(Tag, "CanvasCull failed: " + e.Message);
				}
			}
		}

		private static void Refresh(Camera cam)
		{
			List<Canvas> list = new List<Canvas>();
			foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
			{
				// A layer the main camera does not draw belongs to another camera, which still needs it.
				if (c.isRootCanvas && c.renderMode == RenderMode.WorldSpace && (cam.cullingMask & (1 << c.gameObject.layer)) != 0 && !_hidden.Contains(c))
				{
					list.Add(c);
				}
			}
			// FindObjectsOfType misses a hidden canvas whose object is inactive now; it must still come back in view.
			foreach (Canvas c in _hidden)
			{
				if (c != null)
				{
					list.Add(c);
				}
			}
			_list = list.ToArray();
			_agentUi = new bool[_list.Length];
			for (int i = 0; i < _list.Length; i++)
			{
				if (_list[i] != null)
				{
					AgentUnit unit = _list[i].GetComponentInParent<AgentUnit>();
					_agentUi[i] = unit != null && unit.uiRoot == _list[i];
				}
			}
		}

		private static void Tick(Vector3 pos, float size, float aspect)
		{
			float h = size * MarginView + MarginWorld;
			float w = size * aspect * MarginView + MarginWorld;
			float lod = object.ReferenceEquals(AgentUiZoom, null) ? 0f : AgentUiZoom.Value;
			bool lodHide = lod > 0f && size >= lod;
			for (int i = 0; i < _list.Length; i++)
			{
				Canvas c = _list[i];
				if (c == null)
				{
					continue;
				}
				((RectTransform)c.transform).GetWorldCorners(_corners);
				bool visible = _corners[2].x >= pos.x - w && _corners[0].x <= pos.x + w && _corners[2].y >= pos.y - h && _corners[0].y <= pos.y + h;
				if (lodHide && i < _agentUi.Length && _agentUi[i])
				{
					visible = false;
				}
				if (visible)
				{
					if (_hidden.Remove(c))
					{
						c.enabled = true;
					}
				}
				else if (c.enabled)
				{
					c.enabled = false;
					_hidden.Add(c);
				}
			}
		}

		private static void Restore()
		{
			foreach (Canvas c in _hidden)
			{
				if (c != null)
				{
					c.enabled = true;
				}
			}
			_hidden.Clear();
			_list = new Canvas[0];
			_nextList = 0f;
		}
	}

	// ---------- DoorAnimCull ----------
	public static class DoorAnimCull
	{
		private const string Tag = "PerfPack";
		private static bool _armed = true;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(GameManager), "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, tick, new HarmonyMethod(typeof(DoorAnimCull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "DoorAnimCull installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			if (__instance.state == GameState.STOP)
			{
				_armed = true;
			}
			else if (_armed && __instance.state == GameState.PLAYING)
			{
				_armed = false;
				Run();
			}
		}

		private static void Run()
		{
			try
			{
				int count = 0;
				foreach (Animator a in UnityEngine.Object.FindObjectsOfType<Animator>())
				{
					RuntimeAnimatorController c = a.runtimeAnimatorController;
					if (c != null && (c.name == "IsolateDmgCtrl" || c.name.StartsWith("PassageDoor", StringComparison.Ordinal)) && a.GetComponentInChildren<Renderer>() != null)
					{
						a.cullingMode = AnimatorCullingMode.CullCompletely;
						count++;
					}
				}
				N.ULog.Info(Tag, "DoorAnimCull: " + count + " animators");
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "DoorAnimCull failed: " + e.Message);
			}
		}
	}

	// ---------- OrdealIdle ----------
	// OrdealManager.OnFixedUpdate runs three foreach loops and allocates a new _removedOrdeals list every tick, also when no
	// ordeal is running. Prefix: when the creature, active and removed lists are all empty the loops would do nothing and
	// UpdateOrdealUI is empty, so the original is skipped. Not installed when another mod patches either method.
	public static class OrdealIdle
	{
		private const string Tag = "PerfPack";
		private delegate List<OrdealCreatureModel> CreaturesGetter(OrdealManager m);
		private delegate List<OrdealBase> OrdealsGetter(OrdealManager m);
		private static CreaturesGetter _creatures;
		private static OrdealsGetter _active;
		private static OrdealsGetter _removed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
			FieldInfo creatures = typeof(OrdealManager).GetField("ordealCreatureList", f);
			FieldInfo active = typeof(OrdealManager).GetField("_activatedOrdeals", f);
			FieldInfo removed = typeof(OrdealManager).GetField("_removedOrdeals", f);
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(OrdealManager), "OnFixedUpdate", Type.EmptyTypes);
			MethodBase ui = typeof(OrdealManager).GetMethod("UpdateOrdealUI", f);
			if (object.ReferenceEquals(creatures, null) || object.ReferenceEquals(active, null) || object.ReferenceEquals(removed, null)
				|| !object.ReferenceEquals(creatures.FieldType, typeof(List<OrdealCreatureModel>)) || !object.ReferenceEquals(active.FieldType, typeof(List<OrdealBase>))
				|| !object.ReferenceEquals(removed.FieldType, typeof(List<OrdealBase>)) || object.ReferenceEquals(target, null) || object.ReferenceEquals(ui, null))
			{
				N.ULog.Warn(Tag, "OrdealIdle not installed: OrdealManager members not found");
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner == null)
			{
				owner = Owners.ForeignAny(ui);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "OrdealIdle not installed: " + owner + " patches OrdealManager");
				return 0;
			}
			_creatures = (CreaturesGetter)Getter(creatures, typeof(CreaturesGetter));
			_active = (OrdealsGetter)Getter(active, typeof(OrdealsGetter));
			_removed = (OrdealsGetter)Getter(removed, typeof(OrdealsGetter));
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(OrdealIdle).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "OrdealIdle installed");
			return 1;
		}

		private static Delegate Getter(FieldInfo field, Type delegateType)
		{
			DynamicMethod get = new DynamicMethod("OrdealIdle_" + field.Name, field.FieldType, new Type[] { typeof(OrdealManager) }, typeof(OrdealManager), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return get.CreateDelegate(delegateType);
		}

		public static bool Prefix(OrdealManager __instance)
		{
			List<OrdealCreatureModel> c = _creatures(__instance);
			if (c == null || c.Count != 0)
			{
				return true;
			}
			List<OrdealBase> a = _active(__instance);
			if (a == null || a.Count != 0)
			{
				return true;
			}
			List<OrdealBase> r = _removed(__instance);
			return r == null || r.Count != 0;
		}
	}

	// ---------- OptionSave ----------
	// BaseMod's ModOptionManager.SetSliderValue calls SaveData (serialize every mod's options and rewrite ModOptionData.dat) on
	// every onValueChanged of a mod option slider, i.e. many times per second while dragging. The save asked for by a slider
	// change is deferred until 0.5 s after the last change (and flushed when the game quits); toggles and every other
	// SaveData call write at once, as in BaseMod, and drop a pending deferred save (they write the same data).
	public static class OptionSave
	{
		private const string Tag = "PerfPack";
		private static bool _slider;
		private static LobotomyBaseMod.ModOptionManager _pending;
		private static float _due;
		private static bool _errorLogged;
		private static int _sliderFrame;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type t = typeof(LobotomyBaseMod.ModOptionManager);
			MethodBase set = N.PatchUtil.Resolve(Tag, t, "SetSliderValue", new Type[] { typeof(string), typeof(string), typeof(float) });
			MethodBase save = N.PatchUtil.Resolve(Tag, t, "SaveData", Type.EmptyTypes);
			if (object.ReferenceEquals(set, null) || object.ReferenceEquals(save, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(save);
			if (owner == null)
			{
				owner = Owners.ForeignAny(set);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "OptionSave not installed: " + owner + " patches ModOptionManager");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, set, new HarmonyMethod(typeof(OptionSave).GetMethod("SetPrefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(OptionSave).GetMethod("SetPostfix", BindingFlags.Public | BindingFlags.Static)))
				|| !N.PatchUtil.Install(harmony, Tag, save, new HarmonyMethod(typeof(OptionSave).GetMethod("SavePrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			GameObject go = new GameObject("NailOptimization.PerfPack.OptionSave");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<OptionSaveDriver>();
			N.ULog.Info(Tag, "OptionSave installed");
			return 1;
		}

		public static void SetPrefix()
		{
			_slider = true;
			_sliderFrame = Time.frameCount;
		}

		public static void SetPostfix()
		{
			_slider = false;
		}

		public static bool SavePrefix(LobotomyBaseMod.ModOptionManager __instance)
		{
			bool slider = _slider && _sliderFrame == Time.frameCount;
			_slider = false;
			if (slider)
			{
				_pending = __instance;
				_due = Time.unscaledTime + 0.5f;
				return false;
			}
			_pending = null;
			return true;
		}

		internal static void Flush(bool now)
		{
			LobotomyBaseMod.ModOptionManager p = _pending;
			if (p == null || (!now && Time.unscaledTime < _due))
			{
				return;
			}
			_pending = null;
			try
			{
				p.SaveData();
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(Tag, "OptionSave: deferred save of the mod options failed (logged once): " + e);
				}
			}
		}
	}

	public class OptionSaveDriver : MonoBehaviour
	{
		public void Update()
		{
			OptionSave.Flush(false);
		}

		public void OnApplicationQuit()
		{
			OptionSave.Flush(true);
		}
	}

	// ---------- AttachMiss ----------
	// WorkerSpriteManager.GetAttachmentSprite: for a gift sprite name that is not in the region's database it allocates a
	// Texture2D (never freed) and scans every mod folder (Directory.Exists, GetFiles twice); when no mod has the file it
	// returns null and remembers nothing, so the next call scans again. Postfix: a null result whose name is still not in the
	// database is remembered per region and database object; Prefix: a remembered name returns null at once (the result the
	// scan gives) while the region's database is the same object, otherwise the region's names are forgotten.
	public static class AttachMiss
	{
		private const string Tag = "PerfPack";
		private static readonly Dictionary<int, HashSet<string>> _missing = new Dictionary<int, HashSet<string>>();
		private static readonly Dictionary<int, WorkerSprite.WorkerAttachmentSprite.AttachDatabase> _dbs = new Dictionary<int, WorkerSprite.WorkerAttachmentSprite.AttachDatabase>();
		private static int _count;
		private static bool _errorLogged;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "GetAttachmentSprite", new Type[] { typeof(EGOgiftAttachRegion), typeof(string) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "AttachMiss not installed: " + owner + " patches WorkerSpriteManager.GetAttachmentSprite");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(AttachMiss).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(AttachMiss).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static))))
			{
				return 0;
			}
			N.ULog.Info(Tag, "AttachMiss installed");
			return 1;
		}

		public static bool Prefix(WorkerSpriteManager __instance, EGOgiftAttachRegion region, string name, ref Sprite __result)
		{
			HashSet<string> set;
			if (_count == 0 || name == null || !_missing.TryGetValue((int)region, out set) || !set.Contains(name))
			{
				return true;
			}
			WorkerSprite.WorkerAttachmentSprite.AttachDatabase kept;
			if (!_dbs.TryGetValue((int)region, out kept) || !object.ReferenceEquals(Db(__instance, region), kept))
			{
				set.Clear();
				return true;
			}
			__result = null;
			return false;
		}

		private static WorkerSprite.WorkerAttachmentSprite.AttachDatabase Db(WorkerSpriteManager m, EGOgiftAttachRegion region)
		{
			WorkerSprite.WorkerEquipmentSprite equip;
			if (m.equipData == null || !m.equipData.GetData(WorkerSprite.EquipmentSpriteRegion.ATTACHMENTS, out equip))
			{
				return null;
			}
			WorkerSprite.WorkerAttachmentSprite attach = equip as WorkerSprite.WorkerAttachmentSprite;
			return attach == null ? null : attach.GetDb((int)region);
		}

		public static void Postfix(WorkerSpriteManager __instance, EGOgiftAttachRegion region, string name, Sprite __result)
		{
			if (__result != null || name == null)
			{
				return;
			}
			try
			{
				WorkerSprite.WorkerAttachmentSprite.AttachDatabase db = Db(__instance, region);
				Sprite found;
				if (db == null || db.GetSprite(name, out found))
				{
					return;
				}
				HashSet<string> set;
				if (!_missing.TryGetValue((int)region, out set))
				{
					set = new HashSet<string>(StringComparer.Ordinal);
					_missing[(int)region] = set;
				}
				WorkerSprite.WorkerAttachmentSprite.AttachDatabase kept;
				if (!_dbs.TryGetValue((int)region, out kept) || !object.ReferenceEquals(kept, db))
				{
					set.Clear();
					_dbs[(int)region] = db;
				}
				if (set.Add(name))
				{
					_count++;
					if (_count == 1)
					{
						N.ULog.Info(Tag, "AttachMiss: first missing gift sprite remembered: " + region + "/" + name);
					}
				}
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(Tag, "AttachMiss failed (logged once): " + e);
				}
			}
		}
	}

	// ---------- PortraitCache ----------
	// Add_On.GetPortrait_Mod caches only mod portraits (PortraitsMod). A vanilla portrait runs Resources.Load twice on every call
	// (AuthorNote, then the portrait), and a "Custom/" name that no mod has runs File.Exists in every mod folder, on every
	// call (creature info, codex, work allocation, SefiraPanel refresh). Postfix: results of those two paths (null included)
	// are kept per (modid, portraitSrc); Prefix returns a kept result. Mod artwork results are not kept (BaseMod caches them
	// itself, and a miss there may load later); a kept sprite that was destroyed is dropped and the original runs again.
	// Everything kept is dropped at each scene load (so the scene unload can free the textures) and whenever the count of
	// Add_On.PortraitsMod changes (the original checks that dictionary first).
	public static class PortraitCache
	{
		private const string Tag = "PerfPack";
		private static readonly Dictionary<string, Dictionary<string, Sprite>> _kept = new Dictionary<string, Dictionary<string, Sprite>>(StringComparer.Ordinal);
		private static bool _logged;
		private static int _modCount;
		private static Func<object> _portraitsMod;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(Add_On), "GetPortrait_Mod", new Type[] { typeof(string), typeof(string) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "PortraitCache not installed: " + owner + " patches Add_On.GetPortrait_Mod");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(PortraitCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(PortraitCache).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static))))
			{
				return 0;
			}
			FieldInfo pm = typeof(Add_On).GetField("PortraitsMod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (object.ReferenceEquals(pm, null))
			{
				N.ULog.Warn(Tag, "PortraitCache not installed: Add_On.PortraitsMod not found");
				return 0;
			}
			DynamicMethod get = new DynamicMethod("PortraitCache_PortraitsMod", typeof(object), Type.EmptyTypes, typeof(Add_On), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldsfld, pm);
			il.Emit(OpCodes.Ret);
			_portraitsMod = (Func<object>)get.CreateDelegate(typeof(Func<object>));
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
			N.ULog.Info(Tag, "PortraitCache installed");
			return 1;
		}

		private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			_kept.Clear();
		}

		public static bool Prefix(string modid, string portraitSrc, ref Sprite __result)
		{
			if (modid == null || portraitSrc == null)
			{
				return true;
			}
			System.Collections.ICollection mods = _portraitsMod == null ? null : _portraitsMod() as System.Collections.ICollection;
			int modCount = mods == null ? 0 : mods.Count;
			if (modCount != _modCount)
			{
				_modCount = modCount;
				_kept.Clear();
				return true;
			}
			Dictionary<string, Sprite> byName;
			Sprite s;
			if (!_kept.TryGetValue(modid, out byName) || !byName.TryGetValue(portraitSrc, out s))
			{
				return true;
			}
			if (!object.ReferenceEquals(s, null) && s == null)
			{
				byName.Remove(portraitSrc);
				return true;
			}
			__result = s;
			return false;
		}

		public static void Postfix(string modid, string portraitSrc, Sprite __result)
		{
			if (modid == null || portraitSrc == null)
			{
				return;
			}
			bool custom = portraitSrc.StartsWith("Custom/", StringComparison.Ordinal) || portraitSrc == "Custom";
			if (modid.Length != 0 && !custom)
			{
				return;
			}
			if (custom && __result != null)
			{
				return;
			}
			Dictionary<string, Sprite> byName;
			if (!_kept.TryGetValue(modid, out byName))
			{
				byName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
				_kept[modid] = byName;
			}
			byName[portraitSrc] = __result;
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(Tag, "PortraitCache: first portrait kept: " + (modid.Length == 0 ? "" : modid + ":") + portraitSrc + (__result == null ? " (missing)" : ""));
			}
		}
	}

	// ---------- MouseLists ----------
	// UnitMouseEventManager.UpdatePointEntered runs every frame while the cursor is over the facility: it allocates four
	// List<UnitMouseEventTarget> (only their first element is read, none leaves the method) and the collider array of
	// Physics2D.OverlapPointAll. Transpiler: the four lists come from a static set, cleared on take; the collider query
	// fills a static buffer and returns one cached array of the same length. The first 200 queries are also run through
	// OverlapPointAll and compared element by element; a difference switches back to OverlapPointAll for the session.
	public static class MouseLists
	{
		private const string Tag = "PerfPack";
		private static readonly List<UnitMouseEventTarget>[] _lists = new List<UnitMouseEventTarget>[]
		{
			new List<UnitMouseEventTarget>(), new List<UnitMouseEventTarget>(), new List<UnitMouseEventTarget>(), new List<UnitMouseEventTarget>()
		};
		private static int _next;
		private static Collider2D[] _buf = new Collider2D[16];
		private static Collider2D[] _last;
		private static int _verify = 200;
		private static bool _useAll;
		private static int _lists0;
		private static int _queries0;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(UnitMouseEventManager), "UpdatePointEntered", Type.EmptyTypes);
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "MouseLists not installed: " + owner + " patches UnitMouseEventManager.UpdatePointEntered");
				return 0;
			}
			try
			{
				harmony.Patch(target, null, null, new HarmonyMethod(typeof(MouseLists).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "MouseLists not installed: " + e.Message);
				return 0;
			}
			if (_lists0 != 4 || _queries0 != 1)
			{
				N.ULog.Warn(Tag, "MouseLists: method body differs (" + _lists0 + " lists, " + _queries0 + " queries), left as in the game");
				return 0;
			}
			N.ULog.Info(Tag, "MouseLists installed");
			return 1;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			ConstructorInfo ctor = typeof(List<UnitMouseEventTarget>).GetConstructor(Type.EmptyTypes);
			MethodInfo take = typeof(MouseLists).GetMethod("Take", BindingFlags.Public | BindingFlags.Static);
			MethodInfo query = typeof(MouseLists).GetMethod("Query", BindingFlags.Public | BindingFlags.Static);
			List<int> news = new List<int>();
			List<int> calls = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].opcode == OpCodes.Newobj && object.ReferenceEquals(list[i].operand, ctor))
				{
					news.Add(i);
				}
				else if (list[i].opcode == OpCodes.Call && IsOverlapAll(list[i].operand as MethodInfo))
				{
					calls.Add(i);
				}
			}
			_lists0 = news.Count;
			_queries0 = calls.Count;
			if (news.Count != 4 || calls.Count != 1)
			{
				return list;
			}
			for (int i = 0; i < news.Count; i++)
			{
				list[news[i]].opcode = OpCodes.Call;
				list[news[i]].operand = take;
			}
			list[calls[0]].operand = query;
			return list;
		}

		private static bool IsOverlapAll(MethodInfo m)
		{
			if (object.ReferenceEquals(m, null) || m.Name != "OverlapPointAll" || !object.ReferenceEquals(m.DeclaringType, typeof(Physics2D)))
			{
				return false;
			}
			ParameterInfo[] p = m.GetParameters();
			return p.Length == 1 && object.ReferenceEquals(p[0].ParameterType, typeof(Vector2));
		}

		public static List<UnitMouseEventTarget> Take()
		{
			List<UnitMouseEventTarget> l = _lists[_next & 3];
			_next++;
			l.Clear();
			return l;
		}

		public static Collider2D[] Query(Vector2 point)
		{
			if (_useAll)
			{
				return Physics2D.OverlapPointAll(point, Physics2D.DefaultRaycastLayers, float.NegativeInfinity, float.PositiveInfinity);
			}
			int n = Physics2D.OverlapPointNonAlloc(point, _buf, Physics2D.DefaultRaycastLayers, float.NegativeInfinity, float.PositiveInfinity);
			while (n >= _buf.Length)
			{
				_buf = new Collider2D[_buf.Length * 2];
				n = Physics2D.OverlapPointNonAlloc(point, _buf, Physics2D.DefaultRaycastLayers, float.NegativeInfinity, float.PositiveInfinity);
			}
			if (_last == null || _last.Length != n)
			{
				_last = new Collider2D[n];
			}
			Array.Copy(_buf, _last, n);
			Array.Clear(_buf, 0, n);
			if (_verify > 0)
			{
				_verify--;
				Collider2D[] all = Physics2D.OverlapPointAll(point, Physics2D.DefaultRaycastLayers, float.NegativeInfinity, float.PositiveInfinity);
				bool same = all.Length == n;
				for (int i = 0; same && i < n; i++)
				{
					same = object.ReferenceEquals(all[i], _last[i]);
				}
				if (!same)
				{
					_useAll = true;
					N.ULog.Warn(Tag, "MouseLists: OverlapPointNonAlloc order differs from OverlapPointAll (" + n + " vs " + all.Length + "), the game's query is used");
					return all;
				}
				if (_verify == 0)
				{
					N.ULog.Info(Tag, "MouseLists: 200 collider queries matched OverlapPointAll");
				}
			}
			return _last;
		}
	}

	// ---------- CameraRange ----------
	// CameraMover.currentRangeLevel calls PlayerModel.GetOpenedAreaCount (a new List over all departments plus ToArray) on every
	// read, though the value is used only while _currentRangeLevel is -1. CameraPos (set every frame) reads it five times
	// through GetMovementClamped. Prefix: a cached level is returned as is; -1 runs the original, which computes and stores it.
	public static class CameraRange
	{
		private const string Tag = "PerfPack";
		private delegate int LevelGetter(CameraMover cam);
		private static LevelGetter _get;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			FieldInfo field = typeof(CameraMover).GetField("_currentRangeLevel", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(CameraMover), "get_currentRangeLevel", Type.EmptyTypes);
			if (object.ReferenceEquals(field, null) || !object.ReferenceEquals(field.FieldType, typeof(int)) || object.ReferenceEquals(target, null))
			{
				N.ULog.Warn(Tag, "CameraRange not installed: CameraMover._currentRangeLevel not found");
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "CameraRange not installed: " + owner + " patches CameraMover.currentRangeLevel");
				return 0;
			}
			DynamicMethod get = new DynamicMethod("CameraRange_Level", typeof(int), new Type[] { typeof(CameraMover) }, typeof(CameraMover), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			_get = (LevelGetter)get.CreateDelegate(typeof(LevelGetter));
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(CameraRange).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "CameraRange installed");
			return 1;
		}

		public static bool Prefix(CameraMover __instance, ref int __result)
		{
			int level = _get(__instance);
			if (level == -1)
			{
				return true;
			}
			__result = level;
			return false;
		}
	}

	// ---------- CameraCache ----------
	public static class CameraCache
	{
		private const string Tag = "PerfPack";
		private static int _frame = -1;
		private static Camera _camera;
		private static readonly string[] Targets = new string[]
		{
			"WorkerUnit.UpdateCheckInCamera", "AgentUnit.UpdateAnimationQuality", "OfficerUnit.UpdateAnimationQuality",
			"PlatformerCamera.FixedUpdate", "TempCamera.FixedUpdate", "CameraElevator.Update", "DarkFilterController.Update",
			"TextMeshOutline.LateUpdate", "RotateAngle.FixedUpdate", "TextAppearNormalEffect.UpdateState",
			"TextAppearEffect.FixedUpdate", "UnitMouseEventManager.UpdateDrag"
		};

		public static Camera Main()
		{
			int frame = Time.frameCount;
			if (frame != _frame || _camera == null)
			{
				_frame = frame;
				_camera = Camera.main;
			}
			return _camera;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			HarmonyMethod transpiler = new HarmonyMethod(typeof(CameraCache).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			int count = 0;
			foreach (string target in Targets)
			{
				try
				{
					int dot = target.LastIndexOf('.');
					Type type = AccessTools.TypeByName(target.Substring(0, dot));
					MethodInfo method = object.ReferenceEquals(type, null) ? null : AccessTools.Method(type, target.Substring(dot + 1));
					if (object.ReferenceEquals(method, null))
					{
						continue;
					}
					// Harmony 1.0.9.1 drops exception handlers of a patched method (GL-53).
					if (method.GetMethodBody() == null || method.GetMethodBody().ExceptionHandlingClauses.Count > 0)
					{
						continue;
					}
					harmony.Patch(method, null, null, transpiler);
					count++;
				}
				catch (Exception e)
				{
					N.ULog.Warn(Tag, "CameraCache " + target + ": " + e.Message);
				}
			}
			N.ULog.Info(Tag, "CameraCache installed on " + count + " methods");
			return count > 0 ? 1 : 0;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo main = typeof(CameraCache).GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo called = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(called, null) && called.Name == "get_main" && object.ReferenceEquals(called.DeclaringType, typeof(Camera)))
				{
					list[i].operand = main;
				}
			}
			return list;
		}
	}

	// ---------- TimerNoGc ----------
	// AutoTimer.Destroy(timer) is only GC.Collect(): the timer is not touched, so skipping the body changes nothing but the forced collection.
	public static class TimerNoGc
	{
		private static bool _logged;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			try
			{
				MethodInfo m = typeof(AutoTimer).GetMethod("Destroy", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(AutoTimer) }, null);
				if (object.ReferenceEquals(m, null))
				{
					N.ULog.Warn(PerfPackModule.Id, "TimerNoGc not applied: AutoTimer.Destroy not found");
					return 0;
				}
				string owner = Owners.ForeignAny(m);
				if (owner != null)
				{
					N.ULog.Warn(PerfPackModule.Id, "TimerNoGc not applied: AutoTimer.Destroy is patched by " + owner);
					return 0;
				}
				harmony.Patch(m, new HarmonyMethod(typeof(TimerNoGc).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null, null);
				N.ULog.Info(PerfPackModule.Id, "TimerNoGc installed");
				return 1;
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfPackModule.Id, "TimerNoGc install failed: " + e);
				return 0;
			}
		}

		public static bool Prefix()
		{
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(PerfPackModule.Id, "TimerNoGc: forced collection of AutoTimer.Destroy skipped (logged once)");
			}
			return false;
		}
	}

	// ---------- BonusPool ----------
	public static class BonusPool
	{
		private const string Tag = "PerfPack";
		private const int MaxSpaces = 256;
		private static int _depth;
		private static readonly Dictionary<UnitEquipSpace, EGObonusInfo> _results = new Dictionary<UnitEquipSpace, EGObonusInfo>();

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(UnitEquipSpace), "GetBonus", new Type[] { typeof(UnitModel) });
			MethodBase gifts = N.PatchUtil.Resolve(Tag, typeof(UnitEGOgiftSpace), "GetBonus", new Type[] { typeof(UnitModel) });
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(gifts, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner == null)
			{
				owner = Owners.ForeignAny(gifts);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "BonusPool not installed: " + owner + " patches GetBonus");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(BonusPool).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ResetRegistry.Register(new BonusPoolResetter());
			N.ULog.Info(Tag, "BonusPool installed");
			return 1;
		}

		internal static void Clear()
		{
			_results.Clear();
		}

		private static void Add(EGObonusInfo sum, EGObonusInfo b)
		{
			sum.hp += b.hp;
			sum.mental += b.mental;
			sum.cubeSpeed += b.cubeSpeed;
			sum.workProb += b.workProb;
			sum.movement += b.movement;
			sum.attackSpeed += b.attackSpeed;
			sum.damage += b.damage;
		}

		public static bool Prefix(UnitEquipSpace __instance, UnitModel actor, ref EGObonusInfo __result)
		{
			// A script that reads the bonus from inside GetBonus gets a fresh object, as in vanilla.
			if (_depth > 0)
			{
				return true;
			}
			_depth++;
			try
			{
				__result = Sum(__instance, actor);
			}
			finally
			{
				_depth--;
			}
			return false;
		}

		private static EGObonusInfo Sum(UnitEquipSpace __instance, UnitModel actor)
		{
			EGObonusInfo sum;
			if (!_results.TryGetValue(__instance, out sum))
			{
				if (_results.Count >= MaxSpaces)
				{
					_results.Clear();
				}
				sum = new EGObonusInfo();
				_results[__instance] = sum;
			}
			sum.hp = 0;
			sum.mental = 0;
			sum.cubeSpeed = 0;
			sum.workProb = 0;
			sum.movement = 0;
			sum.attackSpeed = 0;
			sum.damage = 0;
			if (__instance.weapon != null)
			{
				Add(sum, __instance.weapon.GetBonus(actor));
			}
			if (__instance.armor != null)
			{
				Add(sum, __instance.armor.GetBonus(actor));
			}
			List<EGOgiftModel> list = __instance.gifts.replacedGifts;
			for (int i = 0; i < list.Count; i++)
			{
				Add(sum, list[i].GetBonus(actor));
			}
			list = __instance.gifts.addedGifts;
			for (int i = 0; i < list.Count; i++)
			{
				Add(sum, list[i].GetBonus(actor));
			}
			return sum;
		}
	}

	// ---------- ScriptTypes ----------
	// WeaponModel.MakeWeapon (when the script name resolves with Type.GetType) and EGOgiftModel.MakeGift (always) walk every
	// Add_On.AssemList assembly x GetTypes() on each call to find a mod type with the script's short name; with ~300 mods that
	// is a full type scan per weapon or gift made (inventory and day load, runtime EGO changes of kygi123 weapons).
	// ChildCreatureModel.LoadScript does the same scan on every child creature spawn, and InventoryModel.CreateEquipment_Mod
	// (LcId, long) on every equipment made: each saved item at game start and each EGO extracted.
	// Replacement with the same steps: the short name -> type map is built once (last match wins, as in the game's loop) and
	// rebuilt when AssemList grows. Script names resolve as Type.GetType does inside Assembly-CSharp (GameType). Differences:
	// one instance is created instead of one per matching type. Runs the original when another mod has a Prefix or Transpiler
	// on the method. MakeWeapon, MakeGift and LoadScript run the original on an error before any state change (it repeats the
	// same steps and throws where the game throws; the handler it would lose under Harmony is not reached on those paths);
	// CreateEquipment_Mod never runs its handler-less copy and logs errors as the game does.
	public static class ScriptTypes
	{
		private const string Tag = "PerfPack";
		private static HarmonyInstance _harmony;
		private static MethodBase _weapon;
		private static MethodBase _gift;
		private static MethodBase _child;
		private static MethodBase _create;
		private static int _createState;
		private static int _weaponState;
		private static int _giftState;
		private static int _childState;
		private delegate void SpeedSetter(ChildCreatureModel child, float speed);
		private static SpeedSetter _setSpeed;
		private static Dictionary<string, Type> _byName;
		private static int _asmCount = -1;
		private static bool _broken;
		private static bool _failLogged;
		private static int _hits;
		private static bool _logged;
		internal static N.Flag WeaponModScript;
		private static bool _modScriptLogged;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			_harmony = harmony;
			_weapon = N.PatchUtil.Resolve(Tag, typeof(WeaponModel), "MakeWeapon", new Type[] { typeof(EquipmentTypeInfo) });
			_gift = N.PatchUtil.Resolve(Tag, typeof(EGOgiftModel), "MakeGift", new Type[] { typeof(EquipmentTypeInfo) });
			if (object.ReferenceEquals(_weapon, null) || object.ReferenceEquals(_gift, null))
			{
				return 0;
			}
			bool a = N.PatchUtil.Install(harmony, Tag, _weapon, new HarmonyMethod(typeof(ScriptTypes).GetMethod("PrefixWeapon", BindingFlags.Public | BindingFlags.Static)), null);
			bool b = N.PatchUtil.Install(harmony, Tag, _gift, new HarmonyMethod(typeof(ScriptTypes).GetMethod("PrefixGift", BindingFlags.Public | BindingFlags.Static)), null);
			InstallChild(harmony);
			_create = N.PatchUtil.Resolve(Tag, typeof(InventoryModel), "CreateEquipment_Mod", new Type[] { typeof(LobotomyBaseMod.LcId), typeof(long) });
			if (!object.ReferenceEquals(_create, null))
			{
				N.PatchUtil.Install(harmony, Tag, _create, new HarmonyMethod(typeof(ScriptTypes).GetMethod("PrefixCreate", BindingFlags.Public | BindingFlags.Static)), null);
			}
			return a && b ? 1 : 0;
		}

		// Full replacement of InventoryModel.CreateEquipment_Mod(LcId, long) with the same steps, logs and results; only the
		// script type comes from the type map. The original has a try/catch (GL-53), so its patched copy never runs once the map
		// is built: the map is built before any step, and a map that cannot be built leaves the call to the original.
		public static bool PrefixCreate(InventoryModel __instance, LobotomyBaseMod.LcId id, long instanceId, ref EquipmentModel __result)
		{
			if (!Usable(ref _createState, _create))
			{
				return true;
			}
			EquipmentModel result;
			try
			{
				result = Create(__instance, id, instanceId);
			}
			catch (Exception ex)
			{
				LobotomyBaseMod.ModDebug.Log("error make - " + ex.Message + Environment.NewLine + ex.StackTrace);
				result = null;
			}
			__result = result;
			return false;
		}

		// Type.GetType as called from Assembly-CSharp: a plain name resolves in the game assembly (Type.GetType from this
		// assembly cannot see it), then in mscorlib or through an assembly-qualified name. Null throws as Type.GetType does.
		private static readonly Assembly GameAsm = typeof(EquipmentScriptBase).Assembly;

		internal static Type GameType(string name)
		{
			if (name == null)
			{
				throw new ArgumentNullException("typeName");
			}
			if (name.Length == 0)
			{
				return null;
			}
			if (name.IndexOf(',') < 0)
			{
				Type t = GameAsm.GetType(name, false);
				if (object.ReferenceEquals(t, null))
				{
					t = typeof(object).Assembly.GetType(name, false);
				}
				return t;
			}
			return Type.GetType(name, false);
		}

		private static string IdText(LobotomyBaseMod.LcId id)
		{
			return object.ReferenceEquals(id, null) ? null : id.ToString();
		}

		private static EquipmentModel Create(InventoryModel inv, LobotomyBaseMod.LcId id, long instanceId)
		{
			LobotomyBaseMod.ModDebug.Log("Try Make - " + IdText(id));
			EquipmentTypeInfo info = EquipmentTypeList.instance.GetData_Mod(id);
			if (info == null)
			{
				LobotomyBaseMod.ModDebug.Log("Fail Make(Null)- " + IdText(id));
				return null;
			}
			List<EquipmentModel> list = inv.equipList;
			int same = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (EquipmentTypeInfo.GetLcId(list[i].metaInfo) == id)
				{
					same++;
				}
			}
			if (same >= info.MaxNum)
			{
				LobotomyBaseMod.ModDebug.Log("Fail Make(Full)- " + IdText(id));
				return null;
			}
			EquipmentModel model;
			EquipmentTypeInfo.EquipmentType type = info.type;
			if (type != EquipmentTypeInfo.EquipmentType.ARMOR)
			{
				if (type > EquipmentTypeInfo.EquipmentType.WEAPON)
				{
					model = new EGOgiftModel();
				}
				else
				{
					model = new WeaponModel();
				}
			}
			else
			{
				model = new ArmorModel();
			}
			model.instanceId = instanceId;
			model.metaInfo = info;
			Type mod = Find(info.script);
			object obj;
			if (!object.ReferenceEquals(mod, null))
			{
				obj = Activator.CreateInstance(mod);
			}
			else
			{
				try
				{
					obj = Activator.CreateInstance(GameType(info.script));
				}
				catch (ArgumentNullException)
				{
					obj = Activator.CreateInstance(typeof(EquipmentScriptBase));
				}
			}
			if (obj is EquipmentScriptBase)
			{
				model.script = (EquipmentScriptBase)obj;
				model.script.SetModel(model);
			}
			list.Add(model);
			Notice.instance.Send(NoticeName.MakeEquipment, new object[] { model });
			LobotomyBaseMod.ModDebug.Log("Success Make - " + IdText(id));
			Hit();
			return model;
		}

		// Optional part: a missing member leaves child spawns to the game.
		private static void InstallChild(HarmonyInstance harmony)
		{
			try
			{
				FieldInfo speed = typeof(ChildCreatureModel).GetField("Speed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				_child = N.PatchUtil.Resolve(Tag, typeof(ChildCreatureModel), "LoadScript", new Type[] { typeof(string) });
				if (object.ReferenceEquals(speed, null) || !object.ReferenceEquals(speed.FieldType, typeof(float)) || object.ReferenceEquals(_child, null))
				{
					N.ULog.Warn(Tag, "ScriptTypes: ChildCreatureModel.LoadScript left to the game, members not found");
					return;
				}
				DynamicMethod set = new DynamicMethod("ScriptTypes_Speed", typeof(void), new Type[] { typeof(ChildCreatureModel), typeof(float) }, typeof(ChildCreatureModel), true);
				ILGenerator il = set.GetILGenerator();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Stfld, speed);
				il.Emit(OpCodes.Ret);
				_setSpeed = (SpeedSetter)set.CreateDelegate(typeof(SpeedSetter));
				N.PatchUtil.Install(harmony, Tag, _child, new HarmonyMethod(typeof(ScriptTypes).GetMethod("PrefixChild", BindingFlags.Public | BindingFlags.Static)), null);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "ScriptTypes: ChildCreatureModel.LoadScript left to the game: " + e.Message);
			}
		}

		// Same steps as the original: the last type with the short name, else Type.GetType; then SetModel, OnInit, speed.
		public static bool PrefixChild(ChildCreatureModel __instance, string src)
		{
			if (src == null || _setSpeed == null || !Usable(ref _childState, _child))
			{
				return true;
			}
			object obj;
			try
			{
				Type mod = Find(src);
				obj = Activator.CreateInstance(object.ReferenceEquals(mod, null) ? GameType(src) : mod);
			}
			catch (Exception e)
			{
				Fail(e);
				return true;
			}
			__instance.script = (CreatureBase)obj;
			__instance.script.SetModel(__instance);
			__instance.script.OnInit();
			_setSpeed(__instance, __instance.childMetaInfo.speed);
			Hit();
			return false;
		}

		public static bool PrefixWeapon(EquipmentTypeInfo info, ref WeaponModel __result)
		{
			if (info == null || info.script == null || !Usable(ref _weaponState, _weapon))
			{
				return true;
			}
			try
			{
				Type type = GameType(info.script);
				object obj = null;
				if (!object.ReferenceEquals(type, null))
				{
					Type mod = Find(info.script);
					obj = Activator.CreateInstance(object.ReferenceEquals(mod, null) ? type : mod);
				}
				else if (WeaponModScript != null && WeaponModScript.On)
				{
					// The game scans the mods only when a game class of that name exists (bug-wiki: mod-only weapon scripts).
					Type mod = Find(info.script);
					if (!object.ReferenceEquals(mod, null) && typeof(EquipmentScriptBase).IsAssignableFrom(mod) && !mod.IsAbstract)
					{
						obj = Activator.CreateInstance(mod);
						if (!_modScriptLogged)
						{
							_modScriptLogged = true;
							N.ULog.Info(Tag, "weapon mod script used: " + info.script + " (no game class of that name; logged once)");
						}
					}
				}
				WeaponModel model = new WeaponModel();
				model.metaInfo = info;
				if (obj is EquipmentScriptBase)
				{
					model.script = (EquipmentScriptBase)obj;
				}
				__result = model;
			}
			catch (Exception e)
			{
				Fail(e);
				return true;
			}
			__result.script.SetModel(__result);
			Hit();
			return false;
		}

		public static bool PrefixGift(EquipmentTypeInfo info, ref EGOgiftModel __result)
		{
			if (info == null || info.script == null || !Usable(ref _giftState, _gift))
			{
				return true;
			}
			try
			{
				Type type = GameType(info.script);
				Type mod = Find(info.script);
				object obj = null;
				if (!object.ReferenceEquals(mod, null))
				{
					obj = Activator.CreateInstance(mod);
				}
				else
				{
					try
					{
						obj = Activator.CreateInstance(type);
					}
					catch (Exception)
					{
					}
				}
				EGOgiftModel model = new EGOgiftModel();
				model.metaInfo = info;
				if (obj is EquipmentScriptBase)
				{
					model.script = (EquipmentScriptBase)obj;
				}
				__result = model;
			}
			catch (Exception e)
			{
				Fail(e);
				return true;
			}
			__result.script.SetModel(__result);
			Hit();
			return false;
		}

		// First call per method: off when another owner has a Prefix or Transpiler on it (theirs expect the original to run).
		private static bool Usable(ref int state, MethodBase m)
		{
			if (state == 0)
			{
				state = 1;
				try
				{
					Patches info = _harmony.IsPatched(m);
					if (info != null)
					{
						string other = Foreign(info.Prefixes);
						if (other == null)
						{
							other = Foreign(info.Transpilers);
						}
						if (other != null)
						{
							state = 2;
							N.ULog.Warn(Tag, "ScriptTypes: " + m.DeclaringType.Name + "." + m.Name + " left to the game, patched by " + other);
						}
					}
				}
				catch (Exception e)
				{
					state = 2;
					N.ULog.Warn(Tag, "ScriptTypes: patch check failed, " + m.Name + " left to the game: " + e.Message);
				}
			}
			return state == 1;
		}

		private static string Foreign(System.Collections.ObjectModel.ReadOnlyCollection<Patch> list)
		{
			if (list == null)
			{
				return null;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].owner != _harmony.Id)
				{
					return list[i].owner;
				}
			}
			return null;
		}

		// The last AssemList type with this short name, or null. A GetTypes failure throws as in the game's own scan (no map is
		// kept, so the next call scans again, as the game does).
		private static Type Find(string name)
		{
			List<Assembly> list = ((Add_On)Add_On.instance).AssemList;
			int count = list == null ? 0 : list.Count;
			if (_byName == null || count != _asmCount)
			{
				Dictionary<string, Type> map = new Dictionary<string, Type>(StringComparer.Ordinal);
				for (int i = 0; i < count; i++)
				{
					Type[] types;
					try
					{
						types = list[i].GetTypes();
					}
					catch (Exception e)
					{
						if (!_broken)
						{
							_broken = true;
							N.ULog.Warn(Tag, "ScriptTypes: type scan of " + list[i].GetName().Name + " failed (" + e.GetType().Name + "), the call fails as in the game");
						}
						throw;
					}
					for (int j = 0; j < types.Length; j++)
					{
						map[types[j].Name] = types[j];
					}
				}
				_byName = map;
				_asmCount = count;
			}
			Type type = null;
			if (name != null)
			{
				_byName.TryGetValue(name, out type);
			}
			return type;
		}

		private static void Fail(Exception e)
		{
			if (!_failLogged)
			{
				_failLogged = true;
				N.ULog.Warn(Tag, "ScriptTypes: a script lookup failed, the game's method runs instead: " + e.GetType().Name + ": " + e.Message);
			}
		}

		private static void Hit()
		{
			_hits++;
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(Tag, "ScriptTypes: equipment scripts found through the type map (" + (_byName == null ? 0 : _byName.Count) + " names in " + _asmCount + " assemblies)");
			}
		}
	}

	// ---------- SefiraLookup ----------
	public static class SefiraLookup
	{
		private const string Tag = "PerfPack";
		private static Sefira[] _table;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase get = N.PatchUtil.Resolve(Tag, typeof(SefiraManager), "GetSefira", new Type[] { typeof(SefiraEnum) });
			MethodBase init = N.PatchUtil.Resolve(Tag, typeof(SefiraManager), "Init", Type.EmptyTypes);
			if (object.ReferenceEquals(get, null) || object.ReferenceEquals(init, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(get);
			if (owner == null)
			{
				owner = Owners.ForeignAny(init);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "SefiraLookup not installed: " + owner + " patches SefiraManager");
				return 0;
			}
			// The reset goes first: a table that outlives Init would hold the old departments.
			HarmonyMethod reset = new HarmonyMethod(typeof(SefiraLookup).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static));
			if (!N.PatchUtil.Install(harmony, Tag, init, reset, reset))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, get, new HarmonyMethod(typeof(SefiraLookup).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "SefiraLookup installed");
			return 1;
		}

		public static void Reset()
		{
			_table = null;
		}

		private static void Build(SefiraManager manager)
		{
			int max = 0;
			foreach (Sefira s in manager.sefiraList)
			{
				max = Mathf.Max(max, (int)s.sefiraEnum);
			}
			Sefira[] table = new Sefira[max + 1];
			foreach (Sefira s in manager.sefiraList)
			{
				table[(int)s.sefiraEnum] = s;
			}
			_table = table;
		}

		public static bool Prefix(SefiraManager __instance, SefiraEnum sefira, ref Sefira __result)
		{
			if (_table == null)
			{
				try
				{
					Build(__instance);
				}
				catch (Exception e)
				{
					N.ULog.Warn(Tag, "SefiraLookup table: " + e.Message);
					return true;
				}
			}
			int i = (int)sefira;
			if (i < 0 || i >= _table.Length || _table[i] == null)
			{
				return true;
			}
			__result = _table[i];
			return false;
		}
	}

	// ---------- LcIdCache ----------
	public static class LcIdCache
	{
		private const string Tag = "PerfPack";
		private static readonly Dictionary<EquipmentTypeInfo, LobotomyBaseMod.LcId> _ids = new Dictionary<EquipmentTypeInfo, LobotomyBaseMod.LcId>();

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(EquipmentTypeInfo), "GetLcId", new Type[] { typeof(EquipmentTypeInfo) });
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "LcIdCache not installed: " + owner + " patches EquipmentTypeInfo.GetLcId");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(LcIdCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "LcIdCache installed");
			return 1;
		}

		public static bool Prefix(EquipmentTypeInfo equip, ref LobotomyBaseMod.LcId __result)
		{
			if (object.ReferenceEquals(equip, null))
			{
				return true;
			}
			string package = equip.modid;
			if (package == null)
			{
				package = string.Empty;
			}
			LobotomyBaseMod.LcId known;
			// id and modid are public fields: a change makes a new LcId.
			if (!_ids.TryGetValue(equip, out known) || known.id != equip.id || known.packageId != package)
			{
				known = new LobotomyBaseMod.LcId(package, equip.id);
				_ids[equip] = known;
			}
			__result = known;
			return false;
		}
	}

	// ---------- QuietLog ----------
	// Debug.Log still builds a stack trace and BaseMod opens Log.txt for every line. Warnings and errors stay.
	// The Unity filter also starves every logMessageReceived listener of Info lines, the bridge included;
	// probes that need Debug.Log lines turn this flag off.
	public static class QuietLog
	{
		private const string Tag = "PerfPack";
		private static readonly string[] Keep = new string[] { "Exception", "rror", "ERROR", "  at ", "Fail", "fail", "missing", "No Match", "must be", "None Support" };
		private static bool _writeFailed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type md = typeof(LobotomyBaseMod.ModDebug);
			MethodBase info = N.PatchUtil.Resolve(Tag, md, "Debug_Log", new Type[] { typeof(object) });
			MethodBase log = N.PatchUtil.Resolve(Tag, md, "Log", new Type[] { typeof(string) });
			MethodBase ex = N.PatchUtil.Resolve(Tag, md, "Debug_LogException", new Type[] { typeof(Exception) });
			if (object.ReferenceEquals(info, null) || object.ReferenceEquals(log, null) || object.ReferenceEquals(ex, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(log);
			if (owner == null)
			{
				owner = Owners.ForeignAny(info);
			}
			if (owner == null)
			{
				owner = Owners.ForeignAny(ex);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "QuietLog not installed: " + owner + " patches ModDebug");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, log, new HarmonyMethod(typeof(QuietLog).GetMethod("PrefixLog", BindingFlags.Public | BindingFlags.Static)), null)
				|| !N.PatchUtil.Install(harmony, Tag, ex, new HarmonyMethod(typeof(QuietLog).GetMethod("PrefixException", BindingFlags.Public | BindingFlags.Static)), null)
				|| !N.PatchUtil.Install(harmony, Tag, info, new HarmonyMethod(typeof(QuietLog).GetMethod("SkipInfo", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			Debug.unityLogger.filterLogType = LogType.Warning;
			N.ULog.Info(Tag, "QuietLog installed");
			return 1;
		}

		public static bool SkipInfo()
		{
			return false;
		}

		// Full replacement: the Harmony copy of the original lost the clauses of its using block (GL-53), so it never runs.
		public static bool PrefixLog(string msg)
		{
			if (msg != null)
			{
				for (int i = 0; i < Keep.Length; i++)
				{
					if (msg.IndexOf(Keep[i], StringComparison.Ordinal) >= 0)
					{
						Write(msg);
						break;
					}
				}
			}
			return false;
		}

		// The text of an exception may hold none of the kept words.
		public static bool PrefixException(Exception exception)
		{
			if (exception != null)
			{
				Write(exception.Message + Environment.NewLine + exception.StackTrace);
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Write(string msg)
		{
			try
			{
				if (!LobotomyBaseMod.ModDebug.CheckLogFileExist())
				{
					LobotomyBaseMod.ModDebug.FileInit();
				}
				using (StreamWriter w = new StreamWriter(LobotomyBaseMod.ModDebug.LogFilePath, true))
				{
					w.WriteLine(msg);
				}
			}
			catch (Exception e)
			{
				if (!_writeFailed)
				{
					_writeFailed = true;
					N.ULog.Warn(Tag, "QuietLog write failed: " + e.Message);
				}
			}
		}
	}

	// ---------- ClerkCull ----------
	// Clerks and agents outside the view stop animating and drawing. Attacks, suicide, work and damage are driven by animation events,
	// so only a unit that is idle or walking qualifies; any other state brings it back on the next sweep.
	public static class ClerkCull
	{
		private const string Tag = "PerfPack";
		private const float Interval = 0.1f;
		private const float MarginWorld = 3f;
		private const float MarginView = 0.25f;
		private const float Hysteresis = 1f;
		private const int EntersPerSweep = 50;
		private const int ExitsPerSweep = 100;

		private class Item
		{
			public Component Unit;
			public bool On;
			public int Seen;
			public List<Behaviour> Off = new List<Behaviour>();
			public List<Renderer> Hidden = new List<Renderer>();
		}

		private static readonly Dictionary<Component, Item> _items = new Dictionary<Component, Item>();
		private static readonly List<Item> _all = new List<Item>();
		private static readonly List<Animator> _animators = new List<Animator>();
		private static readonly List<Spine.Unity.SkeletonRenderer> _skeletons = new List<Spine.Unity.SkeletonRenderer>();
		private static readonly List<Canvas> _canvases = new List<Canvas>();
		private static readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
		private static N.Flag _flag;
		private static N.Flag _agentFlag;
		private static bool _clerksAllowed;
		private static N.Counter _entered;
		private static N.Counter _enteredAgents;
		private static int _enters;
		private static int _exits;
		private static int _seen;
		private static float _w;
		private static float _h;
		private static Vector3 _pos;
		private static float _next;
		private static Vector3 _lastPos;
		private static float _lastSize;
		private static int _sweep;
		private static int _on;
		private static bool _failed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag, N.Flag agentFlag, bool clerksAllowed)
		{
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(update, null))
			{
				return 0;
			}
			_flag = flag;
			_agentFlag = agentFlag;
			_clerksAllowed = clerksAllowed;
			_entered = N.Counters.Get(Tag, "clerksCulled");
			_enteredAgents = N.Counters.Get(Tag, "agentsCulled");
			if (!N.PatchUtil.Install(harmony, Tag, update, new HarmonyMethod(typeof(ClerkCull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			// A controller swap sets triggers and parameters at once, before the next sweep would see the new state.
			HarmonyMethod swap = new HarmonyMethod(typeof(ClerkCull).GetMethod("SwapPrefix", BindingFlags.Public | BindingFlags.Static));
			Type changer = typeof(WorkerSpine.WorkerAnimatorChanger);
			Type data = typeof(WorkerSpine.WorkerSpineAnimatorData);
			MethodBase[] swaps = new MethodBase[]
			{
				N.PatchUtil.Resolve(Tag, changer, "SetAnimator", new Type[] { data }),
				N.PatchUtil.Resolve(Tag, changer, "SetAnimator", new Type[] { data, typeof(bool) }),
				N.PatchUtil.Resolve(Tag, changer, "SetAnimatorWithUniqueFace", new Type[] { data, typeof(bool) })
			};
			for (int i = 0; i < swaps.Length; i++)
			{
				if (object.ReferenceEquals(swaps[i], null) || !N.PatchUtil.Install(harmony, Tag, swaps[i], swap, null))
				{
					N.ULog.Warn(Tag, "ClerkCull: no agents, controller swap not covered");
					_agentFlag = null;
					break;
				}
			}
			N.ULog.Info(Tag, "ClerkCull installed");
			return 1;
		}

		public static void SwapPrefix(Component __instance)
		{
			if (_on == 0)
			{
				return;
			}
			try
			{
				WorkerUnit unit = __instance.GetComponentInParent<WorkerUnit>();
				Item it;
				if (unit != null && _items.TryGetValue(unit, out it) && it.On)
				{
					Exit(it);
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "ClerkCull swap: " + e.Message);
			}
		}

		public static void Prefix(GameManager __instance)
		{
			if (_failed)
			{
				return;
			}
			try
			{
				if (__instance.state == GameState.STOP || !((_flag.On && _clerksAllowed) || AgentsOn()))
				{
					if (_all.Count > 0)
					{
						Clear();
					}
					return;
				}
				Camera cam = Camera.main;
				if (cam == null || OfficerManager.instance == null || AgentManager.instance == null)
				{
					return;
				}
				float now = Time.realtimeSinceStartup;
				Vector3 pos = cam.transform.position;
				float size = cam.orthographicSize;
				float jump = size * 0.2f;
				if (now < _next && Mathf.Abs(pos.x - _lastPos.x) < jump && Mathf.Abs(pos.y - _lastPos.y) < jump && Mathf.Abs(size - _lastSize) < jump)
				{
					return;
				}
				_next = now + Interval;
				_lastPos = pos;
				_lastSize = size;
				Sweep(pos, size, cam.aspect);
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Warn(Tag, "ClerkCull failed: " + e);
				try
				{
					Clear();
				}
				catch (Exception e2)
				{
					N.ULog.Warn(Tag, "ClerkCull restore failed: " + e2.Message);
				}
			}
		}

		private static void Sweep(Vector3 pos, float size, float aspect)
		{
			float margin = Mathf.Max(MarginWorld, size * MarginView);
			_h = size + margin;
			_w = size * aspect + margin;
			_pos = pos;
			_enters = EntersPerSweep;
			_exits = ExitsPerSweep;
			_seen = 0;
			_sweep++;
			if (_clerksAllowed)
			{
				bool clerks = _flag.On;
				IList<OfficerModel> list = OfficerManager.instance.GetOfficerList();
				for (int i = 0; i < list.Count; i++)
				{
					OfficerModel model = list[i];
					OfficerUnit unit = model.GetUnit();
					if (unit != null)
					{
						Visit(model, unit, clerks);
					}
				}
			}
			// Agents do not wait behind the clerks for the budgets.
			_enters = EntersPerSweep;
			_exits = ExitsPerSweep;
			bool agents = AgentsOn();
			IList<AgentModel> agentList = AgentManager.instance.GetAgentList();
			for (int i = 0; i < agentList.Count; i++)
			{
				AgentModel model = agentList[i];
				AgentUnit unit = model.GetUnit();
				if (unit != null)
				{
					Visit(model, unit, agents);
				}
			}
			// A unit that left the list must not stay switched off.
			if (_seen != _on)
			{
				for (int i = 0; i < _all.Count; i++)
				{
					if (_all[i].On && _all[i].Seen != _sweep)
					{
						Exit(_all[i]);
					}
				}
			}
		}

		private static bool AgentsOn()
		{
			return _agentFlag != null && _agentFlag.On;
		}

		private static void Visit(WorkerModel model, Component unit, bool allowed)
		{
			Vector3 d = unit.transform.position - _pos;
			float x = Mathf.Abs(d.x);
			float y = Mathf.Abs(d.y);
			Item it;
			_items.TryGetValue(unit, out it);
			if (it == null || !it.On)
			{
				if (allowed && _enters > 0 && (x > _w + Hysteresis || y > _h + Hysteresis) && Calm(model, unit))
				{
					if (it == null)
					{
						it = new Item();
						it.Unit = unit;
						_items[unit] = it;
						_all.Add(it);
					}
					_enters--;
					Enter(it);
				}
			}
			else if (!allowed || !Calm(model, unit))
			{
				Exit(it);
			}
			else if (x <= _w && y <= _h)
			{
				if (_exits > 0)
				{
					_exits--;
					Exit(it);
				}
				else
				{
					// A zoom out: the rest comes back on the next frames.
					_next = 0f;
				}
			}
			if (it != null && it.On)
			{
				it.Seen = _sweep;
				_seen++;
			}
		}

		private static bool Calm(WorkerModel model, Component unit)
		{
			if (model.IsDead() || model.IsPanic() || model.IsCrazy() || model.IsAttackState() || model.unconAction != null || !unit.gameObject.activeInHierarchy)
			{
				return false;
			}
			// Only clerks ever set OnWorkEndFlag.
			AgentModel agent = model as AgentModel;
			if (agent == null)
			{
				if (!model.OnWorkEndFlag)
				{
					return false;
				}
			}
			else if (agent.currentSkill != null || agent.GetState() != AgentAIState.IDLE || agent.haltUpdate || ((WorkerUnit)unit).blockMoving)
			{
				return false;
			}
			WorkerCommand cmd = model.GetCurrentCommand();
			return cmd == null || cmd is MoveWorkerCommand;
		}

		private static void Enter(Item it)
		{
			it.On = true;
			_on++;
			if (it.Unit is AgentUnit)
			{
				_enteredAgents.Value++;
			}
			else
			{
				_entered.Value++;
			}
			it.Unit.GetComponentsInChildren<Animator>(false, _animators);
			for (int i = 0; i < _animators.Count; i++)
			{
				if (_animators[i].enabled)
				{
					_animators[i].enabled = false;
					it.Off.Add(_animators[i]);
				}
			}
			it.Unit.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(false, _skeletons);
			for (int i = 0; i < _skeletons.Count; i++)
			{
				if (_skeletons[i].enabled)
				{
					_skeletons[i].enabled = false;
					it.Off.Add(_skeletons[i]);
				}
			}
			it.Unit.GetComponentsInChildren<Canvas>(false, _canvases);
			for (int i = 0; i < _canvases.Count; i++)
			{
				if (_canvases[i].enabled)
				{
					_canvases[i].enabled = false;
					it.Off.Add(_canvases[i]);
				}
			}
			// Only meshes: the game itself writes the enabled flags of the sprite renderers (weapon, mouth, face).
			it.Unit.GetComponentsInChildren<MeshRenderer>(false, _renderers);
			for (int i = 0; i < _renderers.Count; i++)
			{
				if (_renderers[i].enabled)
				{
					_renderers[i].enabled = false;
					it.Hidden.Add(_renderers[i]);
				}
			}
		}

		private static void Exit(Item it)
		{
			for (int i = 0; i < it.Off.Count; i++)
			{
				if (it.Off[i] != null)
				{
					it.Off[i].enabled = true;
				}
			}
			for (int i = 0; i < it.Hidden.Count; i++)
			{
				if (it.Hidden[i] != null)
				{
					it.Hidden[i].enabled = true;
				}
			}
			it.Off.Clear();
			it.Hidden.Clear();
			it.On = false;
			_on--;
		}

		private static void Clear()
		{
			for (int i = 0; i < _all.Count; i++)
			{
				if (_all[i].On)
				{
					Exit(_all[i]);
				}
			}
			_all.Clear();
			_items.Clear();
			_on = 0;
		}
	}

	// ---------- AgentStats ----------
	// The stat getters of AgentModel (maxHp, maxMental, movement, fortitudeStat...) build two new WorkerPrimaryStatBonus per read:
	// GetPrimaryStatBuf() and titleBonus (r188: 264509 GetPrimaryStatBuf calls per 30 s, 32 B each; HP Awareness reads maxHp and
	// maxMental per agent per frame). Each read uses one field of the result at once, so inside these getters the two calls go to
	// helpers that refill one reused instance by the same rules. The methods themselves stay unchanged for every other caller.
	public static class AgentStats
	{
		private const string Tag = "PerfPack";
		private static readonly string[] Getters = new string[]
		{
			"fortitudeStat", "prudenceStat", "temperanceStat", "justiceStat",
			"originFortitudeStat", "originPrudenceStat", "originTemperanceStat", "originJusticeStat",
			"maxHp", "maxMental", "movement", "workSpeed", "workProb", "attackSpeed"
		};
		private static readonly WorkerPrimaryStatBonus _buf = new WorkerPrimaryStatBonus();
		private static readonly WorkerPrimaryStatBonus _title = new WorkerPrimaryStatBonus();
		private static StatsFn _stats;
		private static TitleFn _prefix;
		private static TitleFn _suffix;
		private static MethodInfo _getBuf;
		private static MethodInfo _getTitle;
		private static MethodInfo _myBuf;
		private static MethodInfo _myTitle;
		private static int _replaced;
		private static bool _checked;
		private static bool _useBuf;
		private static bool _useTitle;

		private delegate List<UnitStatBuf> StatsFn(UnitModel m);
		private delegate AgentTitleTypeInfo TitleFn(AgentModel m);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			FieldInfo stats = typeof(UnitModel).GetField("_statBufList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo prefix = typeof(AgentModel).GetField("prefix", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo suffix = typeof(AgentModel).GetField("suffix", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			MethodInfo getBuf = typeof(UnitModel).GetMethod("GetPrimaryStatBuf", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			PropertyInfo title = typeof(AgentModel).GetProperty("titleBonus", BindingFlags.Instance | BindingFlags.Public);
			MethodInfo getTitle = object.ReferenceEquals(title, null) ? null : title.GetGetMethod();
			if (object.ReferenceEquals(stats, null) || object.ReferenceEquals(prefix, null) || object.ReferenceEquals(suffix, null)
				|| object.ReferenceEquals(getBuf, null) || object.ReferenceEquals(getTitle, null))
			{
				N.ULog.Warn(Tag, "AgentStats not installed: a member was not found");
				return 0;
			}
			// A foreign patch on either source must keep working: skip that replacement.
			string owner = Foreign(getBuf);
			if (owner == null)
			{
				_getBuf = getBuf;
			}
			else
			{
				N.ULog.Warn(Tag, "AgentStats keeps GetPrimaryStatBuf: " + owner + " patches it");
			}
			owner = Foreign(getTitle);
			if (owner == null)
			{
				_getTitle = getTitle;
			}
			else
			{
				N.ULog.Warn(Tag, "AgentStats keeps titleBonus: " + owner + " patches it");
			}
			if (object.ReferenceEquals(_getBuf, null) && object.ReferenceEquals(_getTitle, null))
			{
				return 0;
			}
			try
			{
				_stats = (StatsFn)Getter(stats, typeof(UnitModel), typeof(StatsFn));
				_prefix = (TitleFn)Getter(prefix, typeof(AgentModel), typeof(TitleFn));
				_suffix = (TitleFn)Getter(suffix, typeof(AgentModel), typeof(TitleFn));
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "AgentStats not installed: " + e.Message);
				return 0;
			}
			_myBuf = typeof(AgentStats).GetMethod("Buf", BindingFlags.Public | BindingFlags.Static);
			_myTitle = typeof(AgentStats).GetMethod("Title", BindingFlags.Public | BindingFlags.Static);
			HarmonyMethod transpiler = new HarmonyMethod(typeof(AgentStats).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			int count = 0;
			for (int i = 0; i < Getters.Length; i++)
			{
				PropertyInfo p = typeof(AgentModel).GetProperty(Getters[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
				MethodInfo get = object.ReferenceEquals(p, null) ? null : p.GetGetMethod();
				if (object.ReferenceEquals(get, null))
				{
					N.ULog.Warn(Tag, "AgentStats: AgentModel." + Getters[i] + " not found");
					continue;
				}
				// Harmony 1.0.9.1 drops exception handlers of a patched method (GL-53).
				MethodBody body = get.GetMethodBody();
				if (body == null || body.ExceptionHandlingClauses.Count > 0)
				{
					continue;
				}
				try
				{
					harmony.Patch(get, null, null, transpiler);
					count++;
				}
				catch (Exception e)
				{
					N.ULog.Warn(Tag, "AgentStats " + Getters[i] + ": " + e.Message);
				}
			}
			if (count > 0 && _replaced == 0)
			{
				N.ULog.Warn(Tag, "AgentStats: no call matched in " + count + " getters");
				return 0;
			}
			N.ULog.Info(Tag, "AgentStats installed on " + count + " of " + Getters.Length + " getters, " + _replaced + " calls replaced");
			N.Events.On(NoticeName.OnStageStart, Recheck, Tag);
			return count > 0 ? 1 : 0;
		}

		private static string Foreign(MethodBase m)
		{
			string owner = Owners.ForeignAny(m);
			return owner != null ? owner : UnitLoops.ForeignSameCode(m);
		}

		// A mod that loads after us may patch a source (TitleSpecial postfixes titleBonus, r190): then the helper calls the
		// method itself, so that patch keeps working. Checked on first use and at every day start.
		private static void Recheck()
		{
			_checked = true;
			bool buf = !object.ReferenceEquals(_getBuf, null) && Late(_getBuf, _useBuf);
			bool title = !object.ReferenceEquals(_getTitle, null) && Late(_getTitle, _useTitle);
			_useBuf = buf;
			_useTitle = title;
		}

		private static bool Late(MethodBase m, bool was)
		{
			string owner = Foreign(m);
			if (owner == null)
			{
				return true;
			}
			if (was || !_logged.Contains(m.Name))
			{
				_logged.Add(m.Name);
				N.ULog.Warn(Tag, "AgentStats calls " + m.Name + " as is: " + owner + " patches it");
			}
			return false;
		}

		private static readonly List<string> _logged = new List<string>(2);

		private static Delegate Getter(FieldInfo field, Type owner, Type fn)
		{
			DynamicMethod get = new DynamicMethod("AgentStats_" + field.Name, field.FieldType, new Type[] { owner }, owner, true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return get.CreateDelegate(fn);
		}

		// Only a call whose result is read at once (a field or a getter of WorkerPrimaryStatBonus) is replaced.
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i + 1 < list.Count; i++)
			{
				MethodInfo called = list[i].operand as MethodInfo;
				if (object.ReferenceEquals(called, null) || (list[i].opcode != OpCodes.Call && list[i].opcode != OpCodes.Callvirt) || !ReadsAtOnce(list[i + 1]))
				{
					continue;
				}
				if (!object.ReferenceEquals(_getBuf, null) && object.ReferenceEquals(called, _getBuf))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = _myBuf;
					_replaced++;
				}
				else if (!object.ReferenceEquals(_getTitle, null) && object.ReferenceEquals(called, _getTitle))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = _myTitle;
					_replaced++;
				}
			}
			return list;
		}

		private static bool ReadsAtOnce(CodeInstruction next)
		{
			FieldInfo f = next.operand as FieldInfo;
			if (next.opcode == OpCodes.Ldfld && !object.ReferenceEquals(f, null))
			{
				return object.ReferenceEquals(f.DeclaringType, typeof(WorkerPrimaryStatBonus));
			}
			MethodInfo m = next.operand as MethodInfo;
			if ((next.opcode == OpCodes.Call || next.opcode == OpCodes.Callvirt) && !object.ReferenceEquals(m, null))
			{
				return object.ReferenceEquals(m.DeclaringType, typeof(WorkerPrimaryStatBonus)) && m.Name.StartsWith("get_", StringComparison.Ordinal);
			}
			return false;
		}

		// Same sums as UnitModel.GetPrimaryStatBuf (game/UnitModel.cs:855-870) into the reused instance.
		public static WorkerPrimaryStatBonus Buf(UnitModel unit)
		{
			if (!_checked)
			{
				Recheck();
			}
			if (!_useBuf)
			{
				return unit.GetPrimaryStatBuf();
			}
			WorkerPrimaryStatBonus sum = _buf;
			sum.hp = 0;
			sum.mental = 0;
			sum.work = 0;
			sum.battle = 0;
			List<UnitStatBuf> list = _stats(unit);
			for (int i = 0; i < list.Count; i++)
			{
				WorkerPrimaryStatBonus stat = list[i].primaryStat;
				sum.hp += stat.hp;
				sum.mental += stat.mental;
				sum.work += stat.work;
				sum.battle += stat.battle;
			}
			return sum;
		}

		// Same sums as AgentModel.titleBonus (game/AgentModel.cs:548-567) into the reused instance.
		public static WorkerPrimaryStatBonus Title(AgentModel agent)
		{
			if (!_checked)
			{
				Recheck();
			}
			if (!_useTitle)
			{
				return agent.titleBonus;
			}
			WorkerPrimaryStatBonus sum = _title;
			sum.hp = 0;
			sum.mental = 0;
			sum.work = 0;
			sum.battle = 0;
			AgentTitleTypeInfo t = _prefix(agent);
			if (t != null)
			{
				sum.hp += t.hp;
				sum.mental += t.mental;
				sum.work += t.workProb;
				sum.battle += t.attackSpeed;
			}
			t = _suffix(agent);
			if (t != null)
			{
				sum.hp += t.hp;
				sum.mental += t.mental;
				sum.work += t.workProb;
				sum.battle += t.attackSpeed;
			}
			return sum;
		}
	}

	// ---------- UnitLoops ----------
	public static class UnitLoops
	{
		private const string Tag = "PerfPack";
		private static BufsFn _bufs;
		private static StatsFn _stats;

		private delegate List<UnitBuf> BufsFn(UnitModel m);
		private delegate List<UnitStatBuf> StatsFn(UnitModel m);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			FieldInfo bufs = typeof(UnitModel).GetField("_bufList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			FieldInfo stats = typeof(UnitModel).GetField("_statBufList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (object.ReferenceEquals(bufs, null) || object.ReferenceEquals(stats, null))
			{
				return 0;
			}
			_bufs = (BufsFn)Getter(bufs, typeof(BufsFn));
			_stats = (StatsFn)Getter(stats, typeof(StatsFn));
			int count = 0;
			count += One(harmony, typeof(UnitModel), "UpdateBufState", "BufState");
			count += One(harmony, typeof(UnitModel), "GetMovementScaleByBuf", "MoveScale");
			count += One(harmony, typeof(UnitModel), "GetMovementBuf", "MoveBuf");
			count += One(harmony, typeof(UnitEGOgiftSpace), "OnFixedUpdate", "Gifts");
			count += One(harmony, typeof(UnitModel), "GetMaxHpBuf", "MaxHp");
			count += One(harmony, typeof(UnitModel), "GetMaxMentalBuf", "MaxMental");
			count += One(harmony, typeof(UnitModel), "GetPrimaryStatBuf", "PrimaryStat");
			N.ULog.Info(Tag, "UnitLoops installed: " + count + " of 7");
			return count > 0 ? 1 : 0;
		}

		private static Delegate Getter(FieldInfo field, Type fn)
		{
			DynamicMethod get = new DynamicMethod("UnitLoops_" + field.Name, field.FieldType, new Type[] { typeof(UnitModel) }, typeof(UnitModel), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return get.CreateDelegate(fn);
		}

		private static int One(HarmonyInstance harmony, Type type, string name, string prefix)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, type, name, Type.EmptyTypes);
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner == null)
			{
				owner = ForeignSameCode(target);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "UnitLoops skips " + name + ": " + owner + " patches it");
				return 0;
			}
			return N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(UnitLoops).GetMethod(prefix, BindingFlags.Public | BindingFlags.Static)), null) ? 1 : 0;
		}

		// A mod can patch this code through a subclass (RottenWhaleFall: AgentModel.GetMovementScaleByBuf): another MethodInfo, the same handle.
		internal static string ForeignSameCode(MethodBase target)
		{
			foreach (MethodBase m in PatchProcessor.AllPatchedMethods())
			{
				if (m.MethodHandle.Value.Equals(target.MethodHandle.Value))
				{
					string owner = Owners.ForeignAny(m);
					if (owner != null)
					{
						return owner;
					}
				}
			}
			return null;
		}

		// Two buffs or more keep the vanilla copy: a buff may remove another one during FixedUpdate.
		public static bool BufState(UnitModel __instance)
		{
			List<UnitBuf> list = _bufs(__instance);
			if (list.Count > 1)
			{
				return true;
			}
			if (list.Count == 1)
			{
				list[0].FixedUpdate();
			}
			return false;
		}

		public static bool MoveScale(UnitModel __instance, ref float __result)
		{
			List<UnitBuf> list = _bufs(__instance);
			float scale = 1f;
			for (int i = 0; i < list.Count; i++)
			{
				scale *= list[i].MovementScale();
			}
			__result = scale;
			return false;
		}

		public static bool MoveBuf(UnitModel __instance, ref float __result)
		{
			List<UnitStatBuf> list = _stats(__instance);
			float sum = 0f;
			for (int i = 0; i < list.Count; i++)
			{
				sum += list[i].movementSpeed;
			}
			__result = sum;
			return false;
		}

		public static bool MaxHp(UnitModel __instance, ref int __result)
		{
			List<UnitStatBuf> list = _stats(__instance);
			int sum = 0;
			for (int i = 0; i < list.Count; i++)
			{
				sum += list[i].maxHp;
			}
			__result = sum;
			return false;
		}

		public static bool MaxMental(UnitModel __instance, ref int __result)
		{
			List<UnitStatBuf> list = _stats(__instance);
			int sum = 0;
			for (int i = 0; i < list.Count; i++)
			{
				sum += list[i].maxMental;
			}
			__result = sum;
			return false;
		}

		// Still a new object per call, as callers may keep it.
		public static bool PrimaryStat(UnitModel __instance, ref WorkerPrimaryStatBonus __result)
		{
			WorkerPrimaryStatBonus sum = new WorkerPrimaryStatBonus();
			sum.hp = 0;
			sum.mental = 0;
			sum.work = 0;
			sum.battle = 0;
			List<UnitStatBuf> list = _stats(__instance);
			for (int i = 0; i < list.Count; i++)
			{
				WorkerPrimaryStatBonus stat = list[i].primaryStat;
				sum.hp += stat.hp;
				sum.mental += stat.mental;
				sum.work += stat.work;
				sum.battle += stat.battle;
			}
			__result = sum;
			return false;
		}

		public static bool Gifts(UnitEGOgiftSpace __instance)
		{
			List<EGOgiftModel> list = __instance.replacedGifts;
			for (int i = 0; i < list.Count; i++)
			{
				list[i].script.OnFixedUpdate();
			}
			list = __instance.addedGifts;
			for (int i = 0; i < list.Count; i++)
			{
				list[i].script.OnFixedUpdate();
			}
			return false;
		}
	}

	// ---------- HpTrackerLoop ----------
	public static class HpTrackerLoop
	{
		private const string Tag = "PerfPack";
		private const string TrackerType = "HPAwareness.HPTrackerUI";
		private static HarmonyInstance _harmony;
		private static ElementsFn _elements;
		private static TargetFn _target;

		private delegate System.Collections.IList ElementsFn(object tracker);
		private delegate AgentUnit TargetFn(object element);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			_harmony = harmony;
			try
			{
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					Type type = all[i].GetType(TrackerType);
					if (!object.ReferenceEquals(type, null))
					{
						return Patch(type) ? 1 : 0;
					}
				}
				AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
				N.ULog.Info(Tag, "HpTrackerLoop waiting for HP Awareness");
				return 1;
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "HpTrackerLoop: " + e);
				return 0;
			}
		}

		private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			try
			{
				Type type = args.LoadedAssembly.GetType(TrackerType);
				if (object.ReferenceEquals(type, null))
				{
					return;
				}
				AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
				Patch(type);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "HpTrackerLoop: " + e);
			}
		}

		private static bool Patch(Type tracker)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, tracker, "ShowElement", new Type[] { typeof(AgentUnit), typeof(bool) });
			FieldInfo elements = tracker.GetField("elements", BindingFlags.Instance | BindingFlags.Public);
			Type elementType = object.ReferenceEquals(elements, null) || !elements.FieldType.IsGenericType ? null : elements.FieldType.GetGenericArguments()[0];
			FieldInfo unit = object.ReferenceEquals(elementType, null) ? null : elementType.GetField("targetAgentUnit", BindingFlags.Instance | BindingFlags.Public);
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(unit, null) || !object.ReferenceEquals(unit.FieldType, typeof(AgentUnit)) || !typeof(System.Collections.IList).IsAssignableFrom(elements.FieldType) || !typeof(Component).IsAssignableFrom(elementType))
			{
				N.ULog.Warn(Tag, "HpTrackerLoop not installed: HP Awareness changed");
				return false;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "HpTrackerLoop not installed: " + owner + " patches HPTrackerUI.ShowElement");
				return false;
			}
			_elements = (ElementsFn)Getter(tracker, elements, typeof(System.Collections.IList), typeof(ElementsFn));
			_target = (TargetFn)Getter(elementType, unit, typeof(AgentUnit), typeof(TargetFn));
			if (!N.PatchUtil.Install(_harmony, Tag, target, new HarmonyMethod(typeof(HpTrackerLoop).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return false;
			}
			MethodBase update = N.PatchUtil.Resolve(Tag, tracker, "UpdateElement", new Type[] { typeof(AgentUnit) });
			string updateOwner = object.ReferenceEquals(update, null) ? null : Owners.ForeignAny(update);
			if (updateOwner != null)
			{
				N.ULog.Warn(Tag, "HpTrackerLoop skips UpdateElement: " + updateOwner + " patches it");
			}
			else if (!object.ReferenceEquals(update, null))
			{
				N.PatchUtil.Install(_harmony, Tag, update, new HarmonyMethod(typeof(HpTrackerLoop).GetMethod("UpdatePrefix", BindingFlags.Public | BindingFlags.Static)), null);
			}
			N.ULog.Info(Tag, "HpTrackerLoop: HPTrackerUI.ShowElement replaced");
			return true;
		}

		private static Delegate Getter(Type owner, FieldInfo field, Type result, Type fn)
		{
			DynamicMethod get = new DynamicMethod("HpTrackerLoop_" + field.Name, result, new Type[] { typeof(object) }, owner, true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, owner);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			return get.CreateDelegate(fn);
		}

		// Only an agent without an element goes on to the vanilla body, which makes one.
		public static bool UpdatePrefix(object __instance, AgentUnit agent)
		{
			System.Collections.IList list = _elements(__instance);
			for (int i = 0; i < list.Count; i++)
			{
				if (_target(list[i]) == agent)
				{
					return false;
				}
			}
			return true;
		}

		public static bool Prefix(object __instance, AgentUnit agent, bool show)
		{
			System.Collections.IList list = _elements(__instance);
			for (int i = 0; i < list.Count; i++)
			{
				object element = list[i];
				if (_target(element) == agent)
				{
					((Component)element).gameObject.SetActive(show);
					break;
				}
			}
			return false;
		}
	}

	// ---------- BufNameCache ----------
	public static class BufNameCache
	{
		private const string Tag = "PerfPack";
		private static ListFn _list;
		private static readonly Dictionary<Type, string> _names = new Dictionary<Type, string>();

		private delegate List<UnitBuf> ListFn(UnitModel m);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(UnitModel), "GetUnitBufByName", new Type[] { typeof(string) });
			FieldInfo field = typeof(UnitModel).GetField("_bufList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(field, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "BufNameCache not installed: " + owner + " patches UnitModel.GetUnitBufByName");
				return 0;
			}
			DynamicMethod get = new DynamicMethod("BufNameCache_List", typeof(List<UnitBuf>), new Type[] { typeof(UnitModel) }, typeof(UnitModel), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			_list = (ListFn)get.CreateDelegate(typeof(ListFn));
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(BufNameCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "BufNameCache installed");
			return 1;
		}

		public static bool Prefix(UnitModel __instance, string name, ref UnitBuf __result)
		{
			List<UnitBuf> list = _list(__instance);
			__result = null;
			for (int i = 0; i < list.Count; i++)
			{
				UnitBuf buf = list[i];
				Type type = buf.GetType();
				string known;
				if (!_names.TryGetValue(type, out known))
				{
					known = type.Name;
					_names[type] = known;
				}
				if (known == name)
				{
					__result = buf;
					break;
				}
			}
			return false;
		}
	}

	// ---------- FastFindCreature ----------
	public static class FastFindCreature
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;
		private static ListFn _list;
		private static readonly Dictionary<CreatureTypeInfo, bool> _vanilla = new Dictionary<CreatureTypeInfo, bool>();

		private delegate List<CreatureModel> ListFn(CreatureManager m);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase find = N.PatchUtil.Resolve(Tag, typeof(CreatureManager), "FindCreature", new Type[] { typeof(long) });
			MethodBase findMod = N.PatchUtil.Resolve(Tag, typeof(CreatureManager), "FindCreature_Mod", new Type[] { typeof(LobotomyBaseMod.LcIdLong) });
			FieldInfo field = typeof(CreatureManager).GetField("creatureList", BindingFlags.Instance | BindingFlags.NonPublic);
			if (object.ReferenceEquals(find, null) || object.ReferenceEquals(findMod, null) || object.ReferenceEquals(field, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(find);
			if (owner == null)
			{
				owner = Owners.ForeignAny(findMod);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "FastFindCreature not installed: " + owner + " patches CreatureManager.FindCreature");
				return 0;
			}
			DynamicMethod get = new DynamicMethod("FastFindCreature_List", typeof(List<CreatureModel>), new Type[] { typeof(CreatureManager) }, typeof(CreatureManager), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			_list = (ListFn)get.CreateDelegate(typeof(ListFn));
			_flag = flag;
			if (!N.PatchUtil.Install(harmony, Tag, find, new HarmonyMethod(typeof(FastFindCreature).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "FastFindCreature installed");
			InstallGet(harmony);
			return 1;
		}

		private static void InstallGet(HarmonyInstance harmony)
		{
			MethodBase get = N.PatchUtil.Resolve(Tag, typeof(CreatureManager), "GetCreature", new Type[] { typeof(long) });
			MethodBase getMod = N.PatchUtil.Resolve(Tag, typeof(CreatureManager), "GetCreature_Mod", new Type[] { typeof(LobotomyBaseMod.LcIdLong) });
			if (object.ReferenceEquals(get, null) || object.ReferenceEquals(getMod, null))
			{
				return;
			}
			string owner = Owners.ForeignAny(get);
			if (owner == null)
			{
				owner = Owners.ForeignAny(getMod);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "FastFindCreature GetCreature not installed: " + owner + " patches CreatureManager.GetCreature");
				return;
			}
			if (N.PatchUtil.Install(harmony, Tag, get, N.PatchUtil.WithPriority(typeof(FastFindCreature).GetMethod("GetPrefix", BindingFlags.Public | BindingFlags.Static), Priority.Last), null))
			{
				N.ULog.Info(Tag, "FastFindCreature GetCreature installed");
			}
		}

		// GetCreature_Mod(new LcIdLong(id)): the first creature with this id and an empty mod id, as in Prefix.
		public static bool GetPrefix(CreatureManager __instance, long id, ref CreatureModel __result)
		{
			return Prefix(__instance, id, ref __result);
		}

		// new LcIdLong(long) has an empty package, so a creature matches only with this id and an empty mod id.
		public static bool Prefix(CreatureManager __instance, long metaId, ref CreatureModel __result)
		{
			if (!_flag.On)
			{
				return true;
			}
			List<CreatureModel> list = _list(__instance);
			__result = null;
			for (int i = 0; i < list.Count; i++)
			{
				CreatureModel c = list[i];
				if (c.metadataId == metaId && IsVanilla(c.metaInfo))
				{
					__result = c;
					break;
				}
			}
			return false;
		}

		// GetModId allocates a closure per call and walks all mods; the mod list is fixed after loading.
		private static bool IsVanilla(CreatureTypeInfo info)
		{
			if (info == null)
			{
				return true;
			}
			bool vanilla;
			if (!_vanilla.TryGetValue(info, out vanilla))
			{
				vanilla = ((CreatureTypeList)CreatureTypeList.instance).GetModId(info) == "";
				_vanilla[info] = vanilla;
			}
			return vanilla;
		}
	}

	// ---------- NoticePool ----------
	public static class NoticePool
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;
		private static MapFn _map;
		private static int _thread;
		private static int _depth;
		private static readonly List<IObserver[]> _bufs = new List<IObserver[]>();

		private delegate Dictionary<string, List<IObserver>> MapFn(Notice n);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase send = N.PatchUtil.Resolve(Tag, typeof(Notice), "Send", new Type[] { typeof(string), typeof(object[]) });
			FieldInfo field = typeof(Notice).GetField("noticeList", BindingFlags.Instance | BindingFlags.NonPublic);
			if (object.ReferenceEquals(send, null) || object.ReferenceEquals(field, null) || !object.ReferenceEquals(field.FieldType, typeof(Dictionary<string, List<IObserver>>)))
			{
				N.ULog.Warn(Tag, "NoticePool not installed: Notice.Send or noticeList not found");
				return 0;
			}
			string owner = Owners.ForeignAny(send);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "NoticePool not installed: " + owner + " patches Notice.Send");
				return 0;
			}
			DynamicMethod get = new DynamicMethod("NoticePool_Map", typeof(Dictionary<string, List<IObserver>>), new Type[] { typeof(Notice) }, typeof(Notice), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			_map = (MapFn)get.CreateDelegate(typeof(MapFn));
			_thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
			_flag = flag;
			// Last priority: a foreign default-priority prefix runs first; a false from it skips this one as it skips the original.
			if (!N.PatchUtil.Install(harmony, Tag, send, N.PatchUtil.WithPriority(typeof(NoticePool).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static), Priority.Last), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "NoticePool installed (thread " + _thread + ")");
			return 1;
		}

		// Same as the original: a snapshot of the observers taken before the first call, called in list order. A send from an
		// observer uses the next depth's array. A null key, a missing map and other threads run the original.
		public static bool Prefix(Notice __instance, string notice, object[] param)
		{
			if (!_flag.On || notice == null || System.Threading.Thread.CurrentThread.ManagedThreadId != _thread)
			{
				return true;
			}
			Dictionary<string, List<IObserver>> map = _map(__instance);
			if (map == null)
			{
				return true;
			}
			List<IObserver> list;
			if (!map.TryGetValue(notice, out list))
			{
				return false;
			}
			int n = list.Count;
			if (_depth == _bufs.Count)
			{
				_bufs.Add(new IObserver[n < 16 ? 16 : n]);
			}
			IObserver[] buf = _bufs[_depth];
			if (buf.Length < n)
			{
				buf = new IObserver[n > buf.Length * 2 ? n : buf.Length * 2];
				_bufs[_depth] = buf;
			}
			list.CopyTo(buf, 0);
			_depth++;
			try
			{
				for (int i = 0; i < n; i++)
				{
					buf[i].OnNotice(notice, param);
				}
			}
			finally
			{
				_depth--;
				Array.Clear(buf, 0, n);
			}
			return false;
		}
	}

	// ---------- FontWarm ----------
	// Learns in the CurrentMetaIdMod setter, which every content change and close passes while the old content is still laid
	// out (CloseWindow, CreateWindow_Mod, OnChangeCreature and SetWindowType have try or foreach, GL-53). Warms on the
	// OnStageStart and LanaguageChange notices. Only en (ASCII) and ru/bg (ASCII + Cyrillic): other languages need glyph sets
	// too large for the atlas. Style Normal only. Sizes are atlas pixels (font size times the canvas scale).
	public static class FontWarm
	{
		private const string Tag = "PerfPack";
		private const int MaxSize = 200;
		private const int MaxSizes = 32;
		private const int MaxKeys = 16;
		private static N.Flag _flag;
		private static int _scanFrame = -1;
		private static bool _capWarned;
		private static readonly HashSet<string> _learnLogged = new HashSet<string>(StringComparer.Ordinal);
		private static string _path;
		private static string _ascii;
		private static string _cyrillic;
		// "lang height" -> font name -> sorted sizes.
		private static readonly Dictionary<string, Dictionary<string, List<int>>> _table = new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.Ordinal);

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(CreatureInfoWindow), "set_CurrentMetaIdMod", new Type[] { typeof(LobotomyBaseMod.LcIdLong) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			_flag = flag;
			_path = Path.Combine(N.UiHost.RootDir, "NailOptimization.fontwarm.txt");
			Load();
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(FontWarm).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			IObserver o = new FontWarmObserver();
			Notice.instance.Observe(NoticeName.OnStageStart, o);
			Notice.instance.Observe(NoticeName.LanaguageChange, o);
			N.ULog.Info(Tag, "FontWarm installed, " + _table.Count + " keys loaded");
			return 1;
		}

		// Records font and size of every active Text of the open window before its content changes; saves when a pair is new.
		public static void Prefix(CreatureInfoWindow __instance)
		{
			try
			{
				if (!_flag.On || !__instance.IsEnabled || _scanFrame == Time.frameCount)
				{
					return;
				}
				_scanFrame = Time.frameCount;
				string key = Key();
				if (key == null)
				{
					return;
				}
				Dictionary<string, List<int>> fonts;
				if (!_table.TryGetValue(key, out fonts))
				{
					if (_table.Count >= MaxKeys)
					{
						CapWarn("FontWarm: " + MaxKeys + " screen/language keys stored, " + key + " not learned");
						return;
					}
					fonts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
					_table[key] = fonts;
				}
				Component root = __instance.RootCanvas != null ? (Component)__instance.RootCanvas : __instance;
				Text[] ts = root.GetComponentsInChildren<Text>(false);
				bool added = false;
				int pairs = 0;
				for (int i = 0; i < ts.Length; i++)
				{
					Text t = ts[i];
					Font f = t.font;
					if (f == null || !f.dynamic || t.fontStyle != FontStyle.Normal)
					{
						continue;
					}
					int size = t.resizeTextForBestFit ? t.cachedTextGenerator.fontSizeUsedForBestFit : Mathf.RoundToInt(t.fontSize * t.pixelsPerUnit);
					if (Add(fonts, f.name, size))
					{
						added = true;
						pairs++;
					}
				}
				if (_learnLogged.Add(key))
				{
					N.ULog.Info(Tag, "FontWarm learned " + key + ": " + ts.Length + " texts, " + pairs + " new pairs");
				}
				if (added)
				{
					Save();
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "FontWarm learn failed: " + e);
			}
		}

		internal static void Warm()
		{
			if (!_flag.On)
			{
				return;
			}
			string key = Key();
			Dictionary<string, List<int>> fonts;
			if (key == null || !_table.TryGetValue(key, out fonts) || fonts.Count == 0)
			{
				return;
			}
			string chars = Chars(GlobalGameManager.instance.GetCurrentLanguage());
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Font[] all = Resources.FindObjectsOfTypeAll<Font>();
			int nf = 0;
			int ns = 0;
			System.Text.StringBuilder atlas = new System.Text.StringBuilder();
			for (int i = 0; i < all.Length; i++)
			{
				Font f = all[i];
				List<int> sizes;
				if (f == null || !f.dynamic || !fonts.TryGetValue(f.name, out sizes))
				{
					continue;
				}
				for (int k = 0; k < sizes.Count; k++)
				{
					f.RequestCharactersInTexture(chars, sizes[k], FontStyle.Normal);
				}
				nf++;
				ns += sizes.Count;
				Texture tex = f.material == null ? null : f.material.mainTexture;
				if (tex != null)
				{
					atlas.Append(' ').Append(f.name).Append(' ').Append(tex.width).Append('x').Append(tex.height);
				}
			}
			N.ULog.Info(Tag, "FontWarm: " + key + ", " + nf + " fonts, " + ns + " sizes, " + chars.Length + " chars, " + sw.ElapsedMilliseconds + " ms; atlas" + atlas.ToString());
		}

		// Null for languages that are not warmed or when the game manager is missing.
		private static string Key()
		{
			GlobalGameManager g = GlobalGameManager.instance;
			if (g == null)
			{
				return null;
			}
			string lang = g.GetCurrentLanguage();
			if (Chars(lang) == null)
			{
				return null;
			}
			return lang + " " + Screen.width + "x" + Screen.height;
		}

		private static string Chars(string lang)
		{
			if (_ascii == null)
			{
				System.Text.StringBuilder sb = new System.Text.StringBuilder();
				for (int c = 32; c < 127; c++)
				{
					sb.Append((char)c);
				}
				_ascii = sb.ToString();
				for (int c = 0x410; c < 0x450; c++)
				{
					sb.Append((char)c);
				}
				sb.Append((char)0x401).Append((char)0x451).Append((char)0xAB).Append((char)0xBB).Append((char)0x2014).Append((char)0x2013).Append((char)0x2026).Append((char)0x2116);
				_cyrillic = sb.ToString();
			}
			if (lang == "en")
			{
				return _ascii;
			}
			if (lang == "ru" || lang == "bg")
			{
				return _cyrillic;
			}
			return null;
		}

		// Inserts 'size' into the sorted list of 'font'; false when present or out of the caps.
		private static bool Add(Dictionary<string, List<int>> fonts, string font, int size)
		{
			if (size <= 0)
			{
				return false;
			}
			if (size > MaxSize)
			{
				CapWarn("FontWarm: size " + size + " of " + font + " over " + MaxSize + ", not warmed");
				return false;
			}
			List<int> sizes;
			if (!fonts.TryGetValue(font, out sizes))
			{
				sizes = new List<int>();
				fonts[font] = sizes;
			}
			int at = sizes.BinarySearch(size);
			if (at >= 0)
			{
				return false;
			}
			if (sizes.Count >= MaxSizes)
			{
				CapWarn("FontWarm: " + font + " has " + MaxSizes + " sizes, size " + size + " not warmed");
				return false;
			}
			sizes.Insert(~at, size);
			return true;
		}

		private static void CapWarn(string text)
		{
			if (!_capWarned)
			{
				_capWarned = true;
				N.ULog.Warn(Tag, text + " (shown once)");
			}
		}

		// One line per key and font: "lang WxH<TAB>font<TAB>size,size,...".
		private static void Load()
		{
			try
			{
				if (!File.Exists(_path) && File.Exists(_path + ".tmp"))
				{
					// A crash between Delete and Move of Save leaves only the temp file.
					File.Move(_path + ".tmp", _path);
				}
				if (!File.Exists(_path))
				{
					return;
				}
				string[] lines = File.ReadAllLines(_path);
				for (int i = 0; i < lines.Length; i++)
				{
					string[] cols = lines[i].Split('\t');
					if (cols.Length != 3)
					{
						continue;
					}
					Dictionary<string, List<int>> fonts;
					if (!_table.TryGetValue(cols[0], out fonts))
					{
						fonts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
						_table[cols[0]] = fonts;
					}
					string[] sizes = cols[2].Split(',');
					for (int k = 0; k < sizes.Length; k++)
					{
						int sz;
						if (int.TryParse(sizes[k], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sz))
						{
							Add(fonts, cols[1], sz);
						}
					}
				}
			}
			catch (Exception e)
			{
				_table.Clear();
				N.ULog.Warn(Tag, "FontWarm table unreadable, starts empty: " + e.Message);
			}
		}

		private static void Save()
		{
			try
			{
				System.Text.StringBuilder sb = new System.Text.StringBuilder();
				foreach (KeyValuePair<string, Dictionary<string, List<int>>> key in _table)
				{
					foreach (KeyValuePair<string, List<int>> font in key.Value)
					{
						sb.Append(key.Key).Append('\t').Append(font.Key).Append('\t');
						for (int i = 0; i < font.Value.Count; i++)
						{
							if (i > 0)
							{
								sb.Append(',');
							}
							sb.Append(font.Value[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
						}
						sb.Append("\r\n");
					}
				}
				string tmp = _path + ".tmp";
				File.WriteAllText(tmp, sb.ToString());
				if (File.Exists(_path))
				{
					File.Delete(_path);
				}
				File.Move(tmp, _path);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "FontWarm table not saved: " + e.Message);
			}
		}
	}

	internal sealed class FontWarmObserver : IObserver
	{
		public void OnNotice(string notice, params object[] param)
		{
			try
			{
				FontWarm.Warm();
			}
			catch (Exception e)
			{
				N.ULog.Error("PerfPack", "FontWarm warm failed: " + e);
			}
		}
	}

	// ---------- DescReuse ----------
	// The original has a try/finally around a Transform enumerator (not IDisposable, the finally does nothing); GrievantZongzi
	// already prefixes it, so the handler loss of GL-53 exists without us. Foreign prefixes run first (Priority.Last); a false
	// from one skips this prefix as it skips the original.
	public static class DescReuse
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;
		private static readonly char[] Star = new char[] { '*' };
		// Rows this prefix created; rows made by other code (GrievantZongzi colors its own) are never reused.
		private static readonly HashSet<int> _mine = new HashSet<int>();
		private static readonly List<Text> _keep = new List<Text>();
		private static readonly List<Text> _spare = new List<Text>();
		private static bool _logged;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(CreatureInfoWindow), "SetDesc", new Type[] { typeof(int) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPostOrTrans(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "DescReuse not installed: " + owner + " has a Postfix or Transpiler on CreatureInfoWindow.SetDesc");
				return 0;
			}
			_flag = flag;
			N.ResetRegistry.Register(new DescResetter());
			if (!N.PatchUtil.Install(harmony, Tag, m, N.PatchUtil.WithPriority(typeof(DescReuse).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static), Priority.Last), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "DescReuse installed");
			return 1;
		}

		// Same lines, order, sibling order, positions and list height as the original. Own rows of the last call are reused in
		// order, extra ones are hidden and kept; every other child of the list is destroyed as in the original. Any error runs
		// the original, which destroys all rows.
		public static bool Prefix(CreatureInfoWindow __instance, int level)
		{
			if (!_flag.On)
			{
				return true;
			}
			try
			{
				return Build(__instance, level);
			}
			catch (Exception e)
			{
				_keep.Clear();
				N.ULog.Error(Tag, "DescReuse failed, original used: " + e);
				return true;
			}
		}

		internal static void Clear()
		{
			_mine.Clear();
			_keep.Clear();
			_spare.Clear();
		}

		private static bool Build(CreatureInfoWindow __instance, int level)
		{
			CreatureTypeInfo meta = __instance.MetaInfo;
			RectTransform parent = __instance.listParent;
			if (meta == null || parent == null || __instance.descUnit == null)
			{
				return true;
			}
			List<Text> rows = __instance.descList;
			_keep.Clear();
			bool foreign = false;
			for (int i = 0; i < rows.Count; i++)
			{
				Text t = rows[i];
				if (t != null && _mine.Contains(t.GetInstanceID()))
				{
					if (t.transform.parent == parent)
					{
						_keep.Add(t);
					}
				}
				else
				{
					foreign = true;
				}
			}
			// Rows from another builder (the original or GrievantZongzi) mean it destroyed every child, spares included; Destroy
			// takes effect at the end of the frame, so they still look alive now. A builder that leaves descList empty is not seen.
			if (foreign)
			{
				_spare.Clear();
			}
			for (int i = _spare.Count - 1; i >= 0; i--)
			{
				if (_spare[i] == null || _spare[i].transform.parent != parent)
				{
					_spare.RemoveAt(i);
				}
			}
			rows.Clear();
			for (int i = parent.childCount - 1; i >= 0; i--)
			{
				Transform c = parent.GetChild(i);
				Text t = c.GetComponent<Text>();
				if (t == null || (!_keep.Contains(t) && !_spare.Contains(t)))
				{
					UnityEngine.Object.Destroy(c.gameObject);
				}
			}
			CreatureTypeInfo.ObserveTable table = meta.observeTable;
			int count = table.desc.Count;
			string unitName = CreatureModel.GetUnitName(meta, __instance.ObserveInfo);
			int used = 0;
			int made = 0;
			for (int i = 0; i < count; i++)
			{
				if (level < table.desc[i])
				{
					continue;
				}
				string[] parts = meta.desc[i].Split(Star);
				for (int j = 0; j < parts.Length; j++)
				{
					string text = parts[j];
					if (text.Equals(" ") || text.Equals(string.Empty))
					{
						continue;
					}
					Text row;
					if (used < _keep.Count)
					{
						row = _keep[used];
						used++;
					}
					else if (_spare.Count > 0)
					{
						row = _spare[_spare.Count - 1];
						_spare.RemoveAt(_spare.Count - 1);
						row.gameObject.SetActive(true);
						row.transform.localScale = Vector3.one;
					}
					else
					{
						GameObject go = UnityEngine.Object.Instantiate<GameObject>(__instance.descUnit);
						go.transform.SetParent(parent);
						go.transform.localScale = Vector3.one;
						row = go.GetComponent<Text>();
						_mine.Add(row.GetInstanceID());
						made++;
					}
					rows.Add(row);
					row.transform.SetAsLastSibling();
					row.text = text.Replace("$0", unitName);
				}
			}
			for (int i = used; i < _keep.Count; i++)
			{
				_keep[i].gameObject.SetActive(false);
				_spare.Add(_keep[i]);
			}
			_keep.Clear();
			float num = 0f;
			for (int k = 0; k < rows.Count; k++)
			{
				Text t = rows[k];
				t.rectTransform.anchoredPosition = new Vector2(0f, -num);
				num += t.preferredHeight + __instance.Spacing;
			}
			num += __instance.LowerSpacing;
			parent.sizeDelta = new Vector2(parent.sizeDelta.x, num);
			parent.anchoredPosition = Vector2.zero;
			if (!_logged && used > 0)
			{
				_logged = true;
				N.ULog.Info(Tag, "DescReuse: first reuse, rows kept " + used + ", new " + made + ", spare " + _spare.Count);
			}
			return false;
		}
	}

	// Row ids and spares of the last window must not outlive the day; the window may be rebuilt.
	internal sealed class DescResetter : N.IResettable
	{
		public void ResetState()
		{
			DescReuse.Clear();
		}
	}

	// ---------- OrchestraWorkers ----------
	// The original has no exception handler, GL-53 n/a.
	public static class OrchestraWorkers
	{
		private const string Tag = "PerfPack";
		private static readonly List<WorkerModel> _list = new List<WorkerModel>();
		private static bool _installing;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodInfo m = typeof(SilentOrchestra).GetMethod("UniqueEscape", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.DeclaringType, typeof(SilentOrchestra)))
			{
				N.ULog.Warn(Tag, "OrchestraWorkers not installed: SilentOrchestra.UniqueEscape not found");
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "OrchestraWorkers not installed: " + owner + " patches SilentOrchestra.UniqueEscape");
				return 0;
			}
			_installing = true;
			try
			{
				harmony.Patch(m, null, null, new HarmonyMethod(typeof(OrchestraWorkers).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "OrchestraWorkers install failed: " + e);
				return 0;
			}
			finally
			{
				_installing = false;
			}
			N.ResetRegistry.Register(new OrchestraResetter());
			N.ULog.Info(Tag, "OrchestraWorkers installed");
			return 1;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			int n = 0;
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "GetAllWorkers" && object.ReferenceEquals(mi.DeclaringType, typeof(SilentOrchestra)) && mi.GetParameters().Length == 0)
				{
					n++;
				}
			}
			if (n != 1)
			{
				// At our own install another body is left unpatched (Harmony applies nothing when the Transpiler throws). When a
				// later mod patches the method, Harmony reruns this Transpiler: its body is kept, so that mod's patch is not lost.
				if (_installing)
				{
					throw new InvalidOperationException("OrchestraWorkers: " + n + " GetAllWorkers calls in UniqueEscape, expected 1");
				}
				N.ULog.Warn(Tag, "OrchestraWorkers: UniqueEscape changed by another mod, left as is");
				return list;
			}
			MethodInfo workers = typeof(OrchestraWorkers).GetMethod("Workers", BindingFlags.Public | BindingFlags.Static);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "GetAllWorkers" && object.ReferenceEquals(mi.DeclaringType, typeof(SilentOrchestra)) && mi.GetParameters().Length == 0)
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = workers;
				}
			}
			return list;
		}

		// Same filter as SilentOrchestra.GetAllWorkers; the caller reads the list only within this tick.
		public static List<WorkerModel> Workers(SilentOrchestra self)
		{
			_list.Clear();
			List<WorkerModel> all = WorkerManager.instance.GetWorkerList();
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].IsAttackTargetable())
				{
					_list.Add(all[i]);
				}
			}
			return _list;
		}

		internal static void Clear()
		{
			_list.Clear();
		}
	}

	// The worker list must not keep dead workers of earlier days.
	internal sealed class OrchestraResetter : N.IResettable
	{
		public void ResetState()
		{
			OrchestraWorkers.Clear();
		}
	}

	// ---------- DistortionGrab ----------
	public static class DistortionGrab
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;
		private static Camera _cam;
		private static CommandBuffer _cb;
		private static RenderTexture _grab;
		private static int _createdFrame = -1;
		private static bool _failed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase render = N.PatchUtil.Resolve(Tag, typeof(RenderTextureCamera), "OnWillRenderObject", Type.EmptyTypes);
			MethodBase destroy = N.PatchUtil.Resolve(Tag, typeof(RenderTextureCamera), "OnDestroy", Type.EmptyTypes);
			if (object.ReferenceEquals(render, null) || object.ReferenceEquals(destroy, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(render);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "DistortionGrab not installed: " + owner + " patches RenderTextureCamera.OnWillRenderObject");
				return 0;
			}
			_flag = flag;
			if (!N.PatchUtil.Install(harmony, Tag, destroy, null, new HarmonyMethod(typeof(DistortionGrab).GetMethod("Release", BindingFlags.Public | BindingFlags.Static))))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, render, new HarmonyMethod(typeof(DistortionGrab).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "DistortionGrab installed");
			return 1;
		}

		public static void Release()
		{
			if (_cb != null)
			{
				if (_cam != null)
				{
					_cam.RemoveCommandBuffer(CameraEvent.AfterEverything, _cb);
				}
				_cb.Release();
				_cb = null;
			}
			if (_grab != null)
			{
				UnityEngine.Object.Destroy(_grab);
			}
			_grab = null;
			_cam = null;
		}

		private static void Create(RenderTextureCamera instance, Camera cam)
		{
			Release();
			_grab = new RenderTexture(Mathf.Max(2, cam.pixelWidth), Mathf.Max(2, cam.pixelHeight), 0, RenderTextureFormat.ARGB32);
			_cb = new CommandBuffer();
			_cb.name = "NailOptimization distortion grab";
			_cb.Blit(BuiltinRenderTextureType.CurrentActive, _grab);
			cam.AddCommandBuffer(CameraEvent.AfterEverything, _cb);
			_cam = cam;
			_createdFrame = Time.frameCount;
			N.ULog.Info(Tag, "DistortionGrab: " + instance.name + " layer " + instance.gameObject.layer + " on " + cam.name + ", " + _grab.width + "x" + _grab.height);
		}

		public static bool Prefix(RenderTextureCamera __instance)
		{
			if (!_flag.On)
			{
				if (_cb != null)
				{
					Release();
				}
				return true;
			}
			Camera current = Camera.current;
			if (_failed || current == null || (current != _cam && current != Camera.main))
			{
				return true;
			}
			if (_grab == null || current != _cam || _grab.width != Mathf.Max(2, current.pixelWidth) || _grab.height != Mathf.Max(2, current.pixelHeight))
			{
				// The grab is empty until this frame ends, so vanilla renders this one frame.
				try
				{
					Create(__instance, current);
				}
				catch (Exception e)
				{
					_failed = true;
					N.ULog.Warn(Tag, "DistortionGrab off, vanilla render: " + e.Message);
				}
				return true;
			}
			// Other instances of the frame that created the grab: the copy has not run yet.
			if (Time.frameCount == _createdFrame)
			{
				return true;
			}
			Material[] materials = __instance.GetComponent<Renderer>().sharedMaterials;
			for (int i = 0; i < materials.Length; i++)
			{
				if (materials[i] != null && materials[i].HasProperty("_RenderTexture"))
				{
					materials[i].SetTexture("_RenderTexture", _grab);
				}
			}
			return false;
		}
	}

	// ---------- GcQuiet ----------
	// Boehm runs a full collection about every 80-100 MB allocated (177-276 ms at a 400-560 MB heap, r168). GC.Collect resets
	// that count, so a collection in a quiet moment pushes the next automatic one a full interval later. Quiet moments inside
	// a day: the game paused for 0.5 s, the abnormality info window open for 0.3 s, the day end (state goes to STOP).
	// A collection runs only when the heap grew by GcQuietGrowMb since the last collection (ours or automatic) and our
	// last one is over 20 s ago. No patch: a driver polls 10 times a second on unscaled time. Live flag.
	public static class GcQuiet
	{
		private const string Tag = "PerfPack";
		private const float PauseQuiet = 0.5f;
		private const float WindowQuiet = 0.3f;
		private const float MinGap = 20f;
		internal static N.Flag Flag;
		internal static N.IntParam GrowMb;
		private static int _count = -1;
		private static long _base;
		private static float _quietSince = -1f;
		private static int _quietKind;
		private static float _last = -1000f;
		private static bool _inDay;
		private static N.Counter _ours;
		private static N.Counter _auto;

		internal static int Install()
		{
			_ours = N.Counters.Get(Tag, "gcQuiet");
			_auto = N.Counters.Get(Tag, "gcAutoInPlay");
			GameObject go = new GameObject("NailOptimization.PerfPack.GcQuiet");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<GcQuietDriver>();
			N.ULog.Info(Tag, "GcQuiet installed (" + (Flag.On ? "on" : "off") + ", grow " + GrowMb.Value + " MB, gap " + MinGap + " s)");
			return 1;
		}

		internal static void Poll()
		{
			int count = GC.CollectionCount(0);
			GameManager gm = GameManager.currentGameManager;
			bool day = gm != null && gm.state != GameState.STOP;
			bool paused = Time.timeScale == 0f;
			if (count != _count)
			{
				// Collections since the last poll that were not ours (ours update _count themselves).
				if (_count >= 0 && day && !paused)
				{
					_auto.Value += count - _count;
				}
				_count = count;
				_base = GC.GetTotalMemory(false);
			}
			bool wasDay = _inDay;
			_inDay = day;
			if (!Flag.On)
			{
				_quietKind = 0;
				return;
			}
			float now = Time.unscaledTime;
			if (wasDay && !day && gm != null)
			{
				_quietKind = 0;
				// EndGame: day end, also restart, checkpoint and return to title.
				TryCollect("stage stop", now);
				return;
			}
			int kind = 0;
			if (day)
			{
				CreatureInfoWindow w = CreatureInfoWindow.CurrentWindow;
				if (w != null && w.IsEnabled)
				{
					kind = 2;
				}
				else if (paused)
				{
					kind = 1;
				}
			}
			if (kind != _quietKind)
			{
				_quietKind = kind;
				_quietSince = now;
				return;
			}
			if (kind == 0 || now - _quietSince < (kind == 2 ? WindowQuiet : PauseQuiet))
			{
				return;
			}
			TryCollect(kind == 2 ? "info window" : "pause", now);
		}

		private static void TryCollect(string reason, float now)
		{
			if (now - _last < MinGap)
			{
				return;
			}
			long before = GC.GetTotalMemory(false);
			if (before - _base < (long)GrowMb.Value * 1048576L)
			{
				return;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			GC.Collect();
			long ms = sw.ElapsedMilliseconds;
			long after = GC.GetTotalMemory(false);
			_last = Time.unscaledTime;
			_count = GC.CollectionCount(0);
			_base = after;
			_ours.Inc();
			N.ULog.Info(Tag, "GcQuiet: collected at " + reason + ", heap " + (before >> 20) + " -> " + (after >> 20) + " MB, " + ms + " ms (ours " + _ours.Value + ", automatic in play " + _auto.Value + ")");
		}
	}

	public class GcQuietDriver : MonoBehaviour
	{
		private float _next;
		private bool _failed;

		public void Update()
		{
			float t = Time.unscaledTime;
			if (t < _next)
			{
				return;
			}
			_next = t + 0.1f;
			try
			{
				GcQuiet.Poll();
			}
			catch (Exception e)
			{
				_next = t + 10f;
				N.ULog.Warn("PerfPack", "GcQuiet poll failed: " + (_failed ? e.Message : e.ToString()));
				_failed = true;
			}
		}
	}

	// ---------- PauseSkeletons ----------
	public static class PauseSkeletons
	{
		private const string Tag = "PerfPack";
		private const int Grace = 2;
		private const int SlotMask = 31;
		private static N.Flag _flag;
		private static int _frame = -1;
		private static int _paused;
		private static bool _active;
		private static int _initFrame = -1;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase init = N.PatchUtil.Resolve(Tag, typeof(Spine.Unity.SkeletonRenderer), "Initialize", new Type[] { typeof(bool) });
			MethodBase mesh = N.PatchUtil.Resolve(Tag, typeof(Spine.Unity.SkeletonRenderer), "LateUpdate", Type.EmptyTypes);
			MethodBase pose = N.PatchUtil.Resolve(Tag, typeof(Spine.Unity.SkeletonAnimator), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(init, null) || object.ReferenceEquals(mesh, null) || object.ReferenceEquals(pose, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(mesh);
			if (owner == null)
			{
				owner = Owners.ForeignAny(pose);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "PauseSkeletons not installed: " + owner + " patches the skeleton update");
				return 0;
			}
			_flag = flag;
			HarmonyMethod prefix = new HarmonyMethod(typeof(PauseSkeletons).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod initPrefix = new HarmonyMethod(typeof(PauseSkeletons).GetMethod("InitPrefix", BindingFlags.Public | BindingFlags.Static));
			if (!N.PatchUtil.Install(harmony, Tag, init, initPrefix, null) || !N.PatchUtil.Install(harmony, Tag, pose, prefix, null) || !N.PatchUtil.Install(harmony, Tag, mesh, prefix, null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "PauseSkeletons installed");
			return 1;
		}

		// Every skeleton still comes up once in 32 frames (optimizeLevel can stretch that to seconds), so a change made during a pause shows up.
		// Initialize clears the mesh and expects the update of this frame to build it.
		public static void InitPrefix(Spine.Unity.SkeletonRenderer __instance, bool overwrite)
		{
			if (overwrite || !__instance.valid)
			{
				_initFrame = Time.frameCount;
			}
		}

		public static bool Prefix(Spine.Unity.SkeletonRenderer __instance)
		{
			int frame = Time.frameCount;
			if (frame != _frame)
			{
				_frame = frame;
				GameManager gm = GameManager.currentGameManager;
				_paused = (Time.timeScale == 0f && _flag.On && gm != null && gm.state != GameState.STOP) ? _paused + 1 : 0;
				_active = _paused > Grace;
			}
			if (!_active || frame == _initFrame || ((__instance.GetInstanceID() >> 1) & SlotMask) == (frame & SlotMask))
			{
				return true;
			}
			// Cutscenes (White Night, max observation) animate on unscaled time during a pause.
			Spine.Unity.SkeletonAnimator animated = __instance as Spine.Unity.SkeletonAnimator;
			if (animated != null && animated.Translator != null && animated.Translator.Animator != null && animated.Translator.Animator.updateMode == AnimatorUpdateMode.UnscaledTime)
			{
				return true;
			}
			return false;
		}
	}

	// ---------- AgentAnimLod ----------
	// Vanilla AgentUnit.UpdateAnimationQuality sets optimizeLevel from ortho / scale (2 above 30, 3 above 50, 4 above 80; 5 off camera),
	// and the skeleton then skips 1/2, 3/4 or 5/6 of its pose and mesh updates. Clerks already get 5 above 70. This moves agents
	// one step further (2->3, 3->4, 4->5); close views (levels 0 and 1) stay vanilla. Spine pose only: Animator state and events are untouched.
	public static class AgentAnimLod
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(AgentUnit), "UpdateAnimationQuality", Type.EmptyTypes);
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			_flag = flag;
			if (!N.PatchUtil.Install(harmony, Tag, target, null, new HarmonyMethod(typeof(AgentAnimLod).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static))))
			{
				return 0;
			}
			N.ULog.Info(Tag, "AgentAnimLod installed");
			return 1;
		}

		public static void Postfix(AgentUnit __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			Spine.Unity.SkeletonRenderer r = __instance.spineRenderer;
			if (r != null && r.optimizeLevel >= 2 && r.optimizeLevel < 5)
			{
				r.optimizeLevel++;
			}
		}
	}

	// ---------- ModSkeletonCache ----------
	public static class ModSkeletonCache
	{
		private const string Tag = "PerfPack";
		private static FieldInfo _data;

		// Source of the last parse per asset: a hit is served only while the source and the parsed data look unchanged.
		private sealed class Key
		{
			public string Json;
			public byte[] Bytes;
			public Spine.Unity.AtlasAsset[] Atlases;
			public Spine.Unity.AtlasAsset[] AtlasItems;
			public float Scale;
			public Spine.SkeletonData Data;
			public Spine.Unity.SkeletonDataAsset Asset;
			public int Skins;
			public int Animations;
			public int Slots;
			public int Attachments;
		}

		private static readonly Dictionary<int, Key> Keys = new Dictionary<int, Key>();
		private static readonly HashSet<int> Mutable = new HashSet<int>();
		private static readonly List<int> Dead = new List<int>();
		private static int _logs;
		private static int _errors;
		private static N.Counter _hit;
		private static N.Counter _parse;
		private static N.Counter _mutable;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			HarmonyMethod prefix = new HarmonyMethod(typeof(ModSkeletonCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod postfix = new HarmonyMethod(typeof(ModSkeletonCache).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
			Type asset = typeof(Spine.Unity.SkeletonDataAsset);
			_data = asset.GetField("skeletonData", BindingFlags.NonPublic | BindingFlags.Instance);
			if (object.ReferenceEquals(_data, null))
			{
				N.ULog.Warn(Tag, "ModSkeletonCache not installed: no skeletonData field");
				return 0;
			}
			MethodBase text = N.PatchUtil.Resolve(Tag, asset, "GetSkeletonData_string", new Type[] { typeof(bool) });
			MethodBase bytes = N.PatchUtil.Resolve(Tag, asset, "GetSkeletonData_Byte", new Type[] { typeof(bool) });
			if (object.ReferenceEquals(text, null) || object.ReferenceEquals(bytes, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, text, prefix, postfix) || !N.PatchUtil.Install(harmony, Tag, bytes, prefix, postfix))
			{
				return 0;
			}
			_hit = N.Counters.Get(Tag, "modSkelHit");
			_parse = N.Counters.Get(Tag, "modSkelParse");
			_mutable = N.Counters.Get(Tag, "modSkelMutable");
			N.Events.On(NoticeName.OnStageStart, Prune, Tag);
			N.ULog.Info(Tag, "ModSkeletonCache installed");
			return 1;
		}

		public static bool Prefix(Spine.Unity.SkeletonDataAsset __instance, ref Spine.SkeletonData __result)
		{
			try
			{
				Spine.SkeletonData data = _data.GetValue(__instance) as Spine.SkeletonData;
				if (data == null)
				{
					return true;
				}
				int id = __instance.GetInstanceID();
				if (Mutable.Contains(id))
				{
					return true;
				}
				Key key;
				// No parse seen by the cache yet: parse once so the key matches what is served.
				if (!Keys.TryGetValue(id, out key) || !SameSource(key, __instance, data))
				{
					return true;
				}
				if (key.Skins != Count(data.Skins) || key.Animations != Count(data.Animations) || key.Slots != Count(data.Slots)
					|| key.Attachments != AttachmentCount(data))
				{
					// A mod edits the returned data per call: vanilla gives it a fresh parse every time, so the cache stays off for it.
					Mutable.Add(id);
					_mutable.Value++;
					Log("ModSkeletonCache: " + __instance.name + " data changed after the parse, cache off for this asset");
					return true;
				}
				_hit.Value++;
				__result = data;
				return false;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "ModSkeletonCache prefix failed, original used (logged once): " + e);
				}
				return true;
			}
		}

		public static void Postfix(Spine.Unity.SkeletonDataAsset __instance, Spine.SkeletonData __result)
		{
			if (__result == null)
			{
				return;
			}
			Key old;
			// Harmony 1 runs the postfix after a served hit too: the key is already current then.
			if (Keys.TryGetValue(__instance.GetInstanceID(), out old) && object.ReferenceEquals(old.Data, __result))
			{
				return;
			}
			_parse.Value++;
			Key key = new Key();
			key.Asset = __instance;
			key.Json = __instance.skeletonJSON_string;
			key.Bytes = __instance.skeletonSKEL_byte;
			key.Atlases = __instance.atlasAssets;
			key.AtlasItems = __instance.atlasAssets == null ? null : (Spine.Unity.AtlasAsset[])__instance.atlasAssets.Clone();
			key.Scale = __instance.scale;
			key.Data = __result;
			key.Skins = Count(__result.Skins);
			key.Animations = Count(__result.Animations);
			key.Slots = Count(__result.Slots);
			key.Attachments = AttachmentCount(__result);
			Keys[__instance.GetInstanceID()] = key;
		}

		// Keys of destroyed runtime assets would keep their parsed data and source text for the whole session.
		private static void Prune()
		{
			Dead.Clear();
			Dictionary<int, Key>.Enumerator it = Keys.GetEnumerator();
			while (it.MoveNext())
			{
				if (it.Current.Value.Asset == null)
				{
					Dead.Add(it.Current.Key);
				}
			}
			for (int i = 0; i < Dead.Count; i++)
			{
				Keys.Remove(Dead[i]);
				Mutable.Remove(Dead[i]);
			}
			if (Dead.Count > 0)
			{
				N.ULog.Info(Tag, "ModSkeletonCache: pruned " + Dead.Count + " destroyed assets, " + Keys.Count + " kept");
			}
		}

		private static int AttachmentCount(Spine.SkeletonData data)
		{
			Spine.ExposedList<Spine.Skin> skins = data.Skins;
			if (skins == null)
			{
				return -1;
			}
			int n = 0;
			for (int i = 0; i < skins.Count; i++)
			{
				Spine.Skin s = skins.Items[i];
				if (s != null)
				{
					n += s.Attachments.Count;
				}
			}
			return n;
		}

		private static bool SameSource(Key key, Spine.Unity.SkeletonDataAsset asset, Spine.SkeletonData data)
		{
			if (!object.ReferenceEquals(key.Data, data) || !object.ReferenceEquals(key.Json, asset.skeletonJSON_string)
				|| !object.ReferenceEquals(key.Bytes, asset.skeletonSKEL_byte) || !object.ReferenceEquals(key.Atlases, asset.atlasAssets)
				|| key.Scale != asset.scale)
			{
				return false;
			}
			Spine.Unity.AtlasAsset[] now = asset.atlasAssets;
			if (now == null)
			{
				return key.AtlasItems == null;
			}
			if (key.AtlasItems == null || key.AtlasItems.Length != now.Length)
			{
				return false;
			}
			for (int i = 0; i < now.Length; i++)
			{
				if (!object.ReferenceEquals(key.AtlasItems[i], now[i]))
				{
					return false;
				}
			}
			return true;
		}

		private static int Count<T>(Spine.ExposedList<T> list)
		{
			return list == null ? -1 : list.Count;
		}

		private static void Log(string text)
		{
			if (_logs < 20)
			{
				_logs++;
				N.ULog.Info(Tag, text);
			}
		}
	}

	// ---------- WitnessSpread ----------
	public static class WitnessSpread
	{
		private const string Tag = "PerfPack";
		public static N.IntParam Budget;
		private static readonly Queue<KeyValuePair<AgentModel, AgentModel>> _queue = new Queue<KeyValuePair<AgentModel, AgentModel>>();
		private static readonly Dictionary<AgentModel, float> _pending = new Dictionary<AgentModel, float>();
		private static bool _draining;
		private static bool _warned;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve(Tag, typeof(AgentModel), "HorrorDamageByDead", new Type[] { typeof(AgentModel) });
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(GameManager), "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(target);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "WitnessSpread not installed: " + owner + " patches HorrorDamageByDead");
				return 0;
			}
			// The pump goes first: a queue without a pump would lose reactions.
			if (!N.PatchUtil.Install(harmony, Tag, tick, new HarmonyMethod(typeof(WitnessSpread).GetMethod("Pump", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, target, new HarmonyMethod(typeof(WitnessSpread).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ResetRegistry.Register(new WitnessResetter());
			N.ULog.Info(Tag, "WitnessSpread installed");
			return 1;
		}

		public static bool Prefix(AgentModel __instance, AgentModel target)
		{
			if (_draining)
			{
				return true;
			}
			try
			{
				GameManager gm = GameManager.currentGameManager;
				if (gm == null || gm.state != GameState.PLAYING)
				{
					return true;
				}
				// HorrorDamageByDead: 10/30/60/100 % of max mental by level difference; armor W resistance is not applied
				// (TakeDamageWithoutEffect); 1.5 leaves room for equipment OnTakeDamage modifiers.
				int diff = target.level - __instance.level;
				float share = diff <= -1 ? 0f : (diff == 0 ? 0.1f : (diff == 1 ? 0.3f : (diff == 2 ? 0.6f : 1f)));
				// Queued reactions count: several deaths in one tick can panic a witness that one death cannot.
				float need = __instance.maxMental * share * 1.5f;
				float pending;
				_pending.TryGetValue(__instance, out pending);
				if (__instance.mental <= need + pending)
				{
					Drain(long.MaxValue);
					return true;
				}
				_pending[__instance] = pending + need;
				_queue.Enqueue(new KeyValuePair<AgentModel, AgentModel>(__instance, target));
				return false;
			}
			catch (Exception e)
			{
				if (!_warned)
				{
					_warned = true;
					N.ULog.Warn(Tag, "WitnessSpread prefix failed, the game's reaction runs: " + e);
				}
				return true;
			}
		}

		internal static void Clear()
		{
			_queue.Clear();
			_pending.Clear();
		}

		public static void Pump(GameManager __instance)
		{
			if (_queue.Count == 0)
			{
				return;
			}
			if (__instance.state == GameState.STOP)
			{
				_queue.Clear();
				_pending.Clear();
				return;
			}
			if (__instance.state != GameState.PLAYING)
			{
				return;
			}
			Drain(System.Diagnostics.Stopwatch.GetTimestamp() + Budget.Value * System.Diagnostics.Stopwatch.Frequency / 1000L);
		}

		private static void Drain(long end)
		{
			_draining = true;
			try
			{
				while (_queue.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() < end)
				{
					KeyValuePair<AgentModel, AgentModel> e = _queue.Dequeue();
					if (e.Key != null && !e.Key.IsDead())
					{
						e.Key.HorrorDamageByDead(e.Value);
					}
				}
			}
			catch (Exception ex)
			{
				N.ULog.Warn(Tag, "WitnessSpread failed: " + ex.Message);
			}
			finally
			{
				_draining = false;
				if (_queue.Count == 0)
				{
					_pending.Clear();
				}
			}
		}
	}

	// ---------- SkinCache ----------
	internal sealed class SkinEntry
	{
		public Texture2D Tex;
		public Material Mat;
		public int Gen;
	}

	internal sealed class WitnessResetter : N.IResettable
	{
		public void ResetState()
		{
			WitnessSpread.Clear();
		}
	}

	internal sealed class BonusPoolResetter : N.IResettable
	{
		public void ResetState()
		{
			BonusPool.Clear();
		}
	}

	internal sealed class RoomScanResetter : N.IResettable
	{
		public void ResetState()
		{
			RoomScan.Clear();
		}
	}

	internal sealed class SkinCacheResetter : N.IResettable
	{
		public void ResetState()
		{
			SkinCache.MarkPrune();
		}
	}

	public static class SkinCache
	{
		private const string Tag = "PerfPack";
		internal static bool Stats;
		private delegate Texture2D ToTextureFn(Sprite s, bool apply, TextureFormat format, bool mipmaps);
		private delegate void ApplyPmaFn(Texture2D t, bool apply);
		private delegate Spine.AtlasRegion ToRegionFn(Sprite s, bool isolated);
		private static ToTextureFn _toTexture;
		private static ApplyPmaFn _applyPma;
		private static ToRegionFn _toRegion;
		private static readonly Dictionary<string, SkinEntry> _cache = new Dictionary<string, SkinEntry>();
		private static N.Counter _hits;
		private static N.Counter _misses;
		private static float _nextStats;
		private static bool _pruneDue;
		private static int _gen;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type t = typeof(AtlasUtilities);
			BindingFlags stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
			MethodInfo tex = t.GetMethod("ToTexture", stat, null, new Type[] { typeof(Sprite), typeof(bool), typeof(TextureFormat), typeof(bool) }, null);
			MethodInfo pma = t.GetMethod("ApplyPMA", stat, null, new Type[] { typeof(Texture2D), typeof(bool) }, null);
			MethodInfo reg = t.GetMethod("ToAtlasRegion", stat, null, new Type[] { typeof(Sprite), typeof(bool) }, null);
			if (object.ReferenceEquals(tex, null) || object.ReferenceEquals(pma, null) || object.ReferenceEquals(reg, null))
			{
				N.ULog.Warn(Tag, "SkinCache not installed: AtlasUtilities helpers not found");
				return 0;
			}
			MethodBase m = N.PatchUtil.Resolve(Tag, t, "ToAtlasRegionPMAClone", new Type[] { typeof(Sprite), typeof(Shader), typeof(TextureFormat), typeof(bool), typeof(Material) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			try
			{
				_toTexture = (ToTextureFn)Delegate.CreateDelegate(typeof(ToTextureFn), tex);
				_applyPma = (ApplyPmaFn)Delegate.CreateDelegate(typeof(ApplyPmaFn), pma);
				_toRegion = (ToRegionFn)Delegate.CreateDelegate(typeof(ToRegionFn), reg);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "SkinCache not installed: " + e.Message);
				return 0;
			}
			_hits = N.Counters.Get(Tag, "skinHits");
			_misses = N.Counters.Get(Tag, "skinMisses");
			string owner = Owners.ForeignPrefix(m);
			if (owner != null)
			{
				N.ULog.Info(Tag, "SkinCache runs before the Prefix of " + owner + ", which is skipped");
			}
			HarmonyMethod prefix = new HarmonyMethod(typeof(SkinCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			prefix.prioritiy = Priority.First;
			if (!N.PatchUtil.Install(harmony, Tag, m, prefix, null))
			{
				return 0;
			}
			N.ResetRegistry.Register(new SkinCacheResetter());
			N.ULog.Info(Tag, "SkinCache installed (AtlasUtilities.ToAtlasRegionPMAClone)");
			return 1;
		}

		// Day reset: each entry carries the day of its last use; at the first miss after a reset (the new day builds its
		// skins after the scene reload) entries unused for a whole day are destroyed. Sprite references are not kept:
		// a static reference would stop Unity from unloading the sprite.
		internal static void MarkPrune()
		{
			_gen++;
			_pruneDue = true;
		}

		private static void Prune()
		{
			List<string> dead = new List<string>();
			foreach (KeyValuePair<string, SkinEntry> kv in _cache)
			{
				SkinEntry e = kv.Value;
				if (e.Gen < _gen - 1 || e.Tex == null || e.Mat == null)
				{
					dead.Add(kv.Key);
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				SkinEntry e = _cache[dead[i]];
				_cache.Remove(dead[i]);
				if (e.Mat != null)
				{
					UnityEngine.Object.Destroy(e.Mat);
				}
				if (e.Tex != null)
				{
					UnityEngine.Object.Destroy(e.Tex);
				}
			}
			N.ULog.Info(Tag, "SkinCache pruned " + dead.Count + " entries, kept " + _cache.Count);
		}

		public static bool Prefix(ref Spine.AtlasRegion __result, Sprite s, Shader shader, TextureFormat textureFormat, bool mipmaps, Material materialPropertySource)
		{
			try
			{
				if (s == null || shader == null)
				{
					return true;
				}
				int source = materialPropertySource == null ? 0 : materialPropertySource.GetInstanceID();
				string key = s.GetInstanceID() + "|" + shader.GetInstanceID() + "|" + (int)textureFormat + "|" + (mipmaps ? "1" : "0") + "|" + source;
				SkinEntry e;
				if (_cache.TryGetValue(key, out e) && e.Tex != null && e.Mat != null)
				{
					e.Gen = _gen;
					_hits.Value++;
				}
				else
				{
					if (e != null && e.Mat != null)
					{
						UnityEngine.Object.Destroy(e.Mat);
					}
					if (_pruneDue)
					{
						_pruneDue = false;
						Prune();
					}
					e = Build(s, shader, textureFormat, mipmaps, materialPropertySource);
					_cache[key] = e;
					_misses.Value++;
				}
				Spine.AtlasRegion region = _toRegion(s, true);
				region.page = e.Mat.ToSpineAtlasPage();
				__result = region;
				if (Stats && Time.unscaledTime >= _nextStats)
				{
					_nextStats = Time.unscaledTime + 10f;
					LogStats();
				}
				return false;
			}
			catch (Exception ex)
			{
				N.ULog.Warn(Tag, "SkinCache call failed, game code used: " + ex.Message);
				return true;
			}
		}

		private static SkinEntry Build(Sprite s, Shader shader, TextureFormat format, bool mipmaps, Material source)
		{
			Material mat = new Material(shader);
			if (source != null)
			{
				mat.CopyPropertiesFromMaterial(source);
				mat.shaderKeywords = source.shaderKeywords;
			}
			Texture2D tex = _toTexture(s, false, format, mipmaps);
			_applyPma(tex, true);
			// Not "-pma-": the VBlankFF mod destroys textures by that name.
			tex.name = s.name + "-pmac-";
			mat.name = tex.name + shader.name;
			mat.mainTexture = tex;
			SkinEntry e = new SkinEntry();
			e.Tex = tex;
			e.Mat = mat;
			e.Gen = _gen;
			return e;
		}

		private static void LogStats()
		{
			N.ULog.Info(Tag, "SkinCache entries=" + _cache.Count + " hits=" + _hits.Value + " misses=" + _misses.Value + " materials=" + Resources.FindObjectsOfTypeAll(typeof(Material)).Length + " textures=" + Resources.FindObjectsOfTypeAll(typeof(Texture2D)).Length + " meshes=" + Resources.FindObjectsOfTypeAll(typeof(Mesh)).Length);
		}
	}

	// ---------- RoomScan ----------
	// Every clerk and agent walks its whole room each tick looking for a hostile target. A hostile target is never a
	// controlled worker (WorkerModel.IsHostile), so a room holding only those is answered once per tick. The scan of
	// OfficerModel.ProcessActionNormalOfficer and AgentModel.OnFixedUpdate uses foreach and cannot be patched (GL-53);
	// the call right before it arms a flag and the scan's own GetEnteredTargets gets an empty list.
	public static class RoomScan
	{
		private const string Tag = "PerfPack";
		private static readonly System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> Empty = new List<MovableObjectNode>().AsReadOnly();
		private static readonly Dictionary<PassageObjectModel, float> _rooms = new Dictionary<PassageObjectModel, float>();
		private static bool _armed;
		private static bool _logged;
		private static FieldInfo _units;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			_units = AccessTools.Field(typeof(PassageObjectModel), "enteredUnitList");
			if (object.ReferenceEquals(_units, null))
			{
				N.ULog.Warn(Tag, "RoomScan not installed: no enteredUnitList");
				return 0;
			}
			MethodBase list = N.PatchUtil.Resolve(Tag, typeof(PassageObjectModel), "GetEnteredTargets", Type.EmptyTypes);
			MethodBase hostile = N.PatchUtil.Resolve(Tag, typeof(WorkerModel), "IsHostile", new Type[] { typeof(UnitModel) });
			MethodBase queue = N.PatchUtil.Resolve(Tag, typeof(WorkerCommandQueue), "Execute", new Type[] { typeof(WorkerModel) });
			MethodBase bufs = N.PatchUtil.Resolve(Tag, typeof(UnitModel), "UpdateBufState", Type.EmptyTypes);
			MethodBase action = N.PatchUtil.Resolve(Tag, typeof(OfficerModel), "ProcessAction", Type.EmptyTypes);
			if (object.ReferenceEquals(list, null) || object.ReferenceEquals(hostile, null) || object.ReferenceEquals(queue, null) || object.ReferenceEquals(bufs, null) || object.ReferenceEquals(action, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(hostile);
			if (owner == null)
			{
				owner = Owners.ForeignAny(list);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "RoomScan not installed: " + owner + " patches the hostility check");
				return 0;
			}
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			if (!N.PatchUtil.Install(harmony, Tag, list, new HarmonyMethod(typeof(RoomScan).GetMethod("ListPrefix", pub)), null)
				|| !N.PatchUtil.Install(harmony, Tag, action, null, new HarmonyMethod(typeof(RoomScan).GetMethod("Disarm", pub)))
				|| !N.PatchUtil.Install(harmony, Tag, queue, null, new HarmonyMethod(typeof(RoomScan).GetMethod("AfterQueue", pub)))
				|| !N.PatchUtil.Install(harmony, Tag, bufs, null, new HarmonyMethod(typeof(RoomScan).GetMethod("AfterBufs", pub))))
			{
				return 0;
			}
			N.ResetRegistry.Register(new RoomScanResetter());
			N.ULog.Info(Tag, "RoomScan installed");
			return 1;
		}

		internal static void Clear()
		{
			_rooms.Clear();
			_armed = false;
		}

		public static void Disarm()
		{
			_armed = false;
		}

		// OfficerModel.ProcessAction runs the queue, then ProcessActionNormalOfficer; these are its conditions for the scan.
		public static void AfterQueue(WorkerModel agent)
		{
			OfficerModel officer = agent as OfficerModel;
			if (officer != null && officer.OnWorkEndFlag && officer.CurrentPanicAction == null && officer.unconAction == null && officer.GetMovableNode().GetPassage() != null)
			{
				_armed = true;
			}
		}

		// AgentModel.OnFixedUpdate: UpdateBufState, then the scan under these conditions.
		public static void AfterBufs(UnitModel __instance)
		{
			AgentModel agent = __instance as AgentModel;
			if (agent != null && !agent.willDead && !agent.CannotControll() && agent.GetMovableNode().GetPassage() != null)
			{
				_armed = true;
			}
		}

		public static bool ListPrefix(PassageObjectModel __instance, ref System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> __result)
		{
			if (!_armed)
			{
				return true;
			}
			_armed = false;
			if (!IsCalm(__instance))
			{
				return true;
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(Tag, "RoomScan active");
			}
			__result = Empty;
			return false;
		}

		// The stored value is the tick + 1, negative for a room that is not calm.
		private static bool IsCalm(PassageObjectModel room)
		{
			float now = Time.fixedTime + 1f;
			float seen;
			if (_rooms.TryGetValue(room, out seen))
			{
				if (seen == now)
				{
					return true;
				}
				if (seen == -now)
				{
					return false;
				}
			}
			else if (_rooms.Count > 4096)
			{
				_rooms.Clear();
			}
			List<MovableObjectNode> units = (List<MovableObjectNode>)_units.GetValue(room);
			bool calm = true;
			for (int i = 0; i < units.Count; i++)
			{
				WorkerModel worker = units[i].GetUnit() as WorkerModel;
				if (worker == null || worker.IsPanic() || worker.CannotControll())
				{
					calm = false;
					break;
				}
			}
			_rooms[room] = calm ? now : -now;
			return calm;
		}
	}

	// ---------- ClerkImpostor ----------
	// With very many clerks at a wide zoom, each clerk is baked once into a shared render texture and drawn as one quad; animators, skeletons, meshes and canvases stop, logic keeps running.
	// Panicking and dead clerks stay real. GameManager.Update, not FixedUpdate: zooming also works on pause.
	public static class ClerkImpostor
	{
		private const string Tag = "PerfPack";
		private const int MinClerks = 20;
		private const float MinOrtho = 35f;
		private const int PerFrame = 100;
		private const int BakesPerFrame = 10;
		private const int AtlasSize = 2048;
		private const float MaxPartDistance = 6f;
		private const int SlotW = 40;
		private const int SlotH = 80;
		private const int BakeLayer = 31;

		private class Item
		{
			public OfficerUnit Unit;
			public int Slot = -1;
			public bool Baked;
			public bool On;
			public GameObject Quad;
			public Mesh Mesh;
			public List<Behaviour> Off = new List<Behaviour>();
			public List<Renderer> Hidden = new List<Renderer>();
		}

		private static readonly Dictionary<OfficerUnit, Item> _items = new Dictionary<OfficerUnit, Item>();
		private static readonly Stack<int> _free = new Stack<int>();
		private static int _nextSlot;
		private static int _cursor;
		private static int _on;
		private static bool _failed;
		private static RenderTexture _atlas;
		private static Material _material;
		private static Camera _camera;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(update, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, update, new HarmonyMethod(typeof(ClerkImpostor).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "ClerkImpostor installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			if (_failed)
			{
				return;
			}
			try
			{
				if (__instance.state == GameState.STOP)
				{
					if (_items.Count > 0)
					{
						Clear();
					}
					return;
				}
				Camera cam = Camera.main;
				if (cam == null || OfficerManager.instance == null)
				{
					return;
				}
				IList<OfficerModel> list = OfficerManager.instance.GetOfficerList();
				bool wide = cam.orthographicSize >= MinOrtho && list.Count >= MinClerks;
				if (!wide && _on == 0)
				{
					return;
				}
				int bakes = BakesPerFrame;
				int count = Math.Min(PerFrame, list.Count);
				for (int i = 0; i < count; i++)
				{
					if (_cursor >= list.Count)
					{
						_cursor = 0;
						Purge();
					}
					Process(list[_cursor++], wide, ref bakes);
				}
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Warn(Tag, "ClerkImpostor failed: " + e);
				Clear();
			}
		}

		private static void Process(OfficerModel model, bool wide, ref int bakes)
		{
			OfficerUnit unit = model.GetUnit();
			if (unit == null)
			{
				return;
			}
			bool alive = !model.IsDead() && !model.IsPanic() && !model.IsCrazy();
			Item it;
			_items.TryGetValue(unit, out it);
			if (it != null && !alive)
			{
				// Whatever changed its look meanwhile must not come back in the old picture.
				it.Baked = false;
			}
			if (wide && alive && unit.gameObject.activeInHierarchy)
			{
				if (it == null)
				{
					it = new Item();
					it.Unit = unit;
					_items[unit] = it;
				}
				if (!it.On && (it.Baked || (bakes > 0 && Bake(it, ref bakes))))
				{
					Enter(it);
				}
			}
			else if (it != null && it.On)
			{
				Exit(it);
			}
		}

		private static bool Bake(Item it, ref int bakes)
		{
			Renderer[] all = it.Unit.GetComponentsInChildren<Renderer>(false);
			Bounds b = new Bounds();
			bool any = false;
			Renderer first = null;
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].enabled)
				{
					// An empty or distant renderer would stretch the picture and leave the clerk a few pixels in it.
					Bounds part = all[i].bounds;
					if (part.size.y < 0.01f)
					{
						continue;
					}
					// Body meshes are still empty on the first frames of a day: without one the bake would hold only the face sprites, so it waits.
					if (first == null && all[i] is MeshRenderer && all[i].GetComponentInParent<Spine.Unity.SkeletonRenderer>() != null)
					{
						first = all[i];
					}
					if (!any)
					{
						b = part;
						any = true;
					}
					else if (Mathf.Abs(part.center.x - b.center.x) < MaxPartDistance && Mathf.Abs(part.center.y - b.center.y) < MaxPartDistance)
					{
						b.Encapsulate(part);
					}
				}
			}
			if (!any || first == null || first.sharedMaterial == null || b.size.y < 0.01f)
			{
				return false;
			}
			if (it.Slot < 0)
			{
				if (_free.Count > 0)
				{
					it.Slot = _free.Pop();
				}
				else if (_nextSlot < (AtlasSize / SlotW) * (AtlasSize / SlotH))
				{
					it.Slot = _nextSlot++;
				}
				else
				{
					return false;
				}
			}
			bakes--;
			if (_atlas == null)
			{
				_atlas = new RenderTexture(AtlasSize, AtlasSize, 0, RenderTextureFormat.ARGB32);
				_atlas.name = "ClerkImpostorAtlas";
				_material = new Material(first.sharedMaterial.shader);
				_material.mainTexture = _atlas;
				GameObject go = new GameObject("ClerkImpostorCamera");
				UnityEngine.Object.DontDestroyOnLoad(go);
				_camera = go.AddComponent<Camera>();
				_camera.enabled = false;
				_camera.orthographic = true;
				_camera.clearFlags = CameraClearFlags.SolidColor;
				_camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
				_camera.cullingMask = 1 << BakeLayer;
				_camera.nearClipPlane = 0.1f;
				_camera.farClipPlane = 400f;
				_camera.targetTexture = _atlas;
			}
			int columns = AtlasSize / SlotW;
			float u = (float)(it.Slot % columns * SlotW) / AtlasSize;
			float v = (float)(it.Slot / columns * SlotH) / AtlasSize;
			float du = (float)SlotW / AtlasSize;
			float dv = (float)SlotH / AtlasSize;
			float h = Mathf.Max(b.size.y, b.size.x * 2f) * 1.05f;
			float w = h * 0.5f;
			Vector3 center = b.center;

			int[] layers = new int[all.Length];
			for (int i = 0; i < all.Length; i++)
			{
				layers[i] = all[i].gameObject.layer;
				all[i].gameObject.layer = BakeLayer;
			}
			_camera.rect = new Rect(u, v, du, dv);
			_camera.orthographicSize = h * 0.5f;
			_camera.transform.position = new Vector3(center.x, center.y, center.z - 200f);
			try
			{
				_camera.Render();
			}
			finally
			{
				for (int i = 0; i < all.Length; i++)
				{
					all[i].gameObject.layer = layers[i];
				}
			}

			if (it.Quad == null)
			{
				Spine.Unity.SkeletonRenderer spine = it.Unit.GetComponentInChildren<Spine.Unity.SkeletonRenderer>();
				it.Quad = new GameObject("clerk_impostor");
				it.Quad.transform.SetParent(spine != null ? spine.transform : it.Unit.transform, false);
				it.Mesh = new Mesh();
				it.Quad.AddComponent<MeshFilter>().sharedMesh = it.Mesh;
				MeshRenderer r = it.Quad.AddComponent<MeshRenderer>();
				r.sharedMaterial = _material;
				r.sortingLayerID = first.sortingLayerID;
				r.sortingOrder = first.sortingOrder;
				it.Quad.layer = first.gameObject.layer;
			}
			it.Mesh.vertices = new Vector3[] { new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(w * 0.5f, -h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(-w * 0.5f, h * 0.5f, 0f) };
			it.Mesh.uv = new Vector2[] { new Vector2(u, v), new Vector2(u + du, v), new Vector2(u + du, v + dv), new Vector2(u, v + dv) };
			it.Mesh.colors32 = new Color32[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
			it.Mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
			it.Mesh.RecalculateBounds();
			Transform parent = it.Quad.transform.parent;
			Vector3 scale = parent.lossyScale;
			// The sign keeps the picture as baked now; a later flip of the parent mirrors it like the skeleton.
			it.Quad.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f);
			it.Quad.transform.position = center;
			it.Quad.SetActive(false);
			it.Baked = true;
			return true;
		}

		private static void Enter(Item it)
		{
			foreach (Animator a in it.Unit.GetComponentsInChildren<Animator>(false))
			{
				if (a.enabled)
				{
					a.enabled = false;
					it.Off.Add(a);
				}
			}
			foreach (Spine.Unity.SkeletonRenderer s in it.Unit.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(false))
			{
				if (s.enabled)
				{
					s.enabled = false;
					it.Off.Add(s);
				}
			}
			foreach (Canvas c in it.Unit.GetComponentsInChildren<Canvas>(false))
			{
				if (c.enabled)
				{
					c.enabled = false;
					it.Off.Add(c);
				}
			}
			foreach (Renderer r in it.Unit.GetComponentsInChildren<Renderer>(false))
			{
				if (r.enabled && r.gameObject != it.Quad)
				{
					r.enabled = false;
					it.Hidden.Add(r);
				}
			}
			it.Quad.SetActive(true);
			it.On = true;
			_on++;
		}

		private static void Exit(Item it)
		{
			if (it.Quad != null)
			{
				it.Quad.SetActive(false);
			}
			for (int i = 0; i < it.Off.Count; i++)
			{
				if (it.Off[i] != null)
				{
					it.Off[i].enabled = true;
				}
			}
			for (int i = 0; i < it.Hidden.Count; i++)
			{
				if (it.Hidden[i] != null)
				{
					it.Hidden[i].enabled = true;
				}
			}
			it.Off.Clear();
			it.Hidden.Clear();
			it.On = false;
			_on--;
		}

		private static void Drop(Item it)
		{
			if (it.On)
			{
				Exit(it);
			}
			if (it.Quad != null)
			{
				UnityEngine.Object.Destroy(it.Quad);
			}
			if (it.Mesh != null)
			{
				UnityEngine.Object.Destroy(it.Mesh);
			}
			if (it.Slot >= 0)
			{
				_free.Push(it.Slot);
			}
		}

		private static void Purge()
		{
			List<OfficerUnit> gone = null;
			foreach (KeyValuePair<OfficerUnit, Item> p in _items)
			{
				if (p.Value.Unit == null)
				{
					if (gone == null)
					{
						gone = new List<OfficerUnit>();
					}
					gone.Add(p.Key);
				}
			}
			if (gone != null)
			{
				for (int i = 0; i < gone.Count; i++)
				{
					Drop(_items[gone[i]]);
					_items.Remove(gone[i]);
				}
			}
		}

		private static void Clear()
		{
			foreach (Item it in _items.Values)
			{
				Drop(it);
			}
			_items.Clear();
			_on = 0;
			_cursor = 0;
		}
	}

	// ---------- UiRefresh ----------
	public static class UiRefresh
	{
		private const string Tag = "PerfPack";
		internal static N.IntParam Hz;
		private static readonly Dictionary<AgentUnitUI, float> _next = new Dictionary<AgentUnitUI, float>();
		private static readonly Dictionary<AgentUnitUI, bool> _ace = new Dictionary<AgentUnitUI, bool>();
		private static N.Counter _skipped;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(AgentUnitUI), "activateUI", new Type[] { typeof(AgentModel) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPrefix(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "UiRefresh not installed: AgentUnitUI.activateUI already has a Prefix by " + owner);
				return 0;
			}
			_skipped = N.Counters.Get(Tag, "uiSkipped");
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(UiRefresh).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ResetRegistry.Register(new UiRefreshResetter());
			N.ULog.Info(Tag, "UiRefresh installed (AgentUnitUI.activateUI, " + Hz.Value + " per second)");
			return 1;
		}

		// A promotion calls this once with no per-frame refresh after it, so a changed ace state always passes.
		public static bool Prefix(AgentUnitUI __instance, AgentModel model)
		{
			float now = Time.unscaledTime;
			float next;
			bool ace;
			if (_next.TryGetValue(__instance, out next) && now < next && _ace.TryGetValue(__instance, out ace) && ace == model.isAce)
			{
				_skipped.Value++;
				return false;
			}
			_next[__instance] = now + 1f / Hz.Value;
			_ace[__instance] = model.isAce;
			return true;
		}

		internal static void Clear()
		{
			_next.Clear();
			_ace.Clear();
		}
	}

	internal sealed class UiRefreshResetter : N.IResettable
	{
		public void ResetState()
		{
			UiRefresh.Clear();
		}
	}

	// ---------- RequireCache ----------
	public static class RequireCache
	{
		private const string Tag = "PerfPack";
		private static readonly Dictionary<InventoryAgentSlot, EquipmentTypeInfo> _built = new Dictionary<InventoryAgentSlot, EquipmentTypeInfo>();
		private static InventoryUI _window;
		private static N.Counter _skipped;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(InventoryAgentSlot), "SetRequireInfo", new Type[] { typeof(EquipmentTypeInfo) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPrefix(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "RequireCache not installed: InventoryAgentSlot.SetRequireInfo already has a Prefix by " + owner);
				return 0;
			}
			_skipped = N.Counters.Get(Tag, "requireSkipped");
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(RequireCache).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "RequireCache installed (InventoryAgentSlot.SetRequireInfo)");
			return 1;
		}

		// A new window drops all entries; a slot destroyed while its window lives keeps a dead entry until then.
		// Fewer plates than requires means Init failed on a plate, so the slot goes through vanilla again.
		public static bool Prefix(InventoryAgentSlot __instance, EquipmentTypeInfo info)
		{
			try
			{
				if (!__instance.HasRequireComponents || info == null)
				{
					return true;
				}
				InventoryUI window = InventoryUI.CurrentWindow;
				if (!object.ReferenceEquals(window, _window))
				{
					_built.Clear();
					_window = window;
				}
				EquipmentTypeInfo built;
				if (_built.TryGetValue(__instance, out built) && object.ReferenceEquals(built, info) && __instance.require.parent.childCount >= Math.Max(1, info.requires.Count))
				{
					_skipped.Value++;
					return false;
				}
				_built[__instance] = info;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "RequireCache prefix failed, slot built as usual (logged once): " + e);
				}
			}
			return true;
		}
	}

	// ---------- EffectTextures ----------
	// Add_On.LoadMod keeps every CustomEffect picture twice: a readable copy in memory and the one drawn by the video card. Nothing
	// reads their pixels: Spine only draws them. Add_On.init loads all of them before the first mod DLL, so this runs in Nail's constructor:
	// it drops the readable copies at once, which lowers the load peak, and lets effects whose PNG files are byte-identical share one texture.
	// FastFix adds its effect copies on the first frames; the driver handles those. No patches.
	public static class EffectTextures
	{
		internal static int Install()
		{
			EffectTexturesDriver.Effects = true;
			EffectTexturesDriver.Start5();
			N.ULog.Info(PerfPackModule.Id, "EffectTextures installed");
			EffectTexturesDriver.Run();
			return 1;
		}
	}

	public class EffectTexturesDriver : MonoBehaviour
	{
		internal sealed class Use
		{
			internal Spine.Unity.AtlasAsset Atlas;
			internal Material Mat;
			internal Texture2D Tex;
			internal string File;
		}

		// FastFix adds its effect copies on the first frames.
		private const int StartFrame = 5;

		internal static readonly HashSet<Texture2D> Done = new HashSet<Texture2D>();

		internal static bool Effects;
		private static bool _started;

		private int _frames;

		internal static void Start5()
		{
			if (!_started)
			{
				_started = true;
				GameObject go = new GameObject("NailOptimization.EffectTextures");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.AddComponent<EffectTexturesDriver>();
			}
		}

		private void Update()
		{
			_frames++;
			if (_frames >= StartFrame)
			{
				if (Effects)
				{
					Run();
				}
				if (AgentTextures.Enabled)
				{
					AgentTextures.Run();
				}
				Destroy(gameObject);
			}
		}

		internal static void Run()
		{
			float started = Time.realtimeSinceStartup;
			List<Texture2D> queue = new List<Texture2D>();
			int errors = 0;
			double mb = 0;
			try
			{
				Collect(queue);
			}
			catch (Exception e)
			{
				errors++;
				N.ULog.Error(PerfPackModule.Id, "EffectTextures: " + e);
			}
			for (int i = 0; i < queue.Count; i++)
			{
				Texture2D t = queue[i];
				Done.Add(t);
				try
				{
					t.Apply(false, true);
					mb += t.width * (double)t.height * 4 / 1048576;
				}
				catch (Exception e)
				{
					errors++;
					if (errors == 1)
					{
						N.ULog.Error(PerfPackModule.Id, "EffectTextures: " + e);
					}
				}
			}
			N.ULog.Info(PerfPackModule.Id, "EffectTextures done: " + queue.Count + " textures, " + (long)mb + " MB of readable copies freed, errors " + errors + ", " + (long)(Time.realtimeSinceStartup - started) + " s");
		}

		private static void Collect(List<Texture2D> queue)
		{
			Add_On on = Add_On.instance;
			List<Use> uses = new List<Use>();
			foreach (KeyValuePair<string, Spine.Unity.SkeletonDataAsset> kv in on.EffectList)
			{
				if (kv.Value == null || kv.Value.atlasAssets == null)
				{
					continue;
				}
				string dir = FindDir(on, kv.Key);
				for (int a = 0; a < kv.Value.atlasAssets.Length; a++)
				{
					Spine.Unity.AtlasAsset atlas = kv.Value.atlasAssets[a];
					if (atlas == null || atlas.materials == null)
					{
						continue;
					}
					for (int m = 0; m < atlas.materials.Length; m++)
					{
						Material mat = atlas.materials[m];
						Texture2D tex = mat != null ? mat.mainTexture as Texture2D : null;
						if (tex == null || Done.Contains(tex))
						{
							continue;
						}
						Use u = new Use();
						u.Atlas = atlas;
						u.Mat = mat;
						u.Tex = tex;
						u.File = dir != null ? dir + "/" + tex.name + ".png" : null;
						uses.Add(u);
					}
				}
			}
			int shared = Share(uses);
			if (LazyPictures.Effects)
			{
				LazyPictures.DeferEffects(uses);
			}
			HashSet<Texture2D> seen = new HashSet<Texture2D>();
			for (int i = 0; i < uses.Count; i++)
			{
				if (uses[i].Tex != null && seen.Add(uses[i].Tex))
				{
					queue.Add(uses[i].Tex);
				}
			}
			N.ULog.Info(PerfPackModule.Id, "EffectTextures: " + uses.Count + " pictures, " + shared + " shared with an identical one");
		}

		// Key "Name" is the first mod with the folder (Add_On.LoadMod keeps the first); "Name@Folder" is a FastFix copy.
		private static string FindDir(Add_On on, string key)
		{
			int at = key.IndexOf('@');
			string name = at >= 0 ? key.Substring(0, at) : key;
			string folder = at >= 0 ? key.Substring(at + 1) : null;
			for (int d = 0; d < on.DirList.Count; d++)
			{
				if (folder != null && on.DirList[d].Name != folder)
				{
					continue;
				}
				string dir = on.DirList[d].FullName + "/CustomEffect/" + name;
				// A folder without these failed in LoadMod, so the effect came from a later mod.
				if (System.IO.File.Exists(dir + "/atlas.txt") && System.IO.File.Exists(dir + "/json.txt"))
				{
					return dir;
				}
			}
			return null;
		}

		private static int Share(List<Use> uses)
		{
			int shared = 0;
			Dictionary<string, List<Use>> groups = new Dictionary<string, List<Use>>(StringComparer.Ordinal);
			for (int i = 0; i < uses.Count; i++)
			{
				if (uses[i].File == null || !System.IO.File.Exists(uses[i].File))
				{
					continue;
				}
				string key = uses[i].Tex.name + "|" + uses[i].Tex.width + "|" + uses[i].Tex.height + "|" + new System.IO.FileInfo(uses[i].File).Length;
				List<Use> g;
				if (!groups.TryGetValue(key, out g))
				{
					g = new List<Use>();
					groups[key] = g;
				}
				g.Add(uses[i]);
			}
			foreach (KeyValuePair<string, List<Use>> kv in groups)
			{
				List<Use> g = kv.Value;
				if (g.Count < 2)
				{
					continue;
				}
				byte[] first = System.IO.File.ReadAllBytes(g[0].File);
				for (int i = 1; i < g.Count; i++)
				{
					if (object.ReferenceEquals(g[i].Tex, g[0].Tex) || !Same(first, System.IO.File.ReadAllBytes(g[i].File)))
					{
						continue;
					}
					Texture2D old = g[i].Tex;
					g[i].Mat.mainTexture = g[0].Tex;
					g[i].Tex = g[0].Tex;
					Destroy(old);
					shared++;
				}
			}
			return shared;
		}

		private static bool Same(byte[] a, byte[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}
			return true;
		}
	}

	// ---------- AgentTextures ----------
	// Experimental. Drops the readable copies of AgentAnimation (WorkerSpineAnimatorManager) and CatAnimation (ChildHood) pictures on frame 5.
	// The only game reader of them is AtlasUtilities.ToTexture (GetRepackedSkin): for its call the material gets a readable copy from the video card.
	public static class AgentTextures
	{
		public sealed class Swap
		{
			internal Material Mat;
			internal Texture Tex;
		}

		private const string Tag = "PerfPack";

		internal static bool Enabled;

		// Instance ids: a reference here would keep a creature atlas of an earlier day alive through the scene unload.
		private static readonly HashSet<int> Dropped = new HashSet<int>();
		private static readonly Dictionary<Texture2D, Texture2D> Copies = new Dictionary<Texture2D, Texture2D>();
		private static int _copiesFrame;
		private static Swap _pending;
		private static System.Collections.IDictionary _cached;
		private static int _reads;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(AtlasUtilities), "ToTexture", new Type[] { typeof(Spine.AtlasRegion), typeof(bool), typeof(TextureFormat), typeof(bool) });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "AgentTextures not installed: AtlasUtilities.ToTexture is patched by " + owner);
				return 0;
			}
			MethodInfo pre = typeof(AgentTextures).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
			MethodInfo post = typeof(AgentTextures).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(pre), new HarmonyMethod(post)))
			{
				return 0;
			}
			FieldInfo cached = typeof(AtlasUtilities).GetField("CachedRegionTextures", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
			_cached = !object.ReferenceEquals(cached, null) ? cached.GetValue(null) as System.Collections.IDictionary : null;
			Enabled = true;
			EffectTexturesDriver.Start5();
			N.ULog.Info(Tag, "AgentTextures installed (AtlasUtilities.ToTexture)");
			return 1;
		}

		internal static void Run()
		{
			float started = Time.realtimeSinceStartup;
			List<Spine.Unity.SkeletonDataAsset> assets = new List<Spine.Unity.SkeletonDataAsset>();
			try
			{
				FieldInfo f = typeof(WorkerSpine.WorkerSpineAnimatorManager).GetField("nameDicMod", BindingFlags.NonPublic | BindingFlags.Instance);
				System.Collections.IDictionary animators = (System.Collections.IDictionary)f.GetValue(WorkerSpine.WorkerSpineAnimatorManager.instance);
				foreach (object v in animators.Values)
				{
					assets.Add(((WorkerSpine.WorkerSpineAnimatorData)v).skeletonData);
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "AgentTextures: " + e);
			}
			int agents = assets.Count;
			try
			{
				System.Collections.IDictionary cats = FindCats();
				if (cats != null)
				{
					foreach (object v in cats.Values)
					{
						assets.Add((Spine.Unity.SkeletonDataAsset)v);
					}
				}
				N.ULog.Info(Tag, "AgentTextures: " + agents + " agent animations, " + (assets.Count - agents) + " cat animations");
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "AgentTextures: " + e);
			}
			double mb = 0;
			int errors = 0;
			for (int i = 0; i < assets.Count; i++)
			{
				if (assets[i] == null || assets[i].atlasAssets == null)
				{
					continue;
				}
				for (int a = 0; a < assets[i].atlasAssets.Length; a++)
				{
					Spine.Unity.AtlasAsset atlas = assets[i].atlasAssets[a];
					if (atlas == null || atlas.materials == null)
					{
						continue;
					}
					for (int m = 0; m < atlas.materials.Length; m++)
					{
						Material mat = atlas.materials[m];
						Texture2D tex = mat != null ? mat.mainTexture as Texture2D : null;
						if (tex == null || Dropped.Contains(tex.GetInstanceID()) || AgentLazy.IsPending(tex) || LazyPictures.IsPending(tex) || !IsReadable(tex))
						{
							continue;
						}
						try
						{
							tex.Apply(false, true);
							Dropped.Add(tex.GetInstanceID());
							mb += tex.width * (double)tex.height * 4 / 1048576;
						}
						catch (Exception e)
						{
							errors++;
							if (errors == 1)
							{
								N.ULog.Error(Tag, "AgentTextures: " + e);
							}
						}
					}
				}
			}
			N.ULog.Info(Tag, "AgentTextures done: " + Dropped.Count + " textures, " + (long)mb + " MB of readable copies freed, errors " + errors + ", " + (long)(Time.realtimeSinceStartup - started) + " s");
		}

		internal static void MarkDropped(Texture2D tex)
		{
			Dropped.Add(tex.GetInstanceID());
		}

		private static System.Collections.IDictionary FindCats()
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].GetName().Name != "ChildHood")
				{
					continue;
				}
				Type type = all[i].GetType("ChildHood_Mod.Creature.Harmony_Patch");
				FieldInfo f = !object.ReferenceEquals(type, null) ? type.GetField("AnimList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
				if (!object.ReferenceEquals(f, null))
				{
					return f.GetValue(null) as System.Collections.IDictionary;
				}
			}
			return null;
		}

		internal static bool IsReadable(Texture2D t)
		{
			try
			{
				t.GetPixel(0, 0);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static void Prefix(Spine.AtlasRegion ar)
		{
			try
			{
				// Left by a ToTexture that threw: Harmony 1.x skips the Postfix then.
				Restore();
				if (_cached != null && _cached.Contains(ar))
				{
					return;
				}
				Material mat = ar.page.rendererObject as Material;
				Texture2D tex = mat != null ? mat.mainTexture as Texture2D : null;
				LazyPictures.LoadTexture(tex);
				if (tex == null || !Dropped.Contains(tex.GetInstanceID()))
				{
					return;
				}
				Texture2D copy = ReadableCopy(tex);
				Swap s = new Swap();
				s.Mat = mat;
				s.Tex = tex;
				mat.mainTexture = copy;
				_pending = s;
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "AgentTextures read: " + e);
			}
		}

		public static void Postfix()
		{
			Restore();
		}

		private static void Restore()
		{
			if (_pending != null)
			{
				_pending.Mat.mainTexture = _pending.Tex;
				_pending = null;
			}
		}

		// A repack reads many regions of a few atlases within one frame, so copies live until the next read on a later frame.
		private static Texture2D ReadableCopy(Texture2D tex)
		{
			if (_copiesFrame != Time.frameCount)
			{
				_copiesFrame = Time.frameCount;
				foreach (KeyValuePair<Texture2D, Texture2D> kv in Copies)
				{
					UnityEngine.Object.DestroyImmediate(kv.Value);
				}
				Copies.Clear();
			}
			Texture2D copy;
			if (Copies.TryGetValue(tex, out copy))
			{
				return copy;
			}
			RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			RenderTexture was = RenderTexture.active;
			try
			{
				Graphics.Blit(tex, rt);
				RenderTexture.active = rt;
				copy = new Texture2D(tex.width, tex.height, TextureFormat.ARGB32, false, true);
				copy.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0);
			}
			finally
			{
				RenderTexture.active = was;
				RenderTexture.ReleaseTemporary(rt);
			}
			Copies[tex] = copy;
			_reads++;
			if (_reads <= 5)
			{
				N.ULog.Info(Tag, "AgentTextures: pixels read through the video card for " + tex.name);
			}
			return copy;
		}
	}

	// ---------- CreatureTextures ----------
	// Experimental. At day start drops the readable copies of the atlases under CreatureLayer. A creature added later in the day keeps its copy.
	// AgentTextures covers the AtlasUtilities.ToTexture read, so this feature needs it.
	public static class CreatureTextures
	{
		private const string Tag = "PerfPack";
		private static bool _armed = true;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			if (!AgentTextures.Enabled)
			{
				N.ULog.Warn(Tag, "CreatureTextures not installed: needs PerfPack.AgentTexturesExperimental");
				return 0;
			}
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(GameManager), "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, tick, new HarmonyMethod(typeof(CreatureTextures).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "CreatureTextures installed");
			return 1;
		}

		public static void Prefix(GameManager __instance)
		{
			if (__instance.state == GameState.STOP)
			{
				_armed = true;
			}
			else if (_armed && __instance.state == GameState.PLAYING)
			{
				_armed = false;
				Run();
			}
		}

		private static void Run()
		{
			try
			{
				float start = Time.realtimeSinceStartup;
				CreatureLayer layer = CreatureLayer.currentLayer;
				if (layer == null)
				{
					return;
				}
				HashSet<Texture2D> seen = new HashSet<Texture2D>();
				Spine.Unity.SkeletonRenderer[] renderers = layer.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(true);
				int count = 0;
				double mb = 0;
				for (int i = 0; i < renderers.Length; i++)
				{
					Spine.Unity.SkeletonDataAsset asset = renderers[i].skeletonDataAsset;
					if (asset == null || asset.atlasAssets == null)
					{
						continue;
					}
					for (int a = 0; a < asset.atlasAssets.Length; a++)
					{
						Spine.Unity.AtlasAsset atlas = asset.atlasAssets[a];
						if (atlas == null || atlas.materials == null)
						{
							continue;
						}
						for (int m = 0; m < atlas.materials.Length; m++)
						{
							Material mat = atlas.materials[m];
							Texture2D tex = mat != null ? mat.mainTexture as Texture2D : null;
							if (tex == null || !seen.Add(tex) || AgentLazy.IsPending(tex) || LazyPictures.IsPending(tex) || !AgentTextures.IsReadable(tex))
							{
								continue;
							}
							tex.Apply(false, true);
							AgentTextures.MarkDropped(tex);
							count++;
							mb += tex.width * (double)tex.height * 4 / 1048576;
						}
					}
				}
				N.ULog.Info(Tag, "CreatureTextures: " + renderers.Length + " renderers, " + count + " textures, " + (long)mb + " MB of readable copies freed, " + (int)((Time.realtimeSinceStartup - start) * 1000f) + " ms");
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "CreatureTextures: " + e);
			}
		}
	}

	// ---------- AgentLazy ----------
	// Experimental. Replaces WorkerSpineAnimatorManager.Init: new animators (name prefix cases) get a transparent 2x2 placeholder per picture and
	// the atlas keeps its page sizes from atlas.txt. The first GetDataWithCheck_Mod or WorkerAnimatorChanger.SetAnimator* of an animator loads the
	// PNG files into the same Texture2D objects, so materials stay valid. In-place overrides of vanilla animators load at start as before.
	public static class AgentLazy
	{
		private sealed class Item
		{
			internal Texture2D Tex;
			internal string File;
		}

		private const string Tag = "PerfPack";

		private static readonly Dictionary<WorkerSpine.WorkerSpineAnimatorData, List<Item>> Pending = new Dictionary<WorkerSpine.WorkerSpineAnimatorData, List<Item>>();
		private static readonly HashSet<Texture2D> PendingTex = new HashSet<Texture2D>();
		private static readonly Color32[] Clear = new Color32[4];
		private static int _loaded;
		private static float _loadSeconds;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type mgr = typeof(WorkerSpine.WorkerSpineAnimatorManager);
			Type changer = typeof(WorkerSpine.WorkerAnimatorChanger);
			Type data = typeof(WorkerSpine.WorkerSpineAnimatorData);
			MethodBase init = N.PatchUtil.Resolve(Tag, mgr, "Init", new Type[] { typeof(List<WorkerSpine.WorkerSpineAnimatorData>) });
			MethodBase get = N.PatchUtil.Resolve(Tag, mgr, "GetDataWithCheck_Mod", new Type[] { typeof(LobotomyBaseMod.KeyValuePairSS), data.MakeByRefType() });
			MethodBase set1 = N.PatchUtil.Resolve(Tag, changer, "SetAnimator", new Type[] { data });
			MethodBase set2 = N.PatchUtil.Resolve(Tag, changer, "SetAnimator", new Type[] { data, typeof(bool) });
			MethodBase set3 = N.PatchUtil.Resolve(Tag, changer, "SetAnimatorWithUniqueFace", new Type[] { data, typeof(bool) });
			if (object.ReferenceEquals(init, null) || object.ReferenceEquals(get, null) || object.ReferenceEquals(set1, null) || object.ReferenceEquals(set2, null) || object.ReferenceEquals(set3, null))
			{
				return 0;
			}
			const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			if (object.ReferenceEquals(mgr.GetField("nameDicMod", Fields), null) || object.ReferenceEquals(mgr.GetField("nameDic", Fields), null) || object.ReferenceEquals(mgr.GetField("idDic", Fields), null) || object.ReferenceEquals(mgr.GetField("_isLoaded", Fields), null))
			{
				N.ULog.Warn(Tag, "AgentLazy not installed: WorkerSpineAnimatorManager fields changed");
				return 0;
			}
			string owner = Owners.ForeignAny(init);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "AgentLazy not installed: WorkerSpineAnimatorManager.Init is patched by " + owner);
				return 0;
			}
			HarmonyMethod setPrefix = new HarmonyMethod(typeof(AgentLazy).GetMethod("SetPrefix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod getPostfix = new HarmonyMethod(typeof(AgentLazy).GetMethod("GetPostfix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod initPrefix = new HarmonyMethod(typeof(AgentLazy).GetMethod("InitPrefix", BindingFlags.Public | BindingFlags.Static));
			// Init goes last: without the loaders above the game must keep its own Init.
			if (!N.PatchUtil.Install(harmony, Tag, set1, setPrefix, null) || !N.PatchUtil.Install(harmony, Tag, set2, setPrefix, null) || !N.PatchUtil.Install(harmony, Tag, set3, setPrefix, null) || !N.PatchUtil.Install(harmony, Tag, get, null, getPostfix) || !N.PatchUtil.Install(harmony, Tag, init, initPrefix, null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "AgentLazy installed (WorkerSpineAnimatorManager.Init)");
			StartPrefetch();
			return 1;
		}

		// Skeleton file prefetch. Init reads json.txt or skeleton.skel of every AgentAnimation folder on the main thread (b6 boot:
		// 933 ms for 320 folders). Install runs inside Add_On.init after ModList is built and before the other mods' DLLs load,
		// so a worker thread reads the same files (same folders: modpath/AgentAnimation/<dir>, same json-then-skel order) while
		// the DLLs load. Init takes a folder's content by its FullName; a folder the worker did not read (missing, error) is
		// read by Init as before.
		private static System.Threading.Thread _prefetch;
		private static Dictionary<string, object> _prefetched;
		private static string[] _prefetchRoots;
		private static double _prefetchMs;
		private static int _prefetchHits;
		private static int _prefetchMisses;

		private static void StartPrefetch()
		{
			try
			{
				List<ModInfo> mods = ((Add_On)Add_On.instance).ModList;
				if (mods == null || mods.Count == 0)
				{
					return;
				}
				string[] roots = new string[mods.Count];
				for (int i = 0; i < mods.Count; i++)
				{
					roots[i] = mods[i].modpath == null ? null : mods[i].modpath.FullName;
				}
				_prefetchRoots = roots;
				_prefetch = new System.Threading.Thread(Prefetch);
				_prefetch.IsBackground = true;
				_prefetch.Start();
			}
			catch (Exception e)
			{
				_prefetch = null;
				N.ULog.Warn(Tag, "AgentLazy prefetch not started: " + e.Message);
			}
		}

		// Worker thread: System.IO only.
		private static void Prefetch()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Dictionary<string, object> found = new Dictionary<string, object>(StringComparer.Ordinal);
			for (int i = 0; i < _prefetchRoots.Length; i++)
			{
				if (_prefetchRoots[i] == null)
				{
					continue;
				}
				try
				{
					DirectoryInfo[] subs = new DirectoryInfo(_prefetchRoots[i]).GetDirectories();
					DirectoryInfo root = null;
					for (int j = 0; j < subs.Length; j++)
					{
						if (subs[j].Name == "AgentAnimation")
						{
							root = subs[j];
							break;
						}
					}
					if (root == null)
					{
						continue;
					}
					DirectoryInfo[] dirs = root.GetDirectories();
					for (int j = 0; j < dirs.Length; j++)
					{
						try
						{
							string d = dirs[j].FullName;
							if (File.Exists(d + "/json.txt"))
							{
								found[d] = File.ReadAllText(d + "/json.txt");
							}
							else if (File.Exists(d + "/skeleton.skel"))
							{
								found[d] = File.ReadAllBytes(d + "/skeleton.skel");
							}
							else
							{
								found[d] = null;
							}
						}
						catch (Exception)
						{
						}
					}
				}
				catch (Exception)
				{
				}
			}
			_prefetched = found;
			_prefetchMs = sw.Elapsed.TotalMilliseconds;
		}

		// Main thread, inside Init. True when the worker read this folder; content is null when it has neither file.
		private static bool TakePrefetched(string dir, out object content)
		{
			content = null;
			if (_prefetch != null)
			{
				_prefetch.Join();
				_prefetch = null;
			}
			if (_prefetched == null || !_prefetched.TryGetValue(dir, out content))
			{
				_prefetchMisses++;
				return false;
			}
			_prefetched.Remove(dir);
			_prefetchHits++;
			return true;
		}

		internal static bool IsPending(Texture2D tex)
		{
			return PendingTex.Contains(tex);
		}

		public static void SetPrefix(WorkerSpine.WorkerSpineAnimatorData data)
		{
			Load(data);
		}

		public static void GetPostfix(ref WorkerSpine.WorkerSpineAnimatorData output)
		{
			Load(output);
		}

		private static void Load(WorkerSpine.WorkerSpineAnimatorData data)
		{
			List<Item> items;
			if (data == null || Pending.Count == 0 || !Pending.TryGetValue(data, out items))
			{
				return;
			}
			Pending.Remove(data);
			float start = Time.realtimeSinceStartup;
			for (int i = 0; i < items.Count; i++)
			{
				try
				{
					if (!items[i].Tex.LoadImage(File.ReadAllBytes(items[i].File), AgentTextures.Enabled))
					{
						N.ULog.Error(Tag, "AgentLazy: cannot decode " + items[i].File);
					}
					else if (AgentTextures.Enabled)
					{
						AgentTextures.MarkDropped(items[i].Tex);
					}
				}
				catch (Exception e)
				{
					N.ULog.Error(Tag, "AgentLazy: " + items[i].File + ": " + e.Message);
				}
				PendingTex.Remove(items[i].Tex);
			}
			float took = Time.realtimeSinceStartup - start;
			_loaded++;
			_loadSeconds += took;
			if (_loaded <= 20 || took >= 0.25f)
			{
				N.ULog.Info(Tag, "AgentLazy: loaded " + data.name + ", " + items.Count + " pictures, " + (int)(took * 1000f) + " ms; total " + _loaded + " animators, " + (int)(_loadSeconds * 1000f) + " ms, waiting " + Pending.Count);
			}
		}

		// Copy of the game's Init without the PNG decoding of new animators.
		public static bool InitPrefix(WorkerSpine.WorkerSpineAnimatorManager __instance, List<WorkerSpine.WorkerSpineAnimatorData> data)
		{
			try
			{
				const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
				Type mgr = typeof(WorkerSpine.WorkerSpineAnimatorManager);
				Dictionary<LobotomyBaseMod.KeyValuePairSS, WorkerSpine.WorkerSpineAnimatorData> nameDicMod = new Dictionary<LobotomyBaseMod.KeyValuePairSS, WorkerSpine.WorkerSpineAnimatorData>();
				mgr.GetField("nameDicMod", Any).SetValue(__instance, nameDicMod);
				Dictionary<string, WorkerSpine.WorkerSpineAnimatorData> nameDic = (Dictionary<string, WorkerSpine.WorkerSpineAnimatorData>)mgr.GetField("nameDic", Any).GetValue(__instance);
				Dictionary<int, WorkerSpine.WorkerSpineAnimatorData> idDic = (Dictionary<int, WorkerSpine.WorkerSpineAnimatorData>)mgr.GetField("idDic", Any).GetValue(__instance);
				Dictionary<LobotomyBaseMod.KeyValuePairSS, object> skels = new Dictionary<LobotomyBaseMod.KeyValuePairSS, object>();
				Dictionary<LobotomyBaseMod.KeyValuePairSS, string> dirs = new Dictionary<LobotomyBaseMod.KeyValuePairSS, string>();
				System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
				long tRead, tFind, tBase = 0L, tPics = 0L, tAtlas = 0L, tSkel = 0L, mark;
				if (Add_On.instance.DirList.Count > 0)
				{
					foreach (ModInfo mod in Add_On.instance.ModList)
					{
						DirectoryInfo root = Add_On.CheckNamedDir(mod.modpath, "AgentAnimation");
						if (root == null || root.GetDirectories().Length == 0)
						{
							continue;
						}
						foreach (DirectoryInfo dir in root.GetDirectories())
						{
							LobotomyBaseMod.KeyValuePairSS key = new LobotomyBaseMod.KeyValuePairSS(mod.modid, dir.Name);
							object pre;
							if (TakePrefetched(dir.FullName, out pre))
							{
								if (pre != null)
								{
									skels.Add(key, pre);
								}
							}
							else if (File.Exists(dir.FullName + "/json.txt"))
							{
								skels.Add(key, File.ReadAllText(dir.FullName + "/json.txt"));
							}
							else if (File.Exists(dir.FullName + "/skeleton.skel"))
							{
								skels.Add(key, File.ReadAllBytes(dir.FullName + "/skeleton.skel"));
							}
							dirs.Add(key, dir.FullName);
						}
					}
				}
				tRead = sw.ElapsedTicks;
				Dictionary<LobotomyBaseMod.KeyValuePairSS, object> restSkels = new Dictionary<LobotomyBaseMod.KeyValuePairSS, object>(skels);
				Dictionary<LobotomyBaseMod.KeyValuePairSS, string> restDirs = new Dictionary<LobotomyBaseMod.KeyValuePairSS, string>(dirs);
				foreach (WorkerSpine.WorkerSpineAnimatorData d in data)
				{
					try
					{
						__instance.FindNewSkinandSkel_Mod(d, skels, dirs, restSkels, restDirs);
						nameDic.Add(d.name, d);
						nameDicMod.Add(new LobotomyBaseMod.KeyValuePairSS(string.Empty, d.name), d);
						idDic.Add(d.id, d);
					}
					catch (Exception e)
					{
						Debug.LogError(d.name + Environment.NewLine + e);
					}
				}
				tFind = sw.ElapsedTicks;
				int lazy = 0;
				foreach (KeyValuePair<LobotomyBaseMod.KeyValuePairSS, object> kv in restSkels)
				{
					mark = sw.ElapsedTicks;
					string head = kv.Key.value.Split(new char[] { '_' })[0];
					WorkerSpine.WorkerSpineAnimatorData made;
					float scale;
					if (nameDic.ContainsKey(head))
					{
						WorkerSpine.WorkerSpineAnimatorData b = nameDic[head];
						made = new WorkerSpine.WorkerSpineAnimatorData(b.id + 10000000, kv.Key.value, b.animatorSrc, b.skeletonSrc);
						made.LoadData();
						scale = made.skeletonData.scale;
					}
					else if (head == "Custom")
					{
						made = new WorkerSpine.WorkerSpineAnimatorData(kv.Key.GetHashCode(), kv.Key.value);
						scale = 0.01f;
					}
					else
					{
						continue;
					}
					tBase += sw.ElapsedTicks - mark;
					mark = sw.ElapsedTicks;
					string dir = restDirs[kv.Key];
					string atlasText = File.ReadAllText(dir + "/atlas.txt");
					bool eager = !LazyPictures.PagesHaveSize(atlasText);
					List<Item> items = new List<Item>();
					List<Texture2D> textures = new List<Texture2D>();
					foreach (FileInfo file in new DirectoryInfo(dir).GetFiles())
					{
						if (file.Name.Contains(".png"))
						{
							Texture2D tex = new Texture2D(2, 2);
							tex.name = Path.GetFileNameWithoutExtension(file.Name);
							textures.Add(tex);
							if (eager)
							{
								tex.LoadImage(File.ReadAllBytes(dir + "/" + file.Name));
								continue;
							}
							tex.SetPixels32(Clear);
							tex.Apply();
							PendingTex.Add(tex);
							Item item = new Item();
							item.Tex = tex;
							item.File = dir + "/" + file.Name;
							items.Add(item);
						}
					}
					tPics += sw.ElapsedTicks - mark;
					mark = sw.ElapsedTicks;
					Spine.Unity.AtlasAsset atlas = Spine.Unity.AtlasAsset.CreateRuntimeInstance(atlasText, textures.ToArray(), (Shader)null, true);
					tAtlas += sw.ElapsedTicks - mark;
					mark = sw.ElapsedTicks;
					made.skeletonData = kv.Value is string
						? Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance((string)kv.Value, atlas, eager, scale)
						: Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance((byte[])kv.Value, atlas, eager, scale);
					tSkel += sw.ElapsedTicks - mark;
					if (!eager)
					{
						Pending[made] = items;
						lazy++;
					}
					nameDicMod.Add(kv.Key, made);
				}
				mgr.GetField("_isLoaded", Any).SetValue(__instance, true);
				double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
				N.ULog.Info(Tag, "AgentLazy: " + lazy + " animators wait for first use, " + PendingTex.Count + " pictures; init " + (sw.ElapsedTicks * f).ToString("0") + " ms (skeleton files " + (tRead * f).ToString("0") + ", vanilla skins " + ((tFind - tRead) * f).ToString("0") + ", base data " + (tBase * f).ToString("0") + ", pictures " + (tPics * f).ToString("0") + ", atlas " + (tAtlas * f).ToString("0") + ", skeleton " + (tSkel * f).ToString("0") + "); prefetch " + _prefetchHits + " folder(s) read in " + _prefetchMs.ToString("0") + " ms on a worker, " + _prefetchMisses + " read here");
				_prefetched = null;
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "AgentLazy Init: " + e);
				File.WriteAllText(Application.dataPath + "/BaseMods/error2.txt", e.Message + Environment.NewLine + e.StackTrace);
			}
			return false;
		}
	}

	// ---------- IntroSkip ----------
	// IntroPlayer.Update has a foreach (GL-53), Start is empty: Start moves the logo clock to the moment Update loads the title.
	public static class IntroSkip
	{
		private const string Tag = "PerfPack";
		private static FieldInfo _elapsed;

		internal static int Install(HarmonyInstance harmony)
		{
			_elapsed = AccessTools.Field(typeof(IntroPlayer), "_elapsedTime");
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(IntroPlayer), "Start", Type.EmptyTypes);
			if (object.ReferenceEquals(_elapsed, null) || object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(Tag, "IntroSkip not installed: IntroPlayer members not found");
				return 0;
			}
			harmony.Patch(m, null, new HarmonyMethod(typeof(IntroSkip).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
			N.ULog.Info(Tag, "IntroSkip installed");
			return 1;
		}

		public static void Postfix(IntroPlayer __instance)
		{
			try
			{
				_elapsed.SetValue(__instance, 12.1f);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "IntroSkip: " + e.Message);
			}
		}
	}

	// ---------- LazyPictures ----------
	// Experimental. Pictures of mod effects (Add_On.EffectList) and ChildHood cats wait as transparent 2x2 placeholders in their materials.
	// Every route to the screen goes through SkeletonRenderer.Initialize or SkeletonGraphic.Initialize, which load the PNG files into the same Texture2D objects.
	// Effects are decoded in Add_On.init before any mod, so they are freed in Nail's constructor; ChildHood.LoadMod is replaced before its constructor runs.
	public static class LazyPictures
	{
		private sealed class Item
		{
			internal Texture2D Tex;
			internal string File;
			internal bool NonReadable;
			internal bool Done;
		}

		private const string Tag = "PerfPack";

		internal static bool Effects;

		private static readonly Dictionary<Spine.Unity.AtlasAsset, List<Item>> Pending = new Dictionary<Spine.Unity.AtlasAsset, List<Item>>();
		private static readonly Dictionary<Texture2D, Item> ByTex = new Dictionary<Texture2D, Item>();
		private static readonly Color32[] Clear = new Color32[4];
		private static HarmonyInstance _harmony;
		private static bool _hooked;
		private static int _loaded;
		private static float _loadSeconds;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int InstallEffects(HarmonyInstance harmony)
		{
			if (!Hook(harmony))
			{
				return 0;
			}
			Effects = true;
			N.ULog.Info(Tag, "EffectLazy installed");
			return 1;
		}

		// ChildHood is found by its DLL in a BaseMods folder, since NailOptimization loads before it; the option is declared only then.
		internal static bool ChildHoodPresent()
		{
			try
			{
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i].GetName().Name == "ChildHood")
					{
						return true;
					}
				}
				string d = N.UiHost.RootDir;
				while (!string.IsNullOrEmpty(d) && !string.Equals(Path.GetFileName(d), "BaseMods", StringComparison.OrdinalIgnoreCase))
				{
					d = Path.GetDirectoryName(d);
				}
				if (string.IsNullOrEmpty(d))
				{
					return true;
				}
				string[] dirs = Directory.GetDirectories(d);
				for (int i = 0; i < dirs.Length; i++)
				{
					if (File.Exists(Path.Combine(dirs[i], "ChildHood.dll")))
					{
						return true;
					}
				}
				return false;
			}
			catch (Exception ex)
			{
				N.ULog.Warn(Tag, "ChildHood check failed: " + ex.Message);
				return true;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int InstallCats(HarmonyInstance harmony)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].GetName().Name == "ChildHood")
				{
					N.ULog.Warn(Tag, "CatLazy not installed: ChildHood loaded before NailOptimization");
					return 0;
				}
			}
			if (!Hook(harmony))
			{
				return 0;
			}
			_harmony = harmony;
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			N.ULog.Info(Tag, "CatLazy installed (waits for ChildHood)");
			return 1;
		}

		private static bool Hook(HarmonyInstance harmony)
		{
			if (_hooked)
			{
				return true;
			}
			MethodBase r = N.PatchUtil.Resolve(Tag, typeof(Spine.Unity.SkeletonRenderer), "Initialize", new Type[] { typeof(bool) });
			MethodBase g = N.PatchUtil.Resolve(Tag, typeof(Spine.Unity.SkeletonGraphic), "Initialize", new Type[] { typeof(bool) });
			if (object.ReferenceEquals(r, null) || object.ReferenceEquals(g, null))
			{
				return false;
			}
			if (!N.PatchUtil.Install(harmony, Tag, r, new HarmonyMethod(typeof(LazyPictures).GetMethod("RendererPrefix", BindingFlags.Public | BindingFlags.Static)), null) || !N.PatchUtil.Install(harmony, Tag, g, new HarmonyMethod(typeof(LazyPictures).GetMethod("GraphicPrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return false;
			}
			_hooked = true;
			return true;
		}

		internal static bool IsPending(Texture2D tex)
		{
			return ByTex.ContainsKey(tex);
		}

		// A page without a size line takes it from the texture when the atlas is built. A page name is the first line of a block; region lines are indented.
		internal static bool PagesHaveSize(string atlasText)
		{
			string[] lines = atlasText.Replace("\r", "").Split(new char[] { '\n' });
			bool start = true;
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].Trim().Length == 0)
				{
					start = true;
					continue;
				}
				if (start && (i + 1 >= lines.Length || !lines[i + 1].StartsWith("size:", StringComparison.Ordinal)))
				{
					return false;
				}
				start = false;
			}
			return true;
		}

		public static void RendererPrefix(Spine.Unity.SkeletonRenderer __instance)
		{
			try
			{
				Load(__instance.skeletonDataAsset);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "LazyPictures: " + e);
			}
		}

		public static void GraphicPrefix(Spine.Unity.SkeletonGraphic __instance)
		{
			try
			{
				Load(__instance.skeletonDataAsset);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "LazyPictures: " + e);
			}
		}

		private static void Load(Spine.Unity.SkeletonDataAsset asset)
		{
			if (Pending.Count == 0 || asset == null || asset.atlasAssets == null)
			{
				return;
			}
			for (int a = 0; a < asset.atlasAssets.Length; a++)
			{
				Spine.Unity.AtlasAsset atlas = asset.atlasAssets[a];
				List<Item> items;
				if (atlas == null || !Pending.TryGetValue(atlas, out items))
				{
					continue;
				}
				Pending.Remove(atlas);
				for (int i = 0; i < items.Count; i++)
				{
					LoadItem(items[i]);
				}
			}
		}

		internal static void LoadTexture(Texture2D tex)
		{
			Item item;
			if (tex != null && ByTex.TryGetValue(tex, out item))
			{
				LoadItem(item);
			}
		}

		private static void LoadItem(Item item)
		{
			if (item.Done)
			{
				return;
			}
			item.Done = true;
			ByTex.Remove(item.Tex);
			float start = Time.realtimeSinceStartup;
			try
			{
				if (!item.Tex.LoadImage(File.ReadAllBytes(item.File), item.NonReadable))
				{
					N.ULog.Error(Tag, "LazyPictures: cannot decode " + item.File);
				}
				else if (item.NonReadable && AgentTextures.Enabled)
				{
					AgentTextures.MarkDropped(item.Tex);
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "LazyPictures: " + item.File + ": " + e.Message);
			}
			float took = Time.realtimeSinceStartup - start;
			_loaded++;
			_loadSeconds += took;
			if (_loaded <= 20 || took >= 0.1f)
			{
				N.ULog.Info(Tag, "LazyPictures: loaded " + item.Tex.name + " " + item.Tex.width + "x" + item.Tex.height + ", " + (int)(took * 1000f) + " ms; total " + _loaded + " pictures, " + (int)(_loadSeconds * 1000f) + " ms, waiting " + ByTex.Count);
			}
		}

		private static Texture2D Placeholder(string name)
		{
			Texture2D tex = new Texture2D(2, 2);
			tex.name = name;
			tex.SetPixels32(Clear);
			tex.Apply();
			return tex;
		}

		private static void Add(Spine.Unity.AtlasAsset atlas, Item item)
		{
			List<Item> items;
			if (!Pending.TryGetValue(atlas, out items))
			{
				items = new List<Item>();
				Pending[atlas] = items;
			}
			if (!items.Contains(item))
			{
				items.Add(item);
			}
		}

		// A picture with no PNG file found stays with the caller; Use.Tex becomes null for the rest.
		internal static void DeferEffects(List<EffectTexturesDriver.Use> uses)
		{
			Dictionary<Texture2D, string> files = new Dictionary<Texture2D, string>();
			for (int i = 0; i < uses.Count; i++)
			{
				if (uses[i].File != null && !files.ContainsKey(uses[i].Tex) && File.Exists(uses[i].File))
				{
					files[uses[i].Tex] = uses[i].File;
				}
			}
			Dictionary<Texture2D, Item> made = new Dictionary<Texture2D, Item>();
			double mb = 0;
			for (int i = 0; i < uses.Count; i++)
			{
				EffectTexturesDriver.Use u = uses[i];
				string file;
				if (!files.TryGetValue(u.Tex, out file))
				{
					continue;
				}
				Item item;
				if (!made.TryGetValue(u.Tex, out item))
				{
					item = new Item();
					item.Tex = Placeholder(u.Tex.name);
					item.File = file;
					item.NonReadable = true;
					made[u.Tex] = item;
					ByTex[item.Tex] = item;
					EffectTexturesDriver.Done.Add(item.Tex);
					mb += u.Tex.width * (double)u.Tex.height * 4 / 1048576;
				}
				u.Mat.mainTexture = item.Tex;
				Add(u.Atlas, item);
				u.Tex = null;
			}
			foreach (KeyValuePair<Texture2D, Item> kv in made)
			{
				UnityEngine.Object.DestroyImmediate(kv.Key);
			}
			N.ULog.Info(Tag, "EffectLazy: " + made.Count + " pictures wait for first use, " + (long)mb + " MB of level 0 freed");
		}

		private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			try
			{
				if (args.LoadedAssembly.GetName().Name != "ChildHood")
				{
					return;
				}
				AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
				Type type = args.LoadedAssembly.GetType("ChildHood_Mod.Creature.Harmony_Patch");
				MethodBase m = !object.ReferenceEquals(type, null) ? N.PatchUtil.Resolve(Tag, type, "LoadMod", Type.EmptyTypes) : null;
				if (object.ReferenceEquals(m, null))
				{
					N.ULog.Warn(Tag, "CatLazy: ChildHood.LoadMod not found");
					return;
				}
				uint il = HashIl(m);
				if (il != CatsIl)
				{
					N.ULog.Warn(Tag, "CatLazy: ChildHood.LoadMod changed (IL " + il.ToString("x8") + ", copy made for " + CatsIl.ToString("x8") + "): left as is");
					return;
				}
				_cats = type;
				if (N.PatchUtil.Install(_harmony, Tag, m, new HarmonyMethod(typeof(LazyPictures).GetMethod("CatsPrefix", BindingFlags.Public | BindingFlags.Static)), null))
				{
					N.ULog.Info(Tag, "CatLazy: ChildHood.LoadMod replaced");
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "CatLazy: " + e);
			}
		}

		private static Type _cats;
		private const uint CatsIl = 0x34ae3710u;

		// FNV-1a of the method's IL. A rebuilt mod gives another value, even with the same source.
		private static uint HashIl(MethodBase m)
		{
			MethodBody body = m.GetMethodBody();
			byte[] il = body != null ? body.GetILAsByteArray() : new byte[0];
			uint h = 2166136261u;
			for (int i = 0; i < il.Length; i++)
			{
				h = unchecked((h ^ il[i]) * 16777619u);
			}
			return h;
		}


		// Copy of ChildHood's LoadMod without the PNG decoding. Falls back to the original if ChildHood's members are not there.
		public static bool CatsPrefix()
		{
			System.Collections.IDictionary list;
			MethodInfo inDir;
			try
			{
				list = (System.Collections.IDictionary)_cats.GetField("AnimList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
				inDir = _cats.GetMethod("IsInAssemblyDirectory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (list == null || object.ReferenceEquals(inDir, null))
				{
					return true;
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "CatLazy: " + e);
				return true;
			}
			int cats = 0;
			foreach (DirectoryInfo mod in Add_On.instance.DirList)
			{
				try
				{
					if (!(bool)inDir.Invoke(null, new object[] { mod }) || !Directory.Exists(mod.FullName + "/CatAnimation"))
					{
						continue;
					}
				}
				catch (Exception e)
				{
					N.ULog.Error(Tag, "CatLazy: " + e);
					continue;
				}
				foreach (DirectoryInfo dir in new DirectoryInfo(mod.FullName + "/CatAnimation").GetDirectories())
				{
					try
					{
						string atlasText = File.ReadAllText(dir.FullName + "/atlas.txt");
						bool eager = !LazyPictures.PagesHaveSize(atlasText);
						List<Item> items = new List<Item>();
						List<Texture2D> textures = new List<Texture2D>();
						foreach (FileInfo file in dir.GetFiles())
						{
							if (!file.FullName.Contains(".png"))
							{
								continue;
							}
							string name = Path.GetFileNameWithoutExtension(file.Name);
							if (eager)
							{
								Texture2D tex = new Texture2D(2, 2);
								tex.LoadImage(File.ReadAllBytes(file.FullName));
								tex.name = name;
								textures.Add(tex);
								continue;
							}
							Item item = new Item();
							item.Tex = Placeholder(name);
							item.File = file.FullName;
							item.NonReadable = AgentTextures.Enabled;
							ByTex[item.Tex] = item;
							items.Add(item);
							textures.Add(item.Tex);
						}
						Spine.Unity.AtlasAsset atlas = Spine.Unity.AtlasAsset.CreateRuntimeInstance(atlasText, textures.ToArray(), (Shader)null, true);
						Spine.Unity.SkeletonDataAsset data = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(File.ReadAllText(dir.FullName + "/json.txt"), atlas, eager, 0.01f);
						list.Add(dir.Name, data);
						for (int i = 0; i < items.Count; i++)
						{
							Add(atlas, items[i]);
						}
						cats++;
					}
					catch (Exception e)
					{
						N.ULog.Error(Tag, "CatLazy: " + dir.Name + ": " + e);
					}
				}
			}
			N.ULog.Info(Tag, "CatLazy: " + cats + " animations wait for first use");
			return false;
		}
	}

	// ---------- MeshRelease ----------
	public static class MeshRelease
	{
		private const string Tag = "PerfPack";

		private static readonly Dictionary<Type, FieldInfo[]> RefFields = new Dictionary<Type, FieldInfo[]>();

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			N.Events.On(NoticeName.OnStageStart, Sweep, Tag);
			// No Dispose in MeshRendererBuffers.Initialize any more: a mod may hold a mesh it copied out of the renderer.
			// The replaced meshes stay until the day-start sweep, which destroys them only when nothing references them.
			N.ULog.Info(Tag, "MeshRelease installed (day-start sweep)");
			return 1;
		}

		// Renderers of the previous day are destroyed without SkeletonRenderer.OnDestroy, the only path that destroys
		// their meshes (r180: orphans +928..+950 per day restart, held meshes flat). At day start every "Skeleton Mesh"
		// that no MeshFilter and no Spine component field (three levels: renderer, buffers, double buffer, smart mesh)
		// references is destroyed.
		private static void Sweep()
		{
			try
			{
				SweepCore();
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "MeshRelease sweep failed, no meshes destroyed this day: " + e);
			}
		}

		private static void SweepCore()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Dictionary<Mesh, bool> held = new Dictionary<Mesh, bool>();
			UnityEngine.Object[] filters = Resources.FindObjectsOfTypeAll(typeof(MeshFilter));
			for (int i = 0; i < filters.Length; i++)
			{
				MeshFilter mf = filters[i] as MeshFilter;
				if (mf != null && mf.sharedMesh != null)
				{
					held[mf.sharedMesh] = true;
				}
			}
			UnityEngine.Object[] mbs = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
			for (int i = 0; i < mbs.Length; i++)
			{
				MonoBehaviour mb = mbs[i] as MonoBehaviour;
				if (mb == null)
				{
					continue;
				}
				string ns = mb.GetType().Namespace;
				if (ns != null && ns.StartsWith("Spine", StringComparison.Ordinal))
				{
					Collect(mb, held, 0);
				}
				else
				{
					CollectMeshFields(mb, held);
				}
			}
			int destroyed = 0;
			int kept = 0;
			UnityEngine.Object[] meshes = Resources.FindObjectsOfTypeAll(typeof(Mesh));
			for (int i = 0; i < meshes.Length; i++)
			{
				Mesh mesh = meshes[i] as Mesh;
				if (mesh == null || mesh.name != "Skeleton Mesh")
				{
					continue;
				}
				if (held.ContainsKey(mesh))
				{
					kept++;
					continue;
				}
				UnityEngine.Object.Destroy(mesh);
				destroyed++;
			}
			N.ULog.Info(Tag, "MeshRelease: destroyed " + destroyed + " orphaned meshes, kept " + kept + " (" + sw.ElapsedMilliseconds + " ms)");
		}

		private static void Collect(object v, Dictionary<Mesh, bool> held, int depth)
		{
			FieldInfo[] fs = FieldsOf(v.GetType());
			for (int f = 0; f < fs.Length; f++)
			{
				object w = fs[f].GetValue(v);
				if (w == null)
				{
					continue;
				}
				Mesh m = w as Mesh;
				if (!object.ReferenceEquals(m, null))
				{
					if (m != null)
					{
						held[m] = true;
					}
					continue;
				}
				if (AddMeshes(w, held))
				{
					continue;
				}
				if (depth < 3 && !(w is UnityEngine.Object) && !(w is Delegate) && !(w is System.Collections.IEnumerable))
				{
					Collect(w, held, depth + 1);
				}
			}
		}

		// Mesh[] or List<Mesh> (pools, afterimages): every element is held.
		private static bool AddMeshes(object w, Dictionary<Mesh, bool> held)
		{
			Mesh[] arr = w as Mesh[];
			if (arr != null)
			{
				for (int i = 0; i < arr.Length; i++)
				{
					if (arr[i] != null)
					{
						held[arr[i]] = true;
					}
				}
				return true;
			}
			List<Mesh> list = w as List<Mesh>;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i] != null)
					{
						held[list[i]] = true;
					}
				}
				return true;
			}
			return false;
		}

		// Components outside Spine: only fields declared as Mesh, Mesh[] or List<Mesh> (one level), cached per type.
		private static readonly Dictionary<Type, FieldInfo[]> MeshFields = new Dictionary<Type, FieldInfo[]>();

		private static void CollectMeshFields(MonoBehaviour mb, Dictionary<Mesh, bool> held)
		{
			Type type = mb.GetType();
			FieldInfo[] fs;
			if (!MeshFields.TryGetValue(type, out fs))
			{
				List<FieldInfo> list = new List<FieldInfo>();
				for (Type t = type; !object.ReferenceEquals(t, null) && !object.ReferenceEquals(t, typeof(MonoBehaviour)); t = t.BaseType)
				{
					FieldInfo[] all = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
					for (int i = 0; i < all.Length; i++)
					{
						Type ft = all[i].FieldType;
						if (object.ReferenceEquals(ft, typeof(Mesh)) || object.ReferenceEquals(ft, typeof(Mesh[])) || object.ReferenceEquals(ft, typeof(List<Mesh>)))
						{
							list.Add(all[i]);
						}
					}
				}
				fs = list.ToArray();
				MeshFields[type] = fs;
			}
			for (int f = 0; f < fs.Length; f++)
			{
				object w = fs[f].GetValue(mb);
				Mesh m = w as Mesh;
				if (m != null)
				{
					held[m] = true;
				}
				else if (w != null)
				{
					AddMeshes(w, held);
				}
			}
		}

		// Instance fields of reference types that can hold a mesh (no strings, delegates, value types), cached per type.
		private static FieldInfo[] FieldsOf(Type type)
		{
			FieldInfo[] all;
			if (RefFields.TryGetValue(type, out all))
			{
				return all;
			}
			List<FieldInfo> list = new List<FieldInfo>();
			for (Type t = type; !object.ReferenceEquals(t, null) && !object.ReferenceEquals(t, typeof(object)) && !object.ReferenceEquals(t, typeof(MonoBehaviour)); t = t.BaseType)
			{
				FieldInfo[] fs = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				for (int i = 0; i < fs.Length; i++)
				{
					Type ft = fs[i].FieldType;
					if (!ft.IsValueType && !object.ReferenceEquals(ft, typeof(string)) && !typeof(Delegate).IsAssignableFrom(ft))
					{
						list.Add(fs[i]);
					}
				}
			}
			all = list.ToArray();
			RefFields[type] = all;
			return all;
		}
	}

	// ---------- CursorScale ----------
	public static class CursorScale
	{
		private const string Tag = "PerfPack";
		private static bool _inside;
		private static Texture2D _made;
		private static Texture2D _shown;
		private static N.Counter _freed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase scale = N.PatchUtil.Resolve(Tag, typeof(CursorManager), "SetCursorScale", new Type[] { typeof(float) });
			MethodBase make = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "ScaleTexture", new Type[] { typeof(Texture2D), typeof(int), typeof(int), typeof(bool) });
			if (object.ReferenceEquals(scale, null) || object.ReferenceEquals(make, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPrefix(scale);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "CursorScale not installed: CursorManager.SetCursorScale already has a Prefix by " + owner);
				return 0;
			}
			_freed = N.Counters.Get(Tag, "cursorFreed");
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			if (!N.PatchUtil.Install(harmony, Tag, make, null, new HarmonyMethod(typeof(CursorScale).GetMethod("MakePostfix", pub))))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, scale, new HarmonyMethod(typeof(CursorScale).GetMethod("Prefix", pub)), new HarmonyMethod(typeof(CursorScale).GetMethod("Postfix", pub))))
			{
				return 0;
			}
			N.ULog.Info(Tag, "CursorScale installed (CursorManager.SetCursorScale)");
			return 1;
		}

		public static void Prefix()
		{
			_inside = true;
			_made = null;
		}

		public static void MakePostfix(Texture2D __result)
		{
			if (_inside)
			{
				_made = __result;
			}
		}

		// The new cursor is set by now, so the texture of the previous call is unused.
		public static void Postfix()
		{
			_inside = false;
			if (_made == null)
			{
				return;
			}
			if (_shown != null)
			{
				UnityEngine.Object.Destroy(_shown);
				if (_freed.Value == 0)
				{
					N.ULog.Info(Tag, "CursorScale freed its first texture");
				}
				_freed.Value++;
			}
			_shown = _made;
			_made = null;
		}
	}
}

namespace PerfPack
{
	// ---------- SoundPrewarm ----------
	public static class SoundPrewarm
	{
		private const string Tag = "PerfPack";
		internal static N.IntParam Budget;
		internal static N.IntParam KeepMax;
		private static readonly Dictionary<string, AudioClip> _kept = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
		private static readonly List<string> _order = new List<string>();
		private static readonly List<string> _queue = new List<string>();
		private static int _queuePos;
		private static bool _dirty;
		private static string _path;
		private static int _errors;
		private static long _loadTicks;
		private static int _missing;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			if (!object.ReferenceEquals(N.TypeCache.Resolve("SoundClipCache.Harmony_Patch"), null))
			{
				N.ULog.Warn(Tag, "SoundPrewarm not installed: the standalone SoundClipCache mod is loaded (disable one of them)");
				return 0;
			}
			Type t = N.TypeCache.Resolve("SoundEffectPlayer");
			MethodBase once = N.PatchUtil.Resolve(Tag, t, "PlayOnce_Mod", new Type[] { typeof(string), typeof(string), typeof(Vector2), typeof(float), typeof(float), typeof(AudioRolloffMode) });
			MethodBase loop = N.PatchUtil.Resolve(Tag, t, "Play_Mod", new Type[] { typeof(string), typeof(string), typeof(Transform), typeof(float), typeof(float), typeof(AudioRolloffMode) });
			if (object.ReferenceEquals(once, null) || object.ReferenceEquals(loop, null))
			{
				return 0;
			}
			HarmonyMethod post = new HarmonyMethod(typeof(SoundPrewarm).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
			int n = 0;
			n += N.PatchUtil.Install(harmony, Tag, once, null, post) ? 1 : 0;
			n += N.PatchUtil.Install(harmony, Tag, loop, null, post) ? 1 : 0;
			if (n == 0)
			{
				return 0;
			}
			_path = Path.Combine(N.UiHost.RootDir, "PerfPack.sounds.txt");
			ReadList();
			N.ResetRegistry.Register(new SoundSaver());
			GameObject go = new GameObject("NailOptimization.PerfPack.SoundPrewarm");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<SoundPrewarmDriver>();
			N.ULog.Info(Tag, "SoundPrewarm installed " + n + "/2 hooks, " + _queue.Count + " sounds listed (" + _path + ")");
			return 1;
		}

		// Postfix of PlayOnce_Mod and Play_Mod (parameter names of the originals). Cost per call: one dictionary lookup.
		public static void Postfix(string modid, string soundname, SoundEffectPlayer __result)
		{
			try
			{
				if (__result == null || soundname == null)
				{
					return;
				}
				// List line: the name alone, or mod id, tab, name.
				string key = string.IsNullOrEmpty(modid) ? soundname : modid + "\t" + soundname;
				if (_kept.ContainsKey(key) || _kept.Count >= KeepMax.Value)
				{
					return;
				}
				AudioSource src = __result.GetComponent<AudioSource>();
				AudioClip clip = src == null ? null : src.clip;
				if (clip == null)
				{
					return;
				}
				_kept[key] = clip;
				_order.Add(key);
				_dirty = true;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "SoundPrewarm postfix failed (logged once): " + e);
				}
			}
		}

		private static void ReadList()
		{
			string src = _path;
			if (!File.Exists(src))
			{
				// One-time seed from the standalone SoundClipCache list, when present.
				string old = Path.Combine(Application.dataPath, "SoundPrewarm.txt");
				if (!File.Exists(old))
				{
					return;
				}
				src = old;
				_dirty = true;
			}
			try
			{
				string[] lines = File.ReadAllLines(src);
				for (int i = 0; i < lines.Length; i++)
				{
					string l = lines[i].Trim();
					if (l.Length == 0 || l[0] == '#' || _queue.Contains(l))
					{
						continue;
					}
					_queue.Add(l);
				}
				if (!object.ReferenceEquals(src, _path))
				{
					N.ULog.Info(Tag, "SoundPrewarm list seeded from " + src + " (" + _queue.Count + " names)");
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "SoundPrewarm list unreadable: " + e.Message);
			}
		}

		// Driver: loads listed clips within the per-frame budget. Returns false when done.
		internal static bool Step()
		{
			if (_queuePos >= _queue.Count)
			{
				return false;
			}
			LobotomyBaseMod.ModAudioClipManager mods = LobotomyBaseModLib.Singleton<LobotomyBaseMod.ModAudioClipManager>.Instance;
			if (mods.AudioClipDic == null)
			{
				return true;
			}
			long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
			long budget = Budget.Value * System.Diagnostics.Stopwatch.Frequency / 1000;
			while (_queuePos < _queue.Count)
			{
				string name = _queue[_queuePos++];
				if (_kept.ContainsKey(name) || _kept.Count >= Math.Max(KeepMax.Value, _queue.Count))
				{
					continue;
				}
				AudioClip c = null;
				try
				{
					// Same lookup order as SoundEffectPlayer.PlayOnce_Mod.
					int tab = name.IndexOf('\t');
					string sound = tab < 0 ? name : name.Substring(tab + 1);
					c = mods.GetAudioClip(tab < 0 ? "" : name.Substring(0, tab), sound);
					if (c == null)
					{
						c = Resources.Load<AudioClip>("Sounds/" + sound);
					}
				}
				catch (Exception e)
				{
					_errors++;
					if (_errors == 1)
					{
						N.ULog.Error(Tag, "SoundPrewarm load failed (logged once): " + e);
					}
				}
				if (c == null)
				{
					_missing++;
				}
				else
				{
					_kept[name] = c;
					_order.Add(name);
				}
				if (System.Diagnostics.Stopwatch.GetTimestamp() - t0 >= budget)
				{
					break;
				}
			}
			_loadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
			if (_queuePos >= _queue.Count)
			{
				N.ULog.Info(Tag, "SoundPrewarm done: " + _kept.Count + " sounds kept, " + _missing + " not found, load time " + (_loadTicks * 1000 / System.Diagnostics.Stopwatch.Frequency) + " ms");
				return false;
			}
			return true;
		}

		internal static void Save()
		{
			if (!_dirty || _path == null)
			{
				return;
			}
			try
			{
				List<string> lines = new List<string>();
				lines.Add("# PerfPack SoundPrewarm: sounds loaded ahead at game start (<name>, or <mod id><tab><name>). Filled automatically; delete the file to start over.");
				lines.AddRange(_order);
				for (int i = 0; i < _queue.Count; i++)
				{
					if (!_kept.ContainsKey(_queue[i]))
					{
						lines.Add(_queue[i]);
					}
				}
				string tmp = _path + ".tmp";
				File.WriteAllLines(tmp, lines.ToArray());
				if (File.Exists(_path))
				{
					File.Delete(_path);
				}
				File.Move(tmp, _path);
				_dirty = false;
				N.ULog.Info(Tag, "SoundPrewarm list saved: " + (lines.Count - 1) + " names");
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "SoundPrewarm list save failed: " + e.Message);
			}
		}
	}

	internal sealed class SoundSaver : N.IResettable
	{
		public void ResetState()
		{
			SoundPrewarm.Save();
		}
	}

	public class SoundPrewarmDriver : MonoBehaviour
	{
		private bool _loading = true;

		public void Update()
		{
			if (!_loading)
			{
				return;
			}
			try
			{
				_loading = SoundPrewarm.Step();
			}
			catch (Exception e)
			{
				_loading = false;
				N.ULog.Error("PerfPack", "SoundPrewarm stopped: " + e);
			}
		}

		public void OnApplicationQuit()
		{
			SoundPrewarm.Save();
		}
	}
}

namespace PerfPack
{
	// ---------- CorpseFreeze ----------
	internal sealed class Corpse
	{
		internal WorkerModel Model;
		internal WorkerUnit Unit;
		internal float DeadSince;
		internal bool Frozen;
		internal readonly List<Behaviour> Off = new List<Behaviour>();
	}

	public static class CorpseFreeze
	{
		private const string Tag = "PerfPack";
		internal static N.FloatParam Delay;
		private static readonly Dictionary<WorkerModel, Corpse> _corpses = new Dictionary<WorkerModel, Corpse>();
		private static readonly List<WorkerModel> _drop = new List<WorkerModel>();
		private static int _depthSeq;
		private static Type _skeleton;
		private static Type _setter;
		private static Type _speech;
		private static MethodInfo _speechHide;
		private static N.Counter _frozen;
		private static N.Counter _thawed;
		private static N.Counter _reasserted;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install()
		{
			if (!object.ReferenceEquals(N.TypeCache.Resolve("CorpseFreeze.Harmony_Patch"), null))
			{
				N.ULog.Warn(Tag, "CorpseFreeze not installed: the standalone CorpseFreeze mod is loaded (disable one of them)");
				return 0;
			}
			_skeleton = N.TypeCache.Resolve("Spine.Unity.SkeletonRenderer");
			_setter = N.TypeCache.Resolve("WorkerSprite.WorkerSpriteSetter");
			_speech = N.TypeCache.Resolve("AgentSpeech");
			if (!object.ReferenceEquals(_speech, null))
			{
				_speechHide = _speech.GetMethod("DisableText", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			}
			_frozen = N.Counters.Get(Tag, "corpsesFrozen");
			_thawed = N.Counters.Get(Tag, "corpsesThawed");
			_reasserted = N.Counters.Get(Tag, "corpseReasserted");
			N.ResetRegistry.Register(new CorpseResetter());
			GameObject go = new GameObject("NailOptimization.PerfPack.CorpseFreeze");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<CorpseDriver>();
			N.ULog.Info(Tag, "CorpseFreeze installed (sweep 2/s, delay " + Delay.Value + " s; skeleton " + !object.ReferenceEquals(_skeleton, null)
				+ ", sprite setter " + !object.ReferenceEquals(_setter, null) + ", speech " + !object.ReferenceEquals(_speech, null) + ")");
			return 1;
		}

		internal static void Sweep()
		{
			if (GameManager.currentGameManager == null)
			{
				return;
			}
			float now = Time.time;
			if (AgentManager.instance != null)
			{
				IList<AgentModel> agents = AgentManager.instance.GetAgentList();
				for (int i = 0; i < agents.Count; i++)
				{
					Check(agents[i], now);
				}
			}
			if (OfficerManager.instance != null)
			{
				IList<OfficerModel> officers = OfficerManager.instance.GetOfficerList();
				for (int i = 0; i < officers.Count; i++)
				{
					Check(officers[i], now);
				}
			}
			// Bodies whose unit is gone, or workers no longer listed (removed at day end).
			_drop.Clear();
			foreach (KeyValuePair<WorkerModel, Corpse> kv in _corpses)
			{
				Corpse c = kv.Value;
				if (c.Unit == null)
				{
					_drop.Add(kv.Key);
				}
				else if (c.Frozen)
				{
					Reassert(c);
				}
			}
			for (int i = 0; i < _drop.Count; i++)
			{
				_corpses.Remove(_drop[i]);
			}
		}

		private static void Check(WorkerModel w, float now)
		{
			if (w == null)
			{
				return;
			}
			Corpse c;
			bool known = _corpses.TryGetValue(w, out c);
			if (!w.IsDead())
			{
				if (known)
				{
					if (c.Frozen)
					{
						Thaw(c);
					}
					_corpses.Remove(w);
				}
				return;
			}
			if (!known)
			{
				c = new Corpse();
				c.Model = w;
				c.Unit = w.GetWorkerUnit();
				c.DeadSince = now;
				_corpses[w] = c;
				return;
			}
			if (!c.Frozen && c.Unit != null && now - c.DeadSince >= Delay.Value)
			{
				Freeze(c);
			}
		}

		private static void Freeze(Corpse c)
		{
			GameObject go = c.Unit.gameObject;
			if (!object.ReferenceEquals(_skeleton, null))
			{
				OffAll(c, go, _skeleton);
			}
			OffAll(c, go, typeof(Animator));
			if (!object.ReferenceEquals(_setter, null))
			{
				OffAll(c, go, _setter);
			}
			if (!object.ReferenceEquals(_speech, null))
			{
				Component[] sp = go.GetComponentsInChildren(_speech, true);
				for (int i = 0; i < sp.Length; i++)
				{
					Behaviour b = sp[i] as Behaviour;
					if (b == null || !b.enabled)
					{
						continue;
					}
					// A lingering speech bubble is normally hidden by AgentSpeech itself: hide it before switching it off.
					if (!object.ReferenceEquals(_speechHide, null))
					{
						try
						{
							_speechHide.Invoke(b, null);
						}
						catch (Exception)
						{
						}
					}
					Off(c, b);
				}
			}
			Off(c, c.Unit);
			// Units share depth values, so overlapping bodies swap draw order: each body gets its own. The disabled unit no longer moves it; Thaw restores it through the unit.
			Vector3 p = c.Unit.transform.localPosition;
			p.z += 0.00001f * (float)(_depthSeq++ % 500 + 1);
			c.Unit.transform.localPosition = p;
			c.Frozen = true;
			_frozen.Value++;
		}

		private static void OffAll(Corpse c, GameObject go, Type t)
		{
			Component[] arr = go.GetComponentsInChildren(t, true);
			for (int i = 0; i < arr.Length; i++)
			{
				Off(c, arr[i] as Behaviour);
			}
		}

		// Only components that are on now; those, and only those, are switched back on by Thaw.
		private static void Off(Corpse c, Behaviour b)
		{
			if (b == null || !b.enabled)
			{
				return;
			}
			b.enabled = false;
			c.Off.Add(b);
		}

		// Another mod (camera culling) may switch a frozen component on again.
		private static void Reassert(Corpse c)
		{
			for (int i = 0; i < c.Off.Count; i++)
			{
				Behaviour b = c.Off[i];
				if (b != null && b.enabled)
				{
					b.enabled = false;
					_reasserted.Value++;
				}
			}
		}

		private static void Thaw(Corpse c)
		{
			for (int i = 0; i < c.Off.Count; i++)
			{
				Behaviour b = c.Off[i];
				if (b != null)
				{
					b.enabled = true;
				}
			}
			c.Off.Clear();
			c.Frozen = false;
			_thawed.Value++;
		}

		internal static void Clear()
		{
			_corpses.Clear();
		}

		internal static string Status()
		{
			int frozen = 0;
			foreach (KeyValuePair<WorkerModel, Corpse> kv in _corpses)
			{
				if (kv.Value.Frozen)
				{
					frozen++;
				}
			}
			return "dead tracked " + _corpses.Count + ", frozen " + frozen;
		}

		internal static void Fail(Exception e)
		{
			_errors++;
			if (_errors <= 3)
			{
				N.ULog.Error(Tag, "CorpseFreeze sweep failed (" + _errors + "/3 logged): " + e);
			}
		}
	}

	internal sealed class CorpseResetter : N.IResettable
	{
		public void ResetState()
		{
			CorpseFreeze.Clear();
		}
	}

	public class CorpseDriver : MonoBehaviour
	{
		private float _next;

		public void Update()
		{
			float t = Time.unscaledTime;
			if (t < _next)
			{
				return;
			}
			_next = t + 0.5f;
			try
			{
				CorpseFreeze.Sweep();
			}
			catch (Exception e)
			{
				CorpseFreeze.Fail(e);
			}
		}
	}
}

namespace PerfPack
{
	internal static class Standalone
	{
		internal static bool Loaded(string harmonyPatchType, string feature)
		{
			if (object.ReferenceEquals(N.TypeCache.Resolve(harmonyPatchType), null))
			{
				return false;
			}
			N.ULog.Warn("PerfPack", feature + " not installed: the standalone mod " + harmonyPatchType + " is loaded (disable one of them)");
			return true;
		}
	}

	// ---------- PanicThrottle ----------
	public static class PanicThrottle
	{
		private const string Tag = "PerfPack";
		internal static N.IntParam Budget;
		private static int _frame = -1;
		private static long _used;
		private static N.Counter _passed;
		private static N.Counter _deferred;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			if (Standalone.Loaded("PanicThrottle.Harmony_Patch", "PanicThrottle"))
			{
				return 0;
			}
			MethodBase m = N.PatchUtil.Resolve(Tag, N.TypeCache.Resolve("PanicReady"), "StartPanicAction", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			string owner = Owners.ForeignPrefix(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "PanicThrottle not installed: PanicReady.StartPanicAction already has a Prefix by " + owner);
				return 0;
			}
			_passed = N.Counters.Get(Tag, "panicStarted");
			_deferred = N.Counters.Get(Tag, "panicDeferred");
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(PanicThrottle).GetMethod("Prefix", pub)), new HarmonyMethod(typeof(PanicThrottle).GetMethod("Postfix", pub))))
			{
				return 0;
			}
			N.ULog.Info(Tag, "PanicThrottle installed (PanicReady.StartPanicAction, budget " + Budget.Value + " ms per frame)");
			return 1;
		}

		// The first start of a frame always passes; later ones wait for the next tick once the budget is used up.
		public static bool Prefix(out long __state)
		{
			__state = 0L;
			int f = Time.frameCount;
			if (f != _frame)
			{
				_frame = f;
				_used = 0L;
			}
			else if (_used >= Budget.Value * System.Diagnostics.Stopwatch.Frequency / 1000)
			{
				_deferred.Value++;
				return false;
			}
			__state = System.Diagnostics.Stopwatch.GetTimestamp();
			return true;
		}

		public static void Postfix(long __state)
		{
			if (__state == 0L)
			{
				return;
			}
			_used += System.Diagnostics.Stopwatch.GetTimestamp() - __state;
			_passed.Value++;
		}
	}

	// ---------- ArmorAtlas ----------
	// WorkerSpriteManager.GetArmorData_Mod never returns after a hit in ClothesSetMod, so every call reads the PNG again, and
	// it copies the whole texture once per sprite of the atlas. One EGO list slot costs 400-2000 ms and the copies are never
	// freed. The Prefix returns on a hit and cuts all sprites from one copy. The original has a foreach (GL-53) and runs only
	// for basic ids and when no file matches, where its loop finds nothing to protect.
	public static class ArmorAtlas
	{
		private const string Tag = "PerfPack";
		private static int _errors;
		private static int _plainErrors;
		private static Dictionary<string, string> _files;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "GetArmorData_Mod", new Type[] { typeof(LobotomyBaseMod.LcId), typeof(WorkerSprite.WorkerSprite).MakeByRefType() });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			if (PatchProcessor.IsPatched(m) != null)
			{
				N.ULog.Warn(Tag, "ArmorAtlas: GetArmorData_Mod has a foreign patch, left as is");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(ArmorAtlas).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "ArmorAtlas installed (WorkerSpriteManager.GetArmorData_Mod)");
			return 1;
		}

		// GetArmorData walks every mod folder once per sprite of the atlas on the first use of an id without a package: about
		// 1800 CheckNamedDir calls and 1 s per id. The Prefix fills lib from a one-time file index, so the original always
		// takes its lookup path and its foreach (GL-53) stays out of the run, except when lib has no entry to fall back to.
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int InstallPlain(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "GetArmorData", new Type[] { typeof(int), typeof(WorkerSprite.WorkerSprite).MakeByRefType() });
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			if (PatchProcessor.IsPatched(m) != null)
			{
				N.ULog.Warn(Tag, "ArmorAtlas: GetArmorData has a foreign patch, left as is");
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(ArmorAtlas).GetMethod("PlainPrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "ArmorAtlas installed (WorkerSpriteManager.GetArmorData)");
			return 1;
		}

		private static void BuildIndex()
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			foreach (DirectoryInfo modDir in ((Add_On)Add_On.instance).DirList)
			{
				DirectoryInfo dir = EquipmentDataLoader.CheckNamedDir(modDir, "Equipment");
				if (dir == null || !Directory.Exists(dir.FullName + "/Sprite/Armor"))
				{
					continue;
				}
				FileInfo[] files = new DirectoryInfo(dir.FullName + "/Sprite/Armor").GetFiles();
				for (int i = 0; i < files.Length; i++)
				{
					if (!map.ContainsKey(files[i].Name))
					{
						map[files[i].Name] = files[i].FullName;
					}
				}
			}
			_files = map;
			N.ULog.Info(Tag, "ArmorAtlas indexed " + map.Count + " armor files");
		}

		private static WorkerSprite.AtlasLoadData Cut(WorkerSprite.AtlasLoadData basic, Texture2D shared)
		{
			WorkerSprite.AtlasLoadData data = new WorkerSprite.AtlasLoadData();
			data.sprites.Clear();
			for (int j = 0; j < basic.sprites.Count; j++)
			{
				Sprite s = basic.sprites[j];
				Vector2 pivot = new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height);
				Sprite made = Sprite.Create(shared, s.rect, pivot, s.pixelsPerUnit, 0U, SpriteMeshType.FullRect, s.border, true);
				made.name = s.name;
				data.sprites.Add(made);
				data.count++;
			}
			return data;
		}

		public static bool PlainPrefix(WorkerSpriteManager __instance, int id)
		{
			Texture2D loaded = null;
			try
			{
				WorkerSprite.WorkerEquipmentSprite equip;
				__instance.equipData.GetData(WorkerSprite.EquipmentSpriteRegion.CLOTHES, out equip);
				Dictionary<int, WorkerSprite.SpriteResourceLoadData> lib = (equip as WorkerSprite.WorkerClothesSprite).lib;
				int key = id < 100000000 ? id + 100000000 : id;
				if (lib.ContainsKey(key))
				{
					return true;
				}
				if (_files == null)
				{
					BuildIndex();
				}
				string path;
				WorkerSprite.AtlasLoadData basic = __instance.GetClothesSet(0) as WorkerSprite.AtlasLoadData;
				if (basic != null && basic.sprites.Count > 0 && _files.TryGetValue((key - 100000000).ToString() + ".png", out path))
				{
					loaded = new Texture2D(2, 2);
					loaded.LoadImage(File.ReadAllBytes(path));
					lib.Add(key, Cut(basic, loaded));
					loaded = null;
					return true;
				}
				WorkerSprite.SpriteResourceLoadData fallback;
				if (lib.TryGetValue(key - 100000000, out fallback))
				{
					lib.Add(key, fallback);
				}
				return true;
			}
			catch (Exception e)
			{
				if (loaded != null)
				{
					UnityEngine.Object.Destroy(loaded);
				}
				_plainErrors++;
				if (_plainErrors == 1)
				{
					N.ULog.Error(Tag, "ArmorAtlas failed, original used (logged once): " + e);
				}
				return true;
			}
		}

		public static bool Prefix(WorkerSpriteManager __instance, LobotomyBaseMod.LcId id, ref WorkerSprite.WorkerSprite set)
		{
			Texture2D loaded = null;
			try
			{
				if (id.packageId == string.Empty)
				{
					return true;
				}
				if (__instance.ClothesSetMod == null)
				{
					__instance.ClothesSetMod = new Dictionary<LobotomyBaseMod.LcId, WorkerSprite.SpriteResourceLoadData>();
				}
				WorkerSprite.SpriteResourceLoadData known;
				if (__instance.ClothesSetMod.TryGetValue(id, out known))
				{
					set.Armor.SetSprite(known);
					return false;
				}
				List<ModInfo> mods = ((Add_On)Add_On.instance).ModList;
				for (int i = 0; i < mods.Count; i++)
				{
					if (mods[i].modid != id.packageId)
					{
						continue;
					}
					DirectoryInfo dir = EquipmentDataLoader.CheckNamedDir(mods[i].modpath, "Equipment");
					if (dir == null)
					{
						continue;
					}
					string path = dir.FullName + "/Sprite/Armor/" + id.id + ".png";
					if (!File.Exists(path))
					{
						continue;
					}
					WorkerSprite.AtlasLoadData basic = __instance.GetClothesSet(0) as WorkerSprite.AtlasLoadData;
					if (basic == null)
					{
						return true;
					}
					loaded = new Texture2D(2, 2);
					loaded.LoadImage(File.ReadAllBytes(path));
					Texture2D shared = Add_On.duplicateTexture(loaded);
					UnityEngine.Object.Destroy(loaded);
					loaded = shared;
					WorkerSprite.AtlasLoadData data = Cut(basic, shared);
					loaded = null;
					__instance.ClothesSetMod[id] = data;
					set.Armor.SetSprite(data);
					return false;
				}
				return true;
			}
			catch (Exception e)
			{
				if (loaded != null)
				{
					UnityEngine.Object.Destroy(loaded);
				}
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "ArmorAtlas failed, original used (logged once): " + e);
				}
				return true;
			}
		}
	}

	// ---------- InventoryDefer ----------
	public static class InventoryDefer
	{
		private const string Tag = "PerfPack";
		private static N.Flag _flag;
		internal static N.Flag ZayinFirst;
		private static FieldInfo _slots;
		private static FieldInfo _level;
		private static InventoryUI _window;
		private static Dictionary<LobotomyBaseMod.LcId, List<EquipmentModel>> _items;
		private static List<LobotomyBaseMod.LcId> _queue;
		private static int _next;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			_slots = typeof(InventoryItemController).GetField("slotDicMod", BindingFlags.NonPublic | BindingFlags.Instance);
			_level = typeof(InventoryItemController).GetField("selectedLevel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			MethodBase start = N.PatchUtil.Resolve(Tag, typeof(InventoryUI), "Start", Type.EmptyTypes);
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(DeployUI), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(_slots, null) || object.ReferenceEquals(start, null) || object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			if (!N.PatchUtil.Install(harmony, Tag, start, new HarmonyMethod(typeof(InventoryDefer).GetMethod("StartPrefix", BindingFlags.Public | BindingFlags.Static)), null)
				|| !N.PatchUtil.Install(harmony, Tag, tick, new HarmonyMethod(typeof(InventoryDefer).GetMethod("TickPrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "InventoryDefer installed (Inventory.InventoryUI.Start, DeployUI.Update)");
			return 1;
		}

		public static bool StartPrefix(InventoryUI __instance)
		{
			if (ZayinFirst.On && !object.ReferenceEquals(_level, null))
			{
				_level.SetValue(__instance.ItemController, (int)RiskLevel.ZAYIN);
			}
			if (!_flag.On)
			{
				return true;
			}
			try
			{
				_items = ((InventoryModel)InventoryModel.Instance).GetEquipmentListByTypeInfo_Mod();
				if (_items == null)
				{
					return true;
				}
				_slots.SetValue(__instance.ItemController, new Dictionary<LobotomyBaseMod.LcId, InventorySlot>());
				_queue = new List<LobotomyBaseMod.LcId>(_items.Keys);
				_next = 0;
				_window = __instance;
				__instance.IsEnabled = false;
				return false;
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "InventoryDefer start failed, list built as usual: " + e);
				_window = null;
				return true;
			}
		}

		// One slot per frame. An opening before the end builds the rest itself in InventoryItemController.Init.
		public static void TickPrefix()
		{
			if (object.ReferenceEquals(_window, null))
			{
				return;
			}
			try
			{
				if (_window == null || _window.IsEnabled)
				{
					Finish();
					return;
				}
				InventoryItemController c = _window.ItemController;
				Dictionary<LobotomyBaseMod.LcId, InventorySlot> slots = (Dictionary<LobotomyBaseMod.LcId, InventorySlot>)_slots.GetValue(c);
				while (_next < _queue.Count)
				{
					LobotomyBaseMod.LcId id = _queue[_next++];
					EquipmentTypeInfo info = EquipmentTypeList.instance.GetData_Mod(id);
					if (info.type == EquipmentTypeInfo.EquipmentType.SPECIAL || EquipmentTypeInfo.GetLcId(info) == 2 || slots.ContainsKey(id))
					{
						continue;
					}
					InventorySlot slot;
					if (info.type == EquipmentTypeInfo.EquipmentType.WEAPON)
					{
						slot = Prefab.LoadPrefab("UIComponent/Inventory/EquipmentSlot_Weapon").GetComponent<InventoryWeaponSlot>();
						slot.RectTransform.SetParent(c.WeaponListParent);
					}
					else
					{
						slot = Prefab.LoadPrefab("UIComponent/Inventory/EquipmentSlot_Armor").GetComponent<InventoryArmorSlot>();
						slot.RectTransform.SetParent(c.ArmorListParent);
					}
					slot.RectTransform.localScale = Vector3.one;
					slot.SetModel(info, _items[id]);
					slots.Add(id, slot);
					// Opening the window would enable it only for SetList to switch it off; SetList enables what the filter shows.
					slot.gameObject.SetActive(false);
					return;
				}
				Finish();
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "InventoryDefer stopped, the list builds the rest on opening: " + e);
				Finish();
			}
		}

		private static void Finish()
		{
			_window = null;
			_items = null;
			_queue = null;
		}
	}

	// ---------- InventoryTabs ----------
	public static class InventoryTabs
	{
		private const string Tag = "PerfPack";
		private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		private static readonly List<GraphicRaycaster> _raycasters = new List<GraphicRaycaster>();
		private static readonly List<Canvas> _canvases = new List<Canvas>();
		private static FieldInfo _enabled;
		private static FieldInfo _type;
		private static FieldInfo _detail;
		private static FieldInfo _slots;
		private static FieldInfo _level;
		private static FieldInfo _displayed;
		private static MethodInfo _sortList;
		private static MethodInfo _clearRank;
		private static MethodInfo _setRank;
		private static InventoryUI _opened;
		private static bool _closed;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type t = typeof(InventoryItemController);
			_enabled = typeof(InventoryUI).GetField("_isEnabled", Inst);
			_type = t.GetField("_currentWeaponType", Inst);
			_detail = t.GetField("_currentDetail", Inst);
			_slots = t.GetField("slotDicMod", Inst);
			_level = t.GetField("selectedLevel", Inst);
			_displayed = t.GetField("CurrentDisplayed", Inst);
			_sortList = t.GetMethod("SortList", Inst);
			_clearRank = t.GetMethod("ClearButtonRankColor", Inst);
			_setRank = t.GetMethod("SetButtonRankColor", Inst);
			MethodBase button = N.PatchUtil.Resolve(Tag, t, "OnClickButton", new Type[] { typeof(int) });
			MethodBase sort = N.PatchUtil.Resolve(Tag, t, "OnClickSort", new Type[] { typeof(int), typeof(bool) });
			MethodBase set = N.PatchUtil.Resolve(Tag, typeof(InventoryUI), "set_IsEnabled", new Type[] { typeof(bool) });
			MethodBase notice = N.PatchUtil.Resolve(Tag, typeof(InventoryUI), "OnNotice", new Type[] { typeof(string), typeof(object[]) });
			if (object.ReferenceEquals(_enabled, null) || object.ReferenceEquals(_type, null) || object.ReferenceEquals(_detail, null) || object.ReferenceEquals(_slots, null) || object.ReferenceEquals(_level, null)
				|| object.ReferenceEquals(_displayed, null) || object.ReferenceEquals(_sortList, null) || object.ReferenceEquals(_clearRank, null) || object.ReferenceEquals(_setRank, null)
				|| object.ReferenceEquals(button, null) || object.ReferenceEquals(sort, null) || object.ReferenceEquals(set, null) || object.ReferenceEquals(notice, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(button);
			if (owner == null)
			{
				owner = Owners.ForeignAny(sort);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "InventoryTabs not installed: the EGO list is already patched by " + owner);
				return 0;
			}
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			if (!N.PatchUtil.Install(harmony, Tag, button, new HarmonyMethod(typeof(InventoryTabs).GetMethod("ButtonPrefix", pub)), null)
				|| !N.PatchUtil.Install(harmony, Tag, sort, new HarmonyMethod(typeof(InventoryTabs).GetMethod("SortPrefix", pub)), null)
				|| !N.PatchUtil.Install(harmony, Tag, set, new HarmonyMethod(typeof(InventoryTabs).GetMethod("EnabledPrefix", pub)), null)
				|| !N.PatchUtil.Install(harmony, Tag, notice, null, new HarmonyMethod(typeof(InventoryTabs).GetMethod("NoticePostfix", pub))))
			{
				return 0;
			}
			N.ULog.Info(Tag, "InventoryTabs installed");
			return 1;
		}

		private static void ShowControl(RectTransform control, bool show)
		{
			if (!control.gameObject.activeSelf)
			{
				control.gameObject.SetActive(true);
			}
			Canvas canvas = control.GetComponent<Canvas>();
			if (canvas == null)
			{
				canvas = control.gameObject.AddComponent<Canvas>();
				control.gameObject.AddComponent<GraphicRaycaster>();
			}
			canvas.enabled = show;
			control.GetComponent<GraphicRaycaster>().enabled = show;
		}

		// OnClickButton with the controls hidden by their canvases.
		public static bool ButtonPrefix(InventoryItemController __instance, int index)
		{
			try
			{
				bool armor = index == 1;
				ShowControl(__instance.WeaponControl, !armor);
				ShowControl(__instance.ArmorControl, armor);
				__instance.ArmorButton.interactable = !armor;
				__instance.WeaponButton.interactable = armor;
				(armor ? __instance.WeaponButton : __instance.ArmorButton).OnPointerExit(null);
				__instance.ToolTipControl.SetParent(armor ? __instance.ArmorListParent : __instance.WeaponListParent);
				__instance.ToolTipControl.SetAsLastSibling();
				__instance.ToolTipControl.gameObject.SetActive(false);
				_detail.SetValue(__instance, -1);
				_type.SetValue(__instance, (InventoryItemType)index);
				__instance.OnClickSort(__instance.GetSelectedIndex(), true);
				Notice.instance.Send(NoticeName.OnChangeInventoryTap, new object[] { new InventoryItemTypeObject((InventoryItemType)index) });
				return false;
			}
			catch (Exception e)
			{
				return Failed(e);
			}
		}

		// OnClickSort with SetList inside: slots of the other tab are left alone, a slot is switched only when it has to change.
		public static bool SortPrefix(InventoryItemController __instance, int i, bool changedType)
		{
			try
			{
				int level = (int)_level.GetValue(__instance);
				level = i == level && !changedType ? -1 : i;
				_level.SetValue(__instance, level);
				(level == -1 ? _clearRank : _setRank).Invoke(__instance, null);
				bool armor = (InventoryItemType)_type.GetValue(__instance) == InventoryItemType.ARMOR;
				List<InventorySlot> list = new List<InventorySlot>();
				foreach (InventorySlot slot in ((Dictionary<LobotomyBaseMod.LcId, InventorySlot>)_slots.GetValue(__instance)).Values)
				{
					if (armor != (slot.Info.type == EquipmentTypeInfo.EquipmentType.ARMOR))
					{
						continue;
					}
					bool on = level == -1 || int.Parse(slot.Info.grade) - 1 == level;
					if (slot.gameObject.activeSelf != on)
					{
						slot.gameObject.SetActive(on);
					}
					if (on)
					{
						list.Add(slot);
					}
				}
				list.Sort(new Comparison<InventorySlot>(InventorySlot.SortCompare));
				_sortList.Invoke(__instance, new object[] { list });
				_displayed.SetValue(__instance, list);
				return false;
			}
			catch (Exception e)
			{
				return Failed(e);
			}
		}

		// The first opening of the day switches the window on as usual, later closings only hide it.
		public static bool EnabledPrefix(InventoryUI __instance, bool value)
		{
			try
			{
				if (value)
				{
					if (!_closed || !object.ReferenceEquals(_opened, __instance))
					{
						_opened = __instance;
						return true;
					}
					Hide(__instance, false);
					_enabled.SetValue(__instance, true);
					return false;
				}
				if (!object.ReferenceEquals(_opened, __instance) || _closed)
				{
					return true;
				}
				Hide(__instance, true);
				_enabled.SetValue(__instance, false);
				return false;
			}
			catch (Exception e)
			{
				return Failed(e);
			}
		}

		// The day needs nothing from the window: switched off for real, so its slots do not run during the day.
		public static void NoticePostfix(InventoryUI __instance, string notice)
		{
			if (notice != NoticeName.OnStageStart || !_closed || !object.ReferenceEquals(_opened, __instance))
			{
				return;
			}
			try
			{
				__instance.gameObject.SetActive(false);
				Hide(__instance, false);
				_opened = null;
			}
			catch (Exception e)
			{
				Failed(e);
			}
		}

		private static void Hide(InventoryUI window, bool hide)
		{
			if (hide)
			{
				_canvases.Clear();
				Canvas[] canvases = window.GetComponentsInChildren<Canvas>(true);
				for (int i = 0; i < canvases.Length; i++)
				{
					if (canvases[i].enabled)
					{
						_canvases.Add(canvases[i]);
						canvases[i].enabled = false;
					}
				}
				_raycasters.Clear();
				GraphicRaycaster[] all = window.GetComponentsInChildren<GraphicRaycaster>(true);
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i].enabled)
					{
						_raycasters.Add(all[i]);
						all[i].enabled = false;
					}
				}
			}
			else
			{
				for (int i = 0; i < _raycasters.Count; i++)
				{
					if (_raycasters[i] != null)
					{
						_raycasters[i].enabled = true;
					}
				}
				_raycasters.Clear();
				for (int i = 0; i < _canvases.Count; i++)
				{
					if (_canvases[i] != null)
					{
						_canvases[i].enabled = true;
					}
				}
				_canvases.Clear();
			}
			_closed = hide;
		}

		private static bool Failed(Exception e)
		{
			_errors++;
			if (_errors == 1)
			{
				N.ULog.Error(Tag, "InventoryTabs failed, original used (logged once): " + e);
			}
			return true;
		}
	}

	// ---------- FastScale ----------
	public static class FastScale
	{
		private const string Tag = "PerfPack";
		private static bool _customSprites;
		private static int _loadFrame = -1;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase scale = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "ScaleTexture", new Type[] { typeof(Texture2D), typeof(int), typeof(int), typeof(bool) });
			MethodBase load = N.PatchUtil.Resolve(Tag, typeof(WorkerSpriteManager), "LoadCustomSprites", Type.EmptyTypes);
			if (object.ReferenceEquals(scale, null) || object.ReferenceEquals(load, null))
			{
				return 0;
			}
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			if (!N.PatchUtil.Install(harmony, Tag, scale, new HarmonyMethod(typeof(FastScale).GetMethod("Prefix", pub)), null))
			{
				return 0;
			}
			N.PatchUtil.Install(harmony, Tag, load, new HarmonyMethod(typeof(FastScale).GetMethod("LoadPrefix", pub)), new HarmonyMethod(typeof(FastScale).GetMethod("LoadPostfix", pub)));
			N.ULog.Info(Tag, "FastScale installed (WorkerSpriteManager.ScaleTexture)");
			return 1;
		}

		public static void LoadPrefix()
		{
			_customSprites = true;
			_loadFrame = Time.frameCount;
		}

		public static void LoadPostfix()
		{
			_customSprites = false;
		}

		public static bool Prefix(Texture2D source, int targetWidth, int targetHeight, bool mipmap, ref Texture2D __result)
		{
			// LoadCustomSprites is synchronous: a flag from another frame means it threw before LoadPostfix.
			if (!_customSprites || _loadFrame != Time.frameCount)
			{
				return true;
			}
			RenderTexture prev = RenderTexture.active;
			RenderTexture rt = null;
			try
			{
				TextureFormat f = source.format;
				if (f != TextureFormat.RGBA32 && f != TextureFormat.ARGB32 && f != TextureFormat.RGB24)
				{
					return true;
				}
				rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
				// The game's loop samples mip 0 at u = x / width with no half-texel shift; a blit samples texel centers.
				source.mipMapBias = -16f;
				Graphics.Blit(source, rt, Vector2.one, new Vector2(0.5f / source.width - 0.5f / targetWidth, 0.5f / source.height - 0.5f / targetHeight));
				RenderTexture.active = rt;
				Texture2D result = new Texture2D(targetWidth, targetHeight, f, mipmap);
				result.ReadPixels(new Rect(0f, 0f, (float)targetWidth, (float)targetHeight), 0, 0, false);
				result.Apply();
				__result = result;
				// LoadCustomSprite never uses its decoded source again and never frees it.
				UnityEngine.Object.Destroy(source);
				return false;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors <= 3)
				{
					N.ULog.Error(Tag, "FastScale failed, the game's scaling runs (" + _errors + "/3 logged): " + e);
				}
				return true;
			}
			finally
			{
				RenderTexture.active = prev;
				if (rt != null)
				{
					RenderTexture.ReleaseTemporary(rt);
				}
			}
		}
	}

	// ---------- SlotCull ----------
	public static class SlotCull
	{
		private const string Tag = "PerfPack";
		private static readonly Vector3[] _slot = new Vector3[4];
		private static readonly Vector3[] _view = new Vector3[4];
		private static RectTransform _viewRt;
		private static int _viewFrame = -1;
		private static N.Counter _skipped;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			if (Standalone.Loaded("InventoryLoadPerf.Harmony_Patch", "SlotCull"))
			{
				return 0;
			}
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(InventorySlot), "LateUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			_skipped = N.Counters.Get(Tag, "slotUpdatesSkipped");
			if (!N.PatchUtil.Install(harmony, Tag, m, new HarmonyMethod(typeof(SlotCull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return 0;
			}
			N.ULog.Info(Tag, "SlotCull installed (Inventory.InventorySlot.LateUpdate)");
			return 1;
		}

		public static bool Prefix(InventorySlot __instance)
		{
			try
			{
				InventoryUI w = InventoryUI.CurrentWindow;
				if (w == null || w.ItemController == null || __instance.Info == null)
				{
					return true;
				}
				ScrollRect sr = __instance.Info.type == EquipmentTypeInfo.EquipmentType.WEAPON ? w.ItemController.WeaponScroll : w.ItemController.ArmorScroll;
				if (sr == null)
				{
					return true;
				}
				RectTransform vp = sr.viewport != null ? sr.viewport : sr.GetComponent<RectTransform>();
				if (vp == null || __instance.RectTransform == null)
				{
					return true;
				}
				int f = Time.frameCount;
				if (!object.ReferenceEquals(vp, _viewRt) || f != _viewFrame)
				{
					_viewRt = vp;
					_viewFrame = f;
					vp.GetWorldCorners(_view);
				}
				__instance.RectTransform.GetWorldCorners(_slot);
				bool visible = _slot[2].x >= _view[0].x && _slot[0].x <= _view[2].x && _slot[2].y >= _view[0].y && _slot[0].y <= _view[2].y;
				if (!visible)
				{
					_skipped.Value++;
				}
				return visible;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "SlotCull prefix failed, slot updated as usual (logged once): " + e);
				}
				return true;
			}
		}
	}

	// ---------- PathCheck ----------
	public static class PathCheck
	{
		private const string Tag = "PerfPack";
		private const int MaxMismatchLog = 20;
		private const int VerifyLogEvery = 5000;
		private static readonly Queue<MapNode> _queue = new Queue<MapNode>();
		private static readonly HashSet<MapNode> _seen = new HashSet<MapNode>();
		private static N.Counter _nodeTarget;
		private static N.Counter _edgeTarget;
		private static N.Counter _verified;
		private static N.Counter _mismatches;
		private static MethodBase _target;
		private static MethodBase _search;
		private static bool _checkedLate;
		private static bool _off;
		// Candeloro (HMI.Candeloro2.MOD) puts a Transpiler on SearchPath that skips the (node, edge) pairs in its static
		// blockedEdges. While that dictionary is empty its search equals the vanilla one; while it holds pairs the original runs.
		private const string CandeloroOwner = "HMI.Candeloro2.MOD";
		private static System.Collections.ICollection _blocked;
		private static int _errors;
		internal static bool Verify;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			Type t = typeof(MovableObjectNode);
			MethodBase m = N.PatchUtil.Resolve(Tag, t, "CheckConnectedInPassage", new Type[] { t, t });
			_search = N.PatchUtil.Resolve(Tag, typeof(GraphAstar), "SearchPath", new Type[] { typeof(MapNode), typeof(MapNode), typeof(bool) });
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_search, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner == null)
			{
				owner = Owners.ForeignAny(_search);
			}
			if (owner != null)
			{
				N.ULog.Warn(Tag, "PathCheck not installed: the path search is already patched by " + owner);
				return 0;
			}
			_target = m;
			_nodeTarget = N.Counters.Get(Tag, "pathCheckNodeTarget");
			_edgeTarget = N.Counters.Get(Tag, "pathCheckEdgeTarget");
			_verified = N.Counters.Get(Tag, "pathCheckVerified");
			_mismatches = N.Counters.Get(Tag, "pathCheckMismatches");
			HarmonyMethod pre = new HarmonyMethod(typeof(PathCheck).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod post = Verify ? new HarmonyMethod(typeof(PathCheck).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)) : null;
			if (!N.PatchUtil.Install(harmony, Tag, m, pre, post))
			{
				return 0;
			}
			N.ULog.Info(Tag, "PathCheck installed, verify=" + Verify);
			return 1;
		}

		// __state: -1 = no own answer, 0/1 = own answer for the verify Postfix.
		public static bool Prefix(MovableObjectNode n1, MovableObjectNode n2, ref bool __result, out int __state)
		{
			__state = -1;
			try
			{
				if (_off || n1 == null || n2 == null)
				{
					return true;
				}
				if (!_checkedLate)
				{
					CheckLate();
					if (_off)
					{
						return true;
					}
				}
				if (_blocked != null && _blocked.Count > 0)
				{
					return true;
				}
				bool answer;
				if (n2.currentNode != null)
				{
					_nodeTarget.Value++;
					// MoveToNode: SearchPath never returns null, so any unit standing on a node or an edge gets MOVE.
					answer = n1.currentNode != null || n1.currentEdge != null;
				}
				else if (n1.currentNode == null && n1.currentEdge == null)
				{
					answer = false;
				}
				else if (n2.currentEdge == null)
				{
					return true; // the original throws here; keep that
				}
				else
				{
					_edgeTarget.Value++;
					answer = (n1.currentNode == null && n1.currentEdge == n2.currentEdge) || Reaches(n1, n2.currentEdge);
				}
				if (Verify)
				{
					__state = answer ? 1 : 0;
					return true;
				}
				__result = answer;
				return false;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "PathCheck prefix failed, original used (logged once): " + e);
				}
				return true;
			}
		}

		public static void Postfix(MovableObjectNode n1, MovableObjectNode n2, bool __result, int __state)
		{
			if (__state < 0)
			{
				return;
			}
			_verified.Value++;
			if (_verified.Value % VerifyLogEvery == 0)
			{
				N.ULog.Info(Tag, "PathCheck verify: checked=" + _verified.Value + " mismatches=" + _mismatches.Value + " nodeTarget=" + _nodeTarget.Value + " edgeTarget=" + _edgeTarget.Value);
			}
			if ((__state == 1) == __result)
			{
				return;
			}
			_mismatches.Value++;
			if (_mismatches.Value <= MaxMismatchLog)
			{
				try
				{
					N.ULog.Error(Tag, "PathCheck mismatch: original=" + __result + " own=" + (__state == 1) + " n1 node=" + Describe(n1.currentNode) + " edge=" + Describe(n1.currentEdge) + " n2 node=" + Describe(n2.currentNode) + " edge=" + Describe(n2.currentEdge) + " sameEdge=" + (n1.currentEdge == n2.currentEdge));
				}
				catch (Exception e)
				{
					N.ULog.Error(Tag, "PathCheck mismatch: original=" + __result + " own=" + (__state == 1) + ", details failed: " + e.Message);
				}
			}
		}

		private static string Describe(MapNode node)
		{
			return node == null ? "-" : node.GetId() + "(active=" + node.activate + " rabbit=" + node.rabbitUnpassable + ")";
		}

		private static string Describe(MapEdge edge)
		{
			return edge == null ? "-" : Describe(edge.node1) + "~" + Describe(edge.node2);
		}

		// A mod may install its Transpiler after the modules; looked at once, at the first check in the session.
		private static void CheckLate()
		{
			_checkedLate = true;
			string owner = Owners.ForeignAny(_search);
			if (owner == null)
			{
				owner = Owners.ForeignAny(_target);
			}
			if (owner != null)
			{
				if (Owners.ForeignAny(_target) == null && OnlyCandeloro(_search))
				{
					_blocked = CandeloroBlocked();
					if (_blocked != null)
					{
						N.ULog.Info(Tag, "PathCheck kept: the path search is patched by " + CandeloroOwner + "; the original runs while its edges are blocked");
						return;
					}
				}
				_off = true;
				N.ULog.Warn(Tag, "PathCheck switched off: the path search is patched by " + owner);
			}
		}

		// True when every patch on 'm' outside NailOptimization belongs to Candeloro and at least one does.
		private static bool OnlyCandeloro(MethodBase m)
		{
			Patches ps = PatchProcessor.IsPatched(m);
			if (ps == null)
			{
				return false;
			}
			bool seen = false;
			return OnlyCandeloroIn(ps.Prefixes, ref seen) && OnlyCandeloroIn(ps.Postfixes, ref seen) && OnlyCandeloroIn(ps.Transpilers, ref seen) && seen;
		}

		private static bool OnlyCandeloroIn(IList<Patch> list, ref bool seen)
		{
			for (int i = 0; i < list.Count; i++)
			{
				string o = list[i].owner;
				if (o != null && o.StartsWith("Lobotomy.NailOptimization.", StringComparison.Ordinal))
				{
					continue;
				}
				if (o != CandeloroOwner)
				{
					return false;
				}
				seen = true;
			}
			return true;
		}

		// Candeloro's blockedEdges, taken from the class of its SearchPath Transpiler; null when it cannot be read.
		private static System.Collections.ICollection CandeloroBlocked()
		{
			try
			{
				IList<Patch> ts = PatchProcessor.IsPatched(_search).Transpilers;
				for (int i = 0; i < ts.Count; i++)
				{
					if (ts[i].owner != CandeloroOwner || object.ReferenceEquals(ts[i].patch, null))
					{
						continue;
					}
					FieldInfo f = ts[i].patch.DeclaringType.GetField("blockedEdges", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
					if (!object.ReferenceEquals(f, null))
					{
						return f.GetValue(null) as System.Collections.ICollection;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "PathCheck: Candeloro blockedEdges not readable: " + e.Message);
			}
			return null;
		}

		// The rules of GraphAstar.SearchPath(start, tempNode, isRabbit: true) for a temp node joined to both ends of 'goal':
		// a reached node with rabbitUnpassable is not expanded, inactive nodes are not entered (the start is not tested).
		private static bool Reaches(MovableObjectNode from, MapEdge goal)
		{
			_queue.Clear();
			_seen.Clear();
			if (from.currentNode != null)
			{
				Add(from.currentNode);
			}
			else
			{
				// On another edge the original starts from a temp node joined to both ends of that edge.
				AddActive(from.currentEdge.node1);
				AddActive(from.currentEdge.node2);
			}
			while (_queue.Count > 0)
			{
				MapNode node = _queue.Dequeue();
				if (node.rabbitUnpassable)
				{
					continue;
				}
				if (node == goal.node1 || node == goal.node2)
				{
					return true;
				}
				MapEdge[] edges = node.GetEdges();
				for (int i = 0; i < edges.Length; i++)
				{
					AddActive(edges[i].ConnectedNode(node));
				}
			}
			return false;
		}

		private static void AddActive(MapNode node)
		{
			if (node != null && node.activate)
			{
				Add(node);
			}
		}

		private static void Add(MapNode node)
		{
			if (_seen.Add(node))
			{
				_queue.Enqueue(node);
			}
		}
	}

	// ---------- FastSearch ----------
	public static class FastSearch
	{
		private struct Item
		{
			public MapNode node;
			public float cost;
			public float h;
		}

		private delegate List<MapEdge> EdgesFn(MapNode node);

		private const string Tag = "PerfPack";
		private const int MaxMismatchLog = 20;
		private const int VerifyLogEvery = 5000;
		private static readonly Dictionary<MapNode, int> _slots = new Dictionary<MapNode, int>();
		private static readonly List<MapEdge> _trace = new List<MapEdge>();
		private static readonly List<EdgeDirection> _traceDirs = new List<EdgeDirection>();
		private static Item[] _heap = new Item[256];
		private static float[] _infoCost = new float[256];
		private static MapEdge[] _infoEdge = new MapEdge[256];
		private static bool[] _hasInfo = new bool[256];
		private static bool[] _closed = new bool[256];
		private static int _heapCount;
		private static int _used;
		private static EdgesFn _edges;
		private static N.Counter _verified;
		private static N.Counter _mismatches;
		private static int _errors;
		internal static bool Verify;
		internal static N.Flag Live;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(Tag, typeof(GraphAstar), "SearchPath", new Type[] { typeof(MapNode), typeof(MapNode), typeof(bool) });
			FieldInfo field = typeof(MapNode).GetField("edges", BindingFlags.NonPublic | BindingFlags.Instance);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(field, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(m);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "FastSearch not installed: the path search is already patched by " + owner);
				return 0;
			}
			DynamicMethod get = new DynamicMethod("FastSearch_Edges", typeof(List<MapEdge>), new Type[] { typeof(MapNode) }, typeof(MapNode), true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Ret);
			_edges = (EdgesFn)get.CreateDelegate(typeof(EdgesFn));
			_verified = N.Counters.Get(Tag, "fastSearchVerified");
			_mismatches = N.Counters.Get(Tag, "fastSearchMismatches");
			HarmonyMethod pre = new HarmonyMethod(typeof(FastSearch).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod post = Verify ? new HarmonyMethod(typeof(FastSearch).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)) : null;
			if (!N.PatchUtil.Install(harmony, Tag, m, pre, post))
			{
				return 0;
			}
			N.ULog.Info(Tag, "FastSearch installed, verify=" + Verify);
			return 1;
		}

		public static bool Prefix(MapNode startPoint, MapNode endPoint, bool isRabbit, ref PathResult __result, out PathResult __state)
		{
			__state = null;
			try
			{
				if (startPoint == null || endPoint == null || !Live.On)
				{
					return true;
				}
				PathResult own = Search(startPoint, endPoint, isRabbit);
				if (Verify)
				{
					__state = own;
					return true;
				}
				__result = own;
				return false;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "FastSearch failed, original used (logged once): " + e);
				}
				return true;
			}
		}

		public static void Postfix(MapNode startPoint, MapNode endPoint, PathResult __result, PathResult __state)
		{
			if (__state == null)
			{
				return;
			}
			_verified.Value++;
			if (_verified.Value % VerifyLogEvery == 0)
			{
				N.ULog.Info(Tag, "FastSearch verify: checked=" + _verified.Value + " mismatches=" + _mismatches.Value);
			}
			if (Same(__result, __state))
			{
				return;
			}
			_mismatches.Value++;
			if (_mismatches.Value <= MaxMismatchLog)
			{
				N.ULog.Error(Tag, "FastSearch mismatch: " + startPoint.GetId() + " -> " + endPoint.GetId() + " original edges=" + __result.pathEdges.Length + " cost=" + __result.totalCost + " own edges=" + __state.pathEdges.Length + " cost=" + __state.totalCost);
			}
		}

		private static bool Same(PathResult a, PathResult b)
		{
			if (a.pathEdges.Length != b.pathEdges.Length || a.totalCost != b.totalCost)
			{
				return false;
			}
			for (int i = 0; i < a.pathEdges.Length; i++)
			{
				if (a.pathEdges[i] != b.pathEdges[i] || a.edgeDirections[i] != b.edgeDirections[i])
				{
					return false;
				}
			}
			return true;
		}

		// GraphAstar.SearchPath step by step: a node can be queued and expanded twice, a closed node is never re-entered.
		private static PathResult Search(MapNode start, MapNode end, bool isRabbit)
		{
			_slots.Clear();
			_used = 0;
			_heapCount = 0;
			Vector2 endPos = end.GetPosition();
			Push(start, 0f, 0f);
			while (_heapCount > 0)
			{
				Item top = Pop();
				MapNode node = top.node;
				if (node == end)
				{
					return Build(node);
				}
				int own = Slot(node); // Slot may replace the array
				_closed[own] = true;
				List<MapEdge> edges = _edges(node);
				int count = edges.Count;
				for (int i = 0; i < count; i++)
				{
					MapEdge edge = edges[i];
					MapNode next = edge.ConnectedNode(node);
					if (next == null || !next.activate)
					{
						continue;
					}
					int slot = Slot(next);
					if (_closed[slot] || (isRabbit && node.rabbitUnpassable))
					{
						continue;
					}
					float cost = top.cost + edge.cost;
					if (!_hasInfo[slot] || _infoCost[slot] > cost)
					{
						Push(next, cost, GraphAstar.ComputeHeuristic(next.GetPosition(), endPos));
						_hasInfo[slot] = true;
						_infoCost[slot] = cost;
						_infoEdge[slot] = edge;
					}
				}
			}
			return new PathResult(new MapEdge[0], new EdgeDirection[0], 0f);
		}

		private static PathResult Build(MapNode node)
		{
			_trace.Clear();
			_traceDirs.Clear();
			float total = 0f;
			for (;;)
			{
				int slot;
				if (!_slots.TryGetValue(node, out slot) || !_hasInfo[slot])
				{
					break;
				}
				MapEdge edge = _infoEdge[slot];
				node = edge.ConnectedNode(node);
				_trace.Add(edge);
				total += edge.cost;
				_traceDirs.Add(edge.node1 == node ? EdgeDirection.FORWARD : EdgeDirection.BACKWARD);
			}
			int n = _trace.Count;
			MapEdge[] pathEdges = new MapEdge[n];
			EdgeDirection[] dirs = new EdgeDirection[n];
			for (int i = 0; i < n; i++)
			{
				pathEdges[i] = _trace[n - 1 - i];
				dirs[i] = _traceDirs[n - 1 - i];
			}
			return new PathResult(pathEdges, dirs, total);
		}

		private static int Slot(MapNode node)
		{
			int slot;
			if (_slots.TryGetValue(node, out slot))
			{
				return slot;
			}
			if (_used == _closed.Length)
			{
				int size = _used * 2;
				Array.Resize(ref _infoCost, size);
				Array.Resize(ref _infoEdge, size);
				Array.Resize(ref _hasInfo, size);
				Array.Resize(ref _closed, size);
			}
			slot = _used++;
			_hasInfo[slot] = false;
			_closed[slot] = false;
			_slots.Add(node, slot);
			return slot;
		}

		// PathScore.CompareTo.
		private static int Compare(int a, int b)
		{
			return (int)((_heap[a].cost + _heap[a].h - (_heap[b].cost + _heap[b].h)) * 10f);
		}

		private static void Swap(int a, int b)
		{
			Item t = _heap[a];
			_heap[a] = _heap[b];
			_heap[b] = t;
		}

		// PriorityQueue.Enqueue.
		private static void Push(MapNode node, float cost, float h)
		{
			if (_heapCount == _heap.Length)
			{
				Array.Resize(ref _heap, _heapCount * 2);
			}
			int i = _heapCount++;
			_heap[i].node = node;
			_heap[i].cost = cost;
			_heap[i].h = h;
			while (i > 0)
			{
				int parent = (i - 1) / 2;
				if (Compare(i, parent) >= 0)
				{
					break;
				}
				Swap(i, parent);
				i = parent;
			}
		}

		// PriorityQueue.Dequeue.
		private static Item Pop()
		{
			int last = _heapCount - 1;
			Item top = _heap[0];
			_heap[0] = _heap[last];
			_heapCount = last;
			last--;
			int i = 0;
			for (;;)
			{
				int child = i * 2 + 1;
				if (child > last)
				{
					break;
				}
				int right = child + 1;
				if (right <= last && Compare(right, child) < 0)
				{
					child = right;
				}
				if (Compare(i, child) <= 0)
				{
					break;
				}
				Swap(i, child);
				i = child;
			}
			return top;
		}
	}

	// ---------- IdleSpeed ----------
	public static class IdleSpeed
	{
		private const string Tag = "PerfPack";
		private static readonly Dictionary<AgentModel, float> _last = new Dictionary<AgentModel, float>();
		private static N.Counter _skipped;
		private static N.Counter _real;
		private static bool _inTick;
		private static int _errors;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Install(HarmonyInstance harmony)
		{
			MethodBase value = N.PatchUtil.Resolve(Tag, typeof(AgentModel), "GetMovementValue", Type.EmptyTypes);
			MethodBase tick = N.PatchUtil.Resolve(Tag, typeof(GameManager), "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(value, null) || object.ReferenceEquals(tick, null))
			{
				return 0;
			}
			string owner = Owners.ForeignAny(value);
			if (owner != null)
			{
				N.ULog.Warn(Tag, "IdleSpeed not installed: AgentModel.GetMovementValue is already patched by " + owner);
				return 0;
			}
			MethodBase update = N.PatchUtil.Resolve(Tag, typeof(GameManager), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(update, null))
			{
				return 0;
			}
			_skipped = N.Counters.Get(Tag, "idleSpeedSkipped");
			_real = N.Counters.Get(Tag, "idleSpeedReal");
			N.ResetRegistry.Register(new IdleSpeedResetter());
			if (!N.PatchUtil.Install(harmony, Tag, update, Method("UpdatePrefix"), null) || !N.PatchUtil.Install(harmony, Tag, tick, Method("TickPrefix"), Method("TickPostfix")) || !N.PatchUtil.Install(harmony, Tag, value, Method("Prefix"), Method("Postfix")))
			{
				return 0;
			}
			N.ULog.Info(Tag, "IdleSpeed installed");
			return 1;
		}

		private static HarmonyMethod Method(string name)
		{
			return new HarmonyMethod(typeof(IdleSpeed).GetMethod(name, BindingFlags.Public | BindingFlags.Static));
		}

		public static void TickPrefix()
		{
			_inTick = true;
		}

		public static void TickPostfix()
		{
			_inTick = false;
		}

		// A Postfix does not run when the tick throws; Update also runs while paused.
		public static void UpdatePrefix()
		{
			_inTick = false;
		}

		// ProcessMoveByDistance reads the distance only in state MOVE, so a standing agent's tick does not need the value.
		// Outside the tick (CommandWindow) and for a moving agent the original runs. __state: true = store the result.
		public static bool Prefix(AgentModel __instance, ref float __result, out bool __state)
		{
			__state = false;
			if (!_inTick)
			{
				return true;
			}
			try
			{
				float last;
				if (!__instance.GetMovableNode().IsMoving() && _last.TryGetValue(__instance, out last))
				{
					_skipped.Value++;
					__result = last;
					return false;
				}
				_real.Value++;
				__state = true;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(Tag, "IdleSpeed prefix failed, original used (logged once): " + e);
				}
			}
			return true;
		}

		public static void Postfix(AgentModel __instance, float __result, bool __state)
		{
			if (__state)
			{
				_last[__instance] = __result;
			}
		}

		internal static void Clear()
		{
			_last.Clear();
			_inTick = false;
		}
	}

	internal sealed class IdleSpeedResetter : N.IResettable
	{
		public void ResetState()
		{
			IdleSpeed.Clear();
		}
	}
}
