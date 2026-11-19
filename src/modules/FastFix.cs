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
//   Endlessgyro                    its MaxNum patch (aimed at a getter overload that does not exist) goes on get_MaxNum().
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
				return "1.70";
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
			FastFixDriver.GyroPending = true;
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
			if (N.Config.BindFlag("FastFix.AgentHoverJitter", true, "The agent name plate no longer flickers when the mouse is over an agent and the enlarged plate covers the pointer. Read at game start.").On)
			{
				AgentHover.Install(Harmony);
			}
			CameraDrag.Install(Harmony, N.Config.BindFlag("FastFix.CameraDrag", true, "A map drag with the right or middle mouse button keeps following the mouse while the camera shakes from hits, instead of freezing and jumping."));
			StageEndGuard.Install(Harmony, N.Config.BindFlag("FastFix.StageEndGuard", true, "A creature that throws at the end of the day (Invalidum with an escaped creature outside the hallways) no longer stops the day end: the result screen still opens and the error is logged once."));
			NothingWorkerNoModel.Install(Harmony, N.Config.BindFlag("FastFix.NothingWorkerNoModel", true, "A NothingWorker object that a mod created without initializing it (It Goes By The Name of Dream) no longer throws an error every frame."));
			TwilightArmorNull.Install(Harmony, N.Config.BindFlag("FastFix.TwilightArmorNull", true, "The Twilight suit's damage bonus no longer throws an error when it is asked before the day set the suit up (a suit given during the day by a mod). Read at game start."));
			IronMaidenExit.Install(Harmony, N.Config.BindFlag("FastFix.IronMaidenExit", true, "An agent who leaves We Can Change Anything alive is visible again and is no longer killed at the end of the day; the machine closes as after a death. Read at game start."));
			CensoredVictimRelease.Install(Harmony, N.Config.BindFlag("FastFix.CensoredVictimRelease", true, "The employee chosen as CENSORED's sacrifice gets control back when CENSORED escapes before he arrives, instead of standing out of control until the day ends. Read at game start."));
			CorpseSelectRing.Install(Harmony, N.Config.BindFlag("FastFix.CorpseSelectRing", true, "An agent who dies while selected no longer keeps the selection ring under the body."));
			FastFixDriver.YaZiPending = true;
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
		internal static bool GyroPending;
		internal static bool CovariancePending;
		internal static bool ModNrePending;
		internal static bool YaZiPending;

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
			if (WhiteRabbitPending || RiskPortraitPending || StarflarePending || HellTrainPending || EffectNamesPending || BambooPending || CauldronPending || ChaofengPending || ColoredFixerPending || BossBirdPending || AddRoomPending || PinelPending || KingGreedPending || ElysiaUnitPending || AcaciaPending || GyroPending || CovariancePending || ModNrePending || YaZiPending)
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
						if (YaZiPending && YaZiOnDieFix.TryInstall(_frames >= GiveUpFrames))
						{
							YaZiPending = false;
						}
					}
					catch (Exception e)
					{
						YaZiPending = false;
						N.ULog.Error(FastFixModule.Tag, "yazi install failed: " + e);
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
						if (GyroPending && GyroMaxNumFix.TryInstall(_frames >= GiveUpFrames))
						{
							GyroPending = false;
						}
					}
					catch (Exception e)
					{
						GyroPending = false;
						N.ULog.Error(FastFixModule.Tag, "endlessgyro install failed: " + e);
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
						Covariance.StepClear();
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
		private static string _stepFile;
		private static string _skipFile;
		private static bool _stepOpen;
		private static readonly Dictionary<string, bool> SkipKeys = new Dictionary<string, bool>();
		private const string SkipV2 = "v2|";
		// Assemblies read before without a place to fix: key (path|write ticks) -> the "methods not read" text ("" when none).
		private static readonly Dictionary<string, string> Clean = new Dictionary<string, string>();
		private static readonly Dictionary<string, bool> CleanUsed = new Dictionary<string, bool>();
		private static string _cacheFile;
		private static string _cacheHeader;
		private static bool _cacheDirty;

		// True when the attempt is finished. Runs on each driver poll until the give-up frame: assemblies a mod loads late are read then.
		internal static bool TryInstall(bool last)
		{
			if (_flag == null)
			{
				_flag = N.Config.BindFlag("FastFix.Covariance", false, "EXPERIMENTAL, off by default (reports of a crash at start with it on). A mod built for a newer .NET that passes a list of one unit type where a list of another is expected (List<AgentModel> as IEnumerable<UnitModel>) no longer crashes the game without an error: such places are found in the mod code at start and get a compatible list. Read at game start.");
				if (!_flag.On)
				{
					// A step file left by a crash before Covariance was turned off would make LoadGuard blame Covariance for later
					// crashes (LoadGuard reads it at init, before this runs).
					try
					{
						System.IO.File.Delete(System.IO.Path.Combine(System.IO.Path.Combine(N.UiHost.RootDir, "Logs"), "Covariance.step.txt"));
					}
					catch (Exception)
					{
					}
					return true;
				}
				_handlers = N.Config.BindFlag("FastFix.CovarianceHandlers", true, "Such places inside a method with foreach, using or try are fixed too, if 12Harmony.dll is in Managed. This loads 12Harmony, which then builds the patches of all mods (changes how other mods' patches are applied). Read at game start.");
				BuildOps();
				_covary = typeof(Covariance).GetMethod("Covary", BindingFlags.Public | BindingFlags.Static);
				_ownDir = System.IO.Path.GetDirectoryName(typeof(N.ULog).Assembly.Location);
				StepInit();
				CacheLoad();
			}
			if (!_flag.On)
			{
				return true;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			long heap0 = GC.GetTotalMemory(false);
			long priv0 = PrivateBytes();
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			int read = 0;
			int methods = 0;
			int skipped = 0;
			int noRef = 0;
			int notRead = 0;
			int cached = 0;
			List<MethodBase> targets = new List<MethodBase>();
			Dictionary<string, bool> loaded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			for (int a = 0; a < all.Length; a++)
			{
				try
				{
					loaded[all[a].GetName().Name] = true;
				}
				catch (Exception)
				{
				}
			}
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
				string key = AsmKey(all[a]);
				if (SkipKeys.ContainsKey(key))
				{
					N.ULog.Warn(FastFixModule.Tag, "covariance: " + key + " not read: the game closed while reading it at an earlier start");
					continue;
				}
				string note;
				if (Clean.TryGetValue(key, out note))
				{
					cached++;
					CleanUsed[key] = true;
					if (note.Length > 0)
					{
						N.ULog.Info(FastFixModule.Tag, "covariance: " + System.IO.Path.GetFileName(all[a].Location) + ": " + note + " (cached)");
					}
					continue;
				}
				Step("asm", key);
				string missing = MissingRef(all[a], loaded);
				if (missing != null)
				{
					if (!ReportNoRef(all[a], missing, last))
					{
						// The reference ships with the mod and is loaded by it later: read this assembly at a later poll.
						Seen.Remove(all[a]);
						continue;
					}
					noRef++;
					continue;
				}
				Dictionary<int, bool> unsafeMethods = new Dictionary<int, bool>();
				List<string> absent = new List<string>();
				string metaError = CovarianceMeta.Check(all[a], _one, _two, _hasOne, _hasTwo, unsafeMethods, absent);
				if (metaError != null)
				{
					noRef++;
					N.ULog.Warn(FastFixModule.Tag, "covariance: " + key + " not read: metadata check failed: " + metaError);
					continue;
				}
				read++;
				int before = notRead;
				int targetsBefore = targets.Count;
				ScanAssembly(all[a], targets, unsafeMethods, ref methods, ref skipped, ref notRead);
				note = string.Empty;
				if (notRead > before)
				{
					note = (notRead - before) + " methods not read: they use " + (absent.Count > 0 ? string.Join(", ", absent.GetRange(0, Math.Min(3, absent.Count)).ToArray()) : "(unreadable IL or signature)") + " missing in this game's runtime";
					N.ULog.Info(FastFixModule.Tag, "covariance: " + System.IO.Path.GetFileName(all[a].Location) + ": " + note);
				}
				if (targets.Count == targetsBefore)
				{
					Clean[key] = note;
					CleanUsed[key] = true;
					_cacheDirty = true;
				}
			}
			for (int i = 0; i < targets.Count; i++)
			{
				Patch(targets[i]);
			}
			StepClear();
			if (_cacheDirty)
			{
				CacheSave();
			}
			if (read > 0 || noRef > 0 || cached > 0)
			{
				N.ULog.Info(FastFixModule.Tag, "covariance: " + cached + " assemblies unchanged since a clean read (cache), " + read + " assemblies (" + noRef + " not read for a missing reference or a failed metadata check), " + methods + " methods read, " + skipped + " not analyzed, " + notRead + " not read (missing runtime members), " + targets.Count + " methods to patch, " + _fixed + " places fixed in total, " + sw.ElapsedMilliseconds + " ms; memory heap " + Mb(heap0) + " -> " + Mb(GC.GetTotalMemory(false)) + " MB, private " + Mb(priv0) + " -> " + Mb(PrivateBytes()) + " MB");
			}
			return last;
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		private struct MemCounters
		{
			public uint cb;
			public uint PageFaultCount;
			public UIntPtr PeakWorkingSetSize;
			public UIntPtr WorkingSetSize;
			public UIntPtr QuotaPeakPagedPoolUsage;
			public UIntPtr QuotaPagedPoolUsage;
			public UIntPtr QuotaPeakNonPagedPoolUsage;
			public UIntPtr QuotaNonPagedPoolUsage;
			public UIntPtr PagefileUsage;
			public UIntPtr PeakPagefileUsage;
			public UIntPtr PrivateUsage;
		}

		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref MemCounters counters, uint size);

		// Process private bytes (Mono's Process.PrivateMemorySize64 reads 0 here), or -1 when Windows cannot tell.
		private static long PrivateBytes()
		{
			try
			{
				MemCounters c = new MemCounters();
				c.cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemCounters));
				if (!K32GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb))
				{
					return -1;
				}
				return (long)c.PrivateUsage.ToUInt64();
			}
			catch (Exception)
			{
				return -1;
			}
		}

		private static string Mb(long bytes)
		{
			return bytes < 0 ? "?" : (bytes / (1024 * 1024)).ToString();
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

		// Crash step guard: a native abort while reading an assembly or building a patch cannot be caught. The step is written
		// to Logs/Covariance.step.txt before it runs and the file is deleted when the poll ends; a file left by the previous
		// session names the step that closed the game, which goes to Logs/Covariance.skip.txt and is skipped from then on.
		// Keys carry the DLL write time, so an updated mod is tried again; deleting the skip file retries everything.
		private static void StepInit()
		{
			try
			{
				string dir = System.IO.Path.Combine(N.UiHost.RootDir, "Logs");
				System.IO.Directory.CreateDirectory(dir);
				_stepFile = System.IO.Path.Combine(dir, "Covariance.step.txt");
				_skipFile = System.IO.Path.Combine(dir, "Covariance.skip.txt");
				if (System.IO.File.Exists(_skipFile))
				{
					// Only "v2|" entries count: older ones were written before the metadata check (FastFix 1.36), which may make them
					// safe now, so they are read again once; if the game closes again, the entry comes back as v2.
					string[] lines = System.IO.File.ReadAllLines(_skipFile);
					int old = 0;
					for (int i = 0; i < lines.Length; i++)
					{
						string l = lines[i].Trim();
						if (l.StartsWith(SkipV2, StringComparison.Ordinal))
						{
							SkipKeys[l.Substring(SkipV2.Length)] = true;
						}
						else if (l.Length > 0)
						{
							old++;
						}
					}
					if (old > 0)
					{
						System.Text.StringBuilder keep = new System.Text.StringBuilder();
						foreach (KeyValuePair<string, bool> k in SkipKeys)
						{
							keep.Append(SkipV2).Append(k.Key).Append("\r\n");
						}
						string tmp = _skipFile + ".tmp";
						System.IO.File.WriteAllText(tmp, keep.ToString());
						System.IO.File.Delete(_skipFile);
						System.IO.File.Move(tmp, _skipFile);
						N.ULog.Info(FastFixModule.Tag, "covariance: " + old + " entries of " + _skipFile + " from before FastFix 1.36 removed: read again with the metadata check");
					}
				}
				if (!System.IO.File.Exists(_stepFile))
				{
					return;
				}
				string prev = System.IO.File.ReadAllText(_stepFile).Trim();
				System.IO.File.Delete(_stepFile);
				int tab = prev.IndexOf('\t');
				if (tab <= 0)
				{
					return;
				}
				string kind = prev.Substring(0, tab);
				string key = prev.Substring(tab + 1);
				if (!SkipKeys.ContainsKey(key))
				{
					SkipKeys[key] = true;
					System.IO.File.AppendAllText(_skipFile, SkipV2 + key + "\r\n");
				}
				int bar = key.IndexOf('|');
				string path = bar > 0 ? key.Substring(0, bar) : key;
				string what = kind == "asm" ? "reading " + System.IO.Path.GetFileName(path) : "patching " + key.Substring(key.LastIndexOf('|') + 1) + " (" + System.IO.Path.GetFileName(path) + ")";
				N.ULog.Error(FastFixModule.Tag, "covariance: the last start closed while " + what + "; skipped from now on (" + key + "). Delete " + _skipFile + " to try again");
				N.Alerts.Add(2, path, "FastFix covariance: the game closed at the last start while " + what + ". It is skipped from now on; delete Logs/Covariance.skip.txt in the NailOptimization folder to try again.");
			}
			catch (Exception e)
			{
				_stepFile = null;
				N.ULog.Warn(FastFixModule.Tag, "covariance: crash step guard off: " + e.Message);
			}
		}

		private static void Step(string kind, string key)
		{
			if (_stepFile == null)
			{
				return;
			}
			try
			{
				System.IO.File.WriteAllText(_stepFile, kind + "\t" + key);
				_stepOpen = true;
			}
			catch (Exception)
			{
			}
		}

		// Logs/Covariance.cache.txt: line 1 = header (format, FastFix and Assembly-CSharp write ticks, handler option); then one
		// "key<TAB>note" line per assembly read without a place to fix. A different header drops the whole cache: the result of a
		// read depends on this code and on the game's types. Assemblies with places are never cached (their methods get patched).
		private static void CacheLoad()
		{
			try
			{
				string dir = System.IO.Path.Combine(N.UiHost.RootDir, "Logs");
				_cacheFile = System.IO.Path.Combine(dir, "Covariance.cache.txt");
				_cacheHeader = "covcache1|" + Ticks(typeof(Covariance).Assembly.Location) + "|" + Ticks(typeof(AgentModel).Assembly.Location) + "|" + (_handlers != null && _handlers.On ? "h1" : "h0");
				if (!System.IO.File.Exists(_cacheFile))
				{
					return;
				}
				string[] lines = System.IO.File.ReadAllLines(_cacheFile);
				if (lines.Length == 0 || lines[0] != _cacheHeader)
				{
					N.ULog.Info(FastFixModule.Tag, "covariance: cache from another FastFix or game build, all assemblies are read again");
					return;
				}
				for (int i = 1; i < lines.Length; i++)
				{
					int tab = lines[i].IndexOf('\t');
					if (tab > 0)
					{
						Clean[lines[i].Substring(0, tab)] = lines[i].Substring(tab + 1);
					}
				}
			}
			catch (Exception e)
			{
				Clean.Clear();
				N.ULog.Warn(FastFixModule.Tag, "covariance: cache not read: " + e.Message);
			}
		}

		// Writes only the entries of assemblies present in this session, so removed mods drop out. Temp file, then replace.
		private static void CacheSave()
		{
			_cacheDirty = false;
			if (_cacheFile == null)
			{
				return;
			}
			try
			{
				System.Text.StringBuilder sb = new System.Text.StringBuilder();
				sb.Append(_cacheHeader).Append("\r\n");
				foreach (KeyValuePair<string, bool> kv in CleanUsed)
				{
					string note;
					if (Clean.TryGetValue(kv.Key, out note))
					{
						sb.Append(kv.Key).Append('\t').Append(note).Append("\r\n");
					}
				}
				string tmp = _cacheFile + ".tmp";
				System.IO.File.WriteAllText(tmp, sb.ToString());
				if (System.IO.File.Exists(_cacheFile))
				{
					System.IO.File.Delete(_cacheFile);
				}
				System.IO.File.Move(tmp, _cacheFile);
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "covariance: cache not written: " + e.Message);
			}
		}

		private static long Ticks(string path)
		{
			try
			{
				return System.IO.File.GetLastWriteTimeUtc(path).Ticks;
			}
			catch (Exception)
			{
				return 0;
			}
		}

		// Also called by the driver when TryInstall throws: a managed exception is not a crash.
		internal static void StepClear()
		{
			if (!_stepOpen)
			{
				return;
			}
			_stepOpen = false;
			try
			{
				System.IO.File.Delete(_stepFile);
			}
			catch (Exception)
			{
			}
		}

		// Assembly file + write ticks; the location was already read by IsModNet4.
		private static string AsmKey(Assembly asm)
		{
			string loc = asm.Location;
			try
			{
				return loc + "|" + System.IO.File.GetLastWriteTimeUtc(loc).Ticks;
			}
			catch (Exception)
			{
				return loc + "|0";
			}
		}

		private static string MethodKey(MethodBase m)
		{
			return AsmKey(m.DeclaringType.Assembly) + "|" + m.DeclaringType.FullName + "." + m.Name;
		}

		private static string Norm(string name)
		{
			return name.EndsWith(".s", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) : name;
		}

		private static readonly Dictionary<string, string> ModRefs = new Dictionary<string, string>();

		// Why a mod assembly is not read. Returns false while the missing reference is a DLL inside the mod's own folder (the mod's
		// library loader loads it later) and this is not the last poll: the caller tries again. Logs once otherwise:
		// INFO for a .NET Standard / .NET Core build (Mono resolves what the mod's running code uses; the scan cannot) and for a DLL
		// shipped with the mod; WARN only when the reference is nowhere: the mod code that uses it fails when it runs.
		private static bool ReportNoRef(Assembly asm, string missing, bool last)
		{
			string loc = asm.Location;
			string mod = ModFolder(loc);
			string who = "covariance: " + mod + "/" + System.IO.Path.GetFileName(loc) + " not scanned: ";
			if (missing.StartsWith("(", StringComparison.Ordinal))
			{
				N.ULog.Warn(FastFixModule.Tag, who + "reference list " + missing);
				return true;
			}
			string name = missing.Split(new char[] { ',' })[0].Trim();
			if (name == "netstandard" || name == "System.Runtime" || name == "System.Private.CoreLib")
			{
				N.ULog.Info(FastFixModule.Tag, who + "built against " + missing + " (.NET Standard / .NET Core); the scan cannot load it, the mod itself runs while its code uses only types this runtime has");
				return true;
			}
			string found = FindInMod(loc, name + ".dll");
			if (found != null)
			{
				if (!last)
				{
					return false;
				}
				N.ULog.Info(FastFixModule.Tag, who + "its reference " + name + " ships with the mod (" + found + ") but was not loaded by the end of start-up; the mod loads it when it needs it");
				return true;
			}
			N.ULog.Warn(FastFixModule.Tag, who + "its reference " + missing + " is neither loaded, nor in Managed, nor in the mod folder: code of this mod that uses it fails when it runs (a missing dependency mod?)");
			return true;
		}

		// "<folder under BaseMods>" of a mod file, or the file's directory name when it is not under BaseMods.
		private static string ModFolder(string loc)
		{
			string full = loc.Replace('/', '\\');
			int i = full.IndexOf("\\BaseMods\\", StringComparison.OrdinalIgnoreCase);
			if (i < 0)
			{
				return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(full));
			}
			string rest = full.Substring(i + 10);
			int k = rest.IndexOf('\\');
			return k < 0 ? rest : rest.Substring(0, k);
		}

		// Path of 'file' inside the mod's folder under BaseMods, relative to it; null when absent. Cached per mod and file.
		private static string FindInMod(string loc, string file)
		{
			string full = loc.Replace('/', '\\');
			int i = full.IndexOf("\\BaseMods\\", StringComparison.OrdinalIgnoreCase);
			if (i < 0)
			{
				return null;
			}
			string mod = ModFolder(full);
			string root = full.Substring(0, i + 10) + mod;
			string ck = root + "|" + file;
			string hit;
			if (ModRefs.TryGetValue(ck, out hit))
			{
				return hit;
			}
			hit = null;
			try
			{
				string[] files = System.IO.Directory.GetFiles(root, file, System.IO.SearchOption.AllDirectories);
				if (files.Length > 0)
				{
					hit = files[0].Substring(root.Length).TrimStart(new char[] { '\\', '/' });
				}
			}
			catch (Exception)
			{
			}
			ModRefs[ck] = hit;
			return hit;
		}

		// A mod whose reference cannot be loaded is not read: loading its types for the scan is where such mods can fail.
		// Returns the first missing reference, or null. Names already loaded count as present, whatever the version.
		private static string MissingRef(Assembly asm, Dictionary<string, bool> loaded)
		{
			AssemblyName[] refs;
			try
			{
				refs = asm.GetReferencedAssemblies();
			}
			catch (Exception)
			{
				return "(reference list unreadable)";
			}
			for (int i = 0; i < refs.Length; i++)
			{
				if (loaded.ContainsKey(refs[i].Name))
				{
					continue;
				}
				try
				{
					Assembly.Load(refs[i]);
					loaded[refs[i].Name] = true;
				}
				catch (Exception)
				{
					return refs[i].FullName;
				}
			}
			return null;
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

		private static void ScanAssembly(Assembly asm, List<MethodBase> targets, Dictionary<int, bool> unsafeMethods, ref int methods, ref int skipped, ref int notRead)
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
						// Before any other reflection on the method: reading one that reaches a missing runtime member can close the game.
						if (unsafeMethods.ContainsKey(list[m].MetadataToken))
						{
							notRead++;
							continue;
						}
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
						// Junk bytes (obfuscated IL) can read as a switch with a huge count: stop before allocating its table.
						if (n < 0 || n > (il.Length - pos - 4) / 4)
						{
							return false;
						}
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
			string key = MethodKey(m);
			if (SkipKeys.ContainsKey(key))
			{
				for (int i = 0; i < sites.Count; i++)
				{
					N.ULog.Warn(FastFixModule.Tag, sites[i].Text + " - not fixed: the game closed while patching this method at an earlier start");
				}
				Sites.Remove(m.MethodHandle.Value);
				return;
			}
			Step("method", key);
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

	// ---------- Covariance reference pre-check ----------
	// Reads the metadata tables of a mod DLL from its file (no reflection on the mod): every TypeRef into a loaded game or
	// runtime assembly that has no such type, and every MemberRef to such a type that has no member of that name and parameter
	// count (a mod built for .NET 4: System.ValueTuple`2, Array.Empty<T> are not in this game's mscorlib 2.0). Reflection over a
	// method that reaches one of them (its signature, locals or an IL operand) can close the game inside Mono (Many Winged Angel
	// DIYMOD.dll IniFileHelper), so Covariance does not read those methods. References into other mod assemblies are not
	// checked (resolving their types would load them). Any doubt counts as unsafe.
	internal static class CovarianceMeta
	{
		// Returns null when the file was read; unsafeMethods gets the MethodDef tokens not to read, missing the names found.
		internal static string Check(Assembly asm, OpCode[] one, OpCode[] two, bool[] hasOne, bool[] hasTwo, Dictionary<int, bool> unsafeMethods, List<string> missing)
		{
			Reader r;
			try
			{
				r = new Reader(System.IO.File.ReadAllBytes(asm.Location), one, two, hasOne, hasTwo, missing);
			}
			catch (Exception e)
			{
				return e.GetType().Name + ": " + e.Message;
			}
			int n = r.Rows(0x06);
			bool failed = false;
			for (int i = 1; i <= n; i++)
			{
				bool bad;
				try
				{
					bad = r.MethodUnsafe(i);
				}
				catch (Exception e)
				{
					bad = true;
					if (!failed)
					{
						failed = true;
						missing.Add("(unreadable method 0x" + (0x06000000 | i).ToString("X8") + ": " + e.GetType().Name + ")");
					}
				}
				if (bad)
				{
					unsafeMethods[0x06000000 | i] = true;
				}
			}
			return null;
		}

		private sealed class Reader
		{
			private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

			// Column codes: 1 u16, 2 u32, 3 string, 4 guid, 5 blob, 100 + table = table index, 200 + n = coded index n.
			private static readonly int[][] Schema = new int[][]
			{
				new int[] { 1, 3, 4, 4, 4 },
				new int[] { 211, 3, 3 },
				new int[] { 2, 3, 3, 200, 104, 106 },
				new int[] { 104 },
				new int[] { 1, 3, 5 },
				new int[] { 106 },
				new int[] { 2, 1, 1, 3, 5, 108 },
				new int[] { 108 },
				new int[] { 1, 1, 3 },
				new int[] { 102, 200 },
				new int[] { 205, 3, 5 },
				new int[] { 1, 201, 5 },
				new int[] { 202, 210, 5 },
				new int[] { 203, 5 },
				new int[] { 1, 204, 5 },
				new int[] { 1, 2, 102 },
				new int[] { 2, 104 },
				new int[] { 5 },
				new int[] { 102, 120 },
				new int[] { 120 },
				new int[] { 1, 3, 200 },
				new int[] { 102, 123 },
				new int[] { 123 },
				new int[] { 1, 3, 5 },
				new int[] { 1, 106, 206 },
				new int[] { 102, 207, 207 },
				new int[] { 3 },
				new int[] { 5 },
				new int[] { 1, 208, 3, 126 },
				new int[] { 2, 104 },
				new int[] { 2, 2 },
				new int[] { 2 },
				new int[] { 2, 1, 1, 1, 1, 2, 5, 3, 3 },
				new int[] { 2 },
				new int[] { 2, 2, 2 },
				new int[] { 1, 1, 1, 1, 2, 5, 3, 3, 5 },
				new int[] { 2, 135 },
				new int[] { 2, 2, 2, 135 },
				new int[] { 2, 3, 5 },
				new int[] { 2, 2, 3, 3, 209 },
				new int[] { 2, 2, 3, 209 },
				new int[] { 102, 102 },
				new int[] { 1, 1, 212, 3 },
				new int[] { 207, 5 },
				new int[] { 142, 200 }
			};

			// ECMA-335 II.24.2.6: TypeDefOrRef, HasConstant, HasCustomAttribute, HasFieldMarshal, HasDeclSecurity, MemberRefParent,
			// HasSemantics, MethodDefOrRef, MemberForwarded, Implementation, CustomAttributeType, ResolutionScope, TypeOrMethodDef.
			private static readonly int[][] Coded = new int[][]
			{
				new int[] { 0x02, 0x01, 0x1B },
				new int[] { 0x04, 0x08, 0x17 },
				new int[] { 0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B },
				new int[] { 0x04, 0x08 },
				new int[] { 0x02, 0x06, 0x20 },
				new int[] { 0x02, 0x01, 0x1A, 0x06, 0x1B },
				new int[] { 0x14, 0x17 },
				new int[] { 0x06, 0x0A },
				new int[] { 0x04, 0x06 },
				new int[] { 0x26, 0x23, 0x27 },
				new int[] { -1, -1, 0x06, 0x0A, -1 },
				new int[] { 0x00, 0x1A, 0x23, 0x01 },
				new int[] { 0x02, 0x06 }
			};

			private static readonly int[] CodedBits = new int[] { 2, 2, 5, 1, 2, 3, 1, 1, 1, 2, 3, 2, 1 };

			private readonly byte[] _d;
			private readonly OpCode[] _one;
			private readonly OpCode[] _two;
			private readonly bool[] _hasOne;
			private readonly bool[] _hasTwo;
			private readonly List<string> _missing;
			private readonly int[] _rows = new int[64];
			private readonly int[] _tableAt = new int[64];
			private readonly int[] _rowSize = new int[64];
			private readonly int[][] _colAt = new int[64][];
			private readonly int[][] _colSize = new int[64][];
			private int[] _secVa;
			private int[] _secSize;
			private int[] _secRaw;
			private int _strings;
			private int _blob;
			private int _strSize;
			private int _guidSize;
			private int _blobSize;
			// 0 unknown, 1 safe, 2 unsafe, 3 being checked.
			private int[] _typeRef;
			private Type[] _typeRefType;
			private int[] _typeSpec;
			private int[] _memberRef;
			private readonly Dictionary<int, bool> _blobBad = new Dictionary<int, bool>();
			private readonly Dictionary<string, Assembly> _game = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, bool> _mods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<Type, MethodBase[]> _methods = new Dictionary<Type, MethodBase[]>();

			internal Reader(byte[] d, OpCode[] one, OpCode[] two, bool[] hasOne, bool[] hasTwo, List<string> missing)
			{
				_d = d;
				_one = one;
				_two = two;
				_hasOne = hasOne;
				_hasTwo = hasTwo;
				_missing = missing;
				ReadPe();
				_typeRef = new int[_rows[0x01] + 1];
				_typeRefType = new Type[_rows[0x01] + 1];
				_typeSpec = new int[_rows[0x1B] + 1];
				_memberRef = new int[_rows[0x0A] + 1];
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					string name;
					string location;
					try
					{
						if (all[i] is AssemblyBuilder)
						{
							continue;
						}
						name = all[i].GetName().Name;
						location = all[i].Location;
					}
					catch (Exception)
					{
						continue;
					}
					// No location = loaded from bytes, most likely by a mod: never reflected over here.
					if (string.IsNullOrEmpty(location) || location.Replace('/', '\\').IndexOf("\\BaseMods\\", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						_mods[name] = true;
					}
					else if (!_game.ContainsKey(name))
					{
						_game[name] = all[i];
					}
				}
			}

			internal int Rows(int table)
			{
				return _rows[table];
			}

			private int U16(int o)
			{
				return _d[o] | (_d[o + 1] << 8);
			}

			private int U32(int o)
			{
				return _d[o] | (_d[o + 1] << 8) | (_d[o + 2] << 16) | (_d[o + 3] << 24);
			}

			private int Offset(int rva)
			{
				for (int i = 0; i < _secVa.Length; i++)
				{
					if (rva >= _secVa[i] && rva < _secVa[i] + _secSize[i])
					{
						return rva - _secVa[i] + _secRaw[i];
					}
				}
				throw new FormatException("rva 0x" + rva.ToString("X") + " outside the sections");
			}

			private void ReadPe()
			{
				if (U16(0) != 0x5A4D)
				{
					throw new FormatException("no MZ header");
				}
				int pe = U32(0x3C);
				if (U32(pe) != 0x4550)
				{
					throw new FormatException("no PE header");
				}
				int sections = U16(pe + 6);
				int optSize = U16(pe + 20);
				int opt = pe + 24;
				int magic = U16(opt);
				int dirs = magic == 0x20B ? opt + 112 : opt + 96;
				if (magic != 0x10B && magic != 0x20B)
				{
					throw new FormatException("unknown optional header 0x" + magic.ToString("X"));
				}
				_secVa = new int[sections];
				_secSize = new int[sections];
				_secRaw = new int[sections];
				int sec = opt + optSize;
				for (int i = 0; i < sections; i++)
				{
					int s = sec + i * 40;
					_secVa[i] = U32(s + 12);
					_secSize[i] = U32(s + 16);
					_secRaw[i] = U32(s + 20);
				}
				int cli = U32(dirs + 14 * 8);
				if (cli == 0)
				{
					throw new FormatException("no CLI header");
				}
				int meta = Offset(U32(Offset(cli) + 8));
				if (U32(meta) != 0x424A5342)
				{
					throw new FormatException("no metadata signature");
				}
				int verLen = U32(meta + 12);
				int p = meta + 16 + verLen;
				int streams = U16(p + 2);
				p += 4;
				int tables = -1;
				for (int i = 0; i < streams; i++)
				{
					int off = U32(p);
					int start = p + 8;
					int end = start;
					while (_d[end] != 0)
					{
						end++;
					}
					string name = System.Text.Encoding.ASCII.GetString(_d, start, end - start);
					p = start + ((end - start + 4) & ~3);
					if (name == "#~" || name == "#-")
					{
						tables = meta + off;
					}
					else if (name == "#Strings")
					{
						_strings = meta + off;
					}
					else if (name == "#Blob")
					{
						_blob = meta + off;
					}
				}
				if (tables < 0 || _strings == 0 || _blob == 0)
				{
					throw new FormatException("metadata streams missing");
				}
				int heaps = _d[tables + 6];
				_strSize = (heaps & 1) != 0 ? 4 : 2;
				_guidSize = (heaps & 2) != 0 ? 4 : 2;
				_blobSize = (heaps & 4) != 0 ? 4 : 2;
				long valid = (uint)U32(tables + 8) | ((long)(uint)U32(tables + 12) << 32);
				p = tables + 24;
				for (int t = 0; t < 64; t++)
				{
					if ((valid & (1L << t)) != 0)
					{
						if (t >= Schema.Length)
						{
							throw new FormatException("unknown metadata table 0x" + t.ToString("X"));
						}
						_rows[t] = U32(p);
						p += 4;
					}
				}
				if ((heaps & 0x40) != 0)
				{
					p += 4;
				}
				for (int t = 0; t < Schema.Length; t++)
				{
					int[] cols = Schema[t];
					_colAt[t] = new int[cols.Length];
					_colSize[t] = new int[cols.Length];
					int size = 0;
					for (int c = 0; c < cols.Length; c++)
					{
						_colAt[t][c] = size;
						_colSize[t][c] = ColSize(cols[c]);
						size += _colSize[t][c];
					}
					_rowSize[t] = size;
					_tableAt[t] = p;
					p += size * _rows[t];
				}
				if (p > _d.Length)
				{
					throw new FormatException("metadata tables past the end of the file");
				}
			}

			private int ColSize(int code)
			{
				if (code == 1)
				{
					return 2;
				}
				if (code == 2)
				{
					return 4;
				}
				if (code == 3)
				{
					return _strSize;
				}
				if (code == 4)
				{
					return _guidSize;
				}
				if (code == 5)
				{
					return _blobSize;
				}
				if (code < 200)
				{
					return _rows[code - 100] < 0x10000 ? 2 : 4;
				}
				int[] tables = Coded[code - 200];
				int max = 0;
				for (int i = 0; i < tables.Length; i++)
				{
					if (tables[i] >= 0 && _rows[tables[i]] > max)
					{
						max = _rows[tables[i]];
					}
				}
				return max < (1 << (16 - CodedBits[code - 200])) ? 2 : 4;
			}

			private int Col(int table, int row, int col)
			{
				int o = _tableAt[table] + (row - 1) * _rowSize[table] + _colAt[table][col];
				return _colSize[table][col] == 2 ? U16(o) : U32(o);
			}

			private string Str(int index)
			{
				int start = _strings + index;
				int end = start;
				while (_d[end] != 0)
				{
					end++;
				}
				return System.Text.Encoding.UTF8.GetString(_d, start, end - start);
			}

			private int Comp(ref int p)
			{
				int b = _d[p];
				if ((b & 0x80) == 0)
				{
					p += 1;
					return b;
				}
				if ((b & 0xC0) == 0x80)
				{
					int v2 = ((b & 0x3F) << 8) | _d[p + 1];
					p += 2;
					return v2;
				}
				int v4 = ((b & 0x1F) << 24) | (_d[p + 1] << 16) | (_d[p + 2] << 8) | _d[p + 3];
				p += 4;
				return v4;
			}

			// Start of the blob data; len = its length.
			private int Blob(int index, out int len)
			{
				int p = _blob + index;
				len = Comp(ref p);
				if (p + len > _d.Length)
				{
					throw new FormatException("blob past the end of the file");
				}
				return p;
			}

			private bool InRange(int table, int row)
			{
				return row >= 1 && row <= _rows[table];
			}

			private void Missing(string name)
			{
				if (_missing.Count < 16 && !_missing.Contains(name))
				{
					_missing.Add(name);
				}
			}

			internal bool MethodUnsafe(int row)
			{
				if (BlobBad(Col(0x06, row, 4)))
				{
					return true;
				}
				int rva = Col(0x06, row, 0);
				if (rva == 0)
				{
					return false;
				}
				int o = Offset(rva);
				int head = _d[o];
				int start;
				int size;
				bool sections = false;
				if ((head & 3) == 2)
				{
					size = head >> 2;
					start = o + 1;
				}
				else if ((head & 3) == 3)
				{
					size = U32(o + 4);
					int locals = U32(o + 8);
					start = o + ((U16(o) >> 12) * 4);
					sections = (U16(o) & 0x08) != 0;
					if (locals != 0 && TokenBad(locals))
					{
						return true;
					}
				}
				else
				{
					return true;
				}
				if (size < 0 || start + size > _d.Length)
				{
					return true;
				}
				int i = start;
				int end = start + size;
				while (i < end)
				{
					int b = _d[i++];
					OpCode op;
					if (b == 0xFE)
					{
						if (i >= end || !_hasTwo[_d[i]])
						{
							return true;
						}
						op = _two[_d[i++]];
					}
					else
					{
						if (!_hasOne[b])
						{
							return true;
						}
						op = _one[b];
					}
					switch (op.OperandType)
					{
						case OperandType.InlineNone:
							break;
						case OperandType.ShortInlineBrTarget:
						case OperandType.ShortInlineI:
						case OperandType.ShortInlineVar:
							i += 1;
							break;
						case OperandType.InlineVar:
							i += 2;
							break;
						case OperandType.InlineI:
						case OperandType.InlineBrTarget:
						case OperandType.InlineString:
						case OperandType.ShortInlineR:
							i += 4;
							break;
						case OperandType.InlineI8:
						case OperandType.InlineR:
							i += 8;
							break;
						case OperandType.InlineSwitch:
							if (i + 4 > end)
							{
								return true;
							}
							long count = (uint)U32(i);
							i += 4;
							if (count > (end - i) / 4)
							{
								return true;
							}
							i += (int)count * 4;
							break;
						case OperandType.InlineField:
						case OperandType.InlineMethod:
						case OperandType.InlineSig:
						case OperandType.InlineTok:
						case OperandType.InlineType:
							if (i + 4 > end || TokenBad(U32(i)))
							{
								return true;
							}
							i += 4;
							break;
						default:
							return true;
					}
				}
				if (i != end)
				{
					return true;
				}
				return sections && HandlersBad(end);
			}

			// Exception sections after a fat body (II.25.4.5): Mono loads every catch class when it reads the method header.
			private bool HandlersBad(int end)
			{
				int p = (end + 3) & ~3;
				while (true)
				{
					int kind = _d[p];
					bool fat = (kind & 0x40) != 0;
					int size = fat ? (_d[p + 1] | (_d[p + 2] << 8) | (_d[p + 3] << 16)) : _d[p + 1];
					if ((kind & 0x01) == 0 || size < 4 || p + size > _d.Length)
					{
						return true;
					}
					int clause = fat ? 24 : 12;
					for (int c = p + 4; c + clause <= p + size; c += clause)
					{
						int flags = fat ? U32(c) : U16(c);
						if (flags == 0 && TokenBad(U32(c + clause - 4)))
						{
							return true;
						}
					}
					if ((kind & 0x80) == 0)
					{
						return false;
					}
					p = (p + size + 3) & ~3;
				}
			}

			private bool TokenBad(int token)
			{
				int table = (int)((uint)token >> 24);
				int row = token & 0xFFFFFF;
				switch (table)
				{
					case 0x01:
						return TypeRefBad(row);
					case 0x02:
						return !InRange(0x02, row);
					case 0x04:
						return !InRange(0x04, row) || BlobBad(Col(0x04, row, 2));
					case 0x06:
						return !InRange(0x06, row) || BlobBad(Col(0x06, row, 4));
					case 0x0A:
						return MemberRefBad(row);
					case 0x11:
						return !InRange(0x11, row) || BlobBad(Col(0x11, row, 0));
					case 0x1B:
						return TypeSpecBad(row);
					case 0x2B:
						if (!InRange(0x2B, row))
						{
							return true;
						}
						int method = Col(0x2B, row, 0);
						if ((method & 1) == 0 ? TokenBad(0x06000000 | (method >> 1)) : MemberRefBad(method >> 1))
						{
							return true;
						}
						return BlobBad(Col(0x2B, row, 1));
					case 0x70:
						return false;
					default:
						return true;
				}
			}

			private bool TypeRefBad(int row)
			{
				if (!InRange(0x01, row))
				{
					return true;
				}
				int state = _typeRef[row];
				if (state != 0)
				{
					return state != 1;
				}
				_typeRef[row] = 3;
				bool bad;
				try
				{
					bad = ResolveTypeRef(row);
				}
				catch (Exception)
				{
					bad = true;
				}
				_typeRef[row] = bad ? 2 : 1;
				return bad;
			}

			// Sets _typeRefType[row] for a game or runtime type; null = a type of this module or of another mod (not checked).
			private bool ResolveTypeRef(int row)
			{
				int scope = Col(0x01, row, 0);
				string name = Str(Col(0x01, row, 1));
				string ns = Str(Col(0x01, row, 2));
				string full = ns.Length > 0 ? ns + "." + name : name;
				int index = scope >> 2;
				switch (scope & 3)
				{
					case 2:
						if (!InRange(0x23, index))
						{
							return true;
						}
						string asmName = Str(Col(0x23, index, 6));
						Assembly a;
						if (!_game.TryGetValue(asmName, out a))
						{
							if (_mods.ContainsKey(asmName))
							{
								return false;
							}
							Missing("[" + asmName + "] " + full);
							return true;
						}
						Type t = a.GetType(full, false);
						if (object.ReferenceEquals(t, null))
						{
							Missing(full);
							return true;
						}
						_typeRefType[row] = t;
						return false;
					case 3:
						if (TypeRefBad(index))
						{
							return true;
						}
						Type outer = _typeRefType[index];
						if (object.ReferenceEquals(outer, null))
						{
							return false;
						}
						Type nested = outer.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
						if (object.ReferenceEquals(nested, null))
						{
							Missing(outer.FullName + "+" + name);
							return true;
						}
						_typeRefType[row] = nested;
						return false;
					default:
						return false;
				}
			}

			private bool TypeSpecBad(int row)
			{
				if (!InRange(0x1B, row))
				{
					return true;
				}
				int state = _typeSpec[row];
				if (state != 0)
				{
					return state != 1;
				}
				_typeSpec[row] = 3;
				bool bad;
				try
				{
					int len;
					int p = Blob(Col(0x1B, row, 0), out len);
					bad = TypeBad(ref p, p + len);
				}
				catch (Exception)
				{
					bad = true;
				}
				_typeSpec[row] = bad ? 2 : 1;
				return bad;
			}

			// The game type a generic instance TypeSpec is made of (List`1 for List<T>), or null.
			private Type SpecType(int row)
			{
				int len;
				int p = Blob(Col(0x1B, row, 0), out len);
				if (len < 2 || _d[p] != 0x15)
				{
					return null;
				}
				p += 2;
				int coded = Comp(ref p);
				return (coded & 3) == 1 ? _typeRefType[coded >> 2] : null;
			}

			private bool MemberRefBad(int row)
			{
				if (!InRange(0x0A, row))
				{
					return true;
				}
				int state = _memberRef[row];
				if (state != 0)
				{
					return state != 1;
				}
				_memberRef[row] = 3;
				bool bad;
				try
				{
					bad = ResolveMemberRef(row);
				}
				catch (Exception)
				{
					bad = true;
				}
				_memberRef[row] = bad ? 2 : 1;
				return bad;
			}

			private bool ResolveMemberRef(int row)
			{
				int parent = Col(0x0A, row, 0);
				int sig = Col(0x0A, row, 2);
				if (BlobBad(sig))
				{
					return true;
				}
				int index = parent >> 3;
				Type owner = null;
				switch (parent & 7)
				{
					case 1:
						if (TypeRefBad(index))
						{
							return true;
						}
						owner = _typeRefType[index];
						break;
					case 4:
						if (TypeSpecBad(index))
						{
							return true;
						}
						owner = SpecType(index);
						break;
					case 0:
					case 2:
					case 3:
						return false;
					default:
						return true;
				}
				if (object.ReferenceEquals(owner, null))
				{
					return false;
				}
				string name = Str(Col(0x0A, row, 1));
				if (HasMember(owner, name, sig))
				{
					return false;
				}
				Missing(owner.FullName + "::" + name);
				return true;
			}

			private bool HasMember(Type owner, string name, int sig)
			{
				int len;
				int p = Blob(sig, out len);
				if (len == 0)
				{
					return false;
				}
				int end = p + len;
				int cc = _d[p++];
				if (cc == 0x06)
				{
					for (Type t = owner; !object.ReferenceEquals(t, null); t = t.BaseType)
					{
						if (!object.ReferenceEquals(t.GetField(name, All), null))
						{
							return true;
						}
					}
					return false;
				}
				if ((cc & 0x10) != 0)
				{
					Comp(ref p);
				}
				int count = Comp(ref p);
				// Parameter types as far as they are known here (null = any): a .NET 4 overload with the same count as an old one
				// (String.Join(string, IEnumerable<string>) next to Join(string, string[])) must not count as present.
				ParamShape(ref p, end);
				Type[] want = new Type[count];
				bool[] generic = new bool[count];
				for (int k = 0; k < count; k++)
				{
					want[k] = ParamShape(ref p, end);
					generic[k] = _lastGeneric;
				}
				bool ctor = name == ".ctor" || name == ".cctor";
				for (Type t = owner; !object.ReferenceEquals(t, null); t = ctor ? null : t.BaseType)
				{
					MethodBase[] ms;
					if (!_methods.TryGetValue(t, out ms))
					{
						List<MethodBase> list = new List<MethodBase>();
						list.AddRange(t.GetMethods(All));
						list.AddRange(t.GetConstructors(All));
						ms = list.ToArray();
						_methods[t] = ms;
					}
					for (int i = 0; i < ms.Length; i++)
					{
						if (ms[i].Name == name && ParamsMatch(ms[i].GetParameters(), want, generic))
						{
							return true;
						}
					}
				}
				return false;
			}

			private static bool ParamsMatch(ParameterInfo[] ps, Type[] want, bool[] generic)
			{
				int need = want.Length;
				if (ps.Length != need)
				{
					return false;
				}
				for (int k = 0; k < ps.Length; k++)
				{
					if (object.ReferenceEquals(want[k], null))
					{
						continue;
					}
					Type pt = ps[k].ParameterType;
					if (generic[k])
					{
						if (!pt.IsGenericType || !object.ReferenceEquals(pt.GetGenericTypeDefinition(), want[k]))
						{
							return false;
						}
					}
					else if (!object.ReferenceEquals(pt, want[k]))
					{
						return false;
					}
				}
				return true;
			}

			private bool _lastGeneric;

			// One type of a signature: the game type it names, its generic definition (_lastGeneric = true) or null when it
			// cannot be compared here (mod types, generic parameters, by-ref, pointers, arrays of those). Advances p past it.
			private Type ParamShape(ref int p, int end)
			{
				_lastGeneric = false;
				int at = p;
				int b = _d[p];
				Type simple = Primitive(b);
				if (!object.ReferenceEquals(simple, null))
				{
					p++;
					return simple;
				}
				if (b == 0x11 || b == 0x12)
				{
					p++;
					int coded = Comp(ref p);
					return (coded & 3) == 1 && InRange(0x01, coded >> 2) ? _typeRefType[coded >> 2] : null;
				}
				if (b == 0x1D)
				{
					p++;
					Type inner = ParamShape(ref p, end);
					bool innerGeneric = _lastGeneric;
					_lastGeneric = false;
					return object.ReferenceEquals(inner, null) || innerGeneric ? null : inner.MakeArrayType();
				}
				if (b == 0x15)
				{
					p += 2;
					int coded = Comp(ref p);
					int args = Comp(ref p);
					for (int i = 0; i < args; i++)
					{
						ParamShape(ref p, end);
					}
					Type def = (coded & 3) == 1 && InRange(0x01, coded >> 2) ? _typeRefType[coded >> 2] : null;
					_lastGeneric = !object.ReferenceEquals(def, null);
					return def;
				}
				p = at;
				TypeBad(ref p, end);
				_lastGeneric = false;
				return null;
			}

			private static Type Primitive(int b)
			{
				switch (b)
				{
					case 0x01:
						return typeof(void);
					case 0x02:
						return typeof(bool);
					case 0x03:
						return typeof(char);
					case 0x04:
						return typeof(sbyte);
					case 0x05:
						return typeof(byte);
					case 0x06:
						return typeof(short);
					case 0x07:
						return typeof(ushort);
					case 0x08:
						return typeof(int);
					case 0x09:
						return typeof(uint);
					case 0x0A:
						return typeof(long);
					case 0x0B:
						return typeof(ulong);
					case 0x0C:
						return typeof(float);
					case 0x0D:
						return typeof(double);
					case 0x0E:
						return typeof(string);
					case 0x18:
						return typeof(IntPtr);
					case 0x19:
						return typeof(UIntPtr);
					case 0x1C:
						return typeof(object);
					default:
						return null;
				}
			}

			private bool BlobBad(int index)
			{
				bool bad;
				if (_blobBad.TryGetValue(index, out bad))
				{
					return bad;
				}
				_blobBad[index] = true;
				try
				{
					int len;
					int p = Blob(index, out len);
					bad = SigBad(ref p, p + len);
				}
				catch (Exception)
				{
					bad = true;
				}
				_blobBad[index] = bad;
				return bad;
			}

			// A field, method, property, local or method-instance signature (II.23.2).
			private bool SigBad(ref int p, int end)
			{
				if (p >= end)
				{
					return false;
				}
				int cc = _d[p++];
				int count;
				if (cc == 0x06)
				{
					return TypeBad(ref p, end);
				}
				if (cc == 0x07 || cc == 0x0A)
				{
					count = Comp(ref p);
				}
				else
				{
					if ((cc & 0x10) != 0)
					{
						Comp(ref p);
					}
					count = Comp(ref p) + 1;
				}
				for (int i = 0; i < count; i++)
				{
					if (TypeBad(ref p, end))
					{
						return true;
					}
				}
				return false;
			}

			private bool TypeBad(ref int p, int end)
			{
				while (true)
				{
					if (p >= end)
					{
						throw new FormatException("signature ends early");
					}
					int b = _d[p++];
					switch (b)
					{
						case 0x1F:
						case 0x20:
							if (TypeDefOrRefBad(Comp(ref p)))
							{
								return true;
							}
							continue;
						case 0x0F:
						case 0x10:
						case 0x1D:
						case 0x41:
						case 0x45:
							continue;
						case 0x11:
						case 0x12:
							return TypeDefOrRefBad(Comp(ref p));
						case 0x13:
						case 0x1E:
							Comp(ref p);
							return false;
						case 0x14:
							if (TypeBad(ref p, end))
							{
								return true;
							}
							Comp(ref p);
							int sizes = Comp(ref p);
							for (int i = 0; i < sizes; i++)
							{
								Comp(ref p);
							}
							int bounds = Comp(ref p);
							for (int i = 0; i < bounds; i++)
							{
								Comp(ref p);
							}
							return false;
						case 0x15:
							p++;
							if (TypeDefOrRefBad(Comp(ref p)))
							{
								return true;
							}
							int args = Comp(ref p);
							for (int i = 0; i < args; i++)
							{
								if (TypeBad(ref p, end))
								{
									return true;
								}
							}
							return false;
						case 0x1B:
							return SigBad(ref p, end);
						default:
							if ((b >= 0x01 && b <= 0x0E) || b == 0x16 || b == 0x18 || b == 0x19 || b == 0x1C)
							{
								return false;
							}
							throw new FormatException("unknown element type 0x" + b.ToString("X"));
					}
				}
			}

			private bool TypeDefOrRefBad(int coded)
			{
				switch (coded & 3)
				{
					case 0:
						return !InRange(0x02, coded >> 2);
					case 1:
						return TypeRefBad(coded >> 2);
					case 2:
						return TypeSpecBad(coded >> 2);
					default:
						return true;
				}
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
	// Four Fools (1.47): Harmony_Patch's static fields load sprites in its cctor, before its constructor sets Harmony_Patch.path,
	// so zkBufSprite stays null (no icon on the Zkbuf buf). It is loaded again through the mod's own AssetGetter once path is
	// set; no patch. The three "Failed To Load Sprite" lines of the cctor stay (they come before any mod can act).
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
		private static bool _fdDone;
		private static bool _riskDone;
		private static bool _featherDone;
		private static bool _puLaoDone;
		private static bool _pinoEscDone;
		private static bool _floodDone;
		private static bool _beastDone;
		private static bool _yaziDone;
		private static bool _bbDeathDone;
		private static bool _whaleRoamDone;
		private static bool _fourFoolsDone;
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
				InstallCreatureNoAnim();
				InstallDamageFactor();
				InstallSoundDeQueue();
				InstallBulletMax();
				InstallAttackEnd();
				InstallPassageRect();
				InstallNodeQuiet();
				InstallNoNameQuiet();
				InstallChildViewWait();
				InstallAddCompTrace();
				InstallMoveQuiet();
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
			if (!_riskDone)
			{
				_riskDone = Mod("kekesi_CustomRisk", "FastFix.CustomRiskChild", "kekesi CustomRisk: clicking a child creature without its own observation data no longer throws and breaks the command window. Read at game start.", last, InstallCustomRisk);
			}
			if (!_featherDone)
			{
				_featherDone = Mod("FeatherMemories", "FastFix.FeatherHardMode", "Bloody Past (FeatherMemories): its hard-mode setting is read from disk once instead of every tick. Read at game start.", last, InstallFeatherHardMode);
			}
			if (!_fdDone)
			{
				_fdDone = Mod("FD", "FastFix.FDTick", "FD (Harikjay Krau Deluge): a unit in its room no longer closes the game (List<AgentModel> read as IEnumerable<UnitModel>), also without 12Harmony. Read at game start.", last, InstallFdTick);
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
			if (!_fourFoolsDone)
			{
				_fourFoolsDone = Mod("FourFools", "FastFix.FourFoolsBufIcon", "Four Fools: the Zkbuf buf gets its icon (the mod reads it before it knows its folder). Read at game start.", last, InstallFourFools);
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
			bool batch = InstallBatch(last);
			return _blackBirdDone && _pinoDone && _boneFishDone && _dragonDone && _flowerDone && _isomerDone
				&& _toyDone && _invSefiraDone && _invPathDone && _pinoRangeDone && _rainDone && _qiuDone && _niuDone && _hookahDone
				&& _whaleDone && _charlotteDone && _kekesiDone && _fdDone && _riskDone && _featherDone && _puLaoDone
				&& _pinoEscDone && _floodDone && _beastDone && _yaziDone && _bbDeathDone && _whaleRoamDone
				&& _qianRuDone && _cauldronDone && _ribbonDone && _cloudDone && _yaziEscDone && _grunglyDone && _bbPassageDone && _fourFoolsDone && batch;
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

		// ----- mod batch 2026-11-15: Blood Shoes, Fairy Queen, Raspberry Apostle, Charming Pond, TurbidityCurrent, Burrowing Heaven -----

		private static bool _shoesDone;
		private static bool _queenDone;
		private static bool _raspDone;
		private static bool _pondDone;
		private static bool _turbDone;
		private static bool _heavenDone;
		private static bool _scytheDone;
		private static bool _wrResetDone;
		private static bool _brClearDone;
		private static bool _efDone;
		private static bool _introDone;
		private static bool _bxDone;
		private static bool _tmDone;
		private static bool _roseDone;
		private static bool _nothingDone;
		private static bool _trioDone;
		private static bool _rabbitRangeDone;
		private static bool _bleedEndDone;
		private static bool _giftFixDone;
		private static bool _shoesDirDone;
		private static bool _walpDone;
		private static bool _poolDone;
		private static bool _upDone;
		private static bool _lingDone;
		private static bool _incCheckDone;
		private static bool _ladySpecialDone;
		private static bool _tiamatLayerDone;
		private static bool _hmiHoverDone;
		private static bool _fewSuppressDone;
		private static bool _hslebSuppressDone;
		private static bool _fourFoolsSelectDone;
		private static bool _incIdDone;
		private static bool _sheepDone;

		private static bool InstallBatch(bool last)
		{
			if (!_shoesDone)
			{
				_shoesDone = ModByType("BloodShoesCreature.BloodShoes", "FastFix.BloodShoesFix", "Blood Shoes: an escape of any abnormality no longer throws when vanilla Red Shoes is absent; a normal work result no longer lowers the counter (a good one may, as its tip says); the shoes no longer freeze after a hit. Read at game start.", last, InstallBloodShoes);
			}
			if (!_queenDone)
			{
				_queenDone = ModByType("FairyQueen", "FastFix.FairyQueenFix", "Fairy Queen: Prepared Meat marks end with the day; after suppression its hunger restarts instead of holding the counter at 0. Read at game start.", last, InstallFairyQueen);
			}
			if (!_raspDone)
			{
				_raspDone = ModByType("Raspberry_Apostle.Raspberry_Apostle", "FastFix.RaspberryWork", "Raspberry Apostle: a work it or its bushes paused can no longer stay paused forever. Read at game start.", last, InstallRaspberry);
			}
			if (!_pondDone)
			{
				_pondDone = ModByType("Charming.Charming", "FastFix.CharmingPond", "Charming Pond: its internal list is created, so its work effects, recall kill and day-end cleanup run; ponds left in hallways from earlier days are removed. Read at game start.", last, InstallCharmingPond);
			}
			if (!_turbDone)
			{
				_turbDone = ModByType("TurbidityCurrent.BufAdder", "FastFix.TurbidityParalyze", "Dream-Devouring Slitcurrent: a unit without a body in the water no longer gets the paralysis that throws every tick (water that never recedes). Read at game start.", last, InstallTurbidity);
			}
			if (!_heavenDone)
			{
				_heavenDone = ModByType("Permeate_heaven", "FastFix.BurrowingHeavenLure", "Burrowing Heaven: no lure while it is being worked on; clerks are no longer lured (their lure broke and blocked the next ones). Read at game start.", last, InstallHeaven);
			}
			if (!_scytheDone)
			{
				_scytheDone = ModByType("SilentPriceReboot.ScytheClockReboot", "FastFix.ScytheClockCount", "Silent Price (Scythe Clock Reboot): its kill counter display no longer reads past its 13 texts (an error every tick at 13 kills, and on room setup). Read at game start.", last, InstallScythe);
			}
			if (!_wrResetDone)
			{
				_wrResetDone = ModByType("WhiteRabbit.Harmony_Patch", "FastFix.WhiteRabbitReset", "Celestial Bunny: restoring normal speed after its slowdown no longer stops at the first door or unit (collection changed while read). Read at game start.", last, InstallRabbitReset);
			}
			if (!_brClearDone)
			{
				_brClearDone = ModByType("BloodRabbit_Mod.BloodPassageMaker", "FastFix.BloodRabbitClear", "Gory Bunny: clearing its blood pools in a hallway no longer stops at the first pool (collection changed while read). Read at game start.", last, InstallBloodRabbit);
			}
			if (!_efDone)
			{
				_efDone = ModByType("EternalFlower.EternalFlowerWeapon", "FastFix.EternalFlowerThornCap", "Flower of Eternity Remake 1.0.1: its weapon no longer adds rooms to the department hallway list for good and makes at most FastFix.EternalFlowerThorns thorns every 4 s (own hallway first). Read at game start.", last, InstallEternalFlower);
			}
			if (!_introDone)
			{
				_introDone = ModByType("IntroAdder.Harmony_Patch", "FastFix.IntroChain", "IntroAdder splash pictures (LCPD_Statement) play first, then the intro of another intro mod (Wonderlab) instead of only one of them. Read at game start.", last, InstallIntroChain);
			}
			if (!_bxDone)
			{
				_bxDone = ModByType("Baxia_And_Fuxi.Creature.BaxiaAndFuxi", "FastFix.BaxiaObserve", "Baxia and Fuxi: a reset of observation data no longer empties their shared observation tables and no longer throws on regions already there. Read at game start.", last, InstallBaxia);
			}
			if (!_tmDone)
			{
				_tmDone = ModByType("Tiamat.Tiamat", "FastFix.TiamatFix", "Tiamat: stays in its own department after meltdown children (also fixes saves where it moved); only servants' works count; meltdown and lure errors no longer break the overload or the lure timer. Read at game start.", last, InstallTiamat);
			}
			if (!_roseDone)
			{
				_roseDone = ModByType("Sign_of_Roses.SignofRosesChild", "FastFix.SignOfRosesTarget", "Sign of Roses: when the employee its rose marked dies of something else, the rose no longer lowers a random abnormality's counter every tick (mass breaches, a full creature list copied every tick); it counts as having no target, as the mod does after its own kill. Read at game start.", last, InstallRose);
			}
			if (!_nothingDone)
			{
				_nothingDone = ModByType("NothingIs.HP.Creature", "FastFix.NothingIsTitle", "Nobody Is: the title of its disguised agent no longer throws and writes an error on every UI refresh when the creature data is missing. Read at game start.", last, InstallNothingTitle);
			}
			if (!_trioDone)
			{
				_trioDone = ModByType("MagicalTrio.Harmony_Patch", "FastFix.MagicalTrioOwner", "Magical Trio: a hit by a weapon with no owner no longer throws in the mod's set check (the set bonus needs an owner). Read at game start.", last, InstallTrio);
			}
			if (!_rabbitRangeDone)
			{
				_rabbitRangeDone = ModByType("WhiteRabbit.WhiteRabbit", "FastFix.WhiteRabbitRange", "Celestial Bunny: damage with no attacker no longer throws in its range check (counts as out of range). Read at game start.", last, InstallRabbitRange);
			}
			if (!_sheepDone)
			{
				_sheepDone = ModByType("Joker_MOD.Sheep", "FastFix.JokerSheepDamage", "Penitent Clown (Joker): its sheep no longer throw when they attack outside a room or when a target dies during the attack; the same targets take the same damage and panic buf. Read at game start.", last, InstallSheep);
			}
			if (!_incIdDone)
			{
				_incIdDone = ModByType("Incubator.Incubator", "FastFix.IncubatorModId", "Incubator: the mod looks up its abnormalities and EGO under the id BaseMod gave its folder (empty when the game language has no info.xml of the mod), so the Walpurgis Night lasers, witches made by the Incubator and its EGO checks are found again. Saves are not changed. Read at game start.", last, InstallIncModId);
			}
			if (!_walpDone)
			{
				_walpDone = ModByType("Incubator.WalpurgisNight.WalpurgisNight", "FastFix.WalpurgisDayStart", "Incubator: a Walpurgis Night kept in containment no longer makes the witches and the Knight of Despair escape and spawns its lasers while the day is being prepared (lasers failed there and left the creature without its node); both happen at the first frame of the day. Read at game start.", last, InstallWalp);
			}
			if (!_shoesDirDone)
			{
				_shoesDirDone = ModByType("BloodShoesCreature.BloodShoes", "FastFix.BloodShoesLogDir", "Blood Shoes: when the desktop LOG folder does not exist, the mod writes its files to a LOG folder in its own mod folder instead; without it the bleed buf fails to start. Read at game start.", last, InstallShoesDir);
			}
			if (!_giftFixDone)
			{
				_giftFixDone = ModByType("GiftFix.Harmony_Patch", "FastFix.GiftFixScript", "Incubator (GiftFix): giving an EGO gift whose script was not found no longer throws (the mod is built for a newer .NET and stops the day load); the script is set as the mod intends. Read at game start.", last, InstallGiftFix);
			}
			if (!_poolDone)
			{
				_poolDone = ModByType("Incubator.Incubator", "FastFix.IncubatorPoolDestroy", "Incubator: a witch suppressed by a hit inside a physics callback (Der Freischutz bullet) no longer logs 'Destroying components immediately is not permitted' when its animation goes back to the pool; the extra scripts on it are removed at the end of the frame instead of staying on the pooled animation. Read at game start.", last, InstallPool);
			}
			if (!_incCheckDone)
			{
				_incCheckDone = ModByType("Incubator.Incubator", "FastFix.IncubatorCheckAgent", "Incubator: an agent between rooms (on a hallway edge or an elevator) no longer throws in the Incubator's check for agents with low SP; the check stopped the Incubator's whole tick each time. That agent is checked again when it is in a room. Read at game start.", last, InstallIncCheck);
			}
			if (!_ladySpecialDone)
			{
				_ladySpecialDone = ModByType("GirlInRain.GirlInRainAnim", "FastFix.LadyInRainSpecial", "The Lady in Rain: when the mod picks the rare special version of an animation that its model does not have (Escapediespecial), the normal one is played instead of an error; the death motion is no longer lost. Read at game start.", last, InstallLadySpecial);
			}
			if (!_tiamatLayerDone)
			{
				_tiamatLayerDone = ModByType("TiamatEGOreader.Harmony_Patch", "FastFix.TiamatButtonLayer", "Tiamat EGO reader: its EGO switch buttons in the abnormality window no longer write a layer error to the log each time the window opens (the mod sets layer 999; Unity refused it and kept the layer, as now). Read at game start.", last, InstallTiamatLayer);
			}
			if (!_fewSuppressDone)
			{
				_fewSuppressDone = ModByType("FEW_RiskLevel.Harmony_Patch", "FastFix.FewSuppressChild", "Goddess of the Stars (FEW RiskLevel): clicking an escaped child creature (without its own observation data) no longer throws in the mod's risk level label of the suppress window; the window keeps the game's label. Read at game start.", last, InstallFewSuppress);
			}
			if (!_hslebSuppressDone)
			{
				_hslebSuppressDone = ModByType("HSLEB_RiskLevel.Harmony_Patch", "FastFix.HslebSuppressChild", "Souls Pawn Shop (HSLEB RiskLevel): clicking an escaped child creature (without its own observation data) no longer throws in the mod's risk level label of the suppress window; the window keeps the game's label. Read at game start.", last, InstallHslebSuppress);
			}
			if (!_fourFoolsSelectDone)
			{
				_fourFoolsSelectDone = ModByType("FourFools.HP_CSUnit", "FastFix.FourFoolsSelect", "Four Fools: the abnormality choice screen no longer throws when a choice door starts before its abnormality is set. Read at game start.", last, InstallFourFoolsSelect);
			}
			if (!_hmiHoverDone)
			{
				_hmiHoverDone = ModByType("HMI_AgentP_MOD.Harmony_Patch", "FastFix.HmiInfoHover", "I-1236 Branch (HMI_AgentP_MOD): hovering a closed area of the abnormality window while the window has no abnormality set no longer throws in the mod's hover check. Read at game start.", last, InstallHmiHover);
			}
			if (!_lingDone)
			{
				_lingDone = ModByType("Ling.Harmony_Patch", "FastFix.LingDragSelect", "Ling: an area select (left drag) over an object that is not a unit (an effect or bullet collider) no longer throws in the mod's check for its own creature. Read at game start.", last, InstallLingDrag);
			}
			if (!_upDone)
			{
				_upDone = ModByType("UncertainPageMod.UncertainPageScrollVisual", "FastFix.UncertainPagePortrait", "Uncertain Page: the abnormality picture on the page is looked up once per abnormality and kept; the mod's search walked every file of all mods and read every CreatureInfo file on each use (a long freeze when the item is used). The same picture is chosen. Read at game start.", last, InstallUpPortrait);
			}
			if (!_bleedEndDone)
			{
				_bleedEndDone = ModByType("Bleedbuf", "FastFix.BleedIconEnd", "Blood Shoes: the end of a bleed no longer throws in its icon update and writes an error file to the desktop. Read at game start.", last, InstallBleedEnd);
			}
			return _shoesDone && _queenDone && _raspDone && _pondDone && _turbDone && _heavenDone && _scytheDone && _wrResetDone && _brClearDone && _efDone && _introDone && _bxDone && _tmDone && _roseDone
				&& _nothingDone && _trioDone && _rabbitRangeDone && _bleedEndDone && _giftFixDone && _shoesDirDone && _walpDone && _incIdDone && _sheepDone && _poolDone && _upDone && _lingDone && _incCheckDone && _ladySpecialDone && _tiamatLayerDone && _hmiHoverDone && _fewSuppressDone && _hslebSuppressDone && _fourFoolsSelectDone;
		}

		private static readonly HashSet<string> _batchErr = new HashSet<string>();

		private static void BatchErr(string where, Exception e)
		{
			if (_batchErr.Add(where))
			{
				N.ULog.Warn(FastFixModule.Tag, "mod batch fix " + where + " failed (logged once): " + e.Message);
			}
		}

		private static bool ModByType(string typeName, string key, string desc, bool last, Installer install)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			Assembly found = null;
			for (int i = 0; i < all.Length && object.ReferenceEquals(found, null); i++)
			{
				try
				{
					if (!object.ReferenceEquals(all[i].GetType(typeName, false), null))
					{
						found = all[i];
					}
				}
				catch (Exception)
				{
				}
			}
			if (object.ReferenceEquals(found, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, key + " not applied: " + typeName + " not loaded");
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

		private static MethodInfo FindByName(Type t, string name, int args)
		{
			if (object.ReferenceEquals(t, null))
			{
				return null;
			}
			MethodInfo[] ms = t.GetMethods(All);
			for (int i = 0; i < ms.Length; i++)
			{
				if (string.Equals(ms[i].Name, name, StringComparison.Ordinal) && ms[i].GetParameters().Length == args)
				{
					return ms[i];
				}
			}
			return null;
		}

		// --- Blood Shoes ---
		// OnNotice(OnEscape) reads GetCreature(100003L).IsEscaped(); GetCreature matches the vanilla Red Shoes metadata id and returns null
		// when it is absent. Notice.Send has no catch, so the NRE leaves the escaping creature's Escape() and skips later observers.
		// With Red Shoes escaped it calls its own Escape() even when already escaped; Escape() sends OnEscape again, so the next
		// escape anywhere recursed until the stack overflowed (native crash, signature A; run mass-breach-1). 1.48: an OnEscape
		// notice is skipped while the shoes are escaped (the other branch handles OnAgentDead only).
		private static bool _shoesLoopLogged;
		private static FieldInfo _shoeAgent;
		private static Type _leftShoe;
		private static Type _rightShoe;
		private static FieldInfo _leftAttacking;
		private static FieldInfo _rightAttacking;
		private static FieldInfo _leftTimer;
		private static FieldInfo _rightTimer;
		private static FieldInfo _leftAnimScript;
		private static FieldInfo _rightAnimScript;
		private static FieldInfo _leftTarget;
		private static FieldInfo _rightTarget;
		private static readonly Dictionary<CreatureBase, float> _shoeStuck = new Dictionary<CreatureBase, float>();
		private const float ShoeStuckSeconds = 3f;

		private static void InstallBloodShoes(Assembly asm)
		{
			const string key = "FastFix.BloodShoesFix";
			Type t = asm.GetType("BloodShoesCreature.BloodShoes");
			MethodInfo notice = FindByName(t, "OnNotice", 2);
			MethodInfo release = Find(asm, "BloodShoesCreature.BloodShoes", "OnReleaseWork", new Type[] { typeof(UseSkill) });
			_shoeAgent = object.ReferenceEquals(t, null) ? null : t.GetField("agent", All);
			if (object.ReferenceEquals(notice, null) || object.ReferenceEquals(release, null))
			{
				Missing(key, "BloodShoesCreature.BloodShoes.OnNotice / OnReleaseWork");
				return;
			}
			FastFixModule.Harmony.Patch(notice, new HarmonyMethod(Own("ShoesNoticePrefix")), null);
			Patched(key, notice);
			FastFixModule.Harmony.Patch(release, new HarmonyMethod(Own("ShoesReleasePrefix")), new HarmonyMethod(Own("ShoesReleasePostfix")));
			Patched(key, release);
			_leftShoe = asm.GetType("LeftShoe");
			_rightShoe = asm.GetType("RightShoe");
			Type leftAnim = asm.GetType("LeftShoeAnim");
			Type rightAnim = asm.GetType("RightShoeAnim");
			_leftAttacking = object.ReferenceEquals(_leftShoe, null) ? null : _leftShoe.GetField("attacking", All);
			_rightAttacking = object.ReferenceEquals(_rightShoe, null) ? null : _rightShoe.GetField("attacking", All);
			_leftTimer = object.ReferenceEquals(_leftShoe, null) ? null : _leftShoe.GetField("attackTimer", All);
			_rightTimer = object.ReferenceEquals(_rightShoe, null) ? null : _rightShoe.GetField("attackTimer", All);
			_leftTarget = object.ReferenceEquals(_leftShoe, null) ? null : _leftShoe.GetField("_target", All);
			_rightTarget = object.ReferenceEquals(_rightShoe, null) ? null : _rightShoe.GetField("_target", All);
			_leftAnimScript = object.ReferenceEquals(leftAnim, null) ? null : leftAnim.GetField("_script", All);
			_rightAnimScript = object.ReferenceEquals(rightAnim, null) ? null : rightAnim.GetField("_script", All);
			MethodInfo leftHit = FindByName(leftAnim, "Attack", 2);
			MethodInfo rightHit = FindByName(rightAnim, "Attack", 2);
			if (object.ReferenceEquals(_leftAttacking, null) || object.ReferenceEquals(_rightAttacking, null)
				|| object.ReferenceEquals(_leftTimer, null) || object.ReferenceEquals(_rightTimer, null)
				|| !object.ReferenceEquals(_leftTimer.FieldType, typeof(Timer)) || !object.ReferenceEquals(_rightTimer.FieldType, typeof(Timer))
				|| object.ReferenceEquals(_leftTarget, null) || object.ReferenceEquals(_rightTarget, null)
				|| object.ReferenceEquals(_leftAnimScript, null) || object.ReferenceEquals(_rightAnimScript, null)
				|| object.ReferenceEquals(leftHit, null) || object.ReferenceEquals(rightHit, null))
			{
				Missing(key, "LeftShoe/RightShoe attacking, attackTimer, _target or the Anim Attack(TrackEntry, Event)");
				return;
			}
			FastFixModule.Harmony.Patch(leftHit, new HarmonyMethod(Own("ShoeHitPrefix")), null);
			Patched(key, leftHit);
			FastFixModule.Harmony.Patch(rightHit, new HarmonyMethod(Own("ShoeHitPrefix")), null);
			Patched(key, rightHit);
			// AddBleed and GiveDamagebyBleed run on the same Spine event and read _script._target without a check too.
			MethodInfo[] bleed = new MethodInfo[] { FindByName(leftAnim, "AddBleed", 2), FindByName(rightAnim, "AddBleed", 2), FindByName(leftAnim, "GiveDamagebyBleed", 2), FindByName(rightAnim, "GiveDamagebyBleed", 2) };
			for (int i = 0; i < bleed.Length; i++)
			{
				if (object.ReferenceEquals(bleed[i], null))
				{
					if (i < 3)
					{
						Missing(key, "LeftShoeAnim/RightShoeAnim AddBleed / GiveDamagebyBleed");
					}
					continue;
				}
				FastFixModule.Harmony.Patch(bleed[i], new HarmonyMethod(Own("ShoeHitPrefix")), null);
				Patched(key, bleed[i]);
			}
			// GetUnitBufByName compares GetType().Name: a buf of another mod also named Bleedbuf is found, "as Bleedbuf" is null
			// and GiveDamagebyBleed throws in the Spine event; such a hit is skipped. AddBleed also threw when the new buf was not
			// on the target after AddUnitBuf (mass-breach-25/26); it is done whole by ShoeAddBleedPrefix with the same counts.
			_bleedType = asm.GetType("Bleedbuf");
			_bleedCount = object.ReferenceEquals(_bleedType, null) ? null : _bleedType.GetField("count", All);
			_bleedPower = object.ReferenceEquals(_bleedType, null) ? null : _bleedType.GetField("power", All);
			_bleedCtor = object.ReferenceEquals(_bleedType, null) ? null : _bleedType.GetConstructor(new Type[] { typeof(UnitModel), typeof(UnitModel) });
			if (!object.ReferenceEquals(_bleedCount, null) && !object.ReferenceEquals(_bleedPower, null) && !object.ReferenceEquals(_bleedCtor, null)
				&& object.ReferenceEquals(_bleedCount.FieldType, typeof(int)) && object.ReferenceEquals(_bleedPower.FieldType, typeof(int)))
			{
				for (int i = 0; i < bleed.Length; i++)
				{
					if (!object.ReferenceEquals(bleed[i], null))
					{
						FastFixModule.Harmony.Patch(bleed[i], new HarmonyMethod(Own(i < 2 ? "ShoeAddBleedPrefix" : "ShoeBleedPrefix")), null);
					}
				}
				N.ULog.Info(FastFixModule.Tag, key + ": AddBleed done null-safe; GiveDamagebyBleed skips a target whose Bleedbuf is another mod's buf");
			}
			else
			{
				Missing(key, "Bleedbuf count / power / (UnitModel, UnitModel) constructor");
			}
			MethodInfo tick = typeof(ChildCreatureModel).GetMethod("OnFixedUpdate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(tick, null) || !object.ReferenceEquals(tick.DeclaringType, typeof(ChildCreatureModel)))
			{
				Missing(key, "ChildCreatureModel.OnFixedUpdate");
				return;
			}
			FastFixModule.Harmony.Patch(tick, null, new HarmonyMethod(Own("ShoeTickPostfix")));
			Patched(key, tick);
		}

		public static bool ShoesNoticePrefix(CreatureBase __instance, string notice)
		{
			try
			{
				if (string.Equals(notice, NoticeName.OnEscape, StringComparison.Ordinal) && CreatureManager.instance.GetCreature(100003L) == null)
				{
					return false;
				}
				if (string.Equals(notice, NoticeName.OnEscape, StringComparison.Ordinal) && __instance != null && __instance.model != null
					&& __instance.model.IsEscaped())
				{
					if (!_shoesLoopLogged)
					{
						_shoesLoopLogged = true;
						N.ULog.Info(FastFixModule.Tag, "FastFix.BloodShoesFix: OnEscape skipped for escaped Blood Shoes (its own Escape() would send OnEscape again without end; logged once)");
					}
					return false;
				}
			}
			catch (Exception e)
			{
				BatchErr("ShoesNoticePrefix", e);
			}
			return true;
		}

		// The tip says a GOOD result may lower the counter; the code rolls 50% on NORM. NORM: keep the agent field only
		// (CreatureBase.OnReleaseWork is empty); GOOD: the 50% roll in the postfix. BAD stays as it is.
		public static bool ShoesReleasePrefix(CreatureBase __instance, UseSkill skill)
		{
			try
			{
				if (skill != null && skill.GetCurrentFeelingState() == CreatureFeelingState.NORM)
				{
					if (!object.ReferenceEquals(_shoeAgent, null))
					{
						_shoeAgent.SetValue(__instance, skill.agent);
					}
					return false;
				}
			}
			catch (Exception e)
			{
				BatchErr("ShoesReleasePrefix", e);
			}
			return true;
		}

		public static void ShoesReleasePostfix(CreatureBase __instance, UseSkill skill)
		{
			try
			{
				if (skill != null && skill.GetCurrentFeelingState() == CreatureFeelingState.GOOD && UnityEngine.Random.value < 0.5f)
				{
					__instance.model.SubQliphothCounter();
				}
			}
			catch (Exception e)
			{
				BatchErr("ShoesReleasePostfix", e);
			}
		}

		// The "Damage" event hits _script._target without a check; after the target died the NRE also drops the Complete
		// callback that clears attacking, and the shoe stands still.
		private static Type _bleedType;
		private static FieldInfo _bleedCount;
		private static FieldInfo _bleedPower;
		private static ConstructorInfo _bleedCtor;
		private static bool _bleedForeignLogged;
		private static int _bleedAddMissed;

		// The original AddBleed, with checks. Left shoe: existing buf count +3, new buf count +2. Right shoe: existing buf
		// count +1 and power +1, new buf power +1. Always returns false.
		public static bool ShoeAddBleedPrefix(object __instance, Spine.Event e)
		{
			try
			{
				if (e == null || e.Data == null || !string.Equals(e.Data.Name, "Damage", StringComparison.Ordinal))
				{
					return false;
				}
				bool left = object.ReferenceEquals(__instance.GetType(), _leftAnimScript.DeclaringType);
				CreatureBase shoe = (left ? _leftAnimScript : _rightAnimScript).GetValue(__instance) as CreatureBase;
				UnitModel target = shoe == null ? null : (left ? _leftTarget : _rightTarget).GetValue(shoe) as UnitModel;
				if (target == null)
				{
					return false;
				}
				UnitBuf b = target.GetUnitBufByName("Bleedbuf");
				if (b != null)
				{
					if (_bleedType.IsInstanceOfType(b))
					{
						_bleedCount.SetValue(b, (int)_bleedCount.GetValue(b) + (left ? 3 : 1));
						if (!left)
						{
							_bleedPower.SetValue(b, (int)_bleedPower.GetValue(b) + 1);
						}
					}
					return false;
				}
				target.AddUnitBuf((UnitBuf)_bleedCtor.Invoke(new object[] { shoe.model, target }));
				b = target.GetUnitBufByName("Bleedbuf");
				if (b == null || !_bleedType.IsInstanceOfType(b))
				{
					_bleedAddMissed++;
					if (_bleedAddMissed <= 3)
					{
						N.ULog.Info(FastFixModule.Tag, "FastFix.BloodShoesFix: bleed did not stay on " + target.GetUnitName() + " (" + target.GetType().Name + ") after AddUnitBuf; hit left without bleed (" + _bleedAddMissed + "/3 logged)");
					}
					return false;
				}
				FieldInfo f = left ? _bleedCount : _bleedPower;
				f.SetValue(b, (int)f.GetValue(b) + (left ? 2 : 1));
			}
			catch (Exception ex)
			{
				Batch2Err("ShoeAddBleedPrefix", ex);
			}
			return false;
		}

		public static bool ShoeBleedPrefix(object __instance)
		{
			try
			{
				bool left = object.ReferenceEquals(__instance.GetType(), _leftAnimScript.DeclaringType);
				object shoe = (left ? _leftAnimScript : _rightAnimScript).GetValue(__instance);
				UnitModel target = shoe == null ? null : (left ? _leftTarget : _rightTarget).GetValue(shoe) as UnitModel;
				if (target == null)
				{
					return true;
				}
				UnitBuf b = target.GetUnitBufByName("Bleedbuf");
				if (b == null || _bleedType.IsInstanceOfType(b))
				{
					return true;
				}
				if (!_bleedForeignLogged)
				{
					_bleedForeignLogged = true;
					N.ULog.Info(FastFixModule.Tag, "FastFix.BloodShoesFix: " + target.GetUnitName() + " has " + b.GetType().FullName + " (another mod's Bleedbuf); the shoe's bleed hit is skipped (logged once)");
				}
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static bool ShoeHitPrefix(object __instance)
		{
			try
			{
				bool left = object.ReferenceEquals(__instance.GetType(), _leftAnimScript.DeclaringType);
				object shoe = (left ? _leftAnimScript : _rightAnimScript).GetValue(__instance);
				if (shoe == null)
				{
					return false;
				}
				UnitModel target = (left ? _leftTarget : _rightTarget).GetValue(shoe) as UnitModel;
				return target != null;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// attacking is cleared only by the attack animation's Complete; when it never comes the shoe is frozen.
		// After the 0.7 s attack timer ended, attacking that stays true for ShoeStuckSeconds is cleared.
		public static void ShoeTickPostfix(ChildCreatureModel __instance)
		{
			CreatureBase s = __instance.script;
			if (s == null)
			{
				return;
			}
			Type t = s.GetType();
			bool left = object.ReferenceEquals(t, _leftShoe);
			if (!left && !object.ReferenceEquals(t, _rightShoe))
			{
				return;
			}
			try
			{
				FieldInfo attacking = left ? _leftAttacking : _rightAttacking;
				Timer timer = (left ? _leftTimer : _rightTimer).GetValue(s) as Timer;
				if (!(bool)attacking.GetValue(s) || (timer != null && timer.started))
				{
					_shoeStuck.Remove(s);
					return;
				}
				float since;
				if (!_shoeStuck.TryGetValue(s, out since))
				{
					if (_shoeStuck.Count > 32)
					{
						_shoeStuck.Clear();
					}
					_shoeStuck[s] = Time.time;
					return;
				}
				if (Time.time - since >= ShoeStuckSeconds)
				{
					attacking.SetValue(s, false);
					_shoeStuck.Remove(s);
				}
			}
			catch (Exception e)
			{
				BatchErr("ShoeTickPostfix", e);
			}
		}

		// --- Fairy Queen ---
		// agentlist/agentlist2 ("Prepared Meat") are never cleared between days: two works yesterday + one today = death.
		// HungryBuf at 10 layers holds the counter at 0 every tick; after suppression nothing resets it until the next day.
		private static FieldInfo _fqList1;
		private static FieldInfo _fqList2;
		private static FieldInfo _fqHunger;
		private static FieldInfo _fqLayer;
		private static MethodInfo _fqReset;

		private static void InstallFairyQueen(Assembly asm)
		{
			const string key = "FastFix.FairyQueenFix";
			Type t = asm.GetType("FairyQueen");
			Type buf = asm.GetType("FairyQueen+HungryBuf");
			_fqList1 = object.ReferenceEquals(t, null) ? null : t.GetField("agentlist", All);
			_fqList2 = object.ReferenceEquals(t, null) ? null : t.GetField("agentlist2", All);
			_fqHunger = object.ReferenceEquals(t, null) ? null : t.GetField("HungryBuff", All);
			_fqLayer = object.ReferenceEquals(buf, null) ? null : buf.GetField("layer_count", All);
			_fqReset = object.ReferenceEquals(buf, null) ? null : buf.GetMethod("Reset", All, null, Type.EmptyTypes, null);
			MethodInfo end = Find(asm, "FairyQueen", "OnStageEnd", Type.EmptyTypes);
			MethodInfo rel = Find(asm, "FairyQueen", "OnStageRelease", Type.EmptyTypes);
			MethodInfo ret = Find(asm, "FairyQueen", "OnReturn", Type.EmptyTypes);
			if (object.ReferenceEquals(_fqList1, null) || object.ReferenceEquals(_fqList2, null) || object.ReferenceEquals(_fqHunger, null)
				|| object.ReferenceEquals(_fqLayer, null) || object.ReferenceEquals(end, null) || object.ReferenceEquals(rel, null) || object.ReferenceEquals(ret, null))
			{
				Missing(key, "FairyQueen agentlist/agentlist2/HungryBuff, OnStageEnd/OnStageRelease/OnReturn");
				return;
			}
			FastFixModule.Harmony.Patch(end, null, new HarmonyMethod(Own("QueenDayPostfix")));
			Patched(key, end);
			FastFixModule.Harmony.Patch(rel, null, new HarmonyMethod(Own("QueenDayPostfix")));
			Patched(key, rel);
			FastFixModule.Harmony.Patch(ret, null, new HarmonyMethod(Own("QueenReturnPostfix")));
			Patched(key, ret);
		}

		public static void QueenDayPostfix(object __instance)
		{
			try
			{
				System.Collections.IList a = _fqList1.GetValue(__instance) as System.Collections.IList;
				System.Collections.IList b = _fqList2.GetValue(__instance) as System.Collections.IList;
				if (a != null)
				{
					a.Clear();
				}
				if (b != null)
				{
					b.Clear();
				}
			}
			catch (Exception e)
			{
				BatchErr("QueenDayPostfix", e);
			}
		}

		public static void QueenReturnPostfix(CreatureBase __instance)
		{
			try
			{
				object hunger = _fqHunger.GetValue(__instance);
				if (hunger == null)
				{
					return;
				}
				try
				{
					_fqReset.Invoke(hunger, null);
				}
				catch (Exception)
				{
					_fqLayer.SetValue(hunger, 0);
				}
				if (__instance.model != null && __instance.model.qliphothCounter <= 0)
				{
					__instance.model.ResetQliphothCounter();
				}
			}
			catch (Exception e)
			{
				BatchErr("QueenReturnPostfix", e);
			}
		}

		// --- Raspberry Apostle ---
		// Boom pauses the skill; only the Spine Complete of that track entry resumes it. A second Boom on the same animator
		// replaces the entry, the first Complete never fires and the skill (often of another abnormality) stays paused.
		private static readonly Dictionary<UseSkill, float> _raspPaused = new Dictionary<UseSkill, float>();
		private const float RaspResumeSeconds = 15f;
		private static bool _raspLogged;

		private static void InstallRaspberry(Assembly asm)
		{
			const string key = "FastFix.RaspberryWork";
			MethodInfo boss = Find(asm, "Raspberry_Apostle.Raspberry_Apostle", "OnSkillWorkTick", new Type[] { typeof(UseSkill) });
			MethodInfo bush = Find(asm, "Raspberry_Apostle.Raspberry", "OnSkillWorkTick", new Type[] { typeof(UseSkill) });
			MethodInfo tick = typeof(UseSkill).GetMethod("OnFixedUpdate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(boss, null) || object.ReferenceEquals(bush, null) || object.ReferenceEquals(tick, null))
			{
				Missing(key, "Raspberry_Apostle.OnSkillWorkTick / Raspberry.OnSkillWorkTick / UseSkill.OnFixedUpdate");
				return;
			}
			FastFixModule.Harmony.Patch(boss, new HarmonyMethod(Own("RaspTickPrefix")), new HarmonyMethod(Own("RaspTickPostfix")));
			Patched(key, boss);
			FastFixModule.Harmony.Patch(bush, new HarmonyMethod(Own("RaspTickPrefix")), new HarmonyMethod(Own("RaspTickPostfix")));
			Patched(key, bush);
			FastFixModule.Harmony.Patch(tick, null, new HarmonyMethod(Own("RaspSkillPostfix")));
			Patched(key, tick);
		}

		public static bool RaspTickPrefix(UseSkill skill, ref bool __state)
		{
			__state = skill == null || skill.IsWorkPlaying;
			return __state;
		}

		public static void RaspTickPostfix(UseSkill skill, bool __state)
		{
			if (__state && skill != null && !skill.IsWorkPlaying && !_raspPaused.ContainsKey(skill))
			{
				if (_raspPaused.Count > 64)
				{
					RaspPrune();
				}
				_raspPaused[skill] = Time.time;
			}
		}

		// Entries of closed works never tick again; drop those older than a minute.
		private static void RaspPrune()
		{
			List<UseSkill> old = new List<UseSkill>();
			foreach (KeyValuePair<UseSkill, float> kv in _raspPaused)
			{
				if (Time.time - kv.Value > 60f)
				{
					old.Add(kv.Key);
				}
			}
			for (int i = 0; i < old.Count; i++)
			{
				_raspPaused.Remove(old[i]);
			}
		}

		public static void RaspSkillPostfix(UseSkill __instance)
		{
			if (_raspPaused.Count == 0)
			{
				return;
			}
			float since;
			if (!_raspPaused.TryGetValue(__instance, out since))
			{
				return;
			}
			if (__instance.IsWorkPlaying)
			{
				_raspPaused.Remove(__instance);
				return;
			}
			if (Time.time - since >= RaspResumeSeconds)
			{
				_raspPaused.Remove(__instance);
				try
				{
					__instance.ResumeWorking();
				}
				catch (Exception e)
				{
					BatchErr("RaspSkillPostfix", e);
				}
				if (!_raspLogged)
				{
					_raspLogged = true;
					N.ULog.Info(FastFixModule.Tag, "FastFix.RaspberryWork: a work paused by Raspberry Apostle for " + RaspResumeSeconds + " s was resumed");
				}
			}
		}

		// --- Charming Pond ---
		// _noiseArea has no initializer and is never assigned: every branch that touches it throws (work effects, recall kill,
		// counter 1 cleanup, ActivateQliphothCounter, OnStageRelease before the white pond cleanup). Ponds and their AutoTimers
		// then outlive the day. OnStageStart has no exception handlers; the prefix creates the list and removes stale ponds.
		private static FieldInfo _pondArea;
		private static FieldInfo _pondArea2;
		private static FieldInfo _pondTimer;
		private static FieldInfo _pondTimer2;
		private static FieldInfo _pondNoising;
		private static FieldInfo _pondNoising2;

		private static void InstallCharmingPond(Assembly asm)
		{
			const string key = "FastFix.CharmingPond";
			Type t = asm.GetType("Charming.Charming");
			MethodInfo start = Find(asm, "Charming.Charming", "OnStageStart", Type.EmptyTypes);
			if (!object.ReferenceEquals(t, null))
			{
				_pondArea = t.GetField("_noiseArea", All);
				_pondArea2 = t.GetField("_noiseArea2", All);
				_pondTimer = t.GetField("_noiseMakingTimer", All);
				_pondTimer2 = t.GetField("_noiseMakingTimer2", All);
				_pondNoising = t.GetField("_isNoising", All);
				_pondNoising2 = t.GetField("_isNoising2", All);
			}
			if (object.ReferenceEquals(start, null) || object.ReferenceEquals(_pondArea, null) || object.ReferenceEquals(_pondArea2, null)
				|| object.ReferenceEquals(_pondTimer, null) || object.ReferenceEquals(_pondTimer2, null)
				|| object.ReferenceEquals(_pondNoising, null) || object.ReferenceEquals(_pondNoising2, null))
			{
				Missing(key, "Charming.Charming OnStageStart / _noiseArea / _noiseArea2 / timers");
				return;
			}
			FastFixModule.Harmony.Patch(start, new HarmonyMethod(Own("PondStartPrefix")), null);
			Patched(key, start);
		}

		public static void PondStartPrefix(object __instance)
		{
			try
			{
				if (_pondArea.GetValue(__instance) == null)
				{
					_pondArea.SetValue(__instance, Activator.CreateInstance(_pondArea.FieldType));
				}
				AutoTimer t1 = _pondTimer.GetValue(__instance) as AutoTimer;
				AutoTimer t2 = _pondTimer2.GetValue(__instance) as AutoTimer;
				if (t1 != null)
				{
					t1.StopTimer();
				}
				if (t2 != null)
				{
					t2.StopTimer();
				}
				int n = PondClear(_pondArea.GetValue(__instance) as System.Collections.IList, "DestroyNoise") + PondClear(_pondArea2.GetValue(__instance) as System.Collections.IList, "DestroyNoise2");
				_pondNoising.SetValue(__instance, false);
				_pondNoising2.SetValue(__instance, false);
				if (n > 0)
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.CharmingPond: " + n + " pond(s) left from an earlier day removed");
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(FastFixModule.Tag, "FastFix.CharmingPond: stage start cleanup failed: " + e.Message);
			}
		}

		private static int PondClear(System.Collections.IList list, string destroy)
		{
			if (list == null || list.Count == 0)
			{
				return 0;
			}
			int n = list.Count;
			for (int i = 0; i < n; i++)
			{
				object p = list[i];
				if (p == null)
				{
					continue;
				}
				try
				{
					MethodInfo m = p.GetType().GetMethod(destroy, All, null, Type.EmptyTypes, null);
					if (!object.ReferenceEquals(m, null))
					{
						m.Invoke(p, null);
					}
				}
				catch (Exception e)
				{
					BatchErr("PondClear", e);
				}
			}
			list.Clear();
			return n;
		}

		// --- TurbidityCurrent ---
		// TurbidityParalyzeBuf.Init builds an icon only for a WorkerModel with a WorkerUnit or a CreatureModel with a Unit and then
		// uses it unconditionally: other units (rabbits, projectiles, dead workers) throw, and OnFixedUpdate stops every tick.
		private static void InstallTurbidity(Assembly asm)
		{
			const string key = "FastFix.TurbidityParalyze";
			MethodInfo m = Find(asm, "TurbidityCurrent.BufAdder", "AddCount_ParalyzeBuf", new Type[] { typeof(UnitModel), typeof(int) });
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, "TurbidityCurrent.BufAdder.AddCount_ParalyzeBuf(UnitModel, int)");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("TurbidityParalyzePrefix")), null);
			Patched(key, m);
		}

		public static bool TurbidityParalyzePrefix(UnitModel agent)
		{
			try
			{
				WorkerModel w = agent as WorkerModel;
				if (w != null)
				{
					return w.GetWorkerUnit() != null;
				}
				CreatureModel c = agent as CreatureModel;
				return c != null && c.Unit != null;
			}
			catch (Exception)
			{
				return false;
			}
		}

		// --- Burrowing Heaven ---
		// ActivateSkill runs from its 100 s timer without checking a running work: it cancels the work (OnReleaseWork then kills
		// the worker) and lures others. Officers pass CheckAttractCondition, but the lure action takes "worker as AgentModel"
		// (null for an officer) and throws every frame while the officer stays in the lured list.
		private static void InstallHeaven(Assembly asm)
		{
			const string key = "FastFix.BurrowingHeavenLure";
			MethodInfo m = Find(asm, "Permeate_heaven", "CheckAttractCondition", new Type[] { typeof(WorkerModel) });
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(bool)))
			{
				Missing(key, "Permeate_heaven.CheckAttractCondition(WorkerModel)");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("HeavenLurePrefix")), null);
			Patched(key, m);
		}

		public static bool HeavenLurePrefix(CreatureBase __instance, WorkerModel worker, ref bool __result)
		{
			try
			{
				if (!(worker is AgentModel) || (__instance.model != null && __instance.model.IsWorkingState()))
				{
					__result = false;
					return false;
				}
			}
			catch (Exception e)
			{
				BatchErr("HeavenLurePrefix", e);
			}
			return true;
		}

		// --- SilentPriceReboot (Scythe Clock) ---
		// KillCountTxtAry has 13 entries. OnFixedUpdate reads [KillCount] while the counter is 0, and KillCount reaches 13 before the
		// mass kill resets it: an IndexOutOfRangeException every tick. RoomSpriteInit reads [KillCount - 1] = [-1] while KillCount is 0.
		// Neither method has exception handlers.
		private static FieldInfo _scytheKills;

		private static void InstallScythe(Assembly asm)
		{
			const string key = "FastFix.ScytheClockCount";
			Type t = asm.GetType("SilentPriceReboot.ScytheClockReboot");
			_scytheKills = object.ReferenceEquals(t, null) ? null : t.GetField("KillCount", All);
			MethodInfo tick = Find(asm, "SilentPriceReboot.ScytheClockReboot", "OnFixedUpdate", new Type[] { typeof(CreatureModel) });
			MethodInfo room = Find(asm, "SilentPriceReboot.ScytheClockReboot", "RoomSpriteInit", Type.EmptyTypes);
			if (object.ReferenceEquals(_scytheKills, null) || !object.ReferenceEquals(_scytheKills.FieldType, typeof(int))
				|| object.ReferenceEquals(tick, null) || object.ReferenceEquals(room, null))
			{
				Missing(key, "SilentPriceReboot.ScytheClockReboot KillCount / OnFixedUpdate / RoomSpriteInit");
				return;
			}
			FastFixModule.Harmony.Patch(tick, new HarmonyMethod(Own("ScytheTickPrefix")), null);
			Patched(key, tick);
			FastFixModule.Harmony.Patch(room, new HarmonyMethod(Own("ScytheRoomPrefix")), new HarmonyMethod(Own("ScytheRoomPostfix")));
			Patched(key, room);
		}

		public static void ScytheTickPrefix(object __instance)
		{
			try
			{
				if ((int)_scytheKills.GetValue(__instance) > 12)
				{
					_scytheKills.SetValue(__instance, 12);
				}
			}
			catch (Exception e)
			{
				BatchErr("ScytheTickPrefix", e);
			}
		}

		public static void ScytheRoomPrefix(object __instance, ref bool __state)
		{
			__state = false;
			try
			{
				if ((int)_scytheKills.GetValue(__instance) <= 0)
				{
					_scytheKills.SetValue(__instance, 1);
					__state = true;
				}
			}
			catch (Exception e)
			{
				BatchErr("ScytheRoomPrefix", e);
			}
		}

		public static void ScytheRoomPostfix(object __instance, bool __state)
		{
			if (!__state)
			{
				return;
			}
			try
			{
				_scytheKills.SetValue(__instance, 0);
			}
			catch (Exception e)
			{
				BatchErr("ScytheRoomPostfix", e);
			}
		}

		// --- Celestial Bunny (WhiteRabbit) ---
		// ResetAnimatorSpeed removes from changedDoor / changedUnit inside the foreach over them: InvalidOperationException after the
		// first entry, the rest keep their slowed speed. Replacing prefix with the same steps over a copy.
		private static FieldInfo _wrDoors;
		private static FieldInfo _wrUnits;
		private static FieldInfo _wrNormal;
		private static MethodInfo _wrAnimSpeed;
		private static bool _wrLogged;

		private static void InstallRabbitReset(Assembly asm)
		{
			const string key = "FastFix.WhiteRabbitReset";
			Type t = asm.GetType("WhiteRabbit.Harmony_Patch");
			MethodInfo m = Find(asm, "WhiteRabbit.Harmony_Patch", "ResetAnimatorSpeed", Type.EmptyTypes);
			if (!object.ReferenceEquals(t, null))
			{
				_wrDoors = t.GetField("changedDoor", All);
				_wrUnits = t.GetField("changedUnit", All);
				_wrNormal = t.GetField("normalPassage", All);
				_wrAnimSpeed = t.GetMethod("ControlAnimSpeed", All);
			}
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_wrDoors, null) || object.ReferenceEquals(_wrUnits, null)
				|| object.ReferenceEquals(_wrNormal, null) || object.ReferenceEquals(_wrAnimSpeed, null)
				|| !object.ReferenceEquals(_wrDoors.FieldType, typeof(List<DoorObjectModel>))
				|| !object.ReferenceEquals(_wrUnits.FieldType, typeof(Dictionary<MovableObjectNode, Animator>)))
			{
				Missing(key, "WhiteRabbit.Harmony_Patch.ResetAnimatorSpeed / changedDoor / changedUnit / normalPassage / ControlAnimSpeed");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("RabbitResetPrefix")), null);
			Patched(key, m);
		}

		public static bool RabbitResetPrefix()
		{
			try
			{
				PassageObjectModel normal = _wrNormal.GetValue(null) as PassageObjectModel;
				List<DoorObjectModel> doors = _wrDoors.GetValue(null) as List<DoorObjectModel>;
				Dictionary<MovableObjectNode, Animator> units = _wrUnits.GetValue(null) as Dictionary<MovableObjectNode, Animator>;
				if (doors != null)
				{
					DoorObjectModel[] copy = doors.ToArray();
					for (int i = 0; i < copy.Length; i++)
					{
						DoorObjectModel d = copy[i];
						FieldInfo pf = d.GetType().GetField("passage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						PassageObjectModel p = object.ReferenceEquals(pf, null) ? null : pf.GetValue(d) as PassageObjectModel;
						if (p != normal)
						{
							PassageObject po = SefiraMapLayer.instance.GetPassageObject(normal);
							PassageDoor door = po == null ? null : po.GetDoorPassage(d.GetId());
							if (door != null && door.animator != null)
							{
								door.animator.speed = Time.timeScale;
							}
							doors.Remove(d);
						}
					}
				}
				if (units != null)
				{
					List<MovableObjectNode> keys = new List<MovableObjectNode>(units.Keys);
					for (int i = 0; i < keys.Count; i++)
					{
						MovableObjectNode node = keys[i];
						if (node == null || node.GetPassage() == normal)
						{
							continue;
						}
						UnitModel unit = node.GetUnit();
						Animator anim = units[node];
						if (unit is WorkerModel)
						{
							if (anim != null)
							{
								anim.speed = Time.timeScale;
							}
							(unit as WorkerModel).movementMul = Time.timeScale;
						}
						else if (unit is CreatureModel)
						{
							CreatureModel c = unit as CreatureModel;
							if (c.Unit != null)
							{
								_wrAnimSpeed.Invoke(null, new object[] { c.Unit.animTarget, Time.timeScale });
							}
							c.movementScale = Time.timeScale;
						}
						else if (unit is RabbitModel)
						{
							if (anim != null)
							{
								anim.speed = Time.timeScale;
							}
							(unit as RabbitModel).baseMovement = Time.timeScale;
						}
						units.Remove(node);
					}
				}
			}
			catch (Exception e)
			{
				if (!_wrLogged)
				{
					_wrLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "FastFix.WhiteRabbitReset failed (logged once): " + e.Message);
				}
			}
			return false;
		}

		// --- Gory Bunny (BloodRabbit) ---
		// ClearEffect calls DestroyFilter inside the foreach over effects, and DestroyFilter removes the entry from effects:
		// InvalidOperationException, the other pools of that passage stay. Replacing prefix over a copy.
		private static MethodInfo _brGet;
		private static FieldInfo _brEffects;

		private static void InstallBloodRabbit(Assembly asm)
		{
			const string key = "FastFix.BloodRabbitClear";
			Type maker = asm.GetType("BloodRabbit_Mod.BloodPassageMaker");
			Type rabbit = asm.GetType("BloodRabbit_Mod.BloodRabbit");
			MethodInfo m = Find(asm, "BloodRabbit_Mod.BloodPassageMaker", "ClearEffect", new Type[] { typeof(PassageObjectModel) });
			_brGet = object.ReferenceEquals(maker, null) ? null : maker.GetMethod("GetBloodRabbit", All, null, Type.EmptyTypes, null);
			_brEffects = object.ReferenceEquals(rabbit, null) ? null : rabbit.GetField("effects", All);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_brGet, null) || object.ReferenceEquals(_brEffects, null))
			{
				Missing(key, "BloodRabbit_Mod.BloodPassageMaker.ClearEffect / GetBloodRabbit / BloodRabbit.effects");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("BloodRabbitClearPrefix")), null);
			Patched(key, m);
		}

		public static bool BloodRabbitClearPrefix(PassageObjectModel passage)
		{
			try
			{
				object r = _brGet.Invoke(null, null);
				System.Collections.IList list = r == null ? null : _brEffects.GetValue(r) as System.Collections.IList;
				if (list == null || list.Count == 0)
				{
					return false;
				}
				object[] copy = new object[list.Count];
				list.CopyTo(copy, 0);
				for (int i = 0; i < copy.Length; i++)
				{
					object e = copy[i];
					if (e == null)
					{
						continue;
					}
					FieldInfo pf = e.GetType().GetField("passage", All);
					MethodInfo destroy = e.GetType().GetMethod("DestroyFilter", All, null, Type.EmptyTypes, null);
					if (!object.ReferenceEquals(pf, null) && !object.ReferenceEquals(destroy, null) && pf.GetValue(e) == passage)
					{
						destroy.Invoke(e, null);
					}
				}
			}
			catch (Exception e)
			{
				BatchErr("BloodRabbitClearPrefix", e);
			}
			return false;
		}

		// ----- mod batch 2 (2026-11-16): Flower of Eternity Remake 1.0.1, IntroAdder chain, Baxia and Fuxi, Tiamat -----

		private static string Inner(Exception e)
		{
			TargetInvocationException t = e as TargetInvocationException;
			return t != null && t.InnerException != null ? t.InnerException.GetType().Name + ": " + t.InnerException.Message : e.GetType().Name + ": " + e.Message;
		}

		private static void Batch2Err(string where, Exception e)
		{
			if (_batchErr.Add(where))
			{
				N.ULog.Warn(FastFixModule.Tag, "mod batch fix " + where + " failed (logged once): " + Inner(e));
			}
		}

		// --- Flower of Eternity Remake 1.0.1 (EternalFlower.EternalFlowerWeapon) ---
		// OnFixedUpdate every 4 s adds the room passage of every creature of the wielder's department to the game's own
		// sefira.passageList (never removed) and makes a thorn for every hostile unit in every passage of that list, without a cap.
		// The method has try/catch (GL-53), so the prefix only takes over the 4 s tick: it sets damageTime so the original branch
		// never fires and makes at most FastFix.EternalFlowerThorns thorns from a local passage list (own passage first).
		private static FieldInfo _efTime;
		private static MethodInfo _efThorn;
		private static MethodInfo _efHostile;
		private static PropertyInfo _efPassage;
		private static N.IntParam _efCap;
		private static readonly List<PassageObjectModel> _efPassages = new List<PassageObjectModel>();
		private static readonly List<UnitModel> _efTargets = new List<UnitModel>();
		private static readonly object[] _efArg = new object[1];

		private static void InstallEternalFlower(Assembly asm)
		{
			const string key = "FastFix.EternalFlowerThornCap";
			Type t = asm.GetType("EternalFlower.EternalFlowerWeapon");
			MethodInfo m = Find(asm, "EternalFlower.EternalFlowerWeapon", "OnFixedUpdate", Type.EmptyTypes);
			if (!object.ReferenceEquals(t, null))
			{
				BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				_efTime = t.GetField("damageTime", inst);
				_efThorn = t.GetMethod("MakeThorn", inst, null, new Type[] { typeof(UnitModel) }, null);
				_efHostile = t.GetMethod("IsHostile", inst, null, new Type[] { typeof(UnitModel) }, null);
				_efPassage = t.GetProperty("passage", inst);
			}
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_efTime, null) || !object.ReferenceEquals(_efTime.FieldType, typeof(float))
				|| object.ReferenceEquals(_efThorn, null) || object.ReferenceEquals(_efHostile, null) || !object.ReferenceEquals(_efHostile.ReturnType, typeof(bool))
				|| object.ReferenceEquals(_efPassage, null) || !object.ReferenceEquals(_efPassage.PropertyType, typeof(PassageObjectModel)))
			{
				Missing(key, "EternalFlower.EternalFlowerWeapon.OnFixedUpdate / damageTime / MakeThorn / IsHostile / passage");
				return;
			}
			_efCap = N.Config.BindInt("FastFix.EternalFlowerThorns", 12, 1, 100, 1, "Flower of Eternity Remake: most thorns one wielder of its weapon makes every 4 s (the mod makes one for every hostile unit in the department, without a cap).");
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("EternalFlowerTickPrefix")), null);
			Patched(key, m);
		}

		// Own 4 s clock per weapon. damageTime is held at 0 (one cached box), so the original's "+= deltaTime; >= 4" never
		// fires and no frame reads the field by reflection.
		private static readonly object _efZero = 0f;
		private static readonly Dictionary<EquipmentScriptBase, float> _efClock = new Dictionary<EquipmentScriptBase, float>();

		public static void EternalFlowerTickPrefix(EquipmentScriptBase __instance)
		{
			try
			{
				float t;
				_efClock.TryGetValue(__instance, out t);
				t += Time.deltaTime;
				_efTime.SetValue(__instance, _efZero);
				if (t < 4f)
				{
					_efClock[__instance] = t;
					return;
				}
				if (_efClock.Count > 256)
				{
					// weapons of dead or fired agents; a cleared clock only restarts its 4 s
					_efClock.Clear();
				}
				_efClock[__instance] = 0f;
				EternalFlowerThorns(__instance);
			}
			catch (Exception e)
			{
				Batch2Err("EternalFlowerTickPrefix", e);
			}
		}

		private static void EternalFlowerThorns(EquipmentScriptBase w)
		{
			PassageObjectModel own = _efPassage.GetValue(w, null) as PassageObjectModel;
			Sefira sefira = own == null ? null : own.GetSefira();
			if (sefira == null)
			{
				return;
			}
			_efPassages.Clear();
			_efTargets.Clear();
			_efPassages.Add(own);
			for (int i = 0; i < sefira.passageList.Count; i++)
			{
				PassageObjectModel p = sefira.passageList[i];
				if (p != null && !_efPassages.Contains(p))
				{
					_efPassages.Add(p);
				}
			}
			for (int i = 0; i < sefira.creatureList.Count; i++)
			{
				CreatureModel c = sefira.creatureList[i];
				PassageObjectModel p = c == null || c.roomNode == null ? null : c.roomNode.GetAttachedPassage();
				if (p != null && !_efPassages.Contains(p))
				{
					_efPassages.Add(p);
				}
			}
			int cap = _efCap == null ? 12 : _efCap.Value;
			for (int i = 0; i < _efPassages.Count && _efTargets.Count < cap; i++)
			{
				ReadOnlyCollection<MovableObjectNode> nodes = _efPassages[i].GetEnteredTargets();
				for (int j = 0; j < nodes.Count && _efTargets.Count < cap; j++)
				{
					UnitModel u = nodes[j] == null ? null : nodes[j].GetUnit();
					if (u == null || _efTargets.Contains(u))
					{
						continue;
					}
					_efArg[0] = u;
					try
					{
						if ((bool)_efHostile.Invoke(w, _efArg))
						{
							_efTargets.Add(u);
						}
					}
					catch (Exception e)
					{
						Batch2Err("EternalFlowerHostile", e);
					}
				}
			}
			for (int i = 0; i < _efTargets.Count; i++)
			{
				_efArg[0] = _efTargets[i];
				try
				{
					_efThorn.Invoke(w, _efArg);
				}
				catch (Exception e)
				{
					Batch2Err("EternalFlowerThorn", e);
				}
			}
			_efArg[0] = null;
			_efPassages.Clear();
			_efTargets.Clear();
		}

		// --- IntroAdder (LCPD_Statement: TurbidityCurrent, Drifting Fox splash pictures) and other intro mods ---
		// IntroAdder and Wonderlab (and similar mods) each replace IntroPlayer.Update with a prefix that returns false. Harmony 1.0.9.1
		// stops the prefix chain at the first false, so only one of them plays. A prefix at Priority.First runs IntroAdder until its
		// pictures are over (12 s + AddedIntroTime), then sets the clock back to 12 s and runs the first other replacing prefix, which
		// plays its own part and loads the title. Without IntroAdder pictures or another replacing prefix it changes nothing.
		private static MethodInfo _iaUpdate;
		private static FieldInfo _iaAdded;
		private static FieldInfo _iaIntros;
		private static Type _iaType;
		private static FieldInfo _ipElapsed;
		private static MethodInfo _icTarget;
		private static readonly List<Patch> _icList = new List<Patch>();
		private static readonly object[] _icArg = new object[1];
		private static int _icIdx;
		private static bool _icStarted;
		private static string _icOwner;
		private static int _icPhase;
		private static bool _icAdderErr;
		private const string LecOwner = "Lobotomy.kekesi.ErrorCheck";
		private static int _icLecWait = -1;

		private static void InstallIntroChain(Assembly asm)
		{
			const string key = "FastFix.IntroChain";
			_iaType = asm.GetType("IntroAdder.Harmony_Patch");
			_iaUpdate = Find(asm, "IntroAdder.Harmony_Patch", "IntroPlayer_Update", new Type[] { typeof(IntroPlayer) });
			_iaAdded = object.ReferenceEquals(_iaType, null) ? null : _iaType.GetField("AddedIntroTime", All);
			_iaIntros = object.ReferenceEquals(_iaType, null) ? null : _iaType.GetField("intros", All);
			_ipElapsed = typeof(IntroPlayer).GetField("_elapsedTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			_icTarget = typeof(IntroPlayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(_iaUpdate, null) || object.ReferenceEquals(_iaAdded, null) || !object.ReferenceEquals(_iaAdded.FieldType, typeof(float))
				|| object.ReferenceEquals(_ipElapsed, null) || !object.ReferenceEquals(_ipElapsed.FieldType, typeof(float)) || object.ReferenceEquals(_icTarget, null))
			{
				Missing(key, "IntroAdder.Harmony_Patch.IntroPlayer_Update / AddedIntroTime or IntroPlayer.Update / _elapsedTime");
				return;
			}
			FastFixModule.Harmony.Patch(_icTarget, N.PatchUtil.WithPriority(Own("IntroChainPrefix"), Priority.First), null);
			Patched(key, _icTarget);
		}

		public static bool IntroChainPrefix(IntroPlayer __instance)
		{
			try
			{
				if (_icPhase == 0)
				{
					_icPhase = IntroChainStart(__instance);
				}
				if (_icPhase == 1)
				{
					float end = 12f + (float)_iaAdded.GetValue(null);
					float before = (float)_ipElapsed.GetValue(__instance);
					if (before + Time.deltaTime < end)
					{
						try
						{
							_icArg[0] = __instance;
							_iaUpdate.Invoke(null, _icArg);
						}
						catch (Exception e)
						{
							// IntroAdder.NextIntro reads past its list when the last picture is skipped; the next frame ends this part.
							if (!_icAdderErr)
							{
								_icAdderErr = true;
								N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: IntroAdder threw once (" + Inner(e) + ")");
							}
							// It threw before moving the clock: move it here, or this part never ends.
							if ((float)_ipElapsed.GetValue(__instance) == before)
							{
								_ipElapsed.SetValue(__instance, before + Time.deltaTime);
							}
						}
						return false;
					}
					IntroChainHide();
					_ipElapsed.SetValue(__instance, 12f);
					_icPhase = 2;
					N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: IntroAdder pictures done, intro of " + _icOwner + " follows");
				}
				if (_icPhase == 2)
				{
					while (_icIdx < _icList.Count)
					{
						_icArg[0] = __instance;
						object r = _icList[_icIdx].patch.Invoke(null, _icArg);
						if (r is bool && !(bool)r)
						{
							_icStarted = true;
							return false;
						}
						if (_icStarted)
						{
							break;
						}
						// It let the intro run on its first call: not an intro of its own, try the next one.
						N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: intro prefix of " + _icList[_icIdx].owner + " does not replace the intro, next one tried");
						_icIdx++;
					}
					_icPhase = 3;
				}
			}
			catch (Exception e)
			{
				_icPhase = 3;
				Batch2Err("IntroChainPrefix", e);
			}
			return true;
		}

		// 1 = IntroAdder part first, 3 = leave the chain as it is.
		private static int IntroChainStart(IntroPlayer ip)
		{
			float added = (float)_iaAdded.GetValue(null);
			if (added <= 0f)
			{
				return 3;
			}
			// PerfPack.SkipIntroLogos starts the clock at 12.1 s: only the logos are skipped, the pictures still play first.
			if ((float)_ipElapsed.GetValue(ip) >= 12f + added)
			{
				N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: intro clock already past the IntroAdder pictures on its first frame, chain not used");
				return 3;
			}
			Patches info = PatchProcessor.IsPatched(_icTarget);
			if (info == null || info.Prefixes == null)
			{
				return 3;
			}
			_icList.Clear();
			for (int i = 0; i < info.Prefixes.Count; i++)
			{
				Patch p = info.Prefixes[i];
				MethodInfo pm = p == null ? null : p.patch;
				if (object.ReferenceEquals(pm, null) || string.Equals(p.owner, FastFixModule.Harmony.Id, StringComparison.Ordinal) || object.ReferenceEquals(pm.DeclaringType, _iaType))
				{
					continue;
				}
				// LoadErrorCheck's prefix is a gate (error screen up -> no intro), not an intro: never skipped by phase 1 while it works.
				if (string.Equals(p.owner, LecOwner, StringComparison.Ordinal))
				{
					if (SkippedByNail(pm))
					{
						continue;
					}
					// LoadGuard turns it off on its first frame; the order of the two in that frame is not fixed.
					if (_icLecWait < 0)
					{
						_icLecWait = Time.frameCount;
					}
					if (Time.frameCount - _icLecWait < 10)
					{
						return 0;
					}
					N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: LoadErrorCheck gate on IntroPlayer.Update is active, chain not used");
					return 3;
				}
				ParameterInfo[] ps = pm.GetParameters();
				if (!pm.IsStatic || !object.ReferenceEquals(pm.ReturnType, typeof(bool)) || ps == null || ps.Length != 1
					|| !ps[0].ParameterType.IsAssignableFrom(typeof(IntroPlayer)))
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: intro prefix of " + p.owner + " has another form, skipped");
					continue;
				}
				// Harmony order: higher priority first, then lower index.
				int at = 0;
				while (at < _icList.Count && (_icList[at].priority > p.priority || (_icList[at].priority == p.priority && _icList[at].index < p.index)))
				{
					at++;
				}
				_icList.Insert(at, p);
			}
			if (_icList.Count == 0)
			{
				return 3;
			}
			_icIdx = 0;
			_icStarted = false;
			_icOwner = _icList[0].owner;
			N.ULog.Info(FastFixModule.Tag, "FastFix.IntroChain: IntroAdder pictures (" + added.ToString("0.#") + " s) play before the intro of " + _icOwner);
			return 1;
		}

		// True when a NailOptimization module has put a skipping Prefix on this foreign patch method.
		private static bool SkippedByNail(MethodInfo pm)
		{
			Patches pi = PatchProcessor.IsPatched(pm);
			if (pi == null || pi.Prefixes == null)
			{
				return false;
			}
			for (int i = 0; i < pi.Prefixes.Count; i++)
			{
				Patch q = pi.Prefixes[i];
				if (q != null && q.owner != null && q.owner.StartsWith("Lobotomy.NailOptimization.", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static void IntroChainHide()
		{
			System.Collections.IList list = object.ReferenceEquals(_iaIntros, null) ? null : _iaIntros.GetValue(null) as System.Collections.IList;
			if (list == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				RawImage img = list[i] as RawImage;
				if (img != null)
				{
					img.color = new Color(1f, 1f, 1f, 0f);
				}
			}
		}

		// --- Baxia and Fuxi (Baxia_And_Fuxi.Creature) ---
		// The mod keeps both observation tables in static dictionaries and points the instance field observeRegions of 1998110 at
		// them. Vanilla InitObserveRegion starts with observeRegions.Clear(), so a reset of the observation data (CreatureManager
		// .ResetObserveData) empties the shared table; the mod's postfix then only Adds and throws on the first key already there
		// (also for every new CreatureObserveInfoModel of 1998110), which leaves the tables half filled.
		// Prefix on the vanilla method: a shared table is swapped for a scratch one before Clear. Replacing prefix on the mod's
		// postfix: adds missing regions only, a real reset clears the flags, and the instance gets its shared table back.
		private const long BaxiaId = 1998110L;
		private static FieldInfo _bxBaxia;
		private static FieldInfo _bxFuxi;
		private static FieldInfo _obsRegions;
		private static Dictionary<string, ObserveRegion> _bxWas;
		// Instance of the running vanilla InitObserveRegion: the mod's postfix is static, Harmony 1 cannot hand its argument as __instance.
		private static CreatureObserveInfoModel _bxInst;

		private static void InstallBaxia(Assembly asm)
		{
			const string key = "FastFix.BaxiaObserve";
			Type t = asm.GetType("Baxia_And_Fuxi.Creature.BaxiaAndFuxi");
			MethodInfo modPost = Find(asm, "Baxia_And_Fuxi.Creature.Harmony_Patch", "CreatureObserveInfoModel_InitObserveRegion", new Type[] { typeof(CreatureObserveInfoModel) });
			MethodInfo vanilla = typeof(CreatureObserveInfoModel).GetMethod("InitObserveRegion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(List<ObserveInfoData>) }, null);
			_obsRegions = typeof(CreatureObserveInfoModel).GetField("observeRegions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (!object.ReferenceEquals(t, null))
			{
				_bxBaxia = t.GetField("baxia_observe", All);
				_bxFuxi = t.GetField("fuxi_observe", All);
			}
			if (object.ReferenceEquals(modPost, null) || object.ReferenceEquals(vanilla, null) || object.ReferenceEquals(_obsRegions, null)
				|| object.ReferenceEquals(_bxBaxia, null) || object.ReferenceEquals(_bxFuxi, null)
				|| !object.ReferenceEquals(_bxBaxia.FieldType, typeof(Dictionary<string, ObserveRegion>))
				|| !object.ReferenceEquals(_bxFuxi.FieldType, typeof(Dictionary<string, ObserveRegion>)))
			{
				Missing(key, "Baxia_And_Fuxi baxia_observe / fuxi_observe / InitObserveRegion postfix or CreatureObserveInfoModel.InitObserveRegion");
				return;
			}
			FastFixModule.Harmony.Patch(vanilla, new HarmonyMethod(Own("BaxiaObservePrefix")), null);
			Patched(key, vanilla);
			FastFixModule.Harmony.Patch(modPost, new HarmonyMethod(Own("BaxiaRegionsPrefix")), null);
			Patched(key, modPost);
		}

		public static void BaxiaObservePrefix(CreatureObserveInfoModel __instance)
		{
			_bxWas = null;
			_bxInst = __instance;
			try
			{
				if (__instance.creatureTypeId != BaxiaId)
				{
					return;
				}
				Dictionary<string, ObserveRegion> cur = _obsRegions.GetValue(__instance) as Dictionary<string, ObserveRegion>;
				if (cur != null && (object.ReferenceEquals(cur, _bxBaxia.GetValue(null)) || object.ReferenceEquals(cur, _bxFuxi.GetValue(null))))
				{
					_bxWas = cur;
					_obsRegions.SetValue(__instance, new Dictionary<string, ObserveRegion>());
				}
			}
			catch (Exception e)
			{
				Batch2Err("BaxiaObservePrefix", e);
			}
		}

		public static bool BaxiaRegionsPrefix()
		{
			Dictionary<string, ObserveRegion> was = _bxWas;
			CreatureObserveInfoModel inst = _bxInst;
			_bxWas = null;
			_bxInst = null;
			try
			{
				if (inst == null || inst.creatureTypeId != BaxiaId)
				{
					return false;
				}
				bool reset = was != null;
				try
				{
					BaxiaAdd(_bxBaxia.GetValue(null) as Dictionary<string, ObserveRegion>, 1998111L, reset);
					BaxiaAdd(_bxFuxi.GetValue(null) as Dictionary<string, ObserveRegion>, 1998112L, reset);
				}
				finally
				{
					if (reset)
					{
						_obsRegions.SetValue(inst, was);
					}
				}
				if (reset)
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.BaxiaObserve: observation data of Baxia and Fuxi reset with the facility's");
				}
			}
			catch (Exception e)
			{
				Batch2Err("BaxiaRegionsPrefix", e);
			}
			return false;
		}

		private static void BaxiaAdd(Dictionary<string, ObserveRegion> dic, long id, bool reset)
		{
			if (dic == null)
			{
				return;
			}
			CreatureTypeInfo info = CreatureTypeList.instance.GetData_Mod(new LobotomyBaseMod.LcIdLong(id));
			if (info == null || info.observeData == null)
			{
				return;
			}
			for (int i = 0; i < info.observeData.Count; i++)
			{
				ObserveInfoData d = info.observeData[i];
				ObserveRegion r;
				if (dic.TryGetValue(d.regionName, out r))
				{
					if (reset)
					{
						r.isObserved = false;
					}
				}
				else
				{
					ObserveRegion n = new ObserveRegion();
					n.info = d;
					n.isObserved = false;
					dic.Add(d.regionName, n);
				}
			}
		}

		// --- Tiamat (Tiamat.Tiamat) ---
		// OnNotice has no try: an exception in its meltdown branch (MakeChildCreature without a free hallway) leaves
		// CreatureOverloadManager.SetQliphothOverloadLevel through Notice.Send. Its work branch counts a work of any agent on another
		// abnormality, its guide says only servants (buf 142801) count. MakeChildCreature writes the child's hallway department into
		// Tiamat's own sefira/sefiraNum; sefiraNum is saved, and after a reload Tiamat is listed under that other department.
		// ActivateSkill indexes an empty target list and throws before its 200 s timer restarts (luring stops for the day).
		private const int TiamatServantBuf = 142801;
		private static FieldInfo _tmWorking;
		private static FieldInfo _tmTiamat;
		private static FieldInfo _tmZList;
		private static MethodInfo _tmMakeChild;
		private static MethodInfo _tmCalamity;
		private static MethodInfo _tmCount;
		private static MethodInfo _tmTargets;
		private static int _tmMovedDay = -1;

		private static void InstallTiamat(Assembly asm)
		{
			const string key = "FastFix.TiamatFix";
			const string tn = "Tiamat.Tiamat";
			Type t = asm.GetType(tn);
			MethodInfo notice = Find(asm, tn, "OnNotice", new Type[] { typeof(string), typeof(object[]) });
			_tmMakeChild = Find(asm, tn, "MakeChildCreature", new Type[] { typeof(UnitModel), typeof(bool) });
			MethodInfo skill = Find(asm, tn, "ActivateSkill", Type.EmptyTypes);
			MethodInfo stage = Find(asm, tn, "OnStageStart", Type.EmptyTypes);
			_tmCalamity = Find(asm, tn, "SetBothCalamity", Type.EmptyTypes);
			_tmTargets = Find(asm, tn, "GetAttractTargets", Type.EmptyTypes);
			_tmCount = Find(asm, "Tiamat.TiamatDieCount", "Modify", new Type[] { typeof(UnitModel), typeof(int) });
			if (!object.ReferenceEquals(t, null))
			{
				_tmWorking = t.GetField("isOnWorking", All);
				_tmTiamat = t.GetField("tiamat", All);
				_tmZList = t.GetField("z_list", All);
			}
			if (object.ReferenceEquals(notice, null) || object.ReferenceEquals(_tmMakeChild, null) || object.ReferenceEquals(skill, null)
				|| object.ReferenceEquals(stage, null) || object.ReferenceEquals(_tmCalamity, null) || object.ReferenceEquals(_tmTargets, null)
				|| object.ReferenceEquals(_tmCount, null) || !_tmCount.IsStatic || object.ReferenceEquals(_tmWorking, null) || !object.ReferenceEquals(_tmWorking.FieldType, typeof(bool))
				|| object.ReferenceEquals(_tmTiamat, null) || !object.ReferenceEquals(_tmTiamat.FieldType, typeof(CreatureModel))
				|| object.ReferenceEquals(_tmZList, null) || !object.ReferenceEquals(_tmZList.FieldType, typeof(List<ChildCreatureModel>))
				|| !object.ReferenceEquals(_tmTargets.ReturnType, typeof(AgentModel[])))
			{
				Missing(key, "Tiamat.Tiamat OnNotice / MakeChildCreature / ActivateSkill / OnStageStart / fields or TiamatDieCount.Modify");
				return;
			}
			FastFixModule.Harmony.Patch(notice, new HarmonyMethod(Own("TiamatNoticePrefix")), null);
			Patched(key, notice);
			FastFixModule.Harmony.Patch(_tmMakeChild, new HarmonyMethod(Own("TiamatChildPrefix")), new HarmonyMethod(Own("TiamatChildPostfix")));
			Patched(key, _tmMakeChild);
			FastFixModule.Harmony.Patch(skill, new HarmonyMethod(Own("TiamatSkillPrefix")), null);
			Patched(key, skill);
			FastFixModule.Harmony.Patch(stage, null, new HarmonyMethod(Own("TiamatStagePostfix")));
			Patched(key, stage);
		}

		public static bool TiamatNoticePrefix(CreatureBase __instance, string notice, object[] param)
		{
			if (notice == NoticeName.OnQliphothOverloadLevelChanged)
			{
				try
				{
					if (param != null && param.Length != 0 && param[0] is int && (int)param[0] != 1)
					{
						EnergyModel.instance.SubEnergy(20f);
						List<ChildCreatureModel> z = _tmZList.GetValue(__instance) as List<ChildCreatureModel>;
						Sefira home = __instance.model == null ? null : __instance.model.sefira;
						for (int i = 0; i < 3; i++)
						{
							try
							{
								ChildCreatureModel c = _tmMakeChild.Invoke(__instance, new object[] { __instance.model, true }) as ChildCreatureModel;
								if (c != null && z != null)
								{
									z.Add(c);
								}
							}
							catch (Exception e)
							{
								// A throw after MakeChildCreature moved Tiamat skips its postfix: move it back here.
								TiamatChildPostfix(__instance, home);
								Batch2Err("TiamatChild", e);
							}
						}
						_tmCalamity.Invoke(__instance, null);
					}
				}
				catch (Exception e)
				{
					Batch2Err("TiamatMeltdown", e);
				}
				return false;
			}
			if (notice == NoticeName.OnWorkStart)
			{
				try
				{
					CreatureModel target = param != null && param.Length != 0 ? param[0] as CreatureModel : null;
					CreatureModel self = _tmTiamat.GetValue(__instance) as CreatureModel;
					if (target != null && self != null && target != self && !(bool)_tmWorking.GetValue(__instance))
					{
						AgentModel agent = target.currentSkill == null ? null : target.currentSkill.agent;
						if (agent != null && agent.HasUnitBuf((UnitBufType)TiamatServantBuf))
						{
							_tmCount.Invoke(null, new object[] { self, 1 });
						}
					}
				}
				catch (Exception e)
				{
					Batch2Err("TiamatWorkCount", e);
				}
				return false;
			}
			return true;
		}

		public static void TiamatChildPrefix(CreatureBase __instance, out Sefira __state)
		{
			__state = __instance.model == null ? null : __instance.model.sefira;
		}

		public static void TiamatChildPostfix(CreatureBase __instance, Sefira __state)
		{
			CreatureModel m = __instance.model;
			if (m != null && __state != null && m.sefira != __state)
			{
				m.sefira = __state;
				m.sefiraNum = __state.indexString;
			}
		}

		public static bool TiamatSkillPrefix(CreatureBase __instance)
		{
			try
			{
				AgentModel[] list = _tmTargets.Invoke(__instance, null) as AgentModel[];
				return list != null && list.Length != 0;
			}
			catch (Exception e)
			{
				Batch2Err("TiamatSkillPrefix", e);
				return false;
			}
		}

		// Saves written before this fix keep Tiamat's sefiraNum of a hallway department: the room is still built by its node
		// (emergency path), but the model is listed in the other department. Put it back into the department of its room.
		public static void TiamatStagePostfix(CreatureBase __instance)
		{
			try
			{
				CreatureModel m = __instance.model;
				PassageObjectModel room = m == null || m.roomNode == null ? null : m.roomNode.GetAttachedPassage();
				Sefira home = room == null ? null : SefiraManager.instance.GetSefira(room.GetSefiraName());
				if (home == null || (m.sefira == home && home.creatureList.Contains(m)))
				{
					return;
				}
				List<Sefira> all = SefiraManager.instance.sefiraList;
				for (int i = 0; all != null && i < all.Count; i++)
				{
					if (all[i] != null && all[i] != home)
					{
						all[i].creatureList.Remove(m);
					}
				}
				if (!home.creatureList.Contains(m))
				{
					home.creatureList.Add(m);
				}
				string was = m.sefiraNum;
				m.sefira = home;
				m.sefiraNum = home.indexString;
				m.sefiraOrigin = home;
				if (_tmMovedDay != PlayerModel.instance.GetDay())
				{
					_tmMovedDay = PlayerModel.instance.GetDay();
					N.ULog.Info(FastFixModule.Tag, "FastFix.TiamatFix: Tiamat listed under department " + was + " moved back to the department of its room (" + home.indexString + ")");
				}
			}
			catch (Exception e)
			{
				Batch2Err("TiamatStagePostfix", e);
			}
		}

		// --- Sign of Roses (Sign_of_Roses.SignofRosesChild) ---
		// UniqueEscape: at time >= 120 a living targetagent is killed, targetagent = null and time = 90. A target that died of
		// something else keeps targetagent set and time is never reset: every tick from then on copies the whole creature list and
		// calls SubQliphothCounter twice on a random abnormality. The prefix clears a dead target, so the mod's own "no target"
		// branch runs (one counter, time = 90), as after its own kill.
		private delegate AgentModel RoseGetFn(CreatureBase c);
		private delegate void RoseClearFn(CreatureBase c);
		private static RoseGetFn _roseGet;
		private static RoseClearFn _roseClear;

		private static void InstallRose(Assembly asm)
		{
			const string key = "FastFix.SignOfRosesTarget";
			Type t = asm.GetType("Sign_of_Roses.SignofRosesChild");
			MethodInfo m = Find(asm, "Sign_of_Roses.SignofRosesChild", "UniqueEscape", Type.EmptyTypes);
			FieldInfo f = object.ReferenceEquals(t, null) ? null : t.GetField("targetagent", All);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(f, null) || !object.ReferenceEquals(f.FieldType, typeof(AgentModel)) || !typeof(CreatureBase).IsAssignableFrom(t))
			{
				Missing(key, "Sign_of_Roses.SignofRosesChild.UniqueEscape / targetagent");
				return;
			}
			// UniqueEscape runs every tick: field access through emitted delegates, no reflection per call.
			DynamicMethod get = new DynamicMethod("FastFix_RoseGet", typeof(AgentModel), new Type[] { typeof(CreatureBase) }, t, true);
			ILGenerator il = get.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, t);
			il.Emit(OpCodes.Ldfld, f);
			il.Emit(OpCodes.Ret);
			DynamicMethod clear = new DynamicMethod("FastFix_RoseClear", typeof(void), new Type[] { typeof(CreatureBase) }, t, true);
			il = clear.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, t);
			il.Emit(OpCodes.Ldnull);
			il.Emit(OpCodes.Stfld, f);
			il.Emit(OpCodes.Ret);
			_roseGet = (RoseGetFn)get.CreateDelegate(typeof(RoseGetFn));
			_roseClear = (RoseClearFn)clear.CreateDelegate(typeof(RoseClearFn));
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("RoseTargetPrefix")), null);
			Patched(key, m);
		}

		public static void RoseTargetPrefix(CreatureBase __instance)
		{
			try
			{
				AgentModel a = _roseGet(__instance);
				if (a != null && a.IsDead())
				{
					_roseClear(__instance);
				}
			}
			catch (Exception e)
			{
				Batch2Err("RoseTargetPrefix", e);
			}
		}

		// --- mod batch 2026-11-18 (1.49): NothingIs title, MagicalTrio ownerless weapon, Celestial Bunny null attacker ---
		// NothingIs.HP.Creature.AgentModel_GetTitle (Postfix of AgentModel.GetTitle) reads actor.model.metaInfo.creatureStaticData
		// Prefix/Suffix; with a link missing every UI refresh throws inside it and its catch calls Debug.LogError (x684 in 4 min,
		// run mass-breach-2). Its parameters are named __instance/__result (no Harmony 1.0.9.1 patch can bind them) and its body
		// has try/catch (GL-53), so it is skipped as a whole and NothingTitlePostfix does the same with null checks (title left
		// as it was when a link is missing, as its catch did).
		// MagicalTrio.Harmony_Patch.EquipmentScriptBase_OnGiveDamage (Prefix) reads __instance.model.owner unchecked: a weapon
		// with no owner throws on every hit (x171). TrioCapturePrefix (first Prefix of EquipmentScriptBase.OnGiveDamage) keeps
		// the script; TrioSkipPrefix skips the mod's Prefix when that script has no model or owner.
		// WhiteRabbit.WhiteRabbit.IsInRange reads target.GetMovableNode(); CanTakeDamage calls it with a null attacker (x73) and
		// the hit is lost through the exception. A null target or node counts as out of range.
		private static Type _nothingAgent;
		private static FieldInfo _nothingActor;
		private static EquipmentScriptBase _trioScript;

		private static void InstallNothingTitle(Assembly asm)
		{
			const string key = "FastFix.NothingIsTitle";
			MethodInfo m = Find(asm, "NothingIs.HP.Creature", "AgentModel_GetTitle", new Type[] { typeof(AgentModel), typeof(string).MakeByRefType() });
			Type agent = asm.GetType("NothingIs.NothingIs_AgentModel");
			FieldInfo actor = object.ReferenceEquals(agent, null) ? null : agent.GetField("actor", All);
			MethodInfo title = typeof(AgentModel).GetMethod("GetTitle", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(actor, null) || object.ReferenceEquals(title, null)
				|| !typeof(CreatureBase).IsAssignableFrom(actor.FieldType))
			{
				Missing(key, "NothingIs.HP.Creature.AgentModel_GetTitle / NothingIs_AgentModel.actor");
				return;
			}
			_nothingAgent = agent;
			_nothingActor = actor;
			FastFixModule.Harmony.Patch(title, null, new HarmonyMethod(Own("NothingTitlePostfix")));
			Patched(key, title);
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("NothingTitleSkip")), null);
			Patched(key, m);
		}

		public static bool NothingTitleSkip()
		{
			return false;
		}

		public static void NothingTitlePostfix(AgentModel __instance, ref string __result)
		{
			try
			{
				if (__instance == null || !_nothingAgent.IsInstanceOfType(__instance))
				{
					return;
				}
				CreatureBase a = _nothingActor.GetValue(__instance) as CreatureBase;
				CreatureModel c = a == null ? null : a.model;
				if (c == null || c.metaInfo == null || c.metaInfo.creatureStaticData == null)
				{
					return;
				}
				CreatureStaticData.ParameterData pre = c.metaInfo.creatureStaticData.GetParam("Prefix");
				CreatureStaticData.ParameterData suf = c.metaInfo.creatureStaticData.GetParam("Suffix");
				if (pre == null || suf == null)
				{
					return;
				}
				__result = pre.desc + "\n" + suf.desc;
			}
			catch (Exception e)
			{
				Batch2Err("NothingTitlePostfix", e);
			}
		}

		private static void InstallTrio(Assembly asm)
		{
			const string key = "FastFix.MagicalTrioOwner";
			MethodInfo m = FindByName(asm.GetType("MagicalTrio.Harmony_Patch"), "EquipmentScriptBase_OnGiveDamage", 4);
			MethodInfo give = typeof(EquipmentScriptBase).GetMethod("OnGiveDamage", new Type[] { typeof(UnitModel), typeof(UnitModel), typeof(DamageInfo).MakeByRefType() });
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(give, null))
			{
				Missing(key, "MagicalTrio.Harmony_Patch.EquipmentScriptBase_OnGiveDamage / EquipmentScriptBase.OnGiveDamage");
				return;
			}
			FastFixModule.Harmony.Patch(give, N.PatchUtil.WithPriority(Own("TrioCapturePrefix"), Priority.First), null);
			Patched(key, give);
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("TrioSkipPrefix")), null);
			Patched(key, m);
		}

		public static void TrioCapturePrefix(EquipmentScriptBase __instance)
		{
			_trioScript = __instance;
		}

		// Taken once: a call that did not pass through TrioCapturePrefix runs the mod's Prefix as before.
		public static bool TrioSkipPrefix()
		{
			EquipmentScriptBase sc = _trioScript;
			_trioScript = null;
			return sc == null || (sc.model != null && sc.model.owner != null);
		}

		private static void InstallRabbitRange(Assembly asm)
		{
			const string key = "FastFix.WhiteRabbitRange";
			MethodInfo m = Find(asm, "WhiteRabbit.WhiteRabbit", "IsInRange", new Type[] { typeof(UnitModel), typeof(float) });
			if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(bool)))
			{
				Missing(key, "WhiteRabbit.WhiteRabbit.IsInRange(UnitModel, float)");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("RabbitRangePrefix")), null);
			Patched(key, m);
		}

		public static bool RabbitRangePrefix(UnitModel target, ref bool __result)
		{
			try
			{
				if (target == null || target.GetMovableNode() == null)
				{
					__result = false;
					return false;
				}
			}
			catch (Exception e)
			{
				Batch2Err("RabbitRangePrefix", e);
			}
			return true;
		}

		// ----- 1.50 (2026-11-18): AttackEnd with no damage event, quiet GetPassageRect, "invalid node state" trace -----
		// AnimatorEventHandler.AttackEnd reads _giveDamageEvent.Target with no null check (Damage() has one): an animator whose
		// attack has no damage event set throws on the animation event (run mass-breach-3, x1) and the end event is lost. With
		// no damage event the end event is called, as the original does after its check; otherwise the original runs.
		private static FieldInfo _aeGive;
		private static FieldInfo _aeEnd;

		private static void InstallAttackEnd()
		{
			const string key = "FastFix.AttackEndNull";
			if (!N.Config.BindFlag(key, true, "An attack animation of a unit with no damage event set no longer throws at its end (the attack end is still sent). Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(AnimatorEventHandler).GetMethod("AttackEnd", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				FieldInfo give = typeof(AnimatorEventHandler).GetField("_giveDamageEvent", BindingFlags.NonPublic | BindingFlags.Instance);
				FieldInfo end = typeof(AnimatorEventHandler).GetField("_attackEndEvent", BindingFlags.NonPublic | BindingFlags.Instance);
				if (object.ReferenceEquals(m, null) || object.ReferenceEquals(give, null) || object.ReferenceEquals(end, null))
				{
					Missing(key, "AnimatorEventHandler.AttackEnd / _giveDamageEvent / _attackEndEvent");
					return;
				}
				_aeGive = give;
				_aeEnd = end;
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("AttackEndPrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool AttackEndPrefix(AnimatorEventHandler __instance)
		{
			AnimatorEventHandler.EventDelegate end;
			try
			{
				if (object.ReferenceEquals(__instance, null) || _aeGive.GetValue(__instance) != null)
				{
					return true;
				}
				end = _aeEnd.GetValue(__instance) as AnimatorEventHandler.EventDelegate;
			}
			catch (Exception e)
			{
				Batch2Err("AttackEndPrefix", e);
				return true;
			}
			// Outside the try: an exception in the end event propagates as from the original.
			if (end != null)
			{
				end();
			}
			return false;
		}

		// GetPassageRect(PassageObjectModel pass) helpers copied into many mods (global Extension in NothingIs, Incubator and
		// others, FourFools' PositionAndSizeInfos) call Debug.LogError and return Rect.zero when the room view or its sprite
		// is missing; per-frame callers (BingJieXian UpdateNodePosition) wrote 17 lines per call. The prefix returns the same
		// Rect.zero without the log; every other case runs the helper. Patched only where the body has no exception handler
		// (GL-53), contains the helper's "GetPassageRect ERROR" text and names its parameter pass.
		private static void InstallPassageRect()
		{
			const string key = "FastFix.PassageRectQuiet";
			if (!N.Config.BindFlag(key, true, "Mods with a GetPassageRect helper (Nobody Is, Incubator, Four Fools and others): a room whose view or sprite is missing no longer writes an error on every call; the result is the same empty rect. Read at game start.").On)
			{
				return;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			int n = 0;
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				string an = "?";
				Type[] types;
				try
				{
					an = all[i].GetName().Name;
					if (an == "Assembly-CSharp" || an == "mscorlib" || an.StartsWith("System", StringComparison.Ordinal) || an.StartsWith("Unity", StringComparison.Ordinal)
						|| an.StartsWith("Mono", StringComparison.Ordinal) || an.StartsWith("NailOptimization", StringComparison.Ordinal) || all[i] is AssemblyBuilder)
					{
						continue;
					}
					types = all[i].GetTypes();
				}
				catch (ReflectionTypeLoadException e)
				{
					types = e.Types;
				}
				catch (Exception)
				{
					continue;
				}
				if (object.ReferenceEquals(types, null))
				{
					continue;
				}
				for (int t = 0; t < types.Length; t++)
				{
					if (object.ReferenceEquals(types[t], null))
					{
						continue;
					}
					try
					{
						MethodInfo m = types[t].GetMethod("GetPassageRect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
							null, new Type[] { typeof(PassageObjectModel) }, null);
						if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.ReturnType, typeof(Rect)))
						{
							continue;
						}
						MethodBody body = m.GetMethodBody();
						if (body == null || body.ExceptionHandlingClauses.Count > 0 || m.GetParameters()[0].Name != "pass" || !HasLiteral(m, "GetPassageRect ERROR"))
						{
							N.ULog.Info(FastFixModule.Tag, key + " skipped " + types[t].FullName + ".GetPassageRect in " + an + " (not the known helper)");
							continue;
						}
						FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("PassRectPrefix")), null);
						Patched(key, m);
						n++;
					}
					catch (Exception e)
					{
						N.ULog.Error(FastFixModule.Tag, key + " install failed for " + types[t].FullName + ": " + e);
					}
				}
			}
			N.ULog.Info(FastFixModule.Tag, key + ": " + n + " helper(s) patched, scan " + sw.ElapsedMilliseconds + " ms");
		}

		public static bool PassRectPrefix(PassageObjectModel pass, ref Rect __result)
		{
			try
			{
				if (pass == null)
				{
					__result = Rect.zero;
					return false;
				}
				SefiraMapLayer layer = SefiraMapLayer.instance;
				if (layer == null)
				{
					return true;
				}
				PassageObject po = layer.GetPassageObject(pass);
				if (po == null || (pass.type != PassageType.ISOLATEROOM && po.shader == null))
				{
					__result = Rect.zero;
					return false;
				}
			}
			catch (Exception e)
			{
				Batch2Err("PassRectPrefix", e);
			}
			return true;
		}

		// MovableObjectNode.UpdateCurrentPassage with neither node nor edge only logs "ERROR : invalid node state": the passage
		// is not assigned, so no enter/exit runs, and only currentScale is set from the current passage. Mods that move a unit
		// by setting a null node (BingJieXian Yuan Shen traced in mass-breach-4; others in mass-breach-6/7, 300+ lines) fill
		// the log. 1.51 dropped the line in a Debug.Log Prefix limited to Yuan Shen; it never matched in mass-breach-6/7. The
		// Prefix does that path without the log for every unit and skips the original; every other path runs the original.
		// Each distinct owner (creature script, else model type) is written to the Nail log once (16 at most). No handlers.
		private static FieldInfo _mnModel;
		private static HashSet<string> _nodeOwners;

		private static void InstallNodeQuiet()
		{
			const string key = "FastFix.InvalidNodeLog";
			if (!N.Config.BindFlag(key, true, "A unit with neither a map node nor an edge (some mods move units this way) no longer writes invalid node state to the log on every move; the game does nothing else on that path. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo upd = typeof(MovableObjectNode).GetMethod("UpdateCurrentPassage", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
				FieldInfo model = typeof(MovableObjectNode).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance);
				if (object.ReferenceEquals(upd, null) || object.ReferenceEquals(model, null) || !typeof(UnitModel).IsAssignableFrom(model.FieldType))
				{
					Missing(key, "MovableObjectNode.UpdateCurrentPassage / MovableObjectNode.model");
					return;
				}
				_mnModel = model;
				_nodeOwners = new HashSet<string>();
				FastFixModule.Harmony.Patch(upd, new HarmonyMethod(Own("NodeStatePrefix")), null);
				Patched(key, upd);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool NodeStatePrefix(MovableObjectNode __instance)
		{
			try
			{
				if (__instance.currentNode != null || __instance.currentEdge != null)
				{
					return true;
				}
				PassageObjectModel pass = __instance.currentPassage;
				__instance.currentScale = pass != null ? pass.scaleFactor : 1f;
				if (_nodeOwners.Count < 16)
				{
					NoteOwner(_nodeOwners, "FastFix.InvalidNodeLog: invalid node state not logged for ", _mnModel.GetValue(__instance) as UnitModel);
				}
				return false;
			}
			catch (Exception e)
			{
				Batch2Err("NodeStatePrefix", e);
			}
			return true;
		}

		private static void NoteOwner(HashSet<string> seen, string text, UnitModel unit)
		{
			CreatureModel c = unit as CreatureModel;
			string name = unit == null ? "(no model)" : (c != null && c.script != null ? c.script.GetType().FullName + " id " + c.metadataId : unit.GetType().FullName);
			if (seen.Add(name))
			{
				N.ULog.Info(FastFixModule.Tag, text + name + " (each owner once)");
			}
		}

		// CreatureTypeInfo.name reads dataTable.GetList("name"); a creature type without a name entry (FairyLongLegs' child,
		// mass-breach-21) makes GetList log "Dictionary error in collection //Not founded name CreatureDataList" and name returns
		// "Unknown". GlobalHistory.OnNotice reads it on every suppression of such a creature only for a Debug.Log line. The Prefix
		// returns "Unknown" without the error line when the key is missing; every other case runs the original. No handlers.
		private static HashSet<long> _noName;

		private static void InstallNoNameQuiet()
		{
			const string key = "FastFix.CreatureNoNameLog";
			if (!N.Config.BindFlag(key, true, "A creature type without a name in its data (some mod child creatures) no longer writes a dictionary error to the log each time its name is read; the game shows Unknown as before. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo get = typeof(CreatureTypeInfo).GetProperty("name", BindingFlags.Public | BindingFlags.Instance).GetGetMethod();
				if (object.ReferenceEquals(get, null))
				{
					Missing(key, "CreatureTypeInfo.name");
					return;
				}
				_noName = new HashSet<long>();
				FastFixModule.Harmony.Patch(get, new HarmonyMethod(Own("NoNamePrefix")), null);
				Patched(key, get);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool NoNamePrefix(CreatureTypeInfo __instance, ref string __result)
		{
			try
			{
				if (__instance.dataTable == null || __instance.dataTable.dictionary == null || __instance.dataTable.dictionary.ContainsKey("name"))
				{
					return true;
				}
				__result = "Unknown";
				if (_noName.Count < 32 && _noName.Add(__instance.id))
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.CreatureNoNameLog: creature type " + __instance.id + " (" + __instance.script + ") has no name in its data; shown as Unknown (each type once)");
				}
				return false;
			}
			catch (Exception e)
			{
				Batch2Err("NoNamePrefix", e);
			}
			return true;
		}

		// ChildCreatureModel.SetParent makes the unit and observes FixedUpdate at once, but ChildCreatureUnit.Start (which calls
		// the script's OnViewInit) runs only before the unit's first Update. A child that escapes on the tick it is made gets
		// OnEscapeUpdate -> UniqueEscape first; mod scripts that set their animation object in OnViewInit throw there
		// (TheCryingChildrenCreature.UniqueEscape, ReUseChild_1.SetState; mass-breach-23). The escape step waits while the unit is
		// active and its Start has not run yet; Start then comes before the next Update. An inactive unit (Start cannot run) and a
		// unit whose Start ran keep the original. Neither patched method has handlers.
		private static HashSet<int> _childStarted;
		private static int _childWaits;

		private static void InstallChildViewWait()
		{
			const string key = "FastFix.ChildViewWait";
			if (!N.Config.BindFlag(key, true, "A child creature that escapes on the tick it is made waits for its view to be set up before its first escape step. Some mod children (The Crying Children, ReUse) threw an error there. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo start = typeof(ChildCreatureUnit).GetMethod("Start", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				MethodInfo esc = typeof(ChildCreatureModel).GetMethod("OnEscapeUpdate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(start, null) || object.ReferenceEquals(esc, null) || !object.ReferenceEquals(start.DeclaringType, typeof(ChildCreatureUnit)) || !object.ReferenceEquals(esc.DeclaringType, typeof(ChildCreatureModel)))
				{
					Missing(key, "ChildCreatureUnit.Start / ChildCreatureModel.OnEscapeUpdate");
					return;
				}
				_childStarted = new HashSet<int>();
				FastFixModule.Harmony.Patch(start, new HarmonyMethod(Own("ChildStartPrefix")), null);
				Patched(key, start);
				FastFixModule.Harmony.Patch(esc, new HarmonyMethod(Own("ChildEscapePrefix")), null);
				Patched(key, esc);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static void ChildStartPrefix(ChildCreatureUnit __instance)
		{
			try
			{
				_childStarted.Add(__instance.GetInstanceID());
			}
			catch (Exception e)
			{
				Batch2Err("ChildStartPrefix", e);
			}
		}

		public static bool ChildEscapePrefix(ChildCreatureModel __instance)
		{
			try
			{
				ChildCreatureUnit u = __instance.Unit;
				if (u == null || !u.isActiveAndEnabled || _childStarted.Contains(u.GetInstanceID()))
				{
					return true;
				}
				_childWaits++;
				if (_childWaits <= 10)
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.ChildViewWait: " + (__instance.script == null ? "?" : __instance.script.GetType().FullName) + " escape step waits for its view (unit Start not run yet) (" + _childWaits + ")");
				}
				return false;
			}
			catch (Exception e)
			{
				Batch2Err("ChildEscapePrefix", e);
			}
			return true;
		}

		// Incubator AssetGetter.Return pools a SkeletonAnimation and removes its other MonoBehaviours with DestroyImmediate. Called
		// from a physics callback (MajoBase.OnSuppressed <- FreischutzBulletCollision.OnTriggerEnter2D, mass-breach-23) Unity refuses,
		// logs an error and leaves the scripts on the pooled object; the same from an animation event (AnimatorEventHandler.Damage
		// -> DeathAngelWeapon, mass-breach-24), which cannot be detected. Return has try/catch (GL-53), so the Prefix does the whole
		// method and always returns false: the same steps in the same order, but the scripts are disabled at once and destroyed
		// at the end of the frame.
		private static Type _poolAnimType;
		private static System.Collections.IDictionary _poolList;
		private static Type _poolListType;
		private static int _poolDeferred;

		private static void InstallPool(Assembly asm)
		{
			const string key = "FastFix.IncubatorPoolDestroy";
			Type t = asm.GetType("AssetGetter");
			MethodInfo ret = object.ReferenceEquals(t, null) ? null : FindByName(t, "Return", 2);
			FieldInfo list = object.ReferenceEquals(t, null) ? null : t.GetField("_animList", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(ret, null) || object.ReferenceEquals(list, null) || !ret.IsStatic)
			{
				Missing(key, "AssetGetter.Return / _animList");
				return;
			}
			ParameterInfo[] ps = ret.GetParameters();
			_poolList = list.GetValue(null) as System.Collections.IDictionary;
			Type[] ga = list.FieldType.IsGenericType ? list.FieldType.GetGenericArguments() : null;
			if (!object.ReferenceEquals(ps[0].ParameterType, typeof(string)) || !typeof(MonoBehaviour).IsAssignableFrom(ps[1].ParameterType) || _poolList == null || object.ReferenceEquals(ga, null) || ga.Length != 2)
			{
				Missing(key, "AssetGetter.Return(string, SkeletonAnimation) / _animList dictionary");
				return;
			}
			_poolAnimType = ps[1].ParameterType;
			_poolListType = ga[1];
			FastFixModule.Harmony.Patch(ret, new HarmonyMethod(Own("PoolReturnPrefix")), null);
			Patched(key, ret);
		}

		public static bool PoolReturnPrefix(string animName, MonoBehaviour anim)
		{
			try
			{
				anim.gameObject.SetActive(false);
				try
				{
					anim.transform.SetParent(null);
				}
				catch (Exception)
				{
				}
				if (!_poolList.Contains(animName))
				{
					_poolList.Add(animName, Activator.CreateInstance(_poolListType));
				}
				anim.transform.localScale = Vector3.one;
				anim.transform.position = Vector3.up * 100f;
				try
				{
					MonoBehaviour[] arr = anim.gameObject.GetComponents<MonoBehaviour>();
					for (int i = 0; i < arr.Length; i++)
					{
						MonoBehaviour mb = arr[i];
						if (mb == null || _poolAnimType.IsInstanceOfType(mb))
						{
							continue;
						}
						try
						{
							mb.enabled = false;
							UnityEngine.Object.Destroy(mb);
							_poolDeferred++;
							if (_poolDeferred <= 3)
							{
								N.ULog.Info(FastFixModule.Tag, "FastFix.IncubatorPoolDestroy: " + mb.GetType().Name + " on pooled " + animName + " disabled, removed at the end of the frame (" + _poolDeferred + ")");
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
				((System.Collections.IList)_poolList[animName]).Add(anim);
			}
			catch (Exception e)
			{
				Batch2Err("PoolReturnPrefix", e);
			}
			return false;
		}

		// Uncertain Page: UncertainPageScrollVisual.ResolveCreaturePortrait calls TryLoadCreaturePortraitFromInfoXml first on every
		// use; that walks all of BaseMods (Directory.GetFiles *.xml, all directories) and loads each CreatureInfo XmlDocument until
		// the id matches. Nothing is cached, so each item use stalls. Resolve gets a per-type cache (Prefix serves a cached result,
		// Postfix stores it; Resolve has no handlers). The XML lookup (try/catch, GL-53) is replaced whole by a Prefix that always
		// returns false: an index id -> (file, portrait) is built once in the same file order, and the mod's own
		// TryLoadCreaturePortrait is called for each match in that order, as before. An unreadable XML file is skipped instead of
		// ending the search.
		private static Dictionary<CreatureTypeInfo, Sprite> _upCache;
		private static Dictionary<string, List<string[]>> _upIndex;
		private static MethodInfo _upLoad;
		private static System.Threading.Thread _upThread;
		private static string _upRoot;
		private static string _upMsg;

		private static void InstallUpPortrait(Assembly asm)
		{
			const string key = "FastFix.UncertainPagePortrait";
			Type t = asm.GetType("UncertainPageMod.UncertainPageScrollVisual");
			BindingFlags f = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
			MethodInfo resolve = object.ReferenceEquals(t, null) ? null : t.GetMethod("ResolveCreaturePortrait", f, null, new Type[] { typeof(CreatureModel) }, null);
			MethodInfo info = object.ReferenceEquals(t, null) ? null : t.GetMethod("TryLoadCreaturePortraitFromInfoXml", f, null, new Type[] { typeof(CreatureModel) }, null);
			MethodInfo load = object.ReferenceEquals(t, null) ? null : t.GetMethod("TryLoadCreaturePortrait", f, null, new Type[] { typeof(string), typeof(string) }, null);
			if (object.ReferenceEquals(resolve, null) || object.ReferenceEquals(info, null) || object.ReferenceEquals(load, null)
				|| !object.ReferenceEquals(resolve.ReturnType, typeof(Sprite)) || !object.ReferenceEquals(info.ReturnType, typeof(Sprite)) || !object.ReferenceEquals(load.ReturnType, typeof(Sprite)))
			{
				Missing(key, "UncertainPageScrollVisual.ResolveCreaturePortrait / TryLoadCreaturePortraitFromInfoXml / TryLoadCreaturePortrait");
				return;
			}
			MethodBody rb = resolve.GetMethodBody();
			if (rb == null || rb.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "ResolveCreaturePortrait without exception handlers");
				return;
			}
			_upLoad = load;
			_upCache = new Dictionary<CreatureTypeInfo, Sprite>();
			_upRoot = System.IO.Path.Combine(Application.dataPath, "BaseMods");
			try
			{
				_upThread = new System.Threading.Thread(new System.Threading.ThreadStart(UpBuildIndex));
				_upThread.IsBackground = true;
				_upThread.Priority = System.Threading.ThreadPriority.BelowNormal;
				_upThread.Start();
			}
			catch (Exception e)
			{
				_upThread = null;
				Batch2Err("UpIndexThread", e);
			}
			FastFixModule.Harmony.Patch(info, new HarmonyMethod(Own("UpInfoPrefix")), null);
			Patched(key, info);
			FastFixModule.Harmony.Patch(resolve, new HarmonyMethod(Own("UpResolvePrefix")), new HarmonyMethod(Own("UpResolvePostfix")));
			Patched(key, resolve);
		}

		public static bool UpResolvePrefix(CreatureModel creature, ref Sprite __result)
		{
			try
			{
				Sprite s;
				if (creature == null || creature.metaInfo == null || !_upCache.TryGetValue(creature.metaInfo, out s))
				{
					return true;
				}
				if (!object.ReferenceEquals(s, null) && s == null)
				{
					_upCache.Remove(creature.metaInfo);
					return true;
				}
				__result = s;
				return false;
			}
			catch (Exception e)
			{
				Batch2Err("UpResolvePrefix", e);
			}
			return true;
		}

		public static void UpResolvePostfix(CreatureModel creature, Sprite __result)
		{
			try
			{
				if (creature != null && creature.metaInfo != null)
				{
					_upCache[creature.metaInfo] = __result;
				}
			}
			catch (Exception e)
			{
				Batch2Err("UpResolvePostfix", e);
			}
		}

		public static bool UpInfoPrefix(CreatureModel creature, ref Sprite __result)
		{
			__result = null;
			try
			{
				if (_upIndex == null && _upThread != null)
				{
					_upThread.Join();
					_upThread = null;
				}
				if (_upIndex == null)
				{
					UpBuildIndex();
				}
				if (_upMsg != null)
				{
					N.ULog.Info(FastFixModule.Tag, _upMsg);
					_upMsg = null;
				}
				List<string[]> list;
				if (creature != null && creature.metaInfo != null && _upIndex.TryGetValue(creature.metaInfo.id.ToString(), out list))
				{
					for (int i = 0; i < list.Count; i++)
					{
						Sprite s = _upLoad.Invoke(null, new object[] { list[i][1], "CreatureInfo:" + System.IO.Path.GetFileName(list[i][0]) }) as Sprite;
						if (s != null)
						{
							__result = s;
							return false;
						}
					}
				}
			}
			catch (Exception e)
			{
				Batch2Err("UpInfoPrefix", e);
			}
			return false;
		}

		// Runs on the background thread started by InstallUpPortrait (no Unity API here; the message is logged on the main
		// thread at the first use), or on the main thread if that thread could not start.
		private static void UpBuildIndex()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Dictionary<string, List<string[]>> index = new Dictionary<string, List<string[]>>();
			int read = 0;
			int bad = 0;
			string root = _upRoot;
			try
			{
			if (root != null && System.IO.Directory.Exists(root))
			{
				string[] files = System.IO.Directory.GetFiles(root, "*.xml", System.IO.SearchOption.AllDirectories);
				for (int i = 0; i < files.Length; i++)
				{
					if (files[i].Replace("\\", "/").IndexOf("/Creature/CreatureInfo/", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					try
					{
						read++;
						System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
						doc.Load(files[i]);
						System.Xml.XmlNode node = doc.SelectSingleNode("/creature/info");
						System.Xml.XmlAttribute id = node == null || node.Attributes == null ? null : node.Attributes["id"];
						if (id == null)
						{
							continue;
						}
						System.Xml.XmlNode por = doc.SelectSingleNode("/creature/observe/collection/portrait");
						if (por == null || string.IsNullOrEmpty(por.InnerText))
						{
							continue;
						}
						string k = id.Value.Trim();
						List<string[]> list;
						if (!index.TryGetValue(k, out list))
						{
							list = new List<string[]>();
							index[k] = list;
						}
						list.Add(new string[] { files[i], por.InnerText.Trim() });
					}
					catch (Exception)
					{
						bad++;
					}
				}
			}
			}
			catch (Exception e)
			{
				bad++;
				_upMsg = "FastFix.UncertainPagePortrait: index walk stopped: " + e.Message;
			}
			_upIndex = index;
			_upMsg = (_upMsg == null ? "" : _upMsg + "; ") + "FastFix.UncertainPagePortrait: CreatureInfo index built in the background, " + read + " files, " + index.Count + " ids, " + bad + " unreadable, " + sw.ElapsedMilliseconds + " ms";
		}

		// Incubator.CheckAgent reads agent.GetMovableNode().currentPassage.type last in its condition; an agent on an edge has no
		// passage and it throws, which ends Incubator.OnFixedUpdate for the tick (33 times in mass-breach-27). The Prefix skips the
		// call for such an agent: the original threw before MakeChildQB in that case, so nothing else changes. No handlers.
		private static void InstallIncCheck(Assembly asm)
		{
			const string key = "FastFix.IncubatorCheckAgent";
			Type t = asm.GetType("Incubator.Incubator");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CheckAgent", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(AgentModel) }, null);
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, "Incubator.CheckAgent(AgentModel)");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "Incubator.CheckAgent without exception handlers");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("IncCheckPrefix")), null);
			Patched(key, m);
		}

		public static bool IncCheckPrefix(AgentModel agent)
		{
			try
			{
				MovableObjectNode n = agent == null ? null : agent.GetMovableNode();
				return n != null && n.currentPassage != null;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// GirlInRainAnim.PlayAnimation appends "special" to the name one time in ten; the model has no "Escapediespecial", so
		// SetAnimation throws in PlayDeadMotion and the death motion is lost (mass-breach-27). The Transpiler sends the
		// SetAnimation call through LadySet, which drops the suffix when the model has no such animation. PlayAnimation has no
		// handlers.
		private static MethodInfo _ladySetAnim;
		private static int _ladyHits;
		private static bool _ladyLogged;

		private static void InstallLadySpecial(Assembly asm)
		{
			const string key = "FastFix.LadyInRainSpecial";
			Type t = asm.GetType("GirlInRain.GirlInRainAnim");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("PlayAnimation", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
			_ladySetAnim = typeof(Spine.AnimationState).GetMethod("SetAnimation", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(int), typeof(string), typeof(bool) }, null);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_ladySetAnim, null))
			{
				Missing(key, "GirlInRainAnim.PlayAnimation(string) / Spine.AnimationState.SetAnimation(int, string, bool)");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "GirlInRainAnim.PlayAnimation without exception handlers");
				return;
			}
			_ladyHits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("LadyTranspiler")));
			if (_ladyHits != 1)
			{
				N.ULog.Error(FastFixModule.Tag, key + ": expected 1 SetAnimation call, found " + _ladyHits + " (left as it was)");
				return;
			}
			Patched(key, m);
		}

		public static IEnumerable<CodeInstruction> LadyTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			List<int> at = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && object.ReferenceEquals(list[i].operand as MethodInfo, _ladySetAnim))
				{
					at.Add(i);
				}
			}
			_ladyHits = at.Count;
			if (at.Count != 1)
			{
				return list;
			}
			list[at[0]].opcode = OpCodes.Call;
			list[at[0]].operand = Own("LadySet");
			return list;
		}

		public static Spine.TrackEntry LadySet(Spine.AnimationState state, int track, string name, bool loop)
		{
			try
			{
				if (state != null && name != null && name.EndsWith("special", StringComparison.Ordinal) && state.Data != null && state.Data.SkeletonData != null
					&& state.Data.SkeletonData.FindAnimation(name) == null)
				{
					string plain = name.Substring(0, name.Length - 7);
					if (state.Data.SkeletonData.FindAnimation(plain) != null)
					{
						if (!_ladyLogged)
						{
							_ladyLogged = true;
							N.ULog.Info(FastFixModule.Tag, "FastFix.LadyInRainSpecial: " + name + " not in the model; " + plain + " played (logged once)");
						}
						name = plain;
					}
				}
			}
			catch (Exception e)
			{
				Batch2Err("LadySet", e);
			}
			return state.SetAnimation(track, name, loop);
		}

		// TiamatEGOreader.Harmony_Patch.Make{Weapon,Armor}Button{left,Right} set GameObject.layer = 999; Unity refuses a layer
		// outside 0..31, keeps the old one and logs an error each time the abnormality window opens (four per open). The
		// Transpiler sends set_layer through TmSetLayer, which sets only a valid layer: the same result without the error. The
		// four methods have no handlers.
		private static MethodInfo _goSetLayer;
		private static int _tmLayerHits;

		private static void InstallTiamatLayer(Assembly asm)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int a = 0; a < all.Length; a++)
			{
				Type t = null;
				try
				{
					t = all[a].GetType("TiamatEGOreader.Harmony_Patch", false);
				}
				catch (Exception)
				{
				}
				if (!object.ReferenceEquals(t, null))
				{
					TiamatLayerOne(t);
				}
			}
		}

		// Several mods ship their own copy of TiamatEGOreader.dll (three in this install); each copy runs its own Postfix, so each
		// one is patched.
		private static void TiamatLayerOne(Type t)
		{
			const string key = "FastFix.TiamatButtonLayer";
			_goSetLayer = typeof(GameObject).GetProperty("layer").GetSetMethod();
			string[] names = new string[] { "MakeWeaponButtonleft", "MakeWeaponButtonRight", "MakeArmorButtonleft", "MakeArmorButtonRight" };
			for (int i = 0; i < names.Length; i++)
			{
				MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod(names[i], BindingFlags.Public | BindingFlags.Static);
				if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_goSetLayer, null))
				{
					Missing(key, "TiamatEGOreader.Harmony_Patch." + names[i] + " / GameObject.set_layer");
					continue;
				}
				MethodBody b = m.GetMethodBody();
				if (b == null || b.ExceptionHandlingClauses.Count > 0)
				{
					Missing(key, names[i] + " without exception handlers");
					continue;
				}
				_tmLayerHits = 0;
				FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("TmLayerTranspiler")));
				if (_tmLayerHits != 1)
				{
					N.ULog.Error(FastFixModule.Tag, key + ": " + names[i] + ": expected 1 set_layer call, found " + _tmLayerHits + " (left as it was)");
					continue;
				}
				Patched(key, m);
			}
		}

		public static IEnumerable<CodeInstruction> TmLayerTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			List<int> at = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && object.ReferenceEquals(list[i].operand as MethodInfo, _goSetLayer))
				{
					at.Add(i);
				}
			}
			_tmLayerHits = at.Count;
			if (at.Count != 1)
			{
				return list;
			}
			list[at[0]].opcode = OpCodes.Call;
			list[at[0]].operand = Own("TmSetLayer");
			return list;
		}

		public static void TmSetLayer(GameObject go, int layer)
		{
			if (layer >= 0 && layer <= 31)
			{
				go.layer = layer;
			}
		}

		// FEW_RiskLevel.Harmony_Patch.CreatureSuppressRegion_SetData reads target.observeInfo.GetObserveState and metaInfo.id
		// without checks. ChildCreatureModel.SetParent sets observeInfo only for a child with its own base meta, so clicking
		// another escaped child throws (Uncertain Page run, twice). The Prefix skips the postfix for such a target; it only
		// replaces the risk label text and colour. No handlers.
		private static void InstallFewSuppress(Assembly asm)
		{
			RiskSuppressPatch(asm, "FastFix.FewSuppressChild", "FEW_RiskLevel.Harmony_Patch");
		}

		// HSLEB RiskLevel (Souls Pawn Shop) has the same postfix as FEW RiskLevel; same NRE on a new day 1 (three times).
		private static void InstallHslebSuppress(Assembly asm)
		{
			RiskSuppressPatch(asm, "FastFix.HslebSuppressChild", "HSLEB_RiskLevel.Harmony_Patch");
		}

		private static void RiskSuppressPatch(Assembly asm, string key, string typeName)
		{
			Type t = asm.GetType(typeName);
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CreatureSuppressRegion_SetData", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, typeName + ".CreatureSuppressRegion_SetData");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "CreatureSuppressRegion_SetData without exception handlers");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("FewSuppressPrefix")), null);
			Patched(key, m);
		}

		public static bool FewSuppressPrefix(UnitModel target)
		{
			try
			{
				CreatureModel c = target as CreatureModel;
				return c == null || (c.observeInfo != null && c.metaInfo != null);
			}
			catch (Exception)
			{
				return true;
			}
		}

		// FourFools.HP_CSUnit.CSUnit_OnExit (postfix of CreatureSelectUnit.OnExit) reads __instance._creatureIdMod.id; Start calls
		// OnExit before the door gets its abnormality, so _creatureIdMod is null and it throws (new game, twice). The Prefix skips
		// the postfix while _creatureIdMod is null: it only swaps the frame sprite of the mod's own abnormalities. No handlers.
		private static FieldInfo _csIdMod;

		private static void InstallFourFoolsSelect(Assembly asm)
		{
			const string key = "FastFix.FourFoolsSelect";
			Type t = asm.GetType("FourFools.HP_CSUnit");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CSUnit_OnExit", BindingFlags.Public | BindingFlags.Static);
			_csIdMod = typeof(CreatureSelect.CreatureSelectUnit).GetField("_creatureIdMod", All);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_csIdMod, null))
			{
				Missing(key, "FourFools.HP_CSUnit.CSUnit_OnExit / CreatureSelectUnit._creatureIdMod");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "CSUnit_OnExit without exception handlers");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("FourFoolsSelectPrefix")), null);
			Patched(key, m);
		}

		public static bool FourFoolsSelectPrefix(object __instance)
		{
			try
			{
				return __instance != null && _csIdMod.GetValue(__instance) != null;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// HMI_AgentP_MOD.Harmony_Patch.CreatureInfoOpenArea_OnOverlayEnter_Post reads CreatureInfoWindow.CurrentWindow.MetaInfo.id
		// without a null check; with the window open and no abnormality set it throws (once in the Uncertain Page run). The
		// Prefix skips the postfix in that case: it only restyles the cost of the mod's own abnormality (142018). No handlers.
		private static void InstallHmiHover(Assembly asm)
		{
			const string key = "FastFix.HmiInfoHover";
			Type t = asm.GetType("HMI_AgentP_MOD.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CreatureInfoOpenArea_OnOverlayEnter_Post", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(m, null))
			{
				Missing(key, "HMI_AgentP_MOD.Harmony_Patch.CreatureInfoOpenArea_OnOverlayEnter_Post");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "CreatureInfoOpenArea_OnOverlayEnter_Post without exception handlers");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("HmiHoverPrefix")), null);
			Patched(key, m);
		}

		public static bool HmiHoverPrefix()
		{
			try
			{
				CreatureInfoWindow w = CreatureInfoWindow.CurrentWindow;
				return w == null || w.MetaInfo != null;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// Ling.Harmony_Patch.UnitMouseEventManager_OnEndDrag_Postfix takes every Collider2D in the drag area and calls
		// GetComponent<UnitMouseEventTarget>().GetCommandTargetModel() without a null check; a collider without that component
		// throws (mass-breach-25, twice). The Transpiler sends the call through LingTarget, which gives null for a missing
		// component; "null is CreatureModel" is false and the loop goes on. The postfix has no handlers (for over an array).
		private static MethodInfo _lingGetTarget;
		private static int _lingHits;

		private static void InstallLingDrag(Assembly asm)
		{
			const string key = "FastFix.LingDragSelect";
			Type t = asm.GetType("Ling.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("UnitMouseEventManager_OnEndDrag_Postfix", BindingFlags.Public | BindingFlags.Static);
			_lingGetTarget = typeof(UnitMouseEventTarget).GetMethod("GetCommandTargetModel", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_lingGetTarget, null))
			{
				Missing(key, "Ling.Harmony_Patch.UnitMouseEventManager_OnEndDrag_Postfix / UnitMouseEventTarget.GetCommandTargetModel");
				return;
			}
			MethodBody b = m.GetMethodBody();
			if (b == null || b.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "OnEndDrag_Postfix without exception handlers");
				return;
			}
			_lingHits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("LingDragTranspiler")));
			if (_lingHits != 1)
			{
				N.ULog.Error(FastFixModule.Tag, key + ": expected 1 GetCommandTargetModel call, found " + _lingHits + " (left as it was)");
				return;
			}
			Patched(key, m);
		}

		public static IEnumerable<CodeInstruction> LingDragTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			List<int> at = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && object.ReferenceEquals(list[i].operand as MethodInfo, _lingGetTarget))
				{
					at.Add(i);
				}
			}
			_lingHits = at.Count;
			if (at.Count != 1)
			{
				return list;
			}
			list[at[0]].opcode = OpCodes.Call;
			list[at[0]].operand = Own("LingTarget");
			return list;
		}

		public static IMouseCommandTargetModel LingTarget(UnitMouseEventTarget c)
		{
			return c == null ? null : c.GetCommandTargetModel();
		}

		// Unity logs "AddComponent asking for invalid type" (no stack) for AddComponent(null) and returns null. Incubator does it on
		// purpose: Incubator_CreatureBase.OnViewInit adds the type named after AnimName and adds its base script when that type
		// does not exist (every witch view, mass-breach-26). For a null type the Prefix returns null without the call, as Unity
		// does, and logs the first caller once. Other types run the original. AddComponent(Type) has no handlers.
		private static bool _addCompLogged;

		private static void InstallAddCompTrace()
		{
			const string key = "FastFix.AddComponentNullQuiet";
			if (!N.Config.BindFlag(key, true, "AddComponent with no type (Incubator witches do it on purpose and then add their own script) returns nothing at once instead of writing a warning to the log each time. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(GameObject).GetMethod("AddComponent", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Type) }, null);
				if (object.ReferenceEquals(m, null))
				{
					Missing(key, "GameObject.AddComponent(Type)");
					return;
				}
				MethodBody b = m.GetMethodBody();
				if (b == null || b.ExceptionHandlingClauses.Count > 0)
				{
					Missing(key, "GameObject.AddComponent(Type) without exception handlers");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("AddCompPrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool AddCompPrefix(Type componentType, ref Component __result)
		{
			if (!object.ReferenceEquals(componentType, null))
			{
				return true;
			}
			__result = null;
			if (!_addCompLogged)
			{
				_addCompLogged = true;
				try
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.AddComponentNullQuiet: AddComponent(null) returns null without the warning (logged once); first caller:\n" + new System.Diagnostics.StackTrace(1, false));
				}
				catch (Exception)
				{
				}
			}
			return false;
		}

		// Diagnostic, temporary (1.54; remove before release). "Invalid Movable State.. Please report this." comes from
		// MovableObjectNode.ProcessMoveByDistance when the current node is not on the path edge (the node was set while the
		// unit walked a path); it was seen under ChildMoveNode (child creatures) in mass-breach-7. "Not expected state" comes
		// from CreatureModel.OnStageStart for a creature not in WAIT at day start. Both owners are unknown: the child moved by
		// ChildMoveNode and every creature with a state other than WAIT at OnStageStart are written once per type.
		// OnStageStart has no exception handler. Debug.LogError(object) Prefix only compares the text.
		private static ChildCreatureModel _moveChild;
		private static HashSet<string> _moveOwners;
		private const string MoveStateText = "Invalid Movable State.. Please report this.";

		private static void InstallMoveTrace()
		{
			const string key = "FastFix.MoveStateTrace";
			if (!N.Config.BindFlag(key, true, "Diagnostic (temporary): the creatures behind the game's Invalid Movable State and Not expected state errors are written to the Nail log once per type. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo err = typeof(Debug).GetMethod("LogError", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(object) }, null);
				MethodInfo start = typeof(CreatureModel).GetMethod("OnStageStart", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(err, null) || object.ReferenceEquals(start, null))
				{
					Missing(key, "Debug.LogError(object) / CreatureModel.OnStageStart");
					return;
				}
				_moveOwners = new HashSet<string>();
				FastFixModule.Harmony.Patch(err, new HarmonyMethod(Own("MoveErrPrefix")), null);
				Patched(key, err);
				FastFixModule.Harmony.Patch(start, new HarmonyMethod(Own("StageStatePrefix")), null);
				Patched(key, start);
				// KnightOfDespair was in ESCAPE at day start (mass-breach-8); its only start is CreatureModel.Escape (no
				// handlers). Escape while the game is stopped (before the day runs) writes its call stack, 4 at most.
				MethodInfo esc = typeof(CreatureModel).GetMethod("Escape", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(esc, null))
				{
					Missing(key, "CreatureModel.Escape");
					return;
				}
				FastFixModule.Harmony.Patch(esc, new HarmonyMethod(Own("EarlyEscapePrefix")), null);
				Patched(key, esc);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		// "Invalid Movable State.. Please report this." (MovableObjectNode.ProcessMoveByDistance): mods that set the node of a
		// walking unit (kekesi jackk, Joker Sheep and others, runs mass-breach-9/10) write it many times a minute; the game
		// already stops the move right after the line. Debug.LogError(object) Prefix skips only that exact text.
		private static void InstallMoveQuiet()
		{
			const string key = "FastFix.MovableStateLog";
			if (!N.Config.BindFlag(key, true, "A unit whose map node was moved by a mod while it walked no longer writes Invalid Movable State to the log; the game stops that move as before. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo err = typeof(Debug).GetMethod("LogError", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(object) }, null);
				if (object.ReferenceEquals(err, null))
				{
					Missing(key, "Debug.LogError(object)");
					return;
				}
				FastFixModule.Harmony.Patch(err, new HarmonyMethod(Own("MoveQuietPrefix")), null);
				Patched(key, err);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool MoveQuietPrefix(object message)
		{
			string text = message as string;
			return text == null || text.Length != MoveStateText.Length || !string.Equals(text, MoveStateText, StringComparison.Ordinal);
		}

		public static void MoveErrPrefix(object message)
		{
			if (_moveOwners == null || _moveOwners.Count >= 32)
			{
				return;
			}
			string text = message as string;
			if (text == null || !string.Equals(text, MoveStateText, StringComparison.Ordinal))
			{
				return;
			}
			try
			{
				if (_moveChild != null)
				{
					NoteOwner(_moveOwners, "FastFix.MoveStateTrace: Invalid Movable State on child ", _moveChild);
				}
				else if (_moveOwners.Add("(not a child): " + Environment.StackTrace))
				{
					N.ULog.Info(FastFixModule.Tag, "FastFix.MoveStateTrace: Invalid Movable State outside ChildMoveNode: " + Environment.StackTrace.Replace('\n', '|'));
				}
			}
			catch (Exception e)
			{
				Batch2Err("MoveErrPrefix", e);
			}
		}

		private static int _earlyEscapes;

		public static void EarlyEscapePrefix(CreatureModel __instance)
		{
			if (_earlyEscapes >= 4)
			{
				return;
			}
			try
			{
				GameManager gm = GameManager.currentGameManager;
				if (gm != null && gm.state != GameState.STOP)
				{
					return;
				}
				_earlyEscapes++;
				string who = __instance.script != null ? __instance.script.GetType().FullName : "?";
				N.ULog.Info(FastFixModule.Tag, "FastFix.MoveStateTrace: Escape before the day runs (" + (gm == null ? "no GameManager" : "state STOP") + ") for "
					+ who + " id " + __instance.metadataId + ": " + Environment.StackTrace.Replace('\n', '|'));
			}
			catch (Exception e)
			{
				Batch2Err("EarlyEscapePrefix", e);
			}
		}

		public static void StageStatePrefix(CreatureModel __instance)
		{
			try
			{
				if (_moveOwners != null && _moveOwners.Count < 32 && __instance.state != CreatureState.WAIT)
				{
					NoteOwner(_moveOwners, "FastFix.MoveStateTrace: day start with state " + __instance.state + " (Not expected state) for ", __instance);
				}
			}
			catch (Exception e)
			{
				Batch2Err("StageStatePrefix", e);
			}
		}

		// Joker_MOD.Sheep.OnFixedUpdate calls DamageToPassage_onetarget and DamageToPassage with currentPassage. Both run foreach
		// over passage.GetEnteredTargets(): a null passage (sheep on a hallway edge between passages) throws at once, IsHostile
		// reads unit.hp before its null check, and a target killed by TakeDamage leaves the passage list while it is enumerated
		// ("Collection was modified"); run mass-breach-12/13. Both methods have foreach (GL-53, not patched); OnFixedUpdate has
		// no exception handler: its Transpiler routes both calls to SheepDamage, which does the same over a copy of the list,
		// skipping a null passage, node or unit.
		private static MethodInfo _sheepOne;
		private static MethodInfo _sheepAll;
		private static Type _sheepType;
		private static FieldInfo _sheepJoker;
		private static ConstructorInfo _sheepPanic;
		private static int _sheepHits;

		private static void InstallSheep(Assembly asm)
		{
			const string key = "FastFix.JokerSheepDamage";
			Type t = asm.GetType("Joker_MOD.Sheep");
			Type joker = asm.GetType("Joker_MOD.Joker");
			Type panic = asm.GetType("Joker_MOD.Sheep+PanicTrans");
			Type[] dmg = new Type[] { typeof(PassageObjectModel), typeof(RwbpType), typeof(int), typeof(int) };
			const BindingFlags pi = BindingFlags.Public | BindingFlags.Instance;
			MethodInfo fixedUpd = object.ReferenceEquals(t, null) ? null : t.GetMethod("OnFixedUpdate", pi | BindingFlags.DeclaredOnly, null, new Type[] { typeof(CreatureModel) }, null);
			MethodInfo one = object.ReferenceEquals(t, null) ? null : t.GetMethod("DamageToPassage_onetarget", pi, null, dmg, null);
			MethodInfo all = object.ReferenceEquals(t, null) ? null : t.GetMethod("DamageToPassage", pi, null, dmg, null);
			FieldInfo jf = object.ReferenceEquals(t, null) ? null : t.GetField("joker", pi);
			ConstructorInfo ctor = object.ReferenceEquals(panic, null) || object.ReferenceEquals(joker, null) ? null : panic.GetConstructor(new Type[] { joker });
			if (object.ReferenceEquals(fixedUpd, null) || object.ReferenceEquals(one, null) || object.ReferenceEquals(all, null) || object.ReferenceEquals(jf, null)
				|| object.ReferenceEquals(ctor, null) || !typeof(UnitBuf).IsAssignableFrom(panic))
			{
				Missing(key, "Sheep.OnFixedUpdate / DamageToPassage / DamageToPassage_onetarget / joker / PanicTrans(Joker)");
				return;
			}
			MethodBody body = fixedUpd.GetMethodBody();
			if (body == null || body.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "Sheep.OnFixedUpdate without exception handlers");
				return;
			}
			_sheepOne = one;
			_sheepAll = all;
			_sheepType = t;
			_sheepJoker = jf;
			_sheepPanic = ctor;
			_sheepHits = 0;
			FastFixModule.Harmony.Patch(fixedUpd, null, null, new HarmonyMethod(Own("SheepTranspiler")));
			if (_sheepHits != 2)
			{
				N.ULog.Error(FastFixModule.Tag, key + ": expected 2 calls in OnFixedUpdate, found " + _sheepHits + " (calls left as they were)");
			}
			Patched(key, fixedUpd);
		}

		public static IEnumerable<CodeInstruction> SheepTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			List<int> at = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt)
					&& (object.ReferenceEquals(c, _sheepOne) || object.ReferenceEquals(c, _sheepAll)))
				{
					at.Add(i);
				}
			}
			_sheepHits = at.Count;
			if (at.Count != 2)
			{
				return list;
			}
			for (int k = 0; k < at.Count; k++)
			{
				list[at[k]].opcode = OpCodes.Call;
				list[at[k]].operand = Own("SheepDamage");
			}
			return list;
		}

		public static void SheepDamage(CreatureBase sheep, PassageObjectModel passage, RwbpType type, int max, int min)
		{
			if (sheep == null || passage == null)
			{
				return;
			}
			System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> entered = passage.GetEnteredTargets();
			if (entered == null || entered.Count == 0)
			{
				return;
			}
			MovableObjectNode[] nodes = new MovableObjectNode[entered.Count];
			entered.CopyTo(nodes, 0);
			CreatureModel self = sheep.model;
			object joker = _sheepJoker.GetValue(sheep);
			object[] arg = new object[] { joker };
			for (int i = 0; i < nodes.Length; i++)
			{
				MovableObjectNode node = nodes[i];
				UnitModel unit = node == null ? null : node.GetUnit();
				if (unit == null || unit.hp <= 0f || !unit.IsAttackTargetable() || unit == self)
				{
					continue;
				}
				CreatureModel cm = unit as CreatureModel;
				if (cm != null && cm.script != null && _sheepType.IsInstanceOfType(cm.script))
				{
					continue;
				}
				unit.TakeDamage(self, new DamageInfo(type, max, min));
				unit.AddUnitBuf((UnitBuf)_sheepPanic.Invoke(arg));
			}
		}

		// Incubator sets the static Harmony_Patch.ModID to "Incubator" and builds every LcId/LcIdLong of its own content from it.
		// BaseMod reads the mod id from Info/<language>/info.xml (falling back to en, then kr); Incubator ships only cn/jp/ru, so
		// in an en or kr game the folder gets modid "" and its creatures and EGO go into the vanilla tables (saves store them as
		// ("", id)). Lookups under "Incubator" then fail: MakeLesers throws in BuildCreature_Mod, no witch is made, EGO checks
		// never match. An Info/en file is no fix: existing saves then resolve ("", 100002) to vanilla Happy Teddy Bear (run
		// mass-breach-12). ModID is set to the id BaseMod really gave the folder. Incubator.cs also reads _modlist[ModID] to
		// build Risk_Majos, and there is no "" key there (that method has try/catch, GL-53): with id "" Risk_Majos is built
		// here the same way from the vanilla list, limited to creatures whose script is a type of the Incubator assembly.
		private static void InstallIncModId(Assembly asm)
		{
			const string key = "FastFix.IncubatorModId";
			const BindingFlags ps = BindingFlags.Public | BindingFlags.Static;
			const BindingFlags pi = BindingFlags.Public | BindingFlags.Instance;
			Type hp = asm.GetType("Harmony_Patch");
			Type inc = asm.GetType("Incubator.Incubator");
			Type cb = asm.GetType("Incubator_CreatureBase");
			Type mp = asm.GetType("Incubator.Majos.Majo_Patch");
			FieldInfo idf = object.ReferenceEquals(hp, null) ? null : hp.GetField("ModID", ps);
			FieldInfo risk = object.ReferenceEquals(inc, null) ? null : inc.GetField("Risk_Majos", ps);
			MethodInfo getBase = object.ReferenceEquals(mp, null) ? null : mp.GetMethod("GetIncubator_CreatureBase", ps, null, new Type[] { typeof(CreatureTypeInfo) }, null);
			MethodInfo notMajo = object.ReferenceEquals(cb, null) ? null : cb.GetMethod("NotMajo", pi, null, Type.EmptyTypes, null);
			MethodInfo riskLv = object.ReferenceEquals(cb, null) ? null : cb.GetMethod("GetRiskLevel", pi | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(idf, null) || !object.ReferenceEquals(idf.FieldType, typeof(string)) || idf.IsInitOnly
				|| object.ReferenceEquals(risk, null) || !object.ReferenceEquals(risk.FieldType, typeof(Dictionary<int, List<long>>))
				|| object.ReferenceEquals(getBase, null) || object.ReferenceEquals(notMajo, null) || object.ReferenceEquals(riskLv, null)
				|| !object.ReferenceEquals(riskLv.ReturnType, typeof(int)))
			{
				Missing(key, "Harmony_Patch.ModID / Incubator.Risk_Majos / Majo_Patch.GetIncubator_CreatureBase / Incubator_CreatureBase");
				return;
			}
			ModInfo info = IncModInfo(asm);
			if (info == null)
			{
				Missing(key, "ModInfo of the Incubator folder in Add_On.ModList");
				return;
			}
			string cur = idf.GetValue(null) as string;
			string real = info.modid == null ? string.Empty : info.modid;
			if (string.Equals(cur, real, StringComparison.Ordinal))
			{
				N.ULog.Info(FastFixModule.Tag, key + ": mod id '" + real + "' matches, nothing to do");
				return;
			}
			idf.SetValue(null, real);
			N.ULog.Info(FastFixModule.Tag, key + ": mod id '" + cur + "' -> '" + real + "' (the id BaseMod gave the folder " + info.modpath.Name + ")");
			if (real.Length > 0 || risk.GetValue(null) != null)
			{
				return;
			}
			CreatureTypeList list = CreatureTypeList.instance;
			CreatureTypeInfo[] all = list == null ? null : list.GetList();
			if (all == null)
			{
				Missing(key, "CreatureTypeList (witch list not built; the Incubator keeps its own behaviour)");
				return;
			}
			HashSet<string> names = new HashSet<string>();
			Type[] types = asm.GetTypes();
			for (int i = 0; i < types.Length; i++)
			{
				if (typeof(CreatureBase).IsAssignableFrom(types[i]))
				{
					names.Add(types[i].Name);
					names.Add(types[i].FullName);
				}
			}
			Dictionary<int, List<long>> d = new Dictionary<int, List<long>>();
			object[] arg = new object[1];
			int count = 0;
			string text = string.Empty;
			for (int i = 0; i < all.Length; i++)
			{
				CreatureTypeInfo c = all[i];
				if (c == null || c.id == 100001L || c.id == 100002L || c.id == 100003L || string.IsNullOrEmpty(c.script) || !names.Contains(c.script))
				{
					continue;
				}
				arg[0] = c;
				object b = getBase.Invoke(null, arg);
				int lv;
				if (b != null)
				{
					if ((bool)notMajo.Invoke(b, null))
					{
						continue;
					}
					lv = (int)riskLv.Invoke(b, null);
				}
				else
				{
					lv = (int)(CreatureTypeInfo.GetRiskLevelStringToEnum(c.riskLevel) + 1);
				}
				List<long> ids;
				if (!d.TryGetValue(lv, out ids))
				{
					ids = new List<long>();
					d[lv] = ids;
				}
				ids.Add(c.id);
				count++;
				text += " " + c.id + "(risk " + lv + ")";
			}
			if (count == 0)
			{
				N.ULog.Info(FastFixModule.Tag, key + ": no witch found in the creature list, Risk_Majos left to the mod");
				return;
			}
			risk.SetValue(null, d);
			N.ULog.Info(FastFixModule.Tag, key + ": Risk_Majos built from the creature list, " + count + " witches:" + text);
		}

		private static ModInfo IncModInfo(Assembly asm)
		{
			Add_On a = Add_On.instance;
			if (a == null || a.ModList == null || string.IsNullOrEmpty(asm.Location))
			{
				return null;
			}
			string dir = System.IO.Path.GetDirectoryName(asm.Location);
			for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir); up++)
			{
				string parent = System.IO.Path.GetDirectoryName(dir);
				if (!string.IsNullOrEmpty(parent) && string.Equals(System.IO.Path.GetFileName(parent), "BaseMods", StringComparison.OrdinalIgnoreCase))
				{
					break;
				}
				dir = parent;
			}
			if (string.IsNullOrEmpty(dir))
			{
				return null;
			}
			char[] seps = new char[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };
			string want = dir.TrimEnd(seps);
			for (int i = 0; i < a.ModList.Count; i++)
			{
				ModInfo m = a.ModList[i];
				if (m != null && m.modpath != null && string.Equals(m.modpath.FullName.TrimEnd(seps), want, StringComparison.OrdinalIgnoreCase))
				{
					return m;
				}
			}
			return null;
		}

		// Incubator WalpurgisNight.OnViewInit calls Incubator.AllMajoEscape() and MakeLesers() and then SetEscapeUI, SetNode,
		// SetCollision. The creature is meant to arrive with its ordeal during the day; kept in containment its view is made
		// while the day is prepared (GameManager state STOP): AllMajoEscape makes the Knight of Despair transform and escape
		// before CreatureModel.OnStageStart (vanilla puts it back and logs "Not expected state"), MakeLesers throws in
		// SpecialEventManager.BuildCreature_Mod, the rest of OnViewInit never runs, _Node stays null and Update throws and
		// sets a null node every frame (invalid node state spam). Run mass-breach-8/9. OnViewInit has no exception handler;
		// its Transpiler routes both calls through WalpMajo/WalpLasers: during the day they run as before, while the day is
		// prepared they are kept and run by the WalpurgisNight.Update Prefix (no handlers) at the first update of the day.
		// AllMajoEscape and MakeLesers have foreach (GL-53) and are not patched.
		private static MethodInfo _walpMajo;
		private static MethodInfo _walpLasers;
		private static bool _walpMajoPending;
		private static List<object> _walpPending;

		private static void InstallWalp(Assembly asm)
		{
			const string key = "FastFix.WalpurgisDayStart";
			Type t = asm.GetType("Incubator.WalpurgisNight.WalpurgisNight");
			Type inc = asm.GetType("Incubator.Incubator");
			MethodInfo view = object.ReferenceEquals(t, null) ? null : t.GetMethod("OnViewInit", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new Type[] { typeof(CreatureUnit) }, null);
			MethodInfo upd = object.ReferenceEquals(t, null) ? null : t.GetMethod("Update", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
			MethodInfo majo = object.ReferenceEquals(inc, null) ? null : inc.GetMethod("AllMajoEscape", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
			MethodInfo lasers = object.ReferenceEquals(t, null) ? null : t.GetMethod("MakeLesers", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(view, null) || object.ReferenceEquals(upd, null) || object.ReferenceEquals(majo, null) || object.ReferenceEquals(lasers, null))
			{
				Missing(key, "WalpurgisNight.OnViewInit / Update / MakeLesers / Incubator.AllMajoEscape");
				return;
			}
			MethodBody vb = view.GetMethodBody();
			MethodBody ub = upd.GetMethodBody();
			if (vb == null || ub == null || vb.ExceptionHandlingClauses.Count > 0 || ub.ExceptionHandlingClauses.Count > 0)
			{
				Missing(key, "WalpurgisNight.OnViewInit / Update without exception handlers");
				return;
			}
			_walpMajo = majo;
			_walpLasers = lasers;
			_walpPending = new List<object>();
			_walpHits = 0;
			FastFixModule.Harmony.Patch(view, null, null, new HarmonyMethod(Own("WalpViewTranspiler")));
			if (_walpHits != 2)
			{
				N.ULog.Error(FastFixModule.Tag, key + ": expected 2 calls in OnViewInit, found " + _walpHits + " (calls left as they were)");
			}
			Patched(key, view);
			FastFixModule.Harmony.Patch(upd, new HarmonyMethod(Own("WalpUpdatePrefix")), null);
			Patched(key, upd);
		}

		private static int _walpHits;

		public static IEnumerable<CodeInstruction> WalpViewTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			List<int> at = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				MethodInfo c = list[i].operand as MethodInfo;
				if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt)
					&& (object.ReferenceEquals(c, _walpMajo) || object.ReferenceEquals(c, _walpLasers)))
				{
					at.Add(i);
				}
			}
			_walpHits = at.Count;
			if (at.Count != 2)
			{
				return list;
			}
			for (int k = 0; k < at.Count; k++)
			{
				int i = at[k];
				bool majo = object.ReferenceEquals(list[i].operand as MethodInfo, _walpMajo);
				list[i].opcode = OpCodes.Call;
				list[i].operand = Own(majo ? "WalpMajo" : "WalpLasers");
			}
			return list;
		}

		private static bool WalpPrep()
		{
			GameManager gm = GameManager.currentGameManager;
			return gm == null || gm.state == GameState.STOP;
		}

		public static void WalpMajo()
		{
			if (WalpPrep())
			{
				_walpMajoPending = true;
				_walpPending.Clear();
				N.ULog.Info(FastFixModule.Tag, "FastFix.WalpurgisDayStart: Walpurgis Night view made while the day is prepared; witches escape at the first frame of the day");
				return;
			}
			_walpMajo.Invoke(null, null);
		}

		public static void WalpLasers(object walp)
		{
			if (WalpPrep())
			{
				_walpPending.Add(walp);
				return;
			}
			_walpLasers.Invoke(walp, null);
		}

		public static void WalpUpdatePrefix(object __instance)
		{
			if (!_walpMajoPending && _walpPending.Count == 0)
			{
				return;
			}
			GameManager gm = GameManager.currentGameManager;
			if (gm == null || gm.state != GameState.PLAYING)
			{
				return;
			}
			try
			{
				if (_walpMajoPending)
				{
					_walpMajoPending = false;
					_walpMajo.Invoke(null, null);
				}
				int i = _walpPending.IndexOf(__instance);
				if (i >= 0)
				{
					_walpPending.RemoveAt(i);
					_walpLasers.Invoke(__instance, null);
				}
			}
			catch (Exception e)
			{
				Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
				N.ULog.Error(FastFixModule.Tag, "FastFix.WalpurgisDayStart: deferred call threw: " + inner);
			}
		}

		// Blood Shoes writes marker and error files to BloodShoesCreature.BloodShoes.desktopPath (Desktop/LOG) and never creates
		// it. Bleedbuf.Init writes BleedIconInit.txt at its end inside a try; with no folder that throws, the catch writes
		// another file there and throws again, so base.Init is skipped and the exception leaves AddUnitBuf (run mass-breach-6,
		// RightShoeAnim.AddBleed). Init has try/catch (GL-53, not patched). When the folder is missing the static path is set
		// once to a LOG folder inside the mod's own folder under BaseMods (created); an existing Desktop/LOG is left as is.
		private static void InstallShoesDir(Assembly asm)
		{
			const string key = "FastFix.BloodShoesLogDir";
			Type t = asm.GetType("BloodShoesCreature.BloodShoes");
			FieldInfo f = object.ReferenceEquals(t, null) ? null : t.GetField("desktopPath", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(f, null) || !object.ReferenceEquals(f.FieldType, typeof(string)) || f.IsInitOnly || f.IsLiteral)
			{
				Missing(key, "BloodShoesCreature.BloodShoes.desktopPath");
				return;
			}
			string cur = f.GetValue(null) as string;
			if (!string.IsNullOrEmpty(cur) && System.IO.Directory.Exists(cur))
			{
				N.ULog.Info(FastFixModule.Tag, key + ": " + cur + " exists, left as is");
				return;
			}
			string modDir = System.IO.Path.GetDirectoryName(asm.Location);
			for (int up = 0; up < 4 && !string.IsNullOrEmpty(modDir); up++)
			{
				string parent = System.IO.Path.GetDirectoryName(modDir);
				if (!string.IsNullOrEmpty(parent) && string.Equals(System.IO.Path.GetFileName(parent), "BaseMods", StringComparison.OrdinalIgnoreCase))
				{
					break;
				}
				modDir = parent;
			}
			if (string.IsNullOrEmpty(modDir))
			{
				Missing(key, "Blood Shoes mod folder");
				return;
			}
			string dir = System.IO.Path.Combine(modDir, "LOG");
			System.IO.Directory.CreateDirectory(dir);
			f.SetValue(null, dir);
			N.ULog.Info(FastFixModule.Tag, key + ": " + (cur ?? "(null)") + " missing, files go to " + dir);
		}

		// Blood Shoes Bleedbuf.FixedUpdate calls Destroy() when the bleed ends (Destroy sets aimUnit to null) and then
		// ShowIcon(bleedIcon, aimUnit): unit.GetMovableNode() throws and its catch writes Desktop/LOG/BurnShowIconerror.txt at
		// every bleed end. ShowIcon has try/catch (GL-53, not patched). Destroy has none: its Prefix keeps aimUnit, its Postfix
		// puts it back, so the last icon update reads the unit; the buf is already removed from it and the icon is destroyed
		// at the end of the frame as before.
		private static FieldInfo _bleedAim;

		private static void InstallBleedEnd(Assembly asm)
		{
			const string key = "FastFix.BleedIconEnd";
			Type t = asm.GetType("Bleedbuf");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("Destroy", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
			FieldInfo aim = object.ReferenceEquals(t, null) ? null : t.GetField("aimUnit", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodInfo show = object.ReferenceEquals(t, null) ? null : t.GetMethod("ShowIcon", BindingFlags.NonPublic | BindingFlags.Instance);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(aim, null) || object.ReferenceEquals(show, null)
				|| !object.ReferenceEquals(aim.FieldType, typeof(UnitModel)) || !typeof(UnitBuf).IsAssignableFrom(t))
			{
				Missing(key, "Bleedbuf.Destroy / Bleedbuf.aimUnit / Bleedbuf.ShowIcon");
				return;
			}
			_bleedAim = aim;
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("BleedDestroyPrefix")), new HarmonyMethod(Own("BleedDestroyPostfix")));
			Patched(key, m);
		}

		public static void BleedDestroyPrefix(UnitBuf __instance, out UnitModel __state)
		{
			__state = null;
			try
			{
				__state = _bleedAim.GetValue(__instance) as UnitModel;
			}
			catch (Exception e)
			{
				Batch2Err("BleedDestroyPrefix", e);
			}
		}

		public static void BleedDestroyPostfix(UnitBuf __instance, UnitModel __state)
		{
			try
			{
				if (__state != null && _bleedAim.GetValue(__instance) == null)
				{
					_bleedAim.SetValue(__instance, __state);
				}
			}
			catch (Exception e)
			{
				Batch2Err("BleedDestroyPostfix", e);
			}
		}

		// Incubator ships Pre/GiftFix.dll: GiftFix.Harmony_Patch.UnitModel_AttachEGOgift (Prefix of UnitModel.AttachEGOgift)
		// rebuilds gift.script when metaInfo.script is set and the script is the plain EquipmentScriptBase. The DLL targets a
		// newer .NET: CreateScriptInstance uses Type.op_Inequality, missing from the game's mscorlib, so its caller throws
		// MissingMethodException on every such gift and the exception leaves AttachEGOgift (day load stopped, run
		// mass-breach-5). The mod's Prefix has no exception handler: it is skipped and GiftScriptPrefix does the same work
		// (same check, script class looked up by short name in Add_On.AssemList, first match, then Type.GetType; the name
		// table is built once).
		private static Dictionary<string, Type> _giftTypes;

		private static void InstallGiftFix(Assembly asm)
		{
			const string key = "FastFix.GiftFixScript";
			MethodInfo m = Find(asm, "GiftFix.Harmony_Patch", "UnitModel_AttachEGOgift", new Type[] { typeof(EGOgiftModel) });
			if (object.ReferenceEquals(m, null) || m.GetParameters()[0].Name != "gift")
			{
				Missing(key, "GiftFix.Harmony_Patch.UnitModel_AttachEGOgift(EGOgiftModel gift)");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("GiftScriptPrefix")), null);
			Patched(key, m);
		}

		public static bool GiftScriptPrefix(EGOgiftModel gift)
		{
			try
			{
				if (gift == null || gift.metaInfo == null || string.IsNullOrEmpty(gift.metaInfo.script) || gift.script == null)
				{
					return false;
				}
				string have = gift.script.ToString();
				if (have != "EquipmentScriptBase" || gift.metaInfo.script == have)
				{
					return false;
				}
				Type t = GiftScriptType(gift.metaInfo.script);
				EquipmentScriptBase sc = object.ReferenceEquals(t, null) ? new EquipmentScriptBase() : (Activator.CreateInstance(t) as EquipmentScriptBase);
				if (sc != null)
				{
					gift.script = sc;
					gift.script.SetModel(gift);
				}
			}
			catch (Exception e)
			{
				Batch2Err("GiftScriptPrefix", e);
			}
			return false;
		}

		private static Type GiftScriptType(string name)
		{
			if (_giftTypes == null)
			{
				Dictionary<string, Type> map = new Dictionary<string, Type>(StringComparer.Ordinal);
				List<Assembly> list = Add_On.instance == null ? null : Add_On.instance.AssemList;
				for (int i = 0; list != null && i < list.Count; i++)
				{
					Type[] types;
					try
					{
						types = list[i].GetTypes();
					}
					catch (ReflectionTypeLoadException e)
					{
						types = e.Types;
					}
					catch (Exception)
					{
						continue;
					}
					for (int k = 0; !object.ReferenceEquals(types, null) && k < types.Length; k++)
					{
						Type t = types[k];
						if (!object.ReferenceEquals(t, null) && typeof(EquipmentScriptBase).IsAssignableFrom(t) && !map.ContainsKey(t.Name))
						{
							map[t.Name] = t;
						}
					}
				}
				_giftTypes = map;
			}
			Type found;
			if (_giftTypes.TryGetValue(name, out found))
			{
				return found;
			}
			Type g = Type.GetType(name);
			return !object.ReferenceEquals(g, null) && typeof(EquipmentScriptBase).IsAssignableFrom(g) ? g : null;
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

		// ----- vanilla: a creature unit without an animation script -----

		private static readonly HashSet<long> _noAnimLogged = new HashSet<long>();

		// CreatureUnit.OnChangeState calls animTarget.HasDeadMotion / PlayRevivalMotion before its own null check of animTarget.
		private static void InstallCreatureNoAnim()
		{
			const string key = "FastFix.CreatureNoAnimState";
			if (!N.Config.BindFlag(key, true, "A creature without an animation script (some mods) no longer throws when it is suppressed or returns; its return marker is still switched. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(CreatureUnit).GetMethod("OnChangeState", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(m, null) || !object.ReferenceEquals(m.DeclaringType, typeof(CreatureUnit)))
				{
					Missing(key, "CreatureUnit.OnChangeState");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("CreatureNoAnimPrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		// Runs vanilla when animTarget exists (a destroyed script too: vanilla calls its C# overrides); otherwise the null-safe part
		// of vanilla. No try/catch on purpose: failing open after OnAfterSuppressed would call it a second time in vanilla, and the
		// only throws left (script null, OnAfterSuppressed) throw in vanilla too. A subclass override without base is not covered.
		public static bool CreatureNoAnimPrefix(CreatureUnit __instance)
		{
			if (!object.ReferenceEquals(__instance.animTarget, null))
			{
				return true;
			}
			CreatureModel model = __instance.model;
			if (model == null)
			{
				return true;
			}
			if (_noAnimLogged.Add(model.metadataId))
			{
				N.ULog.Info(FastFixModule.Tag, "creature " + model.metadataId + " has no animation script: state change handled without it (logged once per creature)");
			}
			bool show;
			if (model.canBeSuppressed && (model.state == CreatureState.SUPPRESSED || model.state == CreatureState.SUPPRESSED_RETURN))
			{
				if (model.script.OnAfterSuppressed())
				{
					return false;
				}
				show = true;
			}
			else
			{
				show = false;
			}
			if (__instance.returnObject != null)
			{
				__instance.returnObject.gameObject.SetActive(show);
			}
			return false;
		}

		// ----- vanilla: damage factor of equipment and buffs -----

		private static bool _dmgFactorErr;
		private static bool _dmgBufs;

		// UnitModel.GetDamageFactorByEquipment tests weapon before reading armor (a unit with a weapon and no armor throws into its
		// empty catch and loses the gift factor) and drops the result of UnitBuf.GetDamageFactor. No vanilla buff overrides it;
		// mod buffs that do (e.g. Colored Fixer BlueReverb x0.7) change real outgoing damage, so that part has its own flag, off.
		// Full replacement: the original has try/catch and foreach (GL-53).
		private static void InstallDamageFactor()
		{
			const string key = "FastFix.DamageFactor";
			if (!N.Config.BindFlag(key, true, "A unit with a weapon and no armor no longer loses its gift damage factor to an error. Read at game start.").On)
			{
				return;
			}
			_dmgBufs = N.Config.BindFlag("FastFix.BufDamageFactor", false, "Damage factors of mod buffs are applied to outgoing damage (vanilla drops them). Changes balance of such mods. Needs FastFix.DamageFactor. Read at game start.").On;
			try
			{
				MethodInfo m = typeof(UnitModel).GetMethod("GetDamageFactorByEquipment", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(m, null))
				{
					Missing(key, "UnitModel.GetDamageFactorByEquipment");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("DamageFactorPrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool DamageFactorPrefix(UnitModel __instance, ref float __result)
		{
			float num = 1f;
			try
			{
				UnitEquipSpace eq = __instance.Equipment;
				if (eq != null)
				{
					if (eq.weapon != null)
					{
						num *= eq.weapon.GetDamageFactor();
					}
					if (eq.armor != null)
					{
						num *= eq.armor.GetDamageFactor();
					}
					if (eq.gifts != null)
					{
						num *= eq.gifts.GetDamageFactor();
					}
					List<UnitBuf> bufs = _dmgBufs ? __instance.GetUnitBufList() : null;
					if (bufs != null)
					{
						for (int i = 0; i < bufs.Count; i++)
						{
							UnitBuf b = bufs[i];
							if (b != null)
							{
								num *= b.GetDamageFactor();
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				// vanilla swallows here too and returns the factor reached so far
				if (!_dmgFactorErr)
				{
					_dmgFactorErr = true;
					N.ULog.Warn(FastFixModule.Tag, "FastFix.DamageFactor: a factor threw, the factor reached so far is used as in vanilla (logged once): " + e);
				}
			}
			__result = num;
			return false;
		}

		// ----- vanilla: sound sequence with no loaded clip -----

		private static FieldInfo _sndClips;
		private static bool _sndLogged;

		// SoundEffectPlayer.DeQueue calls Stop() on an empty queue or a null clip but goes on (Dequeue throws, or PlayOneShot(null)
		// and clip.length throw). PlaySequence calls it directly, so a sequence whose files all fail to load throws into its caller
		// (CreatureBase.MakeSoundQueue). The prefix closes the player and returns in those two cases; vanilla runs otherwise.
		private static void InstallSoundDeQueue()
		{
			const string key = "FastFix.SoundSequenceEmpty";
			if (!N.Config.BindFlag(key, true, "A sound sequence whose sound files are missing no longer throws; the sound player just closes. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(SoundEffectPlayer).GetMethod("DeQueue", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				_sndClips = typeof(SoundEffectPlayer).GetField("clips", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_sndClips, null) || !object.ReferenceEquals(_sndClips.FieldType, typeof(Queue<AudioClip>)))
				{
					Missing(key, "SoundEffectPlayer.DeQueue / clips");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("SoundDeQueuePrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		// Called once per clip of a sequence (not per frame): reflection is fine here.
		public static bool SoundDeQueuePrefix(SoundEffectPlayer __instance)
		{
			Queue<AudioClip> q;
			try
			{
				q = _sndClips.GetValue(__instance) as Queue<AudioClip>;
			}
			catch (Exception)
			{
				// field read failed: leave the call to vanilla
				return true;
			}
			if (q == null)
			{
				return true;
			}
			if (q.Count > 0 && q.Peek() != null)
			{
				return true;
			}
			// the whole queue: Update's "clips.Count != 0" check then never calls DeQueue again before the destroy
			q.Clear();
			if (!_sndLogged)
			{
				_sndLogged = true;
				N.ULog.Info(FastFixModule.Tag, "FastFix.SoundSequenceEmpty: a sound sequence had no loaded clip; the player was closed instead of throwing (logged once)");
			}
			__instance.Stop();
			return false;
		}

		// ----- vanilla: bullet count update before the bullet window exists -----

		private static bool _bulletLogged;

		// GlobalBulletManager.UpdateMaxBullet checks GlobalBulletWindow.CurrentWindow for null, then calls SetBulletCount on it
		// unguarded (reached from CreatureOverloadManager.OnStageStart and Sefira.UpdateOfficerAliveLevel). Without a window the
		// prefix does the same arithmetic and skips the window; with one vanilla runs.
		private static void InstallBulletMax()
		{
			const string key = "FastFix.BulletWindowNull";
			if (!N.Config.BindFlag(key, true, "Updating the bullet count before the bullet panel exists no longer throws. Read at game start.").On)
			{
				return;
			}
			try
			{
				MethodInfo m = typeof(GlobalBullet.GlobalBulletManager).GetMethod("UpdateMaxBullet", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
				if (object.ReferenceEquals(m, null))
				{
					Missing(key, "GlobalBulletManager.UpdateMaxBullet");
					return;
				}
				FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("BulletMaxPrefix")), null);
				Patched(key, m);
			}
			catch (Exception e)
			{
				N.ULog.Error(FastFixModule.Tag, key + " install failed: " + e);
			}
		}

		public static bool BulletMaxPrefix(GlobalBullet.GlobalBulletManager __instance)
		{
			if (GlobalBullet.GlobalBulletWindow.CurrentWindow != null)
			{
				return true;
			}
			__instance.maxBullet = __instance.initialMaxBullet;
			if (MissionManager.instance.ExistsFinishedBossMission(SefiraEnum.TIPERERTH1))
			{
				__instance.maxBullet += (int)Mathf.Round((float)__instance.maxBullet * 0.3f);
			}
			__instance.maxBullet += SefiraAbilityValueInfo.chesedOfficerAliveValues[SefiraManager.instance.GetOfficerAliveLevel(SefiraEnum.CHESED)];
			if (__instance.currentBullet > __instance.maxBullet)
			{
				__instance.currentBullet = __instance.maxBullet;
			}
			if (!_bulletLogged)
			{
				_bulletLogged = true;
				N.ULog.Info(FastFixModule.Tag, "FastFix.BulletWindowNull: bullet count updated before the bullet panel exists; panel update skipped (logged once)");
			}
			return false;
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
			_moveChild = child;
			try
			{
				node.ProcessMoveNode(movement);
				_moveChild = null;
			}
			catch (MovableObjectNode.MovableElevatorStuckException)
			{
				_moveChild = null;
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

		// ----- kekesi_CustomRisk: suppress window postfix reads observeInfo of a child creature that has none -----

		// CreatureSuppressRegion_SetData reads target.observeInfo / metaInfo before its own dictionary check; a child
		// creature without base meta has observeInfo null (ChildCreatureModel.SetParent). Its postfix only recolours the
		// risk label of custom-risk creatures, so skipping it for such a target changes nothing else.
		private static void InstallCustomRisk(Assembly asm)
		{
			Type t = asm.GetType("kekesi_CustomRisk.Harmony_Patch");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("CreatureSuppressRegion_SetData", All);
			if (object.ReferenceEquals(m, null) || !m.IsStatic)
			{
				Missing("FastFix.CustomRiskChild", "kekesi_CustomRisk.Harmony_Patch.CreatureSuppressRegion_SetData");
				return;
			}
			ParameterInfo[] ps = m.GetParameters();
			if (ps.Length != 2 || ps[1].Name != "target" || !object.ReferenceEquals(ps[1].ParameterType, typeof(UnitModel)))
			{
				Missing("FastFix.CustomRiskChild", "CreatureSuppressRegion_SetData(_, UnitModel target)");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("CustomRiskPrefix")), null);
			Patched("FastFix.CustomRiskChild", m);
		}

		public static bool CustomRiskPrefix(UnitModel target)
		{
			CreatureModel c = target as CreatureModel;
			return c == null || (c.observeInfo != null && c.metaInfo != null);
		}

		// ----- FeatherMemories: HardModeManager.LoadSetting reads the mod data file every tick -----

		// FeatherMemories.OnFixedUpdate calls HardModeManager.LoadSetting each tick: File.Exists + File.ReadAllLines +
		// Split per line (~0.6 ms, ~24 KB per tick under breach). currentState changes only in LoadSetting and in
		// SaveSetting, which sets it itself, so after a load that found Enabled or Disabled the later loads are skipped.
		// While the state is NotSet (choice popup pending) every load still runs.
		private static bool _fhLoaded;
		private static PropertyInfo _fhState;

		private static void InstallFeatherHardMode(Assembly asm)
		{
			Type t = asm.GetType("FeatherMemories.HardModeManager");
			MethodInfo m = object.ReferenceEquals(t, null) ? null : t.GetMethod("LoadSetting", All);
			PropertyInfo p = object.ReferenceEquals(t, null) ? null : t.GetProperty("CurrentState", All);
			if (object.ReferenceEquals(m, null) || !m.IsStatic || m.GetParameters().Length != 0
				|| object.ReferenceEquals(p, null) || !p.PropertyType.IsEnum)
			{
				Missing("FastFix.FeatherHardMode", "FeatherMemories.HardModeManager.LoadSetting() / CurrentState");
				return;
			}
			_fhState = p;
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("FeatherLoadPrefix")), new HarmonyMethod(Own("FeatherLoadPostfix")));
			Patched("FastFix.FeatherHardMode", m);
		}

		public static bool FeatherLoadPrefix()
		{
			return !_fhLoaded;
		}

		public static void FeatherLoadPostfix()
		{
			if (_fhLoaded)
			{
				return;
			}
			try
			{
				_fhLoaded = Convert.ToInt32(_fhState.GetValue(null, null)) != 0;
			}
			catch (Exception)
			{
				_fhLoaded = false;
			}
		}

		// ----- FD: Enumerable.Contains<UnitModel>(List<AgentModel>) in OnFixedUpdate aborts Mono -----

		private static FieldInfo _fdEntry;
		private static FieldInfo _fdChecked;
		private static FieldInfo _fdRooms;
		private static MethodInfo _fdSteam;
		private static bool _fdFailed;

		// OnFixedUpdate has a foreach (handlers): Covariance fixes it only with 12Harmony. A replacing Prefix
		// needs none (the original body never runs); it repeats the steps with List<AgentModel>.Contains.
		private static void InstallFdTick(Assembly asm)
		{
			MethodInfo m = Find(asm, "FD.FD", "OnFixedUpdate", new Type[] { typeof(CreatureModel) });
			Type t = object.ReferenceEquals(m, null) ? null : m.DeclaringType;
			_fdEntry = object.ReferenceEquals(t, null) ? null : t.GetField("entryPassage", All);
			_fdChecked = object.ReferenceEquals(t, null) ? null : t.GetField("checkedUnits", All);
			_fdRooms = object.ReferenceEquals(t, null) ? null : t.GetField("affectedRooms", All);
			_fdSteam = object.ReferenceEquals(t, null) ? null : t.GetMethod("GiveSteamBuf", All, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(t, null) || !typeof(CreatureBase).IsAssignableFrom(t)
				|| object.ReferenceEquals(_fdEntry, null) || !object.ReferenceEquals(_fdEntry.FieldType, typeof(PassageObjectModel))
				|| object.ReferenceEquals(_fdChecked, null) || !object.ReferenceEquals(_fdChecked.FieldType, typeof(List<AgentModel>))
				|| object.ReferenceEquals(_fdRooms, null) || !typeof(System.Collections.ICollection).IsAssignableFrom(_fdRooms.FieldType)
				|| object.ReferenceEquals(_fdSteam, null))
			{
				Missing("FastFix.FDTick", "FD.OnFixedUpdate, entryPassage, checkedUnits, affectedRooms or GiveSteamBuf");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("FdTickPrefix")), null);
			Patched("FastFix.FDTick", m);
			N.ULog.Info(FastFixModule.Tag, "FastFix.FDTick covers the covariance place in FD.FD.OnFixedUpdate (a 'not fixed' covariance line for it can be ignored)");
		}

		public static bool FdTickPrefix(object __instance)
		{
			try
			{
				System.Collections.ICollection rooms = _fdRooms.GetValue(__instance) as System.Collections.ICollection;
				if (rooms != null && rooms.Count != 0)
				{
					_fdSteam.Invoke(__instance, null);
				}
				PassageObjectModel entry = _fdEntry.GetValue(__instance) as PassageObjectModel;
				List<AgentModel> checkedUnits = _fdChecked.GetValue(__instance) as List<AgentModel>;
				CreatureModel model = ((CreatureBase)__instance).model;
				// Before OnViewInit the entry passage is not set yet (the original throws there).
				if (entry == null || checkedUnits == null || model == null)
				{
					return false;
				}
				System.Collections.ObjectModel.ReadOnlyCollection<MovableObjectNode> targets = entry.GetEnteredTargets();
				for (int i = 0; i < targets.Count; i++)
				{
					MovableObjectNode node = targets[i];
					AgentModel agent = node == null ? null : node.GetUnit() as AgentModel;
					if (agent != null && !checkedUnits.Contains(agent) && agent.IsPanic())
					{
						checkedUnits.Add(agent);
						model.SubQliphothCounter();
					}
				}
			}
			catch (Exception e)
			{
				// Any exception: logged once, tick skipped (the original would throw or abort the game).
				if (!_fdFailed)
				{
					_fdFailed = true;
					N.ULog.Error(FastFixModule.Tag, "FD tick fix failed, tick skipped (logged once): " + e);
				}
			}
			return false;
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
				// Without 12Harmony: a Prefix that replaces the whole method (the original body never runs, GL-53 does not apply).
				InstallKekesiPrefix(m);
				return;
			}
			_hits = 0;
			FastFixModule.Harmony.Patch(m, null, null, new HarmonyMethod(Own("KekesiTranspiler")));
			Report("FastFix.KekesiWolfHit", m, "PassageObjectModel.GetEnteredTargets call");
		}

		private static FieldInfo _kwAttack;
		private static FieldInfo _kwEffect;
		private static FieldInfo _kwAttackTime;
		private static FieldInfo _kwSpecialAttackTime;
		private static FieldInfo _kwSpecialTime;
		private static FieldInfo _kwTime;
		private static FieldInfo _kwAnim;
		private static MethodInfo _kwNear;
		private static MethodInfo _kwMakeEffect;
		private static MethodInfo _kwMakeMovement;
		private static MethodInfo _kwStopMovement;
		private static MethodInfo _kwDestroyEffect;
		private static MethodInfo _kwAnimAttack;
		private static MethodInfo _kwAnimAttack2;
		private static MethodInfo _kwAnimDefault;
		private static Type _kwType;
		private static bool _kwFailed;

		private static void InstallKekesiPrefix(MethodInfo m)
		{
			Type t = m.DeclaringType;
			_kwType = t;
			_kwAttack = t.GetField("IsAttack", All);
			_kwEffect = t.GetField("IsEffect", All);
			_kwAttackTime = t.GetField("AttackTime", All);
			_kwSpecialAttackTime = t.GetField("SpecialAttackTime", All);
			_kwSpecialTime = t.GetField("SpecialTime", All);
			_kwTime = t.GetField("time", All);
			_kwAnim = t.GetField("animscript", All);
			_kwNear = t.GetMethod("GetNearEnemy", All, null, Type.EmptyTypes, null);
			_kwMakeEffect = t.GetMethod("MakeEffect", All, null, Type.EmptyTypes, null);
			_kwMakeMovement = t.GetMethod("MakeMovement", All, null, Type.EmptyTypes, null);
			_kwStopMovement = t.GetMethod("StopMovement", All, null, Type.EmptyTypes, null);
			_kwDestroyEffect = t.GetMethod("DestoryEffect", All, null, Type.EmptyTypes, null);
			Type anim = object.ReferenceEquals(_kwAnim, null) ? null : _kwAnim.FieldType;
			_kwAnimAttack = object.ReferenceEquals(anim, null) ? null : anim.GetMethod("Attack", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
			_kwAnimAttack2 = object.ReferenceEquals(anim, null) ? null : anim.GetMethod("Attack2", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
			_kwAnimDefault = object.ReferenceEquals(anim, null) ? null : anim.GetMethod("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (!typeof(CreatureBase).IsAssignableFrom(t)
				|| !IsField(_kwAttack, typeof(bool)) || !IsField(_kwEffect, typeof(bool))
				|| !IsField(_kwAttackTime, typeof(float)) || !IsField(_kwSpecialAttackTime, typeof(float))
				|| !IsField(_kwSpecialTime, typeof(float)) || !IsField(_kwTime, typeof(float))
				|| object.ReferenceEquals(_kwNear, null) || !object.ReferenceEquals(_kwNear.ReturnType, typeof(List<UnitModel>))
				|| object.ReferenceEquals(_kwMakeEffect, null) || object.ReferenceEquals(_kwMakeMovement, null)
				|| object.ReferenceEquals(_kwStopMovement, null) || object.ReferenceEquals(_kwDestroyEffect, null)
				|| object.ReferenceEquals(_kwAnimAttack, null) || object.ReferenceEquals(_kwAnimAttack2, null) || object.ReferenceEquals(_kwAnimDefault, null))
			{
				Missing("FastFix.KekesiWolfHit", "AmirWolf fields or methods for the copy without 12Harmony");
				return;
			}
			FastFixModule.Harmony.Patch(m, new HarmonyMethod(Own("KekesiEscapePrefix")), null);
			Patched("FastFix.KekesiWolfHit", m);
			N.ULog.Info(FastFixModule.Tag, "FastFix.KekesiWolfHit: 12Harmony not loaded, UniqueEscape replaced by a copy that hits a snapshot of the room");
		}

		private static bool IsField(FieldInfo f, Type type)
		{
			return !object.ReferenceEquals(f, null) && object.ReferenceEquals(f.FieldType, type);
		}

		// Copy of AmirWolf.UniqueEscape (KekesiWolf.AmirWolf.cs:223-341); the room hit loops over a snapshot of the room,
		// so a unit that dies and leaves the room does not break the loop.
		public static bool KekesiEscapePrefix(object __instance)
		{
			try
			{
				CreatureBase self = (CreatureBase)__instance;
				if ((bool)_kwAttack.GetValue(__instance))
				{
					return false;
				}
				object anim = _kwAnim.GetValue(__instance);
				float dt = Time.deltaTime;
				if (!(bool)_kwEffect.GetValue(__instance))
				{
					float attackTime = (float)((float)_kwAttackTime.GetValue(__instance) - dt);
					float specialAttackTime = (float)((float)_kwSpecialAttackTime.GetValue(__instance) - dt);
					_kwAttackTime.SetValue(__instance, attackTime);
					_kwSpecialAttackTime.SetValue(__instance, specialAttackTime);
					List<UnitModel> near = (List<UnitModel>)_kwNear.Invoke(__instance, null);
					if (near.Count > 0)
					{
						UnitModel target = near[UnityEngine.Random.Range(0, near.Count)];
						if (specialAttackTime <= 0f)
						{
							_kwStopMovement.Invoke(__instance, null);
							_kwMakeEffect.Invoke(__instance, null);
							_kwAnimAttack2.Invoke(anim, null);
							_kwSpecialAttackTime.SetValue(__instance, 20f);
							_kwEffect.SetValue(__instance, true);
						}
						else if (attackTime <= 0f)
						{
							MovableObjectNode movable = self.movable;
							movable.SetDirection(target.GetMovableNode().GetCurrentViewPosition().x < movable.GetCurrentViewPosition().x ? (UnitDirection)0 : (UnitDirection)1);
							_kwStopMovement.Invoke(__instance, null);
							_kwAnimAttack.Invoke(anim, null);
							_kwAttack.SetValue(__instance, true);
						}
					}
					else if (!self.movable.IsMoving())
					{
						_kwMakeMovement.Invoke(__instance, null);
					}
					return false;
				}
				float time = (float)((float)_kwTime.GetValue(__instance) - dt);
				_kwTime.SetValue(__instance, time);
				if (time <= 0f)
				{
					PassageObjectModel passage = self.model.GetMovableNode().GetPassage();
					if (passage != null)
					{
						// Local snapshot (one hit per 0.3 s): a unit that dies leaves the room list, not this copy.
						List<MovableObjectNode> targets = new List<MovableObjectNode>(passage.GetEnteredTargets());
						for (int i = 0; i < targets.Count; i++)
						{
							UnitModel unit = targets[i] == null ? null : targets[i].GetUnit();
							if (unit == null || unit == self.model)
							{
								continue;
							}
							WorkerModel worker = unit as WorkerModel;
							if (worker != null && !worker.IsDead())
							{
								worker.TakeDamage(new DamageInfo((RwbpType)3, 1, 5));
							}
							CreatureModel creature = unit as CreatureModel;
							if (creature != null && (creature.script == null || !_kwType.IsInstanceOfType(creature.script)))
							{
								creature.TakeDamage(new DamageInfo((RwbpType)3, 1, 5));
							}
						}
						_kwTime.SetValue(__instance, 0.3f);
					}
				}
				float specialTime = (float)((float)_kwSpecialTime.GetValue(__instance) - dt);
				_kwSpecialTime.SetValue(__instance, specialTime);
				if (specialTime <= 0f)
				{
					_kwSpecialAttackTime.SetValue(__instance, 20f);
					_kwSpecialTime.SetValue(__instance, 20f);
					_kwAnimDefault.Invoke(anim, null);
					_kwEffect.SetValue(__instance, false);
					_kwAttack.SetValue(__instance, false);
					Sefira sefira = SefiraManager.instance.GetSefira(self.currentPassage.GetSefiraName());
					for (int i = 0; i < sefira.passageList.Count; i++)
					{
						Notice.instance.Send(NoticeName.PassageWhitle, new object[] { sefira.passageList[i] });
					}
					_kwDestroyEffect.Invoke(__instance, null);
				}
			}
			catch (Exception e)
			{
				if (!_kwFailed)
				{
					_kwFailed = true;
					N.ULog.Error(FastFixModule.Tag, "KekesiWolf escape copy failed, tick skipped (logged once): " + e);
				}
			}
			return false;
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

		// ----- Four Fools: Zkbuf icon read before the mod folder is known -----

		private static void InstallFourFools(Assembly asm)
		{
			Type hp = asm.GetType("FourFools.Harmony_Patch");
			Type getter = asm.GetType("Modes_API_CFQYX.AssetGetter");
			FieldInfo icon = object.ReferenceEquals(hp, null) ? null : hp.GetField("zkBufSprite", BindingFlags.Public | BindingFlags.Static);
			FieldInfo dir = object.ReferenceEquals(hp, null) ? null : hp.GetField("path", BindingFlags.Public | BindingFlags.Static);
			MethodInfo load = object.ReferenceEquals(getter, null) ? null : getter.GetMethod("LoadSprite", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
			if (object.ReferenceEquals(icon, null) || object.ReferenceEquals(dir, null) || object.ReferenceEquals(load, null)
				|| !object.ReferenceEquals(icon.FieldType, typeof(Sprite)))
			{
				Missing("FastFix.FourFoolsBufIcon", "FourFools.Harmony_Patch.zkBufSprite, path or AssetGetter.LoadSprite");
				return;
			}
			if (!object.ReferenceEquals(icon.GetValue(null), null))
			{
				N.ULog.Info(FastFixModule.Tag, "FastFix.FourFoolsBufIcon not needed: zkBufSprite already loaded");
				return;
			}
			if (string.IsNullOrEmpty(dir.GetValue(null) as string))
			{
				Missing("FastFix.FourFoolsBufIcon", "FourFools.Harmony_Patch.path value");
				return;
			}
			Sprite sprite = load.Invoke(null, new object[] { "Joker2\\zkBuf" }) as Sprite;
			if (object.ReferenceEquals(sprite, null))
			{
				Missing("FastFix.FourFoolsBufIcon", "Joker2/zkBuf.png");
				return;
			}
			icon.SetValue(null, sprite);
			N.ULog.Info(FastFixModule.Tag, "FastFix.FourFoolsBufIcon installed: FourFools.Harmony_Patch.zkBufSprite loaded from Assets/Joker2/zkBuf.png");
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

	// ---------- CorpseSelectRing ----------

	// AgentUnit.OnSelect shows selectedIcon (OnEnter -> OnOverlayEnabled). AgentModel.OnDie calls UnitMouseEventManager.Unselect, which
	// only drops the target from its list and never calls the unit's OnUnselect; OnExit returns for a dead model anyway. A selected agent
	// that dies keeps the ring under the body for the rest of the day. Fix: Postfix AgentUnit.OnDie runs the vanilla unselect and
	// SelectIconDisable (selectIconState off, icon hidden). A revived agent gets the ring again on the next select or hover.
	public static class CorpseSelectRing
	{
		private static N.Flag _flag;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			MethodBase die = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(AgentUnit), "OnDie", Type.EmptyTypes);
			if (!object.ReferenceEquals(die, null))
			{
				N.PatchUtil.Install(harmony, FastFixModule.Tag, die, null, new HarmonyMethod(typeof(CorpseSelectRing).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
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
				__instance.OnUnselect();
				__instance.SelectIconDisable();
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Warn(FastFixModule.Tag, "corpse select ring not hidden (logged once): " + e.Message);
				}
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

	// Endlessgyro's ctor patches EquipmentTypeInfo.get_MaxNum(UnitModel, DamageInfo); the game has only get_MaxNum(), so its
	// Patch call throws (caught by the mod, written to BaseMods/LCerror.txt) and its EGO 2025411/2025412 keep the normal
	// limit instead of 1. Its EquipmentTypeInfo_MaxNum is a postfix in shape (ref int __result) and goes on get_MaxNum()
	// unless that getter already has a patch of the mod's own Harmony id (a fixed mod version). The getter has no handler (GL-53).
	public static class GyroMaxNumFix
	{
		private const string Owner = "Lobotomy.Endlessgyro";

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("Endlessgyro");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "endlessgyro fix not applied: Endlessgyro not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.EndlessgyroMaxNum", true, "Endlessgyro: its EGO limit of one copy works again (its patch targeted a method that does not exist). Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("Endlessgyro.Harmony_Patch");
			MethodInfo post = object.ReferenceEquals(t, null) ? null : t.GetMethod("EquipmentTypeInfo_MaxNum", BindingFlags.Public | BindingFlags.Static);
			MethodInfo target = typeof(EquipmentTypeInfo).GetMethod("get_MaxNum", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			ParameterInfo[] ps = object.ReferenceEquals(post, null) ? null : post.GetParameters();
			if (object.ReferenceEquals(target, null) || ps == null || ps.Length != 2 || !object.ReferenceEquals(ps[0].ParameterType, typeof(EquipmentTypeInfo)) || !object.ReferenceEquals(ps[1].ParameterType, typeof(int).MakeByRefType()) || ps[1].Name != "__result")
			{
				N.ULog.Warn(FastFixModule.Tag, "endlessgyro fix not applied: EquipmentTypeInfo_MaxNum or get_MaxNum not as expected");
				return true;
			}
			Patches info = PatchProcessor.IsPatched(target);
			if (info != null && (HasOwner(info.Prefixes) || HasOwner(info.Postfixes)))
			{
				N.ULog.Info(FastFixModule.Tag, "endlessgyro fix not needed: get_MaxNum already patched by the mod");
				return true;
			}
			FastFixModule.Harmony.Patch(target, null, new HarmonyMethod(post), null);
			N.ULog.Info(FastFixModule.Tag, "endlessgyro fix installed (EquipmentTypeInfo_MaxNum as Postfix on get_MaxNum)");
			return true;
		}

		private static bool HasOwner(ReadOnlyCollection<Patch> list)
		{
			if (list == null)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].owner == Owner)
				{
					return true;
				}
			}
			return false;
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

	// Agent name plate flicker on hover (vanilla AgentUI.SetOverlayState): hovering an agent scales its plate (ActiveControl) to
	// 1.15. UnitMouseEventManager knows the pointer is over the world only through uGUI enter/exit on its background, so the
	// enlarged plate under the pointer sends OnPointerExit, which clears the hover; the plate shrinks, the background gets the
	// pointer back and the agent is hovered again, every frame. Fix: Postfix: while enlarged, the plate does not block uGUI rays
	// (CanvasGroup.blocksRaycasts); at normal size it behaves as in the game.
	public static class AgentHover
	{
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(InGameUI.AgentUI), "SetOverlayState", new Type[] { typeof(bool) });
			if (object.ReferenceEquals(m, null))
			{
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(AgentHover).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Postfix(InGameUI.AgentUI __instance, bool b)
		{
			try
			{
				GameObject plate = __instance.ActiveControl;
				if (plate == null)
				{
					return;
				}
				CanvasGroup g = plate.GetComponent<CanvasGroup>();
				if (g == null)
				{
					if (!b)
					{
						return;
					}
					g = plate.AddComponent<CanvasGroup>();
				}
				g.blocksRaycasts = !b;
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "agent hover fix failed (logged once): " + e);
				}
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

	// Twilight armor (vanilla BossBirdArmor). GetDamageFactor reads _worker.hp, and _worker is set only by Init from
	// OnStageStart: a damage query on an armor given after the day started (by a mod) or before day start throws a
	// NullReferenceException. The Prefix returns the neutral factor 1 while _worker is null. No handler in the target (GL-53).
	public static class TwilightArmorNull
	{
		private static N.Flag _flag;
		private static FieldInfo _worker;
		private static bool _logged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			_worker = typeof(BossBirdArmor).GetField("_worker", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(BossBirdArmor), "GetDamageFactor", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_worker, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "twilight armor fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, new HarmonyMethod(typeof(TwilightArmorNull).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(BossBirdArmor __instance, ref float __result)
		{
			if (!_flag.On || _worker.GetValue(__instance) != null)
			{
				return true;
			}
			__result = 1f;
			if (!_logged)
			{
				_logged = true;
				N.ULog.Info(FastFixModule.Tag, "twilight armor: damage factor asked before the armor was set up, 1 returned (logged once)");
			}
			return false;
		}
	}

	// We Can Change Anything (vanilla IronMaiden). OnEnterRoom stores lockupAgent and the machine animation hides the agent's
	// animRoot (IronMaidenAnim.LockAgent). OnReleaseWork tears down (End animation, lockupAgent = null, timer stopped) only
	// when the agent died; nothing in the game shows animRoot again (IL scan 2026-11-11). An agent who leaves alive walks on
	// invisible, the machine stays in its working animation, and OnStageEnd kills him because lockupAgent is still set.
	// Fix: a Postfix on OnReleaseWork does the same teardown for a living agent and shows his animRoot again.
	// No handler in the target (GL-53); OnFixedUpdateInSkill (try/catch) is not patched.
	public static class IronMaidenExit
	{
		private static N.Flag _flag;
		private static FieldInfo _lockup;
		private static FieldInfo _anim;
		private static FieldInfo _timer;
		private static FieldInfo _cur;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
			_lockup = typeof(IronMaiden).GetField("lockupAgent", f);
			_anim = typeof(IronMaiden).GetField("_anim", f);
			_timer = typeof(IronMaiden).GetField("_genEnergyTimer", f);
			_cur = typeof(IronMaidenAnim).GetField("curAgentObj", f);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(IronMaiden), "OnReleaseWork", new Type[] { typeof(UseSkill) });
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_lockup, null) || object.ReferenceEquals(_anim, null) || object.ReferenceEquals(_timer, null) || object.ReferenceEquals(_cur, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "we can change anything fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(IronMaidenExit).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Postfix(IronMaiden __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				AgentModel a = _lockup.GetValue(__instance) as AgentModel;
				if (a == null || a.IsDead() || a.hp <= 0f)
				{
					return;
				}
				_lockup.SetValue(__instance, null);
				global::Timer t = _timer.GetValue(__instance) as global::Timer;
				if (t != null)
				{
					t.StopTimer();
				}
				IronMaidenAnim anim = _anim.GetValue(__instance) as IronMaidenAnim;
				if (anim != null)
				{
					_cur.SetValue(anim, null);
					anim.UpdateGeneratingLevel(-1);
					anim.CompleteGenerating();
				}
				AgentUnit u = a.GetUnit();
				if (u != null && u.animRoot != null)
				{
					u.animRoot.gameObject.SetActive(true);
				}
				N.ULog.Info(FastFixModule.Tag, "we can change anything: agent " + a.name + " left alive, shown again and released");
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "we can change anything fix error (logged once): " + e);
				}
			}
		}
	}

	// CENSORED (vanilla). The roulette victim walks to the unit under Uncontrollable_Censored; when CENSORED escapes first,
	// Execute calls the private Destroy(), which clears commands, invincibility and the animator but never returns control:
	// the victim stays CANNOT_CONTROLL with this action set, and Execute calls Destroy (and OnSacrificeFail) every frame until
	// the day ends (bug-wiki #71, reproduced 2026-11-11). Fix: a Postfix on Destroy gives control back in the escape case only;
	// the OnStageEnd call stays vanilla (AgentModel.OnStageRelease returns control itself). No handler in Destroy (GL-53).
	public static class CensoredVictimRelease
	{
		private static N.Flag _flag;
		private static FieldInfo _model;
		private static FieldInfo _script;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony, N.Flag flag)
		{
			_flag = flag;
			BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
			_model = typeof(Uncontrollable_Censored).GetField("model", f);
			_script = typeof(Uncontrollable_Censored).GetField("script", f);
			MethodBase m = N.PatchUtil.Resolve(FastFixModule.Tag, typeof(Uncontrollable_Censored), "Destroy", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null) || object.ReferenceEquals(_model, null) || object.ReferenceEquals(_script, null))
			{
				N.ULog.Warn(FastFixModule.Tag, "censored victim fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, FastFixModule.Tag, m, null, new HarmonyMethod(typeof(CensoredVictimRelease).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Postfix(Uncontrollable_Censored __instance)
		{
			if (!_flag.On)
			{
				return;
			}
			try
			{
				AgentModel a = _model.GetValue(__instance) as AgentModel;
				Censored s = _script.GetValue(__instance) as Censored;
				if (a == null || s == null || s.model == null || !s.model.IsEscaped())
				{
					return;
				}
				if (a.IsDead() || !a.CannotControll() || !object.ReferenceEquals(a.unconAction, __instance))
				{
					return;
				}
				a.GetControl();
				N.ULog.Info(FastFixModule.Tag, "censored victim released: " + a.name + " gets control back after CENSORED escaped");
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(FastFixModule.Tag, "censored victim fix error (logged once): " + e);
				}
			}
		}
	}

	// YaZi (third-party creature mod). Its Prefix on AgentModel.OnDie looks up creature 100015 and casts its script to YaZi
	// without a null check, but 100015 is WhiteNight (YaZi is 122107): with WhiteNight in the facility every death of an agent
	// with a target throws a NullReferenceException; without it the body does nothing. Fix: a Prefix skips that body while
	// creature 100015 is not a YaZi, so the mod behaves as before minus the exception. No handler in the body (GL-53).
	public static class YaZiOnDieFix
	{
		private static Type _yazi;
		private static bool _logged;

		internal static bool TryInstall(bool last)
		{
			Assembly a = WhiteRabbitFix.FindAssembly("YaZi");
			if (object.ReferenceEquals(a, null))
			{
				if (last)
				{
					N.ULog.Info(FastFixModule.Tag, "yazi ondie fix not applied: YaZi not loaded");
				}
				return last;
			}
			N.Flag flag = N.Config.BindFlag("FastFix.YaZiOnDie", true, "YaZi: agent deaths while WhiteNight is in the facility no longer log a NullReferenceException from AgentModel_OnDie. Read at game start.");
			if (!flag.On)
			{
				return true;
			}
			Type t = a.GetType("YaZi.Harmony_Patch");
			_yazi = a.GetType("YaZi.YaZi");
			MethodInfo target = object.ReferenceEquals(t, null) ? null : t.GetMethod("AgentModel_OnDie", BindingFlags.Public | BindingFlags.Static);
			ParameterInfo[] ps = object.ReferenceEquals(target, null) ? null : target.GetParameters();
			if (object.ReferenceEquals(_yazi, null) || ps == null || ps.Length != 1 || !object.ReferenceEquals(ps[0].ParameterType, typeof(AgentModel)) || !object.ReferenceEquals(target.ReturnType, typeof(void)))
			{
				N.ULog.Warn(FastFixModule.Tag, "yazi ondie fix not applied: members not found");
				return true;
			}
			FastFixModule.Harmony.Patch(target, new HarmonyMethod(typeof(YaZiOnDieFix).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null, null);
			N.ULog.Info(FastFixModule.Tag, "yazi ondie fix installed");
			return true;
		}

		// The original reads only creature 100015, so the decision does not need the dying agent.
		public static bool Prefix()
		{
			try
			{
				CreatureModel c = CreatureManager.instance.FindCreature(100015L);
				if (c == null || _yazi.IsInstanceOfType(c.script))
				{
					return true;
				}
				if (!_logged)
				{
					_logged = true;
					N.ULog.Info(FastFixModule.Tag, "yazi ondie fix: body skipped, creature 100015 is not YaZi");
				}
				return false;
			}
			catch (Exception)
			{
				return true;
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
				// A dead agent's weapon is released at day end (AgentModel.OnStageRelease); his unit has no weapon region to refresh.
				if (__instance.IsDead())
				{
					return;
				}
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
				N.Alerts.Add(2, N.Alerts.PathOf(pm), "patch " + name + " skipped for this session after repeated errors (" + w.Reason + ")");
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

		// Frames whose type could not be read (logged once each).
		private static readonly Dictionary<string, bool> _badFrames = new Dictionary<string, bool>();

		// A frame's type can fail to load (TypeLoadException from GetMethods on a mod type with a missing reference): that frame is
		// unknown, as an unresolvable one; not a failure of the guard.
		private static MethodInfo ResolveFrame(string line)
		{
			try
			{
				return ResolveFrameCore(line);
			}
			catch (Exception e)
			{
				int open = line.IndexOf(" (", StringComparison.Ordinal);
				string where = open > 0 ? line.Substring(0, open) : line;
				if (!_badFrames.ContainsKey(where) && _badFrames.Count < 64)
				{
					_badFrames[where] = true;
					N.ULog.Info(FastFixModule.Tag, "guard: frame " + where + " unreadable (" + e.GetType().Name + ": " + e.Message + "), frame skipped");
				}
				return null;
			}
		}

		// NoFrame for a frame of an excluded assembly, null when unresolvable, else the mod method.
		private static MethodInfo ResolveFrameCore(string line)
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
			N.Alerts.Add(2, N.Alerts.PathOf(m), "method " + name + " skipped for this session after repeated errors (" + w.Reason + ")");
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
