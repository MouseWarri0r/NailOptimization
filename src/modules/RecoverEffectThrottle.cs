// RecoverEffectThrottle: NailOptimization module (api level 3). Replaces the standalone mod Lobotomy.RecoverEffectThrottle.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine. Output goes to <NailOptimization>/modules/.
// The standalone RecoverEffectThrottle.dll must be removed from BaseMods (double Prefix otherwise).
//
// Behavior (unchanged from the standalone mod):
//   AgentModel.MakeRecoverEffect(bool isPhyiscal) Prefix: per-agent/per-type cooldown + per-frame/per-second budget.
//   AgentModel.RecoverHP/RecoverMental(float amount) Prefix+Postfix: mark "nothing to heal" so the effect is skipped.
// Parameter names are bound by Harmony: "isPhyiscal" (typo is in the game), "amount", "__instance".

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Harmony;
using UnityEngine;
using N = NailOptimization;

namespace RecoverEffectThrottle
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class RecoverModule : N.ModuleBase
	{
		public const string Id = "RecoverEffectThrottle";
		public const string HarmonyId = "Lobotomy.NailOptimization.RecoverEffectThrottle";

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
				return 3;
			}
		}

		public override void Init()
		{
			RecoverPatch.Bind();
			N.ResetRegistry.Register(new RecoverResetter());
			int main = Install();
			if (main == 0)
			{
				throw new InvalidOperationException("AgentModel.MakeRecoverEffect(isPhyiscal) was not patched");
			}
		}

		// Only keys that change cached decisions. Budget limits are read live and need no action.
		public override void OnConfigChanged(string key)
		{
			if (string.Equals(key, RecoverPatch.KeyEnabled, StringComparison.Ordinal)
				|| string.Equals(key, RecoverPatch.KeySkip, StringComparison.Ordinal)
				|| string.Equals(key, RecoverPatch.KeyCooldown, StringComparison.Ordinal))
			{
				RecoverPatch.ResetAll();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int Install()
		{
			Type agent = N.TypeCache.Resolve("AgentModel");
			if (object.ReferenceEquals(agent, null))
			{
				N.ULog.Error(Id, "type not found: AgentModel");
				return 0;
			}

			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			HarmonyMethod preEffect = new HarmonyMethod(typeof(RecoverPatch).GetMethod("Prefix", pub));
			HarmonyMethod preHp = new HarmonyMethod(typeof(RecoverPatch).GetMethod("PrefixHP", pub));
			HarmonyMethod preMental = new HarmonyMethod(typeof(RecoverPatch).GetMethod("PrefixMental", pub));
			HarmonyMethod postClear = new HarmonyMethod(typeof(RecoverPatch).GetMethod("PostfixClear", pub));

			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			MethodInfo[] methods = agent.GetMethods(flags);
			int effect = 0;
			int hp = 0;
			int mental = 0;
			for (int i = 0; i < methods.Length; i++)
			{
				MethodInfo m = methods[i];
				ParameterInfo[] ps = m.GetParameters();
				if (ps.Length != 1)
				{
					continue;
				}
				if (m.Name == "MakeRecoverEffect" && ps[0].Name == "isPhyiscal")
				{
					if (N.PatchUtil.Install(harmony, Id, m, preEffect, null))
					{
						effect++;
					}
				}
				else if (m.Name == "RecoverHP" && ps[0].Name == "amount")
				{
					if (N.PatchUtil.Install(harmony, Id, m, preHp, postClear))
					{
						hp++;
					}
				}
				else if (m.Name == "RecoverMental" && ps[0].Name == "amount")
				{
					if (N.PatchUtil.Install(harmony, Id, m, preMental, postClear))
					{
						mental++;
					}
				}
			}
			N.ULog.Info(Id, "patched MakeRecoverEffect " + effect + ", RecoverHP " + hp + ", RecoverMental " + mental);
			return effect;
		}
	}

	// Registered with Core: day-end reset of per-session state.
	public sealed class RecoverResetter : N.IResettable
	{
		public void ResetState()
		{
			RecoverPatch.ResetAll();
		}
	}

	public sealed class RecoverBudget
	{
		private int frame = -1;
		private int usedFrame = 0;
		private float windowStart = 0f;
		private int usedWindow = 0;

		public bool TryTake(int perFrame, int perSecond)
		{
			int f = Time.frameCount;
			if (f != frame)
			{
				frame = f;
				usedFrame = 0;
			}
			float now = Time.unscaledTime;
			if (now - windowStart >= 1f || now < windowStart)
			{
				windowStart = now;
				usedWindow = 0;
			}
			if (usedFrame >= perFrame || usedWindow >= perSecond)
			{
				return false;
			}
			usedFrame++;
			usedWindow++;
			return true;
		}

		public void Reset()
		{
			frame = -1;
			usedFrame = 0;
			windowStart = 0f;
			usedWindow = 0;
		}
	}

	public static class RecoverPatch
	{
		public const string KeyEnabled = "RecoverEffectThrottle.Enabled";
		public const string KeyCooldown = "RecoverEffectThrottle.CooldownPerAgent";
		public const string KeyPerFrame = "RecoverEffectThrottle.MaxPerFrame";
		public const string KeyPerSecond = "RecoverEffectThrottle.MaxPerSecond";
		public const string KeySkip = "RecoverEffectThrottle.SkipWhenNothingToHeal";

		private static N.Flag enabled;
		private static N.FloatParam cooldownP;
		private static N.IntParam perFrameP;
		private static N.IntParam perSecondP;
		private static N.Flag skipEmpty;

		private static N.Counter cAllowed;
		private static N.Counter cCooldown;
		private static N.Counter cBudget;
		private static N.Counter cNothing;

		// [0] = HP, [1] = mental: game time before which no new effect is created. Cleared completely at 2000 agents.
		private static N.BoundedCache<AgentModel, float[]> nextAllowed;
		private static RecoverBudget budget = new RecoverBudget();

		// Set in PrefixHP/PrefixMental when the heal changes nothing; consumed by Prefix (MakeRecoverEffect) and
		// cleared in PostfixClear. Calls are synchronous: one slot is enough; a nested heal of another agent
		// overwrites it and the effect is shown as in vanilla (safe side).
		private static object skipFor = null;
		private static bool skipPhysical = false;

		public static void Bind()
		{
			enabled = N.Config.BindFlag(KeyEnabled, true, "Master switch. Off: every patch of this module passes through to vanilla.");
			cooldownP = N.Config.BindFloat(KeyCooldown, 0.5f, 0f, 5f, 0.1f, "Seconds of game time between recover effects per agent and type (HP / mental). 0 = no cooldown.");
			perFrameP = N.Config.BindInt(KeyPerFrame, 4, 1, 50, 1, "Recover effects allowed per frame.");
			perSecondP = N.Config.BindInt(KeyPerSecond, 30, 1, 500, 5, "Recover effects allowed per real second.");
			skipEmpty = N.Config.BindFlag(KeySkip, true, "Skip the effect when the heal changes nothing (amount <= 0 or HP/mental already full).");

			cAllowed = N.Counters.Get("RecoverEffectThrottle", "allowed");
			cCooldown = N.Counters.Get("RecoverEffectThrottle", "skippedCooldown");
			cBudget = N.Counters.Get("RecoverEffectThrottle", "skippedBudget");
			cNothing = N.Counters.Get("RecoverEffectThrottle", "skippedNothing");

			nextAllowed = new N.BoundedCache<AgentModel, float[]>("RecoverEffectThrottle.nextAllowed", 2000, true);
		}

		public static void ResetAll()
		{
			skipFor = null;
			if (nextAllowed != null)
			{
				nextAllowed.Clear();
			}
			budget.Reset();
		}

		public static void PrefixHP(AgentModel __instance, float amount)
		{
			try
			{
				skipFor = null;
				if (!enabled.On || !skipEmpty.On || object.ReferenceEquals(__instance, null))
				{
					return;
				}
				if (amount <= 0f || __instance.hp >= (float)__instance.maxHp)
				{
					skipFor = __instance;
					skipPhysical = true;
				}
			}
			catch (Exception)
			{
				skipFor = null;
			}
		}

		public static void PrefixMental(AgentModel __instance, float amount)
		{
			try
			{
				skipFor = null;
				if (!enabled.On || !skipEmpty.On || object.ReferenceEquals(__instance, null))
				{
					return;
				}
				if (amount <= 0f || __instance.mental >= (float)__instance.maxMental)
				{
					skipFor = __instance;
					skipPhysical = false;
				}
			}
			catch (Exception)
			{
				skipFor = null;
			}
		}

		public static void PostfixClear()
		{
			skipFor = null;
		}

		public static bool Prefix(AgentModel __instance, bool isPhyiscal)
		{
			try
			{
				if (!enabled.On)
				{
					return true;
				}

				if (!object.ReferenceEquals(skipFor, null) && object.ReferenceEquals(skipFor, __instance) && skipPhysical == isPhyiscal)
				{
					skipFor = null;
					cNothing.Inc();
					return false;
				}

				if (object.ReferenceEquals(__instance, null))
				{
					return true;
				}

				float cooldown = cooldownP.Value;
				float now = Time.time;
				int idx = isPhyiscal ? 0 : 1;

				float[] arr;
				if (!nextAllowed.TryGet(__instance, out arr))
				{
					arr = new float[2];
					nextAllowed.Set(__instance, arr);
				}

				if (cooldown > 0f && now < arr[idx])
				{
					cCooldown.Inc();
					return false;
				}
				if (!budget.TryTake(perFrameP.Value, perSecondP.Value))
				{
					cBudget.Inc();
					return false;
				}

				arr[idx] = now + cooldown;
				cAllowed.Inc();
				return true;
			}
			catch (Exception)
			{
				return true;
			}
		}
	}
}
