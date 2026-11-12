// LoadGuard: NailOptimization module (api level 3). Keeps mod data loading alive when a mod file is broken.
// Harmony 1.0.9.1. No lock / ?. / interpolation. Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine.
// Output goes to <NailOptimization>/modules/.
//
// Every target below has a whole-method catch in vanilla/BaseMod (or is a callee of one). Harmony 1.0.9.1 runs a copy
// without exception clauses (GL-53), so each patch is a full replacement (Prefix, return false): the vanilla body is
// reproduced with the same lists, order and override rules, plus a try per mod file (equipment and creatures: per entry).
//   LocalizeTextDataLoader.LoadLocalizeTextData  per file; mod Localize and equipment xmls: language fallback
//   CreatureDataLoader.Load                       per CreatureGen / CreatureList file
//   CreatureDataLoader.Loading / Loading_Mod      per entry, list additions of a failed entry rolled back
//   CreatureDataLoader.LoadDoc                    mod CreatureInfo: language fallback current -> en -> any
//   EquipmentDataLoader.Load                      per file, per entry (own copy of the LoadEquips loop)
//   GameStaticDataLoader.LoadModIsolate           per file, replace per node
//   CreatureDataLoader.ReLoad                     per mod CreatureList/*.txt file (ReloadData, language change)
// Mod files are never changed; vanilla game files keep the vanilla outcome.
// LoadGuard.Repair (off by default): mod CreatureInfo non-numeric openLevel -> 0, mod EGO gift missing attachPos/attachType
// and non-integer bonus -> defaults; edits the parsed documents in memory only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Harmony;
using LobotomyBaseMod;
using UnityEngine;
using N = NailOptimization;

namespace LoadGuard
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class LoadGuardModule : N.ModuleBase
	{
		public const string Id = "LoadGuard";
		public const string HarmonyId = "Lobotomy.NailOptimization.LoadGuard";
		private const int Expected = 8;

		private static N.Flag _enabled;
		private static N.Flag _repair;

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
				return "1.7";
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
			_enabled = N.Config.BindFlag("LoadGuard.Enabled", true, "Broken mod data files (xml, localization, equipment, creatures, sefira isolate) are skipped one by one instead of breaking the whole loader; each skip is logged. Read at game start.");
			CreatureGuard.UseIndex = N.Config.BindFlag("LoadGuard.CreatureIndex", true, "With LoadGuard on: creature stat files are found through an index built once instead of reading the folders of all mods for each creature. Off = the old walk. Restart required.").On;
			_repair = N.Config.BindFlag("LoadGuard.Repair", false, "With LoadGuard on: broken mod creature openLevel values and EGO gift attachPos/attachType/bonus values are repaired in memory instead of skipping the entry; each repair is logged. Read at game start.");
			if (N.Config.BindFlag("LoadGuard.BootProfile", false, "Diagnostic: logs the time of every assembly load until the first frame, so the start-up time of each mod can be measured. Read at game start.").On)
			{
				BootProfile.Start();
			}
			if (N.Config.BindFlag("LoadGuard.BootFiles", true, "Faster start: the full BaseMods file list that some mods read in their start-up code (Train Ego Mod: about 3 s) is built on a worker thread while the other mods load and handed to them; the log checks it against a fresh list after start. Read at game start.").On)
			{
				BootFiles.Start(HarmonyInstance.Create(HarmonyId + ".bootfiles"));
			}
			if (N.Config.BindFlag("LoadGuard.Alerts", true, "A warning button on the title and in the day lists mod problems with the mod folder: code that failed to load or start, data files that had to be skipped, and mod patches or methods the guards skipped for the session. Read at game start.").On)
			{
				LoadAlerts.Start();
				BaseModCheck.Flag = N.Config.BindFlag("LoadGuard.BaseModCheck", true, "With the warning list on: a BaseMod (LMM) other than 1.3.9 is named in the list, which opens once on the title. Read at game start.");
				CrashTrail.Flag = N.Config.BindFlag("LoadGuard.CrashTrail", true, "With the warning list on: the last step before a game crash (mod being loaded, scene, day, last error) is kept in Logs/LastSession.txt and shown in the list at the next start. Read at game start.");
				if (CrashTrail.Flag.On)
				{
					CrashTrail.Start();
				}
			}
			if (!_enabled.On)
			{
				N.ULog.Info("loadguard", "disabled by LoadGuard.Enabled=false");
				return;
			}
			Guard.RepairOn = _repair.On;
			TypeLoad.Flag = N.Config.BindFlag("LoadGuard.TypeLoad", true, "With LoadGuard on: a mod DLL whose types cannot all load (missing or later-loaded dependency) loads partially instead of stopping the loading of every later mod; it is named in the log and the warning list. Works for mods after NailOptimization in the load order. Read at game start.");
			CreatureLoad.Flag = N.Config.BindFlag("LoadGuard.CreatureLoad", true, "With LoadGuard on: at day load an abnormality whose script class is missing or throws is skipped (or kept with an error) and named in the log and the warning list, instead of stopping the load. Read at game start.");
			if (Guard.RepairOn)
			{
				N.ULog.Info("loadguard", "repair on");
			}
			int n = Install();
			int expected = Expected + (TypeLoad.Flag.On ? 1 : 0) + (CreatureLoad.Flag.On ? 1 : 0);
			N.ULog.Info("loadguard", "hook installed " + n + "/" + expected);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int Install()
		{
			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			int n = 0;

			// First: every mod DLL after this point goes through Assembly.GetTypes in Add_On.Init.
			if (TypeLoad.Flag.On)
			{
				n += TypeLoad.Install(harmony);
			}

			if (Guard.Bind())
			{
				n += Patch(harmony, typeof(LocalizeTextDataLoader), "LoadLocalizeTextData", typeof(Guard), "PrefixLocalize");
			}
			if (CreatureGuard.Bind())
			{
				n += Patch(harmony, typeof(CreatureDataLoader), "Load", typeof(CreatureGuard), "PrefixLoad");
				n += Patch(harmony, typeof(CreatureDataLoader), "Loading", typeof(CreatureGuard), "PrefixLoading");
				n += Patch(harmony, typeof(CreatureDataLoader), "Loading_Mod", typeof(CreatureGuard), "PrefixLoadingMod");
				n += Patch(harmony, typeof(CreatureDataLoader), "ReLoad", typeof(CreatureGuard), "PrefixReLoad");
			}
			n += Patch(harmony, typeof(CreatureDataLoader), "LoadDoc", typeof(Guard), "PrefixLoadDoc");
			n += Patch(harmony, typeof(EquipmentDataLoader), "Load", typeof(Guard), "PrefixEquipment");
			n += Patch(harmony, typeof(GameStaticDataLoader), "LoadModIsolate", typeof(Guard), "PrefixIsolate");
			if (CreatureLoad.Flag.On && CreatureLoad.Bind())
			{
				int k = Patch(harmony, typeof(CreatureManager), "LoadData", typeof(CreatureLoad), "PrefixLoadData");
				if (k > 0)
				{
					// A foreign Prefix after ours or a Transpiler switches the replacement off (the original runs).
					MethodBase target = N.PatchUtil.Resolve(Guard.Tag, typeof(CreatureManager), "LoadData", null);
					N.PatchUtil.GuardLate(Guard.Tag, target, typeof(CreatureLoad).GetMethod("PrefixLoadData"), CreatureLoad.Flag, "LoadGuard.CreatureLoad");
				}
				n += k;
			}
			return n;
		}

		private static int Patch(HarmonyInstance harmony, Type type, string name, Type host, string prefix)
		{
			MethodBase target = N.PatchUtil.Resolve(Guard.Tag, type, name, null);
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			MethodInfo pre = host.GetMethod(prefix, BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(pre, null))
			{
				N.ULog.Error(Guard.Tag, "prefix not found: " + prefix);
				return 0;
			}
			// Last: other mods' Prefixes on these loaders still run before the replacement.
			HarmonyMethod hm = N.PatchUtil.WithPriority(pre, Priority.Last);
			return N.PatchUtil.Install(harmony, Guard.Tag, target, hm, null) ? 1 : 0;
		}
	}

	// Shared helpers and the localization, LoadDoc, equipment and isolate replacements.
	public static class Guard
	{
		internal const string Tag = "loadguard";

		private static MethodInfo _getLocalizeList;
		private static string _gameRoot;
		private static readonly HashSet<string> _fallbackSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static readonly string[] BonusNames = new string[] { "hp", "mental", "cubeSpeed", "workProb", "movement", "attackSpeed" };

		internal static int Files;
		internal static int Skipped;
		internal static int Repairs;
		internal static bool RepairOn;

		internal static bool Bind()
		{
			_getLocalizeList = typeof(LocalizeTextDataLoader).GetMethod("GetLocalizeList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (object.ReferenceEquals(_getLocalizeList, null))
			{
				N.ULog.Error(Tag, "LocalizeTextDataLoader.GetLocalizeList not found, localization not guarded");
				return false;
			}
			return true;
		}

		internal static void Begin()
		{
			Files = 0;
			Skipped = 0;
			Repairs = 0;
		}

		// Summary suffix for the loaders that repair (creatures, equipment); empty while LoadGuard.Repair is off.
		internal static string RepairText()
		{
			return RepairOn ? " repaired=" + Repairs : string.Empty;
		}

		internal static void Summary(string loader, string extra)
		{
			N.ULog.Info(Tag, "summary " + loader + " files=" + Files + " skipped=" + Skipped + extra);
		}

		internal static string Rel(string path)
		{
			try
			{
				if (_gameRoot == null)
				{
					_gameRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
				}
				string p = path.Replace('\\', '/');
				if (p.StartsWith(_gameRoot + "/", StringComparison.OrdinalIgnoreCase))
				{
					return p.Substring(_gameRoot.Length + 1);
				}
				return p;
			}
			catch (Exception)
			{
				return path;
			}
		}

		internal static string Reason(Exception e)
		{
			Exception x = e;
			while (x is TargetInvocationException && x.InnerException != null)
			{
				x = x.InnerException;
			}
			string m = x.Message;
			if (m == null)
			{
				m = string.Empty;
			}
			int nl = m.IndexOf('\n');
			if (nl >= 0)
			{
				m = m.Substring(0, nl);
			}
			return x.GetType().Name + ": " + m.Trim();
		}

		internal static void Skip(string path, string entry, Exception e)
		{
			Skipped++;
			string where = Rel(path);
			if (entry != null)
			{
				where = where + " entry=" + entry;
			}
			N.ULog.Warn(Tag, "skipped " + where + " reason=" + Reason(e));
			LoadAlerts.AddSkip(path, (entry != null ? Path.GetFileName(path) + " entry=" + entry : Path.GetFileName(path)) + ": " + Reason(e));
		}

		internal static void Repaired(string path, string entry, string field, string oldValue, string newValue)
		{
			Repairs++;
			string old = oldValue.Replace('\r', ' ').Replace('\n', ' ');
			if (old.Length > 40)
			{
				old = old.Substring(0, 40) + "...";
			}
			N.ULog.Warn(Tag, "repaired " + Rel(path) + " entry=" + entry + " field=" + field + " value=" + old + "->" + newValue);
		}

		// LoadGuard.Repair, mod EGO gift node before LoadEGOgift: missing attachPos/attachType get defaults, a bonus value
		// int.Parse would reject becomes 0. Same parse rules as vanilla (int.TryParse, current culture).
		private static void RepairGift(XmlNode equipNode, string path, string entry)
		{
			if (equipNode.SelectSingleNode("attachPos") == null)
			{
				AppendText(equipNode, "attachPos", string.Empty);
				Repaired(path, entry, "attachPos", "(missing)", "\"\"");
			}
			if (equipNode.SelectSingleNode("attachType") == null)
			{
				AppendText(equipNode, "attachType", "add");
				Repaired(path, entry, "attachType", "(missing)", "add");
			}
			XmlNode bonus = equipNode.SelectSingleNode("bonus");
			if (bonus == null)
			{
				return;
			}
			for (int i = 0; i < BonusNames.Length; i++)
			{
				XmlNode b = bonus.SelectSingleNode(BonusNames[i]);
				int v;
				if (b == null || int.TryParse(b.InnerText, out v))
				{
					continue;
				}
				string old = b.InnerText;
				b.InnerText = "0";
				Repaired(path, entry, "bonus/" + BonusNames[i], old, "0");
			}
		}

		private static void AppendText(XmlNode parent, string name, string text)
		{
			XmlElement e = parent.OwnerDocument.CreateElement(name);
			e.InnerText = text;
			parent.AppendChild(e);
		}

		// LoadGuard.Repair, mod CreatureInfo document before LoadCreatureTypeInfo: an openLevel float.Parse would reject
		// (collection items, desc, record, specialSkill desc) becomes 0. Same parse rules as vanilla (current culture).
		internal static void RepairInfo(XmlDocument doc, string path, string entry)
		{
			if (doc == null)
			{
				return;
			}
			// Only the places LoadCreatureTypeInfo parses (collection items, desc, record, specialTip).
			XmlNodeList nodes = doc.SelectNodes("/creature/observe/collection/*[@openLevel] | /creature/observe/desc[@openLevel] | /creature/observe/record[@openLevel] | /creature/observe/specialTipSize/specialTip[@openLevel]");
			for (int i = 0; i < nodes.Count; i++)
			{
				XmlAttribute a = nodes[i].Attributes["openLevel"];
				float f;
				if (a == null || float.TryParse(a.Value, out f))
				{
					continue;
				}
				string old = a.Value;
				a.Value = "0";
				Repaired(path, entry, "openLevel@" + nodes[i].Name, old, "0");
			}
		}

		internal static void Internal(string loader, Exception e)
		{
			N.ULog.Error(Tag, loader + " internal " + e);
		}

		// Language folder under root: current, else en, else the first existing one (ordinal). Null if none.
		internal static string PickLang(string root, string current, string what)
		{
			if (current != null && Directory.Exists(root + "/" + current))
			{
				return current;
			}
			string used = null;
			if (Directory.Exists(root + "/en"))
			{
				used = "en";
			}
			else
			{
				DirectoryInfo[] subs = new DirectoryInfo(root).GetDirectories();
				if (subs.Length == 0)
				{
					return null;
				}
				Array.Sort(subs, CompareDirName);
				used = subs[0].Name;
			}
			if (_fallbackSeen.Add(root + "|" + current + "|" + used))
			{
				N.ULog.Info(Tag, "fallback " + Rel(root) + " " + what + " lang=" + current + "->" + used);
			}
			return used;
		}

		private static int CompareDirName(DirectoryInfo a, DirectoryInfo b)
		{
			return string.CompareOrdinal(a.Name, b.Name);
		}

		// ---------- LocalizeTextDataLoader.LoadLocalizeTextData ----------

		public static bool PrefixLocalize(LocalizeTextDataLoader __instance)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			bool modPart = false;
			try
			{
				Begin();
				string lang = __instance._lang;
				string[] localizeList = (string[])_getLocalizeList.Invoke(__instance, null);
				for (int i = 0; i < localizeList.Length; i++)
				{
					string text = localizeList[i];
					XmlDocument xmlDocument = AssetLoader.LoadExternalXML("Language/Localize/" + lang + "/" + text + "_" + lang);
					if (xmlDocument == null)
					{
						xmlDocument = AssetLoader.LoadExternalXML("Language/Localize/en/" + text + "_en");
					}
					if (xmlDocument == null)
					{
						xmlDocument = AssetLoader.LoadExternalXML("Language/Localize/kr/" + text + "_kr");
					}
					if (xmlDocument == null)
					{
						continue;
					}
					foreach (KeyValuePair<string, string> item in __instance.LoadText(xmlDocument))
					{
						if (!dictionary.ContainsKey(item.Key))
						{
							dictionary.Add(item.Key, item.Value);
						}
					}
				}
				modPart = true;
				string current = GlobalGameManager.instance.GetCurrentLanguage();
				List<DirectoryInfo> dirs = Add_On.instance.DirList;
				for (int d = 0; d < dirs.Count; d++)
				{
					string root = dirs[d].FullName + "/Localize";
					if (!Directory.Exists(root))
					{
						continue;
					}
					string use = PickLang(root, current, "Localize");
					if (use == null)
					{
						continue;
					}
					CheckDirText(__instance, new DirectoryInfo(root + "/" + use), dictionary);
				}
				for (int d = 0; d < dirs.Count; d++)
				{
					DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(dirs[d], "Equipment");
					if (directoryInfo == null || !Directory.Exists(directoryInfo.FullName + "/xmls"))
					{
						continue;
					}
					string use = PickLang(directoryInfo.FullName + "/xmls", lang, "Equipment xmls");
					if (use == null)
					{
						continue;
					}
					FileInfo[] files = new DirectoryInfo(directoryInfo.FullName + "/xmls/" + use).GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						FileInfo fileInfo = files[f];
						if (!fileInfo.Name.Contains(".xml") && !fileInfo.Name.Contains(".txt"))
						{
							continue;
						}
						Files++;
						Dictionary<string, string> loaded;
						try
						{
							XmlDocument xmlDocument2 = new XmlDocument();
							xmlDocument2.LoadXml(File.ReadAllText(fileInfo.FullName));
							loaded = __instance.LoadText(xmlDocument2);
						}
						catch (Exception e)
						{
							Skip(fileInfo.FullName, null, e);
							continue;
						}
						foreach (KeyValuePair<string, string> item2 in loaded)
						{
							if (dictionary.ContainsKey(item2.Key))
							{
								dictionary.Remove(item2.Key);
							}
							dictionary.Add(item2.Key, item2.Value);
						}
					}
				}
				LocalizeTextDataModel.instance.Init(dictionary);
			}
			catch (Exception e)
			{
				Internal("localize", e);
				// Vanilla-files part has no catch in vanilla either: the original (copy without handlers) behaves the same.
				if (!modPart)
				{
					return true;
				}
				// Mod part: running the copy would lose its catch (GL-53). Install what was built.
				try
				{
					LocalizeTextDataModel.instance.Init(dictionary);
				}
				catch (Exception e2)
				{
					Internal("localize-init", e2);
				}
				return false;
			}
			Summary("localize", " texts=" + dictionary.Count);
			return false;
		}

		// Same walk as LocalizeTextDataLoader.CheckDirText (sub folders first, then every file), one try per file.
		private static void CheckDirText(LocalizeTextDataLoader loader, DirectoryInfo dir, Dictionary<string, string> dic)
		{
			DirectoryInfo[] directories = dir.GetDirectories();
			for (int i = 0; i < directories.Length; i++)
			{
				CheckDirText(loader, directories[i], dic);
			}
			FileInfo[] files = dir.GetFiles();
			for (int i = 0; i < files.Length; i++)
			{
				Files++;
				Dictionary<string, string> loaded;
				try
				{
					XmlDocument xmlDocument = new XmlDocument();
					xmlDocument.LoadXml(File.ReadAllText(files[i].FullName));
					loaded = loader.LoadText(xmlDocument);
				}
				catch (Exception e)
				{
					Skip(files[i].FullName, null, e);
					continue;
				}
				foreach (KeyValuePair<string, string> item in loaded)
				{
					dic[item.Key] = item.Value;
				}
			}
		}

		// ---------- CreatureDataLoader.LoadDoc ----------

		public static bool PrefixLoadDoc(CreatureDataLoader __instance, string src, string currentLn, bool IsMod, ref XmlDocument __result)
		{
			if (!IsMod)
			{
				XmlDocument doc = AssetLoader.LoadExternalXML(string.Format(__instance.documentSrcFormat, currentLn, src));
				if (doc == null)
				{
					doc = AssetLoader.LoadExternalXML(string.Format(__instance.documentSrcFormat, "en", src));
				}
				__result = doc;
				return false;
			}
			// Mod branch: the first DirList mod that has the file wins (vanilla), language current -> en -> any.
			XmlDocument xmlDocument = new XmlDocument();
			string path = FindCreatureInfo(src, currentLn, currentLn, false);
			if (path == null && currentLn != "en")
			{
				path = FindCreatureInfo(src, "en", currentLn, false);
			}
			if (path == null)
			{
				path = FindCreatureInfo(src, null, currentLn, true);
			}
			if (path != null)
			{
				xmlDocument.LoadXml(File.ReadAllText(path));
			}
			__result = xmlDocument;
			return false;
		}

		private sealed class InfoHit
		{
			internal string Path;
			internal string Root;
			internal string Lang;
		}

		private static Dictionary<string, InfoHit> _infoIndex;
		private static int _infoIndexDirs;

		// The first DirList mod with the name wins, as a walk over the mods would give. Key "lang|name" or "|name" for any language
		// (ordinal language order). Names have no extension: .xml and .txt count as one file.
		private static void BuildInfoIndex()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			List<DirectoryInfo> dirs = Add_On.instance.DirList;
			Dictionary<string, InfoHit> index = new Dictionary<string, InfoHit>(StringComparer.Ordinal);
			for (int d = 0; d < dirs.Count; d++)
			{
				DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(dirs[d], "Creature");
				if (directoryInfo == null || !Directory.Exists(directoryInfo.FullName + "/CreatureInfo"))
				{
					continue;
				}
				string root = directoryInfo.FullName + "/CreatureInfo";
				DirectoryInfo[] langs = new DirectoryInfo(root).GetDirectories();
				Array.Sort(langs, CompareDirName);
				for (int l = 0; l < langs.Length; l++)
				{
					FileInfo[] files = langs[l].GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						string name = files[f].Name;
						if (!name.EndsWith(".xml", StringComparison.Ordinal) && !name.EndsWith(".txt", StringComparison.Ordinal))
						{
							continue;
						}
						string stem = name.Substring(0, name.Length - 4);
						InfoHit hit = new InfoHit();
						hit.Path = files[f].FullName;
						hit.Root = root;
						hit.Lang = langs[l].Name;
						string key = langs[l].Name.ToLowerInvariant() + "|" + stem;
						if (!index.ContainsKey(key))
						{
							index[key] = hit;
						}
						if (!index.ContainsKey("|" + stem))
						{
							index["|" + stem] = hit;
						}
					}
				}
			}
			_infoIndex = index;
			_infoIndexDirs = dirs.Count;
			N.ULog.Info(Tag, "creature info index: " + index.Count + " keys, " + sw.ElapsedMilliseconds + " ms");
		}

		// lang == null and any: every language folder of each mod, ordinal order.
		private static string FindCreatureInfo(string src, string lang, string current, bool any)
		{
			if (_infoIndex == null || _infoIndexDirs != Add_On.instance.DirList.Count)
			{
				BuildInfoIndex();
			}
			InfoHit hit;
			if (!_infoIndex.TryGetValue((any ? string.Empty : lang.ToLowerInvariant()) + "|" + src, out hit))
			{
				return null;
			}
			if (hit.Lang != current && _fallbackSeen.Add(hit.Root + "|" + current + "|" + hit.Lang))
			{
				N.ULog.Info(Tag, "fallback " + Rel(hit.Root) + " CreatureInfo lang=" + current + "->" + hit.Lang);
			}
			return hit.Path;
		}

		// ---------- EquipmentDataLoader.Load ----------

		public static bool PrefixEquipment(EquipmentDataLoader __instance)
		{
			int total = 0;
			try
			{
				Begin();
				ModDebug.Log("EDL Load 1");
				XmlDocument xmlDocument = new XmlDocument();
				if (!File.Exists(Application.dataPath + "/Managed/BaseMod/BaseEquipment.txt"))
				{
					TextAsset textAsset = Resources.Load<TextAsset>("xml/Equipment/Equipment");
					File.WriteAllText(Application.dataPath + "/Managed/BaseMod/BaseEquipment.txt", textAsset.text);
				}
				string text = File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseEquipment.txt");
				xmlDocument.LoadXml(text);
				Dictionary<int, EquipmentTypeInfo> dictionary = __instance.LoadEquips(xmlDocument);
				Dictionary<string, Dictionary<int, EquipmentTypeInfo>> dictionary2 = new Dictionary<string, Dictionary<int, EquipmentTypeInfo>>();
				ModDebug.Log("EDL Load 2");
				List<ModInfo> mods = Add_On.instance.ModList;
				for (int m = 0; m < mods.Count; m++)
				{
					ModInfo modInfo = mods[m];
					DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(modInfo.modpath, "Equipment");
					if (directoryInfo == null || !Directory.Exists(directoryInfo.FullName + "/txts"))
					{
						continue;
					}
					DirectoryInfo directoryInfo2 = new DirectoryInfo(directoryInfo.FullName + "/txts");
					if (directoryInfo2.GetFiles().Length == 0)
					{
						continue;
					}
					bool noModid = modInfo.modid == string.Empty;
					Dictionary<int, EquipmentTypeInfo> dictionary3 = noModid ? null : new Dictionary<int, EquipmentTypeInfo>();
					FileInfo[] files = directoryInfo2.GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						FileInfo fileInfo = files[f];
						if (!fileInfo.Name.Contains(".txt") && !fileInfo.Name.Contains(".xml"))
						{
							continue;
						}
						Files++;
						Dictionary<int, EquipmentTypeInfo> loaded;
						try
						{
							XmlDocument xmlDocument2 = new XmlDocument();
							xmlDocument2.LoadXml(File.ReadAllText(fileInfo.FullName));
							loaded = LoadEquips(__instance, xmlDocument2, fileInfo.FullName);
						}
						catch (Exception e)
						{
							Skip(fileInfo.FullName, null, e);
							continue;
						}
						Dictionary<int, EquipmentTypeInfo> into = noModid ? dictionary : dictionary3;
						foreach (KeyValuePair<int, EquipmentTypeInfo> keyValuePair in loaded)
						{
							if (!noModid)
							{
								keyValuePair.Value.modid = modInfo.modid;
							}
							if (into.ContainsKey(keyValuePair.Key))
							{
								into.Remove(keyValuePair.Key);
							}
							into.Add(keyValuePair.Key, keyValuePair.Value);
						}
					}
					if (!noModid)
					{
						dictionary2[modInfo.modid] = dictionary3;
					}
				}
				ModDebug.Log("EDL Load 3");
				EquipmentTypeList.instance.Init(dictionary);
				EquipmentTypeList.instance.Init_Mod(dictionary2);
				ModDebug.Log("EDL Load 4");
				total = dictionary.Count;
				foreach (KeyValuePair<string, Dictionary<int, EquipmentTypeInfo>> kv in dictionary2)
				{
					total += kv.Value.Count;
				}
			}
			catch (Exception e)
			{
				// Same outcome as the vanilla catch (logged, swallowed); the copy would run without it (GL-53).
				Internal("equipment", e);
				return false;
			}
			Summary("equipment", " equipment=" + total + RepairText());
			return false;
		}

		// Copy of EquipmentDataLoader.LoadEquips with one try per entry. The vanilla ArgumentException catch
		// (duplicate id in one file: Debug.LogError, entry dropped) is kept as it was.
		private static Dictionary<int, EquipmentTypeInfo> LoadEquips(EquipmentDataLoader loader, XmlDocument document, string path)
		{
			Dictionary<int, EquipmentTypeInfo> dictionary = new Dictionary<int, EquipmentTypeInfo>();
			XmlNodeList nodes = document.SelectSingleNode("equipment_list").SelectNodes("equipment");
			for (int i = 0; i < nodes.Count; i++)
			{
				XmlNode xmlNode = nodes[i];
				try
				{
					LoadEquip(loader, xmlNode, dictionary, path);
				}
				catch (Exception e)
				{
					Skip(path, EntryId(xmlNode, "id", i), e);
				}
			}
			return dictionary;
		}

		private static void LoadEquip(EquipmentDataLoader loader, XmlNode xmlNode, Dictionary<int, EquipmentTypeInfo> dictionary, string path)
		{
			EquipmentTypeInfo equipmentTypeInfo = new EquipmentTypeInfo();
			int num = int.Parse(xmlNode.Attributes.GetNamedItem("id").InnerText);
			string innerText = xmlNode.Attributes.GetNamedItem("type").InnerText;
			equipmentTypeInfo.id = num;
			XmlNode xmlNode2 = xmlNode.SelectSingleNode("name");
			if (xmlNode2 != null)
			{
				equipmentTypeInfo.localizeData.Add("name", xmlNode2.InnerText.Trim());
			}
			else
			{
				equipmentTypeInfo.localizeData.Add("name", num + "name");
			}
			XmlNode xmlNode3 = xmlNode.SelectSingleNode("no");
			if (xmlNode3 != null)
			{
				equipmentTypeInfo.localizeData.Add("no", xmlNode3.InnerText.Trim());
				equipmentTypeInfo.no = xmlNode3.InnerText;
			}
			else
			{
				equipmentTypeInfo.no = num.ToString();
			}
			XmlNode xmlNode4 = xmlNode.SelectSingleNode("desc");
			if (xmlNode4 != null)
			{
				equipmentTypeInfo.localizeData.Add("desc", xmlNode4.InnerText.Trim());
			}
			XmlNode xmlNode5 = xmlNode.SelectSingleNode("specialDesc");
			if (xmlNode5 != null)
			{
				equipmentTypeInfo.localizeData.Add("specialDesc", xmlNode5.InnerText.Trim());
			}
			XmlNode xmlNode6 = xmlNode.SelectSingleNode("script");
			if (xmlNode6 != null)
			{
				equipmentTypeInfo.script = xmlNode6.InnerText;
			}
			equipmentTypeInfo.requires = new List<EgoRequire>();
			XmlNodeList requires = xmlNode.SelectNodes("require");
			for (int r = 0; r < requires.Count; r++)
			{
				XmlNode xmlNode7 = requires[r];
				string innerText2 = xmlNode7.Attributes.GetNamedItem("type").InnerText;
				int num2 = int.Parse(xmlNode7.InnerText);
				EgoRequire egoRequire = new EgoRequire();
				if (innerText2 == "R")
				{
					egoRequire.type = EgoRequireType.R;
					egoRequire.value = num2;
				}
				if (innerText2 == "W")
				{
					egoRequire.type = EgoRequireType.W;
					egoRequire.value = num2;
				}
				if (innerText2 == "B")
				{
					egoRequire.type = EgoRequireType.B;
					egoRequire.value = num2;
				}
				if (innerText2 == "P")
				{
					egoRequire.type = EgoRequireType.P;
					egoRequire.value = num2;
				}
				if (innerText2 == "level")
				{
					egoRequire.type = EgoRequireType.level;
					egoRequire.value = num2;
				}
				equipmentTypeInfo.requires.Add(egoRequire);
			}
			XmlNode xmlNode8 = xmlNode.SelectSingleNode("maxNum");
			if (xmlNode8 != null)
			{
				equipmentTypeInfo.maxNum = int.Parse(xmlNode8.InnerText);
			}
			XmlNode xmlNode9 = xmlNode.SelectSingleNode("grade");
			if (xmlNode9 != null)
			{
				equipmentTypeInfo.grade = xmlNode9.InnerText.Trim();
			}
			try
			{
				if (innerText == "weapon")
				{
					equipmentTypeInfo.type = EquipmentTypeInfo.EquipmentType.WEAPON;
					loader.LoadWeapon(equipmentTypeInfo, xmlNode);
					dictionary.Add(num, equipmentTypeInfo);
				}
				else if (innerText == "armor")
				{
					equipmentTypeInfo.type = EquipmentTypeInfo.EquipmentType.ARMOR;
					loader.LoadArmor(equipmentTypeInfo, xmlNode);
					dictionary.Add(num, equipmentTypeInfo);
				}
				else if (innerText == "special")
				{
					equipmentTypeInfo.type = EquipmentTypeInfo.EquipmentType.SPECIAL;
					if (RepairOn)
					{
						RepairGift(xmlNode, path, num.ToString());
					}
					loader.LoadEGOgift(equipmentTypeInfo, xmlNode);
					dictionary.Add(num, equipmentTypeInfo);
				}
			}
			catch (ArgumentException ex)
			{
				Debug.LogError(num + " " + ex);
			}
		}

		internal static string EntryId(XmlNode node, string attr, int index)
		{
			try
			{
				XmlNode a = node.Attributes.GetNamedItem(attr);
				if (a != null)
				{
					return a.InnerText;
				}
			}
			catch (Exception)
			{
				// No attribute access possible (comment node, no attributes): fall back to the index.
			}
			return "#" + index;
		}

		// ---------- GameStaticDataLoader.LoadModIsolate ----------

		public static bool PrefixIsolate(GameStaticDataLoader __instance, Dictionary<string, XmlNode> nodeRoot)
		{
			try
			{
				Begin();
				List<XmlDocument> adds = new List<XmlDocument>();
				List<string> addPaths = new List<string>();
				List<XmlDocument> replaces = new List<XmlDocument>();
				List<string> replacePaths = new List<string>();
				List<DirectoryInfo> dirs = Add_On.instance.DirList;
				for (int d = 0; d < dirs.Count; d++)
				{
					string path = dirs[d].FullName + "/SefiraIsolate";
					if (!Directory.Exists(path))
					{
						continue;
					}
					FileInfo[] files = new DirectoryInfo(path).GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						FileInfo fileInfo = files[f];
						string lower = fileInfo.Name.ToLower();
						bool isAdd = lower == "add.xml" || lower == "add.txt";
						bool isReplace = lower == "replace.xml" || lower == "replace.txt";
						if (!isAdd && !isReplace)
						{
							continue;
						}
						Files++;
						try
						{
							XmlDocument xmlDocument = new XmlDocument();
							xmlDocument.LoadXml(File.ReadAllText(fileInfo.FullName));
							if (isAdd)
							{
								adds.Add(xmlDocument);
								addPaths.Add(fileInfo.FullName);
							}
							else
							{
								replaces.Add(xmlDocument);
								replacePaths.Add(fileInfo.FullName);
							}
						}
						catch (Exception e)
						{
							Skip(fileInfo.FullName, null, e);
						}
					}
				}
				for (int i = 0; i < replaces.Count; i++)
				{
					ReplaceNodes(nodeRoot, replaces[i], replacePaths[i]);
				}
				for (int i = 0; i < adds.Count; i++)
				{
					try
					{
						__instance.LoadModIsolate_Add(nodeRoot, adds[i]);
					}
					catch (Exception e)
					{
						Skip(addPaths[i], null, e);
					}
				}
			}
			catch (Exception e)
			{
				// nodeRoot is changed in place: running the original now would apply files twice.
				Internal("isolate", e);
				return false;
			}
			Summary("isolate", string.Empty);
			return false;
		}

		// Same steps as LoadModIsolate_Replace, one try per node instead of one for the whole file.
		private static void ReplaceNodes(Dictionary<string, XmlNode> nodeRoot, XmlDocument mxml, string path)
		{
			XmlNode xmlNode = mxml.SelectSingleNode("root");
			if (xmlNode == null)
			{
				return;
			}
			XmlNodeList children = xmlNode.ChildNodes;
			for (int i = 0; i < children.Count; i++)
			{
				XmlNode item = children[i];
				// Comments and text: vanilla stops the file there (NRE caught); skip them without a warning.
				if (item.NodeType != XmlNodeType.Element)
				{
					continue;
				}
				try
				{
					string innerText = item.Attributes.GetNamedItem("name").InnerText;
					XmlNode xmlNode3 = nodeRoot[innerText].OwnerDocument.SelectSingleNode("root");
					XmlNode xmlNode4 = xmlNode3.OwnerDocument.ImportNode(item, true);
					xmlNode3.RemoveChild(nodeRoot[innerText]);
					xmlNode3.AppendChild(xmlNode4);
					nodeRoot[innerText] = xmlNode4;
				}
				catch (Exception e)
				{
					Skip(path, EntryId(item, "name", i), e);
				}
			}
		}
	}

	// CreatureDataLoader.Load / Loading / Loading_Mod replacements.
	public static class CreatureGuard
	{
		private static MethodInfo _loadStat;
		private static MethodInfo _loadStatMod;
		private static FieldInfo _genAll;

		// File whose entries Loading/Loading_Mod are reading (set by the Load and ReLoad replacements; null for other callers).
		private static string _curFile;

		internal static bool Bind()
		{
			BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			_loadStat = typeof(CreatureDataLoader).GetMethod("LoadCreatureStat", inst);
			_loadStatMod = typeof(CreatureDataLoader).GetMethod("LoadCreatureStat_Mod", inst);
			_genAll = typeof(CreatureGenerateInfo).GetField("all", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (object.ReferenceEquals(_loadStat, null) || object.ReferenceEquals(_loadStatMod, null) || object.ReferenceEquals(_genAll, null))
			{
				N.ULog.Error(Guard.Tag, "CreatureDataLoader.LoadCreatureStat(_Mod) or CreatureGenerateInfo.all not found, creatures not guarded");
				return false;
			}
			return true;
		}

		public static bool PrefixLoad(CreatureDataLoader __instance)
		{
			int total = 0;
			ModDebug.Log("CDL Load 1");
			ResetIndex();
			try
			{
				Guard.Begin();
				ModDebug.Log("CDL Load 2");
				List<long> list = new List<long>();
				List<LcIdLong> list2 = new List<LcIdLong>();
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.LoadXml(File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseCreatureGen.xml"));
				foreach (object item6 in xmlDocument.SelectNodes("/All/id"))
				{
					long item = int.Parse(((XmlNode)item6).InnerText);
					list.Add(item);
				}
				List<ModInfo> mods = Add_On.instance.ModList;
				for (int m = 0; m < mods.Count; m++)
				{
					ModInfo modInfo = mods[m];
					DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(modInfo.modpath, "Creature");
					if (directoryInfo == null || !Directory.Exists(directoryInfo.FullName + "/CreatureGen"))
					{
						continue;
					}
					FileInfo[] files = new DirectoryInfo(directoryInfo.FullName + "/CreatureGen").GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						FileInfo fileInfo = files[f];
						if (!fileInfo.Name.Contains(".xml") && !fileInfo.Name.Contains(".txt"))
						{
							continue;
						}
						Guard.Files++;
						// Parse the whole file first, then apply: a broken file changes nothing.
						List<long> add = new List<long>();
						List<long> remove = new List<long>();
						try
						{
							XmlDocument xmlDocument2 = new XmlDocument();
							xmlDocument2.LoadXml(File.ReadAllText(fileInfo.FullName));
							foreach (object a in xmlDocument2.SelectNodes("/All/add"))
							{
								add.Add(int.Parse(((XmlNode)a).InnerText));
							}
							foreach (object r in xmlDocument2.SelectNodes("/All/remove"))
							{
								remove.Add(int.Parse(((XmlNode)r).InnerText));
							}
						}
						catch (Exception e)
						{
							Guard.Skip(fileInfo.FullName, null, e);
							continue;
						}
						if (modInfo.modid == string.Empty)
						{
							list.AddRange(add);
							for (int i = 0; i < remove.Count; i++)
							{
								list.Remove(remove[i]);
							}
							continue;
						}
						for (int i = 0; i < add.Count; i++)
						{
							list2.Add(new LcIdLong(modInfo.modid, add[i]));
						}
						for (int i = 0; i < remove.Count; i++)
						{
							list2.Remove(new LcIdLong(modInfo.modid, remove[i]));
						}
					}
				}
				ModDebug.Log("CDL Load 3");
				_genAll.SetValue(null, list.ToArray());
				CreatureGenerateInfo.all_mod = list2;
				if (!EquipmentTypeList.instance.loaded)
				{
					ModDebug.Log("LoadCreatureList >> EquipmentTypeList must be loaded. ");
				}
				__instance.currentLn = GlobalGameManager.instance.GetCurrentLanguage();
				XmlDocument xmlDocument3 = new XmlDocument();
				xmlDocument3.LoadXml(File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseList.txt"));
				XmlNodeList xmlNodeList = xmlDocument3.SelectNodes("/creature_list/creature");
				List<CreatureTypeInfo> list3 = new List<CreatureTypeInfo>();
				Dictionary<string, List<CreatureTypeInfo>> dictionary = new Dictionary<string, List<CreatureTypeInfo>>();
				List<CreatureSpecialSkillTipTable> list4 = new List<CreatureSpecialSkillTipTable>();
				Dictionary<string, List<CreatureSpecialSkillTipTable>> dictionary2 = new Dictionary<string, List<CreatureSpecialSkillTipTable>>();
				Dictionary<long, int> dictionary3 = new Dictionary<long, int>();
				_curFile = Application.dataPath + "/Managed/BaseMod/BaseList.txt";
				__instance.Loading(xmlNodeList, list3, list4, dictionary3, false);
				ModDebug.Log("CDL Load 4");
				for (int m = 0; m < mods.Count; m++)
				{
					ModInfo modInfo2 = mods[m];
					DirectoryInfo directoryInfo3 = EquipmentDataLoader.CheckNamedDir(modInfo2.modpath, "Creature");
					if (directoryInfo3 == null || !Directory.Exists(directoryInfo3.FullName + "/CreatureList"))
					{
						continue;
					}
					DirectoryInfo directoryInfo4 = new DirectoryInfo(directoryInfo3.FullName + "/CreatureList");
					if (directoryInfo4.GetFiles().Length == 0)
					{
						continue;
					}
					if (modInfo2.modid != string.Empty)
					{
						dictionary[modInfo2.modid] = new List<CreatureTypeInfo>();
						dictionary2[modInfo2.modid] = new List<CreatureSpecialSkillTipTable>();
					}
					FileInfo[] files2 = directoryInfo4.GetFiles();
					for (int f = 0; f < files2.Length; f++)
					{
						FileInfo fileInfo2 = files2[f];
						if (!fileInfo2.Name.Contains(".txt") && !fileInfo2.Name.Contains(".xml"))
						{
							continue;
						}
						Guard.Files++;
						XmlNodeList entries;
						try
						{
							XmlDocument xmlDocument4 = new XmlDocument();
							xmlDocument4.LoadXml(File.ReadAllText(fileInfo2.FullName));
							entries = xmlDocument4.SelectNodes("/creature_list/creature");
						}
						catch (Exception e)
						{
							Guard.Skip(fileInfo2.FullName, null, e);
							continue;
						}
						_curFile = fileInfo2.FullName;
						CreatureLoad.NoteFolder(modInfo2.modid, entries, modInfo2.modpath.Name);
						if (modInfo2.modid == string.Empty)
						{
							List<CreatureTypeInfo> list5 = new List<CreatureTypeInfo>();
							List<CreatureSpecialSkillTipTable> list6 = new List<CreatureSpecialSkillTipTable>();
							Dictionary<long, int> dictionary4 = new Dictionary<long, int>();
							__instance.Loading(entries, list5, list6, dictionary4, true);
							foreach (KeyValuePair<long, int> item11 in dictionary4)
							{
								if (dictionary3.ContainsKey(item11.Key))
								{
									for (int k = 0; k < list3.Count; k++)
									{
										dictionary3.Remove(item11.Key);
										dictionary3.Add(item11.Key, item11.Value);
									}
								}
							}
							foreach (CreatureTypeInfo item12 in list5)
							{
								foreach (CreatureTypeInfo item13 in list3)
								{
									if (item12.id == item13.id)
									{
										list3.Remove(item13);
										break;
									}
								}
								list3.Add(item12);
							}
							foreach (CreatureSpecialSkillTipTable item14 in list6)
							{
								foreach (CreatureSpecialSkillTipTable item15 in list4)
								{
									if (item15.creatureTypeId == item14.creatureTypeId)
									{
										list4.Remove(item15);
										break;
									}
								}
								list4.Add(item14);
							}
							continue;
						}
						List<CreatureTypeInfo> list7 = new List<CreatureTypeInfo>();
						List<CreatureSpecialSkillTipTable> list8 = new List<CreatureSpecialSkillTipTable>();
						Dictionary<long, int> specialTipSize = new Dictionary<long, int>();
						__instance.Loading_Mod(entries, list7, list8, specialTipSize, modInfo2.modid);
						for (int i = 0; i < list7.Count; i++)
						{
							list7[i].modid = modInfo2.modid;
						}
						for (int i = 0; i < list8.Count; i++)
						{
							list8[i].modid = modInfo2.modid;
						}
						dictionary[modInfo2.modid].AddRange(list7);
						dictionary2[modInfo2.modid].AddRange(list8);
					}
				}
				_curFile = null;
				ModDebug.Log("CDL Load 5");
				CreatureTypeList.instance.Init_Mod(dictionary, dictionary2);
				InitTypeList(list3.ToArray(), list4.ToArray(), dictionary3);
				ModDebug.Log("CDL Load 6");
				total = list3.Count;
				foreach (KeyValuePair<string, List<CreatureTypeInfo>> kv in dictionary)
				{
					total += kv.Value.Count;
				}
			}
			catch (Exception e)
			{
				_curFile = null;
				// Same outcome as the vanilla catch (logged, swallowed); the copy would run without it (GL-53).
				Guard.Internal("creatures", e);
				return false;
			}
			Guard.Summary("creatures", " creatures=" + total + Guard.RepairText());
			return false;
		}

		// ---------- CreatureDataLoader.ReLoad (ReloadData, language change) ----------
		// Vanilla body (no handler, finally only) plus one try per mod CreatureList file; a file's creatures are merged
		// only after its Loading returned. Same file set as vanilla: BaseList.txt, then *.txt of every DirList mod.

		public static bool PrefixReLoad(CreatureDataLoader __instance)
		{
			int total = 0;
			ResetIndex();
			try
			{
				Guard.Begin();
				if (!EquipmentTypeList.instance.loaded)
				{
					Debug.LogError("LoadCreatureList >> EquipmentTypeList must be loaded. ");
				}
				__instance.currentLn = GlobalGameManager.instance.GetCurrentLanguage();
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.LoadXml(File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseList.txt"));
				XmlNodeList xmlNodeList = xmlDocument.SelectNodes("/creature_list/creature");
				List<CreatureTypeInfo> list = new List<CreatureTypeInfo>();
				List<CreatureSpecialSkillTipTable> list2 = new List<CreatureSpecialSkillTipTable>();
				Dictionary<long, int> dictionary = new Dictionary<long, int>();
				_curFile = Application.dataPath + "/Managed/BaseMod/BaseList.txt";
				__instance.Loading(xmlNodeList, list, list2, dictionary, false);
				List<DirectoryInfo> dirs = Add_On.instance.DirList;
				for (int d = 0; d < dirs.Count; d++)
				{
					DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(dirs[d], "Creature");
					if (directoryInfo == null || !Directory.Exists(directoryInfo.FullName + "/CreatureList"))
					{
						continue;
					}
					DirectoryInfo directoryInfo2 = new DirectoryInfo(directoryInfo.FullName + "/CreatureList");
					if (directoryInfo2.GetFiles().Length == 0)
					{
						continue;
					}
					FileInfo[] files = directoryInfo2.GetFiles();
					for (int f = 0; f < files.Length; f++)
					{
						FileInfo fileInfo = files[f];
						// Load reads .txt and .xml; vanilla ReLoad read .txt only and lost the creatures of .xml lists (codex NRE).
						if (!fileInfo.Name.Contains(".txt") && !fileInfo.Name.Contains(".xml"))
						{
							continue;
						}
						Guard.Files++;
						List<CreatureTypeInfo> list3 = new List<CreatureTypeInfo>();
						List<CreatureSpecialSkillTipTable> list4 = new List<CreatureSpecialSkillTipTable>();
						Dictionary<long, int> dictionary2 = new Dictionary<long, int>();
						try
						{
							XmlDocument xmlDocument2 = new XmlDocument();
							xmlDocument2.LoadXml(File.ReadAllText(fileInfo.FullName));
							XmlNodeList xmlNodeList2 = xmlDocument2.SelectNodes("/creature_list/creature");
							_curFile = fileInfo.FullName;
							__instance.Loading(xmlNodeList2, list3, list4, dictionary2, true);
						}
						catch (Exception e)
						{
							Guard.Skip(fileInfo.FullName, null, e);
							continue;
						}
						foreach (KeyValuePair<long, int> item in dictionary2)
						{
							if (dictionary.ContainsKey(item.Key))
							{
								for (int j = 0; j < list.Count; j++)
								{
									dictionary.Remove(item.Key);
									dictionary.Add(item.Key, item.Value);
								}
							}
						}
						foreach (CreatureTypeInfo item2 in list3)
						{
							foreach (CreatureTypeInfo item3 in list)
							{
								if (item2.id == item3.id)
								{
									list.Remove(item3);
									break;
								}
							}
							list.Add(item2);
						}
						foreach (CreatureSpecialSkillTipTable item4 in list4)
						{
							foreach (CreatureSpecialSkillTipTable item5 in list2)
							{
								if (item5.creatureTypeId == item4.creatureTypeId)
								{
									list2.Remove(item5);
									break;
								}
							}
							list2.Add(item4);
						}
					}
				}
				_curFile = null;
				InitTypeList(list.ToArray(), list2.ToArray(), dictionary);
				total = list.Count;
			}
			catch (Exception e)
			{
				_curFile = null;
				// Vanilla has no catch here (the exception left ReloadData).
				Guard.Internal("creatures (reload)", e);
				return false;
			}
			Guard.Summary("creatures (reload)", " creatures=" + total + Guard.RepairText());
			return false;
		}

		private static void InitTypeList(CreatureTypeInfo[] list, CreatureSpecialSkillTipTable[] tips, Dictionary<long, int> sizes)
		{
			try
			{
				CreatureTypeList.instance.Init(list, tips, sizes);
			}
			catch (NullReferenceException e)
			{
				if (e.StackTrace == null || e.StackTrace.IndexOf("CreatureGenerateInfoManager.Init", StringComparison.Ordinal) < 0)
				{
					throw;
				}
				// Last step of Init, its top stack frame in every boot seen. Title and GameManager build it again.
				N.ULog.Info("loadguard", "creature generate info skipped at load, built again later");
			}
		}

		public static bool PrefixLoading(CreatureDataLoader __instance, XmlNodeList xmlNodeList, List<CreatureTypeInfo> list, List<CreatureSpecialSkillTipTable> list2, Dictionary<long, int> specialTipSize, bool isMod)
		{
			Entries(__instance, xmlNodeList, list, list2, specialTipSize, isMod, null);
			return false;
		}

		public static bool PrefixLoadingMod(CreatureDataLoader __instance, XmlNodeList xmlNodeList, List<CreatureTypeInfo> list, List<CreatureSpecialSkillTipTable> list2, Dictionary<long, int> specialTipSize, string modid)
		{
			Entries(__instance, xmlNodeList, list, list2, specialTipSize, true, modid);
			return false;
		}

		// Loading (modid == null) and Loading_Mod (modid != null): one try per entry; a failed entry's list additions are removed.
		private static void Entries(CreatureDataLoader loader, XmlNodeList nodes, List<CreatureTypeInfo> list, List<CreatureSpecialSkillTipTable> list2, Dictionary<long, int> specialTipSize, bool isMod, string modid)
		{
			string file = CurFile();
			for (int i = 0; i < nodes.Count; i++)
			{
				XmlNode node = nodes[i];
				int count1 = list.Count;
				int count2 = list2.Count;
				HashSet<long> keys0 = new HashSet<long>(specialTipSize.Keys);
				try
				{
					Entry(loader, node, list, list2, specialTipSize, isMod, modid);
				}
				catch (Exception e)
				{
					if (list.Count > count1)
					{
						list.RemoveRange(count1, list.Count - count1);
					}
					if (list2.Count > count2)
					{
						list2.RemoveRange(count2, list2.Count - count2);
					}
					if (specialTipSize.Count > keys0.Count)
					{
						List<long> now = new List<long>(specialTipSize.Keys);
						for (int k = 0; k < now.Count; k++)
						{
							if (!keys0.Contains(now[k]))
							{
								specialTipSize.Remove(now[k]);
							}
						}
					}
					Guard.Skip(file, Guard.EntryId(node, "src", i), e);
				}
			}
		}

		private static string CurFile()
		{
			return _curFile != null ? _curFile : "(unknown caller) BaseList or Creature/CreatureList";
		}

		internal static bool UseIndex;
		private static Dictionary<string, string> _statIndex;
		private static Dictionary<string, string> _childIndex;
		private static int _statIndexDirs;
		private static bool _indexFailed;

		// Names without extension, first in DirList and listing order wins: the same file the walk over the mods stops at.
		private static void BuildCreatureIndex()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			List<DirectoryInfo> dirs = Add_On.instance.DirList;
			Dictionary<string, string> stats = new Dictionary<string, string>(StringComparer.Ordinal);
			Dictionary<string, string> children = new Dictionary<string, string>(StringComparer.Ordinal);
			for (int d = 0; d < dirs.Count; d++)
			{
				try
				{
					DirectoryInfo named = EquipmentDataLoader.CheckNamedDir(dirs[d], "Creature");
					if (named != null && Directory.Exists(named.FullName + "/Creatures"))
					{
						AddFiles(stats, named.FullName + "/Creatures");
					}
					// The child walk names the folder directly and does not go through CheckNamedDir.
					if (Directory.Exists(dirs[d].FullName + "/Creature/Creatures"))
					{
						AddFiles(children, dirs[d].FullName + "/Creature/Creatures");
					}
				}
				catch (Exception e)
				{
					// The walk only fails on this folder when it gets that far, so it decides from here on.
					_indexFailed = true;
					N.ULog.Warn("loadguard", "creature stat index off, using the walk: " + dirs[d].Name + ": " + e.Message);
					break;
				}
			}
			_statIndex = stats;
			_childIndex = children;
			_statIndexDirs = dirs.Count;
			N.ULog.Info("loadguard", "creature stat index: " + stats.Count + " names, " + sw.ElapsedMilliseconds + " ms");
		}

		private static void AddFiles(Dictionary<string, string> index, string path)
		{
			FileInfo[] files = new DirectoryInfo(path).GetFiles();
			for (int f = 0; f < files.Length; f++)
			{
				string name = files[f].Name;
				if (name.EndsWith(".xml", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal))
				{
					string stem = name.Substring(0, name.Length - 4);
					if (!index.ContainsKey(stem))
					{
						index[stem] = files[f].FullName;
					}
				}
			}
		}

		private static bool IndexReady()
		{
			if (!UseIndex)
			{
				return false;
			}
			if (_statIndex == null || _statIndexDirs != Add_On.instance.DirList.Count)
			{
				_indexFailed = false;
				BuildCreatureIndex();
			}
			return !_indexFailed;
		}

		// Files may have changed between passes (language change reload).
		private static void ResetIndex()
		{
			_statIndex = null;
			_childIndex = null;
		}

		private static string FindStat(string name)
		{
			string path;
			return _statIndex.TryGetValue(name, out path) ? path : null;
		}

		private static string FindChild(string name)
		{
			string path;
			return _childIndex.TryGetValue(name, out path) ? path : null;
		}

		private static void Entry(CreatureDataLoader loader, XmlNode xmlNode14, List<CreatureTypeInfo> list, List<CreatureSpecialSkillTipTable> list2, Dictionary<long, int> specialTipSize, bool isMod, string modid)
		{
			string innerText = xmlNode14.Attributes.GetNamedItem("src").InnerText;
			string xml = "";
			if (!isMod)
			{
				string stat0 = xmlNode14.SelectSingleNode("stat").InnerText;
				if (!File.Exists(Application.dataPath + "/Managed/BaseMod/BaseCreatures/" + stat0 + ".txt"))
				{
					TextAsset textAsset = Resources.Load<TextAsset>("xml/creatureStats/" + stat0);
					File.WriteAllText(Application.dataPath + "/Managed/BaseMod/BaseCreatures/" + stat0 + ".txt", textAsset.text);
				}
				xml = File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseCreatures/" + stat0 + ".txt");
			}
			else
			{
				bool indexed = IndexReady();
				if (indexed)
				{
					string statPath = FindStat(xmlNode14.SelectSingleNode("stat").InnerText);
					if (statPath != null)
					{
						xml = File.ReadAllText(statPath);
					}
				}
				List<DirectoryInfo> dirs = indexed ? new List<DirectoryInfo>() : Add_On.instance.DirList;
				for (int d = 0; d < dirs.Count; d++)
				{
					bool flag = false;
					DirectoryInfo directoryInfo = EquipmentDataLoader.CheckNamedDir(dirs[d], "Creature");
					if (directoryInfo != null && Directory.Exists(directoryInfo.FullName + "/Creatures"))
					{
						FileInfo[] files = new DirectoryInfo(directoryInfo.FullName + "/Creatures").GetFiles();
						for (int f = 0; f < files.Length; f++)
						{
							FileInfo fileInfo = files[f];
							if (fileInfo.Name == xmlNode14.SelectSingleNode("stat").InnerText + ".txt" || fileInfo.Name == xmlNode14.SelectSingleNode("stat").InnerText + ".xml")
							{
								xml = File.ReadAllText(fileInfo.FullName);
								flag = true;
								break;
							}
						}
					}
					if (flag)
					{
						break;
					}
				}
			}
			XmlDocument xmlDocument = new XmlDocument();
			XmlDocument doc = loader.LoadDoc(innerText, loader.currentLn, isMod);
			if (Guard.RepairOn && isMod)
			{
				Guard.RepairInfo(doc, CurFile(), innerText);
			}
			xmlDocument.LoadXml(xml);
			ChildCreatureData childData = null;
			CreatureTypeInfo creatureTypeInfo = loader.LoadCreatureTypeInfo(doc, ref list2, ref specialTipSize, out childData);
			XmlNode xmlNode2 = xmlDocument.SelectSingleNode("creature");
			XmlNode stat = xmlDocument.SelectSingleNode("creature/stat");
			XmlNode xmlNode3 = xmlNode2.SelectSingleNode("child");
			LoadStat(loader, stat, xmlNode2, creatureTypeInfo, modid);
			if (xmlNode3 != null)
			{
				string innerText2 = xmlNode3.InnerText;
				string text = "";
				bool walk = true;
				if (IndexReady())
				{
					string childPath = FindChild(innerText2);
					if (childPath != null)
					{
						text = File.ReadAllText(childPath);
					}
					// An empty file sends the walk on to the next mod, which the index does not know.
					walk = childPath != null && text == "";
				}
				List<DirectoryInfo> dirs2 = walk ? Add_On.instance.DirList : new List<DirectoryInfo>();
				for (int d = 0; d < dirs2.Count; d++)
				{
					if (!Directory.Exists(dirs2[d].FullName + "/Creature/Creatures"))
					{
						continue;
					}
					FileInfo[] files2 = new DirectoryInfo(dirs2[d].FullName + "/Creature/Creatures").GetFiles();
					for (int f = 0; f < files2.Length; f++)
					{
						if (files2[f].Name == innerText2 + ".txt" || files2[f].Name == innerText2 + ".xml")
						{
							text = File.ReadAllText(files2[f].FullName);
							break;
						}
					}
					if (text != "")
					{
						break;
					}
				}
				if (text == "")
				{
					if (!File.Exists(Application.dataPath + "/Managed/BaseMod/BaseCreatures/ChildCreatures/" + innerText2 + ".txt"))
					{
						text = Resources.Load<TextAsset>("xml/creatureStats/" + innerText2).text;
						File.WriteAllText(Application.dataPath + "/Managed/BaseMod/BaseCreatures/ChildCreatures/" + innerText2 + ".txt", text);
					}
					text = File.ReadAllText(Application.dataPath + "/Managed/BaseMod/BaseCreatures/ChildCreatures/" + innerText2 + ".txt");
				}
				XmlDocument xmlDocument2 = new XmlDocument();
				xmlDocument2.LoadXml(text);
				ChildCreatureTypeInfo childCreatureTypeInfo = new ChildCreatureTypeInfo();
				XmlNode xmlNode4 = xmlDocument2.SelectSingleNode("creature");
				childCreatureTypeInfo.maxHp = (int)float.Parse(xmlNode4.SelectSingleNode("stat/hp").InnerText);
				childCreatureTypeInfo.speed = float.Parse(xmlNode4.SelectSingleNode("stat/speed").InnerText);
				XmlNode xmlNode5 = xmlNode4.SelectSingleNode("anim");
				if (xmlNode5 != null)
				{
					childCreatureTypeInfo.animSrc = xmlNode5.Attributes.GetNamedItem("prefab").InnerText;
				}
				XmlNode xmlNode6 = xmlNode4.SelectSingleNode("riskLevel");
				if (xmlNode6 != null)
				{
					int riskLevelOpen = (int)float.Parse(xmlNode6.Attributes.GetNamedItem("openLevel").InnerText);
					string innerText3 = xmlNode6.InnerText;
					childCreatureTypeInfo.riskLevelOpen = riskLevelOpen;
					childCreatureTypeInfo._riskLevel = innerText3;
				}
				XmlNode xmlNode7 = xmlNode4.SelectSingleNode("attackType");
				if (xmlNode7 != null)
				{
					int attackTypeOpen = (int)float.Parse(xmlNode7.Attributes.GetNamedItem("openLevel").InnerText);
					string innerText4 = xmlNode7.InnerText;
					childCreatureTypeInfo.attackTypeOpen = attackTypeOpen;
					childCreatureTypeInfo._attackType = innerText4;
				}
				Dictionary<string, DefenseInfo> dictionary = new Dictionary<string, DefenseInfo>();
				XmlNodeList defenses = xmlNode4.SelectNodes("stat/defense");
				for (int a = 0; a < defenses.Count; a++)
				{
					XmlNode item = defenses[a];
					string innerText5 = item.Attributes.GetNamedItem("id").InnerText;
					DefenseInfo defenseInfo = new DefenseInfo();
					XmlNodeList elements = item.SelectNodes("defenseElement");
					for (int b = 0; b < elements.Count; b++)
					{
						XmlNode item2 = elements[b];
						switch (item2.Attributes.GetNamedItem("type").InnerText)
						{
						case "P":
							defenseInfo.P = float.Parse(item2.InnerText);
							break;
						case "B":
							defenseInfo.B = float.Parse(item2.InnerText);
							break;
						case "W":
							defenseInfo.W = float.Parse(item2.InnerText);
							break;
						case "R":
							defenseInfo.R = float.Parse(item2.InnerText);
							break;
						}
					}
					dictionary.Add(innerText5, defenseInfo);
				}
				childCreatureTypeInfo.defenseTable.Init(dictionary);
				XmlNode xmlNode10 = xmlNode4.SelectSingleNode("script");
				if (xmlNode10 != null)
				{
					childCreatureTypeInfo.script = xmlNode10.InnerText;
				}
				XmlNode xmlNode11 = xmlNode4.SelectSingleNode("portrait");
				if (xmlNode11 != null)
				{
					childCreatureTypeInfo._tempPortrait = xmlNode11.InnerText.Trim();
					childCreatureTypeInfo._isChildAndHasData = true;
				}
				XmlNode xmlNode12 = xmlNode4.SelectSingleNode("metaInfo");
				if (xmlNode12 != null)
				{
					string innerText6 = xmlNode12.InnerText;
					CreatureTypeInfo creatureTypeInfo2 = loader.LoadChildMeta(innerText6, ref list2, ref specialTipSize, isMod);
					XmlNode statCreature = xmlNode4;
					XmlNode stat2 = xmlNode4.SelectSingleNode("stat");
					LoadStat(loader, stat2, statCreature, creatureTypeInfo2, modid);
					list.Add(creatureTypeInfo2);
					childCreatureTypeInfo.id = creatureTypeInfo2.id;
					childCreatureTypeInfo.isHasBaseMeta = true;
				}
				XmlNodeList xmlNodeList2 = xmlNode4.SelectNodes("sound");
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
				for (int s = 0; s < xmlNodeList2.Count; s++)
				{
					XmlNode item3 = xmlNodeList2[s];
					string innerText7 = item3.Attributes.GetNamedItem("action").InnerText;
					string innerText8 = item3.Attributes.GetNamedItem("src").InnerText;
					dictionary2.Add(innerText7, innerText8);
				}
				childCreatureTypeInfo.soundTable = dictionary2;
				creatureTypeInfo.childTypeInfo = childCreatureTypeInfo;
				creatureTypeInfo.childTypeInfo.data = childData;
			}
			list.Add(creatureTypeInfo);
		}

		// Private LoadCreatureStat (Loading) or LoadCreatureStat_Mod (Loading_Mod).
		private static void LoadStat(CreatureDataLoader loader, XmlNode stat, XmlNode statCreature, CreatureTypeInfo model, string modid)
		{
			if (modid == null)
			{
				_loadStat.Invoke(loader, new object[] { stat, statCreature, model });
			}
			else
			{
				_loadStatMod.Invoke(loader, new object[] { stat, statCreature, model, modid });
			}
		}
	}

	// LoadGuard.CreatureLoad: full replacement of CreatureManager.LoadData (day load of the saved abnormalities). Vanilla has
	// no try per creature (one exception ends the day load) and a SefiraIsolateException catch, so a Prefix copy is required
	// (GL-53). Same steps and order as game/CreatureManager.cs 325-453, plus per creature:
	//   before any side effect: the script class is resolved as BuildCreatureModel_Mod does and instantiated; no class, a
	//   throwing ctor or a non-CreatureBase type -> the creature is skipped (named in the log and in LoadAlerts);
	//   CreatureModel.LoadData or GenIsolate throws (other than SefiraIsolateException) -> skipped;
	//   BuildCreatureModel_Mod / RegisterCreature / AddCreatureInSefira / OnInit throws -> logged, the load goes on (no rollback).
	public static class CreatureLoad
	{
		private const string Tag = "loadguard";
		private static FieldInfo _nextInstId;
		private static Assembly _gameAsm;
		private static int _kept;
		private static int _skipped;
		private static int _errors;
		private static List<string> _mods;
		internal static N.Flag Flag;
		// modid + id -> mod folder of the CreatureList file that defined it (LoadGuard creature list load); last file wins.
		private static readonly Dictionary<string, string> _folders = new Dictionary<string, string>(StringComparer.Ordinal);
		// Short name -> type over AssemList (last match wins), built once per LoadData call.
		private static Dictionary<string, Type> _types;

		private static string FolderKey(string modid, long id)
		{
			return (modid == null ? string.Empty : modid) + "\n" + id;
		}

		// Called by the creature list replacement for each mod file (main thread, game start and ReLoad).
		internal static void NoteFolder(string modid, XmlNodeList entries, string folder)
		{
			try
			{
				for (int i = 0; i < entries.Count; i++)
				{
					XmlAttribute a = entries[i].Attributes == null ? null : entries[i].Attributes["id"];
					long id;
					if (a != null && long.TryParse(a.Value.Trim(), out id))
					{
						_folders[FolderKey(modid, id)] = folder;
					}
				}
			}
			catch (Exception)
			{
			}
		}

		internal static bool Bind()
		{
			_nextInstId = typeof(CreatureManager).GetField("nextInstId", BindingFlags.Instance | BindingFlags.NonPublic);
			_gameAsm = typeof(CreatureBase).Assembly;
			if (object.ReferenceEquals(_nextInstId, null) || !object.ReferenceEquals(_nextInstId.FieldType, typeof(long)))
			{
				N.ULog.Error(Tag, "CreatureManager.nextInstId not found, creature load not guarded");
				return false;
			}
			return true;
		}

		public static bool PrefixLoadData(CreatureManager __instance, Dictionary<string, object> dic)
		{
			if (!Flag.On)
			{
				return true;
			}
			List<Dictionary<string, object>> list;
			List<long> list2;
			try
			{
				long next = (long)_nextInstId.GetValue(__instance);
				if (!GameUtil.TryGetValue<long>(dic, "nextInstId", ref next))
				{
					int num = 0;
					if (GameUtil.TryGetValue<int>(dic, "nextInstId", ref num))
					{
						next = (long)num;
					}
				}
				_nextInstId.SetValue(__instance, next);
				list = new List<Dictionary<string, object>>();
				GameUtil.TryGetValue<List<Dictionary<string, object>>>(dic, "creatureList", ref list);
				list2 = new List<long>();
				for (int i = 0; i < list.Count; i++)
				{
					long id = 0L;
					ExtenionUtil.TryGetValue<long>(list[i], "metadataId", ref id);
					list2.Add(id);
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Tag, "creature load: header failed, the game's own load runs: " + e);
				return true;
			}
			_kept = 0;
			_skipped = 0;
			_errors = 0;
			_mods = null;
			_types = null;
			for (int i = 0; i < list.Count; i++)
			{
				Dictionary<string, object> entry = list[i];
				long metaId = 0L;
				string modid = string.Empty;
				try
				{
					GameUtil.TryGetValue<long>(entry, "metadataId", ref metaId);
					GameUtil.TryGetValue<string>(entry, "modid", ref modid);
					LcIdLong lcid = new LcIdLong(modid, metaId);
					if (CreatureTypeList.instance.GetData_Mod(lcid) != null)
					{
						LoadOne(__instance, entry, lcid, modid, list2);
					}
					else if (modid == string.Empty)
					{
						// A vanilla-id entry saved before its mod got a modid: the first mod that defines the id takes it.
						foreach (ModInfo info in ((Add_On)Add_On.instance).ModList)
						{
							ModInfo info2 = info;
							if (info2.modid != string.Empty)
							{
								LcIdLong alt = new LcIdLong(info2.modid, metaId);
								if (CreatureTypeList.instance.GetData_Mod(alt) != null)
								{
									LoadOne(__instance, entry, alt, modid, list2);
									break;
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					Skip(modid, metaId, null, "load error " + e.GetType().Name + ": " + e.Message, e);
				}
			}
			if (_skipped > 0 || _errors > 0)
			{
				N.ULog.Warn(Tag, "creature load: kept " + _kept + ", skipped " + _skipped + ", loaded with errors " + _errors + " (mods: " + (_mods == null ? "-" : string.Join(", ", _mods.ToArray())) + ")");
			}
			return false;
		}

		// One saved creature. savedModid = the modid stored in the save (vanilla checks it, not the resolved one, for the
		// 300109 -> 100104 remap).
		private static void LoadOne(CreatureManager mgr, Dictionary<string, object> entry, LcIdLong lcid, string savedModid, List<long> list2)
		{
			CreatureTypeInfo info = CreatureTypeList.instance.GetData_Mod(lcid);
			string why = CheckScript(info);
			if (why != null)
			{
				Skip(lcid.packageId, lcid.id, info, why, null);
				return;
			}
			long instId = 0L;
			GameUtil.TryGetValue<long>(entry, "instanceId", ref instId);
			CreatureModel model = new CreatureModel(instId);
			model.LoadData(entry);
			if (savedModid == string.Empty && model.metadataId == 300109L && !list2.Contains(100104L))
			{
				new List<long>(CreatureGenerateInfo.GetAll(false));
				model.metadataId = 100104L;
				list2.Add(100104L);
			}
			Sefira sefira = SefiraManager.instance.GetSefira(model.sefiraNum);
			try
			{
				model.isolateRoomData = sefira.isolateManagement.GenIsolateByCreatureByNodeId(model.metadataId, model.entryNodeId);
			}
			catch (SefiraIsolateException ex)
			{
				Debug.LogError("Emergency Load " + ex.nodeId);
				Sefira sefira2 = null;
				if (!SefiraManager.instance.GetSefiraByGenNodeId(ex.nodeId, out sefira2))
				{
					Debug.LogError(string.Concat(new object[] { "Failed to gen sefira creature + ", ex.nodeId, "   Creature ID : ", instId }));
					return;
				}
				model.isolateRoomData = sefira2.isolateManagement.GenIsolateByCreatureByNodeId(model.metadataId, model.entryNodeId);
			}
			string step = "BuildCreatureModel_Mod";
			CreatureBase before = model.script;
			try
			{
				try
				{
					mgr.BuildCreatureModel_Mod(model, lcid, model.isolateRoomData, model.sefiraNum);
				}
				catch (Exception e0)
				{
					// Not registered either way, so it is a skip. Script constructor threw (script not replaced yet): nothing was built.
					// Later in the build: map nodes or doors may be half built (R1); its room stays reserved.
					Exception inner = e0.InnerException != null ? e0.InnerException : e0;
					string why0 = object.ReferenceEquals(model.script, before) ? "script '" + info.script + "' constructor threw " : "building the room threw ";
					Skip(lcid.packageId, lcid.id, info, why0 + inner.GetType().Name + ": " + inner.Message, e0);
					return;
				}
				step = "RegisterCreature";
				mgr.RegisterCreature(model);
				step = "AddCreatureInSefira";
				mgr.AddCreatureInSefira(model, model.sefiraNum);
				step = "OnInit";
				model.script.OnInit();
				_kept++;
			}
			catch (Exception e)
			{
				_errors++;
				AddMod(lcid.packageId, lcid.id);
				string name = NameOf(info, lcid);
				N.ULog.Error(Tag, "creature " + name + " of mod " + ModText(lcid.packageId, lcid.id) + " loaded with an error in " + step + ": " + e);
				LoadAlerts.AddKeySafe(1, ModText(lcid.packageId, lcid.id), "AlertCreatureError", "abnormality {0} loaded with an error in {1}; it may misbehave. The game goes on.", new object[] { name, step });
			}
		}

		// Null when the script resolves the way BuildCreatureModel_Mod resolves it (AssemList walk, last match wins; then
		// Type.GetType as called from Assembly-CSharp) to a CreatureBase; else the reason. Nothing is instantiated here: a
		// throwing constructor is caught around BuildCreatureModel_Mod, which runs the constructors before any side effect.
		private static string CheckScript(CreatureTypeInfo info)
		{
			string name = info.script;
			if (string.IsNullOrEmpty(name))
			{
				return "no script name";
			}
			if (_types == null)
			{
				_types = BuildTypes();
			}
			Type found;
			if (!_types.TryGetValue(name, out found))
			{
				found = name.IndexOf(',') < 0 ? _gameAsm.GetType(name, false) : Type.GetType(name, false);
				if (object.ReferenceEquals(found, null) && name.IndexOf(',') < 0)
				{
					found = typeof(object).Assembly.GetType(name, false);
				}
			}
			if (object.ReferenceEquals(found, null))
			{
				return "no script class '" + name + "'";
			}
			if (!typeof(CreatureBase).IsAssignableFrom(found))
			{
				return "script '" + name + "' is not a CreatureBase";
			}
			return null;
		}

		private static Dictionary<string, Type> BuildTypes()
		{
			Dictionary<string, Type> map = new Dictionary<string, Type>(StringComparer.Ordinal);
			List<Assembly> asms = Add_On.instance.AssemList;
			for (int i = 0; i < asms.Count; i++)
			{
				Type[] types;
				try
				{
					types = asms[i].GetTypes();
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
				for (int k = 0; k < types.Length; k++)
				{
					if (!object.ReferenceEquals(types[k], null))
					{
						map[types[k].Name] = types[k];
					}
				}
			}
			return map;
		}

		private static void Skip(string modid, long id, CreatureTypeInfo info, string why, Exception e)
		{
			_skipped++;
			AddMod(modid, id);
			string name = info != null ? NameOf(info, new LcIdLong(modid, id)) : id.ToString();
			N.ULog.Error(Tag, "creature " + name + " of mod " + ModText(modid, id) + " skipped: " + why + (e != null ? " " + e : string.Empty));
			LoadAlerts.AddKeySafe(2, ModText(modid, id), "AlertCreatureSkipped", "abnormality {0} not loaded: {1}. The day goes on without it; SaveGuard keeps the old save.", new object[] { name, why });
		}

		private static string NameOf(CreatureTypeInfo info, LcIdLong lcid)
		{
			string n = null;
			try
			{
				n = info.collectionName;
			}
			catch (Exception)
			{
			}
			return string.IsNullOrEmpty(n) ? lcid.id.ToString() : n + " (" + lcid.id + ")";
		}

		// Mod folder name of a modid (LoadAlerts groups by folder); the modid itself when no folder matches.
		private static string ModText(string modid, long id)
		{
			string folder;
			if (_folders.TryGetValue(FolderKey(modid, id), out folder))
			{
				return folder;
			}
			if (string.IsNullOrEmpty(modid))
			{
				return "game";
			}
			try
			{
				List<ModInfo> mods = Add_On.instance.ModList;
				for (int i = 0; i < mods.Count; i++)
				{
					ModInfo m = mods[i];
					if (m != null && string.Equals(m.modid, modid, StringComparison.Ordinal) && m.modpath != null)
					{
						return m.modpath.Name;
					}
				}
			}
			catch (Exception)
			{
			}
			return modid;
		}

		private static void AddMod(string modid, long id)
		{
			if (_mods == null)
			{
				_mods = new List<string>();
			}
			string m = ModText(modid, id);
			if (!_mods.Contains(m))
			{
				_mods.Add(m);
			}
		}
	}

	// LoadGuard.TypeLoad: mod DLLs whose types cannot all load (missing or later-loaded dependency). Vanilla Add_On.Init calls
	// Assembly.LoadFile(dll).GetTypes() without a try of its own (game/Add_On.cs 99): a ReflectionTypeLoadException ends
	// the loop and every later mod gets no code. Two parts, both installed from LoadGuard Init (inside Add_On.Init, so only
	// mods after NailOptimization in the load order benefit):
	//   AssemblyResolve: a missing assembly is looked up by simple name among the loaded assemblies, then as <name>.dll in
	//   the top level of the BaseMods and workshop mod folders (log only).
	//   Prefix on Assembly.GetTypes: for an assembly inside a mod folder the original runs in a try; on
	//   ReflectionTypeLoadException the loadable types are returned instead (logged and listed once per assembly), so the
	//   loop goes on and later GetTypes callers (creature, weapon, gift scripts) see the same partial list.
	public static class TypeLoad
	{
		private const string Tag = "loadguard";
		[ThreadStatic]
		private static bool _inside;
		private static bool _fired;
		private static int _mainThread;
		// Copy-on-write: read without locks from any thread, replaced as a whole on a new broken assembly.
		private static volatile Dictionary<string, Type[]> _broken = new Dictionary<string, Type[]>(StringComparer.OrdinalIgnoreCase);
		private static List<string> _roots;
		[ThreadStatic]
		private static bool _resolving;
		// Copy-on-write like _broken: simple names no mod folder holds.
		private static volatile HashSet<string> _miss = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal static N.Flag Flag;

		internal static int Install(HarmonyInstance harmony)
		{
			_mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
			Roots();
			try
			{
				AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(OnResolve);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "AssemblyResolve not registered: " + e.Message);
			}
			MethodBase target = typeof(Assembly).GetMethod("GetTypes", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			MethodInfo pre = typeof(TypeLoad).GetMethod("PrefixGetTypes", BindingFlags.Public | BindingFlags.Static);
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(pre, null))
			{
				N.ULog.Error(Tag, "Assembly.GetTypes or prefix not found, type loads not guarded");
				return 0;
			}
			// First: the guard decides before any other prefix; it only adds a try around the original.
			HarmonyMethod hm = N.PatchUtil.WithPriority(pre, Priority.First);
			return N.PatchUtil.Install(harmony, Tag, target, hm, null) ? 1 : 0;
		}

		public static bool PrefixGetTypes(Assembly __instance, ref Type[] __result)
		{
			if (_inside || Flag == null || !Flag.On)
			{
				return true;
			}
			string path;
			try
			{
				if (__instance is System.Reflection.Emit.AssemblyBuilder)
				{
					return true;
				}
				path = __instance.Location;
			}
			catch (Exception)
			{
				return true;
			}
			if (string.IsNullOrEmpty(path) || LoadAlerts.ModOf(path) == "-")
			{
				return true;
			}
			CrashTrail.ModDll(path);
			if (!_fired)
			{
				_fired = true;
				N.ULog.Info(Tag, "type load guard active (first mod GetTypes: " + Path.GetFileName(path) + ")");
			}
			Type[] partial;
			if (_broken.TryGetValue(path, out partial))
			{
				__result = (Type[])partial.Clone();
				return false;
			}
			_inside = true;
			try
			{
				__result = __instance.GetTypes();
				return false;
			}
			catch (ReflectionTypeLoadException e)
			{
				__result = Loadable(e.Types);
				Remember(path, __result, e);
				return false;
			}
			catch (Exception)
			{
				// Anything else: the original runs again and throws as vanilla.
				return true;
			}
			finally
			{
				_inside = false;
			}
		}

		private static Type[] Loadable(Type[] types)
		{
			if (object.ReferenceEquals(types, null))
			{
				return new Type[0];
			}
			List<Type> ok = new List<Type>(types.Length);
			for (int i = 0; i < types.Length; i++)
			{
				if (!object.ReferenceEquals(types[i], null))
				{
					ok.Add(types[i]);
				}
			}
			return ok.ToArray();
		}

		private static void Remember(string path, Type[] types, ReflectionTypeLoadException e)
		{
			Dictionary<string, Type[]> old = _broken;
			if (old.ContainsKey(path))
			{
				return;
			}
			Dictionary<string, Type[]> next = new Dictionary<string, Type[]>(old, StringComparer.OrdinalIgnoreCase);
			next[path] = (Type[])types.Clone();
			_broken = next;
			try
			{
				Report(path, types, e);
			}
			catch (Exception x)
			{
				N.ULog.Warn(Tag, "partial load report failed: " + x.Message);
			}
		}

		private static void Report(string path, Type[] types, ReflectionTypeLoadException e)
		{
			string missing = Missing(e);
			string dll = Path.GetFileName(path);
			string mod = LoadAlerts.ModOf(path);
			int all = object.ReferenceEquals(e.Types, null) ? 0 : e.Types.Length;
			N.ULog.Error(Tag, "mod " + mod + " dll " + dll + " loaded partially (" + types.Length + " of " + all + " types): " + missing);
			if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThread)
			{
				LoadAlerts.AddKeySafe(2, mod, "AlertDllPartial", "{0} loaded partially: {1}. Parts of this mod may not work; the other mods loaded normally.", new object[] { dll, missing });
			}
		}

		// Distinct loader messages, at most three.
		private static string Missing(ReflectionTypeLoadException e)
		{
			List<string> seen = new List<string>();
			Exception[] list = e.LoaderExceptions;
			if (list != null)
			{
				for (int i = 0; i < list.Length && seen.Count < 3; i++)
				{
					if (list[i] == null)
					{
						continue;
					}
					FileNotFoundException f = list[i] as FileNotFoundException;
					string m = f != null && !string.IsNullOrEmpty(f.FileName) ? "missing assembly " + f.FileName : list[i].Message;
					if (!seen.Contains(m))
					{
						seen.Add(m);
					}
				}
			}
			return seen.Count == 0 ? "unknown loader error" : string.Join("; ", seen.ToArray());
		}

		private static Assembly OnResolve(object sender, ResolveEventArgs args)
		{
			if (_resolving)
			{
				return null;
			}
			_resolving = true;
			try
			{
				string name = new AssemblyName(args.Name).Name;
				if (_miss.Contains(name))
				{
					return null;
				}
				Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < loaded.Length; i++)
				{
					if (string.Equals(loaded[i].GetName().Name, name, StringComparison.OrdinalIgnoreCase))
					{
						N.ULog.Info(Tag, "resolved " + args.Name + " to the loaded " + loaded[i].GetName().Name);
						return loaded[i];
					}
				}
				string file = FindDll(name);
				if (file != null)
				{
					N.ULog.Info(Tag, "resolved " + args.Name + " from mod " + LoadAlerts.ModOf(file));
					return Assembly.LoadFile(file);
				}
				HashSet<string> next = new HashSet<string>(_miss, StringComparer.OrdinalIgnoreCase);
				next.Add(name);
				_miss = next;
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "resolve " + args.Name + " failed: " + e.Message);
			}
			finally
			{
				_resolving = false;
			}
			return null;
		}

		// <name>.dll in the top level of a mod folder: the load order first (Add_On.DirList), then any folder of the roots.
		private static string FindDll(string name)
		{
			Add_On a = Add_On.instance;
			if (a != null && a.DirList != null)
			{
				for (int d = 0; d < a.DirList.Count; d++)
				{
					DirectoryInfo dir = a.DirList[d];
					if (dir == null)
					{
						continue;
					}
					string file = Path.Combine(dir.FullName, name + ".dll");
					if (File.Exists(file))
					{
						return file;
					}
				}
			}
			List<string> roots = _roots;
			if (roots == null)
			{
				return null;
			}
			for (int r = 0; r < roots.Count; r++)
			{
				string[] dirs = Directory.GetDirectories(roots[r]);
				for (int d = 0; d < dirs.Length; d++)
				{
					string file = Path.Combine(dirs[d], name + ".dll");
					if (File.Exists(file))
					{
						N.ULog.Warn(Tag, "dll " + name + " found only in a mod folder outside the load list: " + LoadAlerts.ModOf(file));
						return file;
					}
				}
			}
			return null;
		}

		// BaseMods and the Steam workshop folder of the game (2531580), when present.
		private static List<string> Roots()
		{
			if (_roots == null)
			{
				List<string> roots = new List<string>();
				string baseMods = Application.dataPath + "/BaseMods";
				if (Directory.Exists(baseMods))
				{
					roots.Add(baseMods);
				}
				try
				{
					DirectoryInfo common = Directory.GetParent(Application.dataPath).Parent;
					if (common != null && common.Parent != null)
					{
						string ws = Path.Combine(common.Parent.FullName, "workshop/content/2531580");
						if (Directory.Exists(ws))
						{
							roots.Add(ws);
						}
					}
				}
				catch (Exception)
				{
				}
				_roots = roots;
			}
			return _roots;
		}
	}

	// LoadGuard.CrashTrail: a native crash ("Lobotomy has encountered an error and must close": Mono abort, stack overflow)
	// cannot be caught from managed code. A small file Logs/LastSession.txt is rewritten on phase changes only (each mod DLL
	// while Add_On.init loads it, scenes, day start and end, the last distinct exception at most once per second); a clean
	// quit writes "clean". At the next start a file without "clean" becomes a LoadAlerts error naming the last phase, mod
	// and exception. Mods loaded before NailOptimization are not covered.
	public static class CrashTrail
	{
		private const string Tag = "loadguard";
		internal static N.Flag Flag;
		internal static bool Booting;
		private static string _file;
		private static string _phase = "start";
		private static string _mod = string.Empty;
		private static string _err = string.Empty;
		private static float _errAt = -10f;
		private static string _phaseClock = string.Empty;
		private static string _errClock = string.Empty;
		private static bool _on;

		// LoadGuard Init (main thread, inside Add_On.init): reads the trail of the previous session, then starts a new one.
		internal static void Start()
		{
			try
			{
				string dir = Path.Combine(N.UiHost.RootDir, "Logs");
				Directory.CreateDirectory(dir);
				_file = Path.Combine(dir, "LastSession.txt");
				string prev = File.Exists(_file) ? File.ReadAllText(_file) : null;
				_on = true;
				Booting = true;
				Mark("loading mods", string.Empty);
				if (prev != null && !prev.StartsWith("clean", StringComparison.Ordinal))
				{
					Report(prev);
				}
				Application.logMessageReceived += new Application.LogCallback(OnLog);
				UnityEngine.SceneManagement.SceneManager.sceneLoaded += new UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>(OnScene);
				N.Events.On(NoticeName.OnStageStart, new Action(OnDayStart), Tag);
				N.Events.On(NoticeName.OnStageEnd, new Action(OnDayEnd), Tag);
				GameObject go = new GameObject("NailOptimization.CrashTrail");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.AddComponent<CrashTrailDriver>();
			}
			catch (Exception e)
			{
				N.ULog.Warn(Tag, "crash trail not started: " + e.Message);
			}
		}

		// TypeLoad prefix during Add_On.init: the mod whose DLL is being loaded (its Harmony_Patch runs right after).
		internal static void ModDll(string path)
		{
			if (!_on || !Booting)
			{
				return;
			}
			string mod = LoadAlerts.ModOf(path) + " / " + Path.GetFileName(path);
			if (!string.Equals(mod, _mod, StringComparison.Ordinal))
			{
				Mark("loading mods", mod);
			}
		}

		internal static void FirstFrame()
		{
			Booting = false;
			Mark("first frame (mods loaded)", string.Empty);
		}

		private static void OnScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			Mark("scene " + scene.name, string.Empty);
		}

		private static void OnDayStart()
		{
			Mark("day " + Day() + " running", string.Empty);
		}

		private static void OnDayEnd()
		{
			Mark("day " + Day() + " ending", string.Empty);
		}

		private static string Day()
		{
			try
			{
				return (PlayerModel.instance.GetDay() + 1).ToString();
			}
			catch (Exception)
			{
				return "?";
			}
		}

		private static void OnLog(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception || !_on)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (now - _errAt < 1f)
			{
				return;
			}
			string err = FirstLine(condition, 160);
			string at = FirstLine(stackTrace, 160);
			if (at.Length > 0)
			{
				err = err + " at " + at;
			}
			if (string.Equals(err, _err, StringComparison.Ordinal))
			{
				return;
			}
			_errAt = now;
			_err = err;
			_errClock = DateTime.Now.ToString("HH:mm:ss");
			Write("running");
		}

		private static string FirstLine(string s, int max)
		{
			if (string.IsNullOrEmpty(s))
			{
				return string.Empty;
			}
			int n = s.IndexOf('\n');
			string line = (n >= 0 ? s.Substring(0, n) : s).Trim();
			return line.Length > max ? line.Substring(0, max) : line;
		}

		private static void Mark(string phase, string mod)
		{
			_phase = phase;
			_mod = mod;
			_phaseClock = DateTime.Now.ToString("HH:mm:ss");
			Write("running");
		}

		internal static void Clean()
		{
			if (_on)
			{
				Write("clean");
				_on = false;
			}
		}

		private static void Write(string state)
		{
			if (_file == null)
			{
				return;
			}
			try
			{
				File.WriteAllText(_file, state + "\r\nphase=" + _phase + "\r\nphaseat=" + _phaseClock + "\r\nmod=" + _mod + "\r\nerror=" + _err + "\r\nerrorat=" + _errClock + "\r\ntime=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n");
			}
			catch (Exception)
			{
			}
		}

		private static void Report(string prev)
		{
			string phase = Field(prev, "phase=");
			string mod = Field(prev, "mod=");
			string err = Field(prev, "error=");
			string time = Field(prev, "time=");
			string phaseAt = Field(prev, "phaseat=");
			string errAt = Field(prev, "errorat=");
			N.ULog.Error(Tag, "the last session (last record " + time + ") closed unexpectedly: phase=" + phase + " (since " + phaseAt + ") mod=" + mod + " error=" + err + " (at " + errAt + ")");
			string where = mod.Length > 0 ? phase + " (" + mod + ")" : phase;
			int slash = mod.IndexOf(" / ", StringComparison.Ordinal);
			string owner = slash > 0 ? mod.Substring(0, slash) : "Game";
			// The last error can be older than the last step and unrelated to the exit: both times are shown.
			LoadAlerts.AddKeySafe(2, owner, "AlertLastCrash", "The last session closed unexpectedly (last record {0}) during: {1}, since {4}. Last error at {3}: {2}.", new object[] { time, where, err.Length > 0 ? err : "-", errAt.Length > 0 ? errAt : "-", phaseAt.Length > 0 ? phaseAt : "?" });
			LoadAlerts.OpenOnTitle = true;
		}

		private static string Field(string text, string key)
		{
			int i = text.IndexOf(key, StringComparison.Ordinal);
			if (i < 0)
			{
				return string.Empty;
			}
			i += key.Length;
			int e = text.IndexOf('\n', i);
			return (e < 0 ? text.Substring(i) : text.Substring(i, e - i)).Trim();
		}
	}

	public class CrashTrailDriver : MonoBehaviour
	{
		private void OnApplicationQuit()
		{
			CrashTrail.Clean();
		}
	}

	// LoadGuard.BaseModCheck: NailOptimization is built and tested on BaseMod (LMM) 1.3.9, which writes Add_On.version at
	// the end of Add_On.init (game/Add_On.cs 154, 165; the title shows it as "BaseMod <version>"). Read at the first frame.
	public static class BaseModCheck
	{
		internal const string Supported = "1.3.9";
		internal static N.Flag Flag;

		internal static void FirstFrame()
		{
			string v = "unknown";
			try
			{
				FieldInfo f = typeof(Add_On).GetField("version", BindingFlags.Public | BindingFlags.Static);
				object o = object.ReferenceEquals(f, null) ? null : f.GetValue(null);
				if (o is string)
				{
					v = (string)o;
				}
			}
			catch (Exception)
			{
			}
			if (string.Equals(v, Supported, StringComparison.Ordinal))
			{
				N.ULog.Info(Guard.Tag, "basemod version " + v + " ok");
				return;
			}
			N.ULog.Warn(Guard.Tag, "basemod version " + v + " is not the supported " + Supported);
			LoadAlerts.AddKeySafe(2, "BaseMod", "AlertBaseModVersion", "BaseMod (LMM) {0} is not supported: NailOptimization is made for BaseMod {1}. Stable work cannot be guaranteed.", new object[] { v, Supported });
			LoadAlerts.OpenOnTitle = true;
		}
	}

	// Load alerts (LoadGuard.Alerts, default on): critical mod load problems with the mod that caused them, shown as a warning
	// button on the title (UiActions badge) that opens NailUI.AlertBox. Sources, all observed without patching BaseMod:
	//   code    first frame: a top-level *.dll of a ModList folder missing from Add_On.AssemList -> Add_On.init stopped there
	//           (LoadFile/GetTypes threw), so the code of every later mod is missing too (BaseModding.md section 1)
	//   herror  "Herror - <mod> / <file>..." lines (a Harmony_Patch constructor or ModInitializer threw) and "AddOn Init error",
	//           read once on a worker thread from BaseMod Log.txt and, with LogViewer.AdvancedLog, from <Nail>/Logs/*
	//   data    Guard.Skip: a mod data file or entry LoadGuard had to skip, grouped per mod
	// The driver disables itself after the log scan; no per-frame cost afterwards.
	public static class LoadAlerts
	{
		internal const string ActionId = "loadguard.alerts";
		private const string Icon = "Images/warn_sign.png";
		private const int MaxShown = 14;

		private sealed class Item
		{
			internal int Level;
			internal string Mod;
			internal string Text;
			internal int Count;
			// Localized entry: NailUI.xml <ui><s id=Key>, string.Format with Args when the window opens; Text = en fallback.
			internal string Key;
			internal object[] Args;
		}

		private static MethodInfo _uiText;
		private static bool _uiTextTried;

		private static readonly List<Item> _items = new List<Item>();
		private static readonly object _gate = new object();
		private static List<string> _scanLines;
		private static bool _scanDone;
		private static bool _registered;
		private static int _warn;
		private static int _err;
		private static MethodInfo _show;
		private static PropertyInfo _openTitle;
		internal static bool On;
		// Set by BaseModCheck and CrashTrail: the list opens by itself once, 3 s after a title scene is active.
		internal static bool OpenOnTitle;
		private static int _titleFrames;

		internal static void Start()
		{
			On = true;
			GameObject go = new GameObject("NailOptimization.LoadAlerts");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<LoadAlertsDriver>();
			N.Alerts.Sink = new N.AlertSink(FromCore);
		}

		// N.Alerts sink: run-time mod problems (guard blocks, GuardLate trips). Path inside a mod folder -> folder name.
		private static void FromCore(int level, string path, string text)
		{
			string mod = ModOf(path);
			Add(level, mod == "-" && !string.IsNullOrEmpty(path) && path.IndexOf('/') < 0 && path.IndexOf('\\') < 0 ? path : mod, text);
		}

		// Main thread only. Warnings of one mod are grouped into one line with a count.
		internal static void Add(int level, string mod, string text)
		{
			if (!On)
			{
				return;
			}
			if (level < 2)
			{
				for (int i = 0; i < _items.Count; i++)
				{
					Item x = _items[i];
					if (x.Level < 2 && x.Key == null && string.Equals(x.Mod, mod, StringComparison.Ordinal))
					{
						x.Count++;
						Refresh();
						return;
					}
				}
			}
			Item it = new Item();
			it.Level = level;
			it.Mod = mod;
			it.Text = text;
			it.Count = 1;
			_items.Add(it);
			if (level >= 2)
			{
				_err++;
			}
			else
			{
				_warn++;
			}
			N.ULog.Warn(Guard.Tag, "alert " + (level >= 2 ? "error" : "warn") + " mod=" + mod + " " + text);
			Publish();
		}

		// Main thread only. Same mod + same key = one line with a count. fallbackEn is a string.Format pattern over args.
		internal static void AddKey(int level, string mod, string key, string fallbackEn, object[] args)
		{
			if (!On)
			{
				return;
			}
			for (int i = 0; i < _items.Count; i++)
			{
				Item x = _items[i];
				if (string.Equals(x.Key, key, StringComparison.Ordinal) && string.Equals(x.Mod, mod, StringComparison.Ordinal))
				{
					x.Count++;
					Refresh();
					return;
				}
			}
			Item it = new Item();
			it.Level = level;
			it.Mod = mod;
			it.Key = key;
			it.Args = args;
			it.Text = SafeFormat(fallbackEn, args);
			it.Count = 1;
			_items.Add(it);
			if (level >= 2)
			{
				_err++;
			}
			else
			{
				_warn++;
			}
			N.ULog.Warn(Guard.Tag, "alert " + (level >= 2 ? "error" : "warn") + " mod=" + mod + " " + it.Text);
			Publish();
		}

		// AddKey for callers inside a load path (Add_On.Init, day load): an alert failure is logged, never thrown.
		internal static void AddKeySafe(int level, string mod, string key, string fallbackEn, object[] args)
		{
			try
			{
				AddKey(level, mod, key, fallbackEn, args);
			}
			catch (Exception e)
			{
				N.ULog.Warn(Guard.Tag, "alert " + key + " not added: " + e.Message);
			}
		}

		private static string SafeFormat(string pattern, object[] args)
		{
			if (args == null || args.Length == 0)
			{
				return pattern;
			}
			try
			{
				return string.Format(pattern, args);
			}
			catch (FormatException)
			{
				return pattern;
			}
		}

		// Localized text of an item; the en text when NailUI is absent or has no such id.
		private static string TextOf(Item x)
		{
			if (x.Key == null)
			{
				return x.Text;
			}
			try
			{
				if (!_uiTextTried)
				{
					_uiTextTried = true;
					Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
					for (int i = 0; i < all.Length && object.ReferenceEquals(_uiText, null); i++)
					{
						Type t = all[i].GetType("NailUI.UiText", false);
						if (!object.ReferenceEquals(t, null))
						{
							_uiText = t.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string) }, null);
						}
					}
				}
				if (object.ReferenceEquals(_uiText, null))
				{
					return x.Text;
				}
				string pattern = _uiText.Invoke(null, new object[] { x.Key, null }) as string;
				return string.IsNullOrEmpty(pattern) ? x.Text : SafeFormat(pattern, x.Args);
			}
			catch (Exception)
			{
				return x.Text;
			}
		}

		// Driver, every frame until it returns false: true while an open-on-title request waits. Scene check every 30 frames.
		internal static bool TitleTick()
		{
			if (!OpenOnTitle)
			{
				return false;
			}
			if (Time.frameCount % 30 != 0)
			{
				return true;
			}
			string s = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
			if (s == null || s.IndexOf("Title", StringComparison.Ordinal) < 0)
			{
				_titleFrames = 0;
				return true;
			}
			_titleFrames += 30;
			if (_titleFrames < 180)
			{
				return true;
			}
			OpenOnTitle = false;
			try
			{
				OnClick();
			}
			catch (Exception e)
			{
				N.ULog.Warn(Guard.Tag, "alerts: open on title failed: " + e.Message);
			}
			return false;
		}

		internal static void AddSkip(string path, string what)
		{
			if (On)
			{
				Add(1, ModOf(path), what);
			}
		}

		// Folder name after BaseMods/ or the workshop folder 2531580/; "-" when the path is not inside a mod.
		internal static string ModOf(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return "-";
			}
			string p = path.Replace('\\', '/');
			string[] marks = new string[] { "/BaseMods/", "/2531580/" };
			for (int i = 0; i < marks.Length; i++)
			{
				int k = p.IndexOf(marks[i], StringComparison.OrdinalIgnoreCase);
				if (k >= 0)
				{
					string rest = p.Substring(k + marks[i].Length);
					int e = rest.IndexOf('/');
					return e > 0 ? rest.Substring(0, e) : rest;
				}
			}
			return "-";
		}

		private static void Publish()
		{
			if (!_registered)
			{
				_registered = N.UiActions.Add(ActionId, "Mod problems", "\u041F\u0440\u043E\u0431\u043B\u0435\u043C\u044B \u043C\u043E\u0434\u043E\u0432", Icon, new UnityEngine.Events.UnityAction(OnClick), null);
			}
			N.UiActions.SetBadges(ActionId, _warn, _err);
			Refresh();
		}

		// A new entry while our list is open: show it at once (NailUI.AlertBox.OpenTitle; older NailUI: no refresh).
		private static void Refresh()
		{
			try
			{
				if (object.ReferenceEquals(_openTitle, null))
				{
					return;
				}
				string open = _openTitle.GetValue(null, null) as string;
				if (open == null || (open != "Mod problems" && open != "\u041F\u0440\u043E\u0431\u043B\u0435\u043C\u044B \u043C\u043E\u0434\u043E\u0432"))
				{
					return;
				}
				bool ru = Ru();
				ShowBox(Title(ru), Body(ru));
			}
			catch (Exception e)
			{
				N.ULog.Warn(Guard.Tag, "alerts: refresh failed: " + e.Message);
			}
		}

		private static string Title(bool ru)
		{
			return ru ? "\u041F\u0440\u043E\u0431\u043B\u0435\u043C\u044B \u043C\u043E\u0434\u043E\u0432" : "Mod problems";
		}

		// One block per mod in order of the first entry: the mod name once (red if any error), then each entry.
		private static string Body(bool ru)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			List<string> mods = new List<string>();
			for (int i = 0; i < _items.Count; i++)
			{
				if (mods.IndexOf(_items[i].Mod) < 0)
				{
					mods.Add(_items[i].Mod);
				}
			}
			int shown = 0;
			for (int m = 0; m < mods.Count && shown < MaxShown; m++)
			{
				string mod = mods[m];
				int n = 0;
				bool err = false;
				for (int i = 0; i < _items.Count; i++)
				{
					if (string.Equals(_items[i].Mod, mod, StringComparison.Ordinal))
					{
						n++;
						err = err || _items[i].Level >= 2;
					}
				}
				sb.Append(err ? "<color=#ff5c4d>" : "<color=#ffd24d>").Append(mod).Append("</color>:");
				for (int i = 0; i < _items.Count && shown < MaxShown; i++)
				{
					Item x = _items[i];
					if (!string.Equals(x.Mod, mod, StringComparison.Ordinal))
					{
						continue;
					}
					sb.Append(n > 1 ? "\n  - " : " ").Append(TextOf(x));
					if (x.Count > 1)
					{
						sb.Append(ru ? " (\u0438 \u0435\u0449\u0451 " : " (and ").Append(x.Count - 1).Append(ru ? ")" : " more)");
					}
					shown++;
				}
				sb.Append('\n');
			}
			if (_items.Count > shown)
			{
				sb.Append(ru ? "\u0418 \u0435\u0449\u0451 " : "And ").Append(_items.Count - shown).Append(ru ? ", \u0441\u043C. NailOptimization.log" : " more, see NailOptimization.log");
			}
			return sb.ToString();
		}

		private static bool Ru()
		{
			try
			{
				GlobalGameManager.LanguageFont lf = GlobalGameManager.currentLanguageFont;
				return !object.ReferenceEquals(lf, null) && lf.language != null && lf.language.Trim().ToLowerInvariant() == "ru";
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void OnClick()
		{
			bool ru = Ru();
			N.ULog.Info(Guard.Tag, "alerts shown errors=" + _err + " warnings=" + _warn);
			try
			{
				if (!ShowBox(Title(ru), Body(ru)))
				{
					N.ULog.Warn(Guard.Tag, "alerts: NailUI.AlertBox not available; see the alert lines above");
				}
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "alerts: window failed: " + e);
			}
		}

		private static bool ShowBox(string title, string body)
		{
			if (object.ReferenceEquals(_show, null))
			{
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length && object.ReferenceEquals(_show, null); i++)
				{
					Type t = all[i].GetType("NailUI.AlertBox", false);
					if (!object.ReferenceEquals(t, null))
					{
						_show = t.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string) }, null);
						_openTitle = t.GetProperty("OpenTitle", BindingFlags.Public | BindingFlags.Static);
					}
				}
				if (object.ReferenceEquals(_show, null))
				{
					return false;
				}
			}
			object r = _show.Invoke(null, new object[] { title, body });
			return r is bool && (bool)r;
		}

		// First frame: Add_On.init has finished. Mod code check on the main thread (file names only), log scan on a worker.
		internal static void FirstFrame()
		{
			if (CrashTrail.Flag != null && CrashTrail.Flag.On)
			{
				CrashTrail.FirstFrame();
			}
			if (BaseModCheck.Flag != null && BaseModCheck.Flag.On)
			{
				BaseModCheck.FirstFrame();
			}
			try
			{
				CheckCode();
			}
			catch (Exception e)
			{
				N.ULog.Warn(Guard.Tag, "alerts: code check failed: " + e.Message);
			}
			List<string> files = new List<string>();
			try
			{
				string log = ModDebug.LogFilePath;
				if (!string.IsNullOrEmpty(log))
				{
					files.Add(log);
				}
			}
			catch (Exception)
			{
			}
			try
			{
				string dir = Path.Combine(N.UiHost.RootDir, "Logs");
				DateTime since = DateTime.Now.AddSeconds(-Time.realtimeSinceStartup - 30f);
				if (Directory.Exists(dir))
				{
					string[] fs = Directory.GetFiles(dir);
					for (int i = 0; i < fs.Length; i++)
					{
						if (File.GetLastWriteTime(fs[i]) >= since && !fs[i].EndsWith("Profiler.log", StringComparison.OrdinalIgnoreCase))
						{
							files.Add(fs[i]);
						}
					}
				}
			}
			catch (Exception)
			{
			}
			System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(Scan), files);
		}

		private static void CheckCode()
		{
			Add_On a = Add_On.instance;
			if (a == null || a.ModList == null || a.AssemList == null)
			{
				return;
			}
			// A failed LoadFile/GetTypes stops the loader: that mod and every later one have no loaded assembly. So the culprit
			// is the first mod with a DLL after the last mod that has one loaded. A DLL that is not an assembly, or a path that
			// Mono reports differently (non-ASCII names), is not a culprit when later mods loaded. Directories are compared too.
			Dictionary<string, bool> loaded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, bool> dirs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			// LoadFile of a DLL whose assembly name is already loaded returns that assembly (Location of the first copy).
			Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < a.AssemList.Count; i++)
			{
				try
				{
					string loc = Path.GetFullPath(a.AssemList[i].Location);
					loaded[loc] = true;
					dirs[Path.GetDirectoryName(loc)] = true;
					names[a.AssemList[i].GetName().Name] = true;
				}
				catch (Exception)
				{
				}
			}
			int n = a.ModList.Count;
			string[] missing = new string[n];
			bool[] hasDll = new bool[n];
			// Per mod: the first DLL (GetFiles order, the loader's order) after its last loaded DLL that is not loaded.
			string[] tail = new string[n];
			int lastLoaded = -1;
			for (int m = 0; m < n; m++)
			{
				ModInfo mi = a.ModList[m];
				if (mi == null || mi.modpath == null || !mi.modpath.Exists)
				{
					continue;
				}
				FileInfo[] fs = mi.modpath.GetFiles();
				bool any = false;
				for (int f = 0; f < fs.Length; f++)
				{
					if (fs[f].Name.Contains(".dll"))
					{
						hasDll[m] = true;
						if (loaded.ContainsKey(Path.GetFullPath(fs[f].FullName)) || SameName(fs[f].FullName, names))
						{
							any = true;
							tail[m] = null;
						}
						else
						{
							if (missing[m] == null)
							{
								missing[m] = fs[f].Name;
							}
							if (tail[m] == null)
							{
								tail[m] = fs[f].Name;
							}
						}
					}
				}
				if (!any && hasDll[m])
				{
					try
					{
						any = dirs.ContainsKey(Path.GetFullPath(mi.modpath.FullName).TrimEnd(new char[] { Path.DirectorySeparatorChar }));
					}
					catch (Exception)
					{
					}
				}
				if (any)
				{
					lastLoaded = m;
				}
			}
			string culprit = null;
			string file = null;
			int later = 0;
			if (lastLoaded >= 0 && tail[lastLoaded] != null)
			{
				// One DLL of the last loaded mod loaded and a later one of the same mod threw.
				culprit = a.ModList[lastLoaded].modpath.Name;
				file = tail[lastLoaded];
			}
			for (int m = lastLoaded + 1; m < n; m++)
			{
				if (!hasDll[m])
				{
					continue;
				}
				if (culprit == null)
				{
					culprit = a.ModList[m].modpath.Name;
					file = missing[m];
				}
				else
				{
					later++;
				}
			}
			N.ULog.Info(Guard.Tag, "alerts: code check mods=" + a.ModList.Count + " assemblies=" + a.AssemList.Count + (culprit != null ? " stopped at " + culprit + "/" + file : " ok"));
			if (culprit != null)
			{
				Add(2, culprit, Ru() ? file + " \u043D\u0435 \u0437\u0430\u0433\u0440\u0443\u0437\u0438\u043B\u0441\u044F (\u043D\u0435\u0442 \u0437\u0430\u0432\u0438\u0441\u0438\u043C\u043E\u0441\u0442\u0438 \u0438\u043B\u0438 \u0444\u0430\u0439\u043B \u043F\u043E\u0432\u0440\u0435\u0436\u0434\u0451\u043D); \u043A\u043E\u0434 \u0435\u0449\u0451 " + later + " \u043C\u043E\u0434\u043E\u0432 \u043F\u043E\u0441\u043B\u0435 \u043D\u0435\u0433\u043E \u043D\u0435 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043D" : file + " failed to load (missing dependency or broken file); the code of " + later + " later mods was not loaded");
			}
		}

		// Reads the assembly name from the file's metadata; false when the file is no assembly or unreadable.
		private static bool SameName(string path, Dictionary<string, bool> names)
		{
			try
			{
				return names.ContainsKey(AssemblyName.GetAssemblyName(path).Name);
			}
			catch (Exception)
			{
				return false;
			}
		}

		// Worker thread: System.IO and strings only.
		private static void Scan(object state)
		{
			List<string> found = new List<string>();
			List<string> files = state as List<string>;
			for (int f = 0; files != null && f < files.Count; f++)
			{
				try
				{
					ScanFile(files[f], found);
				}
				catch (Exception)
				{
				}
			}
			System.Threading.Monitor.Enter(_gate);
			try
			{
				_scanLines = found;
				_scanDone = true;
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
		}

		private static void ScanFile(string path, List<string> found)
		{
			FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			try
			{
				StreamReader r = new StreamReader(fs);
				string line;
				while ((line = r.ReadLine()) != null)
				{
					int h = line.IndexOf("Herror", StringComparison.Ordinal);
					if (h < 0)
					{
						h = line.IndexOf("AddOn Init error", StringComparison.Ordinal);
					}
					if (h >= 0 && found.Count < 200)
					{
						string t = line.Substring(h);
						if (!found.Contains(t))
						{
							found.Add(t);
						}
					}
				}
			}
			finally
			{
				fs.Close();
			}
		}

		// Main thread; true once the scan result was taken.
		internal static bool TakeScan()
		{
			List<string> lines;
			System.Threading.Monitor.Enter(_gate);
			try
			{
				if (!_scanDone)
				{
					return false;
				}
				lines = _scanLines;
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
			bool ru = Ru();
			bool stopped = false;
			for (int i = 0; i < lines.Count; i++)
			{
				string t = lines[i];
				if (t.StartsWith("Herror", StringComparison.Ordinal))
				{
					// "Herror - <mod> / <file><message>"
					string rest = t.StartsWith("Herror - ", StringComparison.Ordinal) ? t.Substring(9) : t.Substring(6);
					int sl = rest.IndexOf(" / ", StringComparison.Ordinal);
					string mod = sl > 0 ? rest.Substring(0, sl).Trim() : "-";
					string msg = sl > 0 ? rest.Substring(sl + 3) : rest;
					if (msg.Length > 160)
					{
						msg = msg.Substring(0, 160) + "...";
					}
					Add(2, mod, (ru ? "\u043E\u0448\u0438\u0431\u043A\u0430 \u043F\u0440\u0438 \u0437\u0430\u043F\u0443\u0441\u043A\u0435 \u043A\u043E\u0434\u0430, \u0435\u0433\u043E \u043F\u0430\u0442\u0447\u0438 \u043C\u043E\u0433\u0443\u0442 \u043D\u0435 \u0440\u0430\u0431\u043E\u0442\u0430\u0442\u044C: " : "its code threw at start, its patches may be missing: ") + msg);
				}
				else
				{
					stopped = true;
				}
			}
			if (stopped && _err == 0)
			{
				Add(2, "BaseMod", ru ? "\u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0430 \u043C\u043E\u0434\u043E\u0432 \u043F\u0440\u0435\u0440\u0432\u0430\u043D\u0430 (AddOn Init error), \u0441\u043C. Log.txt" : "mod loading stopped (AddOn Init error), see Log.txt");
			}
			N.ULog.Info(Guard.Tag, "alerts: log scan lines=" + lines.Count + " errors=" + _err + " warnings=" + _warn);
			return true;
		}
	}

	// Boot file list. Some mods scan the whole BaseMods tree in their Harmony_Patch constructor (TrainEgoFix:
	// Directory.GetFiles(Application.dataPath + "/BaseMods", "*.*", AllDirectories), 3.4 s in the b1 boot profile). The same
	// call runs on a worker thread from LoadGuard init while the other mods load. A Prefix on
	// Directory.GetFiles(string, string, SearchOption) (no exception handler in its IL) answers exactly that call (same path
	// string, so the returned paths are the same; same pattern; AllDirectories) with a copy of the list until the first frame.
	// Every other call runs the original. After the first frame the list is built again on a worker and the log says whether
	// both lists are equal (a file created during start-up would show here).
	public static class BootFiles
	{
		private const string Pattern = "*.*";

		private static string _root;
		private static System.Threading.Thread _worker;
		private static int _workerId;
		private static string[] _files;
		private static string _error;
		private static volatile bool _open;
		private static int _served;
		private static double _waitMs;
		private static double _scanMs;

		internal static void Start(HarmonyInstance harmony)
		{
			_root = Application.dataPath + "/BaseMods";
			MethodBase target = typeof(Directory).GetMethod("GetFiles", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string), typeof(SearchOption) }, null);
			if (object.ReferenceEquals(target, null))
			{
				N.ULog.Warn(Guard.Tag, "boot files: Directory.GetFiles(string, string, SearchOption) not found, off");
				return;
			}
			_worker = new System.Threading.Thread(Scan);
			_worker.IsBackground = true;
			_workerId = _worker.ManagedThreadId;
			_open = true;
			_worker.Start();
			if (!N.PatchUtil.Install(harmony, Guard.Tag, target, new HarmonyMethod(typeof(BootFiles).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)), null))
			{
				_open = false;
				return;
			}
			GameObject go = new GameObject("NailOptimization.BootFiles");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<BootFilesDriver>();
		}

		// Worker thread: System.IO only.
		private static void Scan()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				_files = Directory.GetFiles(_root, Pattern, SearchOption.AllDirectories);
			}
			catch (Exception e)
			{
				_files = null;
				_error = e.GetType().Name + ": " + e.Message;
			}
			_scanMs = sw.Elapsed.TotalMilliseconds;
		}

		public static bool Prefix(string path, string searchPattern, SearchOption searchOption, ref string[] __result)
		{
			if (!_open || searchOption != SearchOption.AllDirectories || !string.Equals(path, _root, StringComparison.Ordinal) || !string.Equals(searchPattern, Pattern, StringComparison.Ordinal))
			{
				return true;
			}
			if (System.Threading.Thread.CurrentThread.ManagedThreadId == _workerId)
			{
				return true;
			}
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			_worker.Join();
			_waitMs += sw.Elapsed.TotalMilliseconds;
			string[] files = _files;
			if (files == null)
			{
				return true;
			}
			__result = (string[])files.Clone();
			_served++;
			return false;
		}

		// First frame: stop answering and start the check scan.
		internal static System.Threading.Thread Close()
		{
			_open = false;
			if (_worker != null)
			{
				_worker.Join();
			}
			string[] first = _files;
			N.ULog.Info(Guard.Tag, "boot files: " + (first == null ? "scan failed (" + _error + ")" : first.Length + " files in " + _scanMs.ToString("0") + " ms on a worker") + ", served " + _served + " call(s), waited " + _waitMs.ToString("0") + " ms");
			if (first == null || _served == 0)
			{
				return null;
			}
			System.Threading.Thread check = new System.Threading.Thread(Check);
			check.IsBackground = true;
			check.Start();
			return check;
		}

		private static string _checkResult;

		// Worker thread: System.IO and strings only.
		private static void Check()
		{
			try
			{
				string[] now = Directory.GetFiles(_root, Pattern, SearchOption.AllDirectories);
				string[] old = _files;
				int diff = 0;
				string sample = null;
				Dictionary<string, bool> set = new Dictionary<string, bool>(StringComparer.Ordinal);
				for (int i = 0; i < old.Length; i++)
				{
					set[old[i]] = true;
				}
				for (int i = 0; i < now.Length; i++)
				{
					if (!set.ContainsKey(now[i]))
					{
						diff++;
						if (sample == null)
						{
							sample = now[i];
						}
					}
				}
				bool order = now.Length == old.Length;
				for (int i = 0; order && i < now.Length; i++)
				{
					order = string.Equals(now[i], old[i], StringComparison.Ordinal);
				}
				_checkResult = order ? "lists equal (" + now.Length + " files, same order)" : "lists differ: boot " + old.Length + ", now " + now.Length + ", new after boot " + diff + (sample == null ? string.Empty : " (e.g. " + sample.Substring(_root.Length) + ")");
			}
			catch (Exception e)
			{
				_checkResult = "check failed: " + e.GetType().Name + ": " + e.Message;
			}
		}

		internal static string CheckResult
		{
			get
			{
				return _checkResult;
			}
		}
	}

	public class BootFilesDriver : MonoBehaviour
	{
		private System.Threading.Thread _check;
		private bool _closed;

		private void Update()
		{
			if (!_closed)
			{
				_closed = true;
				try
				{
					_check = BootFiles.Close();
				}
				catch (Exception e)
				{
					N.ULog.Error(Guard.Tag, "boot files close failed: " + e);
				}
				if (_check == null)
				{
					enabled = false;
				}
				return;
			}
			if (_check.IsAlive)
			{
				return;
			}
			enabled = false;
			N.ULog.Info(Guard.Tag, "boot files check: " + BootFiles.CheckResult);
		}
	}

	// Boot XML trace (diagnostic, with LoadGuard.BootProfile). Most static data loaders have an exception handler and cannot be
	// timed by a patch of their own (GL-53), but they read their data through XmlDocument (Load and LoadXml have handlers too). Every new XmlDocument
	// constructed before the first frame is stamped with its calling method (first stack frame outside System.Xml, mscorlib and this
	// class). Consecutive calls from one caller form a run; the time from a run's first call to the next run's first call is
	// the cost of that caller's step (parsing plus whatever it does with the data). Runs over 50 ms are logged.
	// Diagnostics under LoadGuard.BootProfile: logs each Prefab.LoadPrefab call over 100 ms (name, caller), up to 40 lines.
	// Prefab.LoadPrefab has no exception handlers (GL-53 ok).
	public static class BootPrefabTrace
	{
		private static System.Diagnostics.Stopwatch _sw;
		private static int _lines;

		internal static void Start(System.Diagnostics.Stopwatch sw)
		{
			_sw = sw;
			MethodInfo target = typeof(Prefab).GetMethod("LoadPrefab", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
			if (object.ReferenceEquals(target, null))
			{
				N.ULog.Warn(Guard.Tag, "boot prefab: Prefab.LoadPrefab not found");
				return;
			}
			HarmonyInstance harmony = HarmonyInstance.Create(LoadGuardModule.HarmonyId + ".bootprefab");
			harmony.Patch(target, new HarmonyMethod(typeof(BootPrefabTrace).GetMethod("Pre", BindingFlags.Public | BindingFlags.Static)), new HarmonyMethod(typeof(BootPrefabTrace).GetMethod("Post", BindingFlags.Public | BindingFlags.Static)));
		}

		public static void Pre(out long __state)
		{
			__state = _lines < 40 ? _sw.ElapsedTicks : 0L;
		}

		public static void Post(long __state, string name)
		{
			if (__state == 0L)
			{
				return;
			}
			double ms = (_sw.ElapsedTicks - __state) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			if (ms < 100.0)
			{
				return;
			}
			_lines++;
			string caller = "?";
			System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(1, false);
			for (int i = 0; i < st.FrameCount; i++)
			{
				MethodBase m = st.GetFrame(i).GetMethod();
				if (object.ReferenceEquals(m, null) || object.ReferenceEquals(m.DeclaringType, null) || m.DeclaringType.Name == "Prefab" || m.Name.IndexOf("LoadPrefab", StringComparison.Ordinal) >= 0)
				{
					continue;
				}
				caller = m.DeclaringType.FullName + "." + m.Name;
				break;
			}
			N.ULog.Info(Guard.Tag, "boot prefab: " + name + " " + ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms at +" + (__state * 1000L / System.Diagnostics.Stopwatch.Frequency) + " ms, from " + caller);
		}
	}

	public static class BootXmlTrace
	{
		private static System.Diagnostics.Stopwatch _sw;
		private static volatile bool _open;
		private static readonly List<long> _at = new List<long>();
		private static readonly List<long> _parse = new List<long>();
		private static readonly List<string> _who = new List<string>();
		private static readonly object _gate = new object();
		private static int _patched;

		internal static void Start(System.Diagnostics.Stopwatch sw)
		{
			_sw = sw;
			_open = true;
			HarmonyInstance harmony = HarmonyInstance.Create(LoadGuardModule.HarmonyId + ".bootxml");
			MethodInfo pre = typeof(BootXmlTrace).GetMethod("Pre", BindingFlags.Public | BindingFlags.Static);
			MethodInfo post = typeof(BootXmlTrace).GetMethod("Post", BindingFlags.Public | BindingFlags.Static);
			MethodBase[] all = typeof(XmlDocument).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
			for (int i = 0; i < all.Length; i++)
			{
				try
				{
					MethodBody body = all[i].GetMethodBody();
					if (body == null || body.ExceptionHandlingClauses.Count > 0)
					{
						N.ULog.Info(Guard.Tag, "boot xml: skipped " + all[i] + (body == null ? " (no body)" : " (exception handler)"));
						continue;
					}
					harmony.Patch(all[i], new HarmonyMethod(pre), new HarmonyMethod(post));
					_patched++;
				}
				catch (Exception e)
				{
					N.ULog.Warn(Guard.Tag, "boot xml: patch of " + all[i] + " failed: " + e.GetType().Name);
				}
			}
		}

		public static void Pre(out long __state)
		{
			__state = _open ? _sw.ElapsedTicks : 0L;
		}

		public static void Post(long __state)
		{
			if (!_open || __state == 0L)
			{
				return;
			}
			long end = _sw.ElapsedTicks;
			string who = Caller();
			System.Threading.Monitor.Enter(_gate);
			try
			{
				_at.Add(__state);
				_parse.Add(end - __state);
				_who.Add(who);
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
		}

		private static string Caller()
		{
			System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(1, false);
			for (int i = 0; i < st.FrameCount; i++)
			{
				MethodBase m = st.GetFrame(i).GetMethod();
				if (object.ReferenceEquals(m, null) || object.ReferenceEquals(m.DeclaringType, null))
				{
					continue;
				}
				Assembly a = m.DeclaringType.Assembly;
				if (object.ReferenceEquals(a, typeof(XmlDocument).Assembly) || object.ReferenceEquals(a, typeof(object).Assembly) || object.ReferenceEquals(a, typeof(BootXmlTrace).Assembly))
				{
					continue;
				}
				return m.DeclaringType.FullName + "." + m.Name + " [" + a.GetName().Name + "]";
			}
			return "(unknown)";
		}

		internal static void Dump(double f, long end)
		{
			_open = false;
			System.Threading.Monitor.Enter(_gate);
			try
			{
				N.ULog.Info(Guard.Tag, "boot xml: " + _patched + " method(s) patched, " + _at.Count + " call(s)");
				int i = 0;
				while (i < _at.Count)
				{
					int j = i;
					long parse = 0L;
					while (j < _at.Count && _who[j] == _who[i])
					{
						parse += _parse[j];
						j++;
					}
					long next = j < _at.Count ? _at[j] : end;
					double ms = (next - _at[i]) * f;
					if (ms >= 50.0)
					{
						N.ULog.Info(Guard.Tag, "boot xml: +" + (_at[i] * f).ToString("0") + " " + ms.ToString("0") + " ms (parse " + (parse * f).ToString("0") + " ms, calls " + (j - i) + ") " + _who[i]);
					}
					i = j;
				}
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
		}
	}

	// Boot trace (diagnostic, with LoadGuard.BootProfile). Mod DLLs initialize inside GlobalGameManager.Awake -> MapGraph.LoadMap
	// (first toucher of Add_On); the static data loaders after them run later in the same Awake. Each target below gets a
	// timing Prefix/Postfix pair (one slot class per target: Harmony 1.0.9.1 has no __originalMethod). Targets whose IL has an
	// exception handler are skipped (the patched copy would lose it, GL-53). The table is logged with the boot profile.
	public static class BootTrace
	{
		private static readonly string[] Targets = new string[]
		{
			"GameStaticDataLoader.LoadStaticData",
			"ExternalFontDataLoader.Load",
			"LocalizeTextDataLoader.LoadLocalizeTextData",
			"StoryDataLoader.LoadStoryData",
			"EquipmentDataLoader.Load",
			"AgentTitleDataLoader.Load",
			"MissionTypeList.LoadData",
			"GameStaticDataLoader.LoadSKillData",
			"CreatureDataLoader.Load",
			"RabbitDataLoader.Load",
			"GameStaticDataLoader.LoadLyricData",
			"GameStaticDataLoader.LoadNewLyricData",
			"GameStaticDataLoader.LoadResearchItemData",
			"GameStaticDataLoader.LoadSefiraDescData",
			"GameStaticDataLoader.LoadAngelaDescData",
			"GameStaticDataLoader.LoadOfficerActionList",
			"GameStaticDataLoader.LoadAgentNameData",
			"GameStaticDataLoader.LoadStageRewardData",
			"GameStaticDataLoader.LoadSefiraIsolateData",
			"GameStaticDataLoader.LoadSpriteLoadingData",
			"GameStaticDataLoader.LoadPanicData",
			"GameStaticDataLoader.LoadHierarchicalData",
			"GameStaticDataLoader.LoadFactionData",
			"GameStaticDataLoader.LoadItemObjectData",
			"GameStaticDataLoader.LoadRandomEventInfo",
			"GameStaticDataLoader.LoadWorkerSpineData",
			"TutorialDataLoader.Load",
			"AgentManager.Init",
			"GlobalGameManager.SetLanguageFont",
			"GlobalGameManager.LoadGlobalData",
			"WorkerSpine.WorkerSpineAnimatorManager.Init",
			"MapGraph.LoadMap",
			"SaveUtil.ReadSerializableFile",
			"CreatureManager.LoadObserveData",
			"GlobalEtcDataModel.LoadGlobalData",
			"ResearchDataModel.LoadData",
			"MissionManager.LoadData",
			"InventoryModel.LoadGlobalData",
			"SefiraCharacterManager.LoadData",
			"ManualUI.Reload",
			"AlterTitleController.Awake",
			"AlterTitleController.Start"
		};

		private static readonly Type[] Slots = new Type[]
		{
			typeof(BootSlot00),
			typeof(BootSlot01),
			typeof(BootSlot02),
			typeof(BootSlot03),
			typeof(BootSlot04),
			typeof(BootSlot05),
			typeof(BootSlot06),
			typeof(BootSlot07),
			typeof(BootSlot08),
			typeof(BootSlot09),
			typeof(BootSlot10),
			typeof(BootSlot11),
			typeof(BootSlot12),
			typeof(BootSlot13),
			typeof(BootSlot14),
			typeof(BootSlot15),
			typeof(BootSlot16),
			typeof(BootSlot17),
			typeof(BootSlot18),
			typeof(BootSlot19),
			typeof(BootSlot20),
			typeof(BootSlot21),
			typeof(BootSlot22),
			typeof(BootSlot23),
			typeof(BootSlot24),
			typeof(BootSlot25),
			typeof(BootSlot26),
			typeof(BootSlot27),
			typeof(BootSlot28),
			typeof(BootSlot29),
			typeof(BootSlot30),
			typeof(BootSlot31),
			typeof(BootSlot32),
			typeof(BootSlot33),
			typeof(BootSlot34),
			typeof(BootSlot35),
			typeof(BootSlot36),
			typeof(BootSlot37),
			typeof(BootSlot38),
			typeof(BootSlot39),
			typeof(BootSlot40),
			typeof(BootSlot41)
		};

		private static System.Diagnostics.Stopwatch _sw;
		private static readonly long[] _first = new long[Targets.Length];
		private static readonly long[] _total = new long[Targets.Length];
		private static readonly int[] _calls = new int[Targets.Length];
		private static readonly string[] _state = new string[Targets.Length];

		internal static long Now()
		{
			return _sw == null ? 0L : _sw.ElapsedTicks;
		}

		internal static void End(int i, long start)
		{
			if (_sw == null)
			{
				return;
			}
			if (_calls[i] == 0)
			{
				_first[i] = start;
			}
			_calls[i]++;
			_total[i] += _sw.ElapsedTicks - start;
		}

		internal static void Start(System.Diagnostics.Stopwatch sw)
		{
			_sw = sw;
			HarmonyInstance harmony = HarmonyInstance.Create(LoadGuardModule.HarmonyId + ".boottrace");
			Assembly game = typeof(GlobalGameManager).Assembly;
			for (int i = 0; i < Targets.Length; i++)
			{
				try
				{
					int dot = Targets[i].LastIndexOf('.');
					Type type = game.GetType(Targets[i].Substring(0, dot), false);
					MethodInfo m = object.ReferenceEquals(type, null) ? null : Find(type, Targets[i].Substring(dot + 1));
					if (object.ReferenceEquals(m, null))
					{
						_state[i] = "not found";
						continue;
					}
					MethodBody body = m.GetMethodBody();
					if (body == null || body.ExceptionHandlingClauses.Count > 0)
					{
						_state[i] = body == null ? "no body" : "skipped: exception handler";
						continue;
					}
					harmony.Patch(m, new HarmonyMethod(Slots[i].GetMethod("Pre")), new HarmonyMethod(Slots[i].GetMethod("Post")));
					_state[i] = "ok";
				}
				catch (Exception e)
				{
					_state[i] = "patch failed: " + e.GetType().Name;
				}
			}
		}

		private static MethodInfo Find(Type type, string name)
		{
			MethodInfo best = null;
			MethodInfo[] all = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].Name == name && !all[i].IsGenericMethodDefinition && (object.ReferenceEquals(best, null) || all[i].GetParameters().Length < best.GetParameters().Length))
				{
					best = all[i];
				}
			}
			return best;
		}

		internal static void Dump(double f)
		{
			for (int i = 0; i < Targets.Length; i++)
			{
				string what = _calls[i] > 0 ? "at +" + (_first[i] * f).ToString("0") + " ms, " + (_total[i] * f).ToString("0") + " ms, calls " + _calls[i] : (_state[i] == "ok" ? "not called" : _state[i]);
				N.ULog.Info(Guard.Tag, "boot trace: " + Targets[i] + " " + what);
			}
		}
	}

	public static class BootSlot00
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(0, __state);
		}
	}

	public static class BootSlot01
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(1, __state);
		}
	}

	public static class BootSlot02
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(2, __state);
		}
	}

	public static class BootSlot03
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(3, __state);
		}
	}

	public static class BootSlot04
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(4, __state);
		}
	}

	public static class BootSlot05
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(5, __state);
		}
	}

	public static class BootSlot06
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(6, __state);
		}
	}

	public static class BootSlot07
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(7, __state);
		}
	}

	public static class BootSlot08
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(8, __state);
		}
	}

	public static class BootSlot09
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(9, __state);
		}
	}

	public static class BootSlot10
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(10, __state);
		}
	}

	public static class BootSlot11
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(11, __state);
		}
	}

	public static class BootSlot12
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(12, __state);
		}
	}

	public static class BootSlot13
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(13, __state);
		}
	}

	public static class BootSlot14
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(14, __state);
		}
	}

	public static class BootSlot15
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(15, __state);
		}
	}

	public static class BootSlot16
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(16, __state);
		}
	}

	public static class BootSlot17
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(17, __state);
		}
	}

	public static class BootSlot18
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(18, __state);
		}
	}

	public static class BootSlot19
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(19, __state);
		}
	}

	public static class BootSlot20
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(20, __state);
		}
	}

	public static class BootSlot21
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(21, __state);
		}
	}

	public static class BootSlot22
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(22, __state);
		}
	}

	public static class BootSlot23
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(23, __state);
		}
	}

	public static class BootSlot24
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(24, __state);
		}
	}

	public static class BootSlot25
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(25, __state);
		}
	}

	public static class BootSlot26
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(26, __state);
		}
	}

	public static class BootSlot27
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(27, __state);
		}
	}

	public static class BootSlot28
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(28, __state);
		}
	}

	public static class BootSlot29
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(29, __state);
		}
	}

	public static class BootSlot30
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(30, __state);
		}
	}

	public static class BootSlot31
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(31, __state);
		}
	}

	public static class BootSlot32
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(32, __state);
		}
	}

	public static class BootSlot33
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(33, __state);
		}
	}

	public static class BootSlot34
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(34, __state);
		}
	}

	public static class BootSlot35
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(35, __state);
		}
	}

	public static class BootSlot36
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(36, __state);
		}
	}

	public static class BootSlot37
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(37, __state);
		}
	}

	public static class BootSlot38
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(38, __state);
		}
	}

	public static class BootSlot39
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(39, __state);
		}
	}

	public static class BootSlot40
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(40, __state);
		}
	}

	public static class BootSlot41
	{
		public static void Pre(out long __state)
		{
			__state = BootTrace.Now();
		}

		public static void Post(long __state)
		{
			BootTrace.End(41, __state);
		}
	}

	// Boot profile (diagnostic, default off). Add_On.init handles one DLL after the other: LoadFile, GetTypes, the Harmony_Patch
	// constructor. AppDomain.AssemblyLoad fires at each first LoadFile, so the time from one load to the next is the cost of
	// the earlier DLL (its types and constructor, with the dependencies it pulls in). The loads are stamped by a Stopwatch and
	// written to the log on the first frame; the last line is the time from the last load to that frame (mod data loading).
	public static class BootProfile
	{
		private static System.Diagnostics.Stopwatch _sw;
		private static float _unityAtStart;
		private static readonly List<long> _ticks = new List<long>();
		private static readonly List<Assembly> _asm = new List<Assembly>();
		private static readonly object _gate = new object();

		internal static void Start()
		{
			_unityAtStart = Time.realtimeSinceStartup;
			_sw = System.Diagnostics.Stopwatch.StartNew();
			AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
			BootTrace.Start(_sw);
			BootXmlTrace.Start(_sw); BootPrefabTrace.Start(_sw);
			GameObject go = new GameObject("NailOptimization.BootProfile");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<BootProfileDriver>();
		}

		private static void OnLoad(object sender, AssemblyLoadEventArgs e)
		{
			long t = _sw.ElapsedTicks;
			System.Threading.Monitor.Enter(_gate);
			try
			{
				_ticks.Add(t);
				_asm.Add(e.LoadedAssembly);
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
		}

		internal static void Dump()
		{
			long end = _sw.ElapsedTicks;
			AppDomain.CurrentDomain.AssemblyLoad -= OnLoad;
			double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			N.ULog.Info(Guard.Tag, "boot: unity time at LoadGuard init " + _unityAtStart.ToString("0.0") + " s, first frame at +" + (end * f).ToString("0") + " ms, loads " + _ticks.Count);
			BootTrace.Dump(f);
			BootXmlTrace.Dump(f, end);
			System.Threading.Monitor.Enter(_gate);
			try
			{
				for (int i = 0; i < _ticks.Count; i++)
				{
					long next = i + 1 < _ticks.Count ? _ticks[i + 1] : end;
					string loc;
					try
					{
						loc = _asm[i].Location;
					}
					catch (Exception)
					{
						loc = "(dynamic)";
					}
					N.ULog.Info(Guard.Tag, "boot: +" + (_ticks[i] * f).ToString("0") + " d=" + ((next - _ticks[i]) * f).ToString("0") + " " + _asm[i].GetName().Name + " | " + loc);
				}
			}
			finally
			{
				System.Threading.Monitor.Exit(_gate);
			}
		}
	}

	public class BootProfileDriver : MonoBehaviour
	{
		private void Update()
		{
			enabled = false;
			try
			{
				BootProfile.Dump();
			}
			catch (Exception e)
			{
				N.ULog.Error(Guard.Tag, "boot profile failed: " + e);
			}
		}
	}

	public class LoadAlertsDriver : MonoBehaviour
	{
		private bool _started;
		private bool _scanned;

		private void Update()
		{
			if (!_started)
			{
				_started = true;
				LoadAlerts.FirstFrame();
				return;
			}
			if (!_scanned && LoadAlerts.TakeScan())
			{
				_scanned = true;
			}
			if (!LoadAlerts.TitleTick() && _scanned)
			{
				enabled = false;
			}
		}
	}
}
