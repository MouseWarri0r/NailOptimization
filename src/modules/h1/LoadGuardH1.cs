// LoadGuardH1: Harmony 1.0.9.1 internals used by LoadGuard (HarmonyCache, HarmonyDefer). Not a module: it lives in
// modules/h1/ (the Core loader reads only the top level of modules/), and LoadGuard loads it by Assembly.LoadFile and
// calls Boot.Start by reflection only when the game runs Harmony 1 itself (assembly 0Harmony). Under HXLoader these
// types are missing from its Harmony 1 facade, so a static reference would push LoadGuard.dll onto the legacy engine.
// Build: reference NailOptimization.dll, 0Harmony, UnityEngine. Output: <NailOptimization>/modules/h1/LoadGuardH1.dll.

using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using UnityEngine;
using N = NailOptimization;

namespace LoadGuardH1
{
	// Entry point for LoadGuard (reflection; no Harmony type in the signature). Same order and ids as LoadGuard 1.13.
	public static class Boot
	{
		internal const string Tag = "loadguard";

		public static void Start(bool cache, bool defer, string harmonyId, Assembly guard)
		{
			if (cache)
			{
				HarmonyCache.Start(HarmonyInstance.Create(harmonyId + ".harmonycache"));
			}
			if (defer)
			{
				HarmonyDefer.GuardAssembly = guard;
				HarmonyDefer.Start(HarmonyInstance.Create(harmonyId + ".harmonydefer"));
			}
		}
	}

	// LoadGuard.HarmonyCache (task 097, boot time): Harmony 1.0.9.1 keeps every PatchInfo as BinaryFormatter bytes in a shared
	// Dictionary<MethodBase, byte[]> and reaches it through GetState (AppDomain.GetAssemblies + GetName per call); each Patch call
	// deserializes, adds, serializes. Both HarmonySharedState.GetPatchInfo and UpdatePatchInfo (no exception handlers) are replaced:
	// the dictionary is taken once, Update keeps (stored byte[], copy of the PatchInfo), Get returns a fresh copy while the state
	// still holds that same byte[] and deserializes otherwise (another Harmony copy wrote it). Serialize stays (other copies read
	// the bytes). Copies: new PatchInfo with cloned arrays; Patch objects are readonly and shared.
	public static class HarmonyCache
	{
		private delegate byte[] SerializeFn(PatchInfo info);
		private delegate PatchInfo DeserializeFn(byte[] bytes);

		private sealed class Entry
		{
			public byte[] Bytes;
			public PatchInfo Info;
		}

		private static Dictionary<MethodBase, byte[]> _state;
		private static SerializeFn _serialize;
		private static DeserializeFn _deserialize;
		private static readonly Dictionary<MethodBase, Entry> _cache = new Dictionary<MethodBase, Entry>();
		private static readonly object _gate = new object();
		internal static bool Active;
		internal static int Hits;
		internal static int Misses;
		internal static int Stores;

		internal static void Start(HarmonyInstance harmony)
		{
			try
			{
				StartCore(harmony);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Boot.Tag, "harmony cache off: " + e);
			}
		}

		private static void StartCore(HarmonyInstance harmony)
		{
			const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
			Type shared = typeof(HarmonySharedState);
			Type ser = typeof(HarmonyInstance).Assembly.GetType("Harmony.PatchInfoSerialization");
			MethodInfo getState = shared.GetMethod("GetState", Any, null, Type.EmptyTypes, null);
			MethodInfo get = shared.GetMethod("GetPatchInfo", Any, null, new Type[] { typeof(MethodBase) }, null);
			MethodInfo set = shared.GetMethod("UpdatePatchInfo", Any, null, new Type[] { typeof(MethodBase), typeof(PatchInfo) }, null);
			MethodInfo s = object.ReferenceEquals(ser, null) ? null : ser.GetMethod("Serialize", Any, null, new Type[] { typeof(PatchInfo) }, null);
			MethodInfo d = object.ReferenceEquals(ser, null) ? null : ser.GetMethod("Deserialize", Any, null, new Type[] { typeof(byte[]) }, null);
			if (object.ReferenceEquals(getState, null) || object.ReferenceEquals(get, null) || object.ReferenceEquals(set, null) || object.ReferenceEquals(s, null) || object.ReferenceEquals(d, null))
			{
				N.ULog.Warn(Boot.Tag, "harmony cache off: Harmony " + typeof(HarmonyInstance).Assembly.GetName().Version + " members not found");
				return;
			}
			_state = getState.Invoke(null, null) as Dictionary<MethodBase, byte[]>;
			if (_state == null)
			{
				N.ULog.Warn(Boot.Tag, "harmony cache off: shared state not found");
				return;
			}
			_serialize = (SerializeFn)Delegate.CreateDelegate(typeof(SerializeFn), s);
			_deserialize = (DeserializeFn)Delegate.CreateDelegate(typeof(DeserializeFn), d);
			// UpdatePatchInfo first: patching GetPatchInfo then stores its own PatchInfo through the new Update.
			if (!N.PatchUtil.Install(harmony, Boot.Tag, set, new HarmonyMethod(typeof(HarmonyCache).GetMethod("UpdatePrefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				return;
			}
			Active = N.PatchUtil.Install(harmony, Boot.Tag, get, new HarmonyMethod(typeof(HarmonyCache).GetMethod("GetPrefix", BindingFlags.Public | BindingFlags.Static)), null);
			N.ULog.Info(Boot.Tag, "harmony cache " + (Active ? "on" : "partly on (Get not patched)") + ", " + _state.Count + " methods patched before it");
			GameObject go = new GameObject("NailOptimization.HarmonyCache");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<HarmonyCacheDriver>();
		}

		// Replaces HarmonySharedState.UpdatePatchInfo: GetState()[method] = patchInfo.Serialize().
		public static bool UpdatePrefix(MethodBase method, PatchInfo patchInfo)
		{
			byte[] bytes = _serialize(patchInfo);
			_state[method] = bytes;
			Entry e = new Entry();
			e.Bytes = bytes;
			e.Info = Copy(patchInfo);
			System.Threading.Monitor.Enter(_gate);
			try
			{
				_cache[method] = e;
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
			Stores++;
			return false;
		}

		// Replaces HarmonySharedState.GetPatchInfo: null when the method has no state, else a PatchInfo nobody else holds.
		public static bool GetPrefix(MethodBase method, ref PatchInfo __result)
		{
			byte[] bytes;
			if (!_state.TryGetValue(method, out bytes) || bytes == null)
			{
				__result = null;
				return false;
			}
			Entry e;
			System.Threading.Monitor.Enter(_gate);
			try
			{
				if (_cache.TryGetValue(method, out e) && object.ReferenceEquals(e.Bytes, bytes))
				{
					Hits++;
					__result = Copy(e.Info);
					return false;
				}
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
			PatchInfo info = _deserialize(bytes);
			e = new Entry();
			e.Bytes = bytes;
			e.Info = Copy(info);
			System.Threading.Monitor.Enter(_gate);
			try
			{
				_cache[method] = e;
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
			Misses++;
			__result = info;
			return false;
		}

		internal static PatchInfo Copy(PatchInfo p)
		{
			if (p == null)
			{
				return null;
			}
			PatchInfo c = new PatchInfo();
			c.prefixes = p.prefixes == null ? null : (Patch[])p.prefixes.Clone();
			c.postfixes = p.postfixes == null ? null : (Patch[])p.postfixes.Clone();
			c.transpilers = p.transpilers == null ? null : (Patch[])p.transpilers.Clone();
			return c;
		}
	}

	public class HarmonyCacheDriver : MonoBehaviour
	{
		private void Update()
		{
			enabled = false;
			N.ULog.Info(Boot.Tag, "harmony cache at first frame: stores " + HarmonyCache.Stores + ", hits " + HarmonyCache.Hits + ", misses " + HarmonyCache.Misses);
		}
	}

	// LoadGuard.HarmonyDefer (task 097, boot time): Harmony 1.0.9.1 PatchProcessor.Patch rebuilds the whole wrapper of a method
	// for every new patch on it (PatchFunctions.UpdateWrapper: copy of the original + all patches, JIT, jump), so a method with
	// N patches is built N times. For a method that already has a 0Harmony wrapper the rebuild is deferred: the original jumps
	// to a small stub (same signature: FlushId(id), then call the original again) that builds the final wrapper on its first
	// call; the driver builds the rest 1 s after the last deferral (4 ms per frame, from the first frame), and all are built
	// when 12Harmony loads (its first Create swaps 0Harmony's generator, GL-55, so earlier methods keep the 0Harmony one).
	// A flush builds from the current shared state (other Harmony copies included). A failing build is replayed patch by
	// patch like vanilla: the failing patch is dropped and the state written back without it (its mod got no exception).
	// No exception handlers in UpdateWrapper (GL-53 ok). All state is guarded by PatchProcessor.locker.
	public static class HarmonyDefer
	{
		private delegate PatchInfo GetFn(MethodBase method);
		private delegate void SetFn(MethodBase method, PatchInfo info);

		private sealed class Pending
		{
			public MethodBase Original;
			public int Id;
			public PatchInfo Basis;
			// R8: no real wrapper to jump back to; the original still reaches the stub.
			public bool Dead;
			public readonly List<PatchInfo> Steps = new List<PatchInfo>();
		}

		private static object _locker;
		private static Dictionary<object, object> _wrappers;
		private static GetFn _get;
		private static SetFn _set;
		private static MethodInfo _flushId;
		private static readonly Dictionary<MethodBase, Pending> _pending = new Dictionary<MethodBase, Pending>();
		private static readonly Dictionary<int, Pending> _byId = new Dictionary<int, Pending>();
		private static readonly List<System.Reflection.Emit.DynamicMethod> _stubs = new List<System.Reflection.Emit.DynamicMethod>();
		[ThreadStatic]
		private static bool _building;
		private static bool _warned;
		private static bool _deadLogged;
		private static bool _h12;
		private static int _nextId;
		private const int MinIl = 32;
		internal static Assembly GuardAssembly;
		internal static bool Active;
		internal static long LastDefer;
		internal static int Methods;
		internal static int Deferred;
		internal static int Built;
		internal static int StubCalls;
		internal static int Dropped;
		internal static int PrefixErrors;

		internal static int PendingCount
		{
			get { return _pending.Count; }
		}

		internal static void Start(HarmonyInstance harmony)
		{
			try
			{
				const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
				FieldInfo locker = typeof(PatchProcessor).GetField("locker", Any);
				FieldInfo refs = typeof(PatchTools).GetField("objectReferences", Any);
				MethodInfo get = typeof(HarmonySharedState).GetMethod("GetPatchInfo", Any, null, new Type[] { typeof(MethodBase) }, null);
				MethodInfo set = typeof(HarmonySharedState).GetMethod("UpdatePatchInfo", Any, null, new Type[] { typeof(MethodBase), typeof(PatchInfo) }, null);
				MethodInfo wrap = typeof(PatchFunctions).GetMethod("UpdateWrapper", Any, null, new Type[] { typeof(MethodBase), typeof(PatchInfo) }, null);
				_flushId = typeof(HarmonyDefer).GetMethod("FlushId", BindingFlags.Public | BindingFlags.Static);
				_locker = object.ReferenceEquals(locker, null) ? null : locker.GetValue(null);
				_wrappers = object.ReferenceEquals(refs, null) ? null : refs.GetValue(null) as Dictionary<object, object>;
				if (_locker == null || _wrappers == null || object.ReferenceEquals(get, null) || object.ReferenceEquals(set, null) || object.ReferenceEquals(wrap, null) || object.ReferenceEquals(_flushId, null))
				{
					N.ULog.Warn(Boot.Tag, "harmony defer off: Harmony " + typeof(HarmonyInstance).Assembly.GetName().Version + " members not found");
					return;
				}
				_get = (GetFn)Delegate.CreateDelegate(typeof(GetFn), get);
				_set = (SetFn)Delegate.CreateDelegate(typeof(SetFn), set);
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					if (Is12Harmony(all[i]))
					{
						_h12 = true;
					}
				}
				AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
				// Last: other prefixes on UpdateWrapper (timing probes) still run before the build is skipped.
				Active = N.PatchUtil.Install(harmony, Boot.Tag, wrap, N.PatchUtil.WithPriority(typeof(HarmonyDefer).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static), Priority.Last), null);
				N.ULog.Info(Boot.Tag, "harmony defer " + (Active ? "on" : "off") + (_h12 ? " (12Harmony already loaded)" : ""));
				if (Active)
				{
					GameObject go = new GameObject("NailOptimization.HarmonyDefer");
					UnityEngine.Object.DontDestroyOnLoad(go);
					go.AddComponent<HarmonyDeferDriver>();
				}
			}
			catch (Exception e)
			{
				Active = false;
				N.ULog.Warn(Boot.Tag, "harmony defer off: " + e);
			}
		}

		private static bool Is12Harmony(Assembly a)
		{
			string n = a.FullName;
			return n != null && n.StartsWith("12Harmony,", StringComparison.Ordinal);
		}

		private static void OnLoad(object sender, AssemblyLoadEventArgs e)
		{
			if (!_h12 && Is12Harmony(e.LoadedAssembly))
			{
				_h12 = true;
				int n = FlushAll();
				N.ULog.Info(Boot.Tag, "harmony defer: 12Harmony loaded, " + n + " deferred methods built with the 0Harmony generator");
			}
		}

		// Prefix on PatchFunctions.UpdateWrapper(original, patchInfo): false = the build is deferred.
		public static bool Prefix(MethodBase original, PatchInfo patchInfo)
		{
			if (_building || !Active || object.ReferenceEquals(original, null) || patchInfo == null)
			{
				return true;
			}
			System.Threading.Monitor.Enter(_locker);
			try
			{
				Pending p;
				bool pending = _pending.TryGetValue(original, out p);
				// Transpilers and patch factories run mod code while the wrapper is built: keep that inside the mod's Patch call.
				if (RunsModCode(pending ? p.Steps[p.Steps.Count - 1] : null, patchInfo))
				{
					if (pending)
					{
						Cancel(p);
					}
					return true;
				}
				if (!pending)
				{
					if (!Eligible(original))
					{
						return true;
					}
					PatchInfo basis = _get(original);
					if (basis == null || RunsModCode(basis, patchInfo))
					{
						return true;
					}
					int id = ++_nextId;
					long from = Harmony.ILCopying.Memory.GetMethodStart(original);
					System.Reflection.Emit.DynamicMethod stub = MakeStub((MethodInfo)original, id);
					long to = Harmony.ILCopying.Memory.GetMethodStart(stub);
					_stubs.Add(stub);
					p = new Pending();
					p.Original = original;
					p.Id = id;
					p.Basis = HarmonyCache.Copy(basis);
					p.Steps.Add(HarmonyCache.Copy(patchInfo));
					// R7: register only after the jump is written; a failed WriteJump leaves no entry (the build runs at once).
					Harmony.ILCopying.Memory.WriteJump(from, to);
					_pending[original] = p;
					_byId[id] = p;
					Methods++;
				}
				else
				{
					p.Steps.Add(HarmonyCache.Copy(patchInfo));
				}
				Deferred++;
				LastDefer = System.Diagnostics.Stopwatch.GetTimestamp();
				return false;
			}
			catch (Exception e)
			{
				PrefixErrors++;
				if (!_warned)
				{
					_warned = true;
					N.ULog.Warn(Boot.Tag, "harmony defer: " + Name(original) + " built at once: " + e.Message);
				}
				return true;
			}
			finally
			{
				System.Threading.Monitor.Exit(_locker);
			}
		}

		private static bool Eligible(MethodBase original)
		{
			MethodInfo mi = original as MethodInfo;
			if (object.ReferenceEquals(mi, null))
			{
				return false;
			}
			Type t = mi.DeclaringType;
			if (object.ReferenceEquals(t, null) || !object.ReferenceEquals(mi.ReflectedType, t) || t.IsValueType || t.IsInterface || t.IsGenericType || t.ContainsGenericParameters || mi.IsGenericMethod || mi.ContainsGenericParameters || mi.IsAbstract)
			{
				return false;
			}
			if ((mi.CallingConvention & CallingConventions.VarArgs) != 0)
			{
				return false;
			}
			MethodBody body = mi.GetMethodBody();
			if (body == null || body.GetILAsByteArray().Length < MinIl)
			{
				return false;
			}
			if (object.ReferenceEquals(t.Assembly, typeof(PatchFunctions).Assembly) || object.ReferenceEquals(t.Assembly, typeof(HarmonyDefer).Assembly) || object.ReferenceEquals(t.Assembly, GuardAssembly))
			{
				return false;
			}
			return _wrappers.ContainsKey(original);
		}

		// static R Name_Defer(T this, args...) { HarmonyDefer.FlushId(id); return original(this, args...); }
		// Mono 2.0 DynamicMethod has no EmitCalli (ModuleBuilder expected, run 097-defer-C1); the plain call cannot be inlined
		// because Eligible takes only originals of MinIl bytes or more (Mono inlines up to 20).
		private static System.Reflection.Emit.DynamicMethod MakeStub(MethodInfo mi, int id)
		{
			ParameterInfo[] ps = mi.GetParameters();
			int off = mi.IsStatic ? 0 : 1;
			Type[] types = new Type[ps.Length + off];
			if (off == 1)
			{
				types[0] = mi.DeclaringType;
			}
			for (int i = 0; i < ps.Length; i++)
			{
				types[i + off] = ps[i].ParameterType;
			}
			System.Reflection.Emit.DynamicMethod dm = new System.Reflection.Emit.DynamicMethod(mi.Name + "_Defer", MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Standard, mi.ReturnType, types, mi.DeclaringType, true);
			System.Reflection.Emit.ILGenerator il = dm.GetILGenerator();
			il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, id);
			il.Emit(System.Reflection.Emit.OpCodes.Call, _flushId);
			for (int i = 0; i < types.Length; i++)
			{
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg, (short)i);
			}
			il.Emit(System.Reflection.Emit.OpCodes.Call, mi);
			il.Emit(System.Reflection.Emit.OpCodes.Ret);
			// Mono: compile the method before its start is read (as MethodPatcher does); an unfinished one gives a bad address (run 097-defer-C2 crashed).
			Harmony.DynamicTools.PrepareDynamicMethod(dm);
			return dm;
		}

		// Called by a stub. Must leave the original jumping to a real wrapper: the stub calls the original next.
		public static void FlushId(int id)
		{
			string dead = null;
			System.Threading.Monitor.Enter(_locker);
			try
			{
				Pending p;
				if (_byId.TryGetValue(id, out p))
				{
					if (p.Dead && HasWrapper(p.Original))
					{
						// Got a real wrapper meanwhile (vanilla build after Cancel): the original no longer reaches the stub.
						p.Dead = false;
						_byId.Remove(id);
					}
					else if (p.Dead)
					{
						dead = Name(p.Original);
						if (!_deadLogged)
						{
							_deadLogged = true;
							N.ULog.Error(Boot.Tag, "harmony defer: no wrapper for " + dead + "; calls of it throw");
						}
					}
					else
					{
						StubCalls++;
						Build(p);
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(Boot.Tag, "harmony defer: build on first call failed: " + e);
			}
			finally
			{
				System.Threading.Monitor.Exit(_locker);
			}
			// R8: the stub would call the original, which jumps back to the stub (endless recursion).
			if (dead != null)
			{
				throw new InvalidOperationException("harmony defer: no wrapper for " + dead);
			}
		}

		// Driver: builds one pending method; false when none is left.
		internal static bool FlushNext()
		{
			System.Threading.Monitor.Enter(_locker);
			try
			{
				Pending p = null;
				foreach (KeyValuePair<MethodBase, Pending> kv in _pending)
				{
					p = kv.Value;
					break;
				}
				if (p == null)
				{
					return false;
				}
				Build(p);
				return _pending.Count > 0;
			}
			catch (Exception e)
			{
				N.ULog.Warn(Boot.Tag, "harmony defer: build failed: " + e);
				return _pending.Count > 0;
			}
			finally
			{
				System.Threading.Monitor.Exit(_locker);
			}
		}

		private static int FlushAll()
		{
			int n = _pending.Count;
			while (FlushNext())
			{
			}
			return n;
		}

		// Caller holds _locker.
		private static void Build(Pending p)
		{
			Cancel(p);
			PatchInfo info = _get(p.Original);
			PatchInfo lastStep = p.Steps[p.Steps.Count - 1];
			// The state lags behind the last step while Patch has not stored it yet (a call of the method in that window).
			if (info == null || Count(lastStep) > Count(info))
			{
				info = lastStep;
			}
			bool was = _building;
			_building = true;
			try
			{
				Exception first = null;
				try
				{
					PatchFunctions.UpdateWrapper(p.Original, info);
					Built++;
				}
				catch (Exception e)
				{
					first = e;
				}
				if (first != null)
				{
					// R6: a throw inside Replay must not hide which patches were lost.
					try
					{
						Replay(p, first, info);
					}
					catch (Exception e)
					{
						Dropped++;
						N.ULog.Warn(Boot.Tag, "harmony defer: replay on " + Name(p.Original) + " failed (" + e.Message + "); patches of " + Owners(p) + " may be lost");
					}
				}
			}
			finally
			{
				_building = was;
			}
			// A dead entry that got a real wrapper now (build or replay) no longer reaches the stub.
			if (p.Dead && HasWrapper(p.Original))
			{
				p.Dead = false;
				_byId.Remove(p.Id);
			}
		}

		private static bool HasWrapper(MethodBase original)
		{
			object last;
			return _wrappers.TryGetValue(original, out last) && last is MethodBase;
		}

		// Owners of the patches the steps added on top of the basis.
		private static string Owners(Pending p)
		{
			List<string> owners = new List<string>();
			PatchInfo s = p.Steps[p.Steps.Count - 1];
			List<Patch> delta = new List<Patch>();
			AddNew(null, p.Basis.prefixes, s.prefixes, delta);
			AddNew(null, p.Basis.postfixes, s.postfixes, delta);
			AddNew(null, p.Basis.transpilers, s.transpilers, delta);
			for (int i = 0; i < delta.Count; i++)
			{
				if (!owners.Contains(delta[i].owner))
				{
					owners.Add(delta[i].owner);
				}
			}
			return owners.Count == 0 ? "?" : string.Join(", ", owners.ToArray());
		}

		// Back to the last real wrapper, then forget the entry: a call after this must not reach the stub again.
		// R8: without a real wrapper the entry stays by id as Dead, so the stub throws instead of recursing.
		private static void Cancel(Pending p)
		{
			object last;
			_pending.Remove(p.Original);
			if (_wrappers.TryGetValue(p.Original, out last) && last is MethodBase)
			{
				long from = Harmony.ILCopying.Memory.GetMethodStart(p.Original);
				long to = Harmony.ILCopying.Memory.GetMethodStart((MethodBase)last);
				Harmony.ILCopying.Memory.WriteJump(from, to);
				_byId.Remove(p.Id);
			}
			else
			{
				p.Dead = true;
			}
		}

		private static int Count(PatchInfo info)
		{
			return (info.prefixes == null ? 0 : info.prefixes.Length) + (info.postfixes == null ? 0 : info.postfixes.Length) + (info.transpilers == null ? 0 : info.transpilers.Length);
		}

		// True when the new patch info adds a transpiler or holds a patch factory (a patch method returning DynamicMethod).
		private static bool RunsModCode(PatchInfo prev, PatchInfo now)
		{
			int before = prev == null || prev.transpilers == null ? 0 : prev.transpilers.Length;
			if (now.transpilers != null && now.transpilers.Length > before)
			{
				return true;
			}
			return HasFactory(now.prefixes) || HasFactory(now.postfixes);
		}

		private static bool HasFactory(Patch[] patches)
		{
			if (patches == null)
			{
				return false;
			}
			for (int i = 0; i < patches.Length; i++)
			{
				MethodInfo pm = patches[i].patch;
				if (!object.ReferenceEquals(pm, null) && object.ReferenceEquals(pm.ReturnType, typeof(System.Reflection.Emit.DynamicMethod)))
				{
					return true;
				}
			}
			return false;
		}

		// Vanilla order: basis + the new patches of each step; a step that fails is dropped as Patch would have thrown.
		private static void Replay(Pending p, Exception first, PatchInfo info)
		{
			if (Count(info) != Count(p.Steps[p.Steps.Count - 1]))
			{
				// The state was changed by another Harmony copy or a direct UpdateWrapper caller: keep it and the last wrapper.
				Dropped++;
				N.ULog.Warn(Boot.Tag, "harmony defer: " + Name(p.Original) + " build failed (" + first.Message + "), state changed outside Patch; last wrapper kept");
				return;
			}
			N.ULog.Warn(Boot.Tag, "harmony defer: " + Name(p.Original) + " build failed (" + first.Message + "), replaying " + p.Steps.Count + " patch call(s)");
			PatchInfo good = p.Basis;
			PatchInfo prev = p.Basis;
			List<Patch> delta = new List<Patch>();
			for (int i = 0; i < p.Steps.Count; i++)
			{
				PatchInfo s = p.Steps[i];
				PatchInfo cand = HarmonyCache.Copy(good);
				delta.Clear();
				cand.prefixes = AddNew(cand.prefixes, prev.prefixes, s.prefixes, delta);
				cand.postfixes = AddNew(cand.postfixes, prev.postfixes, s.postfixes, delta);
				cand.transpilers = AddNew(cand.transpilers, prev.transpilers, s.transpilers, delta);
				prev = s;
				if (delta.Count == 0)
				{
					continue;
				}
				try
				{
					PatchFunctions.UpdateWrapper(p.Original, cand);
					Built++;
					good = cand;
				}
				catch (Exception e)
				{
					Dropped++;
					N.ULog.Warn(Boot.Tag, "harmony defer: patch by " + delta[0].owner + " (" + delta[0].patch + ") on " + Name(p.Original) + " dropped: " + e.Message);
				}
			}
			_set(p.Original, good);
		}

		private static Patch[] AddNew(Patch[] target, Patch[] prev, Patch[] now, List<Patch> delta)
		{
			if (now == null || now.Length == 0)
			{
				return target;
			}
			List<Patch> list = target == null ? new List<Patch>() : new List<Patch>(target);
			// Patch only appends, so the new patches are the tail (a second Patch with the same method counts again, as in vanilla).
			for (int i = prev == null ? 0 : prev.Length; i < now.Length; i++)
			{
				list.Add(now[i]);
				delta.Add(now[i]);
			}
			return list.ToArray();
		}

		private static string Name(MethodBase method)
		{
			return object.ReferenceEquals(method, null) ? "?" : (object.ReferenceEquals(method.DeclaringType, null) ? "" : method.DeclaringType.FullName + ".") + method.Name;
		}
	}

	public class HarmonyDeferDriver : MonoBehaviour
	{
		private bool _first = true;
		private int _reports;

		private void Update()
		{
			if (_first)
			{
				_first = false;
				N.ULog.Info(Boot.Tag, "harmony defer at first frame: " + Summary());
			}
			if (HarmonyDefer.PendingCount == 0)
			{
				return;
			}
			long now = System.Diagnostics.Stopwatch.GetTimestamp();
			if (now - HarmonyDefer.LastDefer < System.Diagnostics.Stopwatch.Frequency)
			{
				return;
			}
			long end = now + System.Diagnostics.Stopwatch.Frequency / 250;
			bool more = true;
			while (more && System.Diagnostics.Stopwatch.GetTimestamp() < end)
			{
				more = HarmonyDefer.FlushNext();
			}
			if (!more && _reports < 3)
			{
				_reports++;
				N.ULog.Info(Boot.Tag, "harmony defer: all built at frame " + Time.frameCount + ": " + Summary());
			}
		}

		private static string Summary()
		{
			return "deferred " + HarmonyDefer.Deferred + " patch calls in " + HarmonyDefer.Methods + " pending entries, built " + HarmonyDefer.Built + " (on first call " + HarmonyDefer.StubCalls + ", dropped " + HarmonyDefer.Dropped + ", prefix errors " + HarmonyDefer.PrefixErrors + "), pending " + HarmonyDefer.PendingCount;
		}
	}
}
