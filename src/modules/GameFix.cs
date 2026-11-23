// GameFix: NailOptimization module (api level 9). Opt-in fixes of the game's own rules (bug-wiki #22 #55 #27).
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine. Output goes to <NailOptimization>/modules/.
// Every option changes a rule of the game, so each one is off by default and read at game start; a patch is installed only
// when its option is on. None of the patched methods has a try/catch/using/foreach (GL-53, checked 2026-11-11).

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Harmony;
using UnityEngine;
using N = NailOptimization;

namespace GameFix
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class GameFixModule : N.ModuleBase
	{
		public const string Id = "GameFix";
		public const string HarmonyId = "Lobotomy.NailOptimization.GameFix";
		internal const string Tag = "gamefix";

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
				return "1.0";
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
			N.Flag teddy = N.Config.BindFlag("GameFix.TeddyDayEnd", false, "Rule change: an agent caught in Happy Teddy's hug when the day ends dies, as the hug would have ended. In the game he survives. Read at game start.");
			N.Flag tiphereth = N.Config.BindFlag("GameFix.TipherethLower", false, "Rule change: agents of the lower Central Command team get the continued-service stat bonus, as the upper team does. Read at game start.");
			N.Flag dragon = N.Config.BindFlag("GameFix.ApostleDragon", false, "Rule change: the Yin and Yang dragon passes WhiteNight's apostles, as it passes WhiteNight. Read at game start.");
			N.Flag bird = N.Config.BindFlag("GameFix.BigBirdTrumpet", false, "Fix: after Big Bird is suppressed, the second trumpet lets it out again only when the alarm has dropped below the second trumpet or risen higher. In the game its own escape keeps the trumpet up, so it can escape again and again. Read at game start.");
			Harmony = HarmonyInstance.Create(HarmonyId);
			Install(teddy, tiphereth, dragon);
			if (bird.On)
			{
				InstallBird();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InstallBird()
		{
			BigBirdTrumpet.Install(Harmony);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Install(N.Flag teddy, N.Flag tiphereth, N.Flag dragon)
		{
			if (teddy.On)
			{
				TeddyDayEnd.Install(Harmony);
			}
			if (tiphereth.On)
			{
				TipherethLower.Install(Harmony);
			}
			if (dragon.On)
			{
				ApostleDragon.Install(Harmony);
			}
		}
	}

	// Happy Teddy (vanilla HappyTeddy). The hug starts in OnFixedUpdate (isKilling = false, death animator, dead type SPECIAL);
	// the creature stays in WORKING_SCENE until the hug ends and the agent dies from the animation event TeddyAnim.OnCalled(1).
	// OnStageRelease kills lastAgent only while isKilling (still walking to the bear), so a day that ends during the hug lets
	// the agent live. Fix: on a normal day end (GameManager.ExitStage in this frame; not on restart, checkpoint or title, which
	// reload the day), a Prefix on OnStageRelease finishes the hugged agent with Die(), as vanilla does in its isKilling branch.
	// Not covered: the short window after KillMoment (state already WAIT, Die() not yet called); the agent survives there as in the game.
	public static class TeddyDayEnd
	{
		private static FieldInfo _isKilling;
		private static int _exitFrame = -1;
		private static bool _errorLogged;

		internal static void Install(HarmonyInstance harmony)
		{
			_isKilling = typeof(HappyTeddy).GetField("isKilling", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodBase release = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(HappyTeddy), "OnStageRelease", Type.EmptyTypes);
			MethodBase exit = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(GameManager), "ExitStage", Type.EmptyTypes);
			if (object.ReferenceEquals(release, null) || object.ReferenceEquals(exit, null) || object.ReferenceEquals(_isKilling, null))
			{
				N.ULog.Warn(GameFixModule.Tag, "happy teddy fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, GameFixModule.Tag, exit, new HarmonyMethod(typeof(TeddyDayEnd).GetMethod("ExitPrefix", BindingFlags.Public | BindingFlags.Static)), null);
			N.PatchUtil.Install(harmony, GameFixModule.Tag, release, new HarmonyMethod(typeof(TeddyDayEnd).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static void ExitPrefix()
		{
			_exitFrame = Time.frameCount;
		}

		public static void Prefix(HappyTeddy __instance)
		{
			if (Time.frameCount != _exitFrame)
			{
				return;
			}
			try
			{
				AgentModel a = __instance.lastAgent;
				if (a == null || a.IsDead() || __instance.model == null || __instance.model.state != CreatureState.WORKING_SCENE)
				{
					return;
				}
				if ((bool)_isKilling.GetValue(__instance) || !a.CannotControll())
				{
					return;
				}
				N.ULog.Info(GameFixModule.Tag, "happy teddy: agent " + a.name + " was in the hug when the day ended, dies as the hug would end");
				a.Die();
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(GameFixModule.Tag, "happy teddy fix error (logged once): " + e);
				}
			}
		}
	}

	// Central Command continued service (vanilla AgentModel.Get*StatBySefiraAbility). The four stat methods add the
	// Tiphereth continued-service value only for TIPERERTH1; SefiraAbilityValueInfo.GetContinuousServiceValues maps both
	// teams to the same table. Fix: a Postfix adds the same value for TIPERERTH2.
	public static class TipherethLower
	{
		internal static void Install(HarmonyInstance harmony)
		{
			HarmonyMethod post = new HarmonyMethod(typeof(TipherethLower).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static));
			string[] names = new string[] { "GetFortitudeStatBySefiraAbility", "GetPrudenceStatBySefiraAbility", "GetTemperanceStatBySefiraAbility", "GetJusticeStatBySefiraAbility" };
			int done = 0;
			for (int i = 0; i < names.Length; i++)
			{
				MethodBase m = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(AgentModel), names[i], Type.EmptyTypes);
				if (!object.ReferenceEquals(m, null) && N.PatchUtil.Install(harmony, GameFixModule.Tag, m, null, post))
				{
					done++;
				}
			}
			N.ULog.Info(GameFixModule.Tag, "lower Central Command continued service: " + done + " of 4 stat methods patched");
		}

		public static void Postfix(AgentModel __instance, ref int __result)
		{
			if (__instance.currentSefiraEnum != SefiraEnum.TIPERERTH2)
			{
				return;
			}
			int[] values = SefiraAbilityValueInfo.tipherethContinuousServiceValues;
			int level = __instance.GetContinuousServiceLevel();
			if (values != null && level >= 1 && level <= values.Length)
			{
				__result += values[level - 1];
			}
		}
	}

	// Yin and Yang dragon (vanilla YinAndYangDragonCollider.OnTriggerEnter2D). The dragon flips hp of every creature it
	// touches except Binah/Geburah cores, WhiteNight, Black Swan and Big Bird; WhiteNight's apostles are not in the list.
	// Fix: a Prefix skips the original when the touched unit is an apostle.
	public static class ApostleDragon
	{
		private static bool _errorLogged;
		private static bool _skipLogged;

		internal static void Install(HarmonyInstance harmony)
		{
			MethodBase m = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(YinAndYangDragonCollider), "OnTriggerEnter2D", new Type[] { typeof(Collider2D) });
			if (object.ReferenceEquals(m, null))
			{
				N.ULog.Warn(GameFixModule.Tag, "apostle dragon fix not applied: YinAndYangDragonCollider.OnTriggerEnter2D not found");
				return;
			}
			N.PatchUtil.Install(harmony, GameFixModule.Tag, m, new HarmonyMethod(typeof(ApostleDragon).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static bool Prefix(Collider2D collision)
		{
			try
			{
				if (collision == null)
				{
					return true;
				}
				UnitMouseEventTarget c = collision.GetComponent<UnitMouseEventTarget>();
				if (c == null || c.target == null)
				{
					return true;
				}
				CreatureUnit u = c.target.GetComponent<CreatureUnit>();
				if (u == null || u.model == null)
				{
					return true;
				}
				if (!(u.model.script is WhiteNightSpace.DeathAngelApostle))
				{
					return true;
				}
				if (!_skipLogged)
				{
					_skipLogged = true;
					N.ULog.Info(GameFixModule.Tag, "yin and yang dragon passed an apostle (logged once)");
				}
				return false;
			}
			catch (Exception e)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					N.ULog.Error(GameFixModule.Tag, "apostle dragon fix error (logged once): " + e);
				}
				return true;
			}
		}
	}

	// Big Bird (vanilla BigBird). OnFixedUpdate sets the Qliphoth counter to 0 (escape) whenever escapeCoolTimer is not running
	// and the emergency level is LEVEL2 or higher; the cooldown after a suppression is 30-40 s. Its own escape adds emergency
	// score and blocks the score recovery for 30 s, so the second trumpet can outlast every cooldown (input/Buglist.txt:37-39).
	// Fix: after a suppression the bird is held while the level stays at or below the level it had then (and at LEVEL2 or more):
	// a Prefix keeps escapeCoolTimer running in 5 s steps, so vanilla takes its timer branch. A drop below LEVEL2 or a rise above
	// that level releases the hold. Escapes from work results are untouched.
	public static class BigBirdTrumpet
	{
		private const float HoldStep = 5f;
		private static FieldInfo _coolField;
		private static readonly System.Collections.Generic.Dictionary<BigBird, Hold> _held = new System.Collections.Generic.Dictionary<BigBird, Hold>();
		private static bool _errorLogged;
		private static int _holds;

		private class Hold
		{
			public Timer Cool;
			public EmergencyLevel Level;
			public bool Logged;
		}

		internal static void Install(HarmonyInstance harmony)
		{
			_coolField = typeof(BigBird).GetField("escapeCoolTimer", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodBase tick = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(BigBird), "OnFixedUpdate", new Type[] { typeof(CreatureModel) });
			MethodBase suppressed = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(BigBird), "OnAfterSuppressed", Type.EmptyTypes);
			MethodBase start = N.PatchUtil.Resolve(GameFixModule.Tag, typeof(BigBird), "OnStageStart", Type.EmptyTypes);
			if (object.ReferenceEquals(_coolField, null) || object.ReferenceEquals(tick, null) || object.ReferenceEquals(suppressed, null) || object.ReferenceEquals(start, null))
			{
				N.ULog.Warn(GameFixModule.Tag, "big bird trumpet fix not applied: members not found");
				return;
			}
			N.PatchUtil.Install(harmony, GameFixModule.Tag, suppressed, null, new HarmonyMethod(typeof(BigBirdTrumpet).GetMethod("SuppressedPostfix", BindingFlags.Public | BindingFlags.Static)));
			N.PatchUtil.Install(harmony, GameFixModule.Tag, start, null, new HarmonyMethod(typeof(BigBirdTrumpet).GetMethod("StartPostfix", BindingFlags.Public | BindingFlags.Static)));
			N.PatchUtil.Install(harmony, GameFixModule.Tag, tick, new HarmonyMethod(typeof(BigBirdTrumpet).GetMethod("TickPrefix", BindingFlags.Public | BindingFlags.Static)), null);
		}

		public static void SuppressedPostfix(BigBird __instance)
		{
			try
			{
				Timer cool = _coolField.GetValue(__instance) as Timer;
				if (cool == null)
				{
					return;
				}
				Hold h = new Hold();
				h.Cool = cool;
				h.Level = PlayerModel.emergencyController.currentLevel;
				_held[__instance] = h;
			}
			catch (Exception e)
			{
				LogError(e);
			}
		}

		public static void StartPostfix()
		{
			_held.Clear();
		}

		public static void TickPrefix(BigBird __instance)
		{
			if (_held.Count == 0)
			{
				return;
			}
			try
			{
				Hold h;
				if (!_held.TryGetValue(__instance, out h))
				{
					return;
				}
				EmergencyLevel level = PlayerModel.emergencyController.currentLevel;
				if (level < EmergencyLevel.LEVEL2 || level > h.Level)
				{
					_held.Remove(__instance);
					return;
				}
				if (h.Cool.started)
				{
					return;
				}
				h.Cool.StartTimer(HoldStep);
				if (!h.Logged)
				{
					h.Logged = true;
					_holds++;
					N.ULog.Info(GameFixModule.Tag, "big bird held: emergency level " + level + " has not dropped since its suppression (" + _holds + ")");
				}
			}
			catch (Exception e)
			{
				LogError(e);
			}
		}

		private static void LogError(Exception e)
		{
			if (!_errorLogged)
			{
				_errorLogged = true;
				N.ULog.Error(GameFixModule.Tag, "big bird trumpet fix error (logged once): " + e);
			}
		}
	}

}
