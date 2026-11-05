// EffectPool: NailOptimization module (api level 3). Replaces the standalone mod Lobotomy.EffectPool (damage popups;
// v1.1 adds the DamageParticleEffect pool, OFF by default: EffectPool.PoolParticles;
// v1.2: a particle variant is pooled only if its SelfDestruct/AutoDestroyer scripts live >= the 1 s parent lifetime);
// v1.3: Invoker B/C with a negative index return the vanilla catch result in the Prefix (GL-53: the copy has no handler)).
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine, UnityEngine.UI. Output goes to <NailOptimization>/modules/.
// The standalone EffectPool.dll must be removed from BaseMods (two pools on one method otherwise).
//
// Game flow (vanilla source seen):
//   EffectInvoker.Invoker(src, mov|pos, lifeTime, isUnscaled): Prefab.LoadPrefab("Effect/Invoke/" + src) -> SetParent(currentLayer)
//     -> EffectLayer.Apply(effect, type, lifeTime) -> [mov path] _node = mov + SetMovableSetting / [pos path] position = pos.
//   EffectLayer.Delete(): Object.Destroy(effect.gameObject) for every entry of _deleted.
//   DamageEffect.Invoker(...) then calls SetData(...) and Dettach().
// What the module does:
//   Release: Prefix on EffectLayer.Delete. A DamageEffect in _deleted is hidden and kept in a stack under a hidden root instead of
//            being destroyed; it is removed from _deleted. Everything else is destroyed by the original.
//   Acquire: Prefix on both EffectInvoker.Invoker overloads, only for src == "DamageEffect" and only when the stack is not empty:
//            the same steps as the original (SetParent, Apply, _node/SetMovableSetting or position) without LoadPrefab/Instantiate.
//   Reset:   a snapshot of the UI fields is taken from the first fresh DamageEffect (SetData has not run yet) and re-applied on
//            every acquire, because SetData does not rewrite every field in every branch.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Harmony;
using UnityEngine;
using UnityEngine.UI;
using N = NailOptimization;

namespace EffectPool
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class PoolModule : N.ModuleBase
	{
		public const string Id = "EffectPool";
		public const string HarmonyId = "Lobotomy.NailOptimization.EffectPool";

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
				return "1.3";
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
			PoolStore.Bind();
			N.ResetRegistry.Register(new PoolResetter());
			Install();
		}

		public override void OnConfigChanged(string key)
		{
			if (string.Equals(key, PoolStore.KeyEnabled, StringComparison.Ordinal))
			{
				if (!PoolStore.Enabled.On)
				{
					int n = PoolStore.Trim(0);
					int np = ParticlePool.Trim(0);
					N.ULog.Info(Id, "disabled: pools drained, destroyed popups " + n + ", particles " + np);
				}
			}
			else if (string.Equals(key, PoolStore.KeyMax, StringComparison.Ordinal))
			{
				PoolStore.Trim(PoolStore.MaxPool.Value);
			}
			else if (string.Equals(key, PoolStore.KeyParticles, StringComparison.Ordinal))
			{
				if (!PoolStore.PoolParticles.On)
				{
					int np = ParticlePool.Trim(0);
					N.ULog.Info(Id, "particle pool off: drained, destroyed " + np);
				}
			}
			else if (string.Equals(key, PoolStore.KeyPartMax, StringComparison.Ordinal))
			{
				ParticlePool.Trim(PoolStore.MaxParticlePerKey.Value);
			}
			else if (string.Equals(key, PoolStore.KeyDump, StringComparison.Ordinal))
			{
				if (PoolStore.ParticleDump.On)
				{
					ParticlePool.TryDump();
				}
			}
		}

		// Resolves every target first; patches nothing unless all of them exist.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Install()
		{
			Type invoker = N.TypeCache.Resolve("EffectInvoker");
			Type layer = N.TypeCache.Resolve("EffectLayer");
			if (object.ReferenceEquals(invoker, null) || object.ReferenceEquals(layer, null))
			{
				throw new InvalidOperationException("EffectInvoker or EffectLayer not found");
			}

			MethodInfo mMov = null;
			MethodInfo mPos = null;
			MethodInfo[] ms = invoker.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
			for (int i = 0; i < ms.Length; i++)
			{
				if (ms[i].Name != "Invoker")
				{
					continue;
				}
				ParameterInfo[] ps = ms[i].GetParameters();
				if (ps.Length != 4 || ps[0].Name != "src" || ps[3].Name != "isUnscaled")
				{
					continue;
				}
				string t = ps[1].ParameterType.Name;
				if (t == "MovableObjectNode")
				{
					mMov = ms[i];
				}
				else if (t == "Vector3")
				{
					mPos = ms[i];
				}
			}

			MethodInfo mDelete = null;
			MethodInfo[] ls = layer.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
			for (int i = 0; i < ls.Length; i++)
			{
				if (ls[i].Name == "Delete" && ls[i].GetParameters().Length == 0)
				{
					mDelete = ls[i];
					break;
				}
			}

			PoolStore.DeletedField = layer.GetField("_deleted", BindingFlags.NonPublic | BindingFlags.Instance);
			PoolStore.NodeField = invoker.GetField("_node", BindingFlags.NonPublic | BindingFlags.Instance);

			if (object.ReferenceEquals(mMov, null) || object.ReferenceEquals(mPos, null) || object.ReferenceEquals(mDelete, null)
				|| object.ReferenceEquals(PoolStore.DeletedField, null) || object.ReferenceEquals(PoolStore.NodeField, null))
			{
				throw new InvalidOperationException("EffectPool targets missing: Invoker(mov) "
					+ (!object.ReferenceEquals(mMov, null)) + ", Invoker(pos) " + (!object.ReferenceEquals(mPos, null))
					+ ", EffectLayer.Delete " + (!object.ReferenceEquals(mDelete, null))
					+ ", _deleted " + (!object.ReferenceEquals(PoolStore.DeletedField, null))
					+ ", _node " + (!object.ReferenceEquals(PoolStore.NodeField, null)) + "; nothing patched");
			}

			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			HarmonyMethod preMov = new HarmonyMethod(typeof(PoolPatch).GetMethod("PrefixMov", pub));
			HarmonyMethod prePos = new HarmonyMethod(typeof(PoolPatch).GetMethod("PrefixPos", pub));
			HarmonyMethod postMov = new HarmonyMethod(typeof(PoolPatch).GetMethod("PostfixMov", pub));
			HarmonyMethod postPos = new HarmonyMethod(typeof(PoolPatch).GetMethod("PostfixPos", pub));
			HarmonyMethod del = new HarmonyMethod(typeof(PoolPatch).GetMethod("DeletePrefix", pub));

			bool a = N.PatchUtil.Install(harmony, Id, mMov, preMov, postMov);
			bool b = N.PatchUtil.Install(harmony, Id, mPos, prePos, postPos);
			bool c = N.PatchUtil.Install(harmony, Id, mDelete, del, null);
			N.ULog.Info(Id, "patched Invoker(mov) " + a + ", Invoker(pos) " + b + ", Delete " + c);
			if (!a || !b || !c)
			{
				throw new InvalidOperationException("EffectPool patch install incomplete (see log)");
			}
			InstallParticles(harmony);
		}

		// Three overloads that Instantiate(list[index]):
		//   A: (UnitModel target, RwbpType type, DefenseInfo defense)
		//   B: (UnitModel target, RwbpType type, float defense, UnitDirection dir)
		//   C: (UnitModel target, RwbpType type, int level)
		// The other overloads only call A. A failure here never breaks the popup pool.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InstallParticles(HarmonyInstance harmony)
		{
			try
			{
				Type t = N.TypeCache.Resolve("DamageParticleEffect");
				if (object.ReferenceEquals(t, null))
				{
					N.ULog.Error(Id, "type not found: DamageParticleEffect, particle pool unavailable");
					return;
				}
				BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
				HarmonyMethod preA = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PrefixA", pub));
				HarmonyMethod postA = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PostfixA", pub));
				HarmonyMethod preB = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PrefixB", pub));
				HarmonyMethod postB = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PostfixB", pub));
				HarmonyMethod preC = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PrefixC", pub));
				HarmonyMethod postC = new HarmonyMethod(typeof(ParticlePatch).GetMethod("PostfixC", pub));

				int count = 0;
				MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
				for (int i = 0; i < ms.Length; i++)
				{
					if (ms[i].Name != "Invoker")
					{
						continue;
					}
					ParameterInfo[] ps = ms[i].GetParameters();
					if (ps.Length < 3 || ps[0].Name != "target" || ps[1].Name != "type")
					{
						continue;
					}
					bool ok = false;
					if (ps.Length == 3 && ps[2].ParameterType.Name == "DefenseInfo" && ps[2].Name == "defense")
					{
						ok = N.PatchUtil.Install(harmony, Id, ms[i], preA, postA);
					}
					else if (ps.Length == 4 && ps[2].ParameterType.Name == "Single" && ps[2].Name == "defense" && ps[3].Name == "dir")
					{
						ok = N.PatchUtil.Install(harmony, Id, ms[i], preB, postB);
					}
					else if (ps.Length == 3 && ps[2].ParameterType.Name == "Int32" && ps[2].Name == "level")
					{
						ok = N.PatchUtil.Install(harmony, Id, ms[i], preC, postC);
					}
					if (ok)
					{
						count++;
					}
				}
				N.ULog.Info(Id, "patched DamageParticleEffect.Invoker overloads " + count + " (expect 3), PoolParticles " + PoolStore.PoolParticles.On + ", ParticleDump " + PoolStore.ParticleDump.On);
			}
			catch (Exception e)
			{
				N.ULog.Error(Id, "particle patch failed: " + e);
			}
		}
	}

	// UI state of a fresh DamageEffect before SetData.
	public class DamageSnapshot
	{
		public bool InnerActive, CountActive, TextEnabled;
		public Vector2 CountPos, IconPos, IconOutPos, TextSize, InnerSize;
		public Sprite TextSprite, InnerSprite;
		public Color TextColor, InnerColor;
		public string CountText, ContextText;
		public int AnimatorCount;

		public static DamageSnapshot Take(DamageEffect d)
		{
			DamageSnapshot s = new DamageSnapshot();
			if (d.DefenseTypeInner != null)
			{
				s.InnerActive = d.DefenseTypeInner.gameObject.activeSelf;
				s.InnerSize = d.DefenseTypeInner.rectTransform.sizeDelta;
				s.InnerSprite = d.DefenseTypeInner.sprite;
				s.InnerColor = d.DefenseTypeInner.color;
			}
			if (d.DefenseTypeText != null)
			{
				s.TextEnabled = d.DefenseTypeText.enabled;
				s.TextSize = d.DefenseTypeText.rectTransform.sizeDelta;
				s.TextSprite = d.DefenseTypeText.sprite;
				s.TextColor = d.DefenseTypeText.color;
			}
			if (d.DamageCount != null)
			{
				s.CountActive = d.DamageCount.gameObject.activeSelf;
				s.CountPos = d.DamageCount.rectTransform.anchoredPosition;
				s.CountText = d.DamageCount.text;
			}
			if (d.DamageContext != null)
			{
				s.ContextText = d.DamageContext.text;
			}
			if (d.Icon != null)
			{
				s.IconPos = d.Icon.rectTransform.anchoredPosition;
			}
			if (d.IconOut != null)
			{
				s.IconOutPos = d.IconOut.rectTransform.anchoredPosition;
			}
			s.AnimatorCount = d.GetComponentsInChildren<Animator>(true).Length;
			return s;
		}

		public void Apply(DamageEffect d)
		{
			if (d.DefenseTypeInner != null)
			{
				d.DefenseTypeInner.gameObject.SetActive(InnerActive);
				d.DefenseTypeInner.rectTransform.sizeDelta = InnerSize;
				d.DefenseTypeInner.sprite = InnerSprite;
				d.DefenseTypeInner.color = InnerColor;
			}
			if (d.DefenseTypeText != null)
			{
				d.DefenseTypeText.enabled = TextEnabled;
				d.DefenseTypeText.rectTransform.sizeDelta = TextSize;
				d.DefenseTypeText.sprite = TextSprite;
				d.DefenseTypeText.color = TextColor;
			}
			if (d.DamageCount != null)
			{
				d.DamageCount.gameObject.SetActive(CountActive);
				d.DamageCount.rectTransform.anchoredPosition = CountPos;
				d.DamageCount.text = CountText;
			}
			if (d.DamageContext != null)
			{
				d.DamageContext.text = ContextText;
			}
			if (d.Icon != null)
			{
				d.Icon.rectTransform.anchoredPosition = IconPos;
			}
			if (d.IconOut != null)
			{
				d.IconOut.rectTransform.anchoredPosition = IconOutPos;
			}
		}
	}

	public static class PoolStore
	{
		public const string KeyEnabled = "EffectPool.Enabled";
		public const string KeyMax = "EffectPool.MaxPool";
		public const string KeyRebind = "EffectPool.RebindAnimators";
		public const string KeyParticles = "EffectPool.PoolParticles";
		public const string KeyPartMax = "EffectPool.MaxParticlePerKey";
		public const string KeyDump = "EffectPool.ParticleDump";

		public static N.Flag Enabled;
		public static N.IntParam MaxPool;
		public static N.Flag Rebind;
		public static N.Flag PoolParticles;
		public static N.IntParam MaxParticlePerKey;
		public static N.Flag ParticleDump;

		public static N.Counter CHits;
		public static N.Counter CMisses;
		public static N.Counter CReleased;
		public static N.Counter COverflow;
		public static N.Counter CRejected;
		public static N.Counter CPartHits;
		public static N.Counter CPartMisses;
		public static N.Counter CPartReleased;
		public static N.Counter CPartOverflow;
		public static N.Counter CPartDiscarded;
		public static N.Counter CPartRejected;

		public static FieldInfo DeletedField;
		public static FieldInfo NodeField;
		public static DamageSnapshot Snapshot;

		// localScale of a fresh popup created through the Vector3 path (that path never sets the scale; the mov path does).
		public static Vector3 PosScale;
		public static bool PosScaleKnown;

		// Set by an acquire prefix, consumed by the following postfix (postfixes also run after a skipped original).
		public static bool FromPool;

		private static Stack<DamageEffect> stack = new Stack<DamageEffect>();
		private static GameObject root;

		public static void Bind()
		{
			Enabled = N.Config.BindFlag(KeyEnabled, true, "Master switch. Off: pool is drained and every patch passes through to vanilla.");
			MaxPool = N.Config.BindInt(KeyMax, 150, 0, 1000, 10, "Maximum pooled damage popups; extra ones are destroyed as in vanilla. 0 = no pooling.");
			Rebind = N.Config.BindFlag(KeyRebind, true, "Rebind Animators of a reused popup (only if the prefab has any).");
			PoolParticles = N.Config.BindFlag(KeyParticles, false, "Pool hit particles (DamageParticleEffect parent + child). Test feature: enable and check the hit effects by eye.");
			MaxParticlePerKey = N.Config.BindInt(KeyPartMax, 12, 0, 100, 1, "Pooled hit particles per variant (damage type x defense level, up to 20 variants). 0 = no pooling.");
			ParticleDump = N.Config.BindFlag(KeyDump, false, "Write the components of the hit particle prefabs to the log once per session (turn off after reading the log).");

			CHits = N.Counters.Get("EffectPool", "hits");
			CMisses = N.Counters.Get("EffectPool", "misses");
			CReleased = N.Counters.Get("EffectPool", "released");
			COverflow = N.Counters.Get("EffectPool", "overflow");
			CRejected = N.Counters.Get("EffectPool", "rejected");
			CPartHits = N.Counters.Get("EffectPool", "particleHits");
			CPartMisses = N.Counters.Get("EffectPool", "particleMisses");
			CPartReleased = N.Counters.Get("EffectPool", "particleReleased");
			CPartOverflow = N.Counters.Get("EffectPool", "particleOverflow");
			CPartDiscarded = N.Counters.Get("EffectPool", "particleDiscarded");
			CPartRejected = N.Counters.Get("EffectPool", "particleRejected");
		}

		public static DamageEffect Acquire()
		{
			while (stack.Count > 0)
			{
				DamageEffect d = stack.Pop();
				if (d != null)
				{
					return d;
				}
			}
			return null;
		}

		public static bool Release(DamageEffect d)
		{
			if (d == null)
			{
				return false;
			}
			// Not active: already pooled or hidden by someone else. Never push the same object twice.
			if (!d.gameObject.activeSelf || !object.ReferenceEquals(d.destroyEvent, null))
			{
				CRejected.Inc();
				return false;
			}
			if (stack.Count >= MaxPool.Value)
			{
				COverflow.Inc();
				return false;
			}
			if (root == null)
			{
				root = new GameObject("DamageEffectPool");
				root.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(root);
			}
			d.gameObject.SetActive(false);
			d.transform.SetParent(root.transform, false);
			NodeField.SetValue(d, null);
			d.Dettach();
			stack.Push(d);
			CReleased.Inc();
			return true;
		}

		// Brings a pooled object to the "fresh from prefab" state. Call after SetParent and SetActive(true).
		public static void Reset(DamageEffect d)
		{
			Snapshot.Apply(d);
			if (Rebind.On && Snapshot.AnimatorCount > 0)
			{
				Animator[] anims = d.GetComponentsInChildren<Animator>(true);
				for (int i = 0; i < anims.Length; i++)
				{
					anims[i].Rebind();
				}
			}
		}

		// Destroys pooled objects until at most max remain; returns how many were destroyed.
		public static int Trim(int max)
		{
			int n = 0;
			while (stack.Count > max)
			{
				DamageEffect d = stack.Pop();
				if (d != null)
				{
					UnityEngine.Object.Destroy(d.gameObject);
					n++;
				}
			}
			return n;
		}
	}

	public static class PoolPatch
	{
		// Parameter names must match the original: src, mov / pos, lifeTime, isUnscaled.
		public static bool PrefixMov(string src, MovableObjectNode mov, float lifeTime, bool isUnscaled, ref EffectInvoker __result)
		{
			try
			{
				if (!PoolStore.Enabled.On || src != "DamageEffect" || mov == null)
				{
					return true;
				}
				if (PoolStore.Snapshot == null)
				{
					return true;
				}
				EffectLayer layer = EffectLayer.currentLayer;
				if (layer == null)
				{
					return true;
				}

				DamageEffect d = PoolStore.Acquire();
				if (d == null)
				{
					PoolStore.CMisses.Inc();
					return true;
				}

				d.transform.SetParent(layer.transform);
				d.gameObject.SetActive(true);
				PoolStore.Reset(d);
				layer.Apply(d, isUnscaled ? EffectLifetimeType.UNSCALED : EffectLifetimeType.NORMAL, lifeTime);
				PoolStore.NodeField.SetValue(d, mov);
				d.SetMovableSetting(mov);
				PoolStore.CHits.Inc();
				PoolStore.FromPool = true;
				__result = d;
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static bool PrefixPos(string src, Vector3 pos, float lifeTime, bool isUnscaled, ref EffectInvoker __result)
		{
			try
			{
				if (!PoolStore.Enabled.On || src != "DamageEffect")
				{
					return true;
				}
				// The pos path never sets the scale: until a fresh pos-path popup showed the prefab scale, use the original.
				if (PoolStore.Snapshot == null || !PoolStore.PosScaleKnown)
				{
					return true;
				}
				EffectLayer layer = EffectLayer.currentLayer;
				if (layer == null)
				{
					return true;
				}

				DamageEffect d = PoolStore.Acquire();
				if (d == null)
				{
					PoolStore.CMisses.Inc();
					return true;
				}

				d.transform.SetParent(layer.transform);
				d.gameObject.SetActive(true);
				PoolStore.Reset(d);
				layer.Apply(d, isUnscaled ? EffectLifetimeType.UNSCALED : EffectLifetimeType.NORMAL, lifeTime);
				d.transform.localScale = PoolStore.PosScale;
				d.transform.position = pos;
				PoolStore.CHits.Inc();
				PoolStore.FromPool = true;
				__result = d;
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static void PostfixMov(string src, EffectInvoker __result)
		{
			Learn(src, __result, false);
		}

		public static void PostfixPos(string src, EffectInvoker __result)
		{
			Learn(src, __result, true);
		}

		// Runs after every EffectInvoker.Invoker call of the game: exits early. Learns the snapshot from the first fresh
		// popup (SetData has not run yet) and the pos-path scale from the first fresh pos-path popup.
		private static void Learn(string src, EffectInvoker result, bool isPos)
		{
			try
			{
				if (PoolStore.FromPool)
				{
					PoolStore.FromPool = false;
					return;
				}
				if (PoolStore.Snapshot != null && (!isPos || PoolStore.PosScaleKnown))
				{
					return;
				}
				if (!PoolStore.Enabled.On || src != "DamageEffect")
				{
					return;
				}
				DamageEffect d = result as DamageEffect;
				if (d == null)
				{
					return;
				}
				if (PoolStore.Snapshot == null)
				{
					PoolStore.Snapshot = DamageSnapshot.Take(d);
					N.ULog.Info(PoolModule.Id, "damage effect snapshot taken, animators " + PoolStore.Snapshot.AnimatorCount);
				}
				if (isPos && !PoolStore.PosScaleKnown)
				{
					PoolStore.PosScale = d.transform.localScale;
					PoolStore.PosScaleKnown = true;
					N.ULog.Info(PoolModule.Id, "pos-path scale learned " + PoolStore.PosScale.x + "," + PoolStore.PosScale.y + "," + PoolStore.PosScale.z);
				}
			}
			catch (Exception)
			{
			}
		}

		public static bool DeletePrefix(EffectLayer __instance)
		{
			try
			{
				if (!PoolStore.Enabled.On)
				{
					return true;
				}
				ParticlePool.MaybePrune();
				List<EffectLayer.Management> list = PoolStore.DeletedField.GetValue(__instance) as List<EffectLayer.Management>;
				if (list == null || list.Count == 0)
				{
					return true;
				}
				for (int i = list.Count - 1; i >= 0; i--)
				{
					EffectLayer.Management m = list[i];
					if (m == null || m.effect == null)
					{
						continue;
					}
					DamageEffect d = m.effect as DamageEffect;
					if (d != null)
					{
						if (PoolStore.Snapshot != null && PoolStore.Release(d))
						{
							list.RemoveAt(i);
						}
						continue;
					}
					DamageParticleEffect pe = m.effect as DamageParticleEffect;
					if (pe != null && PoolStore.PoolParticles.On && ParticlePool.Release(pe))
					{
						list.RemoveAt(i);
					}
				}
			}
			catch (Exception)
			{
			}
			return true;
		}
	}

	// Registered with Core: day-end maintenance of the particle pool (drops keys of destroyed objects).
	public sealed class PoolResetter : N.IResettable
	{
		public void ResetState()
		{
			ParticlePool.FromPool = false;
			ParticlePool.Prune();
		}
	}

	// ---------- Hit particles (DamageParticleEffect) ----------
	// Per hit the game does two Instantiate: the parent through EffectInvoker.Invoker("DamageParticle/DamageParticleParent",
	// mov, 1f, false) and the child ParticleSlot = Instantiate(list[index]) under it. The parent lives 1 s (EffectLayer) and is
	// destroyed with the child. The pool keeps "parent + created child" pairs per key = instanceID of the child prefab
	// (list[index]) and skips both Instantiate calls. Tracked objects are only the ones registered by the postfixes.
	// Components of a tracked pair, taken once at registration (no GetComponentsInChildren per hit).
	public sealed class PairInfo
	{
		public int Key;
		public ParticleSystem[] Systems;
		public Animator[] Anims;
		public RandomRotater[] Rotaters;
	}

	public static class ParticlePool
	{
		public static List<GameObject> TplR, TplW, TplB, TplP;

		// Set by a reuse prefix, consumed by the following postfix (postfixes also run after a skipped original).
		public static bool FromPool;

		private static Dictionary<int, Stack<DamageParticleEffect>> stacks = new Dictionary<int, Stack<DamageParticleEffect>>();
		private static Dictionary<DamageParticleEffect, PairInfo> keyOf = new Dictionary<DamageParticleEffect, PairInfo>();

		// key (instanceID of the child prefab) -> variant allowed in the pool. Decided once per variant.
		private static Dictionary<int, bool> admit = new Dictionary<int, bool>();

		// LoadDefaultPrefab gives the parent lifeTime 1 s; the child is destroyed with it by the game.
		private const float ParentLife = 1f;

		// AutoDestroyer.destroyEvent is private; a pair whose event is already set is not pooled.
		private static FieldInfo adEvent = typeof(AutoDestroyer).GetField("destroyEvent", BindingFlags.NonPublic | BindingFlags.Instance);
		private static GameObject root;
		private static float nextPrune = 0f;
		private static bool dumped = false;

		private static int CountOf(List<GameObject> l)
		{
			return l == null ? -1 : l.Count;
		}

		// Prefab lists are references to assets, the same for every clone of the parent: taken from the first one seen.
		public static void CaptureLists(DamageParticleEffect p)
		{
			if (TplR != null || p == null || p.RTypeParticle == null)
			{
				return;
			}
			TplR = p.RTypeParticle;
			TplW = p.WTypeParticle;
			TplB = p.BTypeParticle;
			TplP = p.PTypeParticle;
			N.ULog.Info(PoolModule.Id, "particle prefab lists captured: R " + CountOf(TplR) + ", W " + CountOf(TplW) + ", B " + CountOf(TplB) + ", P " + CountOf(TplP));
			TryDump();
		}

		public static void TryDump()
		{
			if (dumped || TplR == null || !PoolStore.ParticleDump.On)
			{
				return;
			}
			dumped = true;
			ParticleDump.Run();
		}

		public static List<GameObject> ListFor(RwbpType type)
		{
			switch (type)
			{
			case RwbpType.R:
				return TplR;
			case RwbpType.W:
				return TplW;
			case RwbpType.B:
				return TplB;
			case RwbpType.P:
				return TplP;
			default:
				return null;
			}
		}

		public static GameObject PrefabAt(List<GameObject> list, int index)
		{
			if (list == null || index < 0 || index >= list.Count)
			{
				return null;
			}
			return list[index];
		}

		// Same mapping as DamageParticleEffect.Invoker(target, type, defense).
		public static bool IndexFromDefense(DefenseInfo.Type t, out int index)
		{
			index = 0;
			switch (t)
			{
			case DefenseInfo.Type.NONE:
				index = 2;
				return true;
			case DefenseInfo.Type.WEAKNESS:
				index = 3;
				return true;
			case DefenseInfo.Type.SUPER_WEAKNESS:
				index = 4;
				return true;
			case DefenseInfo.Type.ENDURE:
				index = 1;
				return true;
			case DefenseInfo.Type.RESISTANCE:
				index = 0;
				return true;
			default:
				return false;
			}
		}

		// A variant is poolable only when no script of its prefab can destroy the child before the parent's 1 s are over:
		// SelfDestruct.Start schedules Destroy(gameObject, selfdestruct_in), AutoDestroyer.FixedUpdate destroys after destroyTime.
		private static bool Admit(RwbpType type, int index, GameObject prefab)
		{
			float min = 1000f;
			SelfDestruct[] sds = prefab.GetComponentsInChildren<SelfDestruct>(true);
			for (int i = 0; i < sds.Length; i++)
			{
				if (sds[i].selfdestruct_in < min)
				{
					min = sds[i].selfdestruct_in;
				}
			}
			AutoDestroyer[] ads = prefab.GetComponentsInChildren<AutoDestroyer>(true);
			for (int i = 0; i < ads.Length; i++)
			{
				if (ads[i].destroyTime < min)
				{
					min = ads[i].destroyTime;
				}
			}
			bool ok = min >= ParentLife;
			N.ULog.Info(PoolModule.Id, "particle variant " + type + "[" + index + "] " + prefab.name + ": " + (ok ? "pooled" : "skipped")
				+ ", min script time " + (min >= 1000f ? "none" : min.ToString()));
			return ok;
		}

		// Called from the postfix right after the game created a fresh pair, before any Start/FixedUpdate of its scripts ran.
		public static void RegisterFrom(RwbpType type, int index, DamageParticleEffect p)
		{
			GameObject prefab = PrefabAt(ListFor(type), index);
			if (prefab == null || p == null || p.ParticleSlot == null)
			{
				return;
			}
			int key = prefab.GetInstanceID();
			bool ok;
			if (!admit.TryGetValue(key, out ok))
			{
				ok = Admit(type, index, prefab);
				admit[key] = ok;
			}
			if (!ok)
			{
				return;
			}

			GameObject slot = p.ParticleSlot;
			AutoDestroyer[] ads = slot.GetComponentsInChildren<AutoDestroyer>(true);
			if (!object.ReferenceEquals(adEvent, null))
			{
				for (int i = 0; i < ads.Length; i++)
				{
					if (!object.ReferenceEquals(adEvent.GetValue(ads[i]), null))
					{
						PoolStore.CPartRejected.Inc();
						return;
					}
				}
			}
			// Start has not run yet: a disabled SelfDestruct never schedules its Destroy, a disabled AutoDestroyer never counts.
			// The pair then lives exactly as long as its parent (1 s), as in vanilla, and can be reused.
			SelfDestruct[] sds = slot.GetComponentsInChildren<SelfDestruct>(true);
			for (int i = 0; i < sds.Length; i++)
			{
				sds[i].enabled = false;
			}
			for (int i = 0; i < ads.Length; i++)
			{
				ads[i].enabled = false;
			}

			PairInfo info = new PairInfo();
			info.Key = key;
			info.Systems = slot.GetComponentsInChildren<ParticleSystem>(true);
			info.Anims = slot.GetComponentsInChildren<Animator>(true);
			info.Rotaters = slot.GetComponentsInChildren<RandomRotater>(true);
			keyOf[p] = info;
		}

		public static DamageParticleEffect Acquire(int key)
		{
			Stack<DamageParticleEffect> st;
			if (!stacks.TryGetValue(key, out st))
			{
				return null;
			}
			while (st.Count > 0)
			{
				DamageParticleEffect e = st.Pop();
				if (e == null)
				{
					continue;
				}
				// The child was destroyed while pooled (self-destroying particle script?): drop the pair.
				if (e.ParticleSlot == null)
				{
					keyOf.Remove(e);
					UnityEngine.Object.Destroy(e.gameObject);
					PoolStore.CPartDiscarded.Inc();
					continue;
				}
				return e;
			}
			return null;
		}

		// True: the object was taken into the pool and must be removed from _deleted. Unknown objects return false untouched.
		public static bool Release(DamageParticleEffect p)
		{
			if (p == null)
			{
				return false;
			}
			PairInfo info;
			if (!keyOf.TryGetValue(p, out info))
			{
				return false;
			}
			int key = info.Key;
			if (p.ParticleSlot == null)
			{
				keyOf.Remove(p);
				return false;
			}
			if (!p.gameObject.activeSelf || !object.ReferenceEquals(p.destroyEvent, null))
			{
				PoolStore.CPartRejected.Inc();
				keyOf.Remove(p);
				return false;
			}
			Stack<DamageParticleEffect> st;
			if (!stacks.TryGetValue(key, out st))
			{
				st = new Stack<DamageParticleEffect>();
				stacks[key] = st;
			}
			if (st.Count >= PoolStore.MaxParticlePerKey.Value)
			{
				PoolStore.CPartOverflow.Inc();
				keyOf.Remove(p);
				return false;
			}
			if (root == null)
			{
				root = new GameObject("DamageParticlePool");
				root.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(root);
			}
			p.gameObject.SetActive(false);
			p.transform.SetParent(root.transform, false);
			PoolStore.NodeField.SetValue(p, null);
			p.Dettach();
			st.Push(p);
			PoolStore.CPartReleased.Inc();
			return true;
		}

		// Brings the pair to the "just created" state and puts it under the EffectLayer exactly as
		// EffectInvoker.Invoker does for lifeTime = 1, isUnscaled = false (DamageParticleEffect.LoadDefaultPrefab).
		public static void Prepare(DamageParticleEffect e, MovableObjectNode mov, EffectLayer layer)
		{
			e.transform.SetParent(layer.transform);
			e.gameObject.SetActive(true);
			e.transform.localRotation = Quaternion.identity;

			GameObject slot = e.ParticleSlot;
			slot.SetActive(true);
			Transform ct = slot.transform;
			ct.localPosition = new Vector3(0f, 1.5f, 0f);
			ct.localScale = Vector3.one;

			PairInfo info;
			if (keyOf.TryGetValue(e, out info))
			{
				// RandomRotater.Start runs once per object: repeat its random Z rotation for every reuse.
				for (int i = 0; i < info.Rotaters.Length; i++)
				{
					RandomRotater r = info.Rotaters[i];
					if (r != null)
					{
						r.transform.localRotation = Quaternion.Euler(new Vector3(0f, 0f, UnityEngine.Random.Range(r.min, r.max)));
					}
				}
				for (int i = 0; i < info.Systems.Length; i++)
				{
					info.Systems[i].Clear(false);
					info.Systems[i].Play(false);
				}
				if (PoolStore.Rebind.On)
				{
					for (int i = 0; i < info.Anims.Length; i++)
					{
						info.Anims[i].Rebind();
					}
				}
			}

			layer.Apply(e, EffectLifetimeType.NORMAL, 1f);
			PoolStore.NodeField.SetValue(e, mov);
			e.SetMovableSetting(mov);
		}

		// Destroys pooled pairs until at most max remain per key; returns how many were destroyed.
		public static int Trim(int max)
		{
			int n = 0;
			foreach (KeyValuePair<int, Stack<DamageParticleEffect>> kv in stacks)
			{
				Stack<DamageParticleEffect> st = kv.Value;
				while (st.Count > max)
				{
					DamageParticleEffect e = st.Pop();
					if (e != null)
					{
						keyOf.Remove(e);
						UnityEngine.Object.Destroy(e.gameObject);
						n++;
					}
				}
			}
			if (max <= 0)
			{
				stacks.Clear();
			}
			return n;
		}

		// Forgets destroyed objects (scene change destroys the EffectLayer without going through Release).
		public static void Prune()
		{
			List<DamageParticleEffect> dead = null;
			foreach (KeyValuePair<DamageParticleEffect, PairInfo> kv in keyOf)
			{
				if (kv.Key == null)
				{
					if (dead == null)
					{
						dead = new List<DamageParticleEffect>();
					}
					dead.Add(kv.Key);
				}
			}
			if (dead != null)
			{
				for (int i = 0; i < dead.Count; i++)
				{
					keyOf.Remove(dead[i]);
				}
			}
		}

		public static void MaybePrune()
		{
			float now = Time.unscaledTime;
			if (now < nextPrune)
			{
				return;
			}
			nextPrune = now + 60f;
			Prune();
		}
	}

	public static class ParticlePatch
	{
		private static bool Reuse(UnitModel target, RwbpType type, int index, bool applyDir, bool left, ref DamageParticleEffect result)
		{
			GameObject prefab = ParticlePool.PrefabAt(ParticlePool.ListFor(type), index);
			if (prefab == null)
			{
				return true;
			}
			EffectLayer layer = EffectLayer.currentLayer;
			if (layer == null || target == null)
			{
				return true;
			}
			MovableObjectNode mov = target.GetMovableNode();
			if (mov == null)
			{
				return true;
			}

			DamageParticleEffect e = ParticlePool.Acquire(prefab.GetInstanceID());
			if (e == null)
			{
				PoolStore.CPartMisses.Inc();
				return true;
			}

			ParticlePool.Prepare(e, mov, layer);
			// A and C leave the rotation alone; B sets 180 degrees around Y for LEFT.
			if (applyDir && left)
			{
				e.transform.localRotation = Quaternion.Euler(new Vector3(0f, 180f, 0f));
			}
			PoolStore.CPartHits.Inc();
			ParticlePool.FromPool = true;
			result = e;
			return false;
		}

		// Common postfix part: learn the prefab lists (also for the one-shot dump) and track the created pair.
		private static void Track(RwbpType type, int index, DamageParticleEffect result, bool indexValid)
		{
			if (ParticlePool.FromPool)
			{
				ParticlePool.FromPool = false;
				return;
			}
			if (!PoolStore.Enabled.On || result == null)
			{
				return;
			}
			if (ParticlePool.TplR == null && (PoolStore.PoolParticles.On || PoolStore.ParticleDump.On))
			{
				ParticlePool.CaptureLists(result);
			}
			if (!PoolStore.PoolParticles.On || !indexValid)
			{
				return;
			}
			ParticlePool.RegisterFrom(type, index, result);
		}

		// Parameter names must match the original: target, type, defense / level, dir.
		public static bool PrefixA(UnitModel target, RwbpType type, DefenseInfo defense, ref DamageParticleEffect __result)
		{
			try
			{
				if (!PoolStore.Enabled.On || !PoolStore.PoolParticles.On || defense == null)
				{
					return true;
				}
				int index;
				if (!ParticlePool.IndexFromDefense(defense.GetDefenseType(type), out index))
				{
					return true;
				}
				return Reuse(target, type, index, false, false, ref __result);
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static void PostfixA(UnitModel target, RwbpType type, DefenseInfo defense, DamageParticleEffect __result)
		{
			try
			{
				int index = 0;
				bool valid = defense != null && ParticlePool.IndexFromDefense(defense.GetDefenseType(type), out index);
				Track(type, index, __result, valid);
			}
			catch (Exception)
			{
			}
		}

		public static bool PrefixB(UnitModel target, RwbpType type, float defense, UnitDirection dir, ref DamageParticleEffect __result)
		{
			try
			{
				if ((int)defense < 0)
				{
					// Vanilla catch branch (GL-53: the patched copy has no handler).
					Debug.LogError(new IndexOutOfRangeException());
					__result = null;
					return false;
				}
				if (!PoolStore.Enabled.On || !PoolStore.PoolParticles.On)
				{
					return true;
				}
				return Reuse(target, type, (int)defense, true, dir == UnitDirection.LEFT, ref __result);
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static void PostfixB(UnitModel target, RwbpType type, float defense, UnitDirection dir, DamageParticleEffect __result)
		{
			try
			{
				Track(type, (int)defense, __result, true);
			}
			catch (Exception)
			{
			}
		}

		public static bool PrefixC(UnitModel target, RwbpType type, int level, ref DamageParticleEffect __result)
		{
			try
			{
				if (level < 0)
				{
					// Vanilla catch branch (GL-53: the patched copy has no handler).
					Debug.LogError(new IndexOutOfRangeException());
					__result = null;
					return false;
				}
				if (!PoolStore.Enabled.On || !PoolStore.PoolParticles.On)
				{
					return true;
				}
				return Reuse(target, type, level, false, false, ref __result);
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static void PostfixC(UnitModel target, RwbpType type, int level, DamageParticleEffect __result)
		{
			try
			{
				Track(type, level, __result, true);
			}
			catch (Exception)
			{
			}
		}
	}

	// One-shot log of the hit particle prefabs: components, ParticleSystem main settings (duration, loop, stopAction) and the
	// fields/methods of every script type. Needed to see what could destroy or hide a pooled particle on its own.
	public static class ParticleDump
	{
		private const int MaxLines = 600;
		private static int lines = 0;
		private static Dictionary<string, bool> seen = new Dictionary<string, bool>();

		private static void Out(string s)
		{
			if (lines >= MaxLines)
			{
				return;
			}
			lines++;
			N.ULog.Info("EffectPool.dump", s);
			if (lines == MaxLines)
			{
				N.ULog.Info("EffectPool.dump", "line limit reached");
			}
		}

		private static object Raw(object o, string name)
		{
			try
			{
				PropertyInfo pi = o.GetType().GetProperty(name);
				if (object.ReferenceEquals(pi, null))
				{
					return null;
				}
				return pi.GetValue(o, null);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string Read(object o, string name)
		{
			try
			{
				object v = Raw(o, name);
				return object.ReferenceEquals(v, null) ? "null" : v.ToString();
			}
			catch (Exception)
			{
				return "?";
			}
		}

		private static string PathOf(Transform tr)
		{
			string p = tr.name;
			Transform x = tr.parent;
			while (x != null)
			{
				p = x.name + "/" + p;
				x = x.parent;
			}
			return p;
		}

		public static void Run()
		{
			try
			{
				Out("hit particle prefabs (R/W/B/P lists, index = variant)");
				DumpList("R", ParticlePool.TplR);
				DumpList("W", ParticlePool.TplW);
				DumpList("B", ParticlePool.TplB);
				DumpList("P", ParticlePool.TplP);
				Out("dump end, lines " + lines);
			}
			catch (Exception e)
			{
				Out("dump failed: " + e.Message);
			}
		}

		private static void DumpList(string label, List<GameObject> list)
		{
			if (list == null)
			{
				Out(label + ": list is null");
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				GameObject go = list[i];
				if (go == null)
				{
					Out(label + "[" + i + "]: null");
					continue;
				}
				DumpPrefab(label + "[" + i + "]", go);
			}
		}

		private static void DumpPrefab(string label, GameObject prefab)
		{
			Component[] comps = prefab.GetComponentsInChildren<Component>(true);
			Out(label + " " + prefab.name + ": components " + comps.Length);
			BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
			for (int i = 0; i < comps.Length; i++)
			{
				Component c = comps[i];
				if (c == null)
				{
					continue;
				}
				Type t = c.GetType();
				Out("  " + PathOf(c.transform) + " : " + t.FullName);

				ParticleSystem ps = c as ParticleSystem;
				if (ps != null)
				{
					object main = Raw(ps, "main");
					if (!object.ReferenceEquals(main, null))
					{
						Out("    main: duration " + Read(main, "duration") + ", loop " + Read(main, "loop")
							+ ", playOnAwake " + Read(main, "playOnAwake") + ", stopAction " + Read(main, "stopAction"));
					}
					else
					{
						Out("    particle: duration " + Read(ps, "duration") + ", loop " + Read(ps, "loop") + ", playOnAwake " + Read(ps, "playOnAwake"));
					}
				}

				MonoBehaviour mb = c as MonoBehaviour;
				if (mb != null && !seen.ContainsKey(t.FullName))
				{
					seen[t.FullName] = true;
					FieldInfo[] fs = t.GetFields(f);
					for (int j = 0; j < fs.Length; j++)
					{
						Out("    field " + fs[j].FieldType.Name + " " + fs[j].Name);
					}
					MethodInfo[] ms = t.GetMethods(f);
					for (int j = 0; j < ms.Length; j++)
					{
						Out("    method " + ms[j].Name);
					}
				}
			}
		}
	}
}
