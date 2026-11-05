// FastFix: NailOptimization module (api level 9). Targeted fixes for known defects of other mods, plus an optional guard
// that skips a foreign Harmony patch method which keeps throwing the same exception.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine.
// Output goes to <NailOptimization>/modules/.
//
// 0Harmony 1.0.9.1 has no Unpatch: a foreign patch method is turned off by patching it with a Prefix that skips it.
//   WhiteRabbit (Celestial Bunny)  WhiteRabbit.Harmony_Patch.Update_Patch skipped: its "normal time" mode throws every frame
//                                  (the connected door is looked up in the wrong passage) and is turned off as a whole.
//   FEW RiskLevel, Elysia RiskLevel  empty abnormality slots get a placeholder creature: the mods' UpdateCheck Postfix threw on them.
//   Starflare (CS)                 CSArmor.CollisionCheck and CSCreature.MakeMovement no longer throw.
//   Black Swan, White fixer (vanilla)  BlackSwanSister/FixerWhite.CanTakeDamage no longer reflect to herself without end.
//   Acacia                         its FreischutzBullet.CollisionCheck Prefix no longer throws for a non-unit collider.
//   Elysia RiskLevel               its CreatureUnit.Update Prefix skips a unit with a null model part instead of throwing.
//   KingOfGreedWeapon Enhance      KingOfGreedWeapon.OnTakeDamage, OnAttackEnd, OnCancelWeapon no longer throw for a wearer
//                                  without a passage.
//   Any mod                        MecanimTranslator.Apply tolerates a clip missing from the skeleton; GetBufDamageMultiplier
//                                  walks copies of the buff lists.
//   A mod's fix option is declared only when that mod is loaded, so the panel shows no fix for an absent mod.
//   Special attack stuck           Special-weapon attack with no Spine track for 1 s ends through WorkerUnit.EndAttack.
//   Train restart (vanilla)        HellTrainDeparture.CollisionCheck does nothing while the game state is STOP.
//   Void Dream (vanilla)           Baku wake/annoy release per worker with a timeout; cry loop goes past downed units.
//   Expand once (vanilla)          ExpandUI.OnClickCancel after Confirm keeps _isLoadNextScene: no second expansion.
//   Select click (vanilla)         UnitMouseEventManager.OnPointerClick: a click with a small move clears the drag flag first.
//   Camera drag (vanilla)          CameraMover.LateUpdate: a right/middle-button drag starts and runs during a Recoil shake.
//   Patch guard (on by default)    FastFix.GuardCount identical exceptions within 10 s from one patch method -> that patch
//                                  method is skipped for the rest of the session (a skipped bool Prefix lets the original run).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using UnityEngine;
using UnityEngine.UI;
using N = NailOptimization;

namespace FastFix
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	// ---------- ForeignLeaks ----------
	// Static dictionaries of other mods keyed by objects of one day keep every earlier day reachable (census r171: +183
	// per day in each LinkEGO mod, +33 GiftStacker, MyFormEmpties damage pairs). At each day start the old entries go:
	// destroyed portraits (LinkEGO, every read is behind ContainsKey), gift spaces of agents no longer in AgentManager
	// (GiftStacker, read only when saving that space), damage pairs with a unit no longer in a manager (MyFormEmpties,
	// a remainder under 20 damage per pair).
	public static class ForeignLeaks
	{
		private static N.Flag _flag;
		private static readonly string[] LinkAsm = new string[] { "heavenLinkEGO", "STARRRLinkEGO", "KimLinkEGO" };

		internal static void Install(N.Flag flag)
		{
			_flag = flag;
			N.Events.On(NoticeName.OnStageStart, Prune, FastFixModule.Tag);
		}

		private static void Prune()
		{
			if (!_flag.On)
			{
				return;
			}
			int link = 0;
			for (int i = 0; i < LinkAsm.Length; i++)
			{
				link += PruneDestroyed(Field(LinkAsm[i], LinkAsm[i] + ".Harmony_Patch", "UIDict_disGifts") as System.Collections.IDictionary);
				link += PruneDestroyedList(Field(LinkAsm[i], LinkAsm[i] + ".Harmony_Patch", "UI_disgifts") as System.Collections.IList);
			}
			string line = "link=" + link;
			List<AgentModel> agents = new List<AgentModel>();
			if (AgentManager.instance != null)
			{
				AddAgents(agents, AgentManager.instance.GetAgentList());
				AddAgents(agents, AgentManager.instance.GetAgentSpareList());
			}
			if (agents.Count == 0)
			{
				Log(line + ", no agents: gifts and damage kept", link);
				return;
			}
			int removed = link;
			System.Collections.IDictionary gifts = Field("UnlimitedEgoGiftsMod", "GiftStacker.GiftStackerData", "SpaceToAgentId") as System.Collections.IDictionary;
			if (gifts != null && gifts.Count > 0)
			{
				Dictionary<object, bool> live = new Dictionary<object, bool>();
				for (int i = 0; i < agents.Count; i++)
				{
					AgentModel a = agents[i];
					if (a.Equipment != null && a.Equipment.gifts != null)
					{
						live[a.Equipment.gifts] = true;
					}
				}
				int n = PruneNotLive(gifts, live, null, null);
				removed += n;
				line += " gifts=" + n + "/kept " + gifts.Count;
			}
			System.Collections.IDictionary damage = Field("MyFormEmpties", "MyFormEmpties.Harmony_Patch", "damageRecords") as System.Collections.IDictionary;
			if (damage != null && damage.Count > 0)
			{
				Dictionary<object, bool> live = new Dictionary<object, bool>();
				for (int i = 0; i < agents.Count; i++)
				{
					live[agents[i]] = true;
				}
				if (CreatureManager.instance != null)
				{
					CreatureModel[] cs = CreatureManager.instance.GetCreatureList();
					for (int i = 0; cs != null && i < cs.Length; i++)
					{
						if (cs[i] != null)
						{
							live[cs[i]] = true;
						}
					}
				}
				if (OfficerManager.instance != null)
				{
					IList<OfficerModel> os = OfficerManager.instance.GetOfficerList();
					for (int i = 0; os != null && i < os.Count; i++)
					{
						if (os[i] != null)
						{
							live[os[i]] = true;
						}
					}
				}
				int n = PruneNotLive(damage, live, "from", "to");
				removed += n;
				line += " damage=" + n + "/kept " + damage.Count;
			}
			Log(line, removed);
		}

		private static void AddAgents(List<AgentModel> to, IList<AgentModel> from)
		{
			for (int i = 0; from != null && i < from.Count; i++)
			{
				if (from[i] != null)
				{
					to.Add(from[i]);
				}
			}
		}

		// One line per day start when one of the mods is loaded.
		private static void Log(string line, int removed)
		{
			if (removed > 0 || line.IndexOf('/') >= 0)
			{
				N.ULog.Info(FastFixModule.Tag, "ForeignLeaks: removed " + removed + " (" + line + ")");
			}
		}

		// Elements that are destroyed Unity objects.
		private static int PruneDestroyedList(System.Collections.IList l)
		{
			if (l == null)
			{
				return 0;
			}
			int n = 0;
			for (int i = l.Count - 1; i >= 0; i--)
			{
				UnityEngine.Object u = l[i] as UnityEngine.Object;
				if (!object.ReferenceEquals(u, null) && u == null)
				{
					l.RemoveAt(i);
					n++;
				}
			}
			return n;
		}

		private static object Field(string asmName, string typeName, string field)
		{
			Assembly asm = N.ModHooks.Find(asmName);
			if (object.ReferenceEquals(asm, null))
			{
				return null;
			}
			try
			{
				Type t = asm.GetType(typeName, false);
				if (object.ReferenceEquals(t, null))
				{
					return null;
				}
				FieldInfo f = t.GetField(field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (object.ReferenceEquals(f, null))
				{
					return null;
				}
				return f.GetValue(null);
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "ForeignLeaks: " + typeName + "." + field + " unreadable: " + e.Message);
				return null;
			}
		}

		// Keys that are destroyed Unity objects.
		private static int PruneDestroyed(System.Collections.IDictionary d)
		{
			if (d == null || d.Count == 0)
			{
				return 0;
			}
			List<object> dead = new List<object>();
			foreach (object k in d.Keys)
			{
				UnityEngine.Object u = k as UnityEngine.Object;
				if (!object.ReferenceEquals(u, null) && u == null)
				{
					dead.Add(k);
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				d.Remove(dead[i]);
			}
			return dead.Count;
		}

		// Keys not in 'live' or, with field names, keys whose two unit fields are not both in 'live'.
		private static int PruneNotLive(System.Collections.IDictionary d, Dictionary<object, bool> live, string fieldA, string fieldB)
		{
			List<object> dead = new List<object>();
			FieldInfo fa = null;
			FieldInfo fb = null;
			foreach (object k in d.Keys)
			{
				if (k == null)
				{
					continue;
				}
				if (fieldA == null)
				{
					if (!live.ContainsKey(k))
					{
						dead.Add(k);
					}
					continue;
				}
				if (object.ReferenceEquals(fa, null))
				{
					fa = k.GetType().GetField(fieldA, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					fb = k.GetType().GetField(fieldB, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (object.ReferenceEquals(fa, null) || object.ReferenceEquals(fb, null))
					{
						N.ULog.Warn(FastFixModule.Tag, "ForeignLeaks: key type " + k.GetType().FullName + " has no " + fieldA + "/" + fieldB);
						return 0;
					}
				}
				object a = fa.GetValue(k);
				object b = fb.GetValue(k);
				if (a == null || b == null || !live.ContainsKey(a) || !live.ContainsKey(b))
				{
					dead.Add(k);
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				d.Remove(dead[i]);
			}
			return dead.Count;
		}
	}

	// ---------- NoticeLeaks ----------
	// Notice.Observe keeps an observer until Remove and adds duplicates (census r175/r176: +12 AutoTimer entries,
	// RedShoesSkill, YoungPrince per day). MissionUI.Init observes its one timer again each day, so after N days the
	// timer and OncePerSecond run N times per tick; Mhz_1_76 makes three new timers per day and only stops the old
	// ones; RedShoesSkill (ctor) and YoungPrince (OnStageStart) never remove themselves and keep the old CreatureModel.
	// AutoTimer.Destroy is only GC.Collect and SnowQueen restarts a timer after it, so it cannot unsubscribe.
	// Fix: AutoTimer.Init observes once (the isInitialized guard UnitModel.OnStun already uses); at each day start
	// observers bound to an old day go: destroyed Unity objects, creature scripts and skills whose plain CreatureModel
	// is not in CreatureManager, timers and callbacks that call one of those, and stopped timers that Mhz_1_76 or
	// SnowQueen no longer hold in a field (both replace a timer by a new one; other owners may keep timers in lists).
	public static class NoticeLeaks
	{
		private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		private static N.Flag _flag;
		private static FieldInfo _lists;
		private static FieldInfo _del1;
		private static FieldInfo _update;
		private static FieldInfo _callback;
		private static Type _callbackType;
		private static readonly Dictionary<Type, FieldInfo[]> TimerFields = new Dictionary<Type, FieldInfo[]>();
		private static readonly Dictionary<Type, bool> Failed = new Dictionary<Type, bool>();

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase init = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AutoTimer), "Init", Type.EmptyTypes);
			if (!object.ReferenceEquals(init, null))
			{
				N.PatchUtil.Install(harmony, FastFixModule.Tag, init, new HarmonyMethod(typeof(NoticeLeaks).GetMethod("InitPrefix", BindingFlags.Public | BindingFlags.Static)), null);
			}
			_lists = typeof(Notice).GetField("noticeList", Inst);
			_del1 = typeof(AutoTimer).GetField("del1", Inst);
			_update = typeof(AutoTimer).GetField("updateCalled", Inst);
			_callbackType = typeof(Notice).GetNestedType("CallbackObserver", BindingFlags.Public | BindingFlags.NonPublic);
			if (!object.ReferenceEquals(_callbackType, null))
			{
				_callback = _callbackType.GetField("callback", Inst);
			}
			if (object.ReferenceEquals(_lists, null) || object.ReferenceEquals(_del1, null) || object.ReferenceEquals(_update, null) || object.ReferenceEquals(_callback, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "NoticeLeaks: Notice or AutoTimer fields not found, day start sweep off");
				return;
			}
			N.Events.On(NoticeName.OnStageStart, Sweep, FastFixModule.Tag);
		}

		public static bool InitPrefix(AutoTimer __instance)
		{
			return !(__instance.isInitialized && _flag.On);
		}

		private static void Sweep()
		{
			if (!_flag.On || Notice.instance == null || CreatureManager.instance == null)
			{
				return;
			}
			Dictionary<string, List<IObserver>> lists = _lists.GetValue(Notice.instance) as Dictionary<string, List<IObserver>>;
			CreatureModel[] cs = CreatureManager.instance.GetCreatureList();
			if (lists == null || cs == null || cs.Length == 0)
			{
				return;
			}
			Dictionary<object, bool> live = new Dictionary<object, bool>();
			for (int i = 0; i < cs.Length; i++)
			{
				if (cs[i] != null)
				{
					live[cs[i]] = true;
				}
			}
			int[] removed = new int[4];
			foreach (KeyValuePair<string, List<IObserver>> kv in lists)
			{
				List<IObserver> l = kv.Value;
				for (int i = l == null ? -1 : l.Count - 1; i >= 0; i--)
				{
					int kind = SafeStale(l[i], live);
					if (kind == 0)
					{
						continue;
					}
					AutoTimer t = l[i] as AutoTimer;
					if (t != null)
					{
						t.isInitialized = false;
					}
					l.RemoveAt(i);
					removed[kind]++;
				}
			}
			int n = removed[1] + removed[2] + removed[3];
			if (n > 0)
			{
				N.ULog.Info(FastFixModule.Tag, "NoticeLeaks: removed " + n + " entries (destroyed=" + removed[1] + " creatures=" + removed[2] + " timers=" + removed[3] + ")");
			}
		}

		// One failing observer type is logged once and kept; the sweep goes on.
		private static int SafeStale(IObserver o, Dictionary<object, bool> live)
		{
			try
			{
				return Stale(o, live);
			}
			catch (Exception e)
			{
				Type t = o.GetType();
				if (!Failed.ContainsKey(t))
				{
					Failed[t] = true;
					N.ULog.Warn(FastFixModule.Tag, "NoticeLeaks: observer " + t.FullName + " skipped: " + e.Message);
				}
				return 0;
			}
		}

		// 0 live, 1 destroyed Unity object, 2 creature script or skill of an old day, 3 timer or callback of an old day.
		private static int Stale(IObserver o, Dictionary<object, bool> live)
		{
			if (o == null)
			{
				return 0;
			}
			if (Dead(o, live))
			{
				return o is UnityEngine.Object ? 1 : 2;
			}
			AutoTimer t = o as AutoTimer;
			if (t != null)
			{
				Delegate d = _del1.GetValue(t) as Delegate;
				if (d == null)
				{
					d = _update.GetValue(t) as Delegate;
				}
				if (d == null || d.Target == null)
				{
					return 0;
				}
				if (Dead(d.Target, live) || (!t.started && (d.Target is Mhz_1_76 || d.Target is SnowQueen) && !Holds(d.Target, t)))
				{
					return 3;
				}
				return 0;
			}
			if (object.ReferenceEquals(o.GetType(), _callbackType))
			{
				Delegate d = _callback.GetValue(o) as Delegate;
				if (d != null && d.Target != null && Dead(d.Target, live))
				{
					return 3;
				}
			}
			return 0;
		}

		// A destroyed Unity object, or a creature script or skill whose plain CreatureModel left CreatureManager
		// (child and ordeal models are not in that list and are kept).
		private static bool Dead(object x, Dictionary<object, bool> live)
		{
			UnityEngine.Object u = x as UnityEngine.Object;
			if (!object.ReferenceEquals(u, null))
			{
				return u == null;
			}
			CreatureModel m = null;
			CreatureBase b = x as CreatureBase;
			if (b != null)
			{
				m = b.model;
			}
			else
			{
				CreatureSpecialSkill k = x as CreatureSpecialSkill;
				if (k != null)
				{
					m = k.model;
				}
			}
			return m != null && object.ReferenceEquals(m.GetType(), typeof(CreatureModel)) && !live.ContainsKey(m);
		}

		// The owner still holds the timer in one of its AutoTimer fields (no such field: kept).
		private static bool Holds(object owner, AutoTimer t)
		{
			Type type = owner.GetType();
			FieldInfo[] fs;
			if (!TimerFields.TryGetValue(type, out fs))
			{
				List<FieldInfo> list = new List<FieldInfo>();
				for (Type x = type; !object.ReferenceEquals(x, null) && !object.ReferenceEquals(x, typeof(object)); x = x.BaseType)
				{
					FieldInfo[] all = x.GetFields(Inst | BindingFlags.DeclaredOnly);
					for (int i = 0; i < all.Length; i++)
					{
						if (typeof(AutoTimer).IsAssignableFrom(all[i].FieldType))
						{
							list.Add(all[i]);
						}
					}
				}
				fs = list.ToArray();
				TimerFields[type] = fs;
			}
			for (int i = 0; i < fs.Length; i++)
			{
				if (object.ReferenceEquals(fs[i].GetValue(owner), t))
				{
					return true;
				}
			}
			return fs.Length == 0;
		}
	}

	public class FastFixModule : N.ModuleBase
	{
		public const string Id = "FastFix";
		public const string HarmonyId = "Lobotomy.NailOptimization.FastFix";
		internal const string Tag = "fastfix";

		private static N.Flag _enabled;
		private static N.Flag _guard;
		private static N.IntParam _guardCount;

		internal static HarmonyInstance Harmony;

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
				return "1.26";
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

		public override void Init()
		{
			_enabled = N.Config.BindFlag("FastFix.Enabled", true, "Fixes for known defects of other mods and the optional patch guard. Read at game start.");
			if (!_enabled.On)
			{
				// The other keys stay undeclared: the panel's "Mod fixes" section is empty and hidden; cfg values are kept.
				N.ULog.Info(Tag, "disabled by FastFix.Enabled=false");
				return;
			}
			N.Flag standingCg = N.Config.BindFlag("FastFix.StandingCG", true, "Story character pictures (StoryStandingCGUI) with two face images of the same name no longer throw; the first one is used. Pictures without duplicates load as in the game. Read at game start.");
			N.Flag mecanim = N.Config.BindFlag("FastFix.MecanimMissing", true, "A mod animator clip missing from its skeleton is skipped instead of throwing on each frame. Read at game start.");
			N.Flag execOrder = N.Config.BindFlag("FastFix.ExecOrderAttr", true, "A mod class with an attribute missing from this game no longer logs an exception when first added to an object. Read at game start.");
			N.Flag bufSnapshot = N.Config.BindFlag("FastFix.BufSnapshot", true, "A buff that adds or removes buffs while damage is counted no longer loses the hit. Read at game start.");
			N.Flag splashArmor = N.Config.BindFlag("FastFix.WeaponSplashArmor", true, "A creature's splash or piercing attack no longer stops at the first target it damages (the game reads the attacker's missing armor). Read at game start.");
			N.Flag persephoneGifts = N.Config.BindFlag("FastFix.PersephoneGifts", true, "EGO gifts of an agent in the full Persephone set disappear with the body instead of staying in the air.");
			N.Flag harmonyLabel = N.Config.BindFlag("FastFix.HarmonyLabel", true, "The Harmony weapon's note staff is hidden while its agent is dead instead of staying over the body.");
			StartTooltip.Flag = N.Config.BindFlag("FastFix.StartTooltip", true, "A tooltip whose object was switched off, as at the start of the day, is closed instead of staying on screen.");
			N.Flag childBufs = N.Config.BindFlag("FastFix.ChildBufs", true, "Mod effects on a killed child creature are removed instead of staying on the empty spot.");
			_guard = N.Config.BindFlag("FastFix.PatchGuard", true, "A patch method of another mod that throws the same exception FastFix.GuardCount times within 10 seconds is skipped until the game is closed. Read at game start.");
			_guardCount = N.Config.BindInt("FastFix.GuardCount", 100, 10, 1000, 10, "Identical exceptions within 10 seconds before the patch guard skips that patch method.");
			Harmony = HarmonyInstance.Create(HarmonyId);
			Guard.Count = _guardCount;
			FastFixDriver.WhiteRabbitPending = true;
			FastFixDriver.RiskPortraitPending = true;
			FastFixDriver.StarflarePending = true;
			FastFixDriver.HellTrainPending = true;
			FastFixDriver.EffectNamesPending = true;
			FastFixDriver.BambooPending = true;
			FastFixDriver.CauldronPending = true;
			FastFixDriver.ChaofengPending = true;
			FastFixDriver.ColoredFixerPending = true;
			FastFixDriver.BossBirdPending = true;
			FastFixDriver.AddRoomPending = true;
			FastFixDriver.PinelPending = true;
			FastFixDriver.KingGreedPending = true;
			FastFixDriver.ElysiaUnitPending = true;
			FastFixDriver.AcaciaPending = true;
			PanicStuck.Install(Harmony, N.Config.BindFlag("FastFix.PanicStuck", true, "An agent in the panic that opens containment units walks again when the walk to the unit was interrupted, and picks another unit if it cannot get there."));
			TrainRestart.Install(Harmony, N.Config.BindFlag("FastFix.TrainRestart", true, "Restarting the day while Express Train to Hell departs no longer kills employees of the reloaded day and loses their EGO."));
			OrdealReset.Install(Harmony, N.Config.BindFlag("FastFix.OrdealReset", true, "An ordeal still running when the day ends (retry, return to title) no longer keeps running and ending in the next day."));
			UnknownEquipCount.Install(Harmony, N.Config.BindFlag("FastFix.UnknownEquipCount", true, "A mod that tries to grant equipment no mod defines gets 'nothing to create' instead of an error that stops the save load (Sephirah data)."));
			CensoredChildren.Install(N.Config.BindFlag("FastFix.CensoredChildren", true, "CENSORED Remake (kygi123): summoned children that died are removed 3 s later instead of at the end of the day, so the day no longer slows down with every summon."));
			SpecialAttackStuck.Install(N.Config.BindFlag("FastFix.SpecialAttackStuck", true, "Special EGO weapons with their own Spine attack (kygi123 remakes and similar): an agent whose attack animation never plays no longer stands still for about 100 s and then walks in battle stance; the attack ends after 1 s."));
			VoidDream.Install(Harmony, N.Config.BindFlag("FastFix.VoidDream", true, "Void Dream: each sleeping employee is released by his own wake-up animation, or after 8 s when it never comes, instead of staying frozen and invincible until he dies at day end; the cry hits every unit in the hallway."));
			ExpandOnce.Install(Harmony, N.Config.BindFlag("FastFix.ExpandOnce", true, "Cancel pressed after confirming a department expansion no longer lets the same expansion be confirmed a second time the same day."));
			SelectClick.Install(Harmony, N.Config.BindFlag("FastFix.SelectClick", true, "A click that moved the mouse a little still selects or gives the move order instead of being taken for a drag; a drag cut off by switching windows no longer blocks later clicks."));
			CameraDrag.Install(Harmony, N.Config.BindFlag("FastFix.CameraDrag", true, "A map drag with the right or middle mouse button keeps following the mouse while the camera shakes from hits, instead of freezing and jumping."));
			GameObject go = new GameObject("NailOptimization.FastFix");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<FastFixDriver>();
			if (_guard.On)
			{
				Guard.Start();
			}
			if (standingCg.On)
			{
				StandingCg.Install(Harmony);
			}
			if (mecanim.On)
			{
				MecanimMissing.Install(Harmony);
			}
			StartTooltip.Install(Harmony);
			ChildBufs.Install(Harmony, childBufs);
			ForeignLeaks.Install(N.Config.BindFlag("FastFix.ForeignLeaks", true, "At each day start, entries of earlier days are removed from the static lists of other mods that never release them (LinkEGO gift icons, GiftStacker, MyFormEmpties), so old days do not stay in memory."));
			NoticeLeaks.Install(Harmony, N.Config.BindFlag("FastFix.NoticeLeaks", true, "Game timers and creature scripts of earlier days no longer stay subscribed to game events: the mission panel timer is not added again each day, and at each day start subscribers bound to an old day are removed, so old days do not stay in memory or run every tick."));
			HarmonyLabel.Install(Harmony, harmonyLabel);
			PersephoneGifts.Install(Harmony, persephoneGifts);
			TieIcon.Install(Harmony, N.Config.BindFlag("FastFix.TieIcon", true, "The TieTheTree armor buff icon reuses one image instead of reading it from disk for every buff (a tick lag every 6-7 s)."));
			if (N.Config.BindFlag("FastFix.BlackSwanReflect", true, "Black Swan no longer reflects damage back to herself without end (a stack overflow that ends the creature tick). Read at game start.").On)
			{
				BlackSwanFix.Install(Harmony, typeof(BlackSwanSister), "black swan");
			}
			if (N.Config.BindFlag("FastFix.FixerWhiteReflect", true, "The White fixer no longer reflects damage back to herself or another reflecting unit without end (a stack overflow that froze the game). Read at game start.").On)
			{
				BlackSwanFix.Install(Harmony, typeof(FixerWhite), "fixer white");
			}
			BufSnapshot.CopyBufs = bufSnapshot.On;
			BufSnapshot.SplashArmor = splashArmor.On;
			if (bufSnapshot.On || splashArmor.On)
			{
				BufSnapshot.Install(Harmony);
			}
			if (execOrder.On)
			{
				ExecOrderAttr.Install(Harmony);
			}
		}
	}

	// StoryStandingCGUI.Awake adds every direct child Image to a name-keyed dictionary; a prefab (mod story art) with two
	// children of the same name throws ArgumentException and the picture is left half-built. The Prefix runs the original
	// unless a duplicate exists; then it builds the same dictionary keeping the first image of each name.
	// The original has no exception handler (foreach over an array), GL-53 n/a.
	public static class StandingCg
	{
		private static FieldInfo _faces;
		private static readonly HashSet<string> _reported = new HashSet<string>();
		private static int _errors;

		internal static void Install(HarmonyInstance harmony)
		{
			Type t = N.TypeCache.Resolve("StoryStandingCGUI");
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, t, "Awake", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			_faces = t.GetField("faces", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (object.ReferenceEquals(_faces, null) || !object.ReferenceEquals(_faces.FieldType, typeof(Dictionary<string, Image>)))
			{
				N.ULog.Warn(FastFixModule.Tag, "standingcg not applied: StoryStandingCGUI.faces not found or of another type (GL-10)");
				return;
			}
			bool ok = N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(StandingCg).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
			N.ULog.Info(FastFixModule.Tag, "standingcg fix " + (ok ? "installed" : "failed") + " (StoryStandingCGUI.Awake)");
		}

		public static bool Prefix(StoryStandingCGUI __instance)
		{
			try
			{
				Transform root = __instance.transform;
				int n = root.childCount;
				bool dup = false;
				for (int i = 0; i < n && !dup; i++)
				{
					string a = root.GetChild(i).name;
					for (int j = i + 1; j < n; j++)
					{
						if (a == root.GetChild(j).name)
						{
							dup = true;
							break;
						}
					}
				}
				if (!dup)
				{
					return true;
				}
				Dictionary<string, Image> faces = new Dictionary<string, Image>();
				__instance.imgBody = __instance.GetComponent<Image>();
				Component[] imgs = root.GetComponentsInChildren(typeof(Image), true);
				for (int i = 0; i < imgs.Length; i++)
				{
					Image img = (Image)imgs[i];
					if (img.gameObject.Equals(__instance.gameObject) || !img.transform.parent.Equals(root))
					{
						continue;
					}
					string name = img.gameObject.name;
					if (faces.ContainsKey(name))
					{
						if (_reported.Add(__instance.gameObject.name + "/" + name))
						{
							N.ULog.Warn(FastFixModule.Tag, "standingcg duplicate face '" + name + "' in " + __instance.gameObject.name + ": the first one is used");
						}
						continue;
					}
					faces.Add(name, img);
				}
				_faces.SetValue(__instance, faces);
				return false;
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(FastFixModule.Tag, "standingcg prefix failed, original used (logged once): " + e);
				}
				return true;
			}
		}
	}

	// Deferred work: the WhiteRabbit assembly loads after NailOptimization; guard blocks are applied one frame later.
	public class FastFixDriver : MonoBehaviour
	{
		// Frames to wait for the target mods before giving up (title screen load).
		private const int GiveUpFrames = 600;

		internal static bool WhiteRabbitPending;
		internal static bool RiskPortraitPending;
		internal static bool StarflarePending;
		internal static bool HellTrainPending;
		internal static bool EffectNamesPending;
		internal static bool BambooPending;
		internal static bool CauldronPending;
		internal static bool ChaofengPending;
		internal static bool ColoredFixerPending;
		internal static bool BossBirdPending;
		internal static bool AddRoomPending;
		internal static bool PinelPending;
		internal static bool KingGreedPending;
		internal static bool ElysiaUnitPending;
		internal static bool AcaciaPending;

		// Checks every N frames while waiting: all mod DLLs load in one pass before the first frame.
		private const int PollFrames = 30;

		private int _frames;
		private bool _guardFailed;

		private void Update()
		{
			StartTooltip.Tick();
			VoidDream.Tick();
			SpecialAttackStuck.Tick();
			CensoredChildren.Tick();
			if (WhiteRabbitPending || RiskPortraitPending || StarflarePending || HellTrainPending || EffectNamesPending || BambooPending || CauldronPending || ChaofengPending || ColoredFixerPending || BossBirdPending || AddRoomPending || PinelPending || KingGreedPending || ElysiaUnitPending || AcaciaPending)
			{
				_frames++;
				if (_frames == 1 || _frames % PollFrames == 0 || _frames >= GiveUpFrames)
				{
					try
					{
						if (WhiteRabbitPending && WhiteRabbitFix.TryInstall(_frames >= GiveUpFrames))
						{
							WhiteRabbitPending = false;
						}
					}
					catch (Exception e)
					{
						WhiteRabbitPending = false;
						N.ULog.Error(FastFixModule.Tag, "whiterabbit install failed: " + e);
					}
					try
					{
						if (RiskPortraitPending && RiskPortraitFix.TryInstall(_frames >= GiveUpFrames))
						{
							RiskPortraitPending = false;
						}
					}
					catch (Exception e)
					{
						RiskPortraitPending = false;
						N.ULog.Error(FastFixModule.Tag, "riskportrait install failed: " + e);
					}
					try
					{
						if (StarflarePending && StarflareFix.TryInstall(_frames >= GiveUpFrames))
						{
							StarflarePending = false;
						}
					}
					catch (Exception e)
					{
						StarflarePending = false;
						N.ULog.Error(FastFixModule.Tag, "starflare install failed: " + e);
					}
					try
					{
						if (BambooPending && BambooFix.TryInstall(_frames >= GiveUpFrames))
						{
							BambooPending = false;
						}
					}
					catch (Exception e)
					{
						BambooPending = false;
						N.ULog.Error(FastFixModule.Tag, "bamboo install failed: " + e);
					}
					try
					{
						if (CauldronPending && CauldronFix.TryInstall(_frames >= GiveUpFrames))
						{
							CauldronPending = false;
						}
					}
					catch (Exception e)
					{
						CauldronPending = false;
						N.ULog.Error(FastFixModule.Tag, "cauldron install failed: " + e);
					}
					try
					{
						if (ChaofengPending && ChaofengFix.TryInstall(_frames >= GiveUpFrames))
						{
							ChaofengPending = false;
						}
					}
					catch (Exception e)
					{
						ChaofengPending = false;
						N.ULog.Error(FastFixModule.Tag, "chaofeng install failed: " + e);
					}
					try
					{
						if (ColoredFixerPending && ColoredFixerFix.TryInstall(_frames >= GiveUpFrames))
						{
							ColoredFixerPending = false;
						}
					}
					catch (Exception e)
					{
						ColoredFixerPending = false;
						N.ULog.Error(FastFixModule.Tag, "colored fixer install failed: " + e);
					}
					try
					{
						if (BossBirdPending && BossBirdFix.TryInstall(_frames >= GiveUpFrames))
						{
							BossBirdPending = false;
						}
					}
					catch (Exception e)
					{
						BossBirdPending = false;
						N.ULog.Error(FastFixModule.Tag, "bossbird install failed: " + e);
					}
					try
					{
						if (AddRoomPending && AddRoomFix.TryInstall(_frames >= GiveUpFrames))
						{
							AddRoomPending = false;
						}
					}
					catch (Exception e)
					{
						AddRoomPending = false;
						N.ULog.Error(FastFixModule.Tag, "addroom install failed: " + e);
					}
					try
					{
						if (PinelPending && PinelFix.TryInstall(_frames >= GiveUpFrames))
						{
							PinelPending = false;
						}
					}
					catch (Exception e)
					{
						PinelPending = false;
						N.ULog.Error(FastFixModule.Tag, "pinel install failed: " + e);
					}
					try
					{
						if (KingGreedPending && KingGreedFix.TryInstall(_frames >= GiveUpFrames))
						{
							KingGreedPending = false;
						}
					}
					catch (Exception e)
					{
						KingGreedPending = false;
						N.ULog.Error(FastFixModule.Tag, "king of greed install failed: " + e);
					}
					try
					{
						if (ElysiaUnitPending && ElysiaUnitFix.TryInstall(_frames >= GiveUpFrames))
						{
							ElysiaUnitPending = false;
						}
					}
					catch (Exception e)
					{
						ElysiaUnitPending = false;
						N.ULog.Error(FastFixModule.Tag, "elysia unit install failed: " + e);
					}
					try
					{
						if (AcaciaPending && AcaciaFix.TryInstall(_frames >= GiveUpFrames))
						{
							AcaciaPending = false;
						}
					}
					catch (Exception e)
					{
						AcaciaPending = false;
						N.ULog.Error(FastFixModule.Tag, "acacia install failed: " + e);
					}
					try
					{
						if (HellTrainPending && HellTrainFix.TryInstall(_frames >= GiveUpFrames))
						{
							HellTrainPending = false;
						}
					}
					catch (Exception e)
					{
						HellTrainPending = false;
						N.ULog.Error(FastFixModule.Tag, "hell train install failed: " + e);
					}
					try
					{
						if (EffectNamesPending && EffectNames.TryInstall(_frames >= GiveUpFrames))
						{
							EffectNamesPending = false;
						}
					}
					catch (Exception e)
					{
						EffectNamesPending = false;
						N.ULog.Error(FastFixModule.Tag, "effect names install failed: " + e);
					}
				}
			}
			if (!_guardFailed && Guard.HasQueue)
			{
				try
				{
					Guard.Apply();
				}
				catch (Exception e)
				{
					_guardFailed = true;
					N.ULog.Error(FastFixModule.Tag, "guard apply failed, no further blocks: " + e);
				}
			}
		}
	}

	// ---------- Starflare ----------

	// CSArmor.CollisionCheck inverts its ChildCreatureUnit check, so every note that touches an agent or a passage object
	// throws. The Prefix ends the call for targets without a CreatureUnit: the original deals no damage to them either.
	// CSCreature.MakeMovement and MakeMovementToTarget can run before its animation script is set; the Prefix waits for the next call.
	// A fix made for one build of another mod does not touch a method that has changed: the author may have fixed it, and the fix would hide that.
	internal static class TargetCheck
	{
		internal static bool Same(MethodBase m, uint expected, string label)
		{
			uint actual = HashIl(m);
			if (actual == expected)
			{
				return true;
			}
			// 0: the mod was not in the list when the value was to be recorded, so there is nothing to compare with.
			if (expected == 0u)
			{
				N.ULog.Info(FastFixModule.Tag, label + " IL " + actual.ToString("x8") + ", no value recorded: fix applied");
				return true;
			}
			N.ULog.Warn(FastFixModule.Tag, label + " changed (IL " + actual.ToString("x8") + ", fix made for " + expected.ToString("x8") + "): fix not applied");
			return false;
		}

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
	}

	// Neither original has an exception handler, GL-53 n/a.
	public static class StarflareFix
	{
		private static FieldInfo _anim;
		private static int _errors;

		// True when the attempt is finished (installed, turned off, or given up).
		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("CS");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "starflare not applied: CS not loaded");
				}
				return last;
			}
			if (!N.Config.BindFlag("FastFix.Starflare", true, "Starflare: its suit notes and creature movement no longer throw. Read at game start.").On)
			{
				return true;
			}
			Type armor = a.GetType("CSArmor");
			Type creature = a.GetType("CSCreature");
			if (object.ReferenceEquals(armor, null) || object.ReferenceEquals(creature, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "starflare not applied: types not found");
				return true;
			}
			MethodBase collision = N.PatchUtil.Resolve(FastFixModule.Tag, armor, "CollisionCheck", new Type[] { typeof(Collider2D) });
			MethodBase movement = N.PatchUtil.Resolve(FastFixModule.Tag, creature, "MakeMovement", Type.EmptyTypes);
			MethodBase toTarget = N.PatchUtil.Resolve(FastFixModule.Tag, creature, "MakeMovementToTarget", Type.EmptyTypes);
			_anim = AccessTools.Field(creature, "animscript");
			if (object.ReferenceEquals(collision, null) || object.ReferenceEquals(movement, null) || object.ReferenceEquals(toTarget, null) || object.ReferenceEquals(_anim, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "starflare not applied: members not found");
				return true;
			}
			if (!TargetCheck.Same(collision, 0x74b84902u, "starflare CSArmor.CollisionCheck") | !TargetCheck.Same(movement, 0x048ae1d2u, "starflare CSCreature.MakeMovement") | !TargetCheck.Same(toTarget, 0xdc65ce71u, "starflare CSCreature.MakeMovementToTarget"))
			{
				return true;
			}
			bool ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, collision, new HarmonyMethod(typeof(StarflareFix).GetMethod("CollisionPrefix", BindingFlags.Public | BindingFlags.Static)), null);
			ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, movement, new HarmonyMethod(typeof(StarflareFix).GetMethod("MovementPrefix", BindingFlags.Public | BindingFlags.Static)), null) && ok;
			ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, toTarget, new HarmonyMethod(typeof(StarflareFix).GetMethod("MovementPrefix", BindingFlags.Public | BindingFlags.Static)), null) && ok;
			N.ULog.Info(FastFixModule.Tag, "starflare fix " + (ok ? "installed" : "failed") + " (CSArmor.CollisionCheck, CSCreature.MakeMovement, MakeMovementToTarget)");
			return true;
		}

		public static bool CollisionPrefix(Collider2D collision)
		{
			try
			{
				UnitMouseEventTarget component = collision.GetComponent<UnitMouseEventTarget>();
				if (component == null || component.target == null)
				{
					return true;
				}
				return component.target.GetComponent<CreatureUnit>() != null;
			}
			catch (Exception e)
			{
				Failed(e);
				return true;
			}
		}

		public static bool MovementPrefix(object __instance)
		{
			try
			{
				return _anim.GetValue(__instance) != null;
			}
			catch (Exception e)
			{
				Failed(e);
				return true;
			}
		}

		private static void Failed(Exception e)
		{
			_errors++;
			if (_errors == 1)
			{
				N.ULog.Error(FastFixModule.Tag, "starflare prefix failed (logged once): " + e);
			}
		}
	}

	// ---------- EffectNames ----------

	// Add_On.EffectList is keyed by the CustomEffect folder name, so of two mods with the same folder only the first loads and the
	// other mod shows the first one's picture (or nothing: its animation name is not there). The Transpiler points the later mod's
	// effect lookups at its own copy, loaded under a new key. Callers are found by the name in the IL of that mod's assemblies.
	// Methods with a catch are skipped (GL-53); a finally-only method (foreach) is patched as before and loses its Dispose. A name built at runtime or
	// read from data is not found. Those keep the first mod's effect.
	public static class EffectNames
	{
		// Harmony runs the Transpiler again when anyone patches the method later, so it reads this map and not the current clash.
		private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal);
		private static int _calls;

		// True when the attempt is finished.
		internal static bool TryInstall(bool last)
		{
			Add_On on = Add_On.instance;
			if (on == null || on.DirList == null || on.EffectList == null)
			{
				return last;
			}
			Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.Ordinal);
			for (int d = 0; d < on.DirList.Count; d++)
			{
				string root = on.DirList[d].FullName + "/CustomEffect";
				if (!System.IO.Directory.Exists(root))
				{
					continue;
				}
				string[] effects = System.IO.Directory.GetDirectories(root);
				for (int e = 0; e < effects.Length; e++)
				{
					string name = System.IO.Path.GetFileName(effects[e]);
					if (!seen.ContainsKey(name))
					{
						seen[name] = on.DirList[d].Name;
						continue;
					}
					if (N.Config.BindFlag("FastFix.EffectNames", true, "Two mods with the same effect folder name each show their own effect. Shown only when such mods are loaded. Read at game start.").On)
					{
						Install(name, effects[e], on.DirList[d], seen[name]);
					}
				}
			}
			return true;
		}

		private static void Install(string name, string dir, System.IO.DirectoryInfo folder, string first)
		{
			if (Keys.ContainsKey(name))
			{
				N.ULog.Warn(FastFixModule.Tag, "effect name " + name + " in " + first + " and " + folder.Name + ": a third mod with this name, the last is not loaded");
				return;
			}
			int skipped;
			List<MethodBase> callers = FindCallers(folder.FullName, name, out skipped);
			if (callers.Count == 0)
			{
				N.ULog.Warn(FastFixModule.Tag, "effect name " + name + " in " + first + " and " + folder.Name + ": no lookup found, the second is not loaded; methods with a catch skipped " + skipped);
				return;
			}
			string key = name + "@" + folder.Name;
			try
			{
				Add_On.instance.EffectList[key] = LoadAsset(dir);
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "effect names: " + key + " not loaded: " + e.Message);
				return;
			}
			Keys[name] = key;
			_calls = 0;
			HarmonyMethod transpiler = new HarmonyMethod(typeof(EffectNames).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			for (int i = 0; i < callers.Count; i++)
			{
				try
				{
					FastFixModule.Harmony.Patch(callers[i], null, null, transpiler);
				}
				catch (Exception e)
				{
					N.ULog.Warn(FastFixModule.Tag, "effect names: " + callers[i].DeclaringType + "." + callers[i].Name + ": " + e.Message);
				}
			}
			N.ULog.Info(FastFixModule.Tag, "effect names: " + key + " loaded, calls redirected " + _calls + ", methods with a catch skipped " + skipped);
		}

		private static List<MethodBase> FindCallers(string folder, string name, out int skipped)
		{
			skipped = 0;
			List<MethodBase> found = new List<MethodBase>();
			string root = System.IO.Path.GetFullPath(folder);
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int a = 0; a < all.Length; a++)
			{
				string location;
				try
				{
					location = all[a].Location;
				}
				catch (Exception)
				{
					continue;
				}
				if (string.IsNullOrEmpty(location) || !System.IO.Path.GetFullPath(location).StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				Type[] types;
				try
				{
					types = all[a].GetTypes();
				}
				catch (ReflectionTypeLoadException e)
				{
					types = e.Types;
				}
				const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
				for (int t = 0; t < types.Length; t++)
				{
					if (types[t] == null || types[t].ContainsGenericParameters)
					{
						continue;
					}
					List<MethodBase> methods = new List<MethodBase>();
					try
					{
						methods.AddRange(types[t].GetMethods(Any));
						methods.AddRange(types[t].GetConstructors(Any));
					}
					catch (Exception)
					{
						continue;
					}
					for (int m = 0; m < methods.Count; m++)
					{
						if (methods[m].ContainsGenericParameters)
						{
							continue;
						}
						MethodBody body = methods[m].GetMethodBody();
						if (body == null || !HasLookup(methods[m], body.GetILAsByteArray(), name))
						{
							continue;
						}
						if (HasCatch(body))
						{
							skipped++;
							continue;
						}
						found.Add(methods[m]);
					}
				}
			}
			return found;
		}

		private static bool HasCatch(MethodBody body)
		{
			foreach (ExceptionHandlingClause c in body.ExceptionHandlingClauses)
			{
				if (c.Flags != ExceptionHandlingClauseOptions.Finally)
				{
					return true;
				}
			}
			return false;
		}

		// Looks for ldstr by its opcode and string token mark, then the same call the Transpiler looks for; a false match only makes the Transpiler see the method.
		private static bool HasLookup(MethodBase method, byte[] il, string text)
		{
			for (int i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != 0x72 || il[i + 4] != 0x70)
				{
					continue;
				}
				try
				{
					if (method.Module.ResolveString(BitConverter.ToInt32(il, i + 1)) != text)
					{
						continue;
					}
					int next = i + 5;
					if (next + 9 < il.Length && il[next] == 0x72 && IsCall(il[next + 5]) && IsGetEffect(method.Module.ResolveMethod(BitConverter.ToInt32(il, next + 6))))
					{
						return true;
					}
					// TryGetValue has its out local between the name and the call.
					if (next < il.Length && il[next] == 0x12)
					{
						next += 2;
					}
					if (next + 4 < il.Length && IsCall(il[next]) && IsDictionaryLookup(method.Module.ResolveMethod(BitConverter.ToInt32(il, next + 1))))
					{
						return true;
					}
				}
				catch (Exception)
				{
				}
			}
			return false;
		}

		// Same steps as Add_On.LoadMod.
		private static Spine.Unity.SkeletonDataAsset LoadAsset(string dir)
		{
			List<Texture2D> textures = new List<Texture2D>();
			string[] files = System.IO.Directory.GetFiles(dir, "*.png");
			for (int i = 0; i < files.Length; i++)
			{
				Texture2D tex = new Texture2D(2, 2);
				tex.LoadImage(System.IO.File.ReadAllBytes(files[i]));
				tex.name = System.IO.Path.GetFileNameWithoutExtension(files[i]);
				textures.Add(tex);
			}
			Shader shader = null;
			Spine.Unity.AtlasAsset atlas = Spine.Unity.AtlasAsset.CreateRuntimeInstance(System.IO.File.ReadAllText(dir + "/atlas.txt"), textures.ToArray(), shader, true);
			return Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(System.IO.File.ReadAllText(dir + "/json.txt"), atlas, true, 0.01f);
		}

		private static bool IsCall(byte opcode)
		{
			return opcode == 0x28 || opcode == 0x6F;
		}

		private static bool IsGetEffect(object operand)
		{
			MethodBase m = operand as MethodBase;
			return !object.ReferenceEquals(m, null) && m.Name == "GetEffect";
		}

		private static bool IsDictionaryLookup(object operand)
		{
			MethodBase m = operand as MethodBase;
			if (object.ReferenceEquals(m, null) || (m.Name != "get_Item" && m.Name != "ContainsKey" && m.Name != "TryGetValue"))
			{
				return false;
			}
			Type owner = m.DeclaringType;
			if (object.ReferenceEquals(owner, null) || !owner.IsGenericType)
			{
				return false;
			}
			Type[] args = owner.GetGenericArguments();
			return args.Length == 2 && object.ReferenceEquals(args[1], typeof(Spine.Unity.SkeletonDataAsset));
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i + 1 < list.Count; i++)
			{
				string key;
				if (list[i].opcode != OpCodes.Ldstr || !Keys.TryGetValue((string)list[i].operand, out key))
				{
					continue;
				}
				// GetEffect(effect, animation): only the first string names the effect.
				bool getEffect = i + 2 < list.Count && list[i + 1].opcode == OpCodes.Ldstr && IsGetEffect(list[i + 2].operand);
				int call = list[i + 1].opcode == OpCodes.Ldloca_S || list[i + 1].opcode == OpCodes.Ldloca ? i + 2 : i + 1;
				if (getEffect || (call < list.Count && IsDictionaryLookup(list[call].operand)))
				{
					list[i].operand = key;
					_calls++;
				}
			}
			return list;
		}
	}

	// ---------- ExecOrderAttr ----------

	// On the first AddComponent of a class Unity reads its attributes for DefaultExecutionOrder. A mod built for .NET 4.5 refers to
	// IteratorStateMachineAttribute, missing in this mscorlib, and the read throws (TieTheTree SlashEffect). The Prefix gives the
	// default order 0 for such a class. The original has no exception handler, GL-53 n/a.
	public static class ExecOrderAttr
	{
		private static bool _logged;

		internal static void Install(HarmonyInstance harmony)
		{
			Type t = typeof(GameObject).Assembly.GetType("UnityEngine.AttributeHelperEngine");
			if (object.ReferenceEquals(t, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "exec order fix not applied: AttributeHelperEngine not found");
				return;
			}
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, t, "GetDefaultExecutionOrderFor", new Type[] { typeof(Type) });
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(ExecOrderAttr).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(Type klass, ref int __result)
		{
			try
			{
				klass.GetCustomAttributes(false);
				return true;
			}
			catch (TypeLoadException)
			{
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "exec order: attributes of " + klass.FullName + " unreadable, default order used (logged once)");
				}
				__result = 0;
				return false;
			}
		}
	}

	// ---------- MecanimMissing ----------

	// MecanimTranslator.Apply looks every playing clip up in a table of the skeleton's animations. A mod animator with a clip
	// the skeleton lacks throws on each frame and the skeleton is not posed. The Transpiler swaps the lookup for one that
	// returns an empty animation. Apply has no exception handler, GL-53 n/a.
	public static class MecanimMissing
	{
		private static readonly Spine.Animation Empty = new Spine.Animation("missing", new Spine.ExposedList<Spine.Timeline>(), 0f);
		private static bool _logged;
		private static bool _installLogged;

		internal static void Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(Spine.Unity.SkeletonAnimator.MecanimTranslator), "Apply", new Type[] { typeof(Spine.Skeleton) });
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			try
			{
				harmony.Patch(m, null, null, new HarmonyMethod(typeof(MecanimMissing).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "mecanim patch failed: " + e);
			}
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo get = typeof(MecanimMissing).GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "get_Item" && object.ReferenceEquals(mi.DeclaringType, typeof(Dictionary<int, Spine.Animation>)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = get;
					n++;
				}
			}
			// Harmony runs the Transpiler again on each later patch of this method.
			if (!_installLogged)
			{
				_installLogged = true;
				if (n == 8)
				{
					N.ULog.Info(FastFixModule.Tag, "mecanim fix installed, lookups replaced " + n);
				}
				else
				{
					N.ULog.Warn(FastFixModule.Tag, "mecanim fix: expected 8 lookups, replaced " + n);
				}
			}
			return list;
		}

		public static Spine.Animation Get(Dictionary<int, Spine.Animation> table, int key)
		{
			Spine.Animation a;
			if (table.TryGetValue(key, out a))
			{
				return a;
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "mecanim: animator clip not in its skeleton, skipped (logged once)");
			}
			return Empty;
		}
	}

	// ---------- BufSnapshot ----------

	// UnitModel.GetBufDamageMultiplier walks the buff lists while buffs run their OnTakeDamage; a mod buff that adds or removes
	// a buff there throws and the whole hit is lost. The Transpiler makes both loops walk a copy. The foreach Dispose lost to
	// GL-53 is a collection enumerator, which does nothing; mods patch this method anyway.
	// WeaponModel.OnGiveDamage calls actor.Equipment.armor.script.OnGiveDamageAfter without a null check in its PENETRATION and
	// SPLASH loops; a creature has no armor, so the first splash target that takes damage throws. Vanilla's empty catch hid
	// it, a patched copy drops that catch (GL-53). WeaponTranspiler reads armor.script and calls OnGiveDamageAfter null-safe.
	public static class BufSnapshot
	{
		internal static bool CopyBufs;
		internal static bool SplashArmor;
		private static bool _installLogged;
		private static bool _weaponLogged;

		internal static void Install(HarmonyInstance harmony)
		{
			MethodBase m = CopyBufs ? N.PatchUtil.Resolve(FastFixModule.Tag, typeof(UnitModel), "GetBufDamageMultiplier", new Type[] { typeof(UnitModel), typeof(DamageInfo) }) : null;
			if (!object.ReferenceEquals(m, null))
			{
				try
				{
					harmony.Patch(m, null, null, new HarmonyMethod(typeof(BufSnapshot).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
				}
				catch (Exception e)
				{
					N.ULog.Error(FastFixModule.Tag, "buf snapshot patch failed: " + e);
				}
			}
			// WeaponModel.OnGiveDamage walks the attacker's buffs the same way (TitleBuf4014 adds a buff on a kill).
			MethodBase give = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(WeaponModel), "OnGiveDamage", new Type[] { typeof(UnitModel) });
			if (object.ReferenceEquals(give, null))
			{
				return;
			}
			try
			{
				harmony.Patch(give, null, null, new HarmonyMethod(typeof(BufSnapshot).GetMethod("WeaponTranspiler", BindingFlags.Public | BindingFlags.Static)));
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "buf snapshot patch failed: " + e);
			}
		}

		public static IEnumerable<CodeInstruction> WeaponTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo copy = typeof(BufSnapshot).GetMethod("Copy", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = list.Count - 2; i >= 0 && CopyBufs; i--)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				MethodInfo next = list[i + 1].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "GetUnitBufList" && !object.ReferenceEquals(next, null) && next.Name == "GetEnumerator")
				{
					list.Insert(i + 1, new CodeInstruction(OpCodes.Call, copy));
					n++;
				}
			}
			int scripts = 0;
			int afters = 0;
			if (SplashArmor)
			{
				MethodInfo armorScript = typeof(BufSnapshot).GetMethod("ArmorScript", BindingFlags.Public | BindingFlags.Static);
				MethodInfo safeAfter = typeof(BufSnapshot).GetMethod("SafeAfter", BindingFlags.Public | BindingFlags.Static);
				for (int i = 0; i < list.Count; i++)
				{
					FieldInfo fi = list[i].operand as FieldInfo;
					FieldInfo prev = i > 0 ? list[i - 1].operand as FieldInfo : null;
					MethodInfo mi = list[i].operand as MethodInfo;
					if (list[i].opcode == OpCodes.Ldfld && !object.ReferenceEquals(fi, null) && fi.Name == "script" && list[i - 1].opcode == OpCodes.Ldfld && !object.ReferenceEquals(prev, null) && prev.Name == "armor" && object.ReferenceEquals(prev.DeclaringType, typeof(UnitEquipSpace)))
					{
						list[i].opcode = OpCodes.Call;
						list[i].operand = armorScript;
						scripts++;
					}
					else if (!object.ReferenceEquals(mi, null) && mi.Name == "OnGiveDamageAfter" && object.ReferenceEquals(mi.DeclaringType, typeof(EquipmentScriptBase)))
					{
						list[i].opcode = OpCodes.Call;
						list[i].operand = safeAfter;
						afters++;
					}
				}
			}
			// Harmony runs the Transpiler again on each later patch of this method.
			if (!_weaponLogged)
			{
				_weaponLogged = true;
				if ((CopyBufs && n != 6) || (SplashArmor && (scripts != 10 || afters != 6)))
				{
					N.ULog.Warn(FastFixModule.Tag, "buf snapshot: WeaponModel.OnGiveDamage counts differ from vanilla (6/10/6): " + n + "/" + scripts + "/" + afters);
				}
				else
				{
					N.ULog.Info(FastFixModule.Tag, "buf snapshot: WeaponModel.OnGiveDamage lists copied " + n + ", armor script reads " + scripts + ", OnGiveDamageAfter calls " + afters);
				}
			}
			return list;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo copy = typeof(BufSnapshot).GetMethod("Copy", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = list.Count - 1; i >= 0; i--)
			{
				FieldInfo fi = list[i].operand as FieldInfo;
				MethodInfo mi = list[i].operand as MethodInfo;
				if ((!object.ReferenceEquals(fi, null) && fi.Name == "_bufList") || (!object.ReferenceEquals(mi, null) && mi.Name == "GetUnitBufList"))
				{
					list.Insert(i + 1, new CodeInstruction(OpCodes.Call, copy));
					n++;
				}
			}
			if (!_installLogged)
			{
				_installLogged = true;
				if (n == 2)
				{
					N.ULog.Info(FastFixModule.Tag, "buf snapshot installed, lists copied " + n);
				}
				else
				{
					N.ULog.Warn(FastFixModule.Tag, "buf snapshot: expected 2 lists, copied " + n);
				}
			}
			return list;
		}

		// Stands in for armor.script: null when the unit has no armor.
		public static EquipmentScriptBase ArmorScript(ArmorModel armor)
		{
			return armor == null ? null : armor.script;
		}

		// Stands in for script.OnGiveDamageAfter: skips a missing script (the armor of a creature).
		public static void SafeAfter(EquipmentScriptBase script, UnitModel actor, UnitModel target, DamageInfo dmg)
		{
			if (script != null)
			{
				script.OnGiveDamageAfter(actor, target, dmg);
			}
		}

		public static List<UnitBuf> Copy(List<UnitBuf> list)
		{
			return list == null || list.Count == 0 ? list : new List<UnitBuf>(list);
		}
	}

	// ---------- ChildBufs ----------

	// ChildCreatureModel.Suppressed sets destroied, so OnNotice stops the unit's ticks and its buffs never run again. Mod buffs
	// (My Form Empties Karma, Revival of the Withered Tree BudPoint) remove their icon only from FixedUpdate. Vanilla buffs stay.
	// A killing hit adds its buff after Suppressed (MyFormEmptiesWeaponEffect: TakeDamage, then PassKarma), so AddUnitBuf is covered too.
	public static class ChildBufs
	{
		private static N.Flag _flag;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(ChildCreatureModel), "Suppressed", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			_flag = flag;
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(ChildBufs).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
			MethodBase add = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(UnitModel), "AddUnitBuf", new Type[] { typeof(UnitBuf) });
			if (!object.ReferenceEquals(add, null))
			{
				N.PatchUtil.Install(harmony, FastFixModule.Tag, add, null, new HarmonyMethod(typeof(ChildBufs).GetMethod("AddPostfix", BindingFlags.Public | BindingFlags.Static)));
			}
		}

		public static void Postfix(ChildCreatureModel __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			UnitBuf[] bufs = __instance.GetUnitBufList().ToArray();
			for (int i = 0; i < bufs.Length; i++)
			{
				Remove(bufs[i]);
			}
		}

		public static void AddPostfix(UnitModel __instance, UnitBuf buf)
		{
			ChildCreatureModel child = __instance as ChildCreatureModel;
			if (child != null && child.destroied && _flag.On)
			{
				Remove(buf);
			}
		}

		private static void Remove(UnitBuf buf)
		{
			if (object.ReferenceEquals(buf.GetType().Assembly, typeof(UnitBuf).Assembly))
			{
				return;
			}
			try
			{
				buf.Destroy();
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "child buff " + buf.GetType().Name + " not removed: " + e.Message);
			}
		}
	}

	// ---------- StartTooltip ----------

	// DeployUI switches its canvas off when the day starts, so OnPointerExit never comes and the tooltip stays.
	public static class StartTooltip
	{
		internal static N.Flag Flag;
		private static TooltipMouseOver _source;

		internal static void Install(HarmonyInstance harmony)
		{
			MethodBase target = N.PatchUtil.Resolve("FastFix", typeof(TooltipMouseOver), "SetText", new Type[0]);
			if (!object.ReferenceEquals(target, null))
			{
				N.PatchUtil.Install(harmony, "FastFix", target, new HarmonyMethod(typeof(StartTooltip).GetMethod("PrefixSetText", BindingFlags.Public | BindingFlags.Static)), null);
			}
		}

		public static void PrefixSetText(TooltipMouseOver __instance)
		{
			_source = __instance;
		}

		internal static void Tick()
		{
			if (object.ReferenceEquals(_source, null))
			{
				return;
			}
			OverlayManager overlay = OverlayManager.Instance;
			if (overlay == null || !overlay.IsActivated)
			{
				_source = null;
			}
			else if (Flag != null && Flag.On && (_source == null || !_source.gameObject.activeInHierarchy))
			{
				overlay.ClearOverlay();
				_source = null;
			}
		}
	}

	// ---------- HarmonyLabel ----------

	// YD25_HarmonyWeapon parents harmonyUI to the worker unit at OnStageStart and has no death handling; only its OnStageRelease removes it.
	public static class HarmonyLabel
	{
		private static N.Flag _flag;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase die = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AgentUnit), "OnDie", Type.EmptyTypes);
			MethodBase resurrect = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AgentUnit), "OnResurrect", Type.EmptyTypes);
			if (object.ReferenceEquals(die, null) || object.ReferenceEquals(resurrect, null))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, die, null, new HarmonyMethod(typeof(HarmonyLabel).GetMethod("DiePostfix", BindingFlags.Public | BindingFlags.Static)));
			N.PatchUtil.Install(harmony, FastFixModule.Tag, resurrect, null, new HarmonyMethod(typeof(HarmonyLabel).GetMethod("ResurrectPostfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void DiePostfix(AgentUnit __instance)
		{
			if (_flag.On)
			{
				SetLabel(__instance, false);
			}
		}

		// Not behind the flag: a label hidden before the flag was turned off still has to come back.
		public static void ResurrectPostfix(AgentUnit __instance)
		{
			SetLabel(__instance, true);
		}

		private static void SetLabel(AgentUnit unit, bool on)
		{
			try
			{
				WeaponModel weapon = unit.model.Equipment.weapon;
				if (weapon == null || weapon.script == null || weapon.script.GetType().Name != "YD25_HarmonyWeapon")
				{
					return;
				}
				FieldInfo field = weapon.script.GetType().GetField("harmonyUI", BindingFlags.Instance | BindingFlags.NonPublic);
				GameObject label = field.GetValue(weapon.script) as GameObject;
				if (label != null)
				{
					label.SetActive(on);
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "harmony label not changed: " + e.Message);
			}
		}
	}

	// ---------- TieIcon ----------
	// TieTheTree ShenYuanBuf.Init reads Tie.png from disk and builds a new texture for every buff, about 12 ms, twice every 6-7 s; the texture is never freed.
	// Init has a catch, so it is not patched (GL-53): an empty Harmony_Patch.path makes it return before the icon, and AddUnitBuf's Postfix adds the icon with one shared sprite.
	public static class TieIcon
	{
		private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		private static N.Flag _flag;
		private static FieldInfo _path;
		private static FieldInfo _icon;
		private static MethodInfo _transform;
		private static Sprite _sprite;
		private static string _saved;
		private static bool _failed;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase add = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(UnitModel), "AddUnitBuf", new Type[] { typeof(UnitBuf) });
			if (!object.ReferenceEquals(add, null))
			{
				N.PatchUtil.Install(harmony, FastFixModule.Tag, add, new HarmonyMethod(typeof(TieIcon).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(TieIcon).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
			}
		}

		public static void Prefix(UnitBuf buf, out string __state)
		{
			__state = null;
			// A foreign patch that threw between Prefix and Postfix left the path empty.
			if (_saved != null)
			{
				_path.SetValue(null, _saved);
				_saved = null;
			}
			if (_failed || !_flag.On || buf == null || buf.GetType().Name != "ShenYuanBuf")
			{
				return;
			}
			try
			{
				if (object.ReferenceEquals(_path, null))
				{
					Type patch = buf.GetType().Assembly.GetType("TieTheTree_Mod.Harmony_Patch");
					_path = patch.GetField("path", All);
					_transform = patch.GetMethod("GetTransform", All);
					_icon = buf.GetType().GetField("buf", All);
				}
				string path = (string)_path.GetValue(null);
				if (string.IsNullOrEmpty(path))
				{
					return;
				}
				if (_sprite == null)
				{
					Texture2D texture = new Texture2D(2, 2);
					if (!ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.Combine(path, "Image"), "Tie.png"))))
					{
						_failed = true;
						return;
					}
					_sprite = Sprite.Create(texture, new Rect(0f, 0f, (float)texture.width, (float)texture.height), new Vector2(0.5f, 0.5f));
				}
				_saved = path;
				_path.SetValue(null, null);
				__state = path;
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Warn(FastFixModule.Tag, "TieIcon failed: " + e.Message);
			}
		}

		public static void Postfix(UnitModel __instance, UnitBuf buf, string __state)
		{
			if (__state == null)
			{
				return;
			}
			_path.SetValue(null, __state);
			_saved = null;
			try
			{
				Transform parent = (Transform)_transform.Invoke(null, new object[] { __instance });
				if (parent == null)
				{
					return;
				}
				GameObject icon = new GameObject("tie_icon");
				icon.AddComponent<SpriteRenderer>().sprite = _sprite;
				icon.transform.SetParent(parent);
				icon.transform.localScale = new Vector2(0.3f, 0.3f);
				icon.transform.localPosition = new Vector3(0f, 4f, 0f);
				_icon.SetValue(buf, icon);
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Warn(FastFixModule.Tag, "TieIcon failed: " + e.Message);
			}
		}
	}

	// ---------- PersephoneGifts ----------

	// PersephoneWeapon.HideFaceUpdate (Die animation, hides the gifts) iterates displayedgifts, which only the special attack assigns:
	// an agent that never used it throws there and keeps its gifts. HideFaceUpdate has a foreach (GL-53), so the list is filled at death.
	public static class PersephoneGifts
	{
		private const int FullSetArmor = 8192312;
		private static N.Flag _flag;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase die = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AgentUnit), "OnDie", Type.EmptyTypes);
			if (!object.ReferenceEquals(die, null))
			{
				N.PatchUtil.Install(harmony, FastFixModule.Tag, die, null, new HarmonyMethod(typeof(PersephoneGifts).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
			}
		}

		public static void Postfix(AgentUnit __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				AgentModel agent = __instance.model;
				WeaponModel weapon = agent.Equipment.weapon;
				if (weapon == null || weapon.script == null || weapon.script.GetType().Name != "PersephoneWeapon" || !agent.HasEquipment(FullSetArmor))
				{
					return;
				}
				List<EGOgiftModel> shown = new List<EGOgiftModel>();
				List<EGOgiftModel> gifts = agent.GetAllGifts();
				for (int i = 0; i < gifts.Count; i++)
				{
					if (gifts[i] != null && agent.GetGiftDisplayState(gifts[i]))
					{
						shown.Add(gifts[i]);
					}
				}
				weapon.script.GetType().GetField("displayedgifts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(weapon.script, shown);
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "persephone gifts not set: " + e.Message);
			}
		}
	}

	// ---------- Train Ego: HellTrainGift ----------

	// OnGiveDamage finds a buff by type ADD_SUPERARMOR, casts it to its own debuff and sets RemainTime. A buff of another mod with
	// that type makes the cast null and the hit is lost. Such a target keeps its buff and does not get the debuff.
	public static class HellTrainFix
	{
		private static N.Flag _flag;
		private static Type _debuff;
		private static bool _logged;

		// True when the attempt is finished (installed, turned off, or given up).
		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("TrainEgoFix");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "hell train fix not applied: TrainEgoFix not loaded");
				}
				return last;
			}
			_flag = N.Config.BindFlag("FastFix.HellTrain", true, "Train Ego: a hit with the Hell Train gift on a target with a similar buff of another mod is no longer lost.");
			Type gift = a.GetType("TrainEgo.HellTrainGift");
			_debuff = a.GetType("TrainEgo.HellTrainGift+HellTrainDebuff");
			if (object.ReferenceEquals(gift, null) || object.ReferenceEquals(_debuff, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "hell train fix not applied: types not found");
				return true;
			}
			MethodBase target = N.PatchUtil.Resolve(FastFixModule.Tag, gift, "OnGiveDamage", new Type[] { typeof(UnitModel), typeof(UnitModel), typeof(DamageInfo).MakeByRefType() });
			if (object.ReferenceEquals(target, null))
			{
				return true;
			}
			bool ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, target, new HarmonyMethod(typeof(HellTrainFix).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
			N.ULog.Info(FastFixModule.Tag, "hell train fix " + (ok ? "installed" : "failed") + " (HellTrainGift.OnGiveDamage)");
			return true;
		}

		public static bool Prefix(UnitModel target, ref bool __result)
		{
			if (!_flag.On || target == null)
			{
				return true;
			}
			try
			{
				UnitBuf buf = target.GetUnitBufByType(UnitBufType.ADD_SUPERARMOR);
				if (buf == null || object.ReferenceEquals(buf.GetType(), _debuff))
				{
					return true;
				}
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "hell train: target has foreign buff " + buf.GetType().FullName);
				}
				__result = true;
				return false;
			}
			catch (Exception e)
			{
				if (!_logged)
				{
					_logged = true;
					N.ULog.Error(FastFixModule.Tag, "hell train fix failed (logged once): " + e);
				}
				return true;
			}
		}
	}

	// ---------- FEW RiskLevel, Elysia RiskLevel ----------

	// Their Postfix on CreaturePortrait.UpdateCheck reads creature.metaInfo without the isInit check of the original, so an
	// empty slot throws on every SefiraPanel.Update. The Postfix has a foreach and cannot be patched (GL-53); empty slots get
	// a placeholder creature whose id no level holds, and the Postfix ends at HasCreature. Vanilla and the other known
	// Postfixes gate on isInit, which stays false, or tolerate it (Eight-tailed Fox: script is null). SefiraPanel.Start has
	// no handlers and runs before the first Update.
	public static class RiskPortraitFix
	{
		private static readonly string[] Assemblies = new string[] { "FEW RiskLevel", "Elysia RiskLevel" };
		private static CreatureModel _placeholder;
		private static int _errors;

		// True when the attempt is finished (installed, turned off, or given up).
		internal static bool TryInstall(bool last)
		{
			bool found = false;
			for (int i = 0; i < Assemblies.Length; i++)
			{
				found = found || !object.ReferenceEquals(WhiteRabbitFix.FindAssembly(Assemblies[i]), null);
			}
			if (!found)
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "riskportrait not applied: FEW RiskLevel and Elysia RiskLevel not loaded");
				}
				return last;
			}
			if (!N.Config.BindFlag("FastFix.RiskPortrait", true, "FEW RiskLevel, Elysia RiskLevel: empty abnormality slots of the department panels no longer throw every frame. Read at game start.").On)
			{
				return true;
			}
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(SefiraPanel), "Start", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return true;
			}
			bool ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(RiskPortraitFix).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
			N.ULog.Info(FastFixModule.Tag, "riskportrait fix " + (ok ? "installed" : "failed") + " (SefiraPanel.Start)");
			return true;
		}

		public static void Postfix(SefiraPanel __instance)
		{
			try
			{
				if (_placeholder == null)
				{
					CreatureTypeInfo info = (CreatureTypeInfo)FormatterServices.GetUninitializedObject(typeof(CreatureTypeInfo));
					info.id = long.MinValue;
					_placeholder = (CreatureModel)FormatterServices.GetUninitializedObject(typeof(CreatureModel));
					_placeholder.metaInfo = info;
				}
				SefiraPanel.CreaturePortrait[] slots = __instance.creatureSlots;
				for (int i = 0; i < slots.Length; i++)
				{
					if (!slots[i].isInit && slots[i].creature == null)
					{
						slots[i].creature = _placeholder;
					}
				}
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors == 1)
				{
					N.ULog.Error(FastFixModule.Tag, "riskportrait postfix failed (logged once): " + e);
				}
			}
		}
	}

	// ---------- WhiteRabbit (Celestial Bunny) ----------

	public static class WhiteRabbitFix
	{
		private const string AsmName = "WhiteRabbit";
		private const string TypeName = "WhiteRabbit.Harmony_Patch";
		private const string MethodName = "Update_Patch";

		// True when the attempt is finished (installed, not applicable, or given up).
		internal static bool TryInstall(bool last)
		{
			Assembly asm = FindAssembly(AsmName);
			if (object.ReferenceEquals(asm, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "hook installed 0/0");
				}
				return last;
			}
			if (!N.Config.BindFlag("FastFix.WhiteRabbit", true, "Celestial Bunny (WhiteRabbit): its normal-time mode throws every frame while the abnormality is contained; the mode is turned off. Read at game start.").On)
			{
				return true;
			}
			Type t = asm.GetType(TypeName, false);
			if (object.ReferenceEquals(t, null))
			{
				return NotApplied("type " + TypeName + " not found");
			}
			MethodInfo up = t.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(up, null) || !object.ReferenceEquals(up.ReturnType, typeof(void)))
			{
				return NotApplied("static void " + MethodName + "() not found");
			}
			if (object.ReferenceEquals(t.GetField("changedUnit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), null))
			{
				return NotApplied("field changedUnit not found");
			}
			MethodBase orig = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(PlaySpeedSettingUI), "Update", null);
			if (object.ReferenceEquals(orig, null))
			{
				return NotApplied("PlaySpeedSettingUI.Update not found");
			}
			if (!IsPostfixOf(orig, asm))
			{
				// WhiteRabbit's Harmony_Patch may not have run yet: retry until the last frame. Its constructor catches
				// every error (HarmonyError.txt), so a failed init leaves nothing to fix.
				if (!last)
				{
					return false;
				}
				N.ULog.Info(FastFixModule.Tag, "whiterabbit not applied: " + MethodName + " not registered by WhiteRabbit (its init failed? see BaseMods/HarmonyError.txt)");
				N.ULog.Info(FastFixModule.Tag, "hook installed 0/1");
				return true;
			}
			if (!TargetCheck.Same(up, 0u, "whiterabbit " + MethodName))
			{
				N.ULog.Info(FastFixModule.Tag, "hook installed 0/1");
				return true;
			}
			MethodInfo skip = typeof(WhiteRabbitFix).GetMethod("SkipUpdatePatch", BindingFlags.Public | BindingFlags.Static);
			bool ok = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, up, new HarmonyMethod(skip), null);
			if (ok)
			{
				N.ULog.Info(FastFixModule.Tag, "whiterabbit fix installed (" + TypeName + "." + MethodName + " skipped, normal-time mode off)");
			}
			N.ULog.Info(FastFixModule.Tag, "hook installed " + (ok ? 1 : 0) + "/1");
			return true;
		}

		// Skips WhiteRabbit's whole normal-time mode: SetNormalMode, UpdateNormalMode, ResetAnimatorSpeed and
		// ForcelyResetAnimatorSpeed are called only from Update_Patch, so changedUnit stays empty.
		public static bool SkipUpdatePatch()
		{
			return false;
		}

		private static bool NotApplied(string reason)
		{
			N.ULog.Warn(FastFixModule.Tag, "whiterabbit not applied: " + reason);
			N.ULog.Info(FastFixModule.Tag, "hook installed 0/1");
			return true;
		}

		private static bool IsPostfixOf(MethodBase orig, Assembly asm)
		{
			Patches ps = PatchProcessor.IsPatched(orig);
			if (ps == null)
			{
				return false;
			}
			ReadOnlyCollection<Patch> post = ps.Postfixes;
			for (int i = 0; i < post.Count; i++)
			{
				MethodInfo pm = post[i].patch;
				if (!object.ReferenceEquals(pm, null) && pm.Name == MethodName && !object.ReferenceEquals(pm.DeclaringType, null) && pm.DeclaringType.FullName == TypeName && object.ReferenceEquals(pm.DeclaringType.Assembly, asm))
				{
					return true;
				}
			}
			return false;
		}

		internal static Assembly FindAssembly(string name)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			string prefix = name + ",";
			for (int i = 0; i < all.Length; i++)
			{
				string fn = all[i].FullName;
				if (fn != null && fn.StartsWith(prefix, StringComparison.Ordinal))
				{
					return all[i];
				}
			}
			return null;
		}
	}

	// ---------- Third-party: BambooHattedKim ----------
	// Bodies with try/catch (BuffUtil icons, UniqueProcessMove, KimSkill1, OnReleaseWork) are left alone: GL-53.
	public static class BambooFix
	{
		private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		private const UnitBufType Blossom = (UnitBufType)579002;
		private static int _done;
		private static int _failed;
		private static FieldInfo _tasselBuf;
		private static FieldInfo _isFlying;
		private static FieldInfo _soundPath;
		private static FieldInfo _followTarget;
		private static FieldInfo _followActor;
		private static MethodInfo _setRunning;
		private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
		private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
		private static string _spritePath;

		internal static bool TryInstall(bool last)
		{
			// The assembly of this mod is really named with the extension.
			Assembly a = WhiteRabbitFix.FindAssembly("BambooHattedKim.dll");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "bamboo fix not applied: BambooHattedKim not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.Bamboo", true, "Bamboo-hatted Kim: buffs of child and event creatures tick once, without an exception on expiry; no errors on unequipping Tassel, from destroyed buff stacks, Sinclair, Faust and the guard's target; invulnerability ends with suppression; crit icons and sounds are read from disk once. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type patch = a.GetType("BambooHattedKim.Harmony_Patch");
			Type tassel = a.GetType("BambooHattedKim.EGO.Tassel");
			Type stack = a.GetType("BambooHattedKim.Buff.CreatureModelBuffStack");
			Type sinclair = a.GetType("BambooHattedKim.EGO.BladeLineageSinclair");
			Type faust = a.GetType("BambooHattedKim.EGO.BladeLineageFaust");
			Type kim = a.GetType("BambooHattedKim.BambooHattedKim");
			Type sound = a.GetType("BambooHattedKim.BambooHattedKimSoundEffect");
			Type follow = a.GetType("BambooHattedKim.FollowOwnerCommand");
			if (object.ReferenceEquals(patch, null) || object.ReferenceEquals(tassel, null) || object.ReferenceEquals(stack, null) || object.ReferenceEquals(sinclair, null) || object.ReferenceEquals(faust, null) || object.ReferenceEquals(kim, null) || object.ReferenceEquals(sound, null) || object.ReferenceEquals(follow, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "bamboo fix not applied: types not found");
				return true;
			}
			_tasselBuf = tassel.GetField("tasselBuf", All);
			_isFlying = kim.GetField("isFlying", All);
			_soundPath = sound.GetField("path", All);
			_followTarget = follow.GetField("target", All);
			_setRunning = follow.GetMethod("SetRunningState", All);
			_followActor = follow.GetField("actor", All);
			Prefix(patch.GetMethod("EventCreatureModel_OnFixedUpdate_Postfix", All), "SkipTick");
			Prefix(patch.GetMethod("ChildCreatureModel_OnFixedUpdate_Postfix", All), "SkipTick");
			Prefix(stack.GetMethod("OnNotice", All), "StackNotice");
			Prefix(sinclair.GetMethod("OnNotice", All), "SinclairNotice");
			Prefix(faust.GetMethod("OnNotice", All), "FaustNotice");
			if (!object.ReferenceEquals(_followTarget, null) && !object.ReferenceEquals(_followActor, null) && !object.ReferenceEquals(_setRunning, null))
			{
				Prefix(follow.GetMethod("SetTarget", All), "SetTarget");
			}
			if (!object.ReferenceEquals(_soundPath, null))
			{
				Prefix(sound.GetMethod("MakeSoundEffect", All, null, new Type[] { typeof(string), typeof(Transform), typeof(float), typeof(float), typeof(bool) }, null), "SoundAtParent");
				Prefix(sound.GetMethod("MakeSoundEffect", All, null, new Type[] { typeof(string), typeof(Vector3), typeof(float), typeof(float), typeof(bool) }, null), "SoundAtPosition");
			}
			Install(kim.GetMethod("OnAfterSuppressed", All), null, "KimSuppressed", null);
			Install(tassel.GetMethod("OnRelease", All), "TasselRelease", null, "TasselTranspiler");
			Install(patch.GetMethod("UnitModel_MakeDamageEffect_Prefix", All), null, null, "IconTranspiler");
			N.ULog.Info(FastFixModule.Tag, "bamboo fix: " + _done + " patches" + (_failed > 0 ? ", " + _failed + " failed" : string.Empty));
			return true;
		}

		private static void Prefix(MethodInfo target, string name)
		{
			Install(target, name, null, null);
		}

		private static void Install(MethodInfo target, string prefix, string postfix, string transpiler)
		{
			if (object.ReferenceEquals(target, null))
			{
				_failed++;
				N.ULog.Warn(FastFixModule.Tag, "bamboo fix: a target for " + (prefix ?? postfix ?? transpiler) + " not found");
				return;
			}
			try
			{
				FastFixModule.Harmony.Patch(target, Method(prefix), Method(postfix), Method(transpiler));
				_done++;
			}
			catch (Exception e)
			{
				_failed++;
				N.ULog.Warn(FastFixModule.Tag, "bamboo fix: " + target.Name + " failed: " + e.Message);
			}
		}

		private static HarmonyMethod Method(string name)
		{
			return name == null ? null : new HarmonyMethod(typeof(BambooFix).GetMethod(name, BindingFlags.Public | BindingFlags.Static));
		}

		// UnitModel.UpdateBufState already ticks these buffs over a copy of the list.
		public static bool SkipTick()
		{
			return false;
		}

		public static void TasselRelease(EquipmentScriptBase __instance)
		{
			// EquipmentModel.OnRelease clears the owner before it calls the script, so the buff's own model is used.
			UnitBuf buf = object.ReferenceEquals(_tasselBuf, null) ? null : _tasselBuf.GetValue(__instance) as UnitBuf;
			if (buf != null && buf.model != null)
			{
				buf.model.RemoveUnitBuf(buf);
			}
		}

		// The body after base.OnRelease() reads the owner, which is null at this point, and adds the buff instead of removing it.
		public static IEnumerable<CodeInstruction> TasselTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>();
			List<CodeInstruction> source = new List<CodeInstruction>(instructions);
			for (int i = 0; i < source.Count; i++)
			{
				list.Add(source[i]);
				MethodInfo m = source[i].operand as MethodInfo;
				if (!object.ReferenceEquals(m, null) && m.Name == "OnRelease")
				{
					list.Add(new CodeInstruction(OpCodes.Ret));
					return list;
				}
			}
			throw new InvalidOperationException("Tassel.OnRelease: base call not found");
		}

		// The mod's OnDestory typo keeps destroyed stacks observing. Notice.Send iterates a copy, so removal here is safe.
		public static bool StackNotice(MonoBehaviour __instance)
		{
			if (__instance != null)
			{
				return true;
			}
			IObserver observer = (object)__instance as IObserver;
			Notice.instance.Remove("GetNewUnitBuf", observer);
			Notice.instance.Remove("RemoveNewUnitBuf", observer);
			return false;
		}

		public static bool SinclairNotice(EquipmentScriptBase __instance)
		{
			return __instance.model != null && __instance.model.owner != null;
		}

		public static bool FaustNotice(string noticeName, object[] param)
		{
			if (!noticeName.Equals("OnAttackCrit"))
			{
				return true;
			}
			UnitModel target = param[1] as UnitModel;
			return target != null && target.GetUnitBufByType(Blossom) != null;
		}

		// The mod ignores its parameter and reads nearbyUnits[0], which throws on each tick when nobody is near.
		public static bool SetTarget(object __instance, UnitModel target)
		{
			// The owner hitting its own guard passes the guard itself as the target.
			if (object.ReferenceEquals(target, _followActor.GetValue(__instance)))
			{
				return false;
			}
			if (!object.ReferenceEquals(target, _followTarget.GetValue(__instance)))
			{
				_followTarget.SetValue(__instance, target);
				_setRunning.Invoke(__instance, new object[] { true });
			}
			return false;
		}

		public static void KimSuppressed(object __instance)
		{
			if (!object.ReferenceEquals(_isFlying, null))
			{
				_isFlying.SetValue(__instance, false);
			}
		}

		public static bool SoundAtParent(string src, Transform parent, float vol, float maxDistance, bool loop, ref SoundEffectPlayer __result)
		{
			__result = MakeSound(src, parent, Vector3.zero, vol, maxDistance, loop);
			return false;
		}

		public static bool SoundAtPosition(string src, Vector3 position, float vol, float maxDistance, bool loop, ref SoundEffectPlayer __result)
		{
			__result = MakeSound(src, null, position, vol, maxDistance, loop);
			return false;
		}

		private static SoundEffectPlayer MakeSound(string src, Transform parent, Vector3 position, float vol, float maxDistance, bool loop)
		{
			try
			{
				AudioClip clip;
				if (!Clips.TryGetValue(src, out clip) || clip == null)
				{
					WWW www = new WWW("file://" + (string)_soundPath.GetValue(null) + src);
					while (!www.isDone)
					{
					}
					clip = www.GetAudioClip(false, false);
					if (string.IsNullOrEmpty(www.error))
					{
						Clips[src] = clip;
					}
				}
				GameObject go = Prefab.LoadPrefab("SoundEffectPlayer");
				SoundEffectPlayer player = go.GetComponent<SoundEffectPlayer>();
				AudioSource source = go.GetComponent<AudioSource>();
				if (parent != null)
				{
					go.transform.SetParent(parent);
					go.transform.localPosition = Vector3.zero;
				}
				else
				{
					go.transform.position = position;
				}
				source.clip = clip;
				source.volume = vol;
				source.loop = loop;
				source.Play();
				player.src = source;
				player.src.maxDistance = maxDistance;
				player.src.rolloffMode = AudioRolloffMode.Linear;
				player.SetDestroyTime(loop ? float.PositiveInfinity : clip.length);
				return player;
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "bamboo sound " + src + ": " + e.Message);
				return null;
			}
		}

		// Reads the file once per path: ReadAllBytes, LoadImage and Sprite.Create of the crit text go through the helpers below.
		public static IEnumerable<CodeInstruction> IconTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			int replaced = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int k = 0; k < list.Count; k++)
			{
				CodeInstruction i = list[k];
				MethodInfo m = i.operand as MethodInfo;
				if (object.ReferenceEquals(m, null))
				{
					continue;
				}
				string helper = null;
				if (m.Name == "ReadAllBytes")
				{
					helper = "IconRead";
				}
				else if (m.Name == "LoadImage" && m.GetParameters().Length + (m.IsStatic ? 0 : 1) == 2)
				{
					helper = "IconLoad";
				}
				else if (m.Name == "Create" && object.ReferenceEquals(m.DeclaringType, typeof(Sprite)) && m.GetParameters().Length == 3)
				{
					helper = "IconSprite";
				}
				if (helper != null)
				{
					i.opcode = OpCodes.Call;
					i.operand = typeof(BambooFix).GetMethod(helper, BindingFlags.Public | BindingFlags.Static);
					replaced++;
				}
			}
			if (replaced != 6)
			{
				N.ULog.Warn(FastFixModule.Tag, "bamboo fix: crit icon calls replaced " + replaced + ", expected 6");
			}
			return list;
		}

		public static byte[] IconRead(string path)
		{
			_spritePath = path;
			Sprite sprite;
			return Sprites.TryGetValue(path, out sprite) && sprite != null ? null : System.IO.File.ReadAllBytes(path);
		}

		public static bool IconLoad(Texture2D texture, byte[] bytes)
		{
			return bytes == null || texture.LoadImage(bytes);
		}

		public static Sprite IconSprite(Texture2D texture, Rect rect, Vector2 pivot)
		{
			Sprite sprite;
			if (Sprites.TryGetValue(_spritePath, out sprite) && sprite != null)
			{
				UnityEngine.Object.Destroy(texture);
				return sprite;
			}
			sprite = Sprite.Create(texture, rect, pivot);
			Sprites[_spritePath] = sprite;
			return sprite;
		}
	}

	// ---------- Over Armor Twilight, kekesi_AddRoom ----------
	// OverWeapon_BossBird prefixes AgentModel.OnDie and reads animChanger.CurrentData.name and Equipment.armor.metaInfo
	// without null checks; its own catch logs the NullReferenceException on many deaths. The Transpiler swaps the read of
	// invincible for Gate, which skips the body unless that armor is worn. 0Harmony drops the body's catch (GL-53),
	// so Gate runs the body once more through reflection inside its own try/catch.
	public static class BossBirdFix
	{
		private static MethodInfo _target;
		private static AgentModel _inner;
		private static bool _logged;
		private static bool _ran;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("OverWeapon_BossBird");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "bossbird fix not applied: OverWeapon_BossBird not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.OverBossBird", true, "Over Armor Twilight: agent deaths no longer log a NullReferenceException from AgentModel_OnDie. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("OverWeapon_BossBird.Harmony_Patch");
			_target = object.ReferenceEquals(t, null) ? null : t.GetMethod("AgentModel_OnDie", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(_target, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "bossbird fix not applied: members not found");
				return true;
			}
			HarmonyMethod transpiler = new HarmonyMethod(typeof(BossBirdFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			FastFixModule.Harmony.Patch(_target, null, null, transpiler);
			N.ULog.Info(FastFixModule.Tag, "bossbird fix installed");
			return true;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo gate = typeof(BossBirdFix).GetMethod("Gate", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				FieldInfo f = list[i].operand as FieldInfo;
				if (list[i].opcode == OpCodes.Ldfld && !object.ReferenceEquals(f, null) && f.Name == "invincible")
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = gate;
					n++;
				}
			}
			// A patched copy loses the body's catch (GL-53), so an unexpected body is left unpatched: Harmony applies nothing
			// when the Transpiler throws, and the driver logs it.
			if (n != 1)
			{
				throw new InvalidOperationException("bossbird fix: expected 1 invincible read, found " + n);
			}
			return list;
		}

		// Stands in for agent.invincible: true skips the body.
		public static bool Gate(AgentModel agent)
		{
			if (agent.invincible)
			{
				return true;
			}
			if (object.ReferenceEquals(_inner, agent))
			{
				return false;
			}
			AgentUnit unit = agent.GetUnit();
			if (unit == null || unit.animChanger == null || unit.animChanger.CurrentData == null)
			{
				return true;
			}
			UnitEquipSpace eq = agent.Equipment;
			if (eq == null || eq.armor == null || eq.armor.metaInfo == null)
			{
				return true;
			}
			if (unit.animChanger.CurrentData.name != "BossBirdWeapon_OverBossBirdWeapon" || eq.armor.metaInfo.id != 840129)
			{
				return true;
			}
			AgentModel prev = _inner;
			_inner = agent;
			try
			{
				_target.Invoke(null, new object[] { agent });
				if (!_ran)
				{
					_ran = true;
					N.ULog.Info(FastFixModule.Tag, "bossbird fix: body ran for the armor wearer");
				}
			}
			catch (Exception e)
			{
				Debug.LogError(e.InnerException != null ? e.InnerException : e);
			}
			finally
			{
				_inner = prev;
			}
			return true;
		}
	}

	// kekesi_AddRoom SefiraPanel_Init sets the layer of its button to 999; Unity rejects it and logs an error each time.
	// The Transpiler swaps the setter for SetLayer, which keeps the layer when the value is out of range.
	public static class AddRoomFix
	{
		private static bool _logged;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("kekesi_AddRoom");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "addroom fix not applied: kekesi_AddRoom not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.AddRoom", true, "kekesi_AddRoom: the department panel no longer logs an invalid layer error. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("kekesi_AddRoom.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("SefiraPanel_Init", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "addroom fix not applied: members not found");
				return true;
			}
			HarmonyMethod transpiler = new HarmonyMethod(typeof(AddRoomFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			FastFixModule.Harmony.Patch(m, null, null, transpiler);
			N.ULog.Info(FastFixModule.Tag, "addroom fix installed");
			return true;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo set = typeof(AddRoomFix).GetMethod("SetLayer", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "set_layer" && object.ReferenceEquals(mi.DeclaringType, typeof(GameObject)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = set;
					n++;
				}
			}
			if (!_logged)
			{
				_logged = true;
				if (n != 1)
				{
					N.ULog.Warn(FastFixModule.Tag, "addroom fix: expected 1 layer write, replaced " + n);
				}
			}
			return list;
		}

		public static void SetLayer(GameObject go, int layer)
		{
			if (layer >= 0 && layer <= 31)
			{
				go.layer = layer;
			}
		}
	}

	// BlackSwanSister.CanTakeDamage in the Reflect state calls attacker.TakeDamage(model, dmg) unless the attacker stands on
	// her facing side. When the attacker is herself both positions are equal, no side test passes, and she reflects to herself
	// until the stack overflows. The Transpiler routes that one call through Reflect, which skips a reflection to herself and
	// one started while another reflection is still running; CanTakeDamage still returns false (she takes no damage).
	// FixerWhite.CanTakeDamage while praying reflects to any attacker the same way (one TakeDamage call) and gets the same
	// Transpiler. A reflector already reflecting does not reflect again, which ends every loop (two praying fixers, swan and
	// fixer) while a chain through two different reflectors still runs.
	// The originals have no exception handler, GL-53 n/a.
	public static class BlackSwanFix
	{
		private static readonly List<UnitModel> _active = new List<UnitModel>(4);
		private static bool _selfLogged;
		private static bool _nestedLogged;

		internal static void Install(HarmonyInstance harmony, Type type, string name)
		{
			MethodInfo m = type.GetMethod("CanTakeDamage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new Type[] { typeof(UnitModel), typeof(DamageInfo) }, null);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, name + " fix not applied: CanTakeDamage not found");
				return;
			}
			try
			{
				harmony.Patch(m, null, null, new HarmonyMethod(typeof(BlackSwanFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
				N.ULog.Info(FastFixModule.Tag, name + " fix installed");
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, name + " install failed: " + e);
			}
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo reflect = typeof(BlackSwanFix).GetMethod("Reflect", BindingFlags.Public | BindingFlags.Static);
			int n = 0;
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "TakeDamage" && object.ReferenceEquals(mi.DeclaringType, typeof(UnitModel)) && mi.GetParameters().Length == 2)
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = reflect;
					n++;
				}
			}
			// Another body is left unpatched: Harmony applies nothing when the Transpiler throws.
			if (n != 1)
			{
				throw new InvalidOperationException("reflect fix: " + n + " TakeDamage calls in CanTakeDamage, expected 1");
			}
			return list;
		}

		// Same stack as attacker.TakeDamage(model, dmg).
		public static void Reflect(UnitModel attacker, UnitModel self, DamageInfo dmg)
		{
			if (object.ReferenceEquals(attacker, self))
			{
				if (!_selfLogged)
				{
					_selfLogged = true;
					N.ULog.Info(FastFixModule.Tag, "reflect fix: reflection to herself skipped (" + Who(self) + ")");
				}
				return;
			}
			if (_active.Contains(self))
			{
				if (!_nestedLogged)
				{
					_nestedLogged = true;
					N.ULog.Info(FastFixModule.Tag, "reflect fix: repeated reflection skipped (" + Who(self) + ")");
				}
				return;
			}
			_active.Add(self);
			try
			{
				attacker.TakeDamage(self, dmg);
			}
			finally
			{
				_active.RemoveAt(_active.Count - 1);
			}
		}

		private static string Who(UnitModel u)
		{
			CreatureModel cm = u as CreatureModel;
			return cm != null && cm.script != null ? cm.script.GetType().Name : u.GetType().Name;
		}
	}

	// KingOfGreedWeapon Enhance: KingOfGreedWeapon.OnTakeDamage reads movable.currentPassage.GetEnteredTargets() unchecked.
	// Between passages currentPassage is null, the throw leaves WorkerModel.TakeDamage and ends the attacker's damage loop
	// (BlueStar, HellTrain). The Transpiler makes the three calls null-safe: without a passage the damage is left as is.
	// The original has no exception handler, GL-53 n/a.
	public static class KingGreedFix
	{
		private static bool _logged;
		private static bool _enterLogged;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("KingOfGreedWeapon");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "king of greed fix not applied: KingOfGreedWeapon not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.KingOfGreedPassage", true, "KingOfGreedWeapon Enhance: the weapon no longer throws when its wearer is hit between passages (the attack's damage to the others was lost). Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("KingOfGreedWeapon.KingOfGreedWeapon");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("OnTakeDamage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new Type[] { typeof(UnitModel), typeof(DamageInfo).MakeByRefType() }, null);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "king of greed fix not applied: OnTakeDamage not found");
			}
			else
			{
				try
				{
					FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(typeof(KingGreedFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
					N.ULog.Info(FastFixModule.Tag, "king of greed fix installed");
				}
				catch (Exception e)
				{
					N.ULog.Error(FastFixModule.Tag, "king of greed install failed: " + e);
				}
			}
			if (object.ReferenceEquals(t, null))
			{
				return true;
			}
			HarmonyMethod enter = new HarmonyMethod(typeof(KingGreedFix).GetMethod("EnterTranspiler", BindingFlags.Public | BindingFlags.Static));
			MethodInfo end = t.GetMethod("OnAttackEnd", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new Type[] { typeof(UnitModel), typeof(UnitModel) }, null);
			MethodInfo cancel = t.GetMethod("OnCancelWeapon", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new Type[] { typeof(UnitModel) }, null);
			if (object.ReferenceEquals(end, null) || object.ReferenceEquals(cancel, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "king of greed attack end fix not applied: OnAttackEnd/OnCancelWeapon not found");
				return true;
			}
			try
			{
				FastFixModule.Harmony.Patch(end, null, null, enter);
				FastFixModule.Harmony.Patch(cancel, null, null, enter);
				N.ULog.Info(FastFixModule.Tag, "king of greed attack end fix installed");
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "king of greed attack end install failed: " + e);
			}
			return true;
		}

		// OnAttackEnd and OnCancelWeapon end with passage.EnterUnit(movable); the passage is null between passages and the
		// throw left the Spine event drain. The one EnterUnit call becomes Enter, a no-op without a passage.
		// The originals have no exception handler, GL-53 n/a.
		public static IEnumerable<CodeInstruction> EnterTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			int n = 0;
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (!object.ReferenceEquals(mi, null) && mi.Name == "EnterUnit" && object.ReferenceEquals(mi.DeclaringType, typeof(PassageObjectModel)) && mi.GetParameters().Length == 1)
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = typeof(KingGreedFix).GetMethod("Enter", BindingFlags.Public | BindingFlags.Static);
					n++;
				}
			}
			if (n != 1)
			{
				throw new InvalidOperationException("king of greed fix: " + n + " EnterUnit calls, expected 1");
			}
			return list;
		}

		// Same stack as passage.EnterUnit(node).
		public static void Enter(PassageObjectModel p, MovableObjectNode node)
		{
			if (p != null)
			{
				p.EnterUnit(node);
			}
			else if (!_enterLogged)
			{
				_enterLogged = true;
				N.ULog.Info(FastFixModule.Tag, "king of greed fix: attack end without passage, EnterUnit skipped");
			}
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			BindingFlags st = BindingFlags.Public | BindingFlags.Static;
			int passage = 0;
			int targets = 0;
			int contains = 0;
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo mi = list[i].operand as MethodInfo;
				if (object.ReferenceEquals(mi, null))
				{
					continue;
				}
				Type d = mi.DeclaringType;
				if (mi.Name == "get_currentPassage" && object.ReferenceEquals(d, typeof(MovableObjectNode)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = typeof(KingGreedFix).GetMethod("Passage", st);
					passage++;
				}
				else if (mi.Name == "GetEnteredTargets" && mi.GetParameters().Length == 0 && object.ReferenceEquals(d, typeof(PassageObjectModel)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = typeof(KingGreedFix).GetMethod("Targets", st);
					targets++;
				}
				else if (mi.Name == "Contains" && object.ReferenceEquals(d, typeof(ReadOnlyCollection<MovableObjectNode>)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = typeof(KingGreedFix).GetMethod("Contains", st);
					contains++;
				}
			}
			if (passage != 1 || targets != 1 || contains != 1)
			{
				throw new InvalidOperationException("king of greed fix: unexpected body of OnTakeDamage (" + passage + "/" + targets + "/" + contains + ")");
			}
			return list;
		}

		public static PassageObjectModel Passage(MovableObjectNode node)
		{
			PassageObjectModel p = node == null ? null : node.currentPassage;
			if (p == null && !_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "king of greed fix: wearer without passage, damage kept");
			}
			return p;
		}

		public static ReadOnlyCollection<MovableObjectNode> Targets(PassageObjectModel p)
		{
			return p == null ? null : p.GetEnteredTargets();
		}

		// No passage counts as entered: the mod zeroes the damage only for a wearer that left the passage it stands in.
		public static bool Contains(ReadOnlyCollection<MovableObjectNode> list, MovableObjectNode node)
		{
			return list == null || list.Contains(node);
		}
	}

	// Acacia prefixes FreischutzBullet.CollisionCheck with CollisionCheckOverwrite: for a collider without
	// UnitMouseEventTarget or with a null target it sets its result to false but still reads component.target and
	// target.GetComponent. The throw skips CollisionCheck as the mod's false would, so only the exception is lost. The
	// Transpiler replaces the body with Check, the same logic null-safe. The original has no exception handler, GL-53 n/a.
	public static class AcaciaFix
	{
		private const int EgoId = 301103;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("Acacia");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "acacia fix not applied: Acacia not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.AcaciaBullet", true, "Acacia: its Der Freischutz bullet check no longer throws when the bullet touches something that is not a unit. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("Acacia.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CollisionCheckOverwrite", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Collider2D) }, null);
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(bool)))
			{
				N.ULog.Warn(FastFixModule.Tag, "acacia fix not applied: CollisionCheckOverwrite not found");
				return true;
			}
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(typeof(AcaciaFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
			N.ULog.Info(FastFixModule.Tag, "acacia fix installed");
			return true;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			bool target = false;
			bool id = false;
			foreach (CodeInstruction ci in instructions)
			{
				FieldInfo f = ci.operand as FieldInfo;
				if (!object.ReferenceEquals(f, null) && f.Name == "target" && object.ReferenceEquals(f.DeclaringType, typeof(UnitMouseEventTarget)))
				{
					target = true;
				}
				if (ci.opcode == OpCodes.Ldc_I4 && ci.operand is int && (int)ci.operand == EgoId)
				{
					id = true;
				}
			}
			// The new body replaces logic it was written for; another body is left unpatched.
			if (!target || !id)
			{
				throw new InvalidOperationException("acacia fix: unexpected body of CollisionCheckOverwrite");
			}
			List<CodeInstruction> list = new List<CodeInstruction>();
			list.Add(new CodeInstruction(OpCodes.Ldarg_0, null));
			list.Add(new CodeInstruction(OpCodes.Call, typeof(AcaciaFix).GetMethod("Check", BindingFlags.Public | BindingFlags.Static)));
			list.Add(new CodeInstruction(OpCodes.Ret, null));
			return list;
		}

		// Same results as the mod's prefix: false skips CollisionCheck (no unit, a dead agent or one with the EGO 301103).
		public static bool Check(Collider2D collision)
		{
			if (collision == null)
			{
				return false;
			}
			UnitMouseEventTarget component = collision.GetComponent<UnitMouseEventTarget>();
			if (component == null)
			{
				return false;
			}
			MonoBehaviour target = component.target;
			if (target == null)
			{
				return false;
			}
			AgentUnit unit = target.GetComponent<AgentUnit>();
			if (unit != null)
			{
				AgentModel model = unit.model;
				if (model == null || model.IsDead() || model.HasEquipment(EgoId))
				{
					return false;
				}
			}
			return true;
		}
	}

	// Elysia RiskLevel prefixes CreatureUnit.Update and reads model.observeInfo, model.metaInfo and escapeRisk unchecked; a
	// null one throws every frame and the throw skips CreatureUnit.Update for that unit (seen after all agents died). The
	// Transpiler prepends a null check that jumps to the method's own end. The original has no exception handler, GL-53 n/a.
	public static class ElysiaUnitFix
	{
		private static bool _logged;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("Elysia RiskLevel");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "elysia unit fix not applied: Elysia RiskLevel not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.ElysiaRiskUnit", true, "Elysia RiskLevel: its CreatureUnit.Update Prefix no longer throws every frame for a creature with a missing part (the creature's own update was skipped). Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("Elysia_RiskLevel.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CreatureUnit_Update", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(CreatureUnit) }, null);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "elysia unit fix not applied: CreatureUnit_Update not found");
				return true;
			}
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(typeof(ElysiaUnitFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static)));
			N.ULog.Info(FastFixModule.Tag, "elysia unit fix installed");
			return true;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			CodeInstruction last = list.Count > 0 ? list[list.Count - 1] : null;
			int n = 0;
			bool same = last != null && last.opcode == OpCodes.Ret;
			object end = null;
			// Harmony gives every branch its own label; each one of the two ifs must sit on the final ret, and the guard
			// reuses one of them. Another body is left unpatched.
			for (int i = 0; i < list.Count && same; i++)
			{
				if (list[i].opcode.FlowControl == FlowControl.Cond_Branch)
				{
					n++;
					if (!(list[i].operand is Label) || !last.labels.Contains((Label)list[i].operand))
					{
						same = false;
					}
					else if (end == null)
					{
						end = list[i].operand;
					}
				}
			}
			if (n != 2 || !same || end == null)
			{
				throw new InvalidOperationException("elysia unit fix: unexpected body of CreatureUnit_Update (" + n + " branches)");
			}
			list.Insert(0, new CodeInstruction(OpCodes.Brfalse, end));
			list.Insert(0, new CodeInstruction(OpCodes.Call, typeof(ElysiaUnitFix).GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)));
			list.Insert(0, new CodeInstruction(OpCodes.Ldarg_0, null));
			return list;
		}

		public static bool Ok(CreatureUnit unit)
		{
			string miss = null;
			if (object.ReferenceEquals(unit, null))
			{
				miss = "unit";
			}
			else if (object.ReferenceEquals(unit.model, null))
			{
				miss = "model";
			}
			else if (object.ReferenceEquals(unit.model.observeInfo, null))
			{
				miss = "observeInfo";
			}
			else if (object.ReferenceEquals(unit.model.metaInfo, null))
			{
				miss = "metaInfo";
			}
			else if (object.ReferenceEquals(unit.escapeRisk, null))
			{
				miss = "escapeRisk";
			}
			if (miss == null)
			{
				return true;
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "elysia unit fix: unit skipped, null " + miss);
			}
			return false;
		}
	}

	// Pinel Luti's Creature prefixes IsolateRoom.CheckWorkProcessText and reads TargetUnit.model.currentSkill.skillTypeInfo.id
	// inside a try; currentSkill is null while no work runs, so nearly every call of every room throws and the empty catch
	// returns true (about 2 ms per frame and 1 MB/s of garbage on day 49). The Transpiler replaces the whole body with a call
	// to Before, which checks the chain for null and keeps the mod's logic for its work type 100001.
	public static class PinelFix
	{
		private const long WorkId = 100001L;
		private static FieldInfo _timer;
		private static FieldInfo _used;
		private static MethodInfo _process;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("Pinel Luti's Creature");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "pinel fix not applied: Pinel Luti's Creature not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.PinelWorkText", true, "Pinel Luti's Creature: its CheckWorkProcessText prefix no longer throws and catches an exception in every containment unit each tick. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("PinelLuti.TheShellTrumpet.HP.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("IsolateRoom_CheckWorkProcessText_Before", BindingFlags.Public | BindingFlags.Static);
			_process = object.ReferenceEquals(t, null) ? null : t.GetMethod("Process", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(int) }, null);
			_timer = typeof(IsolateRoom).GetField("_workDescTimer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			_used = typeof(IsolateRoom).GetField("_currentUsedProcessText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_process, null) || object.ReferenceEquals(_timer, null) || object.ReferenceEquals(_used, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "pinel fix not applied: members not found");
				return true;
			}
			HarmonyMethod transpiler = new HarmonyMethod(typeof(PinelFix).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
			FastFixModule.Harmony.Patch(m, null, null, transpiler);
			N.ULog.Info(FastFixModule.Tag, "pinel fix installed");
			return true;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			bool id = false;
			bool timer = false;
			foreach (CodeInstruction ci in instructions)
			{
				// A long constant that fits in an int compiles to ldc.i4 + conv.i8.
				if ((ci.opcode == OpCodes.Ldc_I8 && ci.operand is long && (long)ci.operand == WorkId) || (ci.opcode == OpCodes.Ldc_I4 && ci.operand is int && (int)ci.operand == (int)WorkId))
				{
					id = true;
				}
				string str = ci.operand as string;
				if (ci.opcode == OpCodes.Ldstr && str == "_workDescTimer")
				{
					timer = true;
				}
			}
			// The new body replaces logic it was written for; another body is left unpatched (Harmony applies nothing when
			// the Transpiler throws, and the driver logs it).
			if (!id || !timer)
			{
				throw new InvalidOperationException("pinel fix: unexpected body of IsolateRoom_CheckWorkProcessText_Before");
			}
			List<CodeInstruction> list = new List<CodeInstruction>();
			list.Add(new CodeInstruction(OpCodes.Ldarg_0, null));
			list.Add(new CodeInstruction(OpCodes.Call, typeof(PinelFix).GetMethod("Before", BindingFlags.Public | BindingFlags.Static)));
			list.Add(new CodeInstruction(OpCodes.Ret, null));
			return list;
		}

		// Same results as the mod's prefix: true runs the vanilla method, false skips it.
		public static bool Before(IsolateRoom room)
		{
			if (object.ReferenceEquals(room, null) || object.ReferenceEquals(room.TargetUnit, null))
			{
				return true;
			}
			CreatureModel model = room.TargetUnit.model;
			if (model == null || model.currentSkill == null || model.currentSkill.skillTypeInfo == null || model.currentSkill.skillTypeInfo.id != WorkId)
			{
				return true;
			}
			try
			{
				Timer timer = (Timer)_timer.GetValue(room);
				if (timer.started && timer.RunTimer())
				{
					timer.StartTimer(room.WorkDescFreq);
					if (!model.script.IsActivatedWorkDesc())
					{
						return false;
					}
					List<int> used = (List<int>)_used.GetValue(room);
					if (used.Count == 0)
					{
						return false;
					}
					int num = used[UnityEngine.Random.Range(0, used.Count)];
					used.Remove(num);
					room.DescController.Display((string)_process.Invoke(null, new object[] { num }), num);
				}
				return false;
			}
			catch
			{
				// Silent like the mod's own catch: this runs for every room each tick.
			}
			return true;
		}
	}

	// ---------- Third-party: Colored Fixer (kygi123) ----------
	// Several equipment scripts subscribe a handler to the agent's whole Spine AnimationState.Complete (not to the one track
	// entry) and never remove it, so it runs again on every later completed animation of that agent:
	// TheRedMist_EGO_Mimicry.Groggy_loop (after "Groggy_start") also runs on every loop of "Groggy_loop": it restarts the 100 s
	// EGO_Gone timer and calls LoseControl each time, so the agent stays stunned and out of control for good (Kali stuck after
	// manifesting EGO, cannot be ordered). EGODeactivate (after "Groggy_end"), TheRedMist_Mimicry.EGOActivate and
	// RedGaze_Weapon.EGOActivate (after the manifest animation) re-equip fresh EGO gear on later completions, and
	// PurpleTearArmor.TP (after "Teleport") teleports again. Fix: a Prefix removes the handler from the owner's state at its
	// first run, so each subscription runs once, as the scripts evidently intend.
	public static class ColoredFixerFix
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		private static readonly Dictionary<Type, FieldInfo> Owners = new Dictionary<Type, FieldInfo>();
		private static bool _logged;
		private static bool _errorLogged;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("kygi123_Colored_Fixer");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "colored fixer fix not applied: Colored Fixer not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.ColoredFixer", true, "Colored Fixer: an agent no longer stays stunned and out of control after Kali's EGO ends, and EGO change and teleport effects no longer repeat on later animations. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			int done = 0;
			done += Patch(a, "kygi123_Colored_Fixer.TheRedMist_EGO_Mimicry", "Groggy_loop", "GroggyLoop");
			done += Patch(a, "kygi123_Colored_Fixer.TheRedMist_EGO_Mimicry", "EGODeactivate", "EgoDeactivate");
			done += Patch(a, "kygi123_Colored_Fixer.TheRedMist_Mimicry", "EGOActivate", "Activate");
			done += Patch(a, "kygi123_Colored_Fixer.RedGaze_Weapon", "EGOActivate", "Activate");
			done += Patch(a, "kygi123_Colored_Fixer.PurpleTearArmor", "TP", "Teleport");
			N.ULog.Info(FastFixModule.Tag, "colored fixer fix: " + done + " of 5 handlers patched");
			return true;
		}

		// The handler overload with one parameter (the TrackEntry); the parameterless twins are not subscribed.
		private static int Patch(Assembly a, string typeName, string method, string prefix)
		{
			Type t = a.GetType(typeName, false);
			if (object.ReferenceEquals(t, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "colored fixer fix: " + typeName + " not found");
				return 0;
			}
			MethodInfo m = Handler(t, method);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "colored fixer fix: " + typeName + "." + method + " not found");
				return 0;
			}
			HarmonyMethod pre = new HarmonyMethod(typeof(ColoredFixerFix).GetMethod(prefix, BindingFlags.Public | BindingFlags.Static));
			return N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, m, pre, null) ? 1 : 0;
		}

		public static void GroggyLoop(object __instance)
		{
			Leave(__instance, "Groggy_loop");
		}

		public static void EgoDeactivate(object __instance)
		{
			Leave(__instance, "EGODeactivate");
		}

		public static void Activate(object __instance)
		{
			Leave(__instance, "EGOActivate");
		}

		public static void Teleport(object __instance)
		{
			Leave(__instance, "TP");
		}

		// The handler runs as before, once: it leaves the owner's AnimationState.Complete first.
		private static void Leave(object script, string handler)
		{
			try
			{
				WorkerModel worker = Owner(script);
				WorkerUnit unit = worker == null ? null : worker.GetWorkerUnit();
				object state = unit == null || unit.animChanger == null ? null : (object)unit.animChanger.state;
				if (state == null)
				{
					return;
				}
				EventInfo evt = state.GetType().GetEvent("Complete");
				MethodInfo m = Handler(script.GetType(), handler);
				if (object.ReferenceEquals(evt, null) || object.ReferenceEquals(m, null))
				{
					return;
				}
				evt.RemoveEventHandler(state, Delegate.CreateDelegate(evt.EventHandlerType, script, m));
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "colored fixer fix: " + script.GetType().Name + "." + handler + " ran once and left the Complete event");
				}
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "colored fixer fix failed: " + ex);
				}
			}
		}

		// The handler overload with one parameter (the TrackEntry); the parameterless twins are not subscribed.
		private static MethodInfo Handler(Type t, string method)
		{
			MethodInfo[] all = t.GetMethods(Any | BindingFlags.DeclaredOnly);
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].Name == method && all[i].GetParameters().Length == 1)
				{
					return all[i];
				}
			}
			return null;
		}

		// The agent that owns the script: field "agent" (AgentModel) or "worker" (WorkerModel), else the model owner.
		private static WorkerModel Owner(object script)
		{
			Type t = script.GetType();
			FieldInfo f;
			if (!Owners.TryGetValue(t, out f))
			{
				f = t.GetField("agent", Any);
				if (object.ReferenceEquals(f, null) || !typeof(WorkerModel).IsAssignableFrom(f.FieldType))
				{
					f = t.GetField("worker", Any);
				}
				if (!object.ReferenceEquals(f, null) && !typeof(WorkerModel).IsAssignableFrom(f.FieldType))
				{
					f = null;
				}
				Owners[t] = f;
			}
			WorkerModel w = object.ReferenceEquals(f, null) ? null : f.GetValue(script) as WorkerModel;
			if (w == null)
			{
				EquipmentScriptBase es = script as EquipmentScriptBase;
				if (es != null && es.model != null)
				{
					w = es.model.owner as WorkerModel;
				}
			}
			return w;
		}
	}

	// ---------- Third-party: Cauldron ----------
	// CauldronMinAnim sits next to the original SlimeGirlAnim and takes SetScript. Animator events still reach the original,
	// whose script stays null, so they throw before doing anything. They are dropped: the minion runs on its own timers.
	// feng_Buf.MovementScale returns a bonus (+1 or -0.5) while UnitModel.GetMovementScaleByBuf multiplies:
	// a negative scale walks units backwards and MovableObjectNode.ProcessMoveByDistance throws.
	// ChaoFeng.attack loops over the live entered-unit list of a passage; a unit killed by its damage leaves the list
	// and the loop throws, skipping the rest of the tick. The replacement loops over a copy.
	public static class ChaofengFix
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		private static FieldInfo _fengs;
		private static FieldInfo _qiuNiu;
		private static FieldInfo _model;
		private static Type _feng;
		private static bool _failed;
		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("ChaoFeng");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "chaofeng fix not applied: ChaoFeng not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.Chaofeng", true, "Chaofeng: the slowing wind no longer gives a negative movement scale, and its damage no longer throws when it kills a unit. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type buf = a.GetType("ChaoFeng.Feng+feng_Buf");
			MethodInfo m = object.ReferenceEquals(buf, null) ? null : buf.GetMethod("MovementScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "chaofeng fix not applied: members not found");
				return true;
			}
			HarmonyMethod postfix = new HarmonyMethod(typeof(ChaofengFix).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
			bool done = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, m, null, postfix);
			N.ULog.Info(FastFixModule.Tag, "chaofeng fix: " + (done ? "patched" : "failed"));
			Type boss = a.GetType("ChaoFeng.ChaoFeng");
			_feng = a.GetType("ChaoFeng.Feng");
			_model = typeof(CreatureBase).GetField("model", Any);
			MethodInfo attack = null;
			if (!object.ReferenceEquals(boss, null))
			{
				_fengs = boss.GetField("fengs", Any);
				_qiuNiu = boss.GetField("QiuNiu", Any);
				attack = boss.GetMethod("attack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
			}
			if (object.ReferenceEquals(attack, null) || object.ReferenceEquals(_fengs, null) || object.ReferenceEquals(_qiuNiu, null) || object.ReferenceEquals(_model, null) || object.ReferenceEquals(_feng, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "chaofeng attack fix not applied: members not found");
				return true;
			}
			HarmonyMethod prefix = new HarmonyMethod(typeof(ChaofengFix).GetMethod("AttackPrefix", BindingFlags.Public | BindingFlags.Static));
			done = N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, attack, prefix, null);
			N.ULog.Info(FastFixModule.Tag, "chaofeng attack fix: " + (done ? "patched" : "failed"));
			return true;
		}

		// Same damage as the original body, which never runs.
		public static bool AttackPrefix(object __instance)
		{
			bool hit = false;
			try
			{
				List<PassageObjectModel> fengs = (List<PassageObjectModel>)_fengs.GetValue(__instance);
				UnitModel self = (UnitModel)_model.GetValue(__instance);
				bool qiuNiu = (bool)_qiuNiu.GetValue(__instance);
				for (int i = 0; i < fengs.Count; i++)
				{
					PassageObjectModel passage = fengs[i];
					MovableObjectNode[] nodes = passage.GetEnteredTargetsAsArray();
					System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> live = passage.GetEnteredTargets();
					for (int j = 0; j < nodes.Length; j++)
					{
						// A unit that left the passage during this loop was not hit by the original either.
						if (!live.Contains(nodes[j]))
						{
							continue;
						}
						UnitModel unit = nodes[j].GetUnit();
						if (object.ReferenceEquals(unit, self))
						{
							continue;
						}
						CreatureModel creature = unit as CreatureModel;
						if (creature != null && _feng.IsInstanceOfType(creature.script))
						{
							continue;
						}
						hit = true;
						unit.TakeDamage(self, new DamageInfo(unit.defense.B > 0f ? RwbpType.B : RwbpType.N, 3f));
						if (qiuNiu)
						{
							unit.TakeDamage(self, new DamageInfo(RwbpType.W, 3f));
						}
					}
				}
			}
			catch (Exception e)
			{
				if (!_failed)
				{
					_failed = true;
					N.ULog.Warn(FastFixModule.Tag, "chaofeng attack failed: " + e);
				}
				// Before any damage the original may still run; after it, running it would hit units twice.
				return !hit;
			}
			return false;
		}

		public static void Postfix(ref float __result)
		{
			__result = 1f + __result;
		}
	}

	public static class CauldronFix
	{
		private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
		private static FieldInfo _script;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("Cauldron");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "cauldron fix not applied: Cauldron not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.Cauldron", true, "Cauldron: animation events of minions no longer throw in SlimeGirlAnim. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			_script = typeof(SlimeGirlAnim).GetField("script", Declared);
			if (object.ReferenceEquals(_script, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "cauldron fix not applied: members not found");
				return true;
			}
			HarmonyMethod prefix = new HarmonyMethod(typeof(CauldronFix).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static));
			string[] names = new string[] { "OnCalled", "AttackDamageTimeCalled", "SkillFire" };
			int done = 0;
			for (int i = 0; i < names.Length; i++)
			{
				MethodInfo m = typeof(SlimeGirlAnim).GetMethod(names[i], Declared);
				if (!object.ReferenceEquals(m, null) && N.PatchUtil.Install(FastFixModule.Harmony, FastFixModule.Tag, m, prefix, null))
				{
					done++;
				}
			}
			N.ULog.Info(FastFixModule.Tag, "cauldron fix: " + done + " of " + names.Length + " patches");
			return true;
		}

		public static bool Prefix(SlimeGirlAnim __instance)
		{
			return _script.GetValue(__instance) != null;
		}
	}

	// ---------- Panic: open containment units ----------
	// PanicOpenIsolate orders the walk once in UpdateNextTarget; Execute only acts at the entry node, so an interrupted walk
	// leaves the agent standing for the rest of the panic.
	// Right/middle-button map drag (vanilla): CameraMover.Recoil (camera shake on hits) sets movable=false until its coroutine
	// PlayRecoil ends, and LateUpdate returns at once while movable is false, so a drag freezes for every shake and jumps when it
	// ends. PlayRecoil takes at least one frame per shake step, so under heavy lag a shake lasts seconds. In a fight shakes follow
	// each other and the drag responds only in short bursts. Fix: while a PlayRecoil coroutine runs (counted on its MoveNext:
	// first call $PC 0 -> +1, last call returns false -> -1) and the button is held, movable is true for the duration of
	// LateUpdate only, so a drag starts and keeps running. Other locks (Esc menu, observation, tutorial) keep vanilla behavior,
	// as does a drag that started before them. On release during a shake Drag stays set, so PlayRecoil ends at dragedPos.
	public static class CameraDrag
	{
		private static N.Flag _flag;
		private static FieldInfo _movable;
		private static FieldInfo _drag;
		private static FieldInfo _pcField;
		private static bool _forced;
		private static int _pc;
		private static int _active;
		private static CameraMover _cam;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			_movable = typeof(CameraMover).GetField("_movable", Private);
			_drag = typeof(CameraMover).GetField("Drag", Private);
			Type it = typeof(CameraMover).GetNestedType("<PlayRecoil>c__Iterator0", BindingFlags.NonPublic | BindingFlags.Public);
			MethodBase next = null;
			if (!object.ReferenceEquals(it, null))
			{
				_pcField = it.GetField("$PC", Private | BindingFlags.Public);
				next = it.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(CameraMover), "LateUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(_movable, null) || object.ReferenceEquals(_drag, null) || object.ReferenceEquals(_pcField, null) || object.ReferenceEquals(next, null) || object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "camera drag fix not applied: members not found");
				return;
			}
			if (!N.PatchUtil.Install(harmony, FastFixModule.Tag, next, new HarmonyMethod(typeof(CameraDrag).GetMethod("RecoilPrefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(CameraDrag).GetMethod("RecoilPostfix", BindingFlags.Public | BindingFlags.Static))))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(CameraDrag).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(CameraDrag).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		// Runs once per shake step, not per frame.
		public static void RecoilPrefix(object __instance)
		{
			_pc = (int)_pcField.GetValue(__instance);
		}

		public static void RecoilPostfix(bool __result)
		{
			if (_pc == 0)
			{
				_active++;
			}
			if (!__result && _pc != -1 && _active > 0)
			{
				_active--;
			}
		}

		public static void Prefix(CameraMover __instance)
		{
			_forced = false;
			if (!object.ReferenceEquals(__instance, _cam))
			{
				// A new CameraMover: the coroutines of the old one died with it.
				_cam = __instance;
				_active = 0;
			}
			if (!_flag.On || _active <= 0 || __instance.IsMovable() || !(Input.GetMouseButton(1) || Input.GetMouseButton(2)))
			{
				return;
			}
			_movable.SetValue(__instance, true);
			_forced = true;
		}

		public static void Postfix(CameraMover __instance)
		{
			if (_forced)
			{
				_forced = false;
				_movable.SetValue(__instance, false);
			}
		}
	}

	// Selection and move-command clicks (vanilla UnitMouseEventManager): uGUI starts a drag once the pointer moves a few pixels
	// while pressed. A right drag sets _draggingRight, and OnPointerClick_default ignores the right click (the move command)
	// while it is set; the click is sent before OnEndDrag clears it. So a right click with a small hand movement, more likely at
	// low FPS, does nothing. A left click during a left drag only ends in OnEndDrag's area select. When OnEndDrag never comes
	// (focus lost while pressed), the flag stays set and every later right click, or left click select, is ignored until the
	// next drag. Fix: Prefix on OnPointerClick: when the pointer moved less than ClickPx (at 1080p) since the press, the drag
	// flag of that button is cleared (left: CancelDrag), so the click acts as a click. Larger moves keep vanilla behavior.
	public static class SelectClick
	{
		private const float ClickPx = 16f;
		private static N.Flag _flag;
		private static FieldInfo _right;
		private static FieldInfo _left;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			_right = typeof(UnitMouseEventManager).GetField("_draggingRight", Private);
			_left = typeof(UnitMouseEventManager).GetField("_dragging", Private);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(UnitMouseEventManager), "OnPointerClick", new Type[] { typeof(UnityEngine.EventSystems.BaseEventData) });
			if (object.ReferenceEquals(_right, null) || object.ReferenceEquals(_left, null) || object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "select click fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(SelectClick).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static void Prefix(UnitMouseEventManager __instance, UnityEngine.EventSystems.BaseEventData eventData)
		{
			if (!_flag.On)
			{
				return;
			}
			UnityEngine.EventSystems.PointerEventData pe = eventData as UnityEngine.EventSystems.PointerEventData;
			if (pe == null)
			{
				return;
			}
			float limit = ClickPx * Screen.height / 1080f;
			if ((pe.position - pe.pressPosition).sqrMagnitude >= limit * limit)
			{
				return;
			}
			if (pe.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
			{
				if ((bool)_right.GetValue(__instance))
				{
					_right.SetValue(__instance, false);
				}
			}
			else if (pe.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
			{
				if ((bool)_left.GetValue(__instance))
				{
					__instance.CancelDrag();
				}
			}
		}
	}

	// Department expansion twice in one day (vanilla ExpandUI): OnClickConfirm applies the expansion
	// (SefiraManager.OpenSefiraWithCreature) and sets _isLoadNextScene; OnClickCancel during the closing animation sets it back
	// to false, so OnCalled(1) does not load the next scene and Confirm works again: a second expansion. Fix: Cancel keeps
	// _isLoadNextScene once it is true, so a confirmed expansion always goes on to the next scene and cannot repeat.
	public static class ExpandOnce
	{
		private static N.Flag _flag;
		private static FieldInfo _loading;
		private static bool _was;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			_loading = typeof(ExpandUI).GetField("_isLoadNextScene", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(ExpandUI), "OnClickCancel", Type.EmptyTypes);
			if (object.ReferenceEquals(_loading, null) || object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "expansion fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(ExpandOnce).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(ExpandOnce).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Prefix(ExpandUI __instance)
		{
			_was = _flag.On && (bool)_loading.GetValue(__instance);
		}

		public static void Postfix(ExpandUI __instance)
		{
			if (_was)
			{
				_was = false;
				_loading.SetValue(__instance, true);
				N.ULog.Info(FastFixModule.Tag, "expansion: cancel after confirm ignored, the confirmed expansion proceeds");
			}
		}
	}

	// Void Dream (vanilla Baku). A sleeping worker is released by an animation event of his wake-up (1000600) or annoyed
	// (1000601) animation, and Baku.OnWorkerAnimCalled releases the FIRST worker of a shared queue: the event does not say whose
	// animation fired. A missed event (the WakeUp/Annoyed bool set before the sleep animator is in place: a click right after
	// falling asleep, or an annoy at once when the worker had no mental left or was panicking) leaves the worker queued,
	// invincible and uncontrollable until Uncontrollable_Baku.OnStageEnd kills him; an event out of order releases the wrong
	// worker, and an event with an empty queue throws. Fix: the event releases the worker whose AnimatorEventHandler raised it,
	// and a worker still queued TimeoutSec after he was queued is released the same way. Baku.OnCryDamage leaves its target loop
	// at the first null or downed unit (return instead of continue): later units take no damage and the cry effect is not made.
	// Fix: a Prefix on Baku.OnCryDamage runs the same loop with continue (its foreach is over an array: no exception handler,
	// handlers 0 in the live patch report). SleepWorker (try) is not patched.
	public static class VoidDream
	{
		private const int WakeEvent = 1000600;
		private const int AnnoyEvent = 1000601;
		// Scaled seconds from queueing to the forced release (pause and game speed apply).
		private const float TimeoutSec = 8f;

		private sealed class Entry
		{
			public Baku Script;
			public WorkerModel Worker;
			public bool Annoy;
			public float Due;
		}

		private static N.Flag _flag;
		private static FieldInfo _animEvent;
		private static FieldInfo _wakeup;
		private static FieldInfo _annoyed;
		private static MethodInfo _wakedUp;
		private static MethodInfo _annoyedUp;
		private static MethodInfo _makeEffect;
		private static MethodInfo _cryDmg;
		private static readonly List<Entry> _pending = new List<Entry>();
		private static bool _errorLogged;
		private static bool _eventLogged;
		private static bool _ignoredLogged;
		private static bool _cryLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			_animEvent = typeof(AnimatorEventHandler).GetField("_animEvent", Private);
			_wakeup = typeof(Baku).GetField("wakeup", Private);
			_annoyed = typeof(Baku).GetField("annoyed", Private);
			_wakedUp = typeof(Baku).GetMethod("WakedWorkerWakeUp", Private);
			_annoyedUp = typeof(Baku).GetMethod("AnnoyedWorkerWakeUp", Private);
			_makeEffect = typeof(Baku).GetMethod("MakeEffect", Private);
			PropertyInfo dmg = typeof(Baku).GetProperty("cryDmg", BindingFlags.Static | BindingFlags.NonPublic);
			_cryDmg = object.ReferenceEquals(dmg, null) ? null : dmg.GetGetMethod(true);
			MethodBase anim = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AnimatorEventHandler), "OnAnimCalled", new Type[] { typeof(int) });
			MethodBase wake = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(Baku), "WakeWorker", new Type[] { typeof(WorkerModel) });
			MethodBase annoy = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(Baku), "AnnoyWorker", new Type[] { typeof(WorkerModel) });
			MethodBase cry = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(Baku), "OnCryDamage", Type.EmptyTypes);
			if (object.ReferenceEquals(_animEvent, null) || object.ReferenceEquals(_wakeup, null) || object.ReferenceEquals(_annoyed, null) || object.ReferenceEquals(_wakedUp, null) || object.ReferenceEquals(_annoyedUp, null) || object.ReferenceEquals(anim, null) || object.ReferenceEquals(wake, null) || object.ReferenceEquals(annoy, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "void dream wake fix not applied: members not found");
			}
			else if (N.PatchUtil.Install(harmony, FastFixModule.Tag, anim, new HarmonyMethod(typeof(VoidDream).GetMethod("AnimPrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				HarmonyMethod queued = new HarmonyMethod(typeof(VoidDream).GetMethod("QueuedPostfix", BindingFlags.Public | BindingFlags.Static));
				N.PatchUtil.Install(harmony, FastFixModule.Tag, wake, null, queued);
				N.PatchUtil.Install(harmony, FastFixModule.Tag, annoy, null, queued);
			}
			if (object.ReferenceEquals(_makeEffect, null) || object.ReferenceEquals(_cryDmg, null) || object.ReferenceEquals(cry, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "void dream cry fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, cry, new HarmonyMethod(typeof(VoidDream).GetMethod("CryPrefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		// Runs for every animation event of every unit: two int compares before anything else.
		public static bool AnimPrefix(AnimatorEventHandler __instance, int i)
		{
			if ((i != WakeEvent && i != AnnoyEvent) || !_flag.On)
			{
				return true;
			}
			AnimatorEventHandler.AnimEventDelegate d = _animEvent.GetValue(__instance) as AnimatorEventHandler.AnimEventDelegate;
			Baku script = d == null ? null : d.Target as Baku;
			if (script == null || d.Method.Name != "OnWorkerAnimCalled" || d.GetInvocationList().Length != 1)
			{
				return true;
			}
			try
			{
				bool annoy = i == AnnoyEvent;
				Queue<WorkerModel> q = (annoy ? _annoyed : _wakeup).GetValue(script) as Queue<WorkerModel>;
				WorkerModel[] all = q == null ? new WorkerModel[0] : q.ToArray();
				for (int k = 0; k < all.Length; k++)
				{
					WorkerModel w = all[k];
					WorkerUnit unit = w == null ? null : w.GetWorkerUnit();
					if (unit != null && object.ReferenceEquals(unit.animEventHandler, __instance))
					{
						Release(script, w, annoy, false);
						Once(ref _eventLogged, "void dream: event " + i + " released " + w.name + " (queue " + all.Length + ")");
						return false;
					}
				}
				Once(ref _ignoredLogged, "void dream: event " + i + " ignored, its worker is not queued (queue " + all.Length + ")");
			}
			catch (Exception e)
			{
				LogError(e);
			}
			return false;
		}

		public static void QueuedPostfix(Baku __instance, WorkerModel worker)
		{
			if (worker == null || !_flag.On)
			{
				return;
			}
			try
			{
				AddPending(__instance, worker);
			}
			catch (Exception e)
			{
				LogError(e);
			}
		}

		private static void AddPending(Baku __instance, WorkerModel worker)
		{
			for (int k = 0; k < _pending.Count; k++)
			{
				if (object.ReferenceEquals(_pending[k].Worker, worker) && object.ReferenceEquals(_pending[k].Script, __instance))
				{
					return;
				}
			}
			bool annoy;
			if (Queued(__instance, worker, false))
			{
				annoy = false;
			}
			else if (Queued(__instance, worker, true))
			{
				annoy = true;
			}
			else
			{
				return;
			}
			Entry e = new Entry();
			e.Script = __instance;
			e.Worker = worker;
			e.Annoy = annoy;
			e.Due = Time.time + TimeoutSec;
			_pending.Add(e);
		}

		// FastFixDriver.Update.
		internal static void Tick()
		{
			if (_pending.Count == 0)
			{
				return;
			}
			if (!_flag.On)
			{
				_pending.Clear();
				return;
			}
			// Between day end or retry (OnStageRelease keeps the queues) and the next OnStageStart nothing is released.
			GameManager gm = GameManager.currentGameManager;
			if (gm == null || (gm.state != GameState.PLAYING && gm.state != GameState.PAUSE))
			{
				return;
			}
			float now = Time.time;
			for (int k = _pending.Count - 1; k >= 0; k--)
			{
				if (k >= _pending.Count)
				{
					continue;
				}
				Entry e = _pending[k];
				if (e.Script == null || e.Worker == null || !Queued(e.Script, e.Worker, e.Annoy))
				{
					_pending.RemoveAt(k);
				}
				else if (now >= e.Due)
				{
					try
					{
						Release(e.Script, e.Worker, e.Annoy, true);
					}
					catch (Exception ex)
					{
						_pending.Remove(e);
						N.ULog.Error(FastFixModule.Tag, "void dream: release of " + e.Worker.name + " failed: " + ex.Message);
						LogError(ex);
					}
				}
			}
		}

		private static bool Queued(Baku script, WorkerModel worker, bool annoy)
		{
			Queue<WorkerModel> q = (annoy ? _annoyed : _wakeup).GetValue(script) as Queue<WorkerModel>;
			return q != null && q.Contains(worker);
		}

		// Takes the worker out of the queue (others keep their order) and runs vanilla's release for him.
		private static void Release(Baku script, WorkerModel worker, bool annoy, bool timeout)
		{
			for (int k = _pending.Count - 1; k >= 0; k--)
			{
				if (object.ReferenceEquals(_pending[k].Worker, worker))
				{
					_pending.RemoveAt(k);
				}
			}
			Queue<WorkerModel> q = (annoy ? _annoyed : _wakeup).GetValue(script) as Queue<WorkerModel>;
			if (q == null)
			{
				return;
			}
			int n = q.Count;
			for (int k = 0; k < n; k++)
			{
				WorkerModel w = q.Dequeue();
				if (!object.ReferenceEquals(w, worker))
				{
					q.Enqueue(w);
				}
			}
			bool dead = worker.IsDead();
			if (timeout && !dead && !(worker.unconAction is Uncontrollable_Baku))
			{
				// Another creature took the worker over: his action is left alone, as in vanilla.
				return;
			}
			if (timeout)
			{
				N.ULog.Info(FastFixModule.Tag, "void dream: " + (annoy ? "annoyed" : "woken") + " worker " + worker.name + " released after " + TimeoutSec + " s without his animation event" + (dead ? " (dead, queue only)" : ""));
			}
			if (dead)
			{
				return;
			}
			try
			{
				(annoy ? _annoyedUp : _wakedUp).Invoke(script, new object[] { worker });
			}
			catch (Exception)
			{
				// Out of every queue now: do not leave him invincible.
				worker.SetInvincible(false);
				throw;
			}
		}

		public static bool CryPrefix(Baku __instance)
		{
			if (!_flag.On)
			{
				return true;
			}
			Baku script = __instance;
			PassageObjectModel passage = script.currentPassage;
			if (passage == null)
			{
				return false;
			}
			MovableObjectNode[] targets = passage.GetEnteredTargets(script.movable);
			int hit = 0;
			bool skipped = false;
			for (int k = 0; targets != null && k < targets.Length; k++)
			{
				UnitModel unit = targets[k] == null ? null : targets[k].GetUnit();
				if (unit == null || unit.hp <= 0f)
				{
					skipped = true;
					continue;
				}
				hit++;
				try
				{
					unit.TakeDamage(script.model, (DamageInfo)_cryDmg.Invoke(null, null));
					DamageParticleEffect.Invoker(unit, ((DamageInfo)_cryDmg.Invoke(null, null)).type, script.model);
					WorkerModel worker = unit as WorkerModel;
					if (worker != null)
					{
						script.AnnoyWorker(worker);
					}
				}
				catch (Exception e)
				{
					LogError(e);
				}
			}
			_makeEffect.Invoke(script, new object[] { "BakuChickenCryEffect", script.animScript.mouth, true });
			if (skipped)
			{
				Once(ref _cryLogged, "void dream: cry went past a downed unit, " + hit + " unit(s) hit");
			}
			return false;
		}

		// One info line per game session and kind: proof that a path ran, without per-hit spam.
		private static void Once(ref bool done, string text)
		{
			if (!done)
			{
				done = true;
				N.ULog.Info(FastFixModule.Tag, text);
			}
		}

		private static void LogError(Exception e)
		{
			if (!_errorLogged)
			{
				_errorLogged = true;
				N.ULog.Error(FastFixModule.Tag, "void dream fix error (logged once): " + e);
			}
		}
	}

	// Vanilla OrdealManager keeps _activatedOrdeals and _removedOrdeals across days: OnGameInit and OnStageRelease clear only the
	// ordeal creatures, so an ordeal still running when the day ends (retry, quota, return to title) keeps its FixedUpdate in
	// the next day and can send OrdealEnd there. Fix: both lists are emptied at the start of OnGameInit (once per day start).
	public static class OrdealReset
	{
		private static N.Flag _flag;
		private static FieldInfo _active;
		private static FieldInfo _removed;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			_active = typeof(OrdealManager).GetField("_activatedOrdeals", BindingFlags.Instance | BindingFlags.NonPublic);
			_removed = typeof(OrdealManager).GetField("_removedOrdeals", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(OrdealManager), "OnGameInit", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_active, null) || object.ReferenceEquals(_removed, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "ordeal reset fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(OrdealReset).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static void Prefix(OrdealManager __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				List<OrdealBase> active = _active.GetValue(__instance) as List<OrdealBase>;
				List<OrdealBase> removed = _removed.GetValue(__instance) as List<OrdealBase>;
				int stale = active == null ? 0 : active.Count;
				if (active != null)
				{
					active.Clear();
				}
				if (removed != null)
				{
					removed.Clear();
				}
				if (stale > 0)
				{
					N.ULog.Info(FastFixModule.Tag, "ordeal reset: " + stale + " ordeal(s) of the previous day removed");
				}
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "ordeal reset error (logged once): " + e);
				}
			}
		}
	}

	// InventoryModel.CheckEquipmentCount_Mod reads data_Mod.MaxNum without a null check, so a mod that asks about an equipment
	// id no mod defines throws a NullReferenceException. From a postfix of InventoryModel.LoadGlobalData (Supernova
	// ThePeekingBeater grants ids 11250703/11250704 there) the exception ends GlobalGameManager.LoadGlobalData, which skips
	// the Sephirah character data and logs only to BaseMods/Glerror.txt. Fix: an unknown id answers false (nothing to create),
	// as if its limit were reached; the first ids are logged. Known ids run the original unchanged.
	public static class UnknownEquipCount
	{
		private static N.Flag _flag;
		private static int _logged;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(InventoryModel), "CheckEquipmentCount_Mod", new Type[] { typeof(LobotomyBaseMod.LcId) });
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "unknown equipment count fix not applied: method not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(UnknownEquipCount).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(LobotomyBaseMod.LcId id, ref bool __result)
		{
			if (!_flag.On)
			{
				return true;
			}
			try
			{
				if (id == null || EquipmentTypeList.instance == null || EquipmentTypeList.instance.GetData_Mod(id) != null)
				{
					return true;
				}
				__result = false;
				if (_logged < 10)
				{
					_logged++;
					N.ULog.Warn(FastFixModule.Tag, "equipment id " + id.packageId + ":" + id.id + " is not defined by any mod; a mod asked to grant it, nothing created");
				}
				return false;
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "unknown equipment count fix error (logged once): " + e);
				}
				return true;
			}
		}
	}

	// Day restart while Express Train to Hell departs (vanilla HellTrainDeparture): GameManager.RestartGame calls EndGame
	// (state STOP), loads the day back and only then switches the scene, so the departing train keeps colliding for a few
	// frames and its hits (B 100) kill agents of the reloaded day. They start the day dead and their EGO is gone (death
	// removes it from the inventory). Fix: CollisionCheck does nothing while the game state is STOP; during a day it runs as in
	// vanilla. The cause is inferred from the restart order and the Buglist report; the log line shows when a hit was blocked.
	public static class TrainRestart
	{
		private static N.Flag _flag;
		private static bool _logged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(HellTrainDeparture), "CollisionCheck", new Type[] { typeof(Collider2D) });
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "train restart fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(TrainRestart).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix()
		{
			if (!_flag.On)
			{
				return true;
			}
			GameManager gm = GameManager.currentGameManager;
			if (gm != null && gm.state != GameState.STOP)
			{
				return true;
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "train restart: a Hell Train hit after the day ended or restarted was blocked");
			}
			return false;
		}
	}

	// Special EGO weapons that take the Spine state over (kygi123 template: remainDelay 100, attack duration 100, dummy Attack1
	// that disables the vanilla AttackEnd event) end an attack only through the Spine Complete of the attack track
	// (WorkerModel.SpecialAttackEnd -> WorkerUnit.EndAttack). When that track never plays or is replaced (state null at
	// OnAttackStart, the animation missing in the active skeleton, the AnimationState recreated by an animator switch), the
	// agent stands in place until remainDelay runs out (about 100 s), and the cycle then ends without SetBattle(false), so he
	// walks in battle stance. Fix (no patch): while the game runs, an agent with a SPECIAL weapon, remainDelay above the
	// vanilla 8 and no animation on Spine track 0 (no state, no entry or the empty animation) for 1 s gets the end of a normal
	// special attack: SetState(false) and WorkerUnit.EndAttack. A playing attack keeps track 0 busy and is never touched.
	// ---------- Third-party: CENSORED Remake (kygi123) ----------
	// Censored_Remake_Weapon adds a summoned child creature (CensoredWeaponCreatureModel: its own CreatureUnit and Spine
	// skeleton) to its Childs list on every "Baby" attack event. A child loses hp over time and is suppressed, but it leaves
	// the list and its unit is destroyed only at OnStageRelease: dead children keep ticking (the weapon's OnFixedUpdate runs
	// each one) and keep their units for the rest of the day, so the cost grows with every summon. Fix: a child dead for
	// 3 s (death animation played) gets the mod's own end-of-day cleanup, OnDestroy(), and leaves the list.
	public static class CensoredChildren
	{
		private const float GraceSec = 3f;
		private const float PollSec = 1f;
		private const string WeaponType = "kygi123_Censored_Remake.Censored_Remake_Weapon";

		private static N.Flag _flag;
		private static float _nextPoll;
		private static FieldInfo _childs;
		private static MethodInfo _destroy;
		private static readonly Dictionary<object, float> _since = new Dictionary<object, float>();
		private static int _removed;
		private static bool _logged;
		private static bool _errorLogged;

		internal static void Install(N.Flag flag)
		{
			_flag = flag;
		}

		internal static void Tick()
		{
			if (_flag == null || !_flag.On)
			{
				return;
			}
			float now = Time.time;
			if (now < _nextPoll)
			{
				return;
			}
			_nextPoll = now + PollSec;
			GameManager gm = GameManager.currentGameManager;
			if (gm == null || gm.state == GameState.STOP)
			{
				if (_since.Count > 0)
				{
					_since.Clear();
				}
				if (_removed > 0)
				{
					N.ULog.Info(FastFixModule.Tag, "censored children: " + _removed + " dead child(ren) cleaned up this day");
					_removed = 0;
					_logged = false;
				}
				return;
			}
			if (gm.state != GameState.PLAYING || AgentManager.instance == null)
			{
				return;
			}
			try
			{
				IList<AgentModel> agents = AgentManager.instance.GetAgentList();
				for (int i = 0; i < agents.Count; i++)
				{
					AgentModel agent = agents[i];
					WeaponModel weapon = agent == null ? null : agent.Equipment.weapon;
					if (weapon == null || weapon.script == null || weapon.script.GetType().FullName != WeaponType)
					{
						continue;
					}
					Clean(weapon.script, now);
				}
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "censored children: cleanup failed: " + e);
				}
			}
		}

		private static void Clean(object script, float now)
		{
			if (object.ReferenceEquals(_childs, null))
			{
				_childs = script.GetType().GetField("Childs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (object.ReferenceEquals(_childs, null))
				{
					return;
				}
			}
			System.Collections.IList list = _childs.GetValue(script) as System.Collections.IList;
			if (list == null)
			{
				return;
			}
			for (int i = list.Count - 1; i >= 0; i--)
			{
				CreatureModel child = list[i] as CreatureModel;
				if (child == null || child.hp > 0f)
				{
					continue;
				}
				float since;
				if (!_since.TryGetValue(child, out since))
				{
					_since[child] = now;
					continue;
				}
				if (now - since < GraceSec)
				{
					continue;
				}
				if (object.ReferenceEquals(_destroy, null))
				{
					_destroy = child.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
					if (object.ReferenceEquals(_destroy, null))
					{
						return;
					}
				}
				_since.Remove(child);
				list.RemoveAt(i);
				_destroy.Invoke(child, null);
				_removed++;
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "censored children: a dead summoned child was cleaned up (unit destroyed, no longer ticked)");
				}
			}
		}
	}

	public static class SpecialAttackStuck
	{
		private const float VanillaDelay = 8f;
		private const float GraceSec = 1f;
		private const float PollSec = 0.25f;

		private static N.Flag _flag;
		private static float _nextPoll;
		private static readonly Dictionary<AgentModel, float> _since = new Dictionary<AgentModel, float>();
		private static bool _logged;
		private static bool _errorLogged;

		internal static void Install(N.Flag flag)
		{
			_flag = flag;
		}

		internal static void Tick()
		{
			if (_flag == null || !_flag.On)
			{
				return;
			}
			float now = Time.time;
			if (now < _nextPoll)
			{
				return;
			}
			_nextPoll = now + PollSec;
			GameManager gm = GameManager.currentGameManager;
			if (gm == null || gm.state == GameState.STOP)
			{
				if (_since.Count > 0)
				{
					_since.Clear();
				}
				return;
			}
			if (gm.state != GameState.PLAYING || AgentManager.instance == null)
			{
				return;
			}
			try
			{
				IList<AgentModel> agents = AgentManager.instance.GetAgentList();
				for (int i = 0; i < agents.Count; i++)
				{
					Check(agents[i], now);
				}
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "special attack stuck: check failed: " + e);
				}
			}
		}

		private static void Check(AgentModel agent, float now)
		{
			if (agent == null)
			{
				return;
			}
			WeaponModel weapon = agent.Equipment.weapon;
			if (weapon == null || weapon.remainDelay <= VanillaDelay || weapon.metaInfo == null || weapon.metaInfo.weaponClassType != WeaponClassType.SPECIAL || agent.IsDead())
			{
				if (_since.Count > 0)
				{
					_since.Remove(agent);
				}
				return;
			}
			WorkerUnit unit = agent.GetWorkerUnit();
			if (unit == null || unit.animChanger == null)
			{
				return;
			}
			Spine.AnimationState state = unit.animChanger.state;
			Spine.TrackEntry entry = state == null ? null : state.GetCurrent(0);
			if (entry != null && entry.Animation != null && entry.Animation.Name != "<empty>")
			{
				_since.Remove(agent);
				return;
			}
			float first;
			if (!_since.TryGetValue(agent, out first))
			{
				_since[agent] = now;
				return;
			}
			if (now - first < GraceSec)
			{
				return;
			}
			_since.Remove(agent);
			float delay = weapon.remainDelay;
			unit.animChanger.SetState(false);
			try
			{
				unit.EndAttack();
			}
			catch (Exception e)
			{
				// OnAttackStart threw on the first attack: WeaponModel._currentDamageInfos is still null in OnEndAttackCycle.
				weapon.remainDelay = 0f;
				agent.tempAnim.EndAttack();
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "special attack stuck: end of attack of " + agent.name + " threw, delay cleared: " + e.Message);
				}
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "special attack stuck: ended the attack of " + agent.name + " (weapon " + weapon.metaInfo.id + ", remainDelay " + delay.ToString("0.0") + ", no Spine track for " + (now - first).ToString("0.0") + " s)");
			}
		}
	}

	public static class PanicStuck
	{
		private const int CheckEvery = 31;
		private const int Tries = 5;
		private static N.Flag _flag;
		private static FieldInfo _actor;
		private static FieldInfo _target;
		private static MethodInfo _next;
		private static int _calls;
		private static bool _warned;
		private static readonly Dictionary<AgentModel, int> Failed = new Dictionary<AgentModel, int>();

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
			_actor = typeof(PanicOpenIsolate).GetField("actor", Private);
			_target = typeof(PanicOpenIsolate).GetField("_currentOpenTargetCreature", Private);
			_next = typeof(PanicOpenIsolate).GetMethod("UpdateNextTarget", Private);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(PanicOpenIsolate), "Execute", Type.EmptyTypes);
			if (object.ReferenceEquals(_actor, null) || object.ReferenceEquals(_target, null) || object.ReferenceEquals(_next, null) || object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "panic walk fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(PanicStuck).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Postfix(PanicOpenIsolate __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				_calls++;
				if (_calls % CheckEvery != 0)
				{
					return;
				}
				AgentModel actor = (AgentModel)_actor.GetValue(__instance);
				CreatureModel target = (CreatureModel)_target.GetValue(__instance);
				if (target == null)
				{
					return;
				}
				MovableObjectNode node = actor.GetMovableNode();
				MapNode entry = target.GetEntryNode();
				if (node.IsMoving() || node.InElevator() || entry == node.GetCurrentNode())
				{
					Failed.Remove(actor);
					return;
				}
				int tries;
				Failed.TryGetValue(actor, out tries);
				tries++;
				if (tries > Tries)
				{
					Failed.Remove(actor);
					_next.Invoke(__instance, null);
					return;
				}
				Failed[actor] = tries;
				actor.MoveToNode(entry);
			}
			catch (Exception e)
			{
				if (!_warned)
				{
					_warned = true;
					N.ULog.Warn(FastFixModule.Tag, "panic walk: " + e.Message);
				}
			}
		}
	}

	// ---------- Patch guard ----------

	public static class Guard
	{
		private const float WindowSeconds = 10f;
		private const int MaxKeys = 256;
		private const string WrapperMark = "(wrapper dynamic-method) ";
		private const string OwnPrefix = "Lobotomy.NailOptimization";

		private sealed class Win
		{
			public string Patch;
			public string Wrapper;
			public string Reason;
			public float Start;
			public int Count;
			public bool Queued;
			public bool Found;
			public int BlockedFrame = -1;
			public bool Warned;
		}

		internal static N.IntParam Count;

		private static readonly Dictionary<string, Win> _wins = new Dictionary<string, Win>(StringComparer.Ordinal);
		private static readonly List<Win> _queue = new List<Win>();
		private static readonly HashSet<string> _blocked = new HashSet<string>(StringComparer.Ordinal);
		private static int _failures;

		internal static bool HasQueue
		{
			get
			{
				return _queue.Count > 0;
			}
		}

		internal static void Start()
		{
			Application.logMessageReceived += new Application.LogCallback(OnLog);
			N.ULog.Info(FastFixModule.Tag, "guard on count=" + Count.Value + "/10s");
		}

		// Main thread, every Unity log message. Work only for exceptions: find the patch frame above the Harmony wrapper.
		private static void OnLog(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception || string.IsNullOrEmpty(stackTrace))
			{
				return;
			}
			try
			{
				string patch;
				string wrapper;
				if (!ParseFrames(stackTrace, out patch, out wrapper))
				{
					return;
				}
				string reason = ExceptionType(condition);
				string key = patch + "|" + wrapper + "|" + reason;
				float now = Time.realtimeSinceStartup;
				Win w;
				if (!_wins.TryGetValue(key, out w))
				{
					if (_wins.Count >= MaxKeys)
					{
						Prune(now);
						if (_wins.Count >= MaxKeys)
						{
							return;
						}
					}
					w = new Win();
					w.Patch = patch;
					w.Wrapper = wrapper;
					w.Reason = reason;
					w.Start = now;
					_wins[key] = w;
				}
				if (w.BlockedFrame >= 0)
				{
					// Seen again a frame after the block: the skip Prefix did not take (inlined method).
					if (!w.Warned && Time.frameCount > w.BlockedFrame + 1)
					{
						w.Warned = true;
						N.ULog.Warn(FastFixModule.Tag, "guard block ineffective " + patch + " in " + wrapper + " reason=" + reason);
					}
					return;
				}
				if (now - w.Start > WindowSeconds)
				{
					w.Start = now;
					w.Count = 0;
				}
				w.Count++;
				if (!w.Queued && w.Count >= Count.Value)
				{
					w.Queued = true;
					_queue.Add(w);
				}
			}
			catch (Exception e)
			{
				_failures++;
				if (_failures == 1)
				{
					N.ULog.Error(FastFixModule.Tag, "guard callback error: " + e);
				}
				if (_failures >= 5)
				{
					Application.logMessageReceived -= new Application.LogCallback(OnLog);
					N.ULog.Error(FastFixModule.Tag, "guard stopped after 5 internal errors: " + e);
				}
			}
		}

		// "Ns.Type.Patch () <0x..>" directly above "(wrapper dynamic-method) Ns.Type.Method_PatchN (..) <0x..>".
		internal static bool ParseFrames(string stackTrace, out string patch, out string wrapper)
		{
			patch = null;
			wrapper = null;
			string[] lines = stackTrace.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (!line.StartsWith(WrapperMark, StringComparison.Ordinal))
				{
					continue;
				}
				// Thrown in the wrapper itself (copied original body): not a patch method.
				if (i == 0)
				{
					return false;
				}
				string above = lines[i - 1].Trim();
				if (above.StartsWith("at ", StringComparison.Ordinal))
				{
					above = above.Substring(3);
				}
				if (above.Length == 0 || above[0] == '(')
				{
					return false;
				}
				patch = FrameName(above);
				wrapper = FrameName(line.Substring(WrapperMark.Length));
				return patch.Length > 0 && wrapper.Length > 0;
			}
			return false;
		}

		private static string FrameName(string frame)
		{
			int p = frame.IndexOf(" (", StringComparison.Ordinal);
			return (p > 0 ? frame.Substring(0, p) : frame).Trim();
		}

		private static string ExceptionType(string condition)
		{
			if (condition == null)
			{
				return "?";
			}
			int c = condition.IndexOf(':');
			string t = c > 0 ? condition.Substring(0, c) : condition;
			t = t.Trim();
			return t.Length > 80 ? t.Substring(0, 80) : t;
		}

		private static void Prune(float now)
		{
			List<string> old = new List<string>();
			foreach (KeyValuePair<string, Win> kv in _wins)
			{
				if (!kv.Value.Queued && kv.Value.BlockedFrame < 0 && now - kv.Value.Start > WindowSeconds)
				{
					old.Add(kv.Key);
				}
			}
			if (old.Count == 0)
			{
				// Nothing expired: drop every window that is neither queued nor blocked (blocked ones keep "ineffective").
				foreach (KeyValuePair<string, Win> kv in _wins)
				{
					if (!kv.Value.Queued && kv.Value.BlockedFrame < 0)
					{
						old.Add(kv.Key);
					}
				}
			}
			for (int i = 0; i < old.Count; i++)
			{
				_wins.Remove(old[i]);
			}
		}

		// Driver Update, the frame after the threshold: resolve original and patch method by name, skip the patch method.
		internal static void Apply()
		{
			List<Win> work = new List<Win>(_queue);
			_queue.Clear();
			for (int i = 0; i < work.Count; i++)
			{
				Win w = work[i];
				int n = 0;
				try
				{
					n = Block(w);
				}
				catch (Exception e)
				{
					N.ULog.Error(FastFixModule.Tag, "guard block failed " + w.Patch + ": " + e);
				}
				if (n == 0 && !w.Found)
				{
					N.ULog.Warn(FastFixModule.Tag, "guard could not resolve " + w.Patch + " in " + w.Wrapper + " reason=" + w.Reason);
				}
			}
		}

		private static int Block(Win w)
		{
			int k = w.Wrapper.LastIndexOf("_Patch", StringComparison.Ordinal);
			if (k <= 0)
			{
				return 0;
			}
			string origFull = w.Wrapper.Substring(0, k);
			int dot = origFull.LastIndexOf('.');
			if (dot <= 0)
			{
				return 0;
			}
			string typeName = origFull.Substring(0, dot);
			string methodName = origFull.Substring(dot + 1);
			int n = 0;
			// Snapshot: AllPatchedMethods enumerates Harmony's live state, which our own Patch call below changes
			// (InvalidOperationException "out of sync", run 21:05).
			List<MethodBase> all = new List<MethodBase>(PatchProcessor.AllPatchedMethods());
			for (int a = 0; a < all.Count; a++)
			{
				MethodBase m = all[a];
				if (object.ReferenceEquals(m, null) || m.Name != methodName || object.ReferenceEquals(m.DeclaringType, null))
				{
					continue;
				}
				if (m.DeclaringType.FullName.Replace('+', '/') != typeName && m.DeclaringType.Name != typeName)
				{
					continue;
				}
				Patches ps = PatchProcessor.IsPatched(m);
				if (ps == null)
				{
					continue;
				}
				n += BlockIn(ps.Prefixes, "Prefix", w, m);
				n += BlockIn(ps.Postfixes, "Postfix", w, m);
			}
			return n;
		}

		private static int BlockIn(ReadOnlyCollection<Patch> list, string kind, Win w, MethodBase original)
		{
			int n = 0;
			for (int i = 0; i < list.Count; i++)
			{
				Patch p = list[i];
				MethodInfo pm = p.patch;
				if (object.ReferenceEquals(pm, null) || object.ReferenceEquals(pm.DeclaringType, null))
				{
					continue;
				}
				string name = pm.DeclaringType.FullName.Replace('+', '/') + "." + pm.Name;
				if (name != w.Patch)
				{
					continue;
				}
				w.Found = true;
				if (p.owner != null && p.owner.StartsWith(OwnPrefix, StringComparison.Ordinal))
				{
					N.ULog.Info(FastFixModule.Tag, "guard own patch not blocked " + name + " owner=" + p.owner);
					continue;
				}
				string bk = pm.DeclaringType.Assembly.FullName + "|" + pm.MetadataToken;
				if (_blocked.Contains(bk))
				{
					// Already skipped under another key (other exception type): watch this key for "ineffective" too.
					if (w.BlockedFrame < 0)
					{
						w.BlockedFrame = Time.frameCount;
					}
					n++;
					continue;
				}
				string skipName;
				if (object.ReferenceEquals(pm.ReturnType, typeof(void)))
				{
					skipName = "SkipVoid";
				}
				else if (object.ReferenceEquals(pm.ReturnType, typeof(bool)))
				{
					skipName = "SkipBool";
				}
				else
				{
					N.ULog.Warn(FastFixModule.Tag, "guard not blocked " + name + ": return type " + pm.ReturnType.Name);
					continue;
				}
				_blocked.Add(bk);
				MethodInfo skip = typeof(Guard).GetMethod(skipName, BindingFlags.Public | BindingFlags.Static);
				try
				{
					FastFixModule.Harmony.Patch(pm, new HarmonyMethod(skip), null, null);
				}
				catch (Exception e)
				{
					N.ULog.Error(FastFixModule.Tag, "guard patch failed " + name + ": " + e);
					continue;
				}
				w.BlockedFrame = Time.frameCount;
				n++;
				N.ULog.Warn(FastFixModule.Tag, "guard blocked patch " + name + " (" + kind + ") of " + original.DeclaringType.FullName + "." + original.Name + " owner=" + p.owner + " reason=" + w.Reason + " count=" + w.Count + "/10s");
			}
			return n;
		}

		// Skip Prefix for a void patch method (Prefix or Postfix of the game method).
		public static bool SkipVoid()
		{
			return false;
		}

		// Skip Prefix for a bool Prefix of the game method: report "run the original", as if that Prefix were absent.
		public static bool SkipBool(ref bool __result)
		{
			__result = true;
			return false;
		}
	}
}
