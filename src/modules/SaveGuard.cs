// SaveGuard: NailOptimization module (api level 4). Guards the save files the game writes through SaveUtil.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine.
// Output goes to <NailOptimization>/modules/.
//
//   Read     SaveUtil.ReadSerializableFile is replaced (its body has try/catch, GL-53): the same file logic as the game
//            (val.txt/_temp, main file, .backup fallback, FileReadException) with a SerializationBinder that names the
//            assembly a file needs and that is not loaded. SaveGuard.SafeLoad: types of such assemblies are read as a
//            placeholder and every dictionary value holding one is removed; the original file is copied to SaveStash/
//            and the removed values come back on a later read once their mod is loaded again.
//            Mono 2.0 resolves generic member types (List<ModType>) with Type.GetType and skips the binder, so during a SafeLoad
//            read an AssemblyResolve handler hands out Reflection.Emit stub assemblies and the missing types are defined as
//            [Serializable] stubs (class Stubs, filled by a surrogate), found from the binder and from the load exceptions, with a bounded retry.
//   Write    SaveUtil.WriteSerializableFile Prefix: a file marked unsafe is not written (SaveGuard.Protect); the old file
//            is copied to SaveBackups/<file>/ (SaveGuard.Backups kept); assemblies the written data depends on are logged;
//            a drop of creatures, equipment or codex entries pins the old file in SaveBackups/<file>/pinned/.
//   Marks    day file: GlobalGameManager.LoadDay started and did not finish, or its last read failed; global file: its
//            last read failed, or BaseMods/Glerror.txt was written after the last read (the game's LoadGlobalData catch).
//   Report   entries of mods that are not loaded (creatures, codex, equipment), which the game drops silently on load.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Harmony;
using UnityEngine;
using N = NailOptimization;

namespace SaveGuard
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class SaveGuardModule : N.ModuleBase
	{
		public const string Id = "SaveGuard";
		public const string HarmonyId = "Lobotomy.NailOptimization.SaveGuard";
		private const int Expected = 5;

		private static N.Flag _enabled;
		private static N.Flag _protect;
		private static N.Flag _safeLoad;

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

		// Literal on purpose: a const from Core would be inlined at compile time. 4: UiHost.RootDir.
		public override int RequiredApiLevel
		{
			get
			{
				return 4;
			}
		}

		public override string[] Targets
		{
			get
			{
				return new string[] { "SaveUtil.ReadSerializableFile", "SaveUtil.WriteSerializableFile", "GlobalGameManager.LoadDay", "GlobalGameManager.ReleaseGame" };
			}
		}

		public override void Init()
		{
			_enabled = N.Config.BindFlag("SaveGuard.Enabled", true, "Save files: backup before each write, read check that names missing mods, no overwrite after a failed load, report of entries of removed mods. Read at game start.");
			if (!_enabled.On)
			{
				N.ULog.Info(Guard.Tag, "disabled by SaveGuard.Enabled=false");
				return;
			}
			_protect = N.Config.BindFlag("SaveGuard.Protect", true, "With SaveGuard on: a save file whose load failed is not overwritten until it loads again. Read at game start.");
			_safeLoad = N.Config.BindFlag("SaveGuard.SafeLoad", false, "With SaveGuard on: data of removed mods that makes a save file unreadable is taken out on load and kept in SaveStash/; it is put back when the mod is installed again. Read at game start.");
			Guard.Backups = N.Config.BindInt("SaveGuard.Backups", 3, 0, 20, 1, "With SaveGuard on: copies of each save file kept in SaveBackups/ before it is overwritten; 0 = no copies.");
			Guard.ProtectOn = _protect.On;
			Guard.SafeLoadOn = _safeLoad.On;
			Guard.Init();
			int n = Install();
			N.ULog.Info(Guard.Tag, "hook installed " + n + "/" + Expected);
			N.ULog.Info(Guard.Tag, "options protect=" + Guard.ProtectOn + " safeload=" + Guard.SafeLoadOn + " backups=" + Guard.Backups.Value);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int Install()
		{
			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			int n = 0;
			// Last: other mods' Prefixes on the read (XmlSaveMod keeps the file name) still run before the replacement.
			n += Patch(harmony, typeof(SaveUtil), "ReadSerializableFile", "PrefixRead", null, Priority.Last);
			// First: a blocked write also stops other mods' Prefixes (XmlSaveMod would export the bad data).
			n += Patch(harmony, typeof(SaveUtil), "WriteSerializableFile", "PrefixWrite", null, Priority.First);
			n += Patch(harmony, typeof(GlobalGameManager), "LoadDay", "PrefixLoadDay", "PostfixLoadDay", Priority.Normal);
			n += Patch(harmony, typeof(GlobalGameManager), "ReleaseGame", "PrefixRelease", null, Priority.Normal);
			n += Patch(harmony, typeof(GlobalGameManager), "SaveStateData", "PrefixState", null, Priority.Normal);
			try
			{
				Application.logMessageReceived += new Application.LogCallback(Hooks.OnUnityLog);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Guard.Tag, "day load failure check off: " + e.Message);
			}
			return n;
		}

		private static int Patch(HarmonyInstance harmony, Type type, string name, string prefix, string postfix, int priority)
		{
			MethodBase target = N.PatchUtil.Resolve(Guard.Tag, type, name, null);
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			MethodInfo pre = typeof(Hooks).GetMethod(prefix, BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(pre, null))
			{
				N.ULog.Error(Guard.Tag, "prefix not found: " + prefix);
				return 0;
			}
			HarmonyMethod post = null;
			if (postfix != null)
			{
				MethodInfo pm = typeof(Hooks).GetMethod(postfix, BindingFlags.Public | BindingFlags.Static);
				if (object.ReferenceEquals(pm, null))
				{
					N.ULog.Error(Guard.Tag, "postfix not found: " + postfix);
					return 0;
				}
				post = new HarmonyMethod(pm);
			}
			HarmonyMethod hm = N.PatchUtil.WithPriority(pre, priority);
			return N.PatchUtil.Install(harmony, Guard.Tag, target, hm, post) ? 1 : 0;
		}
	}

	// Hands the formatter a surrogate for every stub type (the emitted types themselves are plain [Serializable]).
	internal sealed class SgSurrogates : ISurrogateSelector
	{
		private static readonly SgStubSurrogate Shared = new SgStubSurrogate();

		public ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
		{
			if (Stubs.IsStubType(type))
			{
				selector = this;
				return Shared;
			}
			selector = null;
			return null;
		}

		public void ChainSelector(ISurrogateSelector selector)
		{
			throw new NotSupportedException();
		}

		public ISurrogateSelector GetNextSelector()
		{
			return null;
		}
	}

	// Fills a stub instance from the stream data (registers it, absorbs its values); a stub is never saved.
	internal sealed class SgStubSurrogate : ISerializationSurrogate
	{
		public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
		{
			throw new SerializationException("SaveGuard stub cannot be saved");
		}

		public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
		{
			Guard.StubBuilt(obj, info);
			return obj;
		}
	}

	// Names the assemblies a save file needs and that cannot be loaded; binds their types to stubs when Map is set.
	// Returning null keeps the formatter's own type resolution.
	internal sealed class SgBinder : SerializationBinder
	{
		internal readonly bool Map;
		internal readonly List<string> MissingAsm = new List<string>();
		internal string FirstType;
		private readonly Dictionary<string, bool> _asmOk = new Dictionary<string, bool>();

		internal SgBinder(bool map)
		{
			Map = map;
		}

		public override Type BindToType(string assemblyName, string typeName)
		{
			try
			{
				string bad = FindMissing(assemblyName, typeName);
				if (bad == null)
				{
					return null;
				}
				if (!MissingAsm.Contains(bad))
				{
					MissingAsm.Add(bad);
				}
				if (FirstType == null)
				{
					FirstType = typeName;
				}
				if (Map)
				{
					Type stub = Stubs.Bind(Guard.SimpleName(assemblyName), typeName, this);
					if (!object.ReferenceEquals(stub, null))
					{
						return stub;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "binder " + e.GetType().Name + ": " + e.Message);
			}
			return null;
		}

		internal string Joined()
		{
			return string.Join("|", MissingAsm.ToArray());
		}

		// A missing assembly found outside the binder calls (load exception text).
		internal void Note(string asm, string typeName)
		{
			if (!MissingAsm.Contains(asm))
			{
				MissingAsm.Add(asm);
			}
			if (FirstType == null)
			{
				FirstType = typeName;
			}
		}

		private string FindMissing(string assemblyName, string typeName)
		{
			string top = Guard.SimpleName(assemblyName);
			if (!AsmOk(top))
			{
				return top;
			}
			// Generic arguments carry their own assembly: "...[[Ns.Type, Asm, Version=..., ...]]".
			int p = typeName.IndexOf(", Version=", StringComparison.Ordinal);
			while (p > 0)
			{
				int s = typeName.LastIndexOf(", ", p - 1, StringComparison.Ordinal);
				if (s >= 0)
				{
					string name = typeName.Substring(s + 2, p - s - 2);
					if (!AsmOk(name))
					{
						return name;
					}
				}
				p = typeName.IndexOf(", Version=", p + 10, StringComparison.Ordinal);
			}
			// Type check only for mod assemblies: game types may be forwarded (UnityEngine facade) and resolve only by default.
			if (typeName.IndexOf('[') < 0 && !Guard.IsGameAssembly(top))
			{
				Assembly a = Guard.FindAssembly(top);
				if (!object.ReferenceEquals(a, null) && object.ReferenceEquals(a.GetType(typeName, false), null))
				{
					return top + ":" + typeName;
				}
			}
			return null;
		}

		private bool AsmOk(string simple)
		{
			bool ok;
			if (_asmOk.TryGetValue(simple, out ok))
			{
				return ok;
			}
			ok = !object.ReferenceEquals(Guard.FindAssembly(simple), null);
			_asmOk[simple] = ok;
			return ok;
		}
	}

	public static class Hooks
	{
		private static FieldInfo _logCount;
		private static bool _stateFailLogged;
		private static bool _inLog;
		private static bool _stateOkLogged;

		// GlobalGameManager.SaveStateData writes <persistentDataPath>170808state.dat (options, language) with FileMode.Create:
		// a crash or kill during the write leaves it empty or cut. The next start then throws in GlobalGameManager.Awake
		// (LoadStateData has no try/catch) before the map, mods, texts and saves load: every UI text is "UNKNOWN" and the save
		// cannot be continued, on every start, until the file is deleted. Mods load after that read, so the read cannot be
		// guarded; the write is made atomic instead: the same dictionary goes to a temp file that then replaces the old one.
		// On any error the original write runs.
		public static bool PrefixState(GlobalGameManager __instance)
		{
			try
			{
				if (object.ReferenceEquals(_logCount, null))
				{
					_logCount = typeof(GlobalGameManager).GetField("logCount", BindingFlags.Instance | BindingFlags.NonPublic);
					if (object.ReferenceEquals(_logCount, null))
					{
						return true;
					}
				}
				Dictionary<string, object> dic = new Dictionary<string, object>();
				dic.Add("bgmVolume", __instance.sceneDataSaver.currentBgmVolume);
				dic.Add("masterVolume", __instance.sceneDataSaver.currentVolume);
				dic.Add("tooltip", __instance.sceneDataSaver.tooltipState);
				dic.Add("dlcCreatureOn", __instance.dlcCreatureOn);
				dic.Add("language", __instance.GetCurrentLanguage());
				dic.Add("logIndex", (int)_logCount.GetValue(__instance));
				MemoryStream ms = new MemoryStream();
				new BinaryFormatter().Serialize(ms, dic);
				byte[] bytes = ms.ToArray();
				string dst = Application.persistentDataPath + "170808state.dat";
				string tmp = dst + ".tmp";
				File.WriteAllBytes(tmp, bytes);
				if (File.Exists(dst))
				{
					File.Replace(tmp, dst, null);
				}
				else
				{
					File.Move(tmp, dst);
				}
				if (!_stateOkLogged)
				{
					_stateOkLogged = true;
					N.ULog.Info(Guard.Tag, "options file written through a temp file (" + bytes.Length + " bytes)");
				}
				return false;
			}
			catch (Exception e)
			{
				if (!_stateFailLogged)
				{
					_stateFailLogged = true;
					N.ULog.Warn(Guard.Tag, "options file atomic write failed, the game's own write runs: " + e.GetType().Name + ": " + e.Message);
				}
				return true;
			}
		}

		// Full replacement of SaveUtil.ReadSerializableFile (the original never runs; its handlers are kept that way).
		// Throws FileReadException on a failed read, exactly where the original throws it.
		public static bool PrefixRead(string fileName, ref Dictionary<string, object> __result)
		{
			__result = Guard.Read(fileName);
			return false;
		}

		public static bool PrefixWrite(string fileName, Dictionary<string, object> dic)
		{
			try
			{
				return Guard.BeforeWrite(fileName, dic);
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "write prefix " + e);
				return true;
			}
		}

		public static void PrefixLoadDay(Dictionary<string, object> data)
		{
			Guard.DayOpen = true;
			Guard.DayFailReported = false;
			try
			{
				Guard.OnLoadDay(data);
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "load day check " + e);
			}
		}

		public static void PostfixLoadDay()
		{
			Guard.DayOpen = false;
		}

		// Day load failure: an exception left GlobalGameManager.LoadDay (its Postfix did not run, DayOpen stays set) and the
		// caller logged it (AlterTitleController.CallContinue does). Reported at once with the error's first lines, instead of
		// only when a later save write is blocked. An error logged while LoadDay still runs (a mod's own caught error) is not
		// a failure: LoadDay is still on the stack then. Runs only for error lines while a day load is open.
		public static void OnUnityLog(string condition, string stackTrace, LogType type)
		{
			if (!Guard.DayOpen || Guard.DayFailReported || _inLog || (type != LogType.Exception && type != LogType.Error))
			{
				return;
			}
			_inLog = true;
			try
			{
				if (Environment.StackTrace.IndexOf("LoadDay", StringComparison.Ordinal) < 0)
				{
					Guard.DayFailReported = true;
					N.ULog.Error(Guard.Tag, "the day load stopped on an error, the day did not open and the save files stay as they were: " + ErrorHead(condition, stackTrace));
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				_inLog = false;
			}
		}

		// First line of the error plus its first three "at" lines (from the message, else from the Unity stack).
		private static string ErrorHead(string condition, string stackTrace)
		{
			string[] lines = (condition ?? string.Empty).Split(new char[] { '\n' });
			StringBuilder sb = new StringBuilder(lines[0].Trim());
			int at = Frames(sb, lines, 1);
			if (at == 0 && stackTrace != null)
			{
				Frames(sb, stackTrace.Split(new char[] { '\n' }), 0);
			}
			return sb.ToString();
		}

		private static int Frames(StringBuilder sb, string[] lines, int from)
		{
			int at = 0;
			for (int i = from; i < lines.Length && at < 3; i++)
			{
				string t = lines[i].Trim();
				if (t.Length > 0)
				{
					sb.Append(" | ").Append(t);
					at++;
				}
			}
			return at;
		}

		public static void PrefixRelease()
		{
			Guard.DayOpen = false;
			Guard.DaySnap = -1;
			Guard.DaySnapDay = -1;
			Guard.ReadFailed.Remove(Guard.DayFile);
			Guard.Reported.Clear();
		}
	}

	internal sealed class StashEntry
	{
		internal string File;
		internal string Copy;
		internal string Path;
		internal string Asms;
	}

	// Stub assemblies and stub types for the assemblies a save file needs and that are not loaded (SafeLoad only).
	// One Reflection.Emit assembly per missing assembly name, kept for the session; a stub type is defined by its full name
	// (Outer+Inner nests). Stubs are plain [Serializable] types, absorb the data of the stream and refuse to be saved.
	internal static class Stubs
	{
		internal const int MaxAttempts = 16;

		private sealed class StubAsm
		{
			internal AssemblyBuilder Ab;
			internal ModuleBuilder Mod;
			internal readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
		}

		private static readonly Dictionary<string, StubAsm> _asms = new Dictionary<string, StubAsm>();
		private static readonly Dictionary<Type, bool> _types = new Dictionary<Type, bool>();
		private static readonly Dictionary<Type, bool> _refs = new Dictionary<Type, bool>();

		private static readonly HashSet<string> _bound = new HashSet<string>();

		internal static int Count;
		internal static int Use;
		internal static bool Probing;

		// AssemblyResolve during a SafeLoad read: a missing assembly becomes its stub assembly.
		internal static Assembly OnResolve(object sender, ResolveEventArgs args)
		{
			try
			{
				if (Probing)
				{
					return null;
				}
				string simple = Guard.SimpleName(args.Name);
				if (simple.Length == 0 || Guard.IsGameAssembly(simple) || simple.EndsWith(".resources", StringComparison.Ordinal))
				{
					return null;
				}
				Use++;
				return AsmFor(simple).Ab;
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "stub resolve " + e.GetType().Name + ": " + e.Message);
				return null;
			}
		}

		// Binder call for a type of a missing assembly: the stub type (an array name gives the array type of the stub), or
		// null (generic names are resolved by the formatter through the stub assemblies once their argument types are defined).
		internal static Type Bind(string asm, string typeName, SgBinder binder)
		{
			Type t = BindCore(asm, typeName, binder);
			if (N.ULog.Enabled && _bound.Add(asm + "|" + typeName))
			{
				N.ULog.Info(Guard.Tag, "bind " + asm + " " + typeName + " -> " + (object.ReferenceEquals(t, null) ? "null" : t.FullName));
			}
			return t;
		}

		private static Type BindCore(string asm, string typeName, SgBinder binder)
		{
			string baseName = StripArray(typeName);
			if (baseName.IndexOf('[') >= 0)
			{
				FromText(typeName, binder);
				return null;
			}
			if (asm.Length == 0 || Guard.IsGameAssembly(asm))
			{
				return null;
			}
			Type t = Get(asm, baseName);
			if (object.ReferenceEquals(t, null))
			{
				return null;
			}
			for (int i = (typeName.Length - baseName.Length) / 2; i > 0; i--)
			{
				t = t.MakeArrayType();
			}
			Use++;
			return t;
		}

		internal static void FromException(Exception e, SgBinder binder)
		{
			int n = 0;
			while (e != null && n < 8)
			{
				if (e.Message != null)
				{
					FromText(e.Message, binder);
				}
				e = e.InnerException;
				n++;
			}
		}

		// Finds "Ns.Type, Asm, Version=" fragments (type names inside brackets) and "type 'Ns.Type' from assembly 'Asm, Version="
		// and defines a stub for each type of an assembly that is not available.
		internal static void FromText(string text, SgBinder binder)
		{
			int p = text.IndexOf(", Version=", StringComparison.Ordinal);
			while (p > 0)
			{
				int a = p;
				while (a > 0 && IsAsmChar(text[a - 1]))
				{
					a--;
				}
				if (a < p && a > 0)
				{
					string asm = text.Substring(a, p - a);
					string type = null;
					if (text[a - 1] == ' ' && a >= 2 && text[a - 2] == ',')
					{
						int e = a - 2;
						while (e >= 2 && text[e - 1] == ']' && text[e - 2] == '[')
						{
							e -= 2;
						}
						int b = e;
						while (b > 0 && IsTypeChar(text[b - 1]))
						{
							b--;
						}
						type = text.Substring(b, e - b);
					}
					else if (text[a - 1] == '\'')
					{
						int ti = text.LastIndexOf("type '", a - 1, StringComparison.Ordinal);
						if (ti >= 0)
						{
							int te = text.IndexOf('\'', ti + 6);
							if (te > ti + 6 && te < a)
							{
								type = StripArray(text.Substring(ti + 6, te - ti - 6));
							}
						}
					}
					if (type != null && type.Length > 0 && type.IndexOf('[') < 0)
					{
						DefineIfMissing(asm, type, binder);
					}
				}
				p = text.IndexOf(", Version=", p + 10, StringComparison.Ordinal);
			}
		}

		private static void DefineIfMissing(string asm, string type, SgBinder binder)
		{
			if (type.IndexOf('`') >= 0 || Guard.IsGameAssembly(asm))
			{
				return;
			}
			if (!object.ReferenceEquals(Guard.FindAssembly(asm), null))
			{
				return;
			}
			if (!object.ReferenceEquals(Get(asm, type), null) && binder != null)
			{
				binder.Note(asm, type);
			}
		}

		private static bool IsAsmChar(char c)
		{
			return char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
		}

		private static bool IsTypeChar(char c)
		{
			return char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '+' || c == '`' || c == '$';
		}

		private static string StripArray(string name)
		{
			while (name.Length >= 2 && name[name.Length - 1] == ']' && name[name.Length - 2] == '[')
			{
				name = name.Substring(0, name.Length - 2);
			}
			return name;
		}

		private static StubAsm AsmFor(string simple)
		{
			StubAsm sa;
			if (_asms.TryGetValue(simple, out sa))
			{
				return sa;
			}
			sa = new StubAsm();
			AssemblyName an = new AssemblyName();
			an.Name = simple;
			sa.Ab = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
			sa.Mod = sa.Ab.DefineDynamicModule(simple);
			_asms[simple] = sa;
			return sa;
		}

		// The stub type of that full name in the stub assembly, defined on first use; null when it cannot be defined.
		internal static Type Get(string asm, string fullName)
		{
			try
			{
				StubAsm sa = AsmFor(asm);
				Type t;
				if (sa.Types.TryGetValue(fullName, out t))
				{
					return t;
				}
				return Define(sa, asm, fullName);
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "stub " + asm + " " + fullName + " failed: " + e.GetType().Name + ": " + e.Message);
				return null;
			}
		}

		private static Type Define(StubAsm sa, string asm, string fullName)
		{
			string[] parts = fullName.Split('+');
			List<TypeBuilder> builders = new List<TypeBuilder>();
			List<string> paths = new List<string>();
			TypeBuilder prev = null;
			string path = null;
			for (int i = 0; i < parts.Length; i++)
			{
				path = path == null ? parts[i] : path + "+" + parts[i];
				TypeBuilder tb;
				if (i == 0)
				{
					// An enclosing type that is already created cannot get nested types any more.
					if (parts.Length > 1 && sa.Types.ContainsKey(path))
					{
						N.ULog.Error(Guard.Tag, "stub " + asm + " " + fullName + ": enclosing type already defined");
						return null;
					}
					tb = sa.Mod.DefineType(path, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Serializable, typeof(object));
				}
				else
				{
					tb = prev.DefineNestedType(parts[i], TypeAttributes.NestedPublic | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Serializable, typeof(object));
				}
				Emit(tb);
				builders.Add(tb);
				paths.Add(path);
				prev = tb;
			}
			Type last = null;
			for (int i = 0; i < builders.Count; i++)
			{
				Type created = builders[i].CreateType();
				sa.Types[paths[i]] = created;
				_types[created] = true;
				Count++;
				last = created;
			}
			if (N.ULog.Enabled)
			{
				N.ULog.Info(Guard.Tag, "stub type " + fullName + " in stub assembly " + asm);
			}
			return last;
		}

		// "<ExType>: <first line of Message>"
		internal static string ExLine(Exception e)
		{
			string m = e.Message == null ? string.Empty : e.Message.Split(new char[] { '\n' })[0].Trim();
			return e.GetType().Name + ": " + m;
		}

		// A stub is a plain [Serializable] type with a default constructor: Mono cannot load an emitted ISerializable type as
		// an array element or generic argument. Its data is taken by SgStubSurrogate.
		private static void Emit(TypeBuilder tb)
		{
			tb.DefineDefaultConstructor(MethodAttributes.Public);
		}

		internal static bool IsStubType(Type t)
		{
			return !object.ReferenceEquals(t, null) && _types.Count > 0 && _types.ContainsKey(t);
		}

		internal static bool IsStub(object o)
		{
			return o != null && _types.Count > 0 && _types.ContainsKey(o.GetType());
		}

		// The type is a stub or built from one (generic argument, array element).
		internal static bool Refs(Type t)
		{
			if (_types.Count == 0)
			{
				return false;
			}
			bool r;
			if (_refs.TryGetValue(t, out r))
			{
				return r;
			}
			r = _types.ContainsKey(t);
			if (!r)
			{
				if (t.IsArray)
				{
					r = Refs(t.GetElementType());
				}
				else if (t.IsGenericType)
				{
					Type[] args = t.GetGenericArguments();
					for (int i = 0; i < args.Length && !r; i++)
					{
						r = Refs(args[i]);
					}
				}
			}
			_refs[t] = r;
			return r;
		}

		internal static bool IsStubAsm(Assembly a)
		{
			if (object.ReferenceEquals(a, null))
			{
				return false;
			}
			if (a is AssemblyBuilder)
			{
				return true;
			}
			foreach (StubAsm sa in _asms.Values)
			{
				if (object.ReferenceEquals(sa.Ab, a))
				{
					return true;
				}
			}
			return false;
		}
	}

	internal static class Guard
	{
		internal const string Tag = "saveguard";
		internal const string DayFile = "saveData170808";
		internal const string GlobalFile = "saveGlobal170808";
		private const int MaxDepth = 64;
		private const int MaxLines = 50;

		internal static bool ProtectOn;
		internal static bool SafeLoadOn;
		internal static N.IntParam Backups;
		internal static bool Ready;

		internal static bool DayOpen;
		internal static bool DayFailReported;
		internal static int DaySnap = -1;
		internal static int DaySnapDay = -1;
		private static int _equipSnap = -1;
		private static int _observeSnap = -1;
		private static DateTime _glerrAtRead = DateTime.MinValue;
		private static DateTime _sessionStart;

		internal static readonly Dictionary<string, string> ReadFailed = new Dictionary<string, string>();
		private static readonly HashSet<string> _once = new HashSet<string>();
		internal static readonly HashSet<string> Reported = new HashSet<string>();
		internal static readonly List<object> Built = new List<object>();
		private static readonly HashSet<object> _taken = new HashSet<object>();
		private static readonly HashSet<object> _absorbed = new HashSet<object>();
		private static readonly Dictionary<string, bool> _asmsLoaded = new Dictionary<string, bool>();
		private static readonly HashSet<string> _copyBad = new HashSet<string>();
		private static readonly List<StashEntry> _stash = new List<StashEntry>();
		private static readonly Dictionary<Type, string> _asmOfType = new Dictionary<Type, string>();

		private static string _backupDir;
		private static string _stashDir;
		private static string _indexPath;
		private static string _glerror;
		private static HashSet<string> _loadedMods;

		// Ready: the folders are known (backups, stash). Read checks and write protection work without them.
		internal static void Init()
		{
			_sessionStart = DateTime.Now;
			try
			{
				_glerror = Application.dataPath + "/BaseMods/Glerror.txt";
				_backupDir = Path.Combine(N.UiHost.RootDir, "SaveBackups");
				_stashDir = Path.Combine(N.UiHost.RootDir, "SaveStash");
				_indexPath = Path.Combine(_stashDir, "stash.txt");
				Ready = true;
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "no mod folder, backups and stash off: " + e.Message);
			}
			if (!Ready)
			{
				return;
			}
			try
			{
				LoadIndex();
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "stash index unreadable, restore off this session: " + e.Message);
			}
		}

		// ---------- read ----------

		// Same steps as SaveUtil.ReadSerializableFile (game source), with the binder and the checks after a good read.
		internal static Dictionary<string, object> Read(string fileName)
		{
			SgBinder binder = new SgBinder(SafeLoadOn);
			ResetRun();
			string source = null;
			FileStream fileStream = null;
			Dictionary<string, object> dictionary = null;
			try
			{
				if (File.Exists(fileName + ".val.txt"))
				{
					FileStream fileStream2 = null;
					try
					{
						fileStream2 = File.Open(fileName + ".val.txt", FileMode.Open);
						if (fileStream2.ReadByte() == 64)
						{
							fileStream = File.Open(fileName + "._temp", FileMode.Open);
							dictionary = Deserialize(fileStream, binder);
							fileStream.Close();
							Debug.LogError("Read done 1 " + fileName);
							fileStream2.Close();
							source = fileName + "._temp";
						}
						else
						{
							fileStream2.Close();
						}
					}
					catch (Exception message)
					{
						if (fileStream != null)
						{
							fileStream.Close();
						}
						fileStream = null;
						if (fileStream2 != null)
						{
							fileStream2.Close();
						}
						fileStream2 = null;
						Debug.LogError(message);
						dictionary = null;
						source = null;
					}
				}
				if (source == null)
				{
					fileStream = File.Open(fileName, FileMode.Open);
					try
					{
						dictionary = Deserialize(fileStream, binder);
						fileStream.Close();
						source = fileName;
					}
					catch (Exception ex)
					{
						Debug.LogError(ex);
						if (!File.Exists(fileName + ".backup"))
						{
							throw;
						}
						fileStream.Close();
						fileStream = File.Open(fileName + ".backup", FileMode.Open);
						dictionary = Deserialize(fileStream, binder);
						fileStream.Close();
						fileStream = null;
						File.Copy(fileName, fileName + UnityEngine.Random.Range(10000, 99999) + ".error_back", true);
						File.Copy(fileName + ".backup", fileName, true);
						source = fileName;
					}
				}
				if (binder.Map && Stubs.Count > 0)
				{
					// Throws when a stub cannot be removed by key: the read then fails as without SafeLoad.
					Strip(fileName, source, dictionary, binder);
				}
			}
			catch (Exception ex2)
			{
				if (fileStream != null)
				{
					fileStream.Close();
				}
				OnReadFailed(fileName, binder, ex2);
				Debug.LogError(ex2);
				FileReadException ex3 = new FileReadException(ex2);
				ex3.fileName = fileName;
				throw ex3;
			}
			AfterRead(fileName, dictionary);
			return dictionary;
		}

		private static Dictionary<string, object> Deserialize(FileStream fs, SgBinder binder)
		{
			if (!binder.Map)
			{
				return DeserializeOnce(fs, binder);
			}
			// SafeLoad: missing assemblies resolve to stub assemblies; each failed attempt may define more stub types.
			ResolveEventHandler handler = new ResolveEventHandler(Stubs.OnResolve);
			AppDomain.CurrentDomain.AssemblyResolve += handler;
			try
			{
				for (int attempt = 1; ; attempt++)
				{
					fs.Position = 0;
					ResetRun();
					int before = Stubs.Count;
					try
					{
						return DeserializeOnce(fs, binder);
					}
					catch (Exception e)
					{
						if (attempt >= Stubs.MaxAttempts)
						{
							throw;
						}
						Exception shown = e;
						for (int i = 0; i < 5 && shown != null; i++)
						{
							N.ULog.Info(Tag, "safeload attempt " + attempt + " " + Stubs.ExLine(shown));
							shown = shown.InnerException;
						}
						Stubs.FromException(e, binder);
						if (Stubs.Count == before)
						{
							throw;
						}
						N.ULog.Info(Tag, "safeload retry " + attempt + " " + Path.GetFileName(fs.Name) + " stubs=" + Stubs.Count);
					}
				}
			}
			finally
			{
				AppDomain.CurrentDomain.AssemblyResolve -= handler;
			}
		}

		private static Dictionary<string, object> DeserializeOnce(FileStream fs, SgBinder binder)
		{
			BinaryFormatter bf = new BinaryFormatter();
			bf.Binder = binder;
			if (binder.Map)
			{
				bf.SurrogateSelector = new SgSurrogates();
			}
			return (Dictionary<string, object>)bf.Deserialize(fs);
		}

		private static void ResetRun()
		{
			Built.Clear();
			_taken.Clear();
			_absorbed.Clear();
			Stubs.Use = 0;
		}

		// SetObjectData of the stub surrogate: registers the instance and absorbs its data.
		internal static void StubBuilt(object self, SerializationInfo info)
		{
			Built.Add(self);
			SerializationInfoEnumerator en = info.GetEnumerator();
			while (en.MoveNext())
			{
				Absorb(en.Value, 0);
			}
		}

		private static void OnReadFailed(string fileName, SgBinder binder, Exception e)
		{
			try
			{
				// A file that does not exist is the game's normal "no save" path, not a failed load.
				if (!File.Exists(fileName))
				{
					return;
				}
				string fb = FileBase(fileName);
				string why;
				if (binder.MissingAsm.Count > 0)
				{
					why = "missing " + binder.Joined() + " (type " + binder.FirstType + ")";
				}
				else
				{
					why = "reason=" + e.GetType().Name + ": " + e.Message;
				}
				ReadFailed[fb] = why;
				N.ULog.Error(Tag, "read failed " + fb + " " + why + (SafeLoadOn ? string.Empty : "; SaveGuard.SafeLoad can load it without that data"));
			}
			catch (Exception x)
			{
				N.ULog.Error(Tag, "read failure log " + x.Message);
			}
		}

		private static void AfterRead(string fileName, Dictionary<string, object> dic)
		{
			string fb = FileBase(fileName);
			try
			{
				ReadFailed.Remove(fb);
				if (_stash.Count > 0)
				{
					Restore(fb, dic);
				}
				if (fb == GlobalFile)
				{
					_glerrAtRead = (_glerror != null && File.Exists(_glerror)) ? File.GetLastWriteTime(_glerror) : DateTime.MinValue;
					HashSet<string> mods = LoadedMods();
					if (mods != null)
					{
						_equipSnap = CountEquips(dic, mods);
						_observeSnap = CountObserve(dic, mods);
						ReportGlobal(dic, mods);
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "after read " + fb + " " + e);
			}
		}

		// ---------- SafeLoad: strip, stash, restore ----------

		private static void Strip(string fileName, string source, Dictionary<string, object> dic, SgBinder binder)
		{
			string fb = FileBase(fileName);
			List<string> removed = new List<string>();
			List<string> unresolved = new List<string>();
			StripNode(dic, string.Empty, 0, removed, unresolved);
			int loose = 0;
			for (int i = 0; i < Built.Count; i++)
			{
				if (!_taken.Contains(Built[i]) && !_absorbed.Contains(Built[i]))
				{
					loose++;
				}
			}
			if (loose > 0)
			{
				unresolved.Add("/(" + loose + " inside other objects)");
			}
			if (unresolved.Count > 0)
			{
				for (int i = 0; i < unresolved.Count && i < MaxLines; i++)
				{
					N.ULog.Error(Tag, "unresolved " + fb + " " + Show(unresolved[i]) + " (" + binder.Joined() + ")");
				}
				throw new SerializationException("SaveGuard: data of " + binder.Joined() + " sits where it cannot be taken out");
			}
			string asms = binder.Joined();
			List<string> fresh = new List<string>();
			for (int i = 0; i < removed.Count; i++)
			{
				// A value under a list element has no stable key path: removed, not stashed.
				if (removed[i].IndexOf("/[", StringComparison.Ordinal) >= 0)
				{
					Once("WARN stripped " + fb + " " + Show(removed[i]) + " (" + asms + "); inside a list, cannot be restored");
					continue;
				}
				if (FindEntry(fb, removed[i]) == null)
				{
					if (fresh.Count < MaxLines)
					{
						N.ULog.Warn(Tag, "stripped " + fb + " " + Show(removed[i]) + " (" + asms + ")");
					}
					fresh.Add(removed[i]);
				}
			}
			if (removed.Count > 0)
			{
				Once("WARN safeload " + fb + " removed=" + removed.Count + "; the next save writes the file without them");
			}
			if (fresh.Count == 0 || !Ready)
			{
				return;
			}
			try
			{
				Directory.CreateDirectory(_stashDir);
				string copy = fb + "_" + Stamp() + ".dat";
				File.Copy(source, Path.Combine(_stashDir, copy), true);
				for (int i = 0; i < fresh.Count; i++)
				{
					StashEntry e = new StashEntry();
					e.File = fb;
					e.Copy = copy;
					e.Path = fresh[i];
					e.Asms = asms;
					_stash.Add(e);
				}
				SaveIndex();
				N.ULog.Info(Tag, "stash " + fb + " -> SaveStash/" + copy + " entries=" + fresh.Count);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "stash " + fb + " failed: " + e.Message);
			}
		}

		private static void StripNode(object node, string path, int depth, List<string> removed, List<string> unresolved)
		{
			if (node == null)
			{
				return;
			}
			if (depth > MaxDepth)
			{
				unresolved.Add(path + "/...");
				return;
			}
			IDictionary d = node as IDictionary;
			if (d != null)
			{
				List<object> drop = null;
				foreach (DictionaryEntry e in d)
				{
					string seg = Seg(e.Key);
					if (RefsStub(e.Key))
					{
						unresolved.Add(path + "/?");
					}
					else if (RefsStub(e.Value))
					{
						if (seg == null)
						{
							unresolved.Add(path + "/?");
						}
						else
						{
							MarkTaken(e.Value, 0);
							if (drop == null)
							{
								drop = new List<object>();
							}
							drop.Add(e.Key);
							removed.Add(path + "/" + seg);
						}
					}
					else
					{
						StripNode(e.Value, path + "/" + (seg != null ? seg : "?"), depth + 1, removed, unresolved);
					}
				}
				if (drop != null)
				{
					for (int i = 0; i < drop.Count; i++)
					{
						d.Remove(drop[i]);
					}
				}
				return;
			}
			IList l = node as IList;
			if (l != null && !IsLeafList(node.GetType()))
			{
				for (int i = 0; i < l.Count; i++)
				{
					object v = l[i];
					if (RefsStub(v))
					{
						unresolved.Add(path + "/[" + i + "]");
					}
					else
					{
						StripNode(v, path + "/[" + i + "]", depth + 1, removed, unresolved);
					}
				}
			}
		}

		// The object is a stub or its runtime type (generic arguments, array element) comes from a stub assembly.
		private static bool RefsStub(object v)
		{
			return v != null && Stubs.Refs(v.GetType());
		}

		private static void MarkTaken(object v, int depth)
		{
			if (v == null || depth > MaxDepth)
			{
				return;
			}
			if (Stubs.IsStub(v))
			{
				_taken.Add(v);
				return;
			}
			IDictionary d = v as IDictionary;
			if (d != null)
			{
				foreach (DictionaryEntry e in d)
				{
					MarkTaken(e.Key, depth + 1);
					MarkTaken(e.Value, depth + 1);
				}
				return;
			}
			IList l = v as IList;
			if (l != null && !IsLeafList(v.GetType()))
			{
				for (int i = 0; i < l.Count; i++)
				{
					MarkTaken(l[i], depth + 1);
				}
			}
		}

		// Puts back stashed values whose mod is loaded again; an entry is dropped once the file holds the key.
		private static void Restore(string fb, Dictionary<string, object> dic)
		{
			Dictionary<string, Dictionary<string, object>> copies = null;
			bool changed = false;
			for (int i = _stash.Count - 1; i >= 0; i--)
			{
				StashEntry e = _stash[i];
				if (e.File != fb || _copyBad.Contains(e.Copy) || !AnyLoaded(e.Asms))
				{
					continue;
				}
				try
				{
					object key;
					IDictionary parent = Parent(dic, e.Path, out key);
					if (parent == null)
					{
						Once("restore pending " + fb + " " + Show(e.Path) + ": parent not in the file");
						continue;
					}
					if (parent.Contains(key))
					{
						_stash.RemoveAt(i);
						changed = true;
						N.ULog.Info(Tag, "stash entry done " + fb + " " + Show(e.Path));
						continue;
					}
					if (copies == null)
					{
						copies = new Dictionary<string, Dictionary<string, object>>();
					}
					Dictionary<string, object> src;
					if (!copies.TryGetValue(e.Copy, out src))
					{
						src = ReadStash(e.Copy);
						copies[e.Copy] = src;
					}
					if (src == null)
					{
						_copyBad.Add(e.Copy);
						continue;
					}
					object srcKey;
					IDictionary srcParent = Parent(src, e.Path, out srcKey);
					if (srcParent == null || !srcParent.Contains(srcKey))
					{
						continue;
					}
					object value = srcParent[srcKey];
					if (HasMissing(value, 0))
					{
						continue;
					}
					parent.Add(key, value);
					Once("INFO restored " + fb + " " + Show(e.Path) + " (" + e.Asms + ")");
				}
				catch (Exception x)
				{
					Once("restore " + fb + " " + Show(e.Path) + " failed: " + x.GetType().Name + ": " + x.Message);
				}
			}
			if (changed)
			{
				SaveIndex();
				DeleteUnusedCopies();
			}
		}

		private static Dictionary<string, object> ReadStash(string copy)
		{
			string path = Path.Combine(_stashDir, copy);
			if (!File.Exists(path))
			{
				Once("stash copy missing: SaveStash/" + copy);
				return null;
			}
			FileStream fs = null;
			try
			{
				fs = File.Open(path, FileMode.Open, FileAccess.Read);
				// No placeholders here: the copy is used only when every type in it resolves again.
				return Deserialize(fs, new SgBinder(false));
			}
			catch (Exception e)
			{
				Once("INFO stash copy SaveStash/" + copy + " waits for its mods: " + e.GetType().Name);
				return null;
			}
			finally
			{
				if (fs != null)
				{
					fs.Close();
				}
			}
		}

		private static void DeleteUnusedCopies()
		{
			try
			{
				if (!Directory.Exists(_stashDir))
				{
					return;
				}
				string[] files = Directory.GetFiles(_stashDir, "*.dat");
				for (int i = 0; i < files.Length; i++)
				{
					string name = Path.GetFileName(files[i]);
					bool used = false;
					for (int j = 0; j < _stash.Count; j++)
					{
						if (_stash[j].Copy == name)
						{
							used = true;
							break;
						}
					}
					if (!used)
					{
						File.Delete(files[i]);
						N.ULog.Info(Tag, "stash copy deleted SaveStash/" + name);
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "stash cleanup " + e.Message);
			}
		}

		private static StashEntry FindEntry(string fb, string path)
		{
			for (int i = 0; i < _stash.Count; i++)
			{
				if (_stash[i].File == fb && _stash[i].Path == path)
				{
					return _stash[i];
				}
			}
			return null;
		}

		private static void LoadIndex()
		{
			if (!File.Exists(_indexPath))
			{
				return;
			}
			string[] lines = File.ReadAllLines(_indexPath);
			for (int i = 0; i < lines.Length; i++)
			{
				string[] f = lines[i].Split('\t');
				if (f.Length < 4)
				{
					continue;
				}
				StashEntry e = new StashEntry();
				e.File = f[0];
				e.Copy = f[1];
				e.Path = f[2];
				e.Asms = f[3];
				_stash.Add(e);
			}
			if (_stash.Count > 0)
			{
				N.ULog.Info(Tag, "stash entries=" + _stash.Count);
			}
		}

		// Temp file first, then replace: a failed write never leaves an empty index.
		private static void SaveIndex()
		{
			try
			{
				Directory.CreateDirectory(_stashDir);
				StringBuilder sb = new StringBuilder();
				for (int i = 0; i < _stash.Count; i++)
				{
					StashEntry e = _stash[i];
					sb.Append(e.File).Append('\t').Append(e.Copy).Append('\t').Append(e.Path).Append('\t').Append(e.Asms).Append("\r\n");
				}
				string tmp = _indexPath + ".tmp";
				File.WriteAllText(tmp, sb.ToString());
				if (File.Exists(_indexPath))
				{
					File.Delete(_indexPath);
				}
				File.Move(tmp, _indexPath);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "stash index write failed: " + e.Message);
			}
		}

		// ---------- write ----------

		internal static bool BeforeWrite(string fileName, Dictionary<string, object> dic)
		{
			string fb = FileBase(fileName);
			try
			{
				if (HasMissing(dic, 0))
				{
					Once("ERROR write blocked " + fb + ": the data still holds a SaveGuard stub; nothing was written");
					return false;
				}
				LogDepends(fb, dic);
				if (!File.Exists(fileName))
				{
					// Nothing on disk to protect (new game, deleted save): stale marks do not apply.
					ReadFailed.Remove(fb);
					if (fb == DayFile)
					{
						DayOpen = false;
					}
				}
				else if (ProtectOn)
				{
					string reason = UnsafeReason(fb);
					if (reason != null)
					{
						// Every blocked write is logged: progress of this session is not being saved.
						N.ULog.Error(Tag, "write blocked " + fb + ": " + reason + " Nothing was written; SaveGuard.Protect=false saves anyway and the unloaded data is lost.");
						return false;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "write check " + fb + " " + e);
			}
			string what = null;
			try
			{
				what = Shrink(fb, dic);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "shrink check " + fb + " " + e);
			}
			try
			{
				Backup(fileName, fb, what);
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "backup " + fb + " " + e.Message);
			}
			return true;
		}

		private static string UnsafeReason(string fb)
		{
			string why;
			if (ReadFailed.TryGetValue(fb, out why))
			{
				return "its last load failed (" + why + "). Install that mod or turn on SaveGuard.SafeLoad, then load again.";
			}
			if (fb == DayFile && DayOpen)
			{
				return "the day load did not finish (an error stopped GlobalGameManager.LoadDay; see the error before this line). Fix or remove the mod that throws, then load again.";
			}
			if (fb == GlobalFile && _glerror != null && File.Exists(_glerror))
			{
				DateTime t = File.GetLastWriteTime(_glerror);
				if (t > _sessionStart && t > _glerrAtRead)
				{
					return "the global load stopped part way: " + FirstLine(_glerror) + " (BaseMods/Glerror.txt). Fix or remove the mod named there, then load again.";
				}
			}
			return null;
		}

		private static void Backup(string fileName, string fb, string shrink)
		{
			if (!Ready || !File.Exists(fileName))
			{
				return;
			}
			string dir = Path.Combine(_backupDir, fb);
			if (shrink != null)
			{
				string pinDir = Path.Combine(dir, "pinned");
				Directory.CreateDirectory(pinDir);
				string pin = Stamp() + ".dat";
				File.Copy(fileName, Path.Combine(pinDir, pin), true);
				N.ULog.Error(Tag, "shrink " + fb + " " + shrink + " pinned=SaveBackups/" + fb + "/pinned/" + pin);
			}
			int keep = Backups.Value;
			if (keep <= 0)
			{
				return;
			}
			Directory.CreateDirectory(dir);
			string name = Stamp() + ".dat";
			File.Copy(fileName, Path.Combine(dir, name), true);
			string[] files = Directory.GetFiles(dir, "*.dat");
			Array.Sort(files, StringComparer.Ordinal);
			int del = files.Length - keep;
			for (int i = 0; i < del; i++)
			{
				File.Delete(files[i]);
			}
			N.ULog.Info(Tag, "backup " + fb + " -> SaveBackups/" + fb + "/" + name + " kept=" + Math.Min(files.Length, keep));
		}

		// Returns "<what> <old>-><new>" when the written data holds fewer entries than the last read or write.
		private static string Shrink(string fb, Dictionary<string, object> dic)
		{
			HashSet<string> mods = LoadedMods();
			if (mods == null)
			{
				return null;
			}
			string what = null;
			if (fb == DayFile)
			{
				int day = -1;
				object o;
				if (dic.TryGetValue("lastDay", out o) && o is int)
				{
					day = (int)o;
				}
				IDictionary dayData = DayEntry(dic, day);
				if (dayData == null)
				{
					return null;
				}
				int n = CountCreatures(dayData, mods);
				// A lower day is a new game or a checkpoint load: no comparison.
				if (DaySnap >= 0 && day >= DaySnapDay && n < DaySnap)
				{
					what = "creatures " + DaySnap + "->" + n;
				}
				DaySnap = n;
				DaySnapDay = day;
			}
			else if (fb == GlobalFile)
			{
				int eq = CountEquips(dic, mods);
				int ob = CountObserve(dic, mods);
				if (_equipSnap >= 0 && eq < _equipSnap)
				{
					what = "equips " + _equipSnap + "->" + eq;
				}
				if (_observeSnap >= 0 && ob < _observeSnap)
				{
					what = (what == null ? string.Empty : what + ", ") + "observe " + _observeSnap + "->" + ob;
				}
				_equipSnap = eq;
				_observeSnap = ob;
			}
			return what;
		}

		private static void LogDepends(string fb, Dictionary<string, object> dic)
		{
			HashSet<string> found = new HashSet<string>();
			CollectAssemblies(dic, 0, found);
			foreach (string a in found)
			{
				Once("INFO " + fb + " depends on " + a + " (removing that mod makes this file unreadable without SaveGuard.SafeLoad)");
			}
		}

		private static void CollectAssemblies(object node, int depth, HashSet<string> found)
		{
			if (node == null || depth > MaxDepth)
			{
				return;
			}
			string asm = ForeignAssembly(node.GetType());
			if (asm != null)
			{
				found.Add(asm);
			}
			IDictionary d = node as IDictionary;
			if (d != null)
			{
				foreach (DictionaryEntry e in d)
				{
					CollectAssemblies(e.Key, depth + 1, found);
					CollectAssemblies(e.Value, depth + 1, found);
				}
				return;
			}
			IList l = node as IList;
			if (l != null && !IsLeafList(node.GetType()))
			{
				for (int i = 0; i < l.Count; i++)
				{
					CollectAssemblies(l[i], depth + 1, found);
				}
			}
		}

		// Simple name of the first assembly of the type (or of a generic argument) that is not the game's or the runtime's.
		private static string ForeignAssembly(Type t)
		{
			string cached;
			if (_asmOfType.TryGetValue(t, out cached))
			{
				return cached;
			}
			string result = null;
			if (Stubs.IsStubAsm(t.Assembly))
			{
				// Stub assemblies are not real mods.
				_asmOfType[t] = null;
				return null;
			}
			string name = t.Assembly.GetName().Name;
			if (!IsGameAssembly(name))
			{
				result = name;
			}
			else if (t.IsGenericType)
			{
				Type[] args = t.GetGenericArguments();
				for (int i = 0; i < args.Length && result == null; i++)
				{
					result = ForeignAssembly(args[i]);
				}
			}
			else if (t.IsArray)
			{
				result = ForeignAssembly(t.GetElementType());
			}
			_asmOfType[t] = result;
			return result;
		}

		internal static bool IsGameAssembly(string name)
		{
			return name == "mscorlib" || name == "Assembly-CSharp" || name == "Assembly-CSharp-firstpass"
				|| name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("UnityEngine", StringComparison.Ordinal);
		}

		// ---------- day load ----------

		internal static void OnLoadDay(Dictionary<string, object> data)
		{
			HashSet<string> mods = LoadedMods();
			if (data == null || mods == null)
			{
				return;
			}
			int day = -1;
			object o;
			if (data.TryGetValue("day", out o) && o is int)
			{
				day = (int)o;
			}
			DaySnap = CountCreatures(data, mods);
			DaySnapDay = day;
			IList list = CreatureList(data);
			if (list == null)
			{
				return;
			}
			int missing = 0;
			for (int i = 0; i < list.Count; i++)
			{
				IDictionary c = list[i] as IDictionary;
				string modid = c == null ? null : Str(c, "modid");
				if (string.IsNullOrEmpty(modid) || mods.Contains(modid))
				{
					continue;
				}
				missing++;
				if (missing <= MaxLines)
				{
					object id = c.Contains("metadataId") ? c["metadataId"] : null;
					Report("WARN missing creature modid=" + modid + " id=" + id + " day=" + day);
				}
			}
			if (missing > 0)
			{
				Report("INFO content report " + DayFile + " day=" + day + " creatures=" + missing + " (mods not loaded; the game drops them on load)");
			}
		}

		private static void ReportGlobal(Dictionary<string, object> dic, HashSet<string> mods)
		{
			int obs = 0;
			IDictionary observe = Child(dic, "observe");
			IDictionary obsMod = observe == null ? null : Child(observe, "observeListMod");
			if (obsMod != null)
			{
				foreach (DictionaryEntry e in obsMod)
				{
					string modid = e.Key as string;
					IDictionary entries = e.Value as IDictionary;
					if (string.IsNullOrEmpty(modid) || mods.Contains(modid) || entries == null)
					{
						continue;
					}
					obs += entries.Count;
					Report("WARN missing observe modid=" + modid + " count=" + entries.Count);
				}
			}
			int eq = 0;
			IDictionary inv = Child(dic, "inventory");
			IList equipsMod = inv == null ? null : inv["equipsMod"] as IList;
			if (equipsMod != null)
			{
				Dictionary<string, int> per = new Dictionary<string, int>();
				for (int i = 0; i < equipsMod.Count; i++)
				{
					string modid = equipsMod[i] as string;
					if (string.IsNullOrEmpty(modid) || mods.Contains(modid))
					{
						continue;
					}
					int c;
					per.TryGetValue(modid, out c);
					per[modid] = c + 1;
					eq++;
				}
				foreach (KeyValuePair<string, int> kv in per)
				{
					Report("WARN missing equipment modid=" + kv.Key + " count=" + kv.Value);
				}
			}
			if (obs + eq > 0)
			{
				Report("INFO content report " + GlobalFile + " observe=" + obs + " equipment=" + eq + " (mods not loaded; the game drops them on load)");
			}
		}

		// ---------- counts ----------

		private static IDictionary DayEntry(Dictionary<string, object> dic, int day)
		{
			object o;
			if (!dic.TryGetValue("dayList", out o))
			{
				return null;
			}
			IDictionary list = o as IDictionary;
			if (list == null || !list.Contains(day))
			{
				return null;
			}
			return list[day] as IDictionary;
		}

		private static IList CreatureList(IDictionary dayData)
		{
			IDictionary creatures = Child(dayData, "creatures");
			return creatures == null ? null : creatures["creatureList"] as IList;
		}

		private static int CountCreatures(IDictionary dayData, HashSet<string> mods)
		{
			IList list = CreatureList(dayData);
			if (list == null)
			{
				return -1;
			}
			int n = 0;
			for (int i = 0; i < list.Count; i++)
			{
				IDictionary c = list[i] as IDictionary;
				string modid = c == null ? null : Str(c, "modid");
				if (string.IsNullOrEmpty(modid) || mods.Contains(modid))
				{
					n++;
				}
			}
			return n;
		}

		private static int CountEquips(IDictionary global, HashSet<string> mods)
		{
			IDictionary inv = Child(global, "inventory");
			IList equips = inv == null ? null : inv["equips"] as IList;
			if (equips == null)
			{
				return -1;
			}
			IList equipsMod = inv["equipsMod"] as IList;
			int n = 0;
			for (int i = 0; i < equips.Count; i++)
			{
				string modid = (equipsMod != null && i < equipsMod.Count) ? equipsMod[i] as string : null;
				if (string.IsNullOrEmpty(modid) || mods.Contains(modid))
				{
					n++;
				}
			}
			return n;
		}

		private static int CountObserve(IDictionary global, HashSet<string> mods)
		{
			IDictionary observe = Child(global, "observe");
			if (observe == null)
			{
				return -1;
			}
			int n = 0;
			IDictionary vanilla = Child(observe, "observeList");
			if (vanilla != null)
			{
				n += vanilla.Count;
			}
			IDictionary byMod = Child(observe, "observeListMod");
			if (byMod != null)
			{
				foreach (DictionaryEntry e in byMod)
				{
					string modid = e.Key as string;
					IDictionary entries = e.Value as IDictionary;
					if (entries != null && (string.IsNullOrEmpty(modid) || mods.Contains(modid)))
					{
						n += entries.Count;
					}
				}
			}
			return n;
		}

		// modids of loaded mods; null until BaseMod has a mod list (counts and the report wait for it).
		private static HashSet<string> LoadedMods()
		{
			if (_loadedMods != null)
			{
				return _loadedMods;
			}
			if (Add_On.instance == null || Add_On.instance.ModList == null || Add_On.instance.ModList.Count == 0)
			{
				return null;
			}
			HashSet<string> set = new HashSet<string>();
			foreach (ModInfo m in Add_On.instance.ModList)
			{
				if (m != null && !string.IsNullOrEmpty(m.modid))
				{
					set.Add(m.modid);
				}
			}
			_loadedMods = set;
			return set;
		}

		// ---------- helpers ----------

		internal static string SimpleName(string assemblyName)
		{
			int c = assemblyName.IndexOf(',');
			return (c < 0 ? assemblyName : assemblyName.Substring(0, c)).Trim();
		}

		// A loaded assembly of that simple name, else one the runtime can load from the game folder, else null.
		internal static Assembly FindAssembly(string simple)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				if (Stubs.IsStubAsm(all[i]))
				{
					continue;
				}
				if (all[i].GetName().Name == simple)
				{
					return all[i];
				}
			}
			// Probing: the stub AssemblyResolve handler must not answer this load.
			bool was = Stubs.Probing;
			Stubs.Probing = true;
			try
			{
				Assembly a = Assembly.Load(simple);
				return Stubs.IsStubAsm(a) ? null : a;
			}
			catch (Exception)
			{
				return null;
			}
			finally
			{
				Stubs.Probing = was;
			}
		}

		private static bool AnyLoaded(string asms)
		{
			bool cached;
			if (_asmsLoaded.TryGetValue(asms, out cached))
			{
				return cached;
			}
			cached = AnyLoadedNow(asms);
			_asmsLoaded[asms] = cached;
			return cached;
		}

		private static bool AnyLoadedNow(string asms)
		{
			string[] names = asms.Split('|');
			for (int i = 0; i < names.Length; i++)
			{
				string simple = names[i];
				int colon = simple.IndexOf(':');
				if (colon >= 0)
				{
					simple = simple.Substring(0, colon);
				}
				if (simple.Length > 0 && !object.ReferenceEquals(FindAssembly(simple), null))
				{
					return true;
				}
			}
			return false;
		}

		private static bool HasMissing(object node, int depth)
		{
			if (node == null)
			{
				return false;
			}
			if (Stubs.Refs(node.GetType()))
			{
				return true;
			}
			if (depth > MaxDepth)
			{
				return false;
			}
			IDictionary d = node as IDictionary;
			if (d != null)
			{
				foreach (DictionaryEntry e in d)
				{
					if (HasMissing(e.Key, depth + 1) || HasMissing(e.Value, depth + 1))
					{
						return true;
					}
				}
				return false;
			}
			IList l = node as IList;
			if (l != null && !IsLeafList(node.GetType()))
			{
				for (int i = 0; i < l.Count; i++)
				{
					if (HasMissing(l[i], depth + 1))
					{
						return true;
					}
				}
			}
			return false;
		}

		// Lists and arrays of primitives and strings hold no nested data.
		private static bool IsLeafList(Type t)
		{
			Type e = null;
			if (t.IsArray)
			{
				e = t.GetElementType();
			}
			else if (t.IsGenericType)
			{
				Type[] args = t.GetGenericArguments();
				if (args.Length == 1)
				{
					e = args[0];
				}
			}
			return !object.ReferenceEquals(e, null) && (e.IsPrimitive || object.ReferenceEquals(e, typeof(string)));
		}

		private static IDictionary Child(IDictionary d, string key)
		{
			return d.Contains(key) ? d[key] as IDictionary : null;
		}

		private static string Str(IDictionary d, string key)
		{
			return d.Contains(key) ? d[key] as string : null;
		}

		// Path segment of a dictionary key: s:<string>, i:<int>, l:<long>; other key types have no path.
		private static string Seg(object key)
		{
			if (key is string)
			{
				return "s:" + Escape((string)key);
			}
			if (key is int)
			{
				return "i:" + ((int)key).ToString(CultureInfo.InvariantCulture);
			}
			if (key is long)
			{
				return "l:" + ((long)key).ToString(CultureInfo.InvariantCulture);
			}
			return null;
		}

		private static IDictionary Parent(IDictionary root, string path, out object key)
		{
			key = null;
			string[] segs = path.Split('/');
			IDictionary cur = root;
			object k = null;
			int last = -1;
			for (int i = 0; i < segs.Length; i++)
			{
				if (segs[i].Length > 0)
				{
					last = i;
				}
			}
			for (int i = 0; i <= last; i++)
			{
				if (segs[i].Length == 0)
				{
					continue;
				}
				k = Key(segs[i]);
				if (k == null)
				{
					return null;
				}
				if (i == last)
				{
					key = k;
					return cur;
				}
				if (!cur.Contains(k))
				{
					return null;
				}
				cur = cur[k] as IDictionary;
				if (cur == null)
				{
					return null;
				}
			}
			return null;
		}

		private static object Key(string seg)
		{
			if (seg.Length < 2 || seg[1] != ':')
			{
				return null;
			}
			string v = seg.Substring(2);
			if (seg[0] == 's')
			{
				return Unescape(v);
			}
			if (seg[0] == 'i')
			{
				return int.Parse(v, CultureInfo.InvariantCulture);
			}
			if (seg[0] == 'l')
			{
				return long.Parse(v, CultureInfo.InvariantCulture);
			}
			return null;
		}

		private static string Escape(string s)
		{
			return s.Replace("%", "%25").Replace("/", "%2F").Replace("\t", "%09").Replace("\r", "%0D").Replace("\n", "%0A");
		}

		private static string Unescape(string s)
		{
			return s.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%09", "\t").Replace("%2F", "/").Replace("%25", "%");
		}

		// Readable path for the log: inventory/reinforcementData.
		private static string Show(string path)
		{
			string[] segs = path.Split('/');
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < segs.Length; i++)
			{
				if (segs[i].Length == 0)
				{
					continue;
				}
				if (sb.Length > 0)
				{
					sb.Append('/');
				}
				string s = segs[i];
				sb.Append(s.Length > 2 && s[1] == ':' ? Unescape(s.Substring(2)) : s);
			}
			return sb.ToString();
		}

		internal static string FileBase(string fileName)
		{
			return Path.GetFileNameWithoutExtension(fileName);
		}

		private static string Stamp()
		{
			return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
		}

		// One line per text and load (cleared in ReleaseGame).
		private static void Report(string line)
		{
			if (Reported.Add(line))
			{
				_once.Remove(line);
				Once(line);
			}
		}

		// Stubs inside the data of another stub.
		internal static void Absorb(object v, int depth)
		{
			if (v == null || depth > 8)
			{
				return;
			}
			if (Stubs.IsStub(v))
			{
				_absorbed.Add(v);
				return;
			}
			IDictionary d = v as IDictionary;
			if (d != null)
			{
				foreach (DictionaryEntry e in d)
				{
					Absorb(e.Key, depth + 1);
					Absorb(e.Value, depth + 1);
				}
				return;
			}
			IList l = v as IList;
			if (l != null && !IsLeafList(v.GetType()))
			{
				for (int i = 0; i < l.Count; i++)
				{
					Absorb(l[i], depth + 1);
				}
			}
		}

		private static string FirstLine(string path)
		{
			try
			{
				string[] lines = File.ReadAllLines(path);
				return lines.Length > 0 ? lines[0].Trim() : "(empty)";
			}
			catch (Exception)
			{
				return "(unreadable)";
			}
		}

		// One line per text and session. A leading "ERROR "/"WARN "/"INFO " picks the level (default WARN).
		private static void Once(string line)
		{
			if (!_once.Add(line))
			{
				return;
			}
			if (line.StartsWith("ERROR ", StringComparison.Ordinal))
			{
				N.ULog.Error(Tag, line.Substring(6));
			}
			else if (line.StartsWith("INFO ", StringComparison.Ordinal))
			{
				N.ULog.Info(Tag, line.Substring(5));
			}
			else if (line.StartsWith("WARN ", StringComparison.Ordinal))
			{
				N.ULog.Warn(Tag, line.Substring(5));
			}
			else
			{
				N.ULog.Warn(Tag, line);
			}
		}
	}
}
