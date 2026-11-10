// DamageEffectThrottle: NailOptimization module (api level 3). Replaces the standalone mod Lobotomy.DamageEffectThrottle.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine. Output goes to <NailOptimization>/modules/.
// The standalone DamageEffectThrottle.dll must be removed from BaseMods (double Prefix otherwise).
//
// 1) Damage popups: Prefix on UnitModel.MakeDamageEffect (the vanilla caller of DamageEffect.Invoker, ignores its result),
//    priority Last so foreign Prefixes run first. DamageEffect.Invoker itself is not patched: a mod that calls it and uses
//    the result always gets a popup. Budget: N per frame and M per second, over budget the popup is not created.
// 2) Blood: ... -> PassageObject.AddBloodMapObject(BloodMapObjectModel model) creates the splash under
//    SefiraMapLayer.instance.bloodNode (always, no early exit; the new object is the last child of bloodNode).
//    - Prefix: when the passage already has >= MaxBloodPerPassage tracked splashes, the oldest one is removed.
//    - Postfix: finds the created object (last children of bloodNode whose model is the argument) and tracks it.
//    - EffectLayer.Update Postfix: every SweepInterval seconds removes splashes older than BloodLifetimeSeconds.
//    bloodNode is destroyed by the game at the end of the day; the tracker only drops its references then.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Harmony;
using UnityEngine;
using N = NailOptimization;

namespace DamageEffectThrottle
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class DamageModule : N.ModuleBase
	{
		public const string Id = "DamageEffectThrottle";
		public const string HarmonyId = "Lobotomy.NailOptimization.DamageEffectThrottle";

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
				return "1.1";
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
			DamageState.Bind();
			N.ResetRegistry.Register(new DamageResetter());
			int popups = Install();
			if (popups == 0)
			{
				throw new InvalidOperationException("UnitModel.MakeDamageEffect was not patched");
			}
		}

		public override void OnConfigChanged(string key)
		{
			if (string.Equals(key, DamageState.KeyEnabled, StringComparison.Ordinal))
			{
				DamageState.ResetAll();
			}
			else if (string.Equals(key, DamageState.KeySweep, StringComparison.Ordinal))
			{
				BloodTracker.ForceSweepSoon();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int Install()
		{
			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			HarmonyMethod popup = N.PatchUtil.WithPriority(typeof(PopupPatch).GetMethod("Prefix", pub), Priority.Last);
			HarmonyMethod bloodPre = new HarmonyMethod(typeof(BloodPatch).GetMethod("Prefix", pub));
			HarmonyMethod bloodPost = new HarmonyMethod(typeof(BloodPatch).GetMethod("Postfix", pub));
			HarmonyMethod sweep = new HarmonyMethod(typeof(SweepPatch).GetMethod("Postfix", pub));

			int n1 = PatchMakeDamageEffect(harmony, popup);

			// Exactly ONE view type is patched, otherwise blood is counted twice. PassageObject is the last link of the
			// chain (called only when an object is really created); SefiraMapLayer is the fallback.
			string[] viewTypes = new string[] { "PassageObject", "SefiraMapLayer" };
			int n2 = 0;
			for (int i = 0; i < viewTypes.Length && n2 == 0; i++)
			{
				n2 = PatchBloodAdd(harmony, N.TypeCache.Resolve(viewTypes[i]), bloodPre, bloodPost);
			}
			if (n2 == 0)
			{
				N.ULog.Error(Id, "AddBloodMapObject(BloodMapObjectModel model) not patched: blood limit is inactive");
			}

			int n3 = PatchPostfixNoArgs(harmony, N.TypeCache.Resolve("EffectLayer"), "Update", sweep);
			if (n3 == 0)
			{
				N.ULog.Error(Id, "EffectLayer.Update not patched: blood lifetime sweep is inactive");
			}

			N.ULog.Info(Id, "patched MakeDamageEffect " + n1 + ", AddBloodMapObject " + n2 + ", EffectLayer.Update " + n3);
			return n1;
		}

		// UnitModel.MakeDamageEffect(RwbpType, float, DefenseInfo.Type): the only vanilla game-path caller of DamageEffect.Invoker.
		private static int PatchMakeDamageEffect(HarmonyInstance harmony, HarmonyMethod prefix)
		{
			MethodBase target = N.PatchUtil.Resolve(Id, typeof(UnitModel), "MakeDamageEffect", new Type[] { typeof(RwbpType), typeof(float), typeof(DefenseInfo.Type) });
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			return N.PatchUtil.Install(harmony, Id, target, prefix, null) ? 1 : 0;
		}

		private static int PatchBloodAdd(HarmonyInstance harmony, Type t, HarmonyMethod prefix, HarmonyMethod postfix)
		{
			if (object.ReferenceEquals(t, null))
			{
				return 0;
			}
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			MethodInfo[] methods = t.GetMethods(flags);
			int count = 0;
			for (int i = 0; i < methods.Length; i++)
			{
				if (methods[i].Name != "AddBloodMapObject")
				{
					continue;
				}
				ParameterInfo[] ps = methods[i].GetParameters();
				if (ps.Length != 1)
				{
					continue;
				}
				if (ps[0].ParameterType.Name != "BloodMapObjectModel")
				{
					continue;
				}
				if (ps[0].Name != "model")
				{
					continue;
				}
				if (N.PatchUtil.Install(harmony, Id, methods[i], prefix, postfix))
				{
					count++;
				}
			}
			return count;
		}

		private static int PatchPostfixNoArgs(HarmonyInstance harmony, Type t, string name, HarmonyMethod postfix)
		{
			if (object.ReferenceEquals(t, null))
			{
				N.ULog.Error(Id, "type not found for " + name);
				return 0;
			}
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			MethodInfo[] methods = t.GetMethods(flags);
			int count = 0;
			for (int i = 0; i < methods.Length; i++)
			{
				if (methods[i].Name != name)
				{
					continue;
				}
				if (methods[i].GetParameters().Length != 0)
				{
					continue;
				}
				if (N.PatchUtil.Install(harmony, Id, methods[i], null, postfix))
				{
					count++;
				}
			}
			return count;
		}
	}

	// Registered with Core: day-end reset of per-session state.
	public sealed class DamageResetter : N.IResettable
	{
		public void ResetState()
		{
			DamageState.ResetAll();
		}
	}

	// Config handles and counters shared by all patches of the module.
	public static class DamageState
	{
		public const string KeyEnabled = "DamageEffectThrottle.Enabled";
		public const string KeyPopupFrame = "DamageEffectThrottle.MaxPopupsPerFrame";
		public const string KeyPopupSecond = "DamageEffectThrottle.MaxPopupsPerSecond";
		public const string KeyBloodMax = "DamageEffectThrottle.MaxBloodPerPassage";
		public const string KeyBloodLife = "DamageEffectThrottle.BloodLifetimeSeconds";
		public const string KeySweep = "DamageEffectThrottle.SweepInterval";
		public const string KeyDestroy = "DamageEffectThrottle.DestroyInsteadOfHide";

		public static N.Flag Enabled;
		public static N.IntParam PopupsPerFrame;
		public static N.IntParam PopupsPerSecond;
		public static N.IntParam BloodMax;
		public static N.FloatParam BloodLife;
		public static N.FloatParam SweepInterval;
		public static N.Flag DestroyInstead;

		public static N.Counter CPopupAllowed;
		public static N.Counter CPopupSkipped;
		public static N.Counter CBloodCreated;
		public static N.Counter CBloodReplaced;
		public static N.Counter CBloodExpired;

		public static void Bind()
		{
			Enabled = N.Config.BindFlag(KeyEnabled, true, "Master switch. Off: every patch of this module passes through to vanilla.");
			PopupsPerFrame = N.Config.BindInt(KeyPopupFrame, 6, 1, 50, 1, "Damage popups allowed per frame.");
			PopupsPerSecond = N.Config.BindInt(KeyPopupSecond, 60, 1, 500, 5, "Damage popups allowed per real second.");
			BloodMax = N.Config.BindInt(KeyBloodMax, 10, 0, 100, 1, "Living blood splashes per passage; the oldest is removed when exceeded. 0 = no limit.");
			BloodLife = N.Config.BindFloat(KeyBloodLife, 60f, 0f, 600f, 5f, "Seconds of game time before a blood splash is removed. 0 = never.");
			SweepInterval = N.Config.BindFloat(KeySweep, 5f, 0.5f, 60f, 0.5f, "Seconds between blood lifetime sweeps.");
			DestroyInstead = N.Config.BindFlag(KeyDestroy, true, "Destroy removed splashes (true) or only hide them (false; use if MissingReferenceException appears).");

			CPopupAllowed = N.Counters.Get("DamageEffectThrottle", "popupsAllowed");
			CPopupSkipped = N.Counters.Get("DamageEffectThrottle", "popupsSkipped");
			CBloodCreated = N.Counters.Get("DamageEffectThrottle", "bloodCreated");
			CBloodReplaced = N.Counters.Get("DamageEffectThrottle", "bloodReplaced");
			CBloodExpired = N.Counters.Get("DamageEffectThrottle", "bloodExpired");
		}

		public static void ResetAll()
		{
			BloodTracker.Reset();
			PopupPatch.Reset();
		}
	}

	// ---------- Damage popups ----------

	public sealed class DamageBudget
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

	public static class PopupPatch
	{
		private static DamageBudget budget = new DamageBudget();

		public static void Reset()
		{
			budget.Reset();
		}

		// No parameters on purpose: nothing from the original is needed. MakeDamageEffect returns void, so skipping it
		// returns nothing to its caller.
		public static bool Prefix()
		{
			try
			{
				if (!DamageState.Enabled.On)
				{
					return true;
				}
				if (budget.TryTake(DamageState.PopupsPerFrame.Value, DamageState.PopupsPerSecond.Value))
				{
					DamageState.CPopupAllowed.Inc();
					return true;
				}
				DamageState.CPopupSkipped.Inc();
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}
	}

	// ---------- Blood ----------

	public class BloodEntry
	{
		public GameObject Go;
		public float Born;
	}

	public static class BloodTracker
	{
		private static readonly object NoPassage = new object();
		private static Dictionary<object, List<BloodEntry>> perPassage = new Dictionary<object, List<BloodEntry>>();
		private static float nextSweep = 0f;

		public static object KeyOf(BloodMapObjectModel model)
		{
			if (model == null || model.passage == null)
			{
				return NoPassage;
			}
			return model.passage;
		}

		public static List<BloodEntry> GetList(object key, bool create)
		{
			List<BloodEntry> list;
			if (perPassage.TryGetValue(key, out list))
			{
				return list;
			}
			if (!create)
			{
				return null;
			}
			list = new List<BloodEntry>();
			perPassage[key] = list;
			return list;
		}

		public static void Remove(BloodEntry e)
		{
			if (e.Go == null)
			{
				return;
			}
			if (DamageState.DestroyInstead.On)
			{
				UnityEngine.Object.Destroy(e.Go);
			}
			else
			{
				e.Go.SetActive(false);
			}
		}

		// Drops all references; the game itself destroys bloodNode at the end of the day. Nothing is destroyed here.
		public static void Reset()
		{
			perPassage.Clear();
			nextSweep = 0f;
		}

		public static void ForceSweepSoon()
		{
			nextSweep = 0f;
		}

		// Called from the EffectLayer.Update postfix; cheaply skipped by time.
		public static void Sweep()
		{
			float now = Time.time;
			if (now < nextSweep)
			{
				return;
			}
			nextSweep = now + DamageState.SweepInterval.Value;

			float life = DamageState.BloodLife.Value;
			List<object> empty = null;
			foreach (KeyValuePair<object, List<BloodEntry>> kv in perPassage)
			{
				List<BloodEntry> list = kv.Value;
				for (int i = list.Count - 1; i >= 0; i--)
				{
					BloodEntry e = list[i];
					if (e.Go == null)
					{
						list.RemoveAt(i);
					}
					else if (life > 0f && now - e.Born >= life)
					{
						Remove(e);
						list.RemoveAt(i);
						DamageState.CBloodExpired.Inc();
					}
				}
				if (list.Count == 0)
				{
					if (empty == null)
					{
						empty = new List<object>();
					}
					empty.Add(kv.Key);
				}
			}
			if (empty != null)
			{
				for (int i = 0; i < empty.Count; i++)
				{
					perPassage.Remove(empty[i]);
				}
			}
		}
	}

	public static class BloodPatch
	{
		// The parameter name must match the original ("model").
		public static bool Prefix(BloodMapObjectModel model)
		{
			try
			{
				if (!DamageState.Enabled.On)
				{
					return true;
				}
				int cap = DamageState.BloodMax.Value;
				if (cap <= 0)
				{
					return true;
				}

				List<BloodEntry> list = BloodTracker.GetList(BloodTracker.KeyOf(model), false);
				if (list == null)
				{
					return true;
				}

				for (int i = list.Count - 1; i >= 0; i--)
				{
					if (list[i].Go == null)
					{
						list.RemoveAt(i);
					}
				}
				// Make room for the new splash: the oldest (list head) is removed.
				while (list.Count >= cap)
				{
					BloodTracker.Remove(list[0]);
					list.RemoveAt(0);
					DamageState.CBloodReplaced.Inc();
				}
				return true;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static void Postfix(BloodMapObjectModel model)
		{
			try
			{
				if (!DamageState.Enabled.On)
				{
					return;
				}
				if (SefiraMapLayer.instance == null || SefiraMapLayer.instance.bloodNode == null)
				{
					return;
				}
				Transform node = SefiraMapLayer.instance.bloodNode.transform;
				int n = node.childCount;
				int stop = n - 6;
				if (stop < 0)
				{
					stop = 0;
				}
				for (int i = n - 1; i >= stop; i--)
				{
					Transform child = node.GetChild(i);
					BloodMapObject bo = child.GetComponent<BloodMapObject>();
					if (bo == null)
					{
						continue;
					}
					if (!object.ReferenceEquals(bo.model, model))
					{
						continue;
					}

					BloodEntry e = new BloodEntry();
					e.Go = child.gameObject;
					e.Born = Time.time;
					BloodTracker.GetList(BloodTracker.KeyOf(model), true).Add(e);
					DamageState.CBloodCreated.Inc();
					return;
				}
			}
			catch (Exception)
			{
			}
		}
	}

	public static class SweepPatch
	{
		public static void Postfix()
		{
			try
			{
				if (!DamageState.Enabled.On)
				{
					return;
				}
				BloodTracker.Sweep();
			}
			catch (Exception)
			{
			}
		}
	}
}
