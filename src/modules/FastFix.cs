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
//   Covariance (any mod)          a List<A> a mod passes as IEnumerable<B> (covariance, absent in this mscorlib 2.0) gets a
//                                  converting call; without it Mono aborts the game (mini-trampolines.c:183).
//   Mod NREs                     one-off NREs of BlackBird, Pinocchio, RottenWhaleFall, DragonScale, Eternal Flower, Isomer
//                                  and a child creature suppressed twice (class ModNreFixes).
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
		private static N.Flag _loopGuard;
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
				return "1.31";
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
			_loopGuard = N.Config.BindFlag("FastFix.LoopGuard", true, "A method of another mod (not a patch) that throws the same exception FastFix.GuardCount times within 10 seconds is skipped until the game is closed, if it returns nothing. Read at game start.");
			_guardCount = N.Config.BindInt("FastFix.GuardCount", 100, 10, 1000, 10, "Identical exceptions within 10 seconds before the patch guard skips that patch method.");
			Harmony = HarmonyInstance.Create(HarmonyId);
			Guard.Count = _guardCount;
			Guard.PatchOn = _guard.On;
			Guard.LoopOn = _loopGuard.On;
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
			FastFixDriver.CovariancePending = true;
			FastFixDriver.ModNrePending = true;
			PanicStuck.Install(Harmony, N.Config.BindFlag("FastFix.PanicStuck", true, "An agent in the panic that opens containment units walks again when the walk to the unit was interrupted, and picks another unit if it cannot get there."));
			TrainRestart.Install(Harmony, N.Config.BindFlag("FastFix.TrainRestart", true, "Restarting the day while Express Train to Hell departs no longer kills employees of the reloaded day and loses their EGO."));
			WeaponReskin.Install(Harmony, N.Config.BindFlag("FastFix.WeaponReskin", true, "After a weapon is unequipped the agent shows and swings the Riot stick instead of One Sin's weapon."));
			OrdealReset.Install(Harmony, N.Config.BindFlag("FastFix.OrdealReset", true, "An ordeal still running when the day ends (retry, return to title) no longer keeps running and ending in the next day."));
			UnknownEquipCount.Install(Harmony, N.Config.BindFlag("FastFix.UnknownEquipCount", true, "A mod that tries to grant equipment no mod defines gets 'nothing to create' instead of an error that stops the save load (Sephirah data)."));
			CensoredChildren.Install(N.Config.BindFlag("FastFix.CensoredChildren", true, "CENSORED Remake (kygi123): summoned children that died are removed 3 s later instead of at the end of the day, so the day no longer slows down with every summon."));
			SpecialAttackStuck.Install(N.Config.BindFlag("FastFix.SpecialAttackStuck", true, "Special EGO weapons with their own Spine attack (kygi123 remakes and similar): an agent whose attack animation never plays no longer stands still for about 100 s and then walks in battle stance; the attack ends after 1 s."));
			VoidDream.Install(Harmony, N.Config.BindFlag("FastFix.VoidDream", true, "Void Dream: each sleeping employee is released by his own wake-up animation, or after 8 s when it never comes, instead of staying frozen and invincible until he dies at day end; the cry hits every unit in the hallway."));
			ExpandOnce.Install(Harmony, N.Config.BindFlag("FastFix.ExpandOnce", true, "Cancel pressed after confirming a department expansion no longer lets the same expansion be confirmed a second time the same day."));
			SelectClick.Install(Harmony, N.Config.BindFlag("FastFix.SelectClick", true, "A click that moved the mouse a little still selects or gives the move order instead of being taken for a drag; a drag cut off by switching windows no longer blocks later clicks."));
			CameraDrag.Install(Harmony, N.Config.BindFlag("FastFix.CameraDrag", true, "A map drag with the right or middle mouse button keeps following the mouse while the camera shakes from hits, instead of freezing and jumping."));
			StageEndGuard.Install(Harmony, N.Config.BindFlag("FastFix.StageEndGuard", true, "A creature that throws at the end of the day (Invalidum with an escaped creature outside the hallways) no longer stops the day end: the result screen still opens and the error is logged once."));
			NothingWorkerNoModel.Install(Harmony, N.Config.BindFlag("FastFix.NothingWorkerNoModel", true, "A NothingWorker object that a mod created without initializing it (It Goes By The Name of Dream) no longer throws an error every frame."));
			GameObject go = new GameObject("NailOptimization.FastFix");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<FastFixDriver>();
			if (_guard.On || _loopGuard.On)
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
		internal static bool CovariancePending;
		internal static bool ModNrePending;

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
			H12Hint.Tick();
			if (WhiteRabbitPending || RiskPortraitPending || StarflarePending || HellTrainPending || EffectNamesPending || BambooPending || CauldronPending || ChaofengPending || ColoredFixerPending || BossBirdPending || AddRoomPending || PinelPending || KingGreedPending || ElysiaUnitPending || AcaciaPending || CovariancePending || ModNrePending)
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
					try
					{
						if (CovariancePending && Covariance.TryInstall(_frames >= GiveUpFrames))
						{
							CovariancePending = false;
						}
					}
					catch (Exception e)
					{
						CovariancePending = false;
						N.ULog.Error(FastFixModule.Tag, "covariance install failed: " + e);
					}
					try
					{
						if (ModNrePending && ModNreFixes.TryInstall(_frames >= GiveUpFrames))
						{
							ModNrePending = false;
						}
					}
					catch (Exception e)
					{
						ModNrePending = false;
						N.ULog.Error(FastFixModule.Tag, "mod nre fixes install failed: " + e);
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

	// ---------- Covariance ----------
	// The game's mscorlib is 2.0: IEnumerable<T> is not covariant there (GenericParam flags 0). A mod built against mscorlib 4.0
	// may pass a List<AgentModel> where IEnumerable<UnitModel> is expected (C# 4 allows it without a cast); the first interface
	// call on it (Enumerable.Contains, List ctor, AddRange, foreach) has no IMT slot and Mono aborts the game:
	// "List<AgentModel> doesn't implement interface IEnumerable<UnitModel>" + "should not be reached at mini-trampolines.c:183"
	// (Krau Deluge FD.OnFixedUpdate, White/Black Bird and Drifting Fox CanTakeDamage). At the title the IL of every mod
	// assembly that references mscorlib 4 is read once: a typed stack walk finds each value whose static type reaches an
	// IEnumerable<B> parameter, field, local, argument or return only through variance (it implements IEnumerable<A>,
	// A derives from B). A Transpiler (Priority.First) puts Covary<B>(value) right after the instruction that produced
	// it: the same items, enumerated through the non-generic IEnumerable. Other variant interfaces are only logged.
	// A target with exception handlers (foreach, using, try) loses them under 0Harmony (GL-53): it is patched only when 12Harmony
	// already builds the wrappers (another mod loaded it) or FastFix.CovarianceHandlers lets this module load it (GL-55: every
	// later wrapper of every mod is then built by 12Harmony); otherwise the place is logged as not fixed.
	public static class Covariance
	{
		private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		private class Site
		{
			public int Index;
			public string Op;
			public string Member;
			public int Count;
			public Type Elem;
			public string Text;
		}

		private struct Slot
		{
			public Type T;
			public int P;
		}

		// Read by the Transpiler, which Harmony runs again whenever the method is patched later.
		private static readonly Dictionary<IntPtr, List<Site>> Sites = new Dictionary<IntPtr, List<Site>>();
		private static readonly Dictionary<Assembly, bool> Seen = new Dictionary<Assembly, bool>();
		private static OpCode[] _one;
		private static OpCode[] _two;
		private static bool[] _hasOne;
		private static bool[] _hasTwo;
		private static int[] _pop1;
		private static int[] _push1;
		private static int[] _pop2;
		private static int[] _push2;
		private static N.Flag _flag;
		private static N.Flag _handlers;
		private static bool _h12;
		private static bool _h12Missing;
		private static string _ownDir;
		private static MethodInfo _covary;
		private static int _fixed;
		private static int _applied;

		// True when the attempt is finished. Runs on each driver poll until the give-up frame: assemblies a mod loads late are read then.
		internal static bool TryInstall(bool last)
		{
			if (_flag == null)
			{
				_flag = N.Config.BindFlag("FastFix.Covariance", true, "A mod built for a newer .NET that passes a list of one unit type where a list of another is expected (List<AgentModel> as IEnumerable<UnitModel>) no longer crashes the game without an error: such places are found in the mod code at start and get a compatible list. Read at game start.");
				if (!_flag.On)
				{
					return true;
				}
				_handlers = N.Config.BindFlag("FastFix.CovarianceHandlers", true, "Such places inside a method with foreach, using or try are fixed too, if 12Harmony.dll is in Managed. This loads 12Harmony, which then builds the patches of all mods (changes how other mods' patches are applied). Read at game start.");
				BuildOps();
				_covary = typeof(Covariance).GetMethod("Covary", BindingFlags.Public | BindingFlags.Static);
				_ownDir = System.IO.Path.GetDirectoryName(typeof(N.ULog).Assembly.Location);
			}
			if (!_flag.On)
			{
				return true;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			int read = 0;
			int methods = 0;
			int skipped = 0;
			List<MethodBase> targets = new List<MethodBase>();
			for (int a = 0; a < all.Length; a++)
			{
				if (Seen.ContainsKey(all[a]))
				{
					continue;
				}
				Seen[all[a]] = true;
				if (!IsModNet4(all[a]))
				{
					continue;
				}
				read++;
				ScanAssembly(all[a], targets, ref methods, ref skipped);
			}
			for (int i = 0; i < targets.Count; i++)
			{
				Patch(targets[i]);
			}
			if (read > 0)
			{
				N.ULog.Info(FastFixModule.Tag, "covariance: " + read + " assemblies, " + methods + " methods read, " + skipped + " not analyzed, " + targets.Count + " methods to patch, " + _fixed + " places fixed in total, " + sw.ElapsedMilliseconds + " ms");
			}
			return last;
		}

		// Same items as the source, through the non-generic IEnumerable; null stays null so the callee throws as before.
		public static IEnumerable<T> Covary<T>(System.Collections.IEnumerable source) where T : class
		{
			if (source == null)
			{
				return null;
			}
			return CovaryItems<T>(source);
		}

		private static IEnumerable<T> CovaryItems<T>(System.Collections.IEnumerable source) where T : class
		{
			System.Collections.IEnumerator e = source.GetEnumerator();
			try
			{
				while (e.MoveNext())
				{
					yield return (T)e.Current;
				}
			}
			finally
			{
				IDisposable d = e as IDisposable;
				if (d != null)
				{
					d.Dispose();
				}
			}
		}

		// Harmony runs this again whenever any mod patches the method later: it must not throw and must check the indexes each time.
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			try
			{
				List<Site> sites;
				if (object.ReferenceEquals(original, null) || !Sites.TryGetValue(original.MethodHandle.Value, out sites))
				{
					return list;
				}
				if (list.Count != sites[0].Count)
				{
					N.ULog.Error(FastFixModule.Tag, "covariance: " + original.DeclaringType.FullName + "." + original.Name + " not fixed: " + list.Count + " instructions, " + sites[0].Count + " read at start (another transpiler ran first)");
					return list;
				}
				for (int i = 0; i < sites.Count; i++)
				{
					Site s = sites[i];
					MemberInfo member = list[s.Index].operand as MemberInfo;
					if (Norm(list[s.Index].opcode.Name) != s.Op || (s.Member != null && (object.ReferenceEquals(member, null) || member.Name != s.Member)))
					{
						N.ULog.Error(FastFixModule.Tag, "covariance: " + original.DeclaringType.FullName + "." + original.Name + " not fixed: instruction " + s.Index + " is not the expected " + s.Op);
						return list;
					}
				}
				List<CodeInstruction> result = new List<CodeInstruction>(list);
				// Sites are sorted by index: inserting from the last keeps the earlier indexes valid.
				for (int i = sites.Count - 1; i >= 0; i--)
				{
					result.Insert(sites[i].Index + 1, new CodeInstruction(OpCodes.Call, _covary.MakeGenericMethod(sites[i].Elem)));
				}
				_applied += sites.Count;
				return result;
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "covariance: transpiler failed, method left unchanged: " + e.Message);
				return list;
			}
		}

		private static string Norm(string name)
		{
			return name.EndsWith(".s", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) : name;
		}

		private static bool IsModNet4(Assembly asm)
		{
			string location;
			try
			{
				if (asm is AssemblyBuilder)
				{
					return false;
				}
				location = asm.Location;
			}
			catch (Exception)
			{
				return false;
			}
			if (string.IsNullOrEmpty(location))
			{
				return false;
			}
			string full = location.Replace('/', '\\');
			if (full.IndexOf("\\BaseMods\\", StringComparison.OrdinalIgnoreCase) < 0 || (!string.IsNullOrEmpty(_ownDir) && full.StartsWith(_ownDir.Replace('/', '\\') + "\\", StringComparison.OrdinalIgnoreCase)))
			{
				return false;
			}
			AssemblyName[] refs;
			try
			{
				refs = asm.GetReferencedAssemblies();
			}
			catch (Exception)
			{
				return false;
			}
			for (int i = 0; i < refs.Length; i++)
			{
				if (refs[i].Name == "mscorlib" && refs[i].Version != null && refs[i].Version.Major >= 4)
				{
					return true;
				}
			}
			return false;
		}

		private static void BuildOps()
		{
			_one = new OpCode[256];
			_two = new OpCode[256];
			_hasOne = new bool[256];
			_hasTwo = new bool[256];
			_pop1 = new int[256];
			_push1 = new int[256];
			_pop2 = new int[256];
			_push2 = new int[256];
			FieldInfo[] fs = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
			for (int i = 0; i < fs.Length; i++)
			{
				if (!object.ReferenceEquals(fs[i].FieldType, typeof(OpCode)))
				{
					continue;
				}
				OpCode op = (OpCode)fs[i].GetValue(null);
				int v = op.Value & 0xFF;
				if (op.Size == 1)
				{
					_one[v] = op;
					_hasOne[v] = true;
					_pop1[v] = Count(op.StackBehaviourPop);
					_push1[v] = Count(op.StackBehaviourPush);
				}
				else
				{
					_two[v] = op;
					_hasTwo[v] = true;
					_pop2[v] = Count(op.StackBehaviourPop);
					_push2[v] = Count(op.StackBehaviourPush);
				}
			}
		}

		// Pops or pushes named by the stack behaviour; -1 = depends on the operand.
		private static int Count(StackBehaviour sb)
		{
			string n = sb.ToString();
			if (n == "Pop0" || n == "Push0")
			{
				return 0;
			}
			if (n == "Varpop" || n == "Varpush")
			{
				return -1;
			}
			int c = 1;
			for (int i = 0; i < n.Length; i++)
			{
				if (n[i] == '_')
				{
					c++;
				}
			}
			return c;
		}

		private static void ScanAssembly(Assembly asm, List<MethodBase> targets, ref int methods, ref int skipped)
		{
			Type[] types;
			try
			{
				types = asm.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				types = e.Types;
			}
			catch (Exception)
			{
				return;
			}
			bool reported = false;
			for (int t = 0; t < types.Length; t++)
			{
				List<MethodBase> list = new List<MethodBase>();
				try
				{
					if (object.ReferenceEquals(types[t], null) || types[t].ContainsGenericParameters || types[t].IsValueType)
					{
						continue;
					}
					list.AddRange(types[t].GetMethods(Any));
					list.AddRange(types[t].GetConstructors(Any));
				}
				catch (Exception)
				{
					continue;
				}
				for (int m = 0; m < list.Count; m++)
				{
					MethodBody body;
					byte[] il;
					try
					{
						if (list[m].ContainsGenericParameters || list[m].IsAbstract || (list[m].IsConstructor && list[m].IsStatic))
						{
							continue;
						}
						body = list[m].GetMethodBody();
						il = body != null ? body.GetILAsByteArray() : null;
					}
					catch (Exception)
					{
						continue;
					}
					if (il == null || il.Length == 0)
					{
						continue;
					}
					methods++;
					List<Site> found = new List<Site>();
					bool ok;
					try
					{
						ok = Walk(list[m], body, il, found);
					}
					catch (Exception e)
					{
						ok = false;
						if (!reported)
						{
							reported = true;
							N.ULog.Warn(FastFixModule.Tag, "covariance: " + types[t].FullName + "." + list[m].Name + " not analyzed: " + e.Message);
						}
					}
					if (!ok)
					{
						skipped++;
						continue;
					}
					if (found.Count > 0)
					{
						found.Sort(delegate(Site x, Site y) { return x.Index.CompareTo(y.Index); });
						Sites[list[m].MethodHandle.Value] = found;
						targets.Add(list[m]);
					}
				}
			}
		}

		// Typed stack walk over the IL; false when the stack cannot be followed (the method is left alone).
		private static bool Walk(MethodBase method, MethodBody body, byte[] il, List<Site> found)
		{
			Module mod = method.Module;
			ParameterInfo[] ps = method.GetParameters();
			MethodInfo mi = method as MethodInfo;
			Type ret = !object.ReferenceEquals(mi, null) ? mi.ReturnType : typeof(void);
			IList<LocalVariableInfo> locals = body.LocalVariables;
			Dictionary<int, Slot[]> at = new Dictionary<int, Slot[]>();
			Dictionary<int, int> handlers = new Dictionary<int, int>();
			IList<ExceptionHandlingClause> clauses = body.ExceptionHandlingClauses;
			for (int c = 0; c < clauses.Count; c++)
			{
				ExceptionHandlingClause h = clauses[c];
				handlers[h.HandlerOffset] = (h.Flags == ExceptionHandlingClauseOptions.Clause || h.Flags == ExceptionHandlingClauseOptions.Filter) ? 1 : 0;
				if (h.Flags == ExceptionHandlingClauseOptions.Filter)
				{
					handlers[h.FilterOffset] = 1;
				}
			}
			List<Slot> stack = new List<Slot>();
			List<Site> unfixed = new List<Site>();
			List<OpCode> opList = new List<OpCode>();
			List<string> memberNames = new List<string>();
			bool dead = false;
			int pos = 0;
			int index = 0;
			while (pos < il.Length)
			{
				int start = pos;
				int hk;
				if (handlers.TryGetValue(start, out hk))
				{
					stack.Clear();
					if (hk == 1)
					{
						stack.Add(Unknown(-1));
					}
					dead = false;
				}
				Slot[] snap;
				if (at.TryGetValue(start, out snap))
				{
					if (dead)
					{
						stack.Clear();
						stack.AddRange(snap);
					}
					else if (!Merge(stack, snap))
					{
						return false;
					}
					dead = false;
				}
				else if (dead)
				{
					stack.Clear();
					dead = false;
				}
				OpCode op;
				int pops;
				int pushes;
				byte b = il[pos++];
				if (b == 0xFE)
				{
					if (pos >= il.Length || !_hasTwo[il[pos]])
					{
						return false;
					}
					pops = _pop2[il[pos]];
					pushes = _push2[il[pos]];
					op = _two[il[pos++]];
				}
				else
				{
					if (!_hasOne[b])
					{
						return false;
					}
					op = _one[b];
					pops = _pop1[b];
					pushes = _push1[b];
				}
				int operandAt = pos;
				int token = 0;
				int[] targetsAt = null;
				switch (op.OperandType)
				{
					case OperandType.InlineNone:
						break;
					case OperandType.ShortInlineBrTarget:
						targetsAt = new int[] { pos + 1 + (sbyte)il[pos] };
						pos += 1;
						break;
					case OperandType.ShortInlineI:
					case OperandType.ShortInlineVar:
						pos += 1;
						break;
					case OperandType.InlineVar:
						pos += 2;
						break;
					case OperandType.InlineBrTarget:
						targetsAt = new int[] { pos + 4 + BitConverter.ToInt32(il, pos) };
						pos += 4;
						break;
					case OperandType.InlineI8:
					case OperandType.InlineR:
						pos += 8;
						break;
					case OperandType.InlineSwitch:
						int n = BitConverter.ToInt32(il, pos);
						int baseAt = pos + 4 + 4 * n;
						targetsAt = new int[n];
						for (int k = 0; k < n; k++)
						{
							targetsAt[k] = baseAt + BitConverter.ToInt32(il, pos + 4 + 4 * k);
						}
						pos = baseAt;
						break;
					default:
						token = BitConverter.ToInt32(il, pos);
						pos += 4;
						break;
				}
				short v = op.Value;
				Type pushType = null;
				MethodBase callee = null;
				ParameterInfo[] cps = null;
				string memberName = null;
				if (op.OperandType == OperandType.InlineMethod)
				{
					callee = ResolveMethod(mod, token);
					if (object.ReferenceEquals(callee, null))
					{
						return false;
					}
					if ((callee.CallingConvention & CallingConventions.VarArgs) != 0)
					{
						return false;
					}
					memberName = callee.Name;
					cps = callee.GetParameters();
				}
				if (v == OpCodes.Calli.Value)
				{
					return false;
				}
				if (pops < 0)
				{
					if (v == OpCodes.Ret.Value)
					{
						pops = object.ReferenceEquals(ret, typeof(void)) ? 0 : 1;
					}
					else if (!object.ReferenceEquals(callee, null))
					{
						pops = cps.Length + ((callee.IsStatic || v == OpCodes.Newobj.Value) ? 0 : 1);
					}
					else
					{
						return false;
					}
				}
				if (stack.Count < pops)
				{
					return false;
				}
				// Consumers: where a value meets its declared type.
				if (!object.ReferenceEquals(callee, null) && (v == OpCodes.Call.Value || v == OpCodes.Callvirt.Value || v == OpCodes.Newobj.Value))
				{
					int first = stack.Count - cps.Length;
					for (int k = 0; k < cps.Length; k++)
					{
						Check(cps[k].ParameterType, stack[first + k], il, method, callee.DeclaringType.Name + "." + callee.Name + " argument " + (k + 1), found, unfixed);
					}
					if (!callee.IsStatic && v != OpCodes.Newobj.Value && callee.DeclaringType.IsInterface && first > 0)
					{
						Check(callee.DeclaringType, stack[first - 1], il, method, callee.DeclaringType.Name + "." + callee.Name + " receiver", found, unfixed);
					}
				}
				else if ((v == OpCodes.Stfld.Value || v == OpCodes.Stsfld.Value) && stack.Count > 0)
				{
					FieldInfo f = ResolveField(mod, token);
					if (!object.ReferenceEquals(f, null))
					{
						Check(f.FieldType, stack[stack.Count - 1], il, method, "field " + f.Name, found, unfixed);
					}
				}
				else if (v == OpCodes.Ret.Value && pops == 1)
				{
					Check(ret, stack[stack.Count - 1], il, method, "return", found, unfixed);
				}
				else if (stack.Count > 0 && IsStloc(v))
				{
					int li = LocalIndex(v, il, operandAt);
					if (li >= 0 && li < locals.Count)
					{
						Check(locals[li].LocalType, stack[stack.Count - 1], il, method, "local " + li, found, unfixed);
					}
				}
				else if (stack.Count > 0 && (v == OpCodes.Starg.Value || v == OpCodes.Starg_S.Value))
				{
					int ai = op.OperandType == OperandType.ShortInlineVar ? il[operandAt] : BitConverter.ToUInt16(il, operandAt);
					Type at0 = ArgType(method, ps, ai);
					if (!object.ReferenceEquals(at0, null))
					{
						Check(at0, stack[stack.Count - 1], il, method, "argument " + ai, found, unfixed);
					}
				}
				Slot top = stack.Count > 0 ? stack[stack.Count - 1] : Unknown(-1);
				stack.RemoveRange(stack.Count - pops, pops);
				// Pushes and their static types.
				if (pushes < 0)
				{
					if (object.ReferenceEquals(callee, null))
					{
						return false;
					}
					if (v == OpCodes.Newobj.Value)
					{
						pushType = callee.DeclaringType;
						pushes = 1;
					}
					else
					{
						MethodInfo cm = callee as MethodInfo;
						pushes = (!object.ReferenceEquals(cm, null) && !object.ReferenceEquals(cm.ReturnType, typeof(void))) ? 1 : 0;
						pushType = !object.ReferenceEquals(cm, null) ? cm.ReturnType : null;
					}
				}
				else if (v == OpCodes.Newobj.Value)
				{
					pushType = callee.DeclaringType;
				}
				else if (v == OpCodes.Dup.Value)
				{
					// Both copies lose their producer: a conversion there would also reach the other consumer.
					stack.Add(Typed(top.T, -1));
					stack.Add(Typed(top.T, -1));
					pushes = 0;
				}
				else if (v == OpCodes.Ldfld.Value || v == OpCodes.Ldsfld.Value)
				{
					FieldInfo f = ResolveField(mod, token);
					pushType = !object.ReferenceEquals(f, null) ? f.FieldType : null;
					memberName = !object.ReferenceEquals(f, null) ? f.Name : null;
				}
				else if (v == OpCodes.Castclass.Value || v == OpCodes.Isinst.Value)
				{
					pushType = ResolveType(mod, token);
				}
				else if (IsLdloc(v))
				{
					int li = LocalIndex(v, il, operandAt);
					pushType = li >= 0 && li < locals.Count ? locals[li].LocalType : null;
				}
				else if (IsLdarg(v))
				{
					int ai = ArgIndex(v, il, operandAt, op);
					pushType = ArgType(method, ps, ai);
				}
				for (int k = 0; k < pushes; k++)
				{
					stack.Add(Typed(pushType, index));
				}
				// Control flow.
				if (v == OpCodes.Leave.Value || v == OpCodes.Leave_S.Value)
				{
					stack.Clear();
				}
				if (targetsAt != null)
				{
					for (int k = 0; k < targetsAt.Length; k++)
					{
						if (targetsAt[k] <= start && stack.Count != 0)
						{
							return false;
						}
						Slot[] old;
						if (at.TryGetValue(targetsAt[k], out old))
						{
							List<Slot> merged = new List<Slot>(old);
							if (!Merge(merged, stack.ToArray()))
							{
								return false;
							}
							at[targetsAt[k]] = merged.ToArray();
						}
						else
						{
							at[targetsAt[k]] = stack.ToArray();
						}
					}
				}
				FlowControl fc = op.FlowControl;
				if (fc == FlowControl.Branch || fc == FlowControl.Return || fc == FlowControl.Throw)
				{
					dead = true;
				}
				opList.Add(op);
				memberNames.Add(memberName);
				index++;
			}
			for (int i = 0; i < found.Count; i++)
			{
				found[i].Op = Norm(opList[found[i].Index].Name);
				found[i].Count = index;
				found[i].Member = memberNames[found[i].Index];
			}
			// Each place is logged once per method when the method is read.
			for (int i = 0; i < unfixed.Count; i++)
			{
				N.ULog.Warn(FastFixModule.Tag, unfixed[i].Text);
			}
			return true;
		}

		private static bool Merge(List<Slot> stack, Slot[] other)
		{
			if (stack.Count != other.Length)
			{
				return false;
			}
			for (int i = 0; i < other.Length; i++)
			{
				Slot s = stack[i];
				if (!object.ReferenceEquals(s.T, other[i].T))
				{
					s.T = null;
				}
				if (s.P != other[i].P)
				{
					s.P = -1;
				}
				stack[i] = s;
			}
			return true;
		}

		private static Slot Unknown(int producer)
		{
			return Typed(null, producer);
		}

		private static Slot Typed(Type t, int producer)
		{
			Slot s = new Slot();
			s.T = t;
			s.P = producer;
			return s;
		}

		private static void Check(Type target, Slot value, byte[] il, MethodBase method, string where, List<Site> found, List<Site> unfixed)
		{
			if (object.ReferenceEquals(value.T, null) || object.ReferenceEquals(target, null) || !target.IsInterface || !target.IsGenericType || target.IsAssignableFrom(value.T))
			{
				return;
			}
			bool covariant;
			Type source = VariantSource(target, value.T, out covariant);
			if (object.ReferenceEquals(source, null))
			{
				return;
			}
			string text = "covariance: " + method.DeclaringType.Assembly.GetName().Name + " " + method.DeclaringType.FullName + "." + method.Name + ": " + Name(value.T) + " used as " + Name(target) + " (" + where + ")";
			Site s = new Site();
			s.Index = value.P;
			s.Text = text;
			if (!covariant || !object.ReferenceEquals(target.GetGenericTypeDefinition(), typeof(IEnumerable<>)) || value.P < 0)
			{
				s.Text = text + " - not fixed, this place can crash the game";
				unfixed.Add(s);
				return;
			}
			for (int i = 0; i < found.Count; i++)
			{
				if (found[i].Index == value.P)
				{
					return;
				}
			}
			s.Elem = target.GetGenericArguments()[0];
			found.Add(s);
		}

		// The interface of 'actual' that equals 'target' up to variance of its type arguments, or null.
		private static Type VariantSource(Type target, Type actual, out bool covariant)
		{
			covariant = false;
			Type def = target.GetGenericTypeDefinition();
			Type[] want = target.GetGenericArguments();
			List<Type> candidates = new List<Type>();
			if (actual.IsInterface && actual.IsGenericType)
			{
				candidates.Add(actual);
			}
			try
			{
				candidates.AddRange(actual.GetInterfaces());
			}
			catch (Exception)
			{
				return null;
			}
			for (int i = 0; i < candidates.Count; i++)
			{
				Type c = candidates[i];
				if (!c.IsGenericType || !object.ReferenceEquals(c.GetGenericTypeDefinition(), def))
				{
					continue;
				}
				Type[] have = c.GetGenericArguments();
				bool co = true;
				bool contra = true;
				for (int k = 0; k < have.Length; k++)
				{
					if (object.ReferenceEquals(have[k], want[k]))
					{
						continue;
					}
					if (have[k].IsValueType || want[k].IsValueType)
					{
						co = false;
						contra = false;
						break;
					}
					if (!want[k].IsAssignableFrom(have[k]))
					{
						co = false;
					}
					if (!have[k].IsAssignableFrom(want[k]))
					{
						contra = false;
					}
				}
				if (co || contra)
				{
					covariant = co;
					return c;
				}
			}
			return null;
		}

		private static string Name(Type t)
		{
			if (!t.IsGenericType)
			{
				return t.Name;
			}
			Type[] args = t.GetGenericArguments();
			string s = t.Name;
			int q = s.IndexOf('`');
			if (q >= 0)
			{
				s = s.Substring(0, q);
			}
			s += "<";
			for (int i = 0; i < args.Length; i++)
			{
				s += (i > 0 ? "," : "") + Name(args[i]);
			}
			return s + ">";
		}

		private static bool IsStloc(short v)
		{
			return v == OpCodes.Stloc.Value || v == OpCodes.Stloc_S.Value || v == OpCodes.Stloc_0.Value || v == OpCodes.Stloc_1.Value || v == OpCodes.Stloc_2.Value || v == OpCodes.Stloc_3.Value;
		}

		private static bool IsLdloc(short v)
		{
			return v == OpCodes.Ldloc.Value || v == OpCodes.Ldloc_S.Value || v == OpCodes.Ldloc_0.Value || v == OpCodes.Ldloc_1.Value || v == OpCodes.Ldloc_2.Value || v == OpCodes.Ldloc_3.Value;
		}

		private static bool IsLdarg(short v)
		{
			return v == OpCodes.Ldarg.Value || v == OpCodes.Ldarg_S.Value || v == OpCodes.Ldarg_0.Value || v == OpCodes.Ldarg_1.Value || v == OpCodes.Ldarg_2.Value || v == OpCodes.Ldarg_3.Value;
		}

		private static int LocalIndex(short v, byte[] il, int operandAt)
		{
			if (v == OpCodes.Stloc_0.Value || v == OpCodes.Ldloc_0.Value)
			{
				return 0;
			}
			if (v == OpCodes.Stloc_1.Value || v == OpCodes.Ldloc_1.Value)
			{
				return 1;
			}
			if (v == OpCodes.Stloc_2.Value || v == OpCodes.Ldloc_2.Value)
			{
				return 2;
			}
			if (v == OpCodes.Stloc_3.Value || v == OpCodes.Ldloc_3.Value)
			{
				return 3;
			}
			if (v == OpCodes.Stloc_S.Value || v == OpCodes.Ldloc_S.Value)
			{
				return il[operandAt];
			}
			return BitConverter.ToUInt16(il, operandAt);
		}

		private static int ArgIndex(short v, byte[] il, int operandAt, OpCode op)
		{
			if (v == OpCodes.Ldarg_0.Value)
			{
				return 0;
			}
			if (v == OpCodes.Ldarg_1.Value)
			{
				return 1;
			}
			if (v == OpCodes.Ldarg_2.Value)
			{
				return 2;
			}
			if (v == OpCodes.Ldarg_3.Value)
			{
				return 3;
			}
			return op.OperandType == OperandType.ShortInlineVar ? il[operandAt] : BitConverter.ToUInt16(il, operandAt);
		}

		// IL argument index -> static type; 'this' of a reference type is its declaring type.
		private static Type ArgType(MethodBase method, ParameterInfo[] ps, int index)
		{
			if (!method.IsStatic)
			{
				if (index == 0)
				{
					return method.DeclaringType.IsValueType ? null : method.DeclaringType;
				}
				index--;
			}
			if (index < 0 || index >= ps.Length)
			{
				return null;
			}
			Type t = ps[index].ParameterType;
			return t.IsByRef ? null : t;
		}

		private static MethodBase ResolveMethod(Module mod, int token)
		{
			try
			{
				return mod.ResolveMethod(token);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static FieldInfo ResolveField(Module mod, int token)
		{
			try
			{
				return mod.ResolveField(token);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Type ResolveType(Module mod, int token)
		{
			try
			{
				return mod.ResolveType(token);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void Patch(MethodBase m)
		{
			List<Site> sites = Sites[m.MethodHandle.Value];
			MethodBody body = m.GetMethodBody();
			if (body.ExceptionHandlingClauses.Count > 0 && !Ensure12())
			{
				for (int i = 0; i < sites.Count; i++)
				{
					N.ULog.Warn(FastFixModule.Tag, sites[i].Text + " - not fixed: the method has exception handlers and " + (_h12Missing ? "12Harmony.dll is missing or failed to load" : "FastFix.CovarianceHandlers is off"));
				}
				Sites.Remove(m.MethodHandle.Value);
				Record12("FastFix.CovarianceHandlers");
				return;
			}
			int before = _applied;
			try
			{
				HarmonyMethod transpiler = new HarmonyMethod(typeof(Covariance).GetMethod("Transpiler", BindingFlags.Public | BindingFlags.Static));
				// Harmony 1.0.9.1 field name; 800 = Priority.First (as PerfTools): runs before other transpilers, on the original instruction indexes.
				transpiler.prioritiy = 800;
				FastFixModule.Harmony.Patch(m, null, null, transpiler);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "covariance: " + m.DeclaringType.FullName + "." + m.Name + " patch failed: " + e.Message);
				Sites.Remove(m.MethodHandle.Value);
				return;
			}
			if (_applied == before)
			{
				return;
			}
			for (int i = 0; i < sites.Count; i++)
			{
				_fixed++;
				N.ULog.Info(FastFixModule.Tag, sites[i].Text + " - fixed");
			}
		}

		// True when 12Harmony is loaded (by Covariance or another mod); loads it like Covariance does when
		// FastFix.CovarianceHandlers is on and 12Harmony.dll is present.
		// Option keys of fixes skipped for lack of 12Harmony (only mods actually loaded); read by H12Hint on the title.
		internal static readonly List<string> H12Skipped = new List<string>();

		internal static void NoH12(string key, string what)
		{
			N.ULog.Warn(FastFixModule.Tag, key + " not applied: " + what);
			Record12(key);
		}

		internal static void Record12(string key)
		{
			if (!H12Skipped.Contains(key))
			{
				H12Skipped.Add(key);
			}
		}

		// 12Harmony.dll next to 0Harmony.dll (Managed): false = the file is missing, true = only the option is off or it failed to load.
		internal static bool H12DllPresent()
		{
			try
			{
				string dir = System.IO.Path.GetDirectoryName(typeof(HarmonyInstance).Assembly.Location);
				return System.IO.File.Exists(System.IO.Path.Combine(dir, "12Harmony.dll"));
			}
			catch (Exception)
			{
				return false;
			}
		}

		internal static bool Handlers12()
		{
			if (_h12)
			{
				return true;
			}
			if (_handlers != null && _flag != null && _flag.On)
			{
				return Ensure12();
			}
			return Loaded12();
		}

		// True when 12Harmony is loaded; never loads it (for callers that run during a stage).
		internal static bool Loaded12()
		{
			if (_h12)
			{
				return true;
			}
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].GetName().Name == "12Harmony")
				{
					return true;
				}
			}
			return false;
		}

		// 12Harmony builds every later wrapper with the original's exception handlers (KB BaseModding: 12Harmony keeps handlers).
		private static bool Ensure12()
		{
			if (_h12)
			{
				return true;
			}
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].GetName().Name == "12Harmony")
				{
					// Another mod loaded it: it already builds the wrappers (GL-55).
					_h12 = true;
					return true;
				}
			}
			if (!_handlers.On || _h12Missing)
			{
				return false;
			}
			// Not every install has 12Harmony.dll next to 0Harmony.dll in Managed: without it these places stay unfixed, once logged.
			string dir = System.IO.Path.GetDirectoryName(typeof(HarmonyInstance).Assembly.Location);
			if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "12Harmony.dll")))
			{
				_h12Missing = true;
				N.ULog.Info(FastFixModule.Tag, "covariance: 12Harmony.dll is not in " + dir + ": places in methods with exception handlers stay unfixed");
				return false;
			}
			try
			{
				Assembly a = Assembly.Load("12Harmony");
				Type t = a.GetType("Harmony.HarmonyInstance", true);
				t.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { FastFixModule.HarmonyId + ".12" });
				_h12 = true;
				N.ULog.Warn(FastFixModule.Tag, "covariance: 12Harmony loaded; Harmony wrappers built from now on keep exception handlers");
				return true;
			}
			catch (Exception e)
			{
				_h12Missing = true;
				N.ULog.Warn(FastFixModule.Tag, "covariance: 12Harmony could not be loaded: " + e.Message);
				return false;
			}
		}
	}

	// ---------- 12Harmony hint ----------
	// Once, on the title screen after the installs finished: if fixes were skipped for lack of 12Harmony, NailUI.H12Hint.Show
	// (by reflection) lists them in the alert box; then FastFix.Hint12Harmony is saved false and it is never shown again.
	internal static class H12Hint
	{
		private static N.Flag _flag;
		private static bool _done;
		private static int _n;

		internal static void Tick()
		{
			if (_done || FastFixDriver.CovariancePending || FastFixDriver.ModNrePending)
			{
				return;
			}
			_n++;
			if (_n % 30 != 0)
			{
				return;
			}
			try
			{
				if (_flag == null)
				{
					_flag = N.Config.BindFlag("FastFix.Hint12Harmony", true, "Show once on the title screen which fixes are off because 12Harmony is not loaded; switched off after it was shown.");
				}
				if (!_flag.On || Covariance.H12Skipped.Count == 0)
				{
					_done = true;
					return;
				}
				string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
				if (scene == null || scene.IndexOf("Title", StringComparison.Ordinal) < 0)
				{
					return;
				}
				_done = true;
				MethodInfo show = null;
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length && object.ReferenceEquals(show, null); i++)
				{
					Type t = all[i].GetType("NailUI.H12Hint", false);
					if (!object.ReferenceEquals(t, null))
					{
						show = t.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string[]), typeof(bool) }, null);
					}
				}
				if (object.ReferenceEquals(show, null))
				{
					N.ULog.Warn(FastFixModule.Tag, "12harmony hint: NailUI.H12Hint not available; skipped fixes: " + string.Join(", ", Covariance.H12Skipped.ToArray()));
					return;
				}
				object r = show.Invoke(null, new object[] { Covariance.H12Skipped.ToArray(), !Covariance.H12DllPresent() });
				if (r is bool && (bool)r)
				{
					N.Config.SetFlag(_flag, false);
					N.ULog.Info(FastFixModule.Tag, "12harmony hint shown fixes=" + Covariance.H12Skipped.Count + " dll=" + Covariance.H12DllPresent());
				}
			}
			catch (Exception e)
			{
				_done = true;
				N.ULog.Error(FastFixModule.Tag, "12harmony hint failed: " + e);
			}
		}
	}

	// ---------- ModNreFixes ----------
	// One-off NullReferenceExceptions of mod creatures seen in the Binah runs (task 2026-11-08-mod-nre-fixes). None of the
	// patched methods has an exception handler (GL-53). Each mod's fix is declared only when that mod is loaded.
	//   Vanilla          CreatureModel.DeleteChildCreatureModel: a child suppressed twice (Eternal Flower's Die) is no longer
	//                    in the list; the second call returns instead of throwing.
	//   BlackBird, Pinocchio  MakeMovement / MakeMovementToPino run on the first escape tick, before OnViewInit set the anim
	//                    script: that tick is skipped and the child moves on the next one.
	//   RottenWhaleFall, DragonScale  OnViewInit: SetScript is skipped when unit.animTarget is null; the rest runs.
	//   Eternal Flower   the attack animation's Spine event handler (GetNearEnemy at stage end) runs inside try/catch, so
	//                    the Spine event queue of that frame goes on; logged once.
	//   Isomer           its damage effect is read from its own folder (the DLL has another folder name built in).
	// Round 2 (1.29): ToyFactory NoticeGuard logs the full exception; Invalidum department of a creature outside a passage and
	// its folder name; Pinocchio range check on a removed target; Lady in Rain drawing on a destroyed view; QiuNiu circle hit
	// on a unit its inverted check reads as a child; Mephisto (MiFe) buf on a non-agent. Transpiled methods with exception
	// handlers are patched only when 12Harmony is already loaded.
	public static class ModNreFixes
	{
		private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		private static bool _childDone;
		private static bool _blackBirdDone;
		private static bool _pinoDone;
		private static bool _boneFishDone;
		private static bool _dragonDone;
		private static bool _flowerDone;
		private static bool _isomerDone;
		private static bool _toyDone;
		private static bool _invSefiraDone;
		private static bool _invPathDone;
		private static bool _pinoRangeDone;
		private static bool _rainDone;
		private static bool _qiuDone;
		private static bool _niuDone;
		private static bool _hookahDone;
		private static bool _whaleDone;
		private static bool _charlotteDone;
		private static bool _kekesiDone;
		private static bool _puLaoDone;
		private static bool _pinoEscDone;
		private static bool _floodDone;
		private static bool _beastDone;
		private static bool _yaziDone;
		private static bool _bbDeathDone;
		private static bool _whaleRoamDone;
		private static bool _qianRuDone;
		private static bool _cauldronDone;
		private static bool _ribbonDone;
		private static bool _cloudDone;
		private static bool _yaziEscDone;
		private static bool _grunglyDone;
		private static bool _bbPassageDone;
		private static bool _childLogged;
		private static FieldInfo _blackBirdAnim;
		private static FieldInfo _pinoAnim;
		private static FieldInfo _flowerAnimator;
		private static MethodInfo _flowerAttack;
		private static string _isomerPath;
		private static bool _flowerKept;
		private static bool _flowerFailed;
		private static bool _moveFailed;
		private static bool _childFailed;
		private static bool _viewInitHit;
		private static FieldInfo _eventField;

		// True when every fix is installed, skipped by its flag, or the give-up frame came (absent mods are logged then).
		internal static bool TryInstall(bool last)
		{
			if (!_childDone)
			{
				_childDone = true;
				InstallChild();
				InstallChildElevator();
			}
			if (!_blackBirdDone)
			{
				_blackBirdDone = Mod("BlackBird_WhiteBird", "FastFix.BlackBirdMove", "White Bird and Black Bird: a black bird that escapes no longer throws on its first move. Read at game start.", last, InstallBlackBird);
			}
			if (!_pinoDone)
			{
				_pinoDone = Mod("Pinocchio", "FastFix.PinocchioMove", "LCPD Pinocchio: a puppet that escapes no longer throws on its first move. Read at game start.", last, InstallPino);
			}
			if (!_boneFishDone)
			{
				_boneFishDone = Mod("RottenWhaleFall", "FastFix.BoneFishView", "Rotten Whale Fall: a bone fish without its animation no longer throws when it appears. Read at game start.", last, InstallBoneFish);
			}
			if (!_dragonDone)
			{
				_dragonDone = Mod("DragonScaleMod", "FastFix.DragonScaleView", "Yin Yang Dragon Scale: the abnormality without its animation no longer throws when the facility loads. Read at game start.", last, InstallDragon);
			}
			if (!_flowerDone)
			{
				_flowerDone = Mod("Eternal Flower", "FastFix.EternalFlowerAttack", "Flower of Eternity: an attack of its flower that hits at the end of the day no longer throws and stops other animation events of that frame. Read at game start.", last, InstallFlower);
			}
			if (!_isomerDone)
			{
				_isomerDone = Mod("Isomer", "FastFix.IsomerEffect", "Isomer: its damage effect picture is read from the mod's own folder. Read at game start.", last, InstallIsomer);
			}
			if (!_toyDone)
			{
				_toyDone = Mod("ToyFactory", "FastFix.ToyFactoryLog", "ToyFactory: an exception its notice guard catches is written in full (with its stack) to ToyFactoryError.txt and once per kind to this log. Read at game start.", last, InstallToyLog);
			}
			if (!_invSefiraDone)
			{
				_invSefiraDone = Mod("Invalidum_MOD", "FastFix.InvalidumSefira", "Invalidum: lighting the departments back up no longer throws when a creature is outside every passage. Read at game start.", last, InstallInvalidumSefira);
			}
			if (!_invPathDone)
			{
				_invPathDone = Mod("Invalidum_MOD", "FastFix.InvalidumPath", "Invalidum: its sounds are read from the mod's own folder (the DLL has another folder name built in). Read at game start.", last, InstallInvalidumPath);
			}
			if (!_pinoRangeDone)
			{
				_pinoRangeDone = Mod("Pinocchio", "FastFix.PinocchioRange", "LCPD Pinocchio: an attack on a target removed at the end of the day no longer throws. Read at game start.", last, InstallPinoRange);
			}
			if (!_rainDone)
			{
				_rainDone = Mod("GirlInRain", "FastFix.LadyInRainDraw", "Lady in Rain: drawing stops when its view or painting tool is gone instead of throwing every frame. Read at game start.", last, InstallRain);
			}
			if (!_qiuDone)
			{
				_qiuDone = Mod("QiuNiu", "FastFix.QiuNiuCircle", "QiuNiu: its circle no longer throws on units that are not agents, officers, creatures or children. Read at game start.", last, InstallQiuNiu);
			}
			if (!_niuDone)
			{
				_niuDone = Mod("MiFe", "FastFix.MephistoBuf", "Mephisto (MiFe): its debuff on an officer or another non-agent no longer throws when that unit is hit. Read at game start.", last, InstallMephisto);
			}
			if (!_hookahDone)
			{
				_hookahDone = Mod("HookahCaterpillar", "FastFix.HookahCaterpillarAnim", "Hookah Caterpillar: its animation is found even when the mod list lookup fails, instead of the creature staying without animation. Read at game start.", last, InstallHookah);
			}
			if (!_whaleDone)
			{
				_whaleDone = Mod("RottenWhaleFall", "FastFix.WhaleTailPull", "Rotten Whale Fall: its tail skips the pull while the target is between rooms instead of throwing every frame. Read at game start.", last, InstallWhaleTail);
			}
			if (!_charlotteDone)
			{
				_charlotteDone = Mod("Charlotte", "FastFix.CharlotteDamage", "Charlotte: taking damage between rooms no longer throws. Read at game start.", last, InstallCharlotte);
			}
			if (!_kekesiDone)
			{
				_kekesiDone = Mod("KekesiWolf", "FastFix.KekesiWolfHit", "Alleyway Watchdog: its area hit no longer stops when it kills someone in the room. Read at game start.", last, InstallKekesi);
			}
			if (!_puLaoDone)
			{
				_puLaoDone = Mod("PuLao", "FastFix.PuLaoChain", "PuLao: its attack near a music creature no longer recurses without end and closes the game. Read at game start.", last, InstallPuLao);
			}
			if (!_pinoEscDone)
			{
				_pinoEscDone = Mod("Pinocchio", "FastFix.PinocchioEscape", "LCPD Pinocchio: picking its defence target no longer throws every tick, chasing a dead target no longer throws. Read at game start.", last, InstallPinoEscape);
			}
			if (!_floodDone)
			{
				_floodDone = Mod("Invalidum_MOD", "FastFix.InvalidumFlood", "Invalidum: its flooded passage no longer stops when a unit in it changes. Read at game start.", last, InstallFlood);
			}
			if (!_beastDone)
			{
				_beastDone = Mod("HomelessBeast", "FastFix.HomelessBeastHit", "Homeless Beast: its department-wide hit no longer stops when it kills someone. Read at game start.", last, InstallBeast);
			}
			if (!_yaziDone)
			{
				_yaziDone = Mod("YaZi", "FastFix.YaZiDeath", "YaZi: suppression without a death animation no longer throws every frame. Read at game start.", last, InstallYaZiDeath);
			}
			if (!_bbDeathDone)
			{
				_bbDeathDone = Mod("BlackBird_WhiteBird", "FastFix.BlackBirdDeath", "White Bird and Black Bird: suppression of the black bird without a death animation no longer throws every frame. Read at game start.", last, InstallBlackBirdDeath);
			}
			if (!_whaleRoamDone)
			{
				_whaleRoamDone = Mod("RottenWhaleFall", "FastFix.RottenWhaleRoam", "Rotten Whale Fall: without a target it keeps one roaming destination instead of picking a new one every tick and turning back and forth. Read at game start.", last, InstallWhaleRoam);
			}
			if (!_qianRuDone)
			{
				_qianRuDone = Mod("QianRu", "FastFix.QianRuEquip", "QianRu: taking back its EGO from the inventory no longer stops after the first removed item. Read at game start.", last, InstallQianRuEquip);
			}
			if (!_cauldronDone)
			{
				_cauldronDone = Mod("Cauldron", "FastFix.CauldronLure", "Cauldron: luring when every candidate is dead no longer throws; the lure is skipped. Read at game start.", last, InstallCauldronLure);
			}
			if (!_ribbonDone)
			{
				_ribbonDone = Mod("Candeloro", "FastFix.CandeloroRibbon", "Candeloro: a ribbon whose end was destroyed no longer throws every frame. Read at game start.", last, InstallCandeloroRibbon);
			}
			if (!_cloudDone)
			{
				_cloudDone = Mod("Cloud9", "FastFix.Cloud9Panic", "Cloud9: an agent panicking while the escaped cloud is between rooms no longer throws. Read at game start.", last, InstallCloud9Panic);
			}
			if (!_yaziEscDone)
			{
				_yaziEscDone = Mod("YaZi", "FastFix.YaZiEscapeUI", "YaZi: a breach before its request window was ever opened no longer throws. Read at game start.", last, InstallYaZiEscapeUI);
			}
			if (!_grunglyDone)
			{
				_grunglyDone = Mod("Grungly", "FastFix.GrunglyLure", "Grungly: a lure whose target no longer qualifies, or with no agent left to lure, no longer throws every tick; the lure stops. Read at game start.", last, InstallGrunglyLure);
			}
			if (!_bbPassageDone)
			{
				_bbPassageDone = Mod("BlackBird_WhiteBird", "FastFix.BlackBirdPassageHit", "Black Bird: its hit on the hallway no longer stops when it kills someone there. Needs 12Harmony. Read at game start.", last, InstallBlackBirdPassageHit);
			}
			return _blackBirdDone && _pinoDone && _boneFishDone && _dragonDone && _flowerDone && _isomerDone
				&& _toyDone && _invSefiraDone && _invPathDone && _pinoRangeDone && _rainDone && _qiuDone && _niuDone && _hookahDone
				&& _whaleDone && _charlotteDone && _kekesiDone && _puLaoDone
				&& _pinoEscDone && _floodDone && _beastDone && _yaziDone && _bbDeathDone && _whaleRoamDone
				&& _qianRuDone && _cauldronDone && _ribbonDone && _cloudDone && _yaziEscDone && _grunglyDone && _bbPassageDone;
		}

		private delegate void Installer(Assembly asm);

		private static bool Mod(string name, string key, string desc, bool last, Installer install)
		{
			Assembly found = WhiteRabbitFix.FindAssembly(name);
			if (object.ReferenceEquals(found, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, key + " not applied: " + name + " not loaded");
				}
				return last;
			}
			if (!N.Config.BindFlag(key, true, desc).On)
			{
				return true;
			}
			try
			{
				install(found);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
			return true;
		}

		private static MethodInfo Own(string name)
		{
			return typeof(ModNreFixes).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
		}

		private static MethodInfo Find(Assembly asm, string type, string method, Type[] args)
		{
			Type t = asm.GetType(type);
			if (object.ReferenceEquals(t, null))
			{
				return null;
			}
			return t.GetMethod(method, All, null, args, null);
		}

		private static void Patched(string key, MethodInfo m)
		{
			N.ULog.Info(FastFixModule.Tag, key + " installed: " + m.DeclaringType.FullName + "." + m.Name);
		}

		private static void Missing(string key, string what)
		{
			N.ULog.Warn(FastFixModule.Tag, key + " not applied: " + what + " not found");
		}

		// ----- vanilla: second suppression of a child -----

		private static void InstallChild()
		{
			if (!N.Config.BindFlag("FastFix.ChildDeleteTwice", true, "A child creature that is suppressed twice (some mods do it) no longer throws the second time. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(CreatureModel).GetMethod("DeleteChildCreatureModel", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(long) }, null);
				if (object.ReferenceEquals(m, null))
				{
					Missing("FastFix.ChildDeleteTwice", "CreatureModel.DeleteChildCreatureModel");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("ChildPrefix")), null);
				Patched("FastFix.ChildDeleteTwice", m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "FastFix.ChildDeleteTwice install failed: " + e);
			}
		}

		public static bool ChildPrefix(CreatureModel __instance, long instId)
		{
			try
			{
				if (!object.ReferenceEquals(__instance.GetChildCreatureModel(instId), null))
				{
					return true;
				}
				if (!_childLogged)
				{
					_childLogged = true;
					N.ULog.Info(FastFixModule.Tag, "child creature " + instId + " of creature " + __instance.metadataId + " deleted twice: second delete skipped (logged once)");
				}
				return false;
			}
			catch (Exception e)
			{
				if (!_childFailed)
				{
					_childFailed = true;
					N.ULog.Error(FastFixModule.Tag, "child delete fix failed, vanilla kept (logged once): " + e);
				}
				return true;
			}
		}

		// ----- vanilla: a child creature whose path ends on an elevator -----

		private static bool _childStuckLogged;

		// CreatureModel.OnFixedUpdate catches MovableElevatorStuckException; ChildCreatureModel.OnFixedUpdate does not.
		// OnFixedUpdate has no exception handlers: a transpiler routes its two ProcessMoveNode calls through ChildMoveNode.
		private static void InstallChildElevator()
		{
			if (!N.Config.BindFlag("FastFix.ChildElevatorStuck", true, "A child creature whose path ends on an elevator no longer throws out of its update; it is handled as for other creatures. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(ChildCreatureModel).GetMethod("OnFixedUpdate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.DeclaringType, typeof(ChildCreatureModel)))
				{
					Missing("FastFix.ChildElevatorStuck", "ChildCreatureModel.OnFixedUpdate");
					return;
				}
				_hits = 0;
				FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("ChildElevatorTranspiler")));
				if (_hits != 2)
				{
					N.ULog.Warn(FastFixModule.Tag, "FastFix.ChildElevatorStuck: " + _hits + " move calls replaced, expected 2");
				}
				Report("FastFix.ChildElevatorStuck", m, "MovableObjectNode.ProcessMoveNode call");
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "FastFix.ChildElevatorStuck install failed: " + e);
			}
		}

		// callvirt MovableObjectNode.ProcessMoveNode(float) -> ldarg.0; call ChildMoveNode(node, movement, this).
		public static IEnumerable<CodeInstruction> ChildElevatorTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("ChildMoveNode");
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && !object.ReferenceEquals(c, null)
					&& string.Equals(c.Name, "ProcessMoveNode", StringComparison.Ordinal) && object.ReferenceEquals(c.DeclaringType, typeof(MovableObjectNode))
					&& c.GetParameters().Length == 1 && object.ReferenceEquals(c.GetParameters()[0].ParameterType, typeof(float)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = to;
					CodeInstruction ld = new CodeInstruction(OpCodes.Ldarg_0);
					ld.labels.AddRange(list[i].labels);
					list[i].labels.Clear();
					list.Insert(i, ld);
					i++;
					_hits++;
				}
			}
			return list;
		}

		// Same handling as CreatureModel.OnFixedUpdate: the node already stopped moving; the creature script is told.
		public static void ChildMoveNode(MovableObjectNode node, float movement, ChildCreatureModel child)
		{
			try
			{
				node.ProcessMoveNode(movement);
			}
			catch (MovableObjectNode.MovableElevatorStuckException)
			{
				if (!_childStuckLogged)
				{
					_childStuckLogged = true;
					N.ULog.Info(FastFixModule.Tag, "child creature " + child.metadataId + " stuck at an elevator: handled as for creatures (vanilla: throws out of its update; logged once)");
				}
				if (child.script != null)
				{
					child.script.OnElevatorStuck();
				}
			}
		}

		// ----- BlackBird, Pinocchio: anim script not set yet -----

		private static void InstallBlackBird(Assembly asm)
		{
			_blackBirdAnim = InstallMove(asm, "BlackBird_WhiteBird.BlackBirdCreature", "_animscript", "FastFix.BlackBirdMove", "BlackBirdPrefix",
				new string[] { "MakeMovement", "MakeMovementToTarget" });
		}

		public static bool BlackBirdPrefix(object __instance)
		{
			return AnimSet(_blackBirdAnim, __instance);
		}

		private static void InstallPino(Assembly asm)
		{
			_pinoAnim = InstallMove(asm, "Pinocchio.PinoAgent", "animscript", "FastFix.PinocchioMove", "PinoPrefix",
				new string[] { "MakeMovement", "MakeMovementToPino", "MakeMovementToTarget" });
		}

		public static bool PinoPrefix(object __instance)
		{
			return AnimSet(_pinoAnim, __instance);
		}

		// Patches each movement method that calls animscript.OnMove(); returns the anim field, or null when nothing was patched.
		private static FieldInfo InstallMove(Assembly asm, string type, string field, string key, string prefix, string[] methods)
		{
			Type t = asm.GetType(type);
			FieldInfo f = object.ReferenceEquals(t, null) ? null : t.GetField(field, All);
			if (object.ReferenceEquals(f, null))
			{
				Missing(key, type + "." + field);
				return null;
			}
			for (int i = 0; i < methods.Length; i++)
			{
				MethodInfo m = t.GetMethod(methods[i], All, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(m, null))
				{
					Missing(key, type + "." + methods[i]);
					continue;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own(prefix)), null);
				Patched(key, m);
			}
			return f;
		}

		// The move waits until the view set the anim script (one frame after the model exists).
		private static bool AnimSet(FieldInfo f, object instance)
		{
			try
			{
				return f.GetValue(instance) != null;
			}
			catch (Exception e)
			{
				if (!_moveFailed)
				{
					_moveFailed = true;
					N.ULog.Error(FastFixModule.Tag, "movement fix failed, vanilla kept (logged once): " + e);
				}
				return true;
			}
		}

		// ----- RottenWhaleFall, DragonScale: OnViewInit without animTarget -----

		private static void InstallBoneFish(Assembly asm)
		{
			InstallViewInit(asm, "RottenWhaleFall.BoneFish", "FastFix.BoneFishView");
		}

		private static void InstallDragon(Assembly asm)
		{
			InstallViewInit(asm, "DragonScaleMod.DragonScale", "FastFix.DragonScaleView");
		}

		private static void InstallViewInit(Assembly asm, string type, string key)
		{
			MethodInfo m = Find(asm, type, "OnViewInit", new Type[] { typeof(CreatureUnit) });
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, type + ".OnViewInit");
				return;
			}
			_viewInitHit = false;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("ViewInitTranspiler")));
			if (_viewInitHit)
			{
				Patched(key, m);
			}
			else
			{
				N.ULog.Warn(FastFixModule.Tag, key + " not applied: SetScript call not found in " + type + ".OnViewInit, method left unchanged");
			}
		}

		// ldarg.0; ldfld anim; ldarg.0; callvirt anim.SetScript(this) -> the call runs only when anim is not null.
		// A body without the pattern stays as it is.
		public static IEnumerable<CodeInstruction> ViewInitTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 1; i + 2 < list.Count; i++)
			{
				MethodInfo call = list[i + 2].operand as MethodInfo;
				FieldInfo field = list[i].operand as FieldInfo;
				if (list[i].opcode != OpCodes.Ldfld || object.ReferenceEquals(field, null) || list[i + 1].opcode != OpCodes.Ldarg_0
					|| list[i + 2].opcode != OpCodes.Callvirt || object.ReferenceEquals(call, null) || call.Name != "SetScript"
					|| !object.ReferenceEquals(call.ReturnType, typeof(void)) || call.GetParameters().Length != 1 || i + 3 >= list.Count)
				{
					continue;
				}
				Label ok = il.DefineLabel();
				Label end = il.DefineLabel();
				list[i + 1].labels.Add(ok);
				list[i + 3].labels.Add(end);
				list.InsertRange(i + 1, new CodeInstruction[]
				{
					new CodeInstruction(OpCodes.Dup, null),
					new CodeInstruction(OpCodes.Brtrue, ok),
					new CodeInstruction(OpCodes.Pop, null),
					new CodeInstruction(OpCodes.Br, end)
				});
				_viewInitHit = true;
				return list;
			}
			return list;
		}

		// ----- Eternal Flower: attack event at stage end -----

		private static void InstallFlower(Assembly asm)
		{
			MethodInfo m = Find(asm, "Eternal_Flower.Eternal_FlowerChildAnim", "Attack", Type.EmptyTypes);
			_flowerAttack = Find(asm, "Eternal_Flower.Eternal_FlowerChildAnim", "Attack", new Type[] { typeof(Spine.TrackEntry), typeof(Spine.Event) });
			_flowerAnimator = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetField("animator", All);
			// Backing field of the field-like event TrackEntry.Event: without it removal of the mod's handler cannot be checked.
			_eventField = typeof(Spine.TrackEntry).GetField("Event", BindingFlags.NonPublic | BindingFlags.Instance);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_flowerAttack, null) || object.ReferenceEquals(_flowerAnimator, null)
				|| object.ReferenceEquals(_eventField, null))
			{
				Missing("FastFix.EternalFlowerAttack", "Eternal_FlowerChildAnim.Attack, animator or TrackEntry.Event field");
				return;
			}
			FastFixModule.Harmony.Patch(m, null, new HarmonyMethod(Own("FlowerPostfix")));
			Patched("FastFix.EternalFlowerAttack", m);
		}

		// Attack() just set track 0 and subscribed Attack(TrackEntry, Event): swap that handler for a guarded one.
		public static void FlowerPostfix(object __instance)
		{
			Spine.TrackEntry entry = null;
			Spine.AnimationState.TrackEntryEventDelegate handler = null;
			bool removed = false;
			try
			{
				Spine.Unity.SkeletonAnimation animator = _flowerAnimator.GetValue(__instance) as Spine.Unity.SkeletonAnimation;
				if (animator == null || animator.AnimationState == null)
				{
					return;
				}
				entry = animator.AnimationState.GetCurrent(0);
				if (entry == null || entry.Animation == null || entry.Animation.Name == null || !entry.Animation.Name.StartsWith("Attack_", StringComparison.Ordinal))
				{
					return;
				}
				handler = (Spine.AnimationState.TrackEntryEventDelegate)Delegate.CreateDelegate(typeof(Spine.AnimationState.TrackEntryEventDelegate), __instance, _flowerAttack);
				Spine.AnimationState.TrackEntryEventDelegate guarded = new GuardedEvent(handler).Invoke;
				entry.Event -= handler;
				removed = true;
				// Add the guarded copy only when the mod's own handler is gone, or the attack would hit twice.
				if (HasHandler(entry, __instance))
				{
					removed = false;
					if (!_flowerKept)
					{
						_flowerKept = true;
						N.ULog.Warn(FastFixModule.Tag, "eternal flower fix: the attack handler could not be removed, left as it is (logged once)");
					}
					return;
				}
				entry.Event += guarded;
			}
			catch (Exception e)
			{
				if (removed && entry != null && handler != null)
				{
					entry.Event += handler;
				}
				if (!_flowerFailed)
				{
					_flowerFailed = true;
					N.ULog.Error(FastFixModule.Tag, "eternal flower fix failed, handler kept (logged once): " + e);
				}
			}
		}

		private static bool HasHandler(Spine.TrackEntry entry, object target)
		{
			Delegate d = _eventField.GetValue(entry) as Delegate;
			if (d == null)
			{
				return false;
			}
			Delegate[] list = d.GetInvocationList();
			for (int i = 0; i < list.Length; i++)
			{
				if (object.ReferenceEquals(list[i].Target, target) && list[i].Method.MethodHandle.Value.Equals(_flowerAttack.MethodHandle.Value))
				{
					return true;
				}
			}
			return false;
		}

		// ----- ToyFactory: NoticeGuard logs the exception message only -----

		private static readonly Dictionary<string, bool> _noticeKeys = new Dictionary<string, bool>(StringComparer.Ordinal);
		private static bool _noticeFailed;

		private static void InstallToyLog(Assembly asm)
		{
			MethodInfo m = Find(asm, "ToyFactory.Harmony_Patch", "Notice_Send_Prefix", new Type[] { typeof(string), typeof(object[]) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.ToyFactoryLog", "ToyFactory.Harmony_Patch.Notice_Send_Prefix");
				return;
			}
			// The method has try/catch: under 0Harmony the wrapper would lose them (GL-53).
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.ToyFactoryLog", "Notice_Send_Prefix has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("ToyLogTranspiler")));
			Report("FastFix.ToyFactoryLog", m, "Exception.Message call");
		}

		private static int _hits;

		private static void Report(string key, MethodInfo m, string what)
		{
			if (_hits > 0)
			{
				Patched(key, m);
			}
			else
			{
				N.ULog.Warn(FastFixModule.Tag, key + " not applied: " + what + " not found in " + m.DeclaringType.FullName + "." + m.Name + ", method left unchanged");
			}
		}

		// ex.Message -> NoticeText(ex): the full exception text goes to ToyFactoryError.txt and once per kind to nail.log.
		public static IEnumerable<CodeInstruction> ToyLogTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo text = typeof(ModNreFixes).GetMethod("NoticeText", BindingFlags.Public | BindingFlags.Static);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo call = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && !object.ReferenceEquals(call, null)
					&& call.Name == "get_Message" && object.ReferenceEquals(call.DeclaringType, typeof(Exception)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = text;
					_hits++;
				}
			}
			return list;
		}

		public static string NoticeText(Exception ex)
		{
			if (ex == null)
			{
				return "";
			}
			string full = ex.ToString();
			try
			{
				string top = ex.StackTrace;
				if (top != null)
				{
					int nl = top.IndexOf('\n');
					top = nl > 0 ? top.Substring(0, nl) : top;
				}
				string key = ex.GetType().FullName + "|" + top;
				if (_noticeKeys.Count < 64 && !_noticeKeys.ContainsKey(key))
				{
					_noticeKeys[key] = true;
					N.ULog.Warn(FastFixModule.Tag, "ToyFactory NoticeGuard isolated an exception (once per kind): " + full);
				}
			}
			catch (Exception e)
			{
				if (!_noticeFailed)
				{
					_noticeFailed = true;
					N.ULog.Error(FastFixModule.Tag, "toyfactory log fix failed (logged once): " + e);
				}
			}
			return full;
		}

		// ----- Invalidum: escaped creature without a passage, wrong folder name -----

		private static FieldInfo _invDrugged;
		private static MethodInfo _invDestroy;
		private static MethodInfo _invEscaped;
		private static bool _invFailed;

		// MakeSefiraBrightly has foreach handlers: a transpiler needs 12Harmony (GL-53), a replacing Prefix does not
		// (the original body never runs). The Prefix repeats its steps with null checks.
		private static void InstallInvalidumSefira(Assembly asm)
		{
			MethodInfo m = Find(asm, "Invalidum_MOD.Invalidum", "MakeSefiraBrightly", new Type[] { typeof(Sefira) });
			Type flooded = asm.GetType("Invalidum_MOD.Invalidum+PassageFlooded");
			_invDrugged = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetField("passagesDrugged", All);
			_invEscaped = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetMethod("GetEscapedCreatures", All, null, Type.EmptyTypes, null);
			_invDestroy = object.ReferenceEquals(flooded, null) ? null : flooded.GetMethod("Destroy", All, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(bool)) || object.ReferenceEquals(_invDrugged, null)
				|| object.ReferenceEquals(_invEscaped, null) || !_invEscaped.IsStatic || object.ReferenceEquals(_invDestroy, null))
			{
				Missing("FastFix.InvalidumSefira", "Invalidum.MakeSefiraBrightly, passagesDrugged, GetEscapedCreatures or PassageFlooded.Destroy");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("InvSefiraPrefix")), null);
			Patched("FastFix.InvalidumSefira", m);
		}

		public static bool InvSefiraPrefix(object __instance, Sefira targetSefira, ref bool __result)
		{
			bool sent = false;
			try
			{
				System.Collections.IList drugged = _invDrugged.GetValue(__instance) as System.Collections.IList;
				if (drugged != null)
				{
					for (int i = 0; i < drugged.Count; i++)
					{
						if (drugged[i] != null)
						{
							_invDestroy.Invoke(drugged[i], null);
						}
					}
					drugged.Clear();
				}
				List<CreatureModel> escaped = _invEscaped.Invoke(null, null) as List<CreatureModel>;
				if (escaped != null)
				{
					for (int i = 0; i < escaped.Count; i++)
					{
						CreatureModel c = escaped[i];
						MovableObjectNode node = c == null ? null : c.GetMovableNode();
						// A creature outside every passage (stage end, suppression) belongs to no department.
						PassageObjectModel passage = node == null ? null : node.currentPassage;
						if (passage != null && passage.GetSefiraEnum() == targetSefira.sefiraEnum && c.metadataId == 100008L)
						{
							__result = false;
							return false;
						}
					}
				}
				sent = true;
				for (int i = 0; i < targetSefira.passageList.Count; i++)
				{
					Notice.instance.Send(NoticeName.PassageWhitle, new object[] { targetSefira.passageList[i] });
				}
				__result = true;
				return false;
			}
			catch (Exception e)
			{
				Fail(ref _invFailed, "invalidum department fix", e);
				if (sent)
				{
					// A notice listener threw: the original would send the same notices again.
					__result = true;
					return false;
				}
				return true;
			}
		}


		private const string InvalidumLiteral = "/BaseMods/Khruter_SpiceMayor_Invalidum_MOD/";
		private static string _invalidumFolder;

		private static void InstallInvalidumPath(Assembly asm)
		{
			Type t = asm.GetType("Invalidum_MOD.Invalidum");
			if (object.ReferenceEquals(t, null) || string.IsNullOrEmpty(asm.Location))
			{
				Missing("FastFix.InvalidumPath", "Invalidum_MOD.Invalidum or its location");
				return;
			}
			string data = Application.dataPath.Replace('\\', '/');
			string dir = System.IO.Path.GetDirectoryName(asm.Location).Replace('\\', '/');
			if (!dir.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase) || !System.IO.Directory.Exists(dir + "/resources"))
			{
				Missing("FastFix.InvalidumPath", "Invalidum resources folder under " + data);
				return;
			}
			string rel = dir.Substring(data.Length) + "/";
			if (string.Equals(rel, InvalidumLiteral, StringComparison.OrdinalIgnoreCase))
			{
				N.ULog.Info(FastFixModule.Tag, "FastFix.InvalidumPath not needed: the folder name matches");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.InvalidumPath", "the methods have exception handlers and 12Harmony is not loaded");
				return;
			}
			_invalidumFolder = rel;
			MethodInfo[] ms = t.GetMethods(All);
			for (int i = 0; i < ms.Length; i++)
			{
				if (!HasLiteral(ms[i], InvalidumLiteral))
				{
					continue;
				}
				try
				{
					FastFixModule.Harmony.Patch(ms[i], null, null, new HarmonyMethod(Own("InvalidumPathTranspiler")));
					Patched("FastFix.InvalidumPath", ms[i]);
				}
				catch (Exception e)
				{
					N.ULog.Error(FastFixModule.Tag, "FastFix.InvalidumPath install failed for " + ms[i].Name + ": " + e);
				}
			}
		}

		// Reads the method's IL for a ldstr whose text contains the literal (install time only).
		private static bool HasLiteral(MethodInfo m, string literal)
		{
			MethodBody body = m.GetMethodBody();
			if (body == null)
			{
				return false;
			}
			byte[] il = body.GetILAsByteArray();
			Module mod = m.Module;
			for (int i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != 0x72)
				{
					continue;
				}
				int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
				if ((token >> 24) != 0x70)
				{
					continue;
				}
				try
				{
					string s = mod.ResolveString(token);
					if (s != null && s.IndexOf(literal, StringComparison.Ordinal) >= 0)
					{
						return true;
					}
				}
				catch (ArgumentException)
				{
					// 0x72 inside another instruction's operand: not a string token, scan on.
				}
			}
			return false;
		}

		public static IEnumerable<CodeInstruction> InvalidumPathTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				string s = list[i].operand as string;
				if (list[i].opcode == OpCodes.Ldstr && s != null && s.IndexOf(InvalidumLiteral, StringComparison.Ordinal) >= 0)
				{
					list[i].operand = s.Replace(InvalidumLiteral, _invalidumFolder);
				}
			}
			return list;
		}

		// ----- Pinocchio: target removed at stage end -----

		private static void InstallPinoRange(Assembly asm)
		{
			// GetDistance, not IsInRange: IsInRange is about 16 IL bytes and Mono may inline it into its callers.
			MethodInfo m = Find(asm, "Pinocchio.Pinocchio", "GetDistance", new Type[] { typeof(UnitModel) });
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(float)))
			{
				Missing("FastFix.PinocchioRange", "Pinocchio.GetDistance");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("PinoRangePrefix")), null);
			Patched("FastFix.PinocchioRange", m);
		}

		// A removed target is out of every range: IsInRange returns false.
		public static bool PinoRangePrefix(UnitModel target, ref float __result)
		{
			try
			{
				if (target == null || target.GetMovableNode() == null)
				{
					__result = float.MaxValue;
					return false;
				}
				return true;
			}
			catch (Exception e)
			{
				Fail(ref _pinoRangeFailed, "pinocchio range fix", e);
				return true;
			}
		}

		private static bool _pinoRangeFailed;

		private static void Fail(ref bool flag, string what, Exception e)
		{
			if (!flag)
			{
				flag = true;
				N.ULog.Error(FastFixModule.Tag, what + " failed, vanilla kept (logged once): " + e);
			}
		}

		// ----- Lady in Rain: endless Drawing loop on a destroyed view -----

		private static FieldInfo _rainScript;
		private static FieldInfo _rainAnimator;
		private static FieldInfo _rainTool;
		private static bool _rainFailed;

		private static void InstallRain(Assembly asm)
		{
			MethodInfo m = Find(asm, "GirlInRain.GirlInRainAnim", "Drawing", Type.EmptyTypes);
			Type owner = asm.GetType("GirlInRain.GirlInRain");
			_rainScript = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetField("_script", All);
			_rainAnimator = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetField("animator", All);
			_rainTool = object.ReferenceEquals(owner, null) ? null : owner.GetField("paintingTool", All);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_rainScript, null) || object.ReferenceEquals(_rainAnimator, null) || object.ReferenceEquals(_rainTool, null))
			{
				Missing("FastFix.LadyInRainDraw", "GirlInRainAnim.Drawing, _script, animator or GirlInRain.paintingTool");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("RainPrefix")), null);
			Patched("FastFix.LadyInRainDraw", m);
		}

		public static bool RainPrefix(object __instance)
		{
			try
			{
				object script = _rainScript.GetValue(__instance);
				if (script == null)
				{
					return false;
				}
				UnityEngine.Object tool = _rainTool.GetValue(script) as UnityEngine.Object;
				UnityEngine.Object anim = _rainAnimator.GetValue(__instance) as UnityEngine.Object;
				// Unity's == treats a destroyed object as null.
				return tool != null && anim != null;
			}
			catch (Exception e)
			{
				Fail(ref _rainFailed, "lady in rain fix", e);
				return true;
			}
		}

		// ----- QiuNiu: HuQin circle hits a unit of no known kind -----

		private static bool _qiuFailed;
		private static bool _qiuLogged;

		private static void InstallQiuNiu(Assembly asm)
		{
			MethodInfo m = Find(asm, "QiuNiu.HuQin", "CollisionCheck", new Type[] { typeof(Collider2D) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.QiuNiuCircle", "HuQin.CollisionCheck");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("QiuNiuPrefix")), null);
			Patched("FastFix.QiuNiuCircle", m);
		}

		// The original reads ChildCreatureUnit.Model for every unit that is no worker, creature, child or rabbit (inverted check):
		// skip exactly the cases that throw, run the original otherwise.
		public static bool QiuNiuPrefix(Collider2D collision)
		{
			try
			{
				if (collision == null)
				{
					return false;
				}
				UnitMouseEventTarget t = collision.GetComponent<UnitMouseEventTarget>();
				if (t == null || t.target == null)
				{
					return true;
				}
				MonoBehaviour mb = t.target;
				WorkerUnit w = mb.GetComponent<WorkerUnit>();
				if (w != null)
				{
					return w.workerModel != null;
				}
				CreatureUnit c = mb.GetComponent<CreatureUnit>();
				if (c != null)
				{
					return c.model != null;
				}
				if (mb.GetComponent<ChildCreatureUnit>() != null)
				{
					return true;
				}
				RabbitUnit r = mb.GetComponent<RabbitUnit>();
				if (r != null)
				{
					return r.model != null;
				}
				if (!_qiuLogged)
				{
					_qiuLogged = true;
					N.ULog.Info(FastFixModule.Tag, "qiuniu circle: hit on " + mb.GetType().Name + " skipped (the mod would throw; logged once)");
				}
				return false;
			}
			catch (Exception e)
			{
				Fail(ref _qiuFailed, "qiuniu circle fix", e);
				return true;
			}
		}

		// ----- Mephisto: NiuBibuf on a unit that is not an agent -----

		private static FieldInfo _niuFlag2;
		private static bool _niuFailed;

		private static void InstallMephisto(Assembly asm)
		{
			MethodInfo m = Find(asm, "MiFe.NiuBibuf", "OnTakeDamage", new Type[] { typeof(UnitModel), typeof(DamageInfo) });
			_niuFlag2 = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetField("flag2", All);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_niuFlag2, null) || !object.ReferenceEquals(_niuFlag2.FieldType, typeof(bool)))
			{
				Missing("FastFix.MephistoBuf", "NiuBibuf.OnTakeDamage or flag2");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("NiuPrefix")), new HarmonyMethod(Own("NiuPostfix")));
			Patched("FastFix.MephistoBuf", m);
		}

		// Its agent-only branch (panic, animator) runs only while flag2 is set: for officers and other units it is cleared for
		// this call only and restored in the postfix (FixedUpdate reads flag2 too).
		public static bool NiuPrefix(UnitBuf __instance, DamageInfo damageInfo, out bool __state)
		{
			__state = false;
			try
			{
				// A null damageInfo or model throws in the original before the postfix could restore flag2: leave those to vanilla.
				if (damageInfo != null && __instance.model != null && !(__instance.model is AgentModel) && (bool)_niuFlag2.GetValue(__instance))
				{
					_niuFlag2.SetValue(__instance, false);
					__state = true;
				}
			}
			catch (Exception e)
			{
				Fail(ref _niuFailed, "mephisto buf fix", e);
			}
			return true;
		}

		public static void NiuPostfix(UnitBuf __instance, bool __state)
		{
			if (!__state)
			{
				return;
			}
			try
			{
				_niuFlag2.SetValue(__instance, true);
			}
			catch (Exception e)
			{
				Fail(ref _niuFailed, "mephisto buf fix", e);
			}
		}

		// ----- HookahCaterpillar: GetAnimAsset finds its folder through Add_On.instance.ModList -----

		private static System.IO.DirectoryInfo _hookahDir;
		private static bool _hookahLogged;

		// GetAnimAsset has a foreach (GL-53): the transpiler needs 12Harmony.
		private static void InstallHookah(Assembly asm)
		{
			MethodInfo m = Find(asm, "HookahCaterpillar.HookahCaterpillar", "GetAnimAsset", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.HookahCaterpillarAnim", "HookahCaterpillar.GetAnimAsset");
				return;
			}
			MethodBody body = m.GetMethodBody();
			if (body != null && body.ExceptionHandlingClauses.Count > 0 && !Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.HookahCaterpillarAnim", "GetAnimAsset has exception handlers and 12Harmony is not loaded");
				return;
			}
			try
			{
				_hookahDir = HookahFolder(asm);
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "hookah caterpillar: folder search failed: " + e.Message);
			}
			_hookahList = 0;
			_hookahPath = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("HookahTranspiler")));
			if (_hookahList > 0 && _hookahPath > 0)
			{
				Patched("FastFix.HookahCaterpillarAnim", m);
			}
			else
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.HookahCaterpillarAnim partly applied: ModList reads replaced " + _hookahList + ", modpath reads replaced " + _hookahPath);
			}
		}

		private static int _hookahList;
		private static int _hookahPath;

		// The folder Add_On loaded the mod from: the assembly's folder or its parent (dlls/); else the first BaseMods folder with the animation.
		private static System.IO.DirectoryInfo HookahFolder(Assembly asm)
		{
			const string anim = "CreatureAnimation/HookahCaterpillarAnim";
			if (!string.IsNullOrEmpty(asm.Location))
			{
				System.IO.DirectoryInfo d = new System.IO.FileInfo(asm.Location).Directory;
				for (int i = 0; i < 2 && d != null; i++)
				{
					if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, anim)))
					{
						return d;
					}
					d = d.Parent;
				}
			}
			string root = System.IO.Path.Combine(Application.dataPath, "BaseMods");
			if (System.IO.Directory.Exists(root))
			{
				string[] dirs = System.IO.Directory.GetDirectories(root);
				for (int i = 0; i < dirs.Length; i++)
				{
					if (System.IO.Directory.Exists(System.IO.Path.Combine(dirs[i], anim)))
					{
						return new System.IO.DirectoryInfo(dirs[i]);
					}
				}
			}
			return null;
		}

		// ldfld Add_On.ModList -> HookahList(Add_On); ldfld ModInfo.modpath -> HookahPath(ModInfo).
		public static IEnumerable<CodeInstruction> HookahTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo ofList = typeof(ModNreFixes).GetMethod("HookahList", BindingFlags.Public | BindingFlags.Static);
			MethodInfo ofPath = typeof(ModNreFixes).GetMethod("HookahPath", BindingFlags.Public | BindingFlags.Static);
			for (int i = 0; i < list.Count; i++)
			{
				FieldInfo f = list[i].operand as FieldInfo;
				if (list[i].opcode != OpCodes.Ldfld || object.ReferenceEquals(f, null))
				{
					continue;
				}
				if (f.Name == "ModList" && object.ReferenceEquals(f.DeclaringType, typeof(Add_On)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = ofList;
					_hookahList++;
				}
				else if (f.Name == "modpath" && object.ReferenceEquals(f.DeclaringType, typeof(ModInfo)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = ofPath;
					_hookahPath++;
				}
			}
			return list;
		}

		public static List<ModInfo> HookahList(Add_On a)
		{
			if (a == null || a.ModList == null)
			{
				HookahNote(a == null ? "Add_On.instance is null" : "Add_On.ModList is null");
				return new List<ModInfo>();
			}
			return a.ModList;
		}

		public static System.IO.DirectoryInfo HookahPath(ModInfo info)
		{
			if (info != null && info.modpath != null)
			{
				return info.modpath;
			}
			HookahNote(info == null ? "no mod with id HookahCaterpillarMod in Add_On.ModList" : "its ModInfo.modpath is null");
			return _hookahDir;
		}

		private static void HookahNote(string what)
		{
			if (!_hookahLogged)
			{
				_hookahLogged = true;
				N.ULog.Warn(FastFixModule.Tag, "hookah caterpillar: " + what + "; animation read from " + (_hookahDir == null ? "(folder not found)" : _hookahDir.FullName) + " (logged once)");
			}
		}

		// ----- RottenWhaleFall: the tail pulls a target that is on an edge (no passage) or gone -----

		private static void InstallWhaleTail(Assembly asm)
		{
			MethodInfo m = Find(asm, "RottenWhaleFall.RottenWhaleFall_Tail", "MovingBehind", new Type[] { typeof(float), typeof(MovableObjectNode), typeof(UnitModel) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.WhaleTailPull", "RottenWhaleFall_Tail.MovingBehind");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("WhaleTailPrefix")), null);
			Patched("FastFix.WhaleTailPull", m);
		}

		// Skips one pull step; FixedUpdate still counts the tail's time down and destroys it.
		public static bool WhaleTailPrefix(MovableObjectNode Node, UnitModel target)
		{
			return target != null && Node != null && Node.currentPassage != null && target.GetMovableNode() != null;
		}

		// ----- Charlotte / KekesiWolf: PassageObjectModel.GetEnteredTargets() call sites -----

		private static System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> _noTargets;

		private static void InstallCharlotte(Assembly asm)
		{
			MethodInfo m = Find(asm, "Charlotte.Charlotte", "OnTakeDamage", new Type[] { typeof(UnitModel), typeof(DamageInfo), typeof(float) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.CharlotteDamage", "Charlotte.OnTakeDamage");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("CharlotteTranspiler")));
			Report("FastFix.CharlotteDamage", m, "PassageObjectModel.GetEnteredTargets call");
		}

		// UniqueEscape has a foreach (GL-53): the transpiler needs 12Harmony.
		private static void InstallKekesi(Assembly asm)
		{
			MethodInfo m = Find(asm, "KekesiWolf.AmirWolf", "UniqueEscape", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.KekesiWolfHit", "AmirWolf.UniqueEscape");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.KekesiWolfHit", "UniqueEscape has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("KekesiTranspiler")));
			Report("FastFix.KekesiWolfHit", m, "PassageObjectModel.GetEnteredTargets call");
		}

		public static IEnumerable<CodeInstruction> CharlotteTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			return EnteredTargets(instructions, "CharlotteTargets");
		}

		public static IEnumerable<CodeInstruction> KekesiTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			return EnteredTargets(instructions, "KekesiTargets");
		}

		// callvirt PassageObjectModel.GetEnteredTargets() -> call <helper>(PassageObjectModel).
		private static IEnumerable<CodeInstruction> EnteredTargets(IEnumerable<CodeInstruction> instructions, string helper)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own(helper);
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if (list[i].opcode == OpCodes.Callvirt && !object.ReferenceEquals(c, null) && c.Name == "GetEnteredTargets" && c.GetParameters().Length == 0
					&& object.ReferenceEquals(c.DeclaringType, typeof(PassageObjectModel)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = to;
					_hits++;
				}
			}
			return list;
		}

		// Charlotte reads the count of units in its passage; on an edge it has none.
		public static System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> CharlotteTargets(PassageObjectModel p)
		{
			if (p != null)
			{
				return p.GetEnteredTargets();
			}
			if (_noTargets == null)
			{
				_noTargets = new System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode>(new List<MovableObjectNode>());
			}
			return _noTargets;
		}

		// The wolf's hit kills units inside its foreach, which removes them from the passage list: iterate a copy.
		public static System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> KekesiTargets(PassageObjectModel p)
		{
			return new System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode>(new List<MovableObjectNode>(p.GetEnteredTargets()));
		}

		// ----- Pinocchio: UniqueEscape picks its defence target by the wrong count and chases a dead target -----

		private static FieldInfo _pinoChasing;
		private static FieldInfo _pinoTarget;
		private static FieldInfo _pinoAttacking;
		private static bool _pinoEscFailed;

		// UniqueEscape has a foreach (GL-53): both patches need 12Harmony.
		private static void InstallPinoEscape(Assembly asm)
		{
			MethodInfo m = Find(asm, "Pinocchio.Pinocchio", "UniqueEscape", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.PinocchioEscape", "Pinocchio.UniqueEscape");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.PinocchioEscape", "UniqueEscape has exception handlers and 12Harmony is not loaded");
				return;
			}
			Type t = m.DeclaringType;
			_pinoChasing = t.GetField("chasing", All);
			_pinoTarget = t.GetField("_target", All);
			_pinoAttacking = t.GetField("attacking", All);
			bool fields = !object.ReferenceEquals(_pinoChasing, null) && object.ReferenceEquals(_pinoChasing.FieldType, typeof(bool))
				&& !object.ReferenceEquals(_pinoTarget, null) && object.ReferenceEquals(_pinoTarget.FieldType, typeof(UnitModel))
				&& !object.ReferenceEquals(_pinoAttacking, null) && object.ReferenceEquals(_pinoAttacking.FieldType, typeof(bool));
			_hits = 0;
			FastFixModule.Harmony.Patch(m, fields ? new HarmonyMethod(Own("PinoEscapePrefix")) : null, null, new HarmonyMethod(Own("PinoPickTranspiler")));
			Report("FastFix.PinocchioEscape", m, "List<AgentModel>.get_Item call");
			if (!fields)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.PinocchioEscape partly applied: fields chasing/_target/attacking not found, dead-target reset skipped");
			}
		}

		// list2[Random.Range(0, list.Count)]: the index counts the near targets, not list2 -> call PinoPick(list2, index).
		public static IEnumerable<CodeInstruction> PinoPickTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("PinoPick");
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if (list[i].opcode == OpCodes.Callvirt && !object.ReferenceEquals(c, null) && c.Name == "get_Item"
					&& object.ReferenceEquals(c.DeclaringType, typeof(List<AgentModel>)))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = to;
					_hits++;
				}
			}
			return list;
		}

		// The agent at index when it is in range, else a random one, or null when list2 is empty
		// (ActiveDefenceChange checks actorModel for null).
		public static AgentModel PinoPick(List<AgentModel> agents, int index)
		{
			if (agents == null || agents.Count == 0)
			{
				return null;
			}
			if (index >= 0 && index < agents.Count)
			{
				return agents[index];
			}
			return agents[UnityEngine.Random.Range(0, agents.Count)];
		}

		// The chase branch nulls a dead target and then reads it: drop a dead or missing target before the tick.
		// Not while attacking: vanilla checks the target only then, and the attack's animation events read _target.
		// Reflection runs once per tick of an escaped Pinocchio only.
		public static void PinoEscapePrefix(object __instance)
		{
			try
			{
				if (!(bool)_pinoChasing.GetValue(__instance) || (bool)_pinoAttacking.GetValue(__instance))
				{
					return;
				}
				UnitModel target = _pinoTarget.GetValue(__instance) as UnitModel;
				if (target == null || target.hp <= 0f)
				{
					_pinoTarget.SetValue(__instance, null);
					_pinoChasing.SetValue(__instance, false);
				}
			}
			catch (Exception e)
			{
				Fail(ref _pinoEscFailed, "pinocchio chase fix", e);
			}
		}

		// ----- Invalidum / HomelessBeast: foreach over a live GetEnteredTargets() list while units die -----

		private static void InstallFlood(Assembly asm)
		{
			EnteredCopy(asm, "Invalidum_MOD.Invalidum+PassageFlooded", "Process", Type.EmptyTypes, "FastFix.InvalidumFlood");
		}

		private static void InstallBeast(Assembly asm)
		{
			EnteredCopy(asm, "HomelessBeast.HomelessBeastAnim", "EventHandler", new Type[] { typeof(Spine.TrackEntry), typeof(Spine.Event) }, "FastFix.HomelessBeastHit");
		}

		// Both methods have a foreach (GL-53): the transpiler needs 12Harmony. KekesiTargets iterates a copy.
		private static void EnteredCopy(Assembly asm, string type, string method, Type[] args, string key)
		{
			MethodInfo m = Find(asm, type, method, args);
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, type + "." + method);
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12(key, method + " has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("KekesiTranspiler")));
			Report(key, m, "PassageObjectModel.GetEnteredTargets call");
		}

		// ----- YaZi / BlackBird: HasDeadMotion is true but the skeleton has no death animation -----

		private static FieldInfo _yaziAnimator;
		private static FieldInfo _bbAnimator;
		private static MethodInfo _bbDeadEnd;
		private static bool _yaziLogged;
		private static bool _bbLogged;
		private static bool _deathFailed;

		private static void InstallYaZiDeath(Assembly asm)
		{
			_yaziAnimator = DeathPatch(asm, "YaZi.YaZiAnim", "FastFix.YaZiDeath", "YaZiHasDeadPostfix", "YaZiPlayDeadPrefix");
		}

		private static void InstallBlackBirdDeath(Assembly asm)
		{
			Type t = asm.GetType("BlackBird_WhiteBird.BlackBirdAnim");
			_bbDeadEnd = object.ReferenceEquals(t, null) ? null : t.GetMethod("DeadEnd", All, null, new Type[] { typeof(Spine.TrackEntry) }, null);
			if (object.ReferenceEquals(_bbDeadEnd, null))
			{
				Missing("FastFix.BlackBirdDeath", "BlackBirdAnim.DeadEnd");
				return;
			}
			_bbAnimator = DeathPatch(asm, "BlackBird_WhiteBird.BlackBirdAnim", "FastFix.BlackBirdDeath", null, "BlackBirdPlayDeadPrefix");
		}

		private static FieldInfo DeathPatch(Assembly asm, string type, string key, string hasName, string playName)
		{
			Type t = asm.GetType(type);
			MethodInfo has = object.ReferenceEquals(t, null) ? null : t.GetMethod("HasDeadMotion", All, null, Type.EmptyTypes, null);
			MethodInfo play = object.ReferenceEquals(t, null) ? null : t.GetMethod("PlayDeadMotion", All, null, Type.EmptyTypes, null);
			FieldInfo f = object.ReferenceEquals(t, null) ? null : t.GetField("animator", All);
			if (object.ReferenceEquals(has, null) || object.ReferenceEquals(play, null) || object.ReferenceEquals(f, null))
			{
				Missing(key, type + ".HasDeadMotion/PlayDeadMotion/animator");
				return null;
			}
			if (!object.ReferenceEquals(f.FieldType, typeof(Spine.Unity.SkeletonAnimation)))
			{
				N.ULog.Warn(FastFixModule.Tag, key + " not applied: " + type + ".animator is " + f.FieldType.AssemblyQualifiedName + ", not the game's SkeletonAnimation");
				return null;
			}
			if (hasName != null)
			{
				FastFixModule.Harmony.Patch(has, null, new HarmonyMethod(Own(hasName)));
				Patched(key, has);
			}
			FastFixModule.Harmony.Patch(play, new HarmonyMethod(Own(playName)), null);
			Patched(key, play);
			return f;
		}

		// HasDeadMotion false: CreatureUnit.OnChangeState takes the vanilla branch (hides the body, shows the return object).
		public static void YaZiHasDeadPostfix(object __instance, ref bool __result)
		{
			if (__result && DeathMissing(__instance, _yaziAnimator, "dead", "YaZi", ref _yaziLogged))
			{
				__result = false;
			}
		}

		public static bool YaZiPlayDeadPrefix(object __instance)
		{
			return !DeathMissing(__instance, _yaziAnimator, "dead", "YaZi", ref _yaziLogged);
		}

		// The mod's own end of the death motion is DeadEnd (Default pose, ParamInit): without "Die" it runs at once.
		public static bool BlackBirdPlayDeadPrefix(object __instance)
		{
			if (!DeathMissing(__instance, _bbAnimator, "Die", "BlackBird", ref _bbLogged))
			{
				return true;
			}
			try
			{
				_bbDeadEnd.Invoke(__instance, new object[] { null });
			}
			catch (Exception e)
			{
				Fail(ref _deathFailed, "black bird death end", e);
			}
			return false;
		}

		// True when SetAnimation(0, name) would throw: no animator, no state or no animation of that name.
		private static bool DeathMissing(object inst, FieldInfo f, string name, string mod, ref bool logged)
		{
			try
			{
				Spine.Unity.SkeletonAnimation a = f.GetValue(inst) as Spine.Unity.SkeletonAnimation;
				if (a != null)
				{
					Spine.AnimationState s = a.AnimationState;
					if (s != null && s.Data != null && s.Data.SkeletonData != null && s.Data.SkeletonData.FindAnimation(name) != null)
					{
						return false;
					}
				}
				if (!logged)
				{
					logged = true;
					N.ULog.Warn(FastFixModule.Tag, mod + ": no \"" + name + "\" animation, death motion skipped (vanilla: throws every frame; logged once)");
				}
				return true;
			}
			catch (Exception e)
			{
				Fail(ref _deathFailed, "death motion check", e);
				return false;
			}
		}


		// ----- Rotten Whale Fall: UniqueEscape picks a new roaming node every tick when it has no target -----

		// UniqueEscape has a try/catch (GL-53): the transpiler needs 12Harmony.
		private static void InstallWhaleRoam(Assembly asm)
		{
			MethodInfo m = Find(asm, "RottenWhaleFall.RottenWhaleFall", "UniqueEscape", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.RottenWhaleRoam", "RottenWhaleFall.UniqueEscape");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.RottenWhaleRoam", "UniqueEscape has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("WhaleRoamTranspiler")));
			Report("FastFix.RottenWhaleRoam", m, "MoveToNode(GetRoamingNodeByRandom()) call");
		}

		// movable.MoveToNode(MapGraph.instance.GetRoamingNodeByRandom(), false) -> RoamIfIdle(movable, node, false).
		// Only the MoveToNode fed by GetRoamingNodeByRandom (within the 3 instructions before it); the special attack's MoveToNode stays.
		public static IEnumerable<CodeInstruction> WhaleRoamTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("RoamIfIdle");
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if (list[i].opcode != OpCodes.Callvirt || object.ReferenceEquals(c, null) || c.Name != "MoveToNode"
					|| !object.ReferenceEquals(c.DeclaringType, typeof(MovableObjectNode)) || c.GetParameters().Length != 2)
				{
					continue;
				}
				for (int k = i - 1; k >= 0 && k >= i - 3; k--)
				{
					MethodInfo r = list[k].operand as MethodInfo;
					if (!object.ReferenceEquals(r, null) && r.Name == "GetRoamingNodeByRandom" && r.GetParameters().Length == 0
						&& object.ReferenceEquals(r.DeclaringType, typeof(MapGraph)))
					{
						list[i].opcode = OpCodes.Call;
						list[i].operand = to;
						_hits++;
						break;
					}
				}
			}
			return list;
		}

		// A new roaming destination only when the whale is not already walking (as its BoneFish does).
		public static void RoamIfIdle(MovableObjectNode movable, MapNode node, bool checkRabbit)
		{
			if (!movable.IsMoving())
			{
				movable.MoveToNode(node, checkRabbit);
			}
		}

		// ----- QianRu: ThisSucks removes from InventoryModel.equipList inside a foreach over it -----

		// ThisSucks has foreach blocks (GL-53): the transpiler needs 12Harmony.
		private static void InstallQianRuEquip(Assembly asm)
		{
			MethodInfo m = Find(asm, "QianRu.QianRu", "ThisSucks", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.QianRuEquip", "QianRu.ThisSucks");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.QianRuEquip", "ThisSucks has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("QianRuEquipTranspiler")));
			if (_hits > 1)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.QianRuEquip: " + _hits + " List<EquipmentModel>.GetEnumerator calls replaced, expected 1");
			}
			Report("FastFix.QianRuEquip", m, "List<EquipmentModel>.GetEnumerator call");
		}

		// foreach (EquipmentModel e in InventoryModel.Instance.equipList) -> the loop walks a copy, Remove hits the real list.
		public static IEnumerable<CodeInstruction> QianRuEquipTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("QianRuEquipEnum");
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && !object.ReferenceEquals(c, null) && c.Name == "GetEnumerator"
					&& object.ReferenceEquals(c.DeclaringType, typeof(List<EquipmentModel>)) && c.GetParameters().Length == 0)
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = to;
					_hits++;
				}
			}
			return list;
		}

		public static List<EquipmentModel>.Enumerator QianRuEquipEnum(List<EquipmentModel> items)
		{
			return new List<EquipmentModel>(items).GetEnumerator();
		}

		// ----- Cauldron: Lure indexes a list filtered to living workers by the unfiltered count -----

		private static FieldInfo _cauldronBreached;
		private static FieldInfo _cauldronUsed;
		private static bool _cauldronLogged;
		private static bool _cauldronFailed;

		// Lure has no exception handlers: a plain prefix.
		private static void InstallCauldronLure(Assembly asm)
		{
			MethodInfo m = Find(asm, "Cauldron.Cauldron", "Lure", new Type[] { typeof(WorkerModel) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.CauldronLure", "Cauldron.Lure");
				return;
			}
			_cauldronBreached = m.DeclaringType.GetField("breached", All);
			_cauldronUsed = m.DeclaringType.GetField("agentUsed", All);
			if (object.ReferenceEquals(_cauldronBreached, null) || !object.ReferenceEquals(_cauldronBreached.FieldType, typeof(bool))
				|| object.ReferenceEquals(_cauldronUsed, null) || !object.ReferenceEquals(_cauldronUsed.FieldType, typeof(List<AgentModel>)))
			{
				Missing("FastFix.CauldronLure", "Cauldron fields breached/agentUsed");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("CauldronLurePrefix")), null);
			Patched("FastFix.CauldronLure", m);
		}

		// Same source choice as the mod (agentUsed, else officers, else agents, by the unfiltered Count).
		// No living worker in it: vanilla cancels the overload and the work, then throws; do the same without the throw.
		public static bool CauldronLurePrefix(object __instance, WorkerModel wm)
		{
			if (wm != null)
			{
				return true;
			}
			try
			{
				if ((bool)_cauldronBreached.GetValue(__instance))
				{
					return true;
				}
				List<AgentModel> used = _cauldronUsed.GetValue(__instance) as List<AgentModel>;
				if (used == null)
				{
					return true;
				}
				bool alive;
				if (used.Count > 0)
				{
					alive = AnyAlive(used);
				}
				else
				{
					IList<OfficerModel> officers = OfficerManager.instance.GetOfficerList();
					if (officers.Count > 0)
					{
						alive = AnyAlive(officers);
					}
					else
					{
						IList<AgentModel> agents = AgentManager.instance.GetAgentList();
						if (agents.Count <= 0)
						{
							return true;
						}
						alive = AnyAlive(agents);
					}
				}
				if (alive)
				{
					return true;
				}
			}
			catch (Exception e)
			{
				Fail(ref _cauldronFailed, "cauldron lure check", e);
				return true;
			}
			// Decided to skip: what vanilla does before it throws, outside the try (no second run on a throw).
			CreatureModel model = ((CreatureBase)__instance).model;
			model.CancelOverload();
			if (model.IsWorkingState() && model.currentSkill != null)
			{
				model.currentSkill.CancelWork();
			}
			if (!_cauldronLogged)
			{
				_cauldronLogged = true;
				N.ULog.Warn(FastFixModule.Tag, "Cauldron: no living worker to lure, lure skipped (vanilla: throws; logged once)");
			}
			return false;
		}

		private static bool AnyAlive<T>(IList<T> workers) where T : WorkerModel
		{
			for (int i = 0; i < workers.Count; i++)
			{
				if (!workers[i].IsDead())
				{
					return true;
				}
			}
			return false;
		}

		// ----- Candeloro: MamiRibbon.Update reads src/dst after the buf destroyed them -----

		private static FieldInfo _ribbonLine;
		private static FieldInfo _ribbonSrc;
		private static FieldInfo _ribbonDst;
		private static bool _ribbonLogged;
		private static bool _ribbonFailed;

		// Update has no exception handlers: a plain prefix.
		private static void InstallCandeloroRibbon(Assembly asm)
		{
			MethodInfo m = Find(asm, "Candeloro.Candeloro+MamiBaiSemaBuf+MamiRibbon", "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.CandeloroRibbon", "MamiRibbon.Update");
				return;
			}
			Type t = m.DeclaringType;
			_ribbonLine = t.GetField("line", All);
			_ribbonSrc = t.GetField("src", All);
			_ribbonDst = t.GetField("dst", All);
			if (object.ReferenceEquals(_ribbonLine, null) || !object.ReferenceEquals(_ribbonLine.FieldType, typeof(LineRenderer))
				|| object.ReferenceEquals(_ribbonSrc, null) || !object.ReferenceEquals(_ribbonSrc.FieldType, typeof(Transform))
				|| object.ReferenceEquals(_ribbonDst, null) || !object.ReferenceEquals(_ribbonDst.FieldType, typeof(Transform)))
			{
				Missing("FastFix.CandeloroRibbon", "MamiRibbon fields line/src/dst");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("CandeloroRibbonPrefix")), null);
			Patched("FastFix.CandeloroRibbon", m);
		}

		// An end destroyed (Unity null): skip the frame, the line keeps its last points (vanilla throws there every frame).
		public static bool CandeloroRibbonPrefix(object __instance)
		{
			try
			{
				LineRenderer line = _ribbonLine.GetValue(__instance) as LineRenderer;
				if (line == null)
				{
					return true;
				}
				Transform src = _ribbonSrc.GetValue(__instance) as Transform;
				Transform dst = _ribbonDst.GetValue(__instance) as Transform;
				if (src != null && dst != null)
				{
					return true;
				}
				if (!_ribbonLogged)
				{
					_ribbonLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "Candeloro: ribbon end destroyed, ribbon update skipped (vanilla: throws every frame; logged once)");
				}
				return false;
			}
			catch (Exception e)
			{
				Fail(ref _ribbonFailed, "candeloro ribbon check", e);
				return true;
			}
		}

		// ----- Cloud9: the department filter of OnNotice/YearningCall reads this.currentPassage, null between rooms -----

		private static bool _cloudLogged;
		private static bool _cloudFailed;

		// The filters are compiler-generated lambdas (no exception handlers): plain prefixes.
		private static void InstallCloud9Panic(Assembly asm)
		{
			string[] names = new string[] { "<OnNotice>b__14_0", "<YearningCall>b__9_0" };
			MethodInfo[] ms = new MethodInfo[names.Length];
			for (int i = 0; i < names.Length; i++)
			{
				ms[i] = Find(asm, "Cloud9.Cloud", names[i], new Type[] { typeof(WorkerModel) });
				if (object.ReferenceEquals(ms[i], null) || !object.ReferenceEquals(ms[i].ReturnType, typeof(bool)) || !typeof(CreatureBase).IsAssignableFrom(ms[i].DeclaringType))
				{
					Missing("FastFix.Cloud9Panic", "Cloud9.Cloud." + names[i] + "(WorkerModel) : bool");
					return;
				}
			}
			for (int i = 0; i < ms.Length; i++)
			{
				FastFixModule.Harmony.Patch(ms[i], new HarmonyMethod(Own("Cloud9PanicPrefix")), null);
				Patched("FastFix.Cloud9Panic", ms[i]);
			}
		}

		// No passage (cloud on an edge or elevator): no worker can share its department, the filter answers false.
		public static bool Cloud9PanicPrefix(object __instance, ref bool __result)
		{
			try
			{
				if (((CreatureBase)__instance).currentPassage != null)
				{
					return true;
				}
			}
			catch (Exception e)
			{
				Fail(ref _cloudFailed, "cloud9 passage check", e);
				return true;
			}
			if (!_cloudLogged)
			{
				_cloudLogged = true;
				N.ULog.Warn(FastFixModule.Tag, "Cloud9: cloud between rooms, department filter skipped (vanilla: throws; logged once)");
			}
			__result = false;
			return false;
		}

		// ----- YaZi: Escape closes animScript.requestUI, which exists only after the request window was opened -----

		private static PropertyInfo _yaziAnimProp;
		private static FieldInfo _yaziRequestUI;
		private static MethodInfo _yaziCloseRequest;
		private static bool _yaziEscLogged;
		private static bool _yaziEscFailed;

		// Escape has no exception handlers: a transpiler on the last statement.
		private static void InstallYaZiEscapeUI(Assembly asm)
		{
			MethodInfo m = Find(asm, "YaZi.YaZi", "Escape", Type.EmptyTypes);
			_yaziAnimProp = object.ReferenceEquals(m, null) ? null : m.DeclaringType.GetProperty("animScript", All);
			_yaziRequestUI = object.ReferenceEquals(_yaziAnimProp, null) ? null : _yaziAnimProp.PropertyType.GetField("requestUI", All);
			_yaziCloseRequest = object.ReferenceEquals(_yaziRequestUI, null) ? null : _yaziRequestUI.FieldType.GetMethod("OnCloseRequest", All, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(_yaziCloseRequest, null) || !typeof(UnityEngine.Object).IsAssignableFrom(_yaziAnimProp.PropertyType)
				|| !typeof(UnityEngine.Object).IsAssignableFrom(_yaziRequestUI.FieldType))
			{
				Missing("FastFix.YaZiEscapeUI", "YaZi.Escape / animScript.requestUI.OnCloseRequest");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("YaZiEscapeTranspiler")));
			if (_hits > 1)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.YaZiEscapeUI: " + _hits + " close calls replaced, expected 1");
			}
			Report("FastFix.YaZiEscapeUI", m, "animScript.requestUI.OnCloseRequest call");
		}

		// call get_animScript; ldfld requestUI; callvirt OnCloseRequest -> call YaZiCloseRequest(this); nop; nop.
		public static IEnumerable<CodeInstruction> YaZiEscapeTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo getter = _yaziAnimProp.GetGetMethod(true);
			for (int i = 0; i + 2 < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				FieldInfo f = list[i + 1].operand as FieldInfo;
				MethodInfo v = list[i + 2].operand as MethodInfo;
				if (!object.ReferenceEquals(c, null) && !object.ReferenceEquals(f, null) && !object.ReferenceEquals(v, null)
					&& string.Equals(c.Name, getter.Name, StringComparison.Ordinal) && object.ReferenceEquals(c.DeclaringType, getter.DeclaringType)
					&& list[i + 1].opcode == OpCodes.Ldfld && string.Equals(f.Name, _yaziRequestUI.Name, StringComparison.Ordinal) && object.ReferenceEquals(f.DeclaringType, _yaziRequestUI.DeclaringType)
					&& string.Equals(v.Name, _yaziCloseRequest.Name, StringComparison.Ordinal) && object.ReferenceEquals(v.DeclaringType, _yaziCloseRequest.DeclaringType))
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = Own("YaZiCloseRequest");
					list[i + 1].opcode = OpCodes.Nop;
					list[i + 1].operand = null;
					list[i + 2].opcode = OpCodes.Nop;
					list[i + 2].operand = null;
					_hits++;
				}
			}
			return list;
		}

		// Once per breach. Window never opened (requestUI null) or anim gone: nothing to close.
		public static void YaZiCloseRequest(object yazi)
		{
			UnityEngine.Object ui = null;
			try
			{
				UnityEngine.Object anim = _yaziAnimProp.GetValue(yazi, null) as UnityEngine.Object;
				if (anim != null)
				{
					ui = _yaziRequestUI.GetValue(anim) as UnityEngine.Object;
				}
			}
			catch (Exception e)
			{
				Fail(ref _yaziEscFailed, "yazi request window lookup", e);
				throw;
			}
			if (ui == null)
			{
				if (!_yaziEscLogged)
				{
					_yaziEscLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "YaZi: no request window to close on escape, close skipped (vanilla: throws; logged once)");
				}
				return;
			}
			_yaziCloseRequest.Invoke(ui, null);
		}

		// ----- Grungly: lastWM re-picks a random agent on every read; null when no agent qualifies -----

		private static FieldInfo _grunglyLastField;
		private static MethodInfo _grunglyGetter;
		private static MethodInfo _grunglyStop;
		private static object _grunglyInst;
		private static Func<AgentModel> _grunglyGet;
		private static Action _grunglyStopCall;
		private static bool _grunglyLogged;
		private static bool _grunglyFailed;

		// Lure and OnFixedUpdate have no exception handlers: transpilers.
		private static void InstallGrunglyLure(Assembly asm)
		{
			MethodInfo lure = Find(asm, "Grungly.Grungly", "Lure", Type.EmptyTypes);
			MethodInfo fixedUpdate = Find(asm, "Grungly.Grungly", "OnFixedUpdate", new Type[] { typeof(CreatureModel) });
			Type t = object.ReferenceEquals(lure, null) ? null : lure.DeclaringType;
			_grunglyLastField = object.ReferenceEquals(t, null) ? null : t.GetField("_lastWM", All);
			PropertyInfo prop = object.ReferenceEquals(t, null) ? null : t.GetProperty("lastWM", All);
			_grunglyGetter = object.ReferenceEquals(prop, null) ? null : prop.GetGetMethod(true);
			_grunglyStop = object.ReferenceEquals(t, null) ? null : t.GetMethod("StopLure", All, null, Type.EmptyTypes, null);
			FieldInfo luring = object.ReferenceEquals(t, null) ? null : t.GetField("luring", All);
			if (object.ReferenceEquals(fixedUpdate, null) || object.ReferenceEquals(_grunglyLastField, null) || !object.ReferenceEquals(_grunglyLastField.FieldType, typeof(AgentModel))
				|| object.ReferenceEquals(_grunglyGetter, null) || !object.ReferenceEquals(_grunglyGetter.ReturnType, typeof(AgentModel))
				|| object.ReferenceEquals(_grunglyStop, null) || object.ReferenceEquals(luring, null) || !object.ReferenceEquals(luring.FieldType, typeof(bool)))
			{
				Missing("FastFix.GrunglyLure", "Grungly.Lure / OnFixedUpdate / lastWM / _lastWM / luring / StopLure");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(lure, null, null, new HarmonyMethod(Own("GrunglyLureTranspiler")));
			if (_hits != 8)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.GrunglyLure: " + _hits + " lastWM reads replaced in Lure, expected 8");
			}
			Report("FastFix.GrunglyLure", lure, "repeated lastWM read");
			_hits = 0;
			FastFixModule.Harmony.Patch(fixedUpdate, null, null, new HarmonyMethod(Own("GrunglyTickTranspiler")));
			if (_hits != 1)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.GrunglyLure: " + _hits + " luring reads replaced in OnFixedUpdate, expected 1");
			}
			Report("FastFix.GrunglyLure", fixedUpdate, "luring read");
		}

		// Lure: the first lastWM read is the null check; later reads use the agent that passed it (_lastWM).
		public static IEnumerable<CodeInstruction> GrunglyLureTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			bool first = true;
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && !object.ReferenceEquals(c, null)
					&& string.Equals(c.Name, _grunglyGetter.Name, StringComparison.Ordinal) && object.ReferenceEquals(c.DeclaringType, _grunglyGetter.DeclaringType))
				{
					if (first)
					{
						first = false;
						continue;
					}
					list[i].opcode = OpCodes.Ldfld;
					list[i].operand = _grunglyLastField;
					_hits++;
				}
			}
			return list;
		}

		// OnFixedUpdate: this.luring -> GrunglyLuring(this.luring, this).
		public static IEnumerable<CodeInstruction> GrunglyTickTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("GrunglyLuring");
			for (int i = 0; i < list.Count; i++)
			{
				FieldInfo f = list[i].operand as FieldInfo;
				if (list[i].opcode == OpCodes.Ldfld && !object.ReferenceEquals(f, null) && string.Equals(f.Name, "luring", StringComparison.Ordinal)
					&& object.ReferenceEquals(f.DeclaringType, _grunglyGetter.DeclaringType))
				{
					list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
					list.Insert(i + 2, new CodeInstruction(OpCodes.Call, to));
					i += 2;
					_hits++;
				}
			}
			return list;
		}

		// Luring with no agent that qualifies: stop the lure (counter back, as Lure does without a target), skip the block.
		public static bool GrunglyLuring(bool luring, object grungly)
		{
			if (!luring)
			{
				return false;
			}
			if (_grunglyFailed)
			{
				return true;
			}
			try
			{
				if (!object.ReferenceEquals(_grunglyInst, grungly))
				{
					_grunglyGet = (Func<AgentModel>)Delegate.CreateDelegate(typeof(Func<AgentModel>), grungly, _grunglyGetter);
					_grunglyStopCall = (Action)Delegate.CreateDelegate(typeof(Action), grungly, _grunglyStop);
					_grunglyInst = grungly;
				}
			}
			catch (Exception e)
			{
				Fail(ref _grunglyFailed, "grungly lure check", e);
				return true;
			}
			if (_grunglyGet() != null)
			{
				return true;
			}
			if (!_grunglyLogged)
			{
				_grunglyLogged = true;
				N.ULog.Warn(FastFixModule.Tag, "Grungly: no agent left to lure, lure stopped (vanilla: throws every tick; logged once)");
			}
			try
			{
				_grunglyStopCall();
			}
			catch (Exception e)
			{
				Fail(ref _grunglyFailed, "grungly stop lure", e);
			}
			return false;
		}

		// ----- BlackBird: DamageToPassage walks the live list of the hallway while its hit kills units -----

		// DamageToPassage has a foreach (try/finally, GL-53): the transpiler needs 12Harmony.
		private static void InstallBlackBirdPassageHit(Assembly asm)
		{
			MethodInfo m = Find(asm, "BlackBird_WhiteBird.BlackBirdCreature", "DamageToPassage", new Type[] { typeof(PassageObjectModel), typeof(RwbpType), typeof(int), typeof(int) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.BlackBirdPassageHit", "BlackBirdCreature.DamageToPassage");
				return;
			}
			if (!Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.BlackBirdPassageHit", "DamageToPassage has exception handlers and 12Harmony is not loaded");
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("BlackBirdPassageTranspiler")));
			if (_hits > 1)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.BlackBirdPassageHit: " + _hits + " GetEnteredTargets calls replaced, expected 1");
			}
			Report("FastFix.BlackBirdPassageHit", m, "PassageObjectModel.GetEnteredTargets call");
		}

		// passage.GetEnteredTargets() -> a read-only copy, so a unit that dies or leaves does not end the loop.
		public static IEnumerable<CodeInstruction> BlackBirdPassageTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo to = Own("BlackBirdPassageTargets");
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && !object.ReferenceEquals(c, null)
					&& string.Equals(c.Name, "GetEnteredTargets", StringComparison.Ordinal) && object.ReferenceEquals(c.DeclaringType, typeof(PassageObjectModel))
					&& c.GetParameters().Length == 0)
				{
					list[i].opcode = OpCodes.Call;
					list[i].operand = to;
					_hits++;
				}
			}
			return list;
		}

		public static ReadOnlyCollection<MovableObjectNode> BlackBirdPassageTargets(PassageObjectModel passage)
		{
			return new ReadOnlyCollection<MovableObjectNode>(passage.GetEnteredTargetsAsArray());
		}

		// ----- PuLao: GiveDamage re-enters itself for a "music" creature that is in its own target list -----

		private static readonly List<UnitModel> _puChain = new List<UnitModel>();
		private static int _puFrame = -1;
		private static bool _puLogged;
		private static bool _puFailLogged;
		private static int _puDepth;

		// GiveDamage has a foreach (GL-53): patching it needs 12Harmony.
		private static void InstallPuLao(Assembly asm)
		{
			MethodInfo m = Find(asm, "PuLao.PuLao", "GiveDamage", new Type[] { typeof(UnitModel), typeof(List<UnitModel>), typeof(DamageInfo) });
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.PuLaoChain", "PuLao.GiveDamage");
				return;
			}
			MethodBody body = m.GetMethodBody();
			if (body != null && body.ExceptionHandlingClauses.Count > 0 && !Covariance.Handlers12())
			{
				Covariance.NoH12("FastFix.PuLaoChain", "GiveDamage has exception handlers and 12Harmony is not loaded");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("PuLaoPrefix")), new HarmonyMethod(Own("PuLaoPostfix")));
			Patched("FastFix.PuLaoChain", m);
		}

		// Each unit starts the damage chain once per PuLao attack (the top-level GiveDamage call); later entries are skipped.
		// Harmony 1 runs the postfix even after a skipping prefix, so __state marks the calls that really ran.
		// The depth and the set are also reset when the frame changes, in case an exception skipped a postfix.
		public static bool PuLaoPrefix(UnitModel actor, out bool __state)
		{
			__state = false;
			bool skip = false;
			try
			{
				int f = Time.frameCount;
				if (f != _puFrame || _puDepth <= 0)
				{
					_puFrame = f;
					_puDepth = 0;
					_puChain.Clear();
				}
				if (_puChain.Contains(actor))
				{
					skip = true;
				}
				else
				{
					_puChain.Add(actor);
					_puDepth++;
					__state = true;
				}
			}
			catch (Exception e)
			{
				if (!_puFailLogged)
				{
					_puFailLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "PuLao chain check failed (logged once): " + e.Message);
				}
			}
			if (skip && !_puLogged)
			{
				_puLogged = true;
				string name = "?";
				try
				{
					name = actor == null ? "null" : actor.GetUnitName();
				}
				catch (Exception)
				{
				}
				N.ULog.Warn(FastFixModule.Tag, "PuLao: damage chain re-entered for " + name + ", repeat skipped (vanilla: endless recursion and a native crash; logged once)");
			}
			return !skip;
		}

		public static void PuLaoPostfix(bool __state)
		{
			if (__state)
			{
				_puDepth--;
			}
		}

		private class GuardedEvent
		{
			private static bool _logged;
			private readonly Spine.AnimationState.TrackEntryEventDelegate _handler;

			public GuardedEvent(Spine.AnimationState.TrackEntryEventDelegate handler)
			{
				_handler = handler;
			}

			public void Invoke(Spine.TrackEntry entry, Spine.Event e)
			{
				try
				{
					_handler(entry, e);
				}
				catch (Exception ex)
				{
					if (!_logged)
					{
						_logged = true;
						N.ULog.Warn(FastFixModule.Tag, "eternal flower attack event threw, skipped (logged once): " + ex);
					}
				}
			}
		}

		// ----- Isomer: effect picture path -----

		private static void InstallIsomer(Assembly asm)
		{
			MethodInfo m = Find(asm, "Isomer", "PreMakeEffect", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				Missing("FastFix.IsomerEffect", "Isomer.PreMakeEffect");
				return;
			}
			if (string.IsNullOrEmpty(asm.Location))
			{
				Missing("FastFix.IsomerEffect", "Isomer.dll location");
				return;
			}
			string data = Application.dataPath.Replace('\\', '/');
			string dir = System.IO.Path.GetDirectoryName(asm.Location).Replace('\\', '/');
			if (!dir.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase))
			{
				Missing("FastFix.IsomerEffect", "Isomer folder under " + data);
				return;
			}
			string rel = dir.Substring(data.Length) + "/Image/DamageEffect.png";
			if (!System.IO.File.Exists(data + rel))
			{
				Missing("FastFix.IsomerEffect", data + rel);
				return;
			}
			_isomerPath = rel;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("IsomerTranspiler")));
			Patched("FastFix.IsomerEffect", m);
		}

		public static IEnumerable<CodeInstruction> IsomerTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			for (int i = 0; i < list.Count; i++)
			{
				string s = list[i].operand as string;
				if (list[i].opcode == OpCodes.Ldstr && s != null && s.StartsWith("/BaseMods/", StringComparison.Ordinal) && s.EndsWith("/Image/DamageEffect.png", StringComparison.Ordinal))
				{
					list[i].operand = _isomerPath;
					return list;
				}
			}
			N.ULog.Warn(FastFixModule.Tag, "isomer fix: path string not found, method left unchanged");
			return list;
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

	// CreatureManager.OnStageEnd calls script.OnStageEnd and OnStageEnd of every creature in one loop. A creature script that
	// throws there (Invalidum.MakeSefiraBrightly with an escaped creature outside any passage) ends GameManager.EndGame and
	// ClearAction before the result screen, and the day stays on "Workday Complete". The Prefix makes the same calls in the
	// same order over a snapshot, each call guarded, and skips the original. The original's only handler is the foreach
	// finally over List<T> (no-op Dispose, GL-53 harmless), so the flag off runs the vanilla copy.
	public static class StageEndGuard
	{
		private static N.Flag _flag;
		private static readonly Dictionary<string, bool> Logged = new Dictionary<string, bool>();

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(CreatureManager), "OnStageEnd", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(StageEndGuard).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(CreatureManager __instance)
		{
			if (!_flag.On)
			{
				return true;
			}
			CreatureModel[] list = __instance.GetCreatureList();
			int failed = 0;
			for (int i = 0; i < list.Length; i++)
			{
				CreatureModel c = list[i];
				try
				{
					c.script.OnStageEnd();
				}
				catch (Exception e)
				{
					failed++;
					Report(c, "script.OnStageEnd", e);
				}
				try
				{
					c.OnStageEnd();
				}
				catch (Exception e)
				{
					failed++;
					Report(c, "OnStageEnd", e);
				}
			}
			if (failed > 0)
			{
				N.ULog.Warn(FastFixModule.Tag, "stage end guard: " + failed + " creature stage end call(s) threw, the day end went on");
			}
			return false;
		}

		private static void Report(CreatureModel c, string where, Exception e)
		{
			string type = (c == null || c.script == null) ? "null" : c.script.GetType().FullName;
			string key = type + "|" + where + "|" + e.GetType().FullName;
			if (Logged.ContainsKey(key))
			{
				return;
			}
			Logged[key] = true;
			string id = c == null ? "?" : c.metadataId + "/" + c.instanceId;
			N.ULog.Error(FastFixModule.Tag, "stage end guard: " + type + " (creature " + id + ") threw in " + where + ", skipped (logged once): " + e);
		}
	}

	// NothingWorker without a model. It Goes By The Name of Dream (Named_Dream_Child.TransformToWorker) loads the
	// NothingWorker prefab and never calls Init, so model stays null and Update throws a NullReferenceException in
	// UpdateDirection every frame; the object never moves. Vanilla Nothing There always calls Init first. The Prefix skips
	// Update while model is null; the picture is the same, the exception flood is gone. Update has no handler (GL-53).
	public static class NothingWorkerNoModel
	{
		private static N.Flag _flag;
		private static bool _logged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(NothingWorker), "Update", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(NothingWorkerNoModel).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(NothingWorker __instance)
		{
			if (!object.ReferenceEquals(__instance.model, null) || !_flag.On)
			{
				return true;
			}
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "NothingWorker without a model (not initialized by a mod), Update skipped (logged once)");
			}
			return false;
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

	// Vanilla UnitModel.ReleaseWeaponV2 (unequip in the inventory, another agent taking the weapon, stage release) gives the agent
	// the dummy Riot stick, but AgentModel.OnReleaseWeapon, unlike OnSetWeapon, does not call AgentUnit.OnChangeWeapon. The unit
	// keeps the old art and Animator params; the Riot stick (MACE) attack then shows the skeleton's default Weapon_Mace art
	// (One Sin's weapon). Fix: after OnReleaseWeapon the unit runs OnChangeWeapon (art and params only, the model is not touched).
	public static class WeaponReskin
	{
		private static N.Flag _flag;
		private static bool _logged;
		private static int _errors;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AgentModel), "OnReleaseWeapon", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "weapon release fix not applied: AgentModel.OnReleaseWeapon not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(WeaponReskin).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Postfix(AgentModel __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				AgentUnit unit = __instance.GetUnit();
				if (unit == null)
				{
					return;
				}
				unit.OnChangeWeapon();
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "weapon release: unit weapon look refreshed after unequip (first agent " + __instance.instanceId + ")");
				}
			}
			catch (Exception e)
			{
				if (_errors < 3)
				{
					_errors++;
					N.ULog.Error(FastFixModule.Tag, "weapon release error " + _errors + "/3 (later ones silent): " + e);
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
			public MethodInfo Loop;
			public int LastFrame = -1;
		}

		internal static N.IntParam Count;
		internal static bool PatchOn;
		private static bool _fullLogged;
		internal static bool LoopOn;

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
			N.ULog.Info(FastFixModule.Tag, "guard on count=" + Count.Value + "/10s patches=" + PatchOn + " loops=" + LoopOn);
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
				MethodInfo loop = null;
				if (ParseFrames(stackTrace, out patch, out wrapper))
				{
					if (!PatchOn)
					{
						return;
					}
				}
				else
				{
					if (!LoopOn)
					{
						return;
					}
					loop = LoopFrame(stackTrace, out patch);
					if (object.ReferenceEquals(loop, null))
					{
						return;
					}
					wrapper = "";
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
							if (!_fullLogged)
							{
								_fullLogged = true;
								N.ULog.Warn(FastFixModule.Tag, "guard table full (" + MaxKeys + " queued or blocked keys): new exception kinds are not counted (logged once)");
							}
							return;
						}
					}
					w = new Win();
					w.Patch = patch;
					w.Wrapper = wrapper;
					w.Reason = reason;
					w.Loop = loop;
					w.Start = now;
					_wins[key] = w;
				}
				if (w.BlockedFrame >= 0)
				{
					// Seen again a frame after the block: the skip Prefix did not take (inlined method).
					if (!w.Warned && Time.frameCount > w.BlockedFrame + 1)
					{
						w.Warned = true;
						N.ULog.Warn(FastFixModule.Tag, "guard block ineffective " + patch + (wrapper.Length > 0 ? " in " + wrapper : "") + " reason=" + reason);
					}
					return;
				}
				if (now - w.Start > WindowSeconds)
				{
					w.Start = now;
					w.Count = 0;
				}
				if (!object.ReferenceEquals(w.Loop, null))
				{
					// A burst in one frame (many units at day end) is one hit: only a repeating loop reaches the count.
					if (Time.frameCount == w.LastFrame)
					{
						return;
					}
					w.LastFrame = Time.frameCount;
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
					n = object.ReferenceEquals(w.Loop, null) ? Block(w) : Quarantine(w);
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

		// ----- loop guard: a method of another mod that throws over and over (no Harmony patch frame in the stack) -----

		private const string HarmonySuffix = "_Patch";
		private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
		private static readonly Dictionary<string, MethodInfo> _frames = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
		private static readonly Dictionary<string, bool> _excluded = new Dictionary<string, bool>(StringComparer.Ordinal);
		private static readonly MethodInfo NoFrame = typeof(Guard).GetMethod("SkipVoid", BindingFlags.Public | BindingFlags.Static);

		// The topmost frame whose method belongs to a mod assembly; null when a frame above it cannot be resolved
		// (the culprit is then unknown and nothing is quarantined). name = that frame's text.
		internal static MethodInfo LoopFrame(string stackTrace, out string name)
		{
			name = null;
			string[] lines = stackTrace.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (line.StartsWith("at ", StringComparison.Ordinal))
				{
					line = line.Substring(3);
				}
				if (line.Length == 0)
				{
					continue;
				}
				int b = line.IndexOf(" [0x", StringComparison.Ordinal);
				if (b < 0)
				{
					b = line.IndexOf(" <0x", StringComparison.Ordinal);
				}
				if (b > 0)
				{
					line = line.Substring(0, b);
				}
				MethodInfo m;
				if (!_frames.TryGetValue(line, out m))
				{
					if (_frames.Count >= 1024)
					{
						_frames.Clear();
					}
					m = ResolveFrame(line);
					_frames[line] = m;
				}
				if (object.ReferenceEquals(m, null))
				{
					return null;
				}
				if (object.ReferenceEquals(m, NoFrame))
				{
					// Game, Unity, runtime, Harmony or NailOptimization code: look further down.
					continue;
				}
				name = line.StartsWith(WrapperMark, StringComparison.Ordinal) ? line.Substring(WrapperMark.Length) : line;
				return m;
			}
			return null;
		}

		// NoFrame for a frame of an excluded assembly, null when unresolvable, else the mod method.
		private static MethodInfo ResolveFrame(string line)
		{
			bool wrapped = line.StartsWith(WrapperMark, StringComparison.Ordinal);
			if (wrapped)
			{
				line = line.Substring(WrapperMark.Length);
			}
			else if (line[0] == '(')
			{
				// (wrapper managed-to-native), (wrapper delegate-invoke) and the like: runtime glue.
				return NoFrame;
			}
			int open = line.IndexOf(" (", StringComparison.Ordinal);
			if (open <= 0 || !line.EndsWith(")", StringComparison.Ordinal))
			{
				return null;
			}
			string full = line.Substring(0, open);
			string args = line.Substring(open + 2, line.Length - open - 3).Trim();
			if (full.IndexOf("..", StringComparison.Ordinal) >= 0 || full.IndexOf('[') >= 0)
			{
				// Constructor or generic method: never quarantined; a mod one makes the culprit unknown.
				return Foreign(full) ? null : NoFrame;
			}
			int dot = full.LastIndexOf('.');
			if (dot <= 0)
			{
				return null;
			}
			string typeName = full.Substring(0, dot);
			string method = full.Substring(dot + 1);
			if (typeName.IndexOf('<') >= 0)
			{
				// Closed generic type ("System.Collections.Generic.List`1/Enumerator<MovableObjectNode>"): runtime or Unity code
				// is glue (look further down); any other stays unknown.
				return typeName.StartsWith("System.", StringComparison.Ordinal) || typeName.StartsWith("UnityEngine.", StringComparison.Ordinal) ? NoFrame : null;
			}
			if (wrapped)
			{
				int k = method.LastIndexOf(HarmonySuffix, StringComparison.Ordinal);
				if (k <= 0)
				{
					return null;
				}
				method = method.Substring(0, k);
			}
			Type t = FindType(typeName);
			if (object.ReferenceEquals(t, null))
			{
				return null;
			}
			if (Excluded(t.Assembly))
			{
				return NoFrame;
			}
			string[] want = args.Length == 0 ? new string[0] : args.Split(',');
			MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
			for (int i = 0; i < ms.Length; i++)
			{
				if (ms[i].Name != method)
				{
					continue;
				}
				string[] w = want;
				if (wrapped && !ms[i].IsStatic && want.Length > 0)
				{
					// A Harmony wrapper is static: its first parameter is the instance.
					w = new string[want.Length - 1];
					Array.Copy(want, 1, w, 0, w.Length);
				}
				if (SameParams(ms[i].GetParameters(), w))
				{
					return ms[i];
				}
			}
			return null;
		}

		// True when the type part of a constructor or generic frame is not known to be game or runtime code.
		private static bool Foreign(string full)
		{
			int cut = full.IndexOf("..", StringComparison.Ordinal);
			if (cut < 0)
			{
				int lb = full.IndexOf('[');
				int rb = full.LastIndexOf(']');
				if (lb > 0 && rb > lb && rb + 1 < full.Length && full[rb + 1] == '.')
				{
					// Generic type: the type name ends at '[' ("Ns.List`1[T].get_Item").
					cut = lb;
				}
				else
				{
					// Generic method: the type name ends at the last '.' before '['.
					cut = lb > 0 ? full.LastIndexOf('.', lb) : -1;
				}
			}
			if (cut <= 0)
			{
				return true;
			}
			Type t = FindType(full.Substring(0, cut));
			return object.ReferenceEquals(t, null) || !Excluded(t.Assembly);
		}

		// Frame parameters "Ns.Type name" (Mono prints ".Type" for the global namespace) against the method's, by short type name.
		private static bool SameParams(ParameterInfo[] ps, string[] want)
		{
			if (ps.Length != want.Length)
			{
				return false;
			}
			for (int i = 0; i < ps.Length; i++)
			{
				string w = want[i].Trim();
				int sp = w.LastIndexOf(' ');
				if (sp > 0)
				{
					w = w.Substring(0, sp);
				}
				int d = Math.Max(w.LastIndexOf('.'), Math.Max(w.LastIndexOf('/'), w.LastIndexOf('+')));
				if (d >= 0)
				{
					w = w.Substring(d + 1);
				}
				if (w != ps[i].ParameterType.Name)
				{
					return false;
				}
			}
			return true;
		}

		private static Type FindType(string name)
		{
			Type t;
			if (_types.TryGetValue(name, out t))
			{
				return t;
			}
			string clr = name.Replace('/', '+');
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length && object.ReferenceEquals(t, null); i++)
			{
				try
				{
					t = all[i].GetType(clr, false);
				}
				catch (Exception)
				{
					t = null;
				}
			}
			if (_types.Count < 1024)
			{
				_types[name] = t;
			}
			return t;
		}

		// Game, Unity, runtime, Harmony, this mod and its modules, LobBridge, DOTween: never quarantined.
		private static bool Excluded(Assembly a)
		{
			string n = a.GetName().Name;
			bool x;
			if (_excluded.TryGetValue(n, out x))
			{
				return x;
			}
			x = n.StartsWith("Assembly-", StringComparison.Ordinal) || n == "mscorlib" || n == "0Harmony" || n == "12Harmony" || n == "LobBridge"
				|| n.StartsWith("UnityEngine", StringComparison.Ordinal) || n.StartsWith("System", StringComparison.Ordinal)
				|| n.StartsWith("Mono", StringComparison.Ordinal) || n.StartsWith("DOTween", StringComparison.Ordinal)
				|| n.StartsWith("NailOptimization", StringComparison.Ordinal) || object.ReferenceEquals(a, typeof(Guard).Assembly);
			if (!x && !string.IsNullOrEmpty(a.Location))
			{
				x = a.Location.Replace('\\', '/').IndexOf("/NailOptimization/", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			_excluded[n] = x;
			return x;
		}

		// Driver Update, the frame after the threshold: a skip Prefix on the mod method, if it is safe to skip.
		private static int Quarantine(Win w)
		{
			MethodInfo m = w.Loop;
			w.Found = true;
			string name = m.DeclaringType.FullName + "." + m.Name;
			string why = null;
			MethodBody body = m.GetMethodBody();
			if (!object.ReferenceEquals(m.ReturnType, typeof(void)))
			{
				why = "returns " + m.ReturnType.Name;
			}
			else if (m.IsAbstract || m.ContainsGenericParameters || body == null)
			{
				why = "abstract, generic or without a body";
			}
			else
			{
				MethodInfo root = m.GetBaseDefinition();
				MethodBody rb = object.ReferenceEquals(root, null) || object.ReferenceEquals(root, m) ? null : root.GetMethodBody();
				if (rb != null && Excluded(root.DeclaringType.Assembly) && rb.GetILAsByteArray().Length > 1)
				{
					// Skipping it would also skip the game's own work in base (buff expiry, cleanup).
					why = "overrides " + root.DeclaringType.Name + "." + root.Name + ", which has a body";
				}
				else if (body.ExceptionHandlingClauses.Count > 0 && !Covariance.Loaded12())
				{
					// Detect only: loading 12Harmony mid-run would change how every later patch is built (GL-55).
					why = "has exception handlers and 12Harmony is not loaded";
				}
			}
			if (why != null)
			{
				// Kept in _wins as blocked-free and queued: this key is not reported again.
				N.ULog.Warn(FastFixModule.Tag, "guard not quarantined " + name + ": " + why + " reason=" + w.Reason + " count=" + w.Count + "/10s");
				return 0;
			}
			string bk = m.DeclaringType.Assembly.FullName + "|" + m.MetadataToken;
			if (_blocked.Contains(bk))
			{
				if (w.BlockedFrame < 0)
				{
					w.BlockedFrame = Time.frameCount;
				}
				return 1;
			}
			_blocked.Add(bk);
			try
			{
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(NoFrame), null, null);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, "guard patch failed " + name + ": " + e);
				return 0;
			}
			w.BlockedFrame = Time.frameCount;
			N.ULog.Warn(FastFixModule.Tag, "guard quarantined " + name + " reason=" + w.Reason + " count=" + w.Count + "/10s");
			return 1;
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
