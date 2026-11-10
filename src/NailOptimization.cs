// NailOptimization Core, phase 1, Drop 1. Assembly/module/namespace name: NailOptimization. AssemblyVersion must stay unchanged.
// References: Assembly-CSharp, 0Harmony, UnityEngine, UnityEngine.UI, mscorlib, System.
// Contents: public contract (append-only), ULog, Config, Counters, ResetRegistry, BoundedCache, TypeCache, PatchUtil,
// module loader, conflict detector, read-only uGUI panel (F10), title probe T. No Harmony patches are installed by Core.
// Api level 4: option catalog (Options), module list (Modules), UI host registration (UiHost).
// Api level 5: extra UI entry points of other modules (UiActions): title buttons left of ours, in-day slots, Escape ownership.
// Api level 6: alert badge per UiActions entry (SetBadge/GetBadge/BadgeVersion), drawn by the title buttons and the host's slots.
// Api level 7: split badge, warn and error counts (SetBadges/GetBadges/BadgeText).
// Api level 8: BadgeView (UiActions.CreateBadge), warn sign drawn from Images/warn_sign.png.
// Api level 9: PatchUtil.Foreign*, PatchUtil.GuardLate, ModHooks.WhenLoaded, Events.On, DepthBuffers<T>.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Harmony;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NailOptimization
{
	// ---------- public contract (append-only) ----------
	public interface IModule
	{
		string Name { get; }
		void Init();
	}

	public abstract class ModuleBase : IModule
	{
		public abstract string Name { get; }

		public virtual void Init()
		{
		}

		// Drop 1 additions. Defaults keep modules built against the phase 0 Core valid.
		public virtual string Version
		{
			get
			{
				return "0";
			}
		}

		public virtual int RequiredApiLevel
		{
			get
			{
				return 1;
			}
		}

		// "Namespace.Class.Method" strings of patch targets (used by the conflict detector only).
		public virtual string[] Targets
		{
			get
			{
				return new string[0];
			}
		}

		// Phase 3 (api level 3): called on the main thread after a flag/param of this module changed
		// (panel edit or live reload of the cfg file). Default does nothing. Exceptions are isolated by Core.
		public virtual void OnConfigChanged(string key)
		{
		}
	}

	public interface IResettable
	{
		void ResetState();
	}

	public sealed class Flag
	{
		public string Key;
		public bool On;
		// Phase 3 additions (append-only).
		public bool Def;
		public string Module = string.Empty;
		public string Comment = string.Empty;
	}

	// Phase 3: bounded integer parameter. Hot path reads Value (a field). Core clamps every write to Min..Max.
	public sealed class IntParam
	{
		public string Key;
		public int Value;
		public int Def;
		public int Min;
		public int Max;
		public int Step = 1;
		public string Module = string.Empty;
		public string Comment = string.Empty;
	}

	// Phase 3: bounded float parameter. Core rejects NaN and clamps every write to Min..Max.
	public sealed class FloatParam
	{
		public string Key;
		public float Value;
		public float Def;
		public float Min;
		public float Max;
		public float Step = 0.1f;
		public string Module = string.Empty;
		public string Comment = string.Empty;
	}

	public sealed class Counter
	{
		public string Module;
		public string Name;
		public long Value;

		public void Inc()
		{
			Value++;
		}
	}

	public static class Core
	{
		// Frozen. A const is inlined into callers: never use it as RequiredApiLevel. Use ApiLevel at runtime.
		public const int ContractVersion = 1;
		private static int _pings;

		public static int ApiLevel
		{
			get
			{
				return 9;
			}
		}

		public static int Ping(string caller)
		{
			_pings++;
			ULog.Info("core", "Core.Ping from " + caller + " n=" + _pings);
			return _pings;
		}

		public static int PingV2(string caller, int tag)
		{
			_pings++;
			ULog.Info("core", "Core.PingV2 from " + caller + " tag=" + tag + " n=" + _pings);
			return _pings;
		}
	}

	public static class Util
	{
		public static bool Same(object a, object b)
		{
			return object.ReferenceEquals(a, b);
		}
	}

	// ---------- api level 4 (append-only): option catalog, module list, UI host ----------
	public enum OptionKind
	{
		Raw = 0,
		Flag = 1,
		Int = 2,
		Float = 3
	}

	// Read-only description of one declared option. Read values through the Flag/IntParam/FloatParam object
	// (On/Value); write only through Config.SetFlag/SetInt/SetFloat.
	public sealed class OptionInfo
	{
		public string Key = string.Empty;
		public string Module = string.Empty;
		public OptionKind Kind;
		public string Comment = string.Empty;
		public Flag Flag;
		public IntParam IntParam;
		public FloatParam FloatParam;
	}

	public static class Options
	{
		// Changes whenever any value changes (panel edit, live reload of the cfg file).
		public static int Version
		{
			get
			{
				return Config.Version;
			}
		}

		public static string SaveState
		{
			get
			{
				return Config.SaveState;
			}
		}

		// Fills 'into' (cleared first) with the typed options (flag/int/float) in declaration order.
		// Raw (restart-only) keys are not listed. Returns the count.
		public static int Snapshot(List<OptionInfo> into)
		{
			into.Clear();
			int n = Config.DeclCount;
			for (int i = 0; i < n; i++)
			{
				Config.Decl d = Config.DeclAt(i);
				if (d.Kind < 1 || d.Kind > 3)
				{
					continue;
				}
				OptionInfo o = new OptionInfo();
				o.Key = d.Key;
				o.Module = d.Module;
				o.Comment = d.Comment;
				o.Kind = (OptionKind)d.Kind;
				o.Flag = d.F;
				o.IntParam = d.I;
				o.FloatParam = d.N;
				into.Add(o);
			}
			return into.Count;
		}
	}

	public sealed class ModuleStatus
	{
		public string Name = string.Empty;
		public string Version = string.Empty;
		public string Status = string.Empty;
		public string Error = string.Empty;
		public string[] Targets = new string[0];
	}

	public static class Modules
	{
		public static int Snapshot(List<ModuleStatus> into)
		{
			into.Clear();
			for (int i = 0; i < ModuleRegistry.List.Count; i++)
			{
				ModuleInfo mi = ModuleRegistry.List[i];
				ModuleStatus ms = new ModuleStatus();
				ms.Name = mi.Name;
				ms.Version = mi.Version;
				ms.Status = mi.Status;
				ms.Error = mi.Error;
				ms.Targets = mi.Targets;
				into.Add(ms);
			}
			return into.Count;
		}

		public static int OkCount
		{
			get
			{
				return ModuleRegistry.OkCount;
			}
		}

		public static int Total
		{
			get
			{
				return ModuleRegistry.List.Count;
			}
		}
	}

	// A UI module registers one host. Core calls Tick every frame (a cheap call while closed), and Toggle on the hotkey
	// and on the title button. The host owns the hotkey settings (HotkeyEnabled/HotkeyKey/HotkeyBlocked below).
	// A host that throws is bypassed for that call (the built-in panel opens); three failures drop it.
	public interface IUiHost
	{
		bool IsOpen { get; }
		void Toggle();
		void Tick();
	}

	public static class UiHost
	{
		private static IUiHost _host;
		private static int _failures;

		// Hotkey settings, written by the host every frame. HotkeyKey None = the key from Gui.Hotkey in the config.
		// HotkeyBlocked: the host is capturing keyboard input (text field, key binding), so the hotkey must not fire.
		public static bool HotkeyEnabled = true;
		public static KeyCode HotkeyKey = KeyCode.None;
		public static bool HotkeyBlocked;

		public static bool Registered
		{
			get
			{
				return !object.ReferenceEquals(_host, null);
			}
		}

		// Directory of NailOptimization.dll ("Images" and "Localization" live next to it).
		public static string RootDir
		{
			get
			{
				return PLog.Dir;
			}
		}

		public static KeyCode Hotkey
		{
			get
			{
				return CoreSettings.Hotkey;
			}
		}

		public static bool Register(IUiHost host)
		{
			if (object.ReferenceEquals(host, null))
			{
				return false;
			}
			if (!object.ReferenceEquals(_host, null))
			{
				ULog.Warn("ui", "host already registered; second registration ignored");
				return false;
			}
			_host = host;
			_failures = 0;
			ULog.Info("ui", "host registered " + host.GetType().FullName);
			return true;
		}

		// True when the screen point is over the title screen button; a host must not treat that click as "outside".
		public static bool OverToggleArea(Vector2 screenPoint)
		{
			return GuiPanel.OverToggleAt(screenPoint);
		}

		internal static void Toggle()
		{
			if (!object.ReferenceEquals(_host, null))
			{
				try
				{
					_host.Toggle();
					return;
				}
				catch (Exception e)
				{
					Fail("Toggle", e);
				}
			}
			GuiPanel.Toggle();
		}

		internal static void Tick()
		{
			if (object.ReferenceEquals(_host, null))
			{
				return;
			}
			try
			{
				KeyCode hk = HotkeyKey == KeyCode.None ? CoreSettings.Hotkey : HotkeyKey;
				if (HotkeyEnabled && !HotkeyBlocked && Input.GetKeyDown(hk))
				{
					Toggle();
				}
				if (!object.ReferenceEquals(_host, null))
				{
					_host.Tick();
				}
			}
			catch (Exception e)
			{
				Fail("Tick", e);
			}
		}

		private static void Fail(string where, Exception e)
		{
			_failures++;
			ULog.Error("ui", "host " + where + " threw (" + _failures + "): " + e);
			if (_failures >= 3)
			{
				_host = null;
				ULog.Error("ui", "host dropped after repeated errors; the built-in panel is used");
			}
		}
	}

	// Api level 5: true = the Escape press of this frame belongs to the caller's window (open now, or closed by this press).
	public delegate bool EscapeQuery();

	// Api level 5: extra UI entry points of other modules (e.g. a log window). Core builds one title button per action left of
	// its own; the UI host may add one in-day slot per action. Register once, from Init. A callback that throws three times is dropped.
	public static class UiActions
	{
		private sealed class Entry
		{
			internal string Id;
			internal string LabelEn;
			internal string LabelRu;
			internal string Icon;
			internal UnityAction Click;
			internal EscapeQuery Escape;
			internal EscapeQuery Wheel;
			internal int Failures;
			// Api 7: alert badge counts (both 0 = no badge).
			internal int BadgeWarn;
			internal int BadgeErr;
		}

		private sealed class Area
		{
			internal RectTransform Rt;
			internal Camera Cam;
		}

		private static readonly List<Entry> _list = new List<Entry>();
		private static readonly List<Area> _areas = new List<Area>();
		private static int _version;
		private static int _badgeVersion;
		private static int _escapeLogs;
		private static int _wheelLogs;

		public static int Count
		{
			get
			{
				return _list.Count;
			}
		}

		// Bumped by every Add; a host compares it to rebuild its slots.
		public static int Version
		{
			get
			{
				return _version;
			}
		}

		public static bool Add(string id, string labelEn, string labelRu, string iconRel, UnityAction click, EscapeQuery ownsEscape)
		{
			if (string.IsNullOrEmpty(id) || object.ReferenceEquals(click, null))
			{
				ULog.Warn("ui", "action rejected (empty id or no callback): " + id);
				return false;
			}
			for (int i = 0; i < _list.Count; i++)
			{
				if (string.Equals(_list[i].Id, id, StringComparison.Ordinal))
				{
					ULog.Warn("ui", "action " + id + " already registered; second registration ignored");
					return false;
				}
			}
			Entry e = new Entry();
			e.Id = id;
			e.LabelEn = string.IsNullOrEmpty(labelEn) ? id : labelEn;
			e.LabelRu = labelRu;
			e.Icon = iconRel;
			e.Click = click;
			e.Escape = ownsEscape;
			_list.Add(e);
			_version++;
			ULog.Info("ui", "action registered " + id + " icon=" + iconRel);
			return true;
		}

		public static string GetId(int i)
		{
			return i >= 0 && i < _list.Count ? _list[i].Id : string.Empty;
		}

		// "ru" gives the Russian label when one was given; anything else gives English.
		public static string GetLabel(int i, string lang)
		{
			if (i < 0 || i >= _list.Count)
			{
				return string.Empty;
			}
			Entry e = _list[i];
			if (string.Equals(lang, "ru", StringComparison.Ordinal) && !string.IsNullOrEmpty(e.LabelRu))
			{
				return e.LabelRu;
			}
			return e.LabelEn;
		}

		public static string GetIcon(int i)
		{
			return i >= 0 && i < _list.Count ? _list[i].Icon : null;
		}

		public static void Invoke(int i)
		{
			if (i < 0 || i >= _list.Count)
			{
				return;
			}
			Entry e = _list[i];
			if (e.Failures >= 3)
			{
				return;
			}
			try
			{
				e.Click();
			}
			catch (Exception ex)
			{
				e.Failures++;
				ULog.Error("ui", "action " + e.Id + " threw (" + e.Failures + "): " + ex);
				if (e.Failures >= 3)
				{
					ULog.Error("ui", "action " + e.Id + " disabled after repeated errors");
				}
			}
		}

		// Screen area of an action button (title clone, in-day slot). Windows ignore clicks inside these areas.
		public static void SetArea(RectTransform rt, Camera cam)
		{
			if (rt == null)
			{
				return;
			}
			for (int i = _areas.Count - 1; i >= 0; i--)
			{
				if (_areas[i].Rt == null)
				{
					_areas.RemoveAt(i);
				}
				else if (_areas[i].Rt == rt)
				{
					_areas[i].Cam = cam;
					return;
				}
			}
			Area a = new Area();
			a.Rt = rt;
			a.Cam = cam;
			_areas.Add(a);
		}

		public static bool OverArea(Vector2 screen)
		{
			for (int i = 0; i < _areas.Count; i++)
			{
				RectTransform rt = _areas[i].Rt;
				if (rt == null || !rt.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, _areas[i].Cam))
				{
					return true;
				}
			}
			return false;
		}

		// Called by the host's Escape guard every frame.
		public static bool EscapeOwned()
		{
			for (int i = 0; i < _list.Count; i++)
			{
				Entry e = _list[i];
				if (object.ReferenceEquals(e.Escape, null))
				{
					continue;
				}
				try
				{
					if (e.Escape())
					{
						return true;
					}
				}
				catch (Exception ex)
				{
					if (_escapeLogs < 3)
					{
						_escapeLogs++;
						ULog.Warn("ui", "escape query of " + e.Id + " threw: " + ex.GetType().Name + " " + ex.Message);
					}
				}
			}
			return false;
		}

		// Optional query of a registered action: true = the mouse wheel of this frame belongs to its window (cursor over it).
		public static bool SetWheel(string id, EscapeQuery ownsWheel)
		{
			for (int i = 0; i < _list.Count; i++)
			{
				if (string.Equals(_list[i].Id, id, StringComparison.Ordinal))
				{
					_list[i].Wheel = ownsWheel;
					return true;
				}
			}
			ULog.Warn("ui", "wheel query for unknown action " + id + " ignored");
			return false;
		}

		// Api 6: alert badge of an action (count of new issues, level 0 none, 1 warn, 2 error). Hosts draw it next to the action's buttons.
		// Kept for api 6 callers: level 1 sets the warn count, level 2 the error count, the other part 0.
		public static bool SetBadge(string id, int count, int level)
		{
			if (level >= 2)
			{
				return SetBadges(id, 0, count);
			}
			return SetBadges(id, level == 1 ? count : 0, 0);
		}

		// Api 7: warn and error counts of an action; both 0 = no badge.
		public static bool SetBadges(string id, int warn, int err)
		{
			if (warn < 0)
			{
				warn = 0;
			}
			if (err < 0)
			{
				err = 0;
			}
			for (int i = 0; i < _list.Count; i++)
			{
				Entry e = _list[i];
				if (string.Equals(e.Id, id, StringComparison.Ordinal))
				{
					if (e.BadgeWarn != warn || e.BadgeErr != err)
					{
						e.BadgeWarn = warn;
						e.BadgeErr = err;
						_badgeVersion++;
					}
					return true;
				}
			}
			return false;
		}

		// Api 6 view: count = warn + error, level = highest present (0 = no badge).
		public static int GetBadge(int i, out int level)
		{
			int warn;
			int err;
			GetBadges(i, out warn, out err);
			level = err > 0 ? 2 : (warn > 0 ? 1 : 0);
			return warn + err;
		}

		// Api 7: warn and error counts of entry i.
		public static void GetBadges(int i, out int warn, out int err)
		{
			if (i < 0 || i >= _list.Count)
			{
				warn = 0;
				err = 0;
				return;
			}
			warn = _list[i].BadgeWarn;
			err = _list[i].BadgeErr;
		}

		// Api 7: rich text "<yellow>N sign</yellow> <red>M sign</red>", zero part left out, each part capped at 99+; empty when both are 0.
		public static string BadgeText(int warn, int err, string sign)
		{
			string s = string.Empty;
			if (warn > 0)
			{
				s = "<color=#FFD940>" + BadgeCount(warn) + sign + "</color>";
			}
			if (err > 0)
			{
				s = s + (s.Length > 0 ? " " : string.Empty) + "<color=#FF5C4D>" + BadgeCount(err) + sign + "</color>";
			}
			return s;
		}

		internal static string BadgeCount(int n)
		{
			return n > 99 ? "99+" : n.ToString(CultureInfo.InvariantCulture);
		}

		// Api 8: badge with the warn sign as an image (Images/warn_sign.png); the caller sizes and places root.
		public static BadgeView CreateBadge(RectTransform root, Font font, int fontSize, bool alignRight)
		{
			return new BadgeView(root, font, fontSize, alignRight);
		}

		// Bumped by every badge change; hosts compare it once per tick.
		public static int BadgeVersion
		{
			get
			{
				return _badgeVersion;
			}
		}

		// Called by the host's camera guard on frames with a wheel delta.
		public static bool WheelOwned()
		{
			for (int i = 0; i < _list.Count; i++)
			{
				Entry e = _list[i];
				if (object.ReferenceEquals(e.Wheel, null))
				{
					continue;
				}
				try
				{
					if (e.Wheel())
					{
						return true;
					}
				}
				catch (Exception ex)
				{
					if (_wheelLogs < 3)
					{
						_wheelLogs++;
						ULog.Warn("ui", "wheel query of " + e.Id + " threw: " + ex.GetType().Name + " " + ex.Message);
					}
				}
			}
			return false;
		}
	}

	// Api 8: "N<sign> M<sign>", warn part yellow left of the error part red; the sign is an image tinted with its part colour.
	// A zero part is hidden; 0/0 hides root. Without Images/warn_sign.png the sign is the text "(!)".
	public sealed class BadgeView
	{
		private const string SignPath = "Images/warn_sign.png";
		private static readonly Color WarnColor = new Color(1f, 0.85f, 0.25f, 1f);
		private static readonly Color ErrColor = new Color(1f, 0.36f, 0.3f, 1f);
		private static bool _signWarned;
		private readonly RectTransform _root;
		private readonly bool _right;
		private readonly int _size;
		private readonly Sprite _sprite;
		private readonly Text[] _num = new Text[2];
		private readonly Image[] _sign = new Image[2];
		private readonly float[] _numW = new float[2];
		private readonly float[] _partW = new float[2];

		internal BadgeView(RectTransform root, Font font, int fontSize, bool alignRight)
		{
			_root = root;
			_right = alignRight;
			_size = fontSize;
			_sprite = TitleWatch.LoadActionSprite(SignPath);
			if (_sprite == null && !_signWarned)
			{
				_signWarned = true;
				ULog.Warn("ui", "badge sign " + SignPath + " not loaded: text sign (!) used");
			}
			for (int k = 0; k < 2; k++)
			{
				Color c = k == 0 ? WarnColor : ErrColor;
				RectTransform tr = Part(k == 0 ? "Warn" : "Err");
				Text t = tr.gameObject.AddComponent<Text>();
				t.font = font;
				t.fontSize = fontSize;
				t.fontStyle = FontStyle.Bold;
				t.alignment = TextAnchor.MiddleLeft;
				t.horizontalOverflow = HorizontalWrapMode.Overflow;
				t.verticalOverflow = VerticalWrapMode.Overflow;
				t.supportRichText = false;
				t.raycastTarget = false;
				t.color = c;
				Outline o = tr.gameObject.AddComponent<Outline>();
				o.effectColor = new Color(0f, 0f, 0f, 0.9f);
				tr.gameObject.SetActive(false);
				_num[k] = t;
				if (_sprite != null)
				{
					RectTransform ir = Part(k == 0 ? "WarnSign" : "ErrSign");
					Image im = ir.gameObject.AddComponent<Image>();
					im.sprite = _sprite;
					im.preserveAspect = true;
					im.raycastTarget = false;
					im.color = c;
					Outline io = ir.gameObject.AddComponent<Outline>();
					io.effectColor = new Color(0f, 0f, 0f, 0.9f);
					ir.gameObject.SetActive(false);
					_sign[k] = im;
				}
			}
		}

		public void Set(int warn, int err)
		{
			if (warn <= 0 && err <= 0)
			{
				if (_root.gameObject.activeSelf)
				{
					_root.gameObject.SetActive(false);
				}
				return;
			}
			if (!_root.gameObject.activeSelf)
			{
				_root.gameObject.SetActive(true);
			}
			Prepare(0, warn);
			Prepare(1, err);
			float space = _partW[0] > 0f && _partW[1] > 0f ? _size * 0.4f : 0f;
			float total = _partW[0] + space + _partW[1];
			float x = _right ? -total : -total * 0.5f;
			x = Place(0, x) + space;
			Place(1, x);
		}

		private RectTransform Part(string name)
		{
			GameObject go = new GameObject(name);
			RectTransform rt = go.AddComponent<RectTransform>();
			rt.SetParent(_root, false);
			float ax = _right ? 1f : 0.5f;
			rt.anchorMin = new Vector2(ax, 0.5f);
			rt.anchorMax = new Vector2(ax, 0.5f);
			rt.pivot = new Vector2(0f, 0.5f);
			return rt;
		}

		// Sets part k's text and visibility; width 0 = hidden.
		private void Prepare(int k, int n)
		{
			Text t = _num[k];
			Image im = _sign[k];
			bool on = n > 0;
			if (t.gameObject.activeSelf != on)
			{
				t.gameObject.SetActive(on);
			}
			if (im != null && im.gameObject.activeSelf != on)
			{
				im.gameObject.SetActive(on);
			}
			if (!on)
			{
				_numW[k] = 0f;
				_partW[k] = 0f;
				return;
			}
			t.text = UiActions.BadgeCount(n) + (im == null ? "(!)" : string.Empty);
			_numW[k] = t.preferredWidth;
			_partW[k] = _numW[k] + (im != null ? _size * 0.15f + _size : 0f);
		}

		private float Place(int k, float x)
		{
			if (_partW[k] <= 0f)
			{
				return x;
			}
			RectTransform tr = _num[k].rectTransform;
			tr.anchoredPosition = new Vector2(x, 0f);
			tr.sizeDelta = new Vector2(_numW[k], _size * 1.4f);
			Image im = _sign[k];
			if (im != null)
			{
				RectTransform ir = im.rectTransform;
				ir.anchoredPosition = new Vector2(x + _numW[k] + _size * 0.15f, 0f);
				ir.sizeDelta = new Vector2(_size, _size);
			}
			return x + _partW[k];
		}
	}

	public static class ULog
	{
		private static bool _logging = true;

		public static bool Enabled
		{
			get
			{
				return _logging;
			}
		}

		internal static void SetEnabled(bool value)
		{
			_logging = value;
		}

		public static void Info(string src, string msg)
		{
			if (_logging)
			{
				PLog.Write("INFO", src + " " + msg);
			}
		}

		public static void Warn(string src, string msg)
		{
			if (_logging)
			{
				PLog.Write("WARN", src + " " + msg);
			}
		}

		// Errors are always written.
		public static void Error(string src, string msg)
		{
			PLog.Write("ERROR", src + " " + msg);
		}
	}

	public static class Config
	{
		internal sealed class Decl
		{
			public string Key;
			public string Def;
			public string Comment;
			public string Current;
			public bool Present;
			// Phase 3: 0 raw/restart-only, 1 flag, 2 int, 3 float.
			public int Kind;
			public string Module = string.Empty;
			public Flag F;
			public IntParam I;
			public FloatParam N;
			// Value differs from the file (pending write, or session only when Config.Save is off).
			public bool Dirty;
			// Last raw text seen in the file for this key (to skip unchanged keys on live reload).
			public string Seen;
		}

		private static readonly Dictionary<string, string> _file = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, Decl> _decls = new Dictionary<string, Decl>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<Decl> _order = new List<Decl>();
		private static readonly Dictionary<string, Flag> _flags = new Dictionary<string, Flag>(StringComparer.OrdinalIgnoreCase);
		private static string _path;
		private static bool _loaded;
		private static bool _fileExisted;
		private static bool _endsNewline = true;
		private const float WriteDelay = 0.5f;
		private static long _stamp;
		private static bool _stampLogged;
		private static bool _watchBroken;
		private static bool _bakDone;
		private static int _dirtyCount;
		private static float _flushAt;
		private static float _retryAt;
		private static float _nextWatch;
		private static string _saveError;
		private static int _version;
		private static int _tickErrors;
		private static int _notifyDepth;

		internal static void Load()
		{
			if (_loaded)
			{
				return;
			}
			_loaded = true;
			try
			{
				_path = Path.Combine(PLog.Dir, "NailOptimization.cfg.txt");
				_stamp = SafeStamp();
				if (!File.Exists(_path))
				{
					return;
				}
				_fileExisted = true;
				string text = File.ReadAllText(_path);
				_endsNewline = text.Length == 0 || text.EndsWith("\n", StringComparison.Ordinal);
				ParseInto(text, _file);
			}
			catch (Exception e)
			{
				ULog.Error("config", "load failed: " + e);
			}
		}

		private static string Raw(string key, string def, string comment)
		{
			Decl d;
			string v;
			if (!_decls.TryGetValue(key, out d))
			{
				d = new Decl();
				d.Key = key;
				d.Def = def;
				d.Comment = comment;
				d.Present = _file.ContainsKey(key);
				d.Module = ModuleOf(key);
				d.Seen = _file.TryGetValue(key, out v) ? v : null;
				_decls.Add(key, d);
				_order.Add(d);
			}
			if (_file.TryGetValue(key, out v))
			{
				d.Current = v;
				return v;
			}
			d.Current = def;
			return def;
		}

		internal static bool ParseBool(string s, bool def)
		{
			if (s == null)
			{
				return def;
			}
			string t = s.Trim().ToLowerInvariant();
			if (t == "true" || t == "1" || t == "on" || t == "yes")
			{
				return true;
			}
			if (t == "false" || t == "0" || t == "off" || t == "no")
			{
				return false;
			}
			return def;
		}

		public static bool GetBool(string key, bool def, string comment)
		{
			return ParseBool(Raw(key, def ? "true" : "false", comment), def);
		}

		public static int GetInt(string key, int def, string comment)
		{
			int r;
			if (int.TryParse(Raw(key, def.ToString(CultureInfo.InvariantCulture), comment), NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
			{
				return r;
			}
			return def;
		}

		public static float GetFloat(string key, float def, string comment)
		{
			float r;
			if (float.TryParse(Raw(key, def.ToString("R", CultureInfo.InvariantCulture), comment), NumberStyles.Float, CultureInfo.InvariantCulture, out r))
			{
				return r;
			}
			return def;
		}

		public static string GetString(string key, string def, string comment)
		{
			return Raw(key, def, comment);
		}

		private static void ParseInto(string text, Dictionary<string, string> into)
		{
			string[] lines = text.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string key;
				string val;
				if (SplitLine(lines[i], out key, out val))
				{
					into[key] = val;
				}
			}
		}

		private static bool SplitLine(string raw, out string key, out string val)
		{
			key = null;
			val = null;
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#' || line[0] == ';')
			{
				return false;
			}
			int eq = line.IndexOf('=');
			if (eq <= 0)
			{
				return false;
			}
			key = line.Substring(0, eq).Trim();
			val = line.Substring(eq + 1).Trim();
			return key.Length > 0;
		}

		private static string ModuleOf(string key)
		{
			int dot = key.IndexOf('.');
			return dot > 0 ? key.Substring(0, dot) : string.Empty;
		}

		private static string FmtBool(bool v)
		{
			return v ? "true" : "false";
		}

		private static string FmtInt(int v)
		{
			return v.ToString(CultureInfo.InvariantCulture);
		}

		private static string FmtFloat(float v)
		{
			return v.ToString("R", CultureInfo.InvariantCulture);
		}

		private static int ClampInt(int v, int min, int max)
		{
			return v < min ? min : (v > max ? max : v);
		}

		// NaN must be rejected by the caller (v != v).
		private static float ClampFloat(float v, float min, float max)
		{
			return v < min ? min : (v > max ? max : v);
		}

		// Runtime flag: the module reads Flag.On on its hot path. Changed by the panel / live reload (main thread only).
		public static Flag BindFlag(string key, bool def, string comment)
		{
			Flag f;
			if (_flags.TryGetValue(key, out f))
			{
				return f;
			}
			f = new Flag();
			f.Key = key;
			f.Def = def;
			f.Comment = comment;
			f.Module = ModuleOf(key);
			f.On = GetBool(key, def, comment);
			Decl d = _decls[key];
			d.Kind = 1;
			d.F = f;
			d.Current = FmtBool(f.On);
			_flags.Add(key, f);
			return f;
		}

		// Bounded integer parameter. The cfg comment gets the range appended. Out-of-range file values are clamped.
		public static IntParam BindInt(string key, int def, int min, int max, int step, string comment)
		{
			Decl d;
			if (_decls.TryGetValue(key, out d) && d.I != null)
			{
				return d.I;
			}
			if (max < min)
			{
				int t = min;
				min = max;
				max = t;
			}
			if (step < 1)
			{
				step = 1;
			}
			def = ClampInt(def, min, max);
			IntParam p = new IntParam();
			p.Key = key;
			p.Def = def;
			p.Min = min;
			p.Max = max;
			p.Step = step;
			p.Comment = comment;
			p.Module = ModuleOf(key);
			int raw = GetInt(key, def, comment + " [" + min + ".." + max + "]");
			p.Value = ClampInt(raw, min, max);
			if (p.Value != raw)
			{
				ULog.Warn("config", key + "=" + raw + " is outside " + min + ".." + max + ", using " + p.Value);
			}
			d = _decls[key];
			d.Kind = 2;
			d.I = p;
			d.Current = FmtInt(p.Value);
			return p;
		}

		// Bounded float parameter. NaN from the file is replaced by the default.
		public static FloatParam BindFloat(string key, float def, float min, float max, float step, string comment)
		{
			Decl d;
			if (_decls.TryGetValue(key, out d) && d.N != null)
			{
				return d.N;
			}
			if (max < min)
			{
				float t = min;
				min = max;
				max = t;
			}
			if (!(step > 0f))
			{
				step = 0.1f;
			}
			if (float.IsNaN(def))
			{
				def = min;
			}
			def = ClampFloat(def, min, max);
			FloatParam p = new FloatParam();
			p.Key = key;
			p.Def = def;
			p.Min = min;
			p.Max = max;
			p.Step = step;
			p.Comment = comment;
			p.Module = ModuleOf(key);
			float raw = GetFloat(key, def, comment + " [" + FmtFloat(min) + ".." + FmtFloat(max) + "]");
			if (float.IsNaN(raw))
			{
				ULog.Warn("config", key + " is NaN, using " + FmtFloat(def));
				raw = def;
			}
			p.Value = ClampFloat(raw, min, max);
			if (p.Value != raw)
			{
				ULog.Warn("config", key + "=" + FmtFloat(raw) + " is outside " + FmtFloat(min) + ".." + FmtFloat(max) + ", using " + FmtFloat(p.Value));
			}
			d = _decls[key];
			d.Kind = 3;
			d.N = p;
			d.Current = FmtFloat(p.Value);
			return p;
		}

		// Writers: main thread only. Clamp, update the file mirror, mark for write (debounced), notify the owning module.
		public static void SetFlag(Flag f, bool value)
		{
			if (f == null || f.On == value)
			{
				return;
			}
			Decl d;
			if (!_decls.TryGetValue(f.Key, out d))
			{
				return;
			}
			f.On = value;
			Changed(d, FmtBool(value), true);
		}

		public static void SetInt(IntParam p, int value)
		{
			if (p == null)
			{
				return;
			}
			int v = ClampInt(value, p.Min, p.Max);
			if (v == p.Value)
			{
				return;
			}
			Decl d;
			if (!_decls.TryGetValue(p.Key, out d))
			{
				return;
			}
			p.Value = v;
			Changed(d, FmtInt(v), true);
		}

		public static void SetFloat(FloatParam p, float value)
		{
			if (p == null || float.IsNaN(value))
			{
				return;
			}
			float v = ClampFloat(value, p.Min, p.Max);
			if (v == p.Value)
			{
				return;
			}
			Decl d;
			if (!_decls.TryGetValue(p.Key, out d))
			{
				return;
			}
			p.Value = v;
			Changed(d, FmtFloat(v), true);
		}

		private static void Changed(Decl d, string text, bool persist)
		{
			d.Current = text;
			d.Seen = text;
			_file[d.Key] = text;
			_version++;
			if (persist)
			{
				if (!d.Dirty)
				{
					d.Dirty = true;
					_dirtyCount++;
				}
				_flushAt = Time.unscaledTime + WriteDelay;
			}
			Notify(d.Key);
		}

		private static void Notify(string key)
		{
			if (_notifyDepth >= 3)
			{
				ULog.Warn("config", "OnConfigChanged recursion cut at " + key);
				return;
			}
			_notifyDepth++;
			try
			{
				CoreSettings.OnKey(key);
				string mod = ModuleOf(key);
				if (mod.Length == 0)
				{
					return;
				}
				for (int i = 0; i < ModuleRegistry.List.Count; i++)
				{
					ModuleInfo mi = ModuleRegistry.List[i];
					if (mi.Base == null || mi.Status != "Ok" || !string.Equals(mi.Name, mod, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					try
					{
						mi.Base.OnConfigChanged(key);
					}
					catch (Exception e)
					{
						mi.CallbackErrors++;
						if (mi.CallbackErrors <= 3)
						{
							ULog.Error("config", "module " + mi.Name + " OnConfigChanged threw for " + key + ": " + e);
						}
					}
					break;
				}
			}
			finally
			{
				_notifyDepth--;
			}
		}

		internal static int Version
		{
			get
			{
				return _version;
			}
		}

		internal static string SaveState
		{
			get
			{
				if (_saveError != null)
				{
					return "NOT SAVED: " + _saveError;
				}
				if (_dirtyCount > 0)
				{
					if (CoreSettings.SaveFlag != null && !CoreSettings.SaveFlag.On)
					{
						return "session only: " + _dirtyCount + " unsaved";
					}
					return "writing...";
				}
				return "saved";
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static long Stamp()
		{
			return File.GetLastWriteTimeUtc(_path).Ticks;
		}

		private static long SafeStamp()
		{
			try
			{
				long s = Stamp();
				if (!_stampLogged)
				{
					_stampLogged = true;
					ULog.Info("config", "watch stamp ok");
				}
				return s;
			}
			catch (Exception e)
			{
				if (!_watchBroken)
				{
					_watchBroken = true;
					ULog.Error("config", "file stamp unavailable, live reload disabled: " + e.GetType().Name);
				}
				return 0;
			}
		}

		// Called every frame by the driver: debounced write and the periodic file check. Never on a game hot path.
		internal static void Tick()
		{
			try
			{
				if (CoreSettings.SaveFlag == null || CoreSettings.WatchParam == null)
				{
					return;
				}
				float now = Time.unscaledTime;
				if (_dirtyCount > 0 && now >= _flushAt && now >= _retryAt && CoreSettings.SaveFlag.On)
				{
					FlushDirty();
				}
				float w = CoreSettings.WatchParam.Value;
				if (w > 0f && !_watchBroken)
				{
					if (_nextWatch > now + w)
					{
						_nextWatch = now + w;
					}
					if (now >= _nextWatch)
					{
						_nextWatch = now + w;
						long st = SafeStamp();
						if (!_watchBroken && st != _stamp && Reload("watch"))
						{
							_stamp = st;
						}
					}
				}
			}
			catch (Exception e)
			{
				_tickErrors++;
				if (_tickErrors <= 3)
				{
					ULog.Error("config", "tick threw (" + _tickErrors + "): " + e);
				}
			}
		}

		// Returns false when the file could not be read (the caller keeps the old stamp and retries on the next poll).
		internal static bool Reload(string why)
		{
			string text;
			try
			{
				text = File.ReadAllText(_path);
			}
			catch (Exception e)
			{
				ULog.Warn("config", "reload read failed: " + e.Message);
				return false;
			}
			Dictionary<string, string> ext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			ParseInto(text, ext);
			int n = ApplyExternal(ext);
			ULog.Info("config", "live reload (" + why + "): changed=" + n);
			return true;
		}

		// Applies values read from the file. Keys that are Dirty (our pending change) win and are skipped.
		private static int ApplyExternal(Dictionary<string, string> ext)
		{
			List<Decl> changed = null;
			foreach (KeyValuePair<string, string> kv in ext)
			{
				Decl d;
				if (!_decls.TryGetValue(kv.Key, out d))
				{
					_file[kv.Key] = kv.Value;
					continue;
				}
				if (d.Dirty || (d.Seen != null && d.Seen == kv.Value))
				{
					continue;
				}
				_file[kv.Key] = kv.Value;
				d.Seen = kv.Value;
				if (ApplyDecl(d, kv.Value))
				{
					if (changed == null)
					{
						changed = new List<Decl>();
					}
					changed.Add(d);
				}
			}
			if (changed == null)
			{
				return 0;
			}
			_version++;
			for (int i = 0; i < changed.Count; i++)
			{
				Notify(changed[i].Key);
			}
			return changed.Count;
		}

		// Returns true when the live value changed. Restart-only keys (Kind 0) are never applied.
		private static bool ApplyDecl(Decl d, string text)
		{
			if (d.Kind == 1)
			{
				bool nv = ParseBool(text, d.F.On);
				if (nv == d.F.On)
				{
					return false;
				}
				d.F.On = nv;
				d.Current = FmtBool(nv);
				ULog.Info("config", "live " + d.Key + " -> " + d.Current);
				return true;
			}
			if (d.Kind == 2)
			{
				int r;
				if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
				{
					ULog.Warn("config", d.Key + "='" + text + "' is not an integer, keeping " + d.I.Value);
					return false;
				}
				int v = ClampInt(r, d.I.Min, d.I.Max);
				if (v != r)
				{
					ULog.Warn("config", d.Key + "=" + r + " is outside " + d.I.Min + ".." + d.I.Max + ", using " + v);
				}
				if (v == d.I.Value)
				{
					return false;
				}
				d.I.Value = v;
				d.Current = FmtInt(v);
				ULog.Info("config", "live " + d.Key + " -> " + d.Current);
				return true;
			}
			if (d.Kind == 3)
			{
				float r;
				if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out r) || float.IsNaN(r))
				{
					ULog.Warn("config", d.Key + "='" + text + "' is not a number, keeping " + FmtFloat(d.N.Value));
					return false;
				}
				float v = ClampFloat(r, d.N.Min, d.N.Max);
				if (v != r)
				{
					ULog.Warn("config", d.Key + "=" + FmtFloat(r) + " is outside " + FmtFloat(d.N.Min) + ".." + FmtFloat(d.N.Max) + ", using " + FmtFloat(v));
				}
				if (v == d.N.Value)
				{
					return false;
				}
				d.N.Value = v;
				d.Current = FmtFloat(v);
				ULog.Info("config", "live " + d.Key + " -> " + d.Current);
				return true;
			}
			return false;
		}

		// Re-reads the file, applies foreign edits to non-dirty keys, then rewrites only the lines of dirty keys.
		// Comments, order, unknown keys and the line ending style are preserved. No atomic replace (File.Replace is unverified).
		private static void FlushDirty()
		{
			try
			{
				string text = File.Exists(_path) ? File.ReadAllText(_path) : string.Empty;
				Dictionary<string, string> ext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				ParseInto(text, ext);
				ApplyExternal(ext);
				string eol = (text.Length == 0 || text.IndexOf("\r\n", StringComparison.Ordinal) >= 0) ? "\r\n" : "\n";
				string[] lines = text.Split('\n');
				Dictionary<string, int> at = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < lines.Length; i++)
				{
					string k;
					string v;
					if (SplitLine(lines[i], out k, out v))
					{
						at[k] = i;
					}
				}
				StringBuilder add = new StringBuilder();
				StringBuilder what = new StringBuilder();
				int written = 0;
				for (int i = 0; i < _order.Count; i++)
				{
					Decl d = _order[i];
					if (!d.Dirty)
					{
						continue;
					}
					written++;
					if (written <= 5)
					{
						if (written > 1)
						{
							what.Append(", ");
						}
						what.Append(d.Key).Append('=').Append(d.Current);
					}
					int li;
					if (at.TryGetValue(d.Key, out li))
					{
						string old = lines[li];
						bool cr = old.EndsWith("\r", StringComparison.Ordinal);
						int eq = old.IndexOf('=');
						lines[li] = old.Substring(0, eq) + "=" + d.Current + (cr ? "\r" : string.Empty);
					}
					else
					{
						if (!string.IsNullOrEmpty(d.Comment))
						{
							add.Append("# ").Append(d.Comment).Append(eol);
						}
						add.Append(d.Key).Append('=').Append(d.Current).Append(eol);
					}
				}
				string body = string.Join("\n", lines);
				if (add.Length > 0)
				{
					if (body.Length > 0 && !body.EndsWith("\n", StringComparison.Ordinal))
					{
						body = body + eol;
					}
					body = body + add.ToString();
				}
				if (!_bakDone)
				{
					_bakDone = true;
					if (text.Length > 0)
					{
						try
						{
							File.WriteAllText(_path + ".bak", text);
						}
						catch (Exception be)
						{
							ULog.Warn("config", "backup write failed: " + be.Message);
						}
					}
				}
				File.WriteAllText(_path, body);
				for (int i = 0; i < _order.Count; i++)
				{
					_order[i].Dirty = false;
					_order[i].Present = true;
				}
				_dirtyCount = 0;
				_retryAt = 0f;
				_fileExisted = true;
				_stamp = SafeStamp();
				_saveError = null;
				_version++;
				ULog.Info("config", "saved " + written + " keys (" + what.ToString() + ")");
			}
			catch (Exception e)
			{
				string msg = e.GetType().Name + ": " + e.Message;
				if (_saveError != msg)
				{
					ULog.Error("config", "save failed, will retry: " + msg);
				}
				_saveError = msg;
				_retryAt = Time.unscaledTime + 10f;
				_version++;
			}
		}

		internal static Decl DeclAt(int i)
		{
			return _order[i];
		}

		internal static int DeclCount
		{
			get
			{
				return _order.Count;
			}
		}

		internal static string DeclLine(int i)
		{
			Decl d = _order[i];
			return d.Key + "=" + d.Current;
		}

		internal static void FlushMissing()
		{
			try
			{
				StringBuilder sb = new StringBuilder();
				int n = 0;
				for (int i = 0; i < _order.Count; i++)
				{
					Decl d = _order[i];
					if (d.Present)
					{
						continue;
					}
					d.Present = true;
					n++;
					if (!string.IsNullOrEmpty(d.Comment))
					{
						sb.Append("# ").Append(d.Comment).Append(Environment.NewLine);
					}
					sb.Append(d.Key).Append('=').Append(d.Def).Append(Environment.NewLine);
				}
				if (n == 0 && _fileExisted)
				{
					ULog.Info("config", "no missing keys, declared=" + _order.Count);
					return;
				}
				string head = string.Empty;
				if (!_fileExisted)
				{
					head = "# NailOptimization config. key=value. '#' or ';' only at the start of a line. Keys are case-insensitive." + Environment.NewLine + "# Module keys use the prefix Module.Key. Missing keys are appended automatically; values changed in the panel are written here (Config.Save)." + Environment.NewLine;
				}
				else if (!_endsNewline)
				{
					head = Environment.NewLine;
				}
				File.AppendAllText(_path, head + sb.ToString());
				_fileExisted = true;
				_endsNewline = true;
				_stamp = SafeStamp();
				ULog.Info("config", "appended " + n + " missing keys, declared=" + _order.Count);
			}
			catch (Exception e)
			{
				ULog.Error("config", "flush failed: " + e);
			}
		}
	}

	internal static class CoreSettings
	{
		internal static string Order = string.Empty;
		internal static KeyCode Hotkey = KeyCode.F10;
		internal static bool Prewarm;
		internal static string DetectNames = string.Empty;
		internal static bool DumpPatches;
		internal static bool LogEnvironment = true;
		internal static int OpenTimingFrames = 12;
		internal static bool TitleButton = true;
		internal static string TitleIcon = "Images/config_icon.png";
		internal static Flag LoggingFlag;
		internal static Flag SaveFlag;
		internal static FloatParam WatchParam;

		internal static void OnKey(string key)
		{
			if (LoggingFlag != null && string.Equals(key, "Logging", StringComparison.OrdinalIgnoreCase))
			{
				ULog.SetEnabled(LoggingFlag.On);
			}
		}

		internal static void Load()
		{
			LoggingFlag = Config.BindFlag("Logging", true, "Write Info/Warn lines to NailOptimization.log. Errors are always written.");
			ULog.SetEnabled(LoggingFlag.On);
			Order = Config.GetString("Order", string.Empty, "Module load order: comma-separated file names (without .dll) loaded first; the rest load by name.");
			string hk = Config.GetString("Gui.Hotkey", "F10", "Panel hotkey (UnityEngine.KeyCode name).");
			try
			{
				Hotkey = (KeyCode)Enum.Parse(typeof(KeyCode), hk, true);
			}
			catch (Exception)
			{
				Hotkey = KeyCode.F10;
				ULog.Error("config", "bad Gui.Hotkey '" + hk + "', using F10");
			}
			Prewarm = Config.GetBool("Gui.Prewarm", false, "Build the panel and rasterize glyphs at startup (frame 2) instead of on first open.");
			LogEnvironment = Config.GetBool("Gui.LogEnvironment", true, "Log canvas scan (FindObjectsOfType) on every panel open. Diagnostic.");
			OpenTimingFrames = Config.GetInt("Gui.OpenTimingFrames", 12, "Frames of unscaledDeltaTime recorded after each panel open (0 = off, max 32).");
			if (OpenTimingFrames < 0)
			{
				OpenTimingFrames = 0;
			}
			if (OpenTimingFrames > 32)
			{
				OpenTimingFrames = 32;
			}
			DetectNames = Config.GetString("Detect.Names", "ALot,NZM,SomeOptimizer,some.optimizer,Charming", "Comma-separated substrings of assembly names / Harmony ids reported as third-party optimizers (warning only).");
			DumpPatches = Config.GetBool("Detect.DumpPatches", false, "Write every detected Harmony patch to the log at frame 2.");
			TitleButton = Config.GetBool("Gui.TitleButton", true, "Icon button on the title screens (opens the panel). Read-only UI.");
			TitleIcon = Config.GetString("Gui.TitleIcon", "Images/config_icon.png", "Title icon PNG, path relative to the NailOptimization folder. Missing file = placeholder square.");
			SaveFlag = Config.BindFlag("Config.Save", true, "Write changes made in the panel to this file (false = session only).");
			WatchParam = Config.BindFloat("Config.WatchSeconds", 10f, 0f, 300f, 1f, "Seconds between checks of this file for external edits, applied live (0 = off).");
		}
	}

	public static class Counters
	{
		private static readonly Dictionary<string, Counter> _map = new Dictionary<string, Counter>(StringComparer.Ordinal);
		private static readonly List<Counter> _list = new List<Counter>();

		public static Counter Get(string module, string name)
		{
			string key = module + "." + name;
			Counter c;
			if (_map.TryGetValue(key, out c))
			{
				return c;
			}
			c = new Counter();
			c.Module = module;
			c.Name = name;
			_map.Add(key, c);
			_list.Add(c);
			return c;
		}

		internal static int Count
		{
			get
			{
				return _list.Count;
			}
		}

		internal static void Summary()
		{
			int shown = 0;
			for (int i = 0; i < _list.Count && shown < 80; i++)
			{
				Counter c = _list[i];
				if (c.Value != 0L)
				{
					shown++;
					ULog.Info("counter", c.Module + "." + c.Name + "=" + c.Value);
				}
			}
		}
	}

	public static class ResetRegistry
	{
		private static readonly List<IResettable> _list = new List<IResettable>();
		private static int _fired;

		// Holds a strong reference for the process lifetime: register long-lived stores only.
		public static void Register(IResettable r)
		{
			if (r == null)
			{
				return;
			}
			for (int i = 0; i < _list.Count; i++)
			{
				if (object.ReferenceEquals(_list[i], r))
				{
					return;
				}
			}
			_list.Add(r);
		}

		internal static int Count
		{
			get
			{
				return _list.Count;
			}
		}

		internal static void ResetAll()
		{
			_fired++;
			for (int i = 0; i < _list.Count; i++)
			{
				try
				{
					_list[i].ResetState();
				}
				catch (Exception e)
				{
					ULog.Error("reset", "ResetState threw (" + _list[i].GetType().FullName + "): " + e);
				}
			}
			ULog.Info("core", "reset hook fired n=" + _fired + " registered=" + _list.Count);
			Counters.Summary();
		}
	}

	// Single-threaded game code: no locking. Cleared completely when full (no LRU).
	public class BoundedCache<K, V> : IResettable
	{
		private readonly Dictionary<K, V> _map = new Dictionary<K, V>();
		private readonly string _name;
		private readonly int _capacity;
		public int Evictions;

		public BoundedCache(string name, int capacity, bool resetOnRelease)
		{
			_name = name;
			_capacity = capacity < 1 ? 1 : capacity;
			if (resetOnRelease)
			{
				ResetRegistry.Register(this);
			}
		}

		public string Name
		{
			get
			{
				return _name;
			}
		}

		public int Count
		{
			get
			{
				return _map.Count;
			}
		}

		public bool TryGet(K key, out V value)
		{
			return _map.TryGetValue(key, out value);
		}

		public void Set(K key, V value)
		{
			if (_map.Count >= _capacity && !_map.ContainsKey(key))
			{
				_map.Clear();
				Evictions++;
			}
			_map[key] = value;
		}

		public void Clear()
		{
			_map.Clear();
		}

		public void ResetState()
		{
			_map.Clear();
		}
	}

	public static class TypeCache
	{
		private static readonly Dictionary<string, Type> _map = new Dictionary<string, Type>(StringComparer.Ordinal);

		// Resolves a game/mod class by full name across loaded assemblies. Negative results are cached.
		public static Type Resolve(string fullName)
		{
			Type t;
			if (_map.TryGetValue(fullName, out t))
			{
				return t;
			}
			t = null;
			try
			{
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					Type found = null;
					try
					{
						found = all[i].GetType(fullName, false);
					}
					catch (Exception)
					{
					}
					if (!object.ReferenceEquals(found, null))
					{
						t = found;
						break;
					}
				}
			}
			catch (Exception e)
			{
				ULog.Error("typecache", "resolve failed " + fullName + ": " + e);
			}
			_map[fullName] = t;
			return t;
		}
	}

	public static class PatchUtil
	{
		private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		public static MethodBase Resolve(string module, Type type, string name, Type[] args)
		{
			if (object.ReferenceEquals(type, null))
			{
				ULog.Error(module, "patch target type is null for method " + name);
				return null;
			}
			MethodBase m = null;
			try
			{
				if (args == null)
				{
					m = type.GetMethod(name, All);
				}
				else
				{
					m = type.GetMethod(name, All, null, args, null);
				}
			}
			catch (Exception e)
			{
				ULog.Error(module, "resolve " + type.FullName + "." + name + " threw: " + e.Message);
				return null;
			}
			if (object.ReferenceEquals(m, null))
			{
				ULog.Error(module, "patch target not found: " + type.FullName + "." + name);
			}
			return m;
		}

		public static HarmonyMethod WithPriority(MethodInfo method, int priority)
		{
			HarmonyMethod hm = new HarmonyMethod(method);
			FieldInfo f = typeof(HarmonyMethod).GetField("prioritiy", All);
			if (object.ReferenceEquals(f, null))
			{
				ULog.Error("patchutil", "HarmonyMethod.prioritiy field not found, priority not applied");
				return hm;
			}
			f.SetValue(hm, priority);
			return hm;
		}

		public static bool Install(HarmonyInstance harmony, string module, MethodBase target, HarmonyMethod prefix, HarmonyMethod postfix)
		{
			if (object.ReferenceEquals(target, null))
			{
				ULog.Error(module, "Install skipped: target is null");
				return false;
			}
			if (harmony == null)
			{
				ULog.Error(module, "Install skipped: HarmonyInstance is null");
				return false;
			}
			try
			{
				harmony.Patch(target, prefix, postfix, null);
				ULog.Info(module, "patched " + target.DeclaringType.FullName + "." + target.Name);
				return true;
			}
			catch (Exception e)
			{
				ULog.Error(module, "Install failed " + target.Name + ": " + e);
				return false;
			}
		}

		// ----- api 9: foreign patch owners (owner ids outside "Lobotomy.NailOptimization.") -----
		private const string OwnPrefix = "Lobotomy.NailOptimization.";

		// Name of a foreign owner of any Prefix, Postfix or Transpiler on 'm', or null.
		public static string ForeignAny(MethodBase m)
		{
			Patches ps = PatchesOf(m);
			if (ps == null)
			{
				return null;
			}
			string owner = ForeignIn(ps.Prefixes);
			if (owner == null)
			{
				owner = ForeignIn(ps.Postfixes);
			}
			return owner != null ? owner : ForeignIn(ps.Transpilers);
		}

		public static string ForeignPrefix(MethodBase m)
		{
			Patches ps = PatchesOf(m);
			return ps == null ? null : ForeignIn(ps.Prefixes);
		}

		public static string ForeignPostfix(MethodBase m)
		{
			Patches ps = PatchesOf(m);
			return ps == null ? null : ForeignIn(ps.Postfixes);
		}

		public static string ForeignTranspiler(MethodBase m)
		{
			Patches ps = PatchesOf(m);
			return ps == null ? null : ForeignIn(ps.Transpilers);
		}

		private static Patches PatchesOf(MethodBase m)
		{
			if (object.ReferenceEquals(m, null))
			{
				return null;
			}
			try
			{
				return PatchProcessor.IsPatched(m);
			}
			catch (Exception e)
			{
				ULog.Warn("patchutil", "patch info of " + m.Name + " unreadable: " + e.Message);
				return null;
			}
		}

		private static string ForeignIn(IList<Patch> patches)
		{
			if (patches == null)
			{
				return null;
			}
			for (int i = 0; i < patches.Count; i++)
			{
				string owner = patches[i].owner;
				if (owner == null || !owner.StartsWith(OwnPrefix, StringComparison.Ordinal))
				{
					return owner == null ? "?" : owner;
				}
			}
			return null;
		}

		// ----- api 9: guards for prefixes that return false -----
		private sealed class Guard
		{
			public MethodBase Target;
			public MethodInfo Prefix;
			public Flag Flag;
			public string Feature;
			public string Module;
			public bool NotFoundLogged;
		}

		private static readonly List<Guard> _guards = new List<Guard>();

		// Registers an installed prefix that may return false. At the deferred phase and at every day start Core checks the
		// target: a foreign Transpiler, or a foreign Prefix that runs after ours (lower priority, or the same priority and a
		// later index; Harmony 1 skips the prefixes after a false), switches flag.On off and logs an ERROR. HarmonyBefore and
		// HarmonyAfter of other mods are not evaluated. Use one flag per guarded target: a trip switches the whole flag.
		public static bool GuardLate(string module, MethodBase target, MethodInfo prefix, Flag flag, string feature)
		{
			if (object.ReferenceEquals(target, null) || object.ReferenceEquals(prefix, null) || flag == null)
			{
				ULog.Warn(module, "GuardLate skipped for " + feature + ": null argument");
				return false;
			}
			Guard g = new Guard();
			g.Target = target;
			g.Prefix = prefix;
			g.Flag = flag;
			g.Feature = feature;
			g.Module = module;
			_guards.Add(g);
			return true;
		}

		internal static void CheckGuards(string when)
		{
			for (int i = 0; i < _guards.Count; i++)
			{
				Guard g = _guards[i];
				if (!g.Flag.On)
				{
					continue;
				}
				try
				{
					string owner = Overtaker(g);
					if (owner == null)
					{
						continue;
					}
					g.Flag.On = false;
					Type dt = g.Target.DeclaringType;
					string target = (object.ReferenceEquals(dt, null) ? "?" : dt.FullName) + "." + g.Target.Name;
					ULog.Error(g.Module, g.Feature + " off (" + when + "): " + owner + " runs after our prefix on " + target);
				}
				catch (Exception e)
				{
					ULog.Warn(g.Module, "guard " + g.Feature + " check failed: " + e.Message);
				}
			}
		}

		internal static void CheckGuardsAtDayStart()
		{
			CheckGuards("day start");
		}

		private static string Overtaker(Guard g)
		{
			Patches ps = PatchesOf(g.Target);
			if (ps == null)
			{
				return null;
			}
			string trans = ForeignIn(ps.Transpilers);
			if (trans != null)
			{
				return trans + " (Transpiler)";
			}
			IList<Patch> pre = ps.Prefixes;
			Patch ours = null;
			for (int i = 0; i < pre.Count; i++)
			{
				if (SameMethod(pre[i].patch, g.Prefix))
				{
					ours = pre[i];
					break;
				}
			}
			if (ours == null)
			{
				if (!g.NotFoundLogged)
				{
					g.NotFoundLogged = true;
					ULog.Warn(g.Module, "guard " + g.Feature + ": own prefix not found on " + g.Target.Name);
				}
				return null;
			}
			for (int i = 0; i < pre.Count; i++)
			{
				Patch p = pre[i];
				if (object.ReferenceEquals(p, ours) || (p.owner != null && p.owner.StartsWith(OwnPrefix, StringComparison.Ordinal)))
				{
					continue;
				}
				if (p.priority < ours.priority || (p.priority == ours.priority && p.index > ours.index))
				{
					return (p.owner == null ? "?" : p.owner) + " (Prefix)";
				}
			}
			return null;
		}

		// Patch.patch comes back from the serialized PatchInfo; Equals can fail on another reflected instance.
		private static bool SameMethod(MethodInfo a, MethodInfo b)
		{
			if (object.ReferenceEquals(a, null) || object.ReferenceEquals(b, null))
			{
				return false;
			}
			if (a.Equals(b))
			{
				return true;
			}
			int ta = a.MetadataToken;
			int tb = b.MetadataToken;
			return ta == tb && a.Module.Equals(b.Module);
		}
	}

	// ---------- api 9: installs that wait for another mod's assembly ----------
	public static class ModHooks
	{
		private sealed class Pending
		{
			public string Assembly;
			public Action Install;
			public Action<Assembly> InstallAsm;
			public string Module;
		}

		private static readonly List<Pending> _pending = new List<Pending>();
		private static volatile bool _loaded;
		private static bool _subscribed;

		// Runs 'install' once on the main thread when an assembly with the simple name 'assemblyName' is loaded: now if it
		// already is (so finish the module's own setup before the call), else on the first driver frame after it loads.
		// Exceptions are logged under 'module'. Main thread only.
		public static void WhenLoaded(string assemblyName, Action install, string module)
		{
			Add(assemblyName, install, null, module);
		}

		// Same, with the loaded assembly passed to 'install'.
		public static void WhenLoaded(string assemblyName, Action<Assembly> install, string module)
		{
			Add(assemblyName, null, install, module);
		}

		private static void Add(string assemblyName, Action install, Action<Assembly> installAsm, string module)
		{
			if (string.IsNullOrEmpty(assemblyName) || (install == null && installAsm == null))
			{
				ULog.Warn(module, "WhenLoaded skipped: empty assembly name or null install");
				return;
			}
			if (!_subscribed)
			{
				_subscribed = true;
				AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			}
			Pending p = new Pending();
			p.Assembly = assemblyName;
			p.Install = install;
			p.InstallAsm = installAsm;
			p.Module = module;
			Assembly asm = Find(assemblyName);
			if (!object.ReferenceEquals(asm, null))
			{
				Run(p, asm);
				return;
			}
			_pending.Add(p);
		}

		public static Assembly Find(string assemblyName)
		{
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			string prefix = assemblyName + ",";
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

		private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
		{
			_loaded = true;
		}

		// Driver frame; 'force' rescans without a load event (deferred phase).
		internal static void Tick(bool force)
		{
			if (_pending.Count == 0 || (!_loaded && !force))
			{
				return;
			}
			_loaded = false;
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				Pending p = _pending[i];
				Assembly asm = Find(p.Assembly);
				if (!object.ReferenceEquals(asm, null))
				{
					_pending.RemoveAt(i);
					Run(p, asm);
				}
			}
			if (force && _pending.Count > 0)
			{
				StringBuilder sb = new StringBuilder();
				for (int i = 0; i < _pending.Count; i++)
				{
					sb.Append(i == 0 ? "" : ", ").Append(_pending[i].Assembly).Append(" (").Append(_pending[i].Module).Append(")");
				}
				ULog.Info("core", "WhenLoaded pending: " + sb.ToString());
			}
		}

		private static void Run(Pending p, Assembly asm)
		{
			try
			{
				if (p.Install != null)
				{
					p.Install();
				}
				else
				{
					p.InstallAsm(asm);
				}
				ULog.Info(p.Module, "WhenLoaded " + p.Assembly + " installed");
			}
			catch (Exception e)
			{
				ULog.Error(p.Module, "install for " + p.Assembly + " threw: " + e);
			}
		}
	}

	// ---------- api 9: game notices without own IObserver classes ----------
	public static class Events
	{
		private sealed class Relay : IObserver
		{
			public string Notice;
			public readonly List<Action> Handlers = new List<Action>();
			public readonly List<string> Modules = new List<string>();
			public readonly List<int> Errors = new List<int>();

			public void OnNotice(string notice, params object[] param)
			{
				int n = Handlers.Count;
				for (int i = 0; i < n; i++)
				{
					try
					{
						Handlers[i]();
					}
					catch (Exception e)
					{
						Errors[i]++;
						if (Errors[i] <= 3)
						{
							ULog.Error(Modules[i], "handler of " + Notice + " threw" + (Errors[i] == 3 ? " (further errors not logged): " : ": ") + e);
						}
					}
				}
			}
		}

		private static readonly Dictionary<string, Relay> _relays = new Dictionary<string, Relay>(StringComparer.Ordinal);

		// Calls 'handler' on the main thread each time the game sends 'notice' (a NoticeName constant), in registration order.
		public static bool On(string notice, Action handler, string module)
		{
			if (string.IsNullOrEmpty(notice) || handler == null)
			{
				ULog.Warn(module, "Events.On skipped: empty notice or null handler");
				return false;
			}
			Relay r;
			if (!_relays.TryGetValue(notice, out r))
			{
				r = new Relay();
				r.Notice = notice;
				try
				{
					Notice.instance.Observe(notice, r);
				}
				catch (Exception e)
				{
					ULog.Error(module, "Events.On " + notice + " failed: " + e.Message);
					return false;
				}
				_relays[notice] = r;
			}
			if (r.Handlers.Contains(handler))
			{
				return true;
			}
			r.Handlers.Add(handler);
			r.Modules.Add(module);
			r.Errors.Add(0);
			return true;
		}
	}

	// ---------- api 9: reusable arrays per call depth (main thread only) ----------
	public sealed class DepthBuffers<T>
	{
		private readonly List<T[]> _bufs = new List<T[]>();
		private readonly int _min;
		private readonly int _thread;
		private int _depth;

		public DepthBuffers(int minLength)
		{
			_min = minLength < 1 ? 1 : minLength;
			_thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
		}

		// False on another thread than the creator; callers on such threads must not Rent.
		public bool OnOwnerThread
		{
			get
			{
				return System.Threading.Thread.CurrentThread.ManagedThreadId == _thread;
			}
		}

		public int Depth
		{
			get
			{
				return _depth;
			}
		}

		// An array of at least 'length' for the next depth; pair every Rent with Return (try/finally in the caller).
		public T[] Rent(int length)
		{
			if (_depth == _bufs.Count)
			{
				_bufs.Add(new T[length > _min ? length : _min]);
			}
			T[] buf = _bufs[_depth];
			if (buf.Length < length)
			{
				buf = new T[length > buf.Length * 2 ? length : buf.Length * 2];
				_bufs[_depth] = buf;
			}
			_depth++;
			return buf;
		}

		// Releases the deepest array and clears its first 'used' slots, so references do not stay alive.
		public void Return(int used)
		{
			if (_depth == 0)
			{
				return;
			}
			_depth--;
			T[] buf = _bufs[_depth];
			int n = used < 0 ? 0 : (used < buf.Length ? used : buf.Length);
			Array.Clear(buf, 0, n);
		}

		// Drops all depths and clears every array (after an exception left Rent without Return).
		public void Reset()
		{
			_depth = 0;
			for (int i = 0; i < _bufs.Count; i++)
			{
				Array.Clear(_bufs[i], 0, _bufs[i].Length);
			}
		}
	}

	// ---------- bootstrap ----------
	public class Harmony_Patch
	{
		private static bool installed;

		public Harmony_Patch()
		{
			if (installed)
			{
				PLog.Write("WARN", "second Harmony_Patch instance ignored");
				return;
			}
			installed = true;
			try
			{
				Boot();
			}
			catch (Exception e)
			{
				PLog.Write("ERROR", "boot failed: " + e);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Boot()
		{
			PLog.Init();
			PLog.Write("INFO", "constructor reached");
			try
			{
				Config.Load();
				CoreSettings.Load();
				ULog.Info("core", "boot apiLevel=" + Core.ApiLevel + " contract=" + Core.ContractVersion + " dir=" + PLog.Dir);
			}
			catch (Exception e)
			{
				PLog.Write("ERROR", "config phase failed: " + e);
			}
			ModuleLoader.LoadAll();
			NailDriver.Create();
		}
	}

	// ---------- logging ----------
	internal static class PLog
	{
		internal static string Dir;
		private static string _path;
		private static bool _inited;
		private static bool _writeFailed;

		internal static void Init()
		{
			if (_inited)
			{
				return;
			}
			_inited = true;
			try
			{
				string loc = typeof(Harmony_Patch).Assembly.Location;
				if (!string.IsNullOrEmpty(loc))
				{
					Dir = Path.GetDirectoryName(loc);
				}
			}
			catch (Exception)
			{
			}
			try
			{
				if (string.IsNullOrEmpty(Dir))
				{
					Dir = Application.dataPath;
				}
				if (!Directory.Exists(Dir))
				{
					Directory.CreateDirectory(Dir);
				}
				_path = Path.Combine(Dir, "NailOptimization.log");
				File.WriteAllText(_path, string.Empty);
			}
			catch (Exception)
			{
			}
		}

		internal static string Frame()
		{
			try
			{
				return Time.frameCount.ToString();
			}
			catch (Exception)
			{
				return "?";
			}
		}

		internal static void Write(string level, string msg)
		{
			WriteRaw("[" + DateTime.Now.ToString("HH:mm:ss.fff") + " f=" + Frame() + "] " + level + " " + msg + Environment.NewLine);
		}

		internal static void WriteRaw(string text)
		{
			try
			{
				if (!_inited)
				{
					Init();
				}
				File.AppendAllText(_path, text);
			}
			catch (Exception e)
			{
				if (!_writeFailed)
				{
					_writeFailed = true;
					try
					{
						UnityEngine.Debug.Log("NailOptimization log write failed: " + e.Message);
					}
					catch (Exception)
					{
					}
				}
			}
		}
	}

	// ---------- environment (frame 2) ----------
	internal static class EnvProbe
	{
		internal static bool IsNoise(string n)
		{
			return n.StartsWith("System", StringComparison.Ordinal)
				|| n.StartsWith("mscorlib", StringComparison.Ordinal)
				|| n.StartsWith("UnityEngine", StringComparison.Ordinal)
				|| n.StartsWith("Unity.", StringComparison.Ordinal)
				|| n.StartsWith("Mono.", StringComparison.Ordinal)
				|| n.StartsWith("I18N", StringComparison.Ordinal)
				|| n.StartsWith("Boo.", StringComparison.Ordinal)
				|| n.StartsWith("UnityScript", StringComparison.Ordinal);
		}

		internal static void LogAssemblies(string phase)
		{
			try
			{
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				List<string> names = new List<string>();
				for (int i = 0; i < all.Length; i++)
				{
					string n = all[i].GetName().Name;
					if (!IsNoise(n))
					{
						names.Add(n);
					}
				}
				ULog.Info("env", "assemblies(" + phase + ") total=" + all.Length + " shown=" + names.Count + ": " + string.Join(";", names.ToArray()));
			}
			catch (Exception e)
			{
				ULog.Error("env", "assemblies(" + phase + ") failed: " + e);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void LogAddOn(string phase)
		{
			try
			{
				Add_On inst = Add_On.instance;
				if (inst == null)
				{
					ULog.Info("env", "AddOn(" + phase + ") instance=null version=" + Add_On.version);
					return;
				}
				List<Assembly> list = inst.AssemList;
				if (list == null)
				{
					ULog.Info("env", "AddOn(" + phase + ") AssemList=null version=" + Add_On.version);
					return;
				}
				List<string> names = new List<string>();
				for (int i = 0; i < list.Count; i++)
				{
					names.Add(list[i].GetName().Name);
				}
				ULog.Info("env", "AddOn(" + phase + ") version=" + Add_On.version + " AssemList=" + list.Count + ": " + string.Join(";", names.ToArray()));
			}
			catch (Exception e)
			{
				ULog.Error("env", "AddOn(" + phase + ") failed: " + e);
			}
		}
	}

	// ---------- module registry and loader ----------
	internal sealed class ModuleInfo
	{
		internal string Name;
		internal string Version = "?";
		internal string File;
		internal string Asm = "?";
		internal string TypeName = "?";
		internal string Status = "Created";
		internal string Error = string.Empty;
		internal string[] Targets = new string[0];
		internal ModuleBase Base;
		internal int CallbackErrors;
	}

	internal static class ModuleRegistry
	{
		internal static readonly List<ModuleInfo> List = new List<ModuleInfo>();

		internal static int OkCount
		{
			get
			{
				int n = 0;
				for (int i = 0; i < List.Count; i++)
				{
					if (List[i].Status == "Ok")
					{
						n++;
					}
				}
				return n;
			}
		}

		internal static void AddFailure(string file, string status, string error)
		{
			ModuleInfo mi = new ModuleInfo();
			mi.Name = file;
			mi.File = file;
			mi.Status = status;
			mi.Error = error;
			List.Add(mi);
		}

		internal static void LogSummary()
		{
			ULog.Info("modules", "registry count=" + List.Count + " ok=" + OkCount);
			for (int i = 0; i < List.Count; i++)
			{
				ModuleInfo m = List[i];
				string line = m.Name + " v" + m.Version + " status=" + m.Status + " file=" + m.File;
				if (m.Error.Length > 0)
				{
					line = line + " error=" + m.Error;
				}
				ULog.Info("modules", line);
			}
		}
	}

	internal static class ModuleLoader
	{
		private static int _files;
		private static int _skipped;
		private static int _loaded;
		private static int _inited;
		private static int _errors;

		internal static void LoadAll()
		{
			try
			{
				string dir = Path.Combine(PLog.Dir, "modules");
				if (!Directory.Exists(dir))
				{
					ULog.Warn("loader", "modules dir missing: " + dir);
					return;
				}
				string[] paths = Directory.GetFiles(dir);
				Array.Sort(paths, StringComparer.Ordinal);
				List<string> ordered = new List<string>(paths);
				string[] want = CoreSettings.Order.Split(',');
				int insertAt = 0;
				for (int w = 0; w < want.Length; w++)
				{
					string nm = want[w].Trim();
					if (nm.Length == 0)
					{
						continue;
					}
					string target = nm.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? nm : nm + ".dll";
					for (int p = 0; p < ordered.Count; p++)
					{
						if (string.Equals(Path.GetFileName(ordered[p]), target, StringComparison.OrdinalIgnoreCase))
						{
							string s = ordered[p];
							ordered.RemoveAt(p);
							ordered.Insert(insertAt, s);
							insertAt++;
							break;
						}
					}
				}
				List<string> seen = new List<string>();
				for (int i = 0; i < ordered.Count; i++)
				{
					string path = ordered[i];
					string file = Path.GetFileName(path);
					_files++;
					ULog.Info("loader", "module file: " + file);
					if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					{
						_skipped++;
						ULog.Info("loader", "skip (name does not end with .dll): " + file);
						continue;
					}
					string asmName;
					try
					{
						AssemblyName an = AssemblyName.GetAssemblyName(path);
						asmName = an.Name;
					}
					catch (Exception e)
					{
						_errors++;
						ULog.Error("loader", "bad assembly file " + file + ": " + e.GetType().Name + " " + e.Message);
						ModuleRegistry.AddFailure(file, "BadAssembly", e.GetType().Name);
						continue;
					}
					if (seen.Contains(asmName))
					{
						_skipped++;
						ULog.Warn("loader", "skip (duplicate assembly name " + asmName + "): " + file);
						ModuleRegistry.AddFailure(file, "Skipped", "duplicate assembly name " + asmName);
						continue;
					}
					seen.Add(asmName);
					LoadOne(path, file);
				}
			}
			catch (Exception e2)
			{
				ULog.Error("loader", "LoadAll failed: " + e2);
			}
			ULog.Info("loader", "summary: files=" + _files + " skipped=" + _skipped + " loaded=" + _loaded + " initialized=" + _inited + " errors=" + _errors);
		}

		private static void LoadOne(string path, string file)
		{
			Assembly asm;
			try
			{
				asm = Assembly.LoadFile(path);
			}
			catch (Exception e)
			{
				_errors++;
				ULog.Error("loader", "LoadFile failed " + file + ": " + e.GetType().Name + " " + e.Message);
				ModuleRegistry.AddFailure(file, "LoadFailed", e.GetType().Name);
				return;
			}
			_loaded++;
			Type[] types;
			try
			{
				types = asm.GetTypes();
			}
			catch (ReflectionTypeLoadException rex)
			{
				_errors++;
				ULog.Error("loader", "GetTypes partial " + file + ": " + rex.Message);
				Exception[] le = rex.LoaderExceptions;
				if (le != null)
				{
					for (int j = 0; j < le.Length; j++)
					{
						ULog.Error("loader", "  loader exception: " + le[j].GetType().Name + " " + le[j].Message);
					}
				}
				types = rex.Types;
			}
			catch (Exception e2)
			{
				_errors++;
				ULog.Error("loader", "GetTypes failed " + file + ": " + e2.GetType().Name + " " + e2.Message);
				ModuleRegistry.AddFailure(file, "TypesFailed", e2.GetType().Name);
				return;
			}
			if (types == null)
			{
				return;
			}
			ULog.Info("loader", "module " + file + " types=" + types.Length);
			for (int k = 0; k < types.Length; k++)
			{
				Type t = types[k];
				if (object.ReferenceEquals(t, null))
				{
					continue;
				}
				InitType(t, file);
			}
		}

		private static void InitType(Type t, string file)
		{
			bool isModule;
			try
			{
				isModule = t.IsClass && !t.IsAbstract && typeof(IModule).IsAssignableFrom(t);
				if (!isModule && t.IsClass)
				{
					CheckIdentityMismatch(t, file);
				}
			}
			catch (Exception e)
			{
				_errors++;
				ULog.Error("loader", "type check failed " + file + ": " + e.GetType().Name + " " + e.Message);
				return;
			}
			if (!isModule)
			{
				return;
			}
			ModuleInfo mi = new ModuleInfo();
			mi.File = file;
			mi.Name = t.Name;
			mi.TypeName = t.FullName;
			try
			{
				mi.Asm = t.Assembly.GetName().Name;
			}
			catch (Exception)
			{
			}
			ModuleRegistry.List.Add(mi);
			IModule m;
			try
			{
				m = Activator.CreateInstance(t) as IModule;
			}
			catch (Exception e2)
			{
				_errors++;
				mi.Status = "Error";
				mi.Error = "create: " + e2.GetType().Name;
				ULog.Error("loader", "create failed " + t.FullName + " (" + file + "): " + e2);
				return;
			}
			if (m == null)
			{
				_errors++;
				mi.Status = "Error";
				mi.Error = "cast to IModule failed";
				ULog.Error("loader", "cast to IModule failed " + t.FullName + " (" + file + ")");
				return;
			}
			int required = 1;
			try
			{
				mi.Name = m.Name;
				ModuleBase mb = m as ModuleBase;
				if (mb != null)
				{
					mi.Base = mb;
					mi.Version = mb.Version;
					required = mb.RequiredApiLevel;
					string[] targets = mb.Targets;
					if (targets != null)
					{
						mi.Targets = targets;
					}
				}
			}
			catch (Exception e4)
			{
				_errors++;
				mi.Status = "Error";
				mi.Error = "metadata: " + e4.GetType().Name;
				ULog.Error("loader", "metadata read threw " + t.FullName + " (" + file + "): " + e4);
				return;
			}
			if (required > Core.ApiLevel)
			{
				_errors++;
				mi.Status = "VersionMismatch";
				mi.Error = "requires api " + required + ", core api " + Core.ApiLevel;
				ULog.Error("loader", "module " + mi.Name + " (" + file + ") requires api " + required + " but core api is " + Core.ApiLevel + ": Init not called");
				return;
			}
			try
			{
				m.Init();
				_inited++;
				mi.Status = "Ok";
				ULog.Info("loader", "module init ok: " + t.FullName + " name=" + mi.Name + " v" + mi.Version + " (" + file + ")");
			}
			catch (Exception e3)
			{
				_errors++;
				mi.Status = "Error";
				mi.Error = "Init: " + e3.GetType().Name;
				ULog.Error("loader", "module init threw: " + t.FullName + " (" + file + "): " + e3);
			}
		}

		// detects a second copy of the contract types (IsAssignableFrom false although names match)
		private static void CheckIdentityMismatch(Type t, string file)
		{
			Type[] ifs = t.GetInterfaces();
			for (int i = 0; i < ifs.Length; i++)
			{
				if (ifs[i].Name == "IModule")
				{
					ULog.Error("loader", "IModule identity mismatch (duplicate Core copy?): " + t.FullName + " (" + file + ")");
					return;
				}
			}
			Type b = t.BaseType;
			while (!object.ReferenceEquals(b, null))
			{
				if (b.Name == "ModuleBase")
				{
					ULog.Error("loader", "ModuleBase identity mismatch (duplicate Core copy?): " + t.FullName + " (" + file + ")");
					return;
				}
				b = b.BaseType;
			}
		}
	}

	// ---------- conflict detector (frame 2, warnings only) ----------
	internal static class ConflictDetector
	{
		internal static int Warnings;
		internal static int ThirdParty;

		private sealed class PatchRow
		{
			public string Target;
			public string Asm;
			public string Owner;
			public string Kind;
			public int Prio;
		}

		internal static void Run()
		{
			try
			{
				DetectAssemblies();
			}
			catch (Exception e)
			{
				ULog.Error("detect", "assembly scan failed: " + e);
			}
			try
			{
				ScanPatches();
			}
			catch (Exception e2)
			{
				ULog.Error("detect", "patch scan failed: " + e2);
			}
		}

		private static string[] Tokens()
		{
			string[] raw = CoreSettings.DetectNames.Split(',');
			List<string> list = new List<string>();
			for (int i = 0; i < raw.Length; i++)
			{
				string t = raw[i].Trim();
				if (t.Length > 0)
				{
					list.Add(t);
				}
			}
			return list.ToArray();
		}

		private static bool Matches(string text, string[] tokens)
		{
			for (int i = 0; i < tokens.Length; i++)
			{
				if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		private static void DetectAssemblies()
		{
			string[] tokens = Tokens();
			if (tokens.Length == 0)
			{
				return;
			}
			Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < all.Length; i++)
			{
				string n = all[i].GetName().Name;
				if (EnvProbe.IsNoise(n) || n == "NailOptimization")
				{
					continue;
				}
				if (Matches(n, tokens))
				{
					ThirdParty++;
					Warnings++;
					ULog.Warn("detect", "third-party optimizer assembly present: " + n);
				}
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void ScanPatches()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			string[] tokens = Tokens();
			List<MethodBase> methods = new List<MethodBase>(PatchProcessor.AllPatchedMethods());
			List<PatchRow> rows = new List<PatchRow>();
			for (int i = 0; i < methods.Count; i++)
			{
				MethodBase m = methods[i];
				if (object.ReferenceEquals(m, null))
				{
					continue;
				}
				Patches p;
				try
				{
					p = PatchProcessor.IsPatched(m);
				}
				catch (Exception e)
				{
					ULog.Error("detect", "IsPatched failed: " + e.GetType().Name + " " + e.Message);
					continue;
				}
				if (p == null)
				{
					continue;
				}
				string target = Describe(m);
				AddRows(rows, target, "prefix", p.Prefixes);
				AddRows(rows, target, "postfix", p.Postfixes);
				AddRows(rows, target, "transpiler", p.Transpilers);
			}
			Dictionary<string, List<string>> asmsByTarget = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			List<string> ownerSeen = new List<string>();
			StringBuilder detail = new StringBuilder();
			for (int r = 0; r < rows.Count; r++)
			{
				PatchRow row = rows[r];
				List<string> asms;
				if (!asmsByTarget.TryGetValue(row.Target, out asms))
				{
					asms = new List<string>();
					asmsByTarget.Add(row.Target, asms);
				}
				if (!asms.Contains(row.Asm))
				{
					asms.Add(row.Asm);
				}
				if (tokens.Length > 0 && row.Owner != null && !ownerSeen.Contains(row.Owner) && Matches(row.Owner, tokens))
				{
					ownerSeen.Add(row.Owner);
					ThirdParty++;
					Warnings++;
					ULog.Warn("detect", "third-party optimizer Harmony id present: " + row.Owner + " (assembly " + row.Asm + ")");
				}
				if (CoreSettings.DumpPatches)
				{
					detail.Append("  ").Append(row.Target).Append(" | ").Append(row.Kind).Append(" | owner=").Append(row.Owner).Append(" prio=").Append(row.Prio).Append(" | ").Append(row.Asm).Append(Environment.NewLine);
				}
			}
			if (CoreSettings.DumpPatches && ULog.Enabled)
			{
				PLog.WriteRaw(detail.ToString());
			}
			int contested = 0;
			foreach (KeyValuePair<string, List<string>> kv in asmsByTarget)
			{
				if (kv.Value.Count >= 2)
				{
					contested++;
					Warnings++;
					ULog.Warn("detect", "CONTESTED " + kv.Key + " assemblies=" + string.Join(",", kv.Value.ToArray()));
				}
			}
			for (int mi = 0; mi < ModuleRegistry.List.Count; mi++)
			{
				ModuleInfo mod = ModuleRegistry.List[mi];
				for (int t = 0; t < mod.Targets.Length; t++)
				{
					List<string> asms2;
					if (!asmsByTarget.TryGetValue(mod.Targets[t], out asms2))
					{
						ULog.Info("detect", "module " + mod.Name + " target not patched: " + mod.Targets[t]);
						continue;
					}
					List<string> foreign = new List<string>();
					for (int a = 0; a < asms2.Count; a++)
					{
						if (asms2[a] != mod.Asm && asms2[a] != "NailOptimization")
						{
							foreign.Add(asms2[a]);
						}
					}
					if (foreign.Count > 0)
					{
						Warnings++;
						ULog.Warn("detect", "module " + mod.Name + " target " + mod.Targets[t] + " also patched by: " + string.Join(",", foreign.ToArray()));
					}
				}
			}
			ULog.Info("detect", "patch scan: methods=" + methods.Count + " patches=" + rows.Count + " contested=" + contested + " thirdParty=" + ThirdParty + " warnings=" + Warnings + " elapsedMs=" + sw.ElapsedMilliseconds);
		}

		private static void AddRows(List<PatchRow> rows, string target, string kind, IList<Patch> list)
		{
			if (list == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				Patch pt = list[i];
				PatchRow row = new PatchRow();
				row.Target = target;
				row.Kind = kind;
				row.Owner = pt.owner;
				row.Prio = pt.priority;
				row.Asm = AsmOf(pt);
				rows.Add(row);
			}
		}

		private static string AsmOf(Patch pt)
		{
			try
			{
				MethodInfo mi = pt.patch;
				if (object.ReferenceEquals(mi, null))
				{
					return "?";
				}
				Type dt = mi.DeclaringType;
				if (object.ReferenceEquals(dt, null))
				{
					return "?";
				}
				return dt.Assembly.GetName().Name;
			}
			catch (Exception)
			{
				return "?";
			}
		}

		private static string Describe(MethodBase m)
		{
			try
			{
				Type dt = m.DeclaringType;
				if (object.ReferenceEquals(dt, null))
				{
					return m.Name;
				}
				return dt.FullName + "." + m.Name;
			}
			catch (Exception)
			{
				return "?";
			}
		}
	}

	// ---------- reset hook ----------
	internal class ResetObserver : IObserver
	{
		public void OnNotice(string notice, params object[] param)
		{
			try
			{
				ResetRegistry.ResetAll();
			}
			catch (Exception e)
			{
				ULog.Error("reset", "observer failed: " + e);
			}
		}
	}

	// ---------- driver ----------
	public class NailDriver : MonoBehaviour
	{
		private static bool _created;
		private int _tick;
		private bool _deferredDone;
		private bool _observerDone;
		private int _observerTries;
		private int _errors;

		internal static void Create()
		{
			if (_created)
			{
				return;
			}
			_created = true;
			try
			{
				GameObject go = new GameObject("NailOptimizationDriver");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.AddComponent<NailDriver>();
				ULog.Info("core", "driver created");
			}
			catch (Exception e)
			{
				ULog.Error("core", "driver create failed: " + e);
			}
		}

		public void Update()
		{
			try
			{
				_tick++;
				if (!_deferredDone && _tick >= 2)
				{
					_deferredDone = true;
					RunDeferred();
				}
				if (!_observerDone)
				{
					TryRegisterObserver();
				}
				ModHooks.Tick(false);
				UiHost.Tick();
				GuiPanel.Tick();
				TitleWatch.Poll();
				Config.Tick();
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors <= 3)
				{
					ULog.Error("core", "driver Update threw (" + _errors + "): " + e);
				}
			}
		}

		private static void RunDeferred()
		{
			ULog.Info("core", "deferred phase start");
			EnvProbe.LogAssemblies("frame2");
			EnvProbe.LogAddOn("frame2");
			ConflictDetector.Run();
			ModHooks.Tick(true);
			PatchUtil.CheckGuards("deferred");
			Events.On(NoticeName.OnStageStart, PatchUtil.CheckGuardsAtDayStart, "core");
			ModuleRegistry.LogSummary();
			Config.FlushMissing();
			if (CoreSettings.Prewarm)
			{
				GuiPanel.Prewarm();
			}
			ULog.Info("core", "deferred phase end");
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TryRegisterObserver()
		{
			_observerTries++;
			Notice n = Notice.instance;
			if (n == null)
			{
				if (_observerTries > 600)
				{
					_observerDone = true;
					ULog.Error("core", "Notice.instance still null after 600 frames, reset hook not registered");
				}
				return;
			}
			IObserver o = new ResetObserver();
			n.Observe(NoticeName.OnReleaseGameManager, o);
			_observerDone = true;
			ULog.Info("core", "reset observer registered on OnReleaseGameManager after " + _observerTries + " tries");
		}
	}

	// ---------- read-only panel (uGUI) ----------
	public class PanelTicker : MonoBehaviour
	{
		public int Ticks;

		public void Update()
		{
			Ticks++;
		}
	}

	internal static class GuiPanel
	{
		private static GameObject _root;
		private static RectTransform _panelRt;
		private static PanelTicker _ticker;
		private static Text _label;
		private static Font _font;
		private static bool _open;
		private static bool _buildFailed;
		private static bool _dead;
		private static int _errors;
		private static int _builds;
		private static int _openFrame;
		private static int _framesOpen;
		private static int _closedFramesLeft;
		private static int _ticksAtClose;
		private static float[] _dt = new float[32];
		private static int _timingCount;
		private static int _timingTarget;
		private static float _openCallMs;
		private static float _buildMs;
		private static RectTransform _toggleRt;
		private static Camera _toggleCam;
		private const int RowsPerPage = 8;
		private static readonly Color ColOn = new Color(0.2f, 0.55f, 0.3f, 1f);
		private static readonly Color ColOff = new Color(0.35f, 0.35f, 0.42f, 1f);
		private static readonly Color ColBtn = new Color(0.25f, 0.28f, 0.4f, 1f);
		private static Text _pageTitle;
		private static Text _footer;
		private static GameObject _rowsRoot;
		private static readonly List<PageInfo> _pages = new List<PageInfo>();
		private static readonly List<ParamRow> _rows = new List<ParamRow>();
		private static int _page;
		private static int _seenVersion;
		private static bool _suppress;

		internal static void SetToggleArea(RectTransform rt, Camera cam)
		{
			_toggleRt = rt;
			_toggleCam = cam;
		}

		private static bool OverToggleArea()
		{
			if (_toggleRt == null)
			{
				return false;
			}
			Camera cam = null;
			if (_toggleCam != null)
			{
				cam = _toggleCam;
			}
			return RectTransformUtility.RectangleContainsScreenPoint(_toggleRt, Input.mousePosition, cam);
		}

		internal static bool OverToggleAt(Vector2 screenPoint)
		{
			if (_toggleRt == null)
			{
				return false;
			}
			Camera cam = null;
			if (_toggleCam != null)
			{
				cam = _toggleCam;
			}
			return RectTransformUtility.RectangleContainsScreenPoint(_toggleRt, screenPoint, cam);
		}

		internal static bool IsOpen
		{
			get
			{
				return _open;
			}
		}

		internal static void Tick()
		{
			if (_dead)
			{
				return;
			}
			try
			{
				if (_open)
				{
					_framesOpen++;
					if (_seenVersion != Config.Version)
					{
						_seenVersion = Config.Version;
						RefreshRows();
					}
					if (Input.GetMouseButtonUp(0))
					{
						DeselectOwn();
					}
					if (_timingCount < _timingTarget)
					{
						_dt[_timingCount] = Time.unscaledDeltaTime * 1000f;
						_timingCount++;
						if (_timingCount == _timingTarget)
						{
							LogTiming();
						}
					}
					if (Time.frameCount > _openFrame + 1 && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, null) && !OverToggleArea())
					{
						ULog.Info("gui", "outside click -> close " + PointerInfo());
						Close();
					}
				}
				else if (_closedFramesLeft > 0)
				{
					_closedFramesLeft--;
					if (_closedFramesLeft == 0 && _ticker != null)
					{
						ULog.Info("gui", "closed-state panel Update calls over 120 frames = " + (_ticker.Ticks - _ticksAtClose) + " (expect 0)");
					}
				}
				if (Input.GetKeyDown(CoreSettings.Hotkey) && !UiHost.Registered)
				{
					Toggle();
				}
			}
			catch (Exception e)
			{
				_errors++;
				ULog.Error("gui", "Tick threw (" + _errors + "): " + e);
				if (_errors >= 5)
				{
					_dead = true;
					ULog.Error("gui", "panel disabled after repeated errors");
				}
			}
		}

		internal static void Toggle()
		{
			try
			{
				if (_open)
				{
					Close();
				}
				else
				{
					Open();
				}
			}
			catch (Exception e)
			{
				ULog.Error("gui", "toggle failed: " + e);
			}
		}

		private static void Open()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			if (_root == null && !_buildFailed)
			{
				Build();
			}
			if (_root == null)
			{
				ULog.Error("gui", "open: no root");
				return;
			}
			BuildPages();
			ShowPage();
			_root.SetActive(true);
			_open = true;
			_openFrame = Time.frameCount;
			_framesOpen = 0;
			_closedFramesLeft = 0;
			_timingCount = 0;
			_timingTarget = CoreSettings.OpenTimingFrames;
			if (CoreSettings.LogEnvironment)
			{
				LogEnvironment();
			}
			_openCallMs = (float)sw.Elapsed.TotalMilliseconds;
		}

		private static void Close()
		{
			if (_timingCount > 0 && _timingCount < _timingTarget)
			{
				LogTiming();
			}
			_timingTarget = 0;
			_open = false;
			if (_root != null)
			{
				_root.SetActive(false);
			}
			if (_ticker != null)
			{
				_ticksAtClose = _ticker.Ticks;
				ULog.Info("gui", "close: framesOpen=" + _framesOpen + " panelUpdateCalls=" + _ticker.Ticks);
			}
			_closedFramesLeft = 120;
		}

		private static void LogTiming()
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < _timingCount; i++)
			{
				if (i > 0)
				{
					sb.Append(',');
				}
				sb.Append(_dt[i].ToString("F1", CultureInfo.InvariantCulture));
			}
			ULog.Info("gui", "open timing: buildMs=" + _buildMs.ToString("F1", CultureInfo.InvariantCulture) + " openCallMs=" + _openCallMs.ToString("F1", CultureInfo.InvariantCulture) + " logEnv=" + CoreSettings.LogEnvironment + " dtMs[0..]=" + sb.ToString());
			_timingTarget = _timingCount;
		}

		private static void LogEnvironment()
		{
			try
			{
				string scene = "?";
				try
				{
					scene = SceneManager.GetActiveScene().name;
				}
				catch (Exception)
				{
				}
				Canvas[] all = UnityEngine.Object.FindObjectsOfType<Canvas>();
				int maxSort = -32768;
				int overlay = 0;
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i].renderMode == RenderMode.ScreenSpaceOverlay)
					{
						overlay++;
					}
					if (all[i].sortingOrder > maxSort && all[i].gameObject != _root)
					{
						maxSort = all[i].sortingOrder;
					}
				}
				ULog.Info("gui", "open: screen=" + Screen.width + "x" + Screen.height + " timeScale=" + Time.timeScale + " scene=" + scene + " canvases=" + all.Length + " overlay=" + overlay + " maxOtherSort=" + maxSort + " builds=" + _builds + " " + PointerInfo());
			}
			catch (Exception e)
			{
				ULog.Error("gui", "LogEnvironment failed: " + e);
			}
		}

		private static string PointerInfo()
		{
			EventSystem es = EventSystem.current;
			if (es == null)
			{
				return "eventSystem=null";
			}
			return "eventSystem=" + es.name + " overUI=" + es.IsPointerOverGameObject();
		}

		internal static void Prewarm()
		{
			try
			{
				System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
				if (_root == null && !_buildFailed)
				{
					Build();
				}
				if (!object.ReferenceEquals(_font, null) && _font != null)
				{
					StringBuilder chars = new StringBuilder();
					for (int c = 32; c < 127; c++)
					{
						chars.Append((char)c);
					}
					_font.RequestCharactersInTexture(chars.ToString(), 16, FontStyle.Normal);
				}
				ULog.Info("gui", "prewarm done in " + sw.ElapsedMilliseconds + " ms");
			}
			catch (Exception e)
			{
				ULog.Error("gui", "prewarm failed: " + e);
			}
		}

		private static void Build()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				_builds++;
				_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
				ULog.Info("gui", "build: builtin Arial " + (_font == null ? "NULL" : "ok"));
				GameObject root = new GameObject("NailOptimizationPanel");
				UnityEngine.Object.DontDestroyOnLoad(root);
				Canvas canvas = root.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 30000;
				CanvasScaler scaler = root.AddComponent<CanvasScaler>();
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				scaler.referenceResolution = new Vector2(1280f, 720f);
				scaler.matchWidthOrHeight = 0.5f;
				root.AddComponent<GraphicRaycaster>();

				GameObject panel = new GameObject("Panel");
				Image panelImage = panel.AddComponent<Image>();
				panelImage.color = new Color(0.05f, 0.05f, 0.08f, 0.94f);
				Place(panel, root.transform, new Vector2(0f, 0f), new Vector2(760f, 560f));
				_panelRt = panel.GetComponent<RectTransform>();
				_ticker = panel.AddComponent<PanelTicker>();
				Button backdrop = panel.AddComponent<Button>();
				backdrop.transition = Selectable.Transition.None;
				backdrop.targetGraphic = panelImage;
				backdrop.onClick.AddListener(new UnityAction(OnBackdropClick));

				GameObject labelGo = new GameObject("Label");
				_label = labelGo.AddComponent<Text>();
				_label.font = _font;
				_label.fontSize = 14;
				_label.color = Color.white;
				_label.alignment = TextAnchor.UpperLeft;
				_label.horizontalOverflow = HorizontalWrapMode.Wrap;
				_label.verticalOverflow = VerticalWrapMode.Truncate;
				_label.supportRichText = false;
				_label.raycastTarget = false;
				Place(labelGo, panel.transform, new Vector2(0f, 195f), new Vector2(720f, 150f));

				GameObject btn = new GameObject("BtnClose");
				Image img = btn.AddComponent<Image>();
				img.color = new Color(0.25f, 0.28f, 0.4f, 1f);
				Place(btn, panel.transform, new Vector2(0f, -250f), new Vector2(180f, 36f));
				Button b = btn.AddComponent<Button>();
				b.targetGraphic = img;
				b.onClick.AddListener(new UnityAction(OnClickClose));
				GameObject textGo = new GameObject("Caption");
				Text t = textGo.AddComponent<Text>();
				t.font = _font;
				t.fontSize = 16;
				t.color = Color.white;
				t.alignment = TextAnchor.MiddleCenter;
				t.raycastTarget = false;
				t.text = "Close";
				Place(textGo, btn.transform, new Vector2(0f, 0f), new Vector2(180f, 36f));

				_pageTitle = MakeText(panel.transform, "PageTitle", new Vector2(0f, 100f), new Vector2(420f, 30f), 16, TextAnchor.MiddleCenter);
				MakeButton(panel.transform, "BtnPrev", new Vector2(-300f, 100f), new Vector2(110f, 30f), "< Prev", ColBtn, new UnityAction(OnPrev));
				MakeButton(panel.transform, "BtnNext", new Vector2(300f, 100f), new Vector2(110f, 30f), "Next >", ColBtn, new UnityAction(OnNext));
				_footer = MakeText(panel.transform, "Footer", new Vector2(0f, -218f), new Vector2(720f, 24f), 14, TextAnchor.MiddleLeft);

				root.SetActive(false);
				_root = root;
				_buildMs = (float)sw.Elapsed.TotalMilliseconds;
				ULog.Info("gui", "build ok in " + sw.ElapsedMilliseconds + " ms");
			}
			catch (Exception e)
			{
				_buildFailed = true;
				ULog.Error("gui", "build failed: " + e);
			}
		}

		private static void Place(GameObject go, Transform parent, Vector2 pos, Vector2 size)
		{
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = pos;
			rt.sizeDelta = size;
		}

		private sealed class PageInfo
		{
			internal string Title = string.Empty;
			internal bool IsCore;
			internal ModuleInfo Mod;
			internal List<Config.Decl> Items = new List<Config.Decl>();
		}

		private static Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
		{
			GameObject go = new GameObject(name);
			Text t = go.AddComponent<Text>();
			t.font = _font;
			t.fontSize = fontSize;
			t.color = Color.white;
			t.alignment = anchor;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			t.supportRichText = false;
			t.raycastTarget = false;
			Place(go, parent, pos, size);
			return t;
		}

		private static Button MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, string caption, Color color, UnityAction onClick)
		{
			GameObject go = new GameObject(name);
			Image img = go.AddComponent<Image>();
			img.color = color;
			Place(go, parent, pos, size);
			Button b = go.AddComponent<Button>();
			b.targetGraphic = img;
			Navigation nav = new Navigation();
			nav.mode = Navigation.Mode.None;
			b.navigation = nav;
			b.onClick.AddListener(onClick);
			Text t = MakeText(go.transform, "Caption", new Vector2(0f, 0f), size, 14, TextAnchor.MiddleCenter);
			t.text = caption;
			return b;
		}

		private static RectTransform NewRt(string name, Transform parent)
		{
			GameObject g = new GameObject(name, new Type[] { typeof(RectTransform) });
			RectTransform r = g.GetComponent<RectTransform>();
			r.SetParent(parent, false);
			return r;
		}

		private static Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size)
		{
			GameObject go = new GameObject("Slider");
			Image hit = go.AddComponent<Image>();
			hit.color = new Color(0f, 0f, 0f, 0.02f);
			Place(go, parent, pos, size);
			RectTransform rt = go.GetComponent<RectTransform>();

			RectTransform bg = NewRt("Background", rt);
			Image bgImg = bg.gameObject.AddComponent<Image>();
			bgImg.color = new Color(0.2f, 0.22f, 0.3f, 1f);
			bgImg.raycastTarget = false;
			bg.anchorMin = new Vector2(0f, 0.3f);
			bg.anchorMax = new Vector2(1f, 0.7f);
			bg.offsetMin = Vector2.zero;
			bg.offsetMax = Vector2.zero;

			RectTransform fillArea = NewRt("Fill Area", rt);
			fillArea.anchorMin = new Vector2(0f, 0.3f);
			fillArea.anchorMax = new Vector2(1f, 0.7f);
			fillArea.offsetMin = new Vector2(7f, 0f);
			fillArea.offsetMax = new Vector2(-7f, 0f);
			RectTransform fill = NewRt("Fill", fillArea);
			Image fillImg = fill.gameObject.AddComponent<Image>();
			fillImg.color = new Color(0.35f, 0.5f, 0.85f, 1f);
			fillImg.raycastTarget = false;
			fill.sizeDelta = new Vector2(10f, 0f);

			RectTransform area = NewRt("Handle Slide Area", rt);
			area.anchorMin = new Vector2(0f, 0f);
			area.anchorMax = new Vector2(1f, 1f);
			area.offsetMin = new Vector2(7f, 0f);
			area.offsetMax = new Vector2(-7f, 0f);
			RectTransform handle = NewRt("Handle", area);
			Image handleImg = handle.gameObject.AddComponent<Image>();
			handleImg.color = new Color(0.85f, 0.88f, 1f, 1f);
			handle.sizeDelta = new Vector2(14f, 0f);

			Slider s = go.AddComponent<Slider>();
			s.fillRect = fill;
			s.handleRect = handle;
			s.targetGraphic = handleImg;
			s.direction = Slider.Direction.LeftToRight;
			Navigation nav = new Navigation();
			nav.mode = Navigation.Mode.None;
			s.navigation = nav;
			return s;
		}

		private static void Deselect()
		{
			EventSystem es = EventSystem.current;
			if (es != null)
			{
				es.SetSelectedGameObject(null);
			}
		}

		// Keyboard keys must never drive a selected slider/button (arrows and Enter belong to the game).
		private static void DeselectOwn()
		{
			EventSystem es = EventSystem.current;
			if (es == null || _root == null)
			{
				return;
			}
			GameObject sel = es.currentSelectedGameObject;
			if (sel != null && sel.transform.IsChildOf(_root.transform))
			{
				es.SetSelectedGameObject(null);
			}
		}

		private static ModuleInfo OwnerOf(Config.Decl d)
		{
			if (d.Module.Length == 0)
			{
				return null;
			}
			for (int i = 0; i < ModuleRegistry.List.Count; i++)
			{
				ModuleInfo m = ModuleRegistry.List[i];
				if (m.Status == "Ok" && string.Equals(m.Name, d.Module, StringComparison.OrdinalIgnoreCase))
				{
					return m;
				}
			}
			return null;
		}

		private static void BuildPages()
		{
			_pages.Clear();
			PageInfo core = new PageInfo();
			core.Title = "Core";
			core.IsCore = true;
			_pages.Add(core);
			int n = Config.DeclCount;
			for (int i = 0; i < n; i++)
			{
				Config.Decl d = Config.DeclAt(i);
				if (d.Kind == 0)
				{
					continue;
				}
				ModuleInfo owner = OwnerOf(d);
				if (owner == null)
				{
					if (core.Items.Count < RowsPerPage)
					{
						core.Items.Add(d);
					}
					continue;
				}
				PageInfo target = null;
				for (int p = _pages.Count - 1; p > 0; p--)
				{
					if (object.ReferenceEquals(_pages[p].Mod, owner) && _pages[p].Items.Count < RowsPerPage)
					{
						target = _pages[p];
						break;
					}
				}
				if (target == null)
				{
					target = new PageInfo();
					target.Mod = owner;
					_pages.Add(target);
				}
				target.Items.Add(d);
			}
			for (int p = 1; p < _pages.Count; p++)
			{
				PageInfo pg = _pages[p];
				int total = 0;
				int index = 0;
				for (int q = 1; q < _pages.Count; q++)
				{
					if (object.ReferenceEquals(_pages[q].Mod, pg.Mod))
					{
						total++;
						if (q <= p)
						{
							index++;
						}
					}
				}
				pg.Title = total > 1 ? pg.Mod.Name + " (" + index + "/" + total + ")" : pg.Mod.Name;
			}
			if (_page < 0 || _page >= _pages.Count)
			{
				_page = 0;
			}
		}

		private static string InfoText(PageInfo p)
		{
			StringBuilder sb = new StringBuilder();
			if (p.IsCore)
			{
				sb.Append("NailOptimization  core api ").Append(Core.ApiLevel).Append("  hotkey ").Append(CoreSettings.Hotkey.ToString()).Append('\n');
				int total = ModuleRegistry.List.Count;
				int ok = ModuleRegistry.OkCount;
				sb.Append("modules: ").Append(total).Append(" (ok ").Append(ok).Append(", problems ").Append(total - ok).Append(")\n");
				int moduleMax = 3;
				for (int i = 0; i < total && i < moduleMax; i++)
				{
					ModuleInfo m = ModuleRegistry.List[i];
					sb.Append("  ").Append(m.Name).Append(" v").Append(m.Version).Append(" [").Append(m.Status).Append("]");
					if (m.Error.Length > 0)
					{
						sb.Append(' ').Append(m.Error);
					}
					sb.Append('\n');
				}
				if (total > moduleMax)
				{
					sb.Append("  (+").Append(total - moduleMax).Append(" more, see log)\n");
				}
				sb.Append("conflicts: warnings=").Append(ConflictDetector.Warnings).Append(" thirdParty=").Append(ConflictDetector.ThirdParty).Append("  reset-registered=").Append(ResetRegistry.Count).Append("  counters=").Append(Counters.Count).Append('\n');
				sb.Append("restart-only:");
				int shown = 0;
				int decls = Config.DeclCount;
				for (int d = 0; d < decls && shown < 5; d++)
				{
					if (Config.DeclAt(d).Kind != 0)
					{
						continue;
					}
					sb.Append("  ").Append(Config.DeclLine(d));
					shown++;
				}
			}
			else
			{
				ModuleInfo m = p.Mod;
				sb.Append(m.Name).Append(" v").Append(m.Version).Append(" [").Append(m.Status).Append("]\n");
				sb.Append("targets:");
				int tmax = m.Targets.Length < 4 ? m.Targets.Length : 4;
				for (int t = 0; t < tmax; t++)
				{
					sb.Append(' ').Append(m.Targets[t]);
				}
				if (m.Targets.Length > tmax)
				{
					sb.Append(" (+").Append(m.Targets.Length - tmax).Append(')');
				}
				if (m.Targets.Length == 0)
				{
					sb.Append(" none declared");
				}
				sb.Append('\n');
				bool save = CoreSettings.SaveFlag == null || CoreSettings.SaveFlag.On;
				sb.Append("changes apply immediately; ").Append(save ? "written to cfg.txt after 0.5 s" : "session only (Config.Save=false)").Append('\n');
			}
			return sb.ToString();
		}

		private static string FooterText()
		{
			float w = CoreSettings.WatchParam != null ? CoreSettings.WatchParam.Value : 0f;
			return "config: " + Config.SaveState + "    live reload " + (w > 0f ? w.ToString("F0", CultureInfo.InvariantCulture) + " s" : "off");
		}

		private static void ShowPage()
		{
			if (_root == null || _pages.Count == 0 || _pageTitle == null)
			{
				return;
			}
			if (_page < 0 || _page >= _pages.Count)
			{
				_page = 0;
			}
			PageInfo p = _pages[_page];
			if (_rowsRoot != null)
			{
				_rowsRoot.SetActive(false);
				UnityEngine.Object.Destroy(_rowsRoot);
			}
			_rows.Clear();
			RectTransform rrt = NewRt("Rows", _panelRt);
			rrt.anchorMin = new Vector2(0.5f, 0.5f);
			rrt.anchorMax = new Vector2(0.5f, 0.5f);
			rrt.pivot = new Vector2(0.5f, 0.5f);
			rrt.anchoredPosition = Vector2.zero;
			rrt.sizeDelta = Vector2.zero;
			_rowsRoot = rrt.gameObject;
			for (int i = 0; i < p.Items.Count; i++)
			{
				_rows.Add(new ParamRow(p.Items[i], rrt, 60f - 34f * i, !p.IsCore));
			}
			_pageTitle.text = (_page + 1) + "/" + _pages.Count + "  " + p.Title;
			_label.text = InfoText(p);
			RefreshRows();
			_seenVersion = Config.Version;
		}

		private static void RefreshRows()
		{
			for (int i = 0; i < _rows.Count; i++)
			{
				_rows[i].Refresh();
			}
			if (_footer != null)
			{
				_footer.text = FooterText();
			}
		}

		private static void AfterEdit()
		{
			RefreshRows();
			_seenVersion = Config.Version;
			Deselect();
		}

		private static void OnPrev()
		{
			if (_pages.Count > 1)
			{
				_page = (_page + _pages.Count - 1) % _pages.Count;
				ShowPage();
			}
			Deselect();
		}

		private static void OnNext()
		{
			if (_pages.Count > 1)
			{
				_page = (_page + 1) % _pages.Count;
				ShowPage();
			}
			Deselect();
		}

		private sealed class ParamRow
		{
			private readonly Config.Decl _d;
			private readonly string _name;
			private readonly Text _nameText;
			private readonly Text _val;
			private readonly Slider _slider;
			private readonly Text _togText;
			private readonly Image _togImg;

			internal ParamRow(Config.Decl d, Transform parent, float y, bool shortName)
			{
				_d = d;
				int dot = d.Key.IndexOf('.');
				_name = (shortName && dot > 0) ? d.Key.Substring(dot + 1) : d.Key;
				_nameText = MakeText(parent, "Name", new Vector2(-235f, y), new Vector2(260f, 28f), 14, TextAnchor.MiddleLeft);
				_val = MakeText(parent, "Value", new Vector2(150f, y), new Vector2(120f, 28f), 14, TextAnchor.MiddleCenter);
				if (d.Kind == 1)
				{
					Button tog = MakeButton(parent, "Toggle", new Vector2(0f, y), new Vector2(170f, 26f), string.Empty, ColOff, new UnityAction(OnToggle));
					_togText = tog.GetComponentInChildren<Text>();
					_togImg = tog.targetGraphic as Image;
				}
				else
				{
					_slider = MakeSlider(parent, new Vector2(0f, y), new Vector2(170f, 22f));
					if (d.Kind == 2)
					{
						_slider.wholeNumbers = true;
						_slider.minValue = (float)d.I.Min;
						_slider.maxValue = (float)d.I.Max;
					}
					else
					{
						_slider.wholeNumbers = false;
						_slider.minValue = d.N.Min;
						_slider.maxValue = d.N.Max;
					}
					Refresh();
					_slider.onValueChanged.AddListener(new UnityAction<float>(OnSlider));
				}
				MakeButton(parent, "Reset", new Vector2(285f, y), new Vector2(80f, 26f), "Reset", ColBtn, new UnityAction(OnReset));
				Refresh();
			}

			internal void Refresh()
			{
				_nameText.text = _d.Dirty ? _name + " *" : _name;
				if (_d.Kind == 1)
				{
					bool on = _d.F.On;
					_togText.text = on ? "ON" : "OFF";
					_togImg.color = on ? ColOn : ColOff;
					_val.text = "def " + (_d.F.Def ? "on" : "off");
				}
				else if (_d.Kind == 2)
				{
					_suppress = true;
					_slider.value = (float)_d.I.Value;
					_suppress = false;
					_val.text = _d.I.Value.ToString(CultureInfo.InvariantCulture) + " [" + _d.I.Min + ".." + _d.I.Max + "]";
				}
				else
				{
					_suppress = true;
					_slider.value = _d.N.Value;
					_suppress = false;
					_val.text = _d.N.Value.ToString("0.###", CultureInfo.InvariantCulture) + " [" + _d.N.Min.ToString("0.###", CultureInfo.InvariantCulture) + ".." + _d.N.Max.ToString("0.###", CultureInfo.InvariantCulture) + "]";
				}
			}

			private void OnSlider(float v)
			{
				if (_suppress)
				{
					return;
				}
				if (_d.Kind == 2)
				{
					int step = _d.I.Step;
					int q = _d.I.Min + (int)Math.Round((v - _d.I.Min) / (double)step) * step;
					Config.SetInt(_d.I, q);
				}
				else
				{
					float step = _d.N.Step;
					float q = _d.N.Min + (float)Math.Round((v - _d.N.Min) / step) * step;
					Config.SetFloat(_d.N, (float)Math.Round(q, 4));
				}
				RefreshRows();
				_seenVersion = Config.Version;
			}

			private void OnToggle()
			{
				Config.SetFlag(_d.F, !_d.F.On);
				AfterEdit();
			}

			private void OnReset()
			{
				if (_d.Kind == 1)
				{
					Config.SetFlag(_d.F, _d.F.Def);
				}
				else if (_d.Kind == 2)
				{
					Config.SetInt(_d.I, _d.I.Def);
				}
				else
				{
					Config.SetFloat(_d.N, _d.N.Def);
				}
				AfterEdit();
			}
		}

		private static void OnBackdropClick()
		{
		}

		private static void OnClickClose()
		{
			ULog.Info("gui", "click Close " + PointerInfo());
			Toggle();
		}
	}

	// ---------- title screen button (Alter: AlterTitleController._gadgetRoot, base: NewTitleScript._gadgets) ----------
	internal static class TitleWatch
	{
		private const int BuildDelayFrames = 2;
		private const float BoxWidth = 150f;
		private const float BoxHeight = 200f;
		private const float IconSize = 110f;
		private const float Eps = 0.001f;
		private static int _curId;
		private static int _kind;
		private static int _stage;
		private static int _shownFrame;
		private static RectTransform _anchor;
		private static UIController _ui;
		private static GameObject _button;
		private static Sprite _sprite;
		private static bool _spriteTried;
		private static bool _spriteWasLoaded;
		private const int OverlapEvery = 30;
		private const int MaxMoves = 3;
		private const float MoveGapShare = 0.05f;
		private static int _moves;
		private static readonly List<int> _vanilla = new List<int>();
		private static readonly List<int> _moved = new List<int>();
		private static readonly List<Transform> _movedT = new List<Transform>();
		private static readonly List<float> _movedX = new List<float>();
		private const float LabelScale = 1.75f;
		private static readonly Vector3[] _wc = new Vector3[4];
		// Our buttons on this screen, right to left: the base button first, then one per UiActions entry.
		private static readonly List<RectTransform> _own = new List<RectTransform>();
		private static readonly Dictionary<string, Sprite> _actionSprites = new Dictionary<string, Sprite>();
		// Api 6 badges: button and badge text per UiActions index on this screen (null = no button).
		private static readonly List<RectTransform> _actRt = new List<RectTransform>();
		private static readonly List<BadgeView> _badges = new List<BadgeView>();
		private static int _badgeVer = -1;
		private static int _badgeErrors;
		private static int _soundLogged;

		internal static void Poll()
		{
			if (!CoreSettings.TitleButton)
			{
				return;
			}
			AlterTitleController a = AlterTitleController.Controller;
			NewTitleScript n = NewTitleScript.instance;
			bool hasA = a != null;
			bool hasN = n != null;
			if (!hasA && !hasN)
			{
				if (_curId != 0)
				{
					Reset();
				}
				return;
			}
			int kind = hasA ? 1 : 2;
			int id = hasA ? a.GetInstanceID() : n.GetInstanceID();
			if (id != _curId || kind != _kind)
			{
				Begin(kind, id, a, n);
			}
			if (_stage >= 2)
			{
				if (_stage == 2 && UiActions.BadgeVersion != _badgeVer)
				{
					SyncBadges();
				}
				if (_stage == 2 && _button != null && _moves < MaxMoves && Time.frameCount % OverlapEvery == 0)
				{
					try
					{
						CheckOverlap();
					}
					catch (Exception e)
					{
						_moves = MaxMoves;
						ULog.Error("title", "overlap check failed, stopped for this screen: " + e);
					}
				}
				return;
			}
			try
			{
				Step();
			}
			catch (Exception e)
			{
				_stage = 99;
				ULog.Error("title", "button step failed, stopped for this screen: " + e);
			}
		}

		private static void Reset()
		{
			_curId = 0;
			_kind = 0;
			_stage = 0;
			_anchor = null;
			_ui = null;
			_button = null;
			_moves = 0;
			_soundLogged = 0;
			_vanilla.Clear();
			_moved.Clear();
			_movedT.Clear();
			_movedX.Clear();
			_own.Clear();
			_actRt.Clear();
			_badges.Clear();
			_badgeVer = -1;
		}

		private static void Begin(int kind, int id, AlterTitleController a, NewTitleScript n)
		{
			Reset();
			_curId = id;
			_kind = kind;
			try
			{
				if (kind == 1)
				{
					_anchor = a._gadgetRoot;
					if (_anchor != null)
					{
						_ui = _anchor.GetComponent<UIController>();
					}
				}
				else
				{
					_ui = n._gadgets;
					if (_ui != null)
					{
						_anchor = _ui.GetComponent<RectTransform>();
					}
				}
			}
			catch (Exception e)
			{
				ULog.Error("title", "anchor resolve failed: " + e);
			}
			if (_anchor == null)
			{
				_stage = 99;
				ULog.Warn("title", "no gadget anchor on " + (kind == 1 ? "alter" : "base") + " screen, button not created");
				return;
			}
			ULog.Info("title", "screen " + (kind == 1 ? "alter" : "base") + " id=" + id + " anchor=" + _anchor.name);
		}

		private static bool ReadShown()
		{
			if (_anchor == null || !_anchor.gameObject.activeInHierarchy)
			{
				return false;
			}
			if (_ui == null)
			{
				return true;
			}
			return _ui.isShow;
		}

		private static void Step()
		{
			if (_stage == 0)
			{
				if (ReadShown())
				{
					_shownFrame = Time.frameCount;
					_stage = 1;
				}
				return;
			}
			if (_stage == 1 && Time.frameCount >= _shownFrame + BuildDelayFrames)
			{
				if (!ReadShown())
				{
					_stage = 0;
					return;
				}
				Build();
				_stage = 2;
			}
		}

		private static Sprite LoadSprite()
		{
			if (_sprite != null)
			{
				return _sprite;
			}
			if (_spriteTried && !_spriteWasLoaded)
			{
				return null;
			}
			_spriteTried = true;
			_spriteWasLoaded = false;
			Sprite sp = ReadSprite(CoreSettings.TitleIcon);
			if (sp != null)
			{
				_sprite = sp;
				_spriteWasLoaded = true;
			}
			return sp;
		}

		// Action icons: one load per path per session; a failed load is cached as null.
		internal static Sprite LoadActionSprite(string rel)
		{
			if (string.IsNullOrEmpty(rel))
			{
				return null;
			}
			Sprite sp;
			if (_actionSprites.TryGetValue(rel, out sp))
			{
				return sp;
			}
			sp = ReadSprite(rel);
			_actionSprites[rel] = sp;
			return sp;
		}

		private static Sprite ReadSprite(string rel)
		{
			try
			{
				string path = Path.Combine(PLog.Dir, rel.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(path))
				{
					ULog.Warn("title", "icon file not found: " + path + " (placeholder used)");
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				if (!tex.LoadImage(bytes))
				{
					UnityEngine.Object.Destroy(tex);
					ULog.Warn("title", "icon decode failed: " + path + " (placeholder used)");
					return null;
				}
				tex.wrapMode = TextureWrapMode.Clamp;
				tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
				Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, (float)tex.width, (float)tex.height), new Vector2(0.5f, 0.5f), 100f);
				sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
				ULog.Info("title", "icon loaded " + tex.width + "x" + tex.height + " from " + path);
				return sp;
			}
			catch (Exception e)
			{
				ULog.Error("title", "icon load failed: " + e);
				return null;
			}
		}

		// Base title: the gadget icons are cream, not white. Take the colour of the first neighbour label (e.g. "Story"); white if none.
		private static Color NeighbourTint(Transform own)
		{
			Text[] ts = _anchor.GetComponentsInChildren<Text>(false);
			for (int i = 0; i < ts.Length; i++)
			{
				if (ts[i].transform.IsChildOf(own))
				{
					continue;
				}
				Color c = ts[i].color;
				ULog.Info("title", "icon tint " + c.r.ToString("F2", CultureInfo.InvariantCulture) + "," + c.g.ToString("F2", CultureInfo.InvariantCulture) + "," + c.b.ToString("F2", CultureInfo.InvariantCulture) + " from " + ts[i].name);
				c.a = 1f;
				return c;
			}
			ULog.Info("title", "icon tint white (no neighbour label)");
			return Color.white;
		}

		private static void Build()
		{
			if (_button != null || _anchor == null)
			{
				return;
			}
			Vector3[] corners = new Vector3[4];
			float minX = float.MaxValue;
			float midY = _anchor.position.y;
			RectTransform[] rts = _anchor.GetComponentsInChildren<RectTransform>(false);
			for (int i = 0; i < rts.Length; i++)
			{
				if (rts[i] == _anchor)
				{
					continue;
				}
				rts[i].GetWorldCorners(corners);
				float l = Mathf.Min(Mathf.Min(corners[0].x, corners[1].x), Mathf.Min(corners[2].x, corners[3].x));
				if (l < minX - Eps)
				{
					minX = l;
					midY = (corners[0].y + corners[2].y) * 0.5f;
				}
			}
			Renderer[] rs = _anchor.GetComponentsInChildren<Renderer>(false);
			for (int r = 0; r < rs.Length; r++)
			{
				float rl = rs[r].bounds.min.x;
				if (rl < minX - Eps)
				{
					minX = rl;
				}
			}
			if (minX == float.MaxValue)
			{
				minX = _anchor.position.x;
			}
			Vector3 leftLocal = _anchor.InverseTransformPoint(new Vector3(minX, midY, _anchor.position.z));
			RememberVanilla();
			if (_kind == 2 && BuildClone(leftLocal))
			{
				BuildActions(leftLocal.y);
				FitOnScreen();
				return;
			}
			try
			{
				Sprite sp = LoadSprite();
				string baseLang;
				RectTransform rt = MakePlain(leftLocal, sp, "NO", TitleLabel(out baseLang), "NailOptimizationButton", new UnityAction(OnClick));
				// The label is wider than the box: keep it off the game's gadgets.
				rt.localPosition = rt.localPosition - new Vector3(LabelOverhang(rt), 0f, 0f);
				GuiPanel.SetToggleArea(rt, CanvasCam());
				_button = rt.gameObject;
				_own.Add(rt);
				ULog.Info("title", "button created kind=" + (_kind == 1 ? "alter" : "base") + " id=" + _curId + " localPos=" + rt.localPosition.ToString("F1") + " world=" + rt.position.ToString("F2") + " icon=" + (sp != null ? "png" : "placeholder"));
			}
			catch (Exception e)
			{
				_stage = 99;
				ULog.Error("title", "button build failed: " + e);
				return;
			}
			BuildActions(leftLocal.y);
			FitOnScreen();
		}

		// Group placed right of the gadgets: our leftmost icon on one line with the last gadget icon, with the same icon
		// spacing as between the last two gadgets. No change when no two gadget icons give a spacing.
		private static void AlignToGadgets(ref float dx, ref float dy)
		{
			RectTransform last = null;
			RectTransform prev = null;
			float lastR = float.MinValue;
			float prevR = float.MinValue;
			for (int i = 0; i < _anchor.childCount; i++)
			{
				RectTransform c = _anchor.GetChild(i) as RectTransform;
				if (c == null || !c.gameObject.activeInHierarchy || IsOwn(c))
				{
					continue;
				}
				Image ci = FirstSpriteImage(c);
				if (ci == null || !ci.gameObject.activeInHierarchy)
				{
					continue;
				}
				float r = WorldMaxX(ci.rectTransform);
				if (r > lastR)
				{
					prev = last;
					prevR = lastR;
					last = ci.rectTransform;
					lastR = r;
				}
				else if (r > prevR)
				{
					prev = ci.rectTransform;
					prevR = r;
				}
			}
			RectTransform first = _own[_own.Count - 1];
			Image oi = first != null ? FirstSpriteImage(first) : null;
			if (last == null || prev == null || oi == null)
			{
				return;
			}
			float step = WorldMinX(last) - prevR;
			if (step <= 0f)
			{
				return;
			}
			dx = lastR + step - WorldMinX(oi.rectTransform);
			dy = WorldMidY(last) - WorldMidY(oi.rectTransform);
		}

		private static float WorldMinX(RectTransform rt)
		{
			rt.GetWorldCorners(_wc);
			return Mathf.Min(_wc[0].x, _wc[2].x);
		}

		private static float WorldMaxX(RectTransform rt)
		{
			rt.GetWorldCorners(_wc);
			return Mathf.Max(_wc[0].x, _wc[2].x);
		}

		private static float WorldMidY(RectTransform rt)
		{
			rt.GetWorldCorners(_wc);
			return (_wc[0].y + _wc[2].y) * 0.5f;
		}

		// A custom title (BlueArchive) can move the gadgets into a screen corner: our group, built left of them, is then
		// off-screen. Move it right of the gadgets, or at least inside the screen. A group that fits is left unchanged.
		private static void FitOnScreen()
		{
			if (_own.Count == 0 || _own[0] == null)
			{
				return;
			}
			Camera cam = CanvasCam();
			Vector3 lo;
			Vector3 hi;
			if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(_anchor, Vector2.zero, cam, out lo)
				|| !RectTransformUtility.ScreenPointToWorldPointInRectangle(_anchor, new Vector2((float)Screen.width, (float)Screen.height), cam, out hi))
			{
				return;
			}
			float sxMin = Mathf.Min(lo.x, hi.x);
			float sxMax = Mathf.Max(lo.x, hi.x);
			float syMin = Mathf.Min(lo.y, hi.y);
			float syMax = Mathf.Max(lo.y, hi.y);
			float gxMin = float.MaxValue;
			float gxMax = float.MinValue;
			float gyMin = float.MaxValue;
			float gyMax = float.MinValue;
			for (int i = 0; i < _own.Count; i++)
			{
				if (_own[i] == null)
				{
					continue;
				}
				RectTransform[] parts = _own[i].GetComponentsInChildren<RectTransform>(false);
				for (int p = 0; p < parts.Length; p++)
				{
					parts[p].GetWorldCorners(_wc);
					for (int c = 0; c < 4; c++)
					{
						gxMin = Mathf.Min(gxMin, _wc[c].x);
						gxMax = Mathf.Max(gxMax, _wc[c].x);
						gyMin = Mathf.Min(gyMin, _wc[c].y);
						gyMax = Mathf.Max(gyMax, _wc[c].y);
					}
				}
			}
			if (gxMin == float.MaxValue || (gxMin >= sxMin && gxMax <= sxMax && gyMin >= syMin && gyMax <= syMax))
			{
				return;
			}
			_own[0].GetWorldCorners(_wc);
			float gap = Mathf.Abs(_wc[2].x - _wc[0].x) * MoveGapShare;
			float right = float.MinValue;
			RectTransform[] rts = _anchor.GetComponentsInChildren<RectTransform>(false);
			for (int i = 0; i < rts.Length; i++)
			{
				if (rts[i] == _anchor || IsOwn(rts[i]))
				{
					continue;
				}
				rts[i].GetWorldCorners(_wc);
				right = Mathf.Max(right, Mathf.Max(_wc[0].x, _wc[2].x));
			}
			float dx = 0f;
			float dy = 0f;
			string where = "inside the screen";
			if (right != float.MinValue && right + gap >= sxMin && right + gap + (gxMax - gxMin) <= sxMax)
			{
				dx = right + gap - gxMin;
				where = "right of the gadgets";
				AlignToGadgets(ref dx, ref dy);
			}
			else if (gxMin < sxMin)
			{
				dx = sxMin + gap - gxMin;
			}
			else if (gxMax > sxMax)
			{
				dx = sxMax - gap - gxMax;
			}
			if (gyMin + dy < syMin)
			{
				dy = syMin + gap - gyMin;
			}
			else if (gyMax + dy > syMax)
			{
				dy = syMax - gap - gyMax;
			}
			Vector3 shift = new Vector3(dx, dy, 0f);
			for (int i = 0; i < _own.Count; i++)
			{
				if (_own[i] != null)
				{
					_own[i].position = _own[i].position + shift;
				}
			}
			ULog.Info("title", "title buttons off-screen (x " + gxMin.ToString("F2", CultureInfo.InvariantCulture) + ".." + gxMax.ToString("F2", CultureInfo.InvariantCulture)
				+ ", screen " + sxMin.ToString("F2", CultureInfo.InvariantCulture) + ".." + sxMax.ToString("F2", CultureInfo.InvariantCulture) + "), moved " + where
				+ " by " + shift.ToString("F2") + ", first now localPos=" + _own[0].localPosition.ToString("F1"));
		}

		// Plain icon button with its right edge at at.x. Throws on failure (the half-built object is destroyed first).
		private static RectTransform MakePlain(Vector3 at, Sprite sp, string caption, string label, string name, UnityAction click)
		{
			GameObject go = null;
			try
			{
				go = new GameObject(name);
				Image hit = go.AddComponent<Image>();
				hit.color = new Color(1f, 1f, 1f, 0f);
				RectTransform rt = go.GetComponent<RectTransform>();
				rt.SetParent(_anchor, false);
				rt.anchorMin = new Vector2(0.5f, 0.5f);
				rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.sizeDelta = new Vector2(BoxWidth, BoxHeight);
				rt.localScale = Vector3.one;
				rt.localPosition = new Vector3(at.x - BoxWidth * 0.5f, at.y, 0f);

				GameObject iconGo = new GameObject("Icon");
				Image icon = iconGo.AddComponent<Image>();
				RectTransform irt = iconGo.GetComponent<RectTransform>();
				irt.SetParent(rt, false);
				irt.anchorMin = new Vector2(0.5f, 0.5f);
				irt.anchorMax = new Vector2(0.5f, 0.5f);
				irt.pivot = new Vector2(0.5f, 0.5f);
				irt.anchoredPosition = Vector2.zero;
				irt.sizeDelta = new Vector2(IconSize, IconSize);
				icon.raycastTarget = false;
				if (sp != null)
				{
					icon.sprite = sp;
					icon.preserveAspect = true;
					icon.color = _kind == 1 ? Color.white : NeighbourTint(rt);
				}
				else
				{
					icon.color = new Color(0.25f, 0.28f, 0.4f, 1f);
					GameObject textGo = new GameObject("Caption");
					Text t = textGo.AddComponent<Text>();
					t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
					t.fontSize = 28;
					t.color = Color.white;
					t.alignment = TextAnchor.MiddleCenter;
					t.raycastTarget = false;
					t.text = caption;
					RectTransform trt = textGo.GetComponent<RectTransform>();
					trt.SetParent(irt, false);
					trt.anchorMin = Vector2.zero;
					trt.anchorMax = Vector2.one;
					trt.offsetMin = Vector2.zero;
					trt.offsetMax = Vector2.zero;
				}
				Button b = go.AddComponent<Button>();
				b.targetGraphic = icon;
				b.transition = Selectable.Transition.ColorTint;
				ColorBlock cb = b.colors;
				cb.normalColor = new Color(0.75f, 0.75f, 0.75f, 1f);
				cb.highlightedColor = Color.white;
				cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
				cb.colorMultiplier = 1f;
				cb.fadeDuration = 0.1f;
				b.colors = cb;
				Navigation nav = new Navigation();
				nav.mode = Navigation.Mode.None;
				b.navigation = nav;
				if (_kind == 1)
				{
					b.onClick.AddListener(new UnityAction(PlayClickSound));
					EventTrigger et = go.AddComponent<EventTrigger>();
					EventTrigger.Entry enter = new EventTrigger.Entry();
					enter.eventID = EventTriggerType.PointerEnter;
					enter.callback.AddListener(new UnityAction<BaseEventData>(PlayHoverSound));
					et.triggers.Add(enter);
				}
				b.onClick.AddListener(click);
				AddPlainLabel(rt, label);
				return rt;
			}
			catch (Exception)
			{
				if (go != null)
				{
					UnityEngine.Object.Destroy(go);
				}
				throw;
			}
		}

		// Inside the box, under the icon: part of the hit area, the Alter badge stays below the box.
		private static void AddPlainLabel(RectTransform rt, string label)
		{
			GameObject go = new GameObject("Label");
			Text t = go.AddComponent<Text>();
			t.font = BadgeFont();
			t.fontSize = 28;
			t.color = new Color(0.85f, 0.85f, 0.85f, 1f);
			t.alignment = TextAnchor.UpperCenter;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Overflow;
			t.raycastTarget = false;
			t.text = label;
			RectTransform trt = go.GetComponent<RectTransform>();
			trt.SetParent(rt, false);
			trt.anchorMin = new Vector2(0.5f, 0.5f);
			trt.anchorMax = new Vector2(0.5f, 0.5f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -IconSize * 0.5f - 4f);
			trt.sizeDelta = new Vector2(BoxWidth, 36f);
		}

		// The Alter title's own list: 0 hover, 1 click.
		private static void PlayHoverSound(BaseEventData e)
		{
			PlayTitleSound(0);
		}

		private static void PlayClickSound()
		{
			PlayTitleSound(1);
		}

		private static void PlayTitleSound(int index)
		{
			try
			{
				AlterTitleController c = AlterTitleController.Controller;
				AudioClipPlayer p = c != null ? c.GetComponent<AudioClipPlayer>() : null;
				if (p != null)
				{
					p.OnPlayInList(index);
				}
				if ((_soundLogged & (1 << index)) == 0)
				{
					_soundLogged |= 1 << index;
					ULog.Info("title", "sound " + index + " player=" + (p != null));
				}
			}
			catch (Exception e)
			{
				ULog.Warn("title", "title sound failed: " + e.GetType().Name);
			}
		}

		private static Camera CanvasCam()
		{
			Canvas cv = _anchor.GetComponentInParent<Canvas>();
			if (cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay)
			{
				return cv.worldCamera;
			}
			return null;
		}

		// One button per UiActions entry, each left of the previous own button: a Story clone on the base title, else a plain button.
		private static void BuildActions(float y)
		{
			int n = UiActions.Count;
			if (n == 0 || _own.Count == 0)
			{
				return;
			}
			Button src = _kind == 2 ? FindCloneSource() : null;
			string lang;
			TitleLabel(out lang);
			for (int i = 0; i < n; i++)
			{
				_actRt.Add(null);
				try
				{
					RectTransform prev = _own[_own.Count - 1];
					if (prev == null)
					{
						return;
					}
					float rightX = LeftEdgeLocal(prev) - prev.rect.width * MoveGapShare;
					Sprite sp = LoadActionSprite(UiActions.GetIcon(i));
					string label = UiActions.GetLabel(i, lang);
					string name = "NailOptimizationAction_" + UiActions.GetId(i);
					UnityAction click = new UnityAction(new ActionClick(i).Run);
					RectTransform rt = null;
					if (src != null && sp != null)
					{
						rt = MakeClone(src, rightX, sp, label, lang, name, click);
					}
					if (rt == null)
					{
						string caption = label.Length > 3 ? label.Substring(0, 3).ToUpperInvariant() : label.ToUpperInvariant();
						rt = MakePlain(new Vector3(rightX, y, 0f), sp, caption, label, name, click);
					}
					// Registered as ours first: CheckOverlap must never move it, even if a later step throws.
					_own.Add(rt);
					_actRt[i] = rt;
					// Labels are drawn past the box edges: keep the two labels apart.
					float extra = LabelOverhang(prev) + LabelOverhang(rt);
					if (extra > 0f)
					{
						rt.localPosition = rt.localPosition - new Vector3(extra, 0f, 0f);
					}
					UiActions.SetArea(rt, CanvasCam());
					ULog.Info("title", "action button " + UiActions.GetId(i) + " created label=" + label + " clone=" + (rt.GetComponent<Animator>() != null) + " overhang=" + extra.ToString("F1", CultureInfo.InvariantCulture) + " localPos=" + rt.localPosition.ToString("F1"));
				}
				catch (Exception e)
				{
					ULog.Error("title", "action button " + UiActions.GetId(i) + " failed: " + e);
				}
			}
		}

		// Api 8: BadgeView "N<sign> M<sign>" (warn yellow, error red) above the action button on the base title, below it on the Alter title; hidden at 0/0.
		private static void SyncBadges()
		{
			_badgeVer = UiActions.BadgeVersion;
			try
			{
				for (int i = 0; i < _actRt.Count; i++)
				{
					RectTransform rt = _actRt[i];
					if (rt == null)
					{
						continue;
					}
					while (_badges.Count <= i)
					{
						_badges.Add(null);
					}
					int warn;
					int err;
					UiActions.GetBadges(i, out warn, out err);
					BadgeView b = _badges[i];
					if (b == null)
					{
						if (warn <= 0 && err <= 0)
						{
							continue;
						}
						b = MakeBadge(rt, _kind == 1);
						_badges[i] = b;
					}
					b.Set(warn, err);
				}
			}
			catch (Exception e)
			{
				if (_badgeErrors < 3)
				{
					_badgeErrors++;
					ULog.Error("title", "badge update failed: " + e);
				}
			}
		}

		private static BadgeView MakeBadge(RectTransform rt, bool below)
		{
			GameObject go = new GameObject("NailBadge");
			RectTransform trt = go.AddComponent<RectTransform>();
			// Base title: on top of the icon (the cloned button rect is taller than its icon). Alter title: below the button as before.
			Image icon = below ? null : FirstSpriteImage(rt);
			trt.SetParent(icon != null ? icon.rectTransform : rt, false);
			float y = below ? 0f : 1f;
			trt.anchorMin = new Vector2(0.5f, y);
			trt.anchorMax = new Vector2(0.5f, y);
			trt.pivot = new Vector2(0.5f, below ? 1f : 0f);
			trt.anchoredPosition = new Vector2(0f, below ? -4f : (icon != null ? 2f : 4f));
			trt.sizeDelta = new Vector2(220f, 40f);
			Font f = BadgeFont();
			BadgeView b = UiActions.CreateBadge(trt, f, 30, false);
			ULog.Info("title", "badge created on " + rt.name + (below ? " below" : (icon != null ? " above icon " + icon.name : " above")) + " font=" + (f != null ? f.name : "null"));
			return b;
		}

		private static Font BadgeFont()
		{
			try
			{
				GlobalGameManager.LanguageFont lf = GlobalGameManager.currentLanguageFont;
				if (!object.ReferenceEquals(lf, null))
				{
					GlobalGameManager.FontData fd = lf.GetFont(FontType.TITLE);
					if (!object.ReferenceEquals(fd, null) && fd.font != null)
					{
						return fd.font;
					}
				}
			}
			catch (Exception e)
			{
				ULog.Warn("title", "badge TITLE font lookup failed: " + e.GetType().Name);
			}
			return Resources.GetBuiltinResource<Font>("Arial.ttf");
		}

		// Left edge of an own button in the anchor's local space.
		private static float LeftEdgeLocal(RectTransform rt)
		{
			rt.GetWorldCorners(_wc);
			float minX = Mathf.Min(_wc[0].x, _wc[2].x);
			return _anchor.InverseTransformPoint(new Vector3(minX, _wc[0].y, _wc[0].z)).x;
		}

		// Half of the label width that sticks out of the button box (label local units, button scale assumed 1).
		private static float LabelOverhang(RectTransform rt)
		{
			Transform own = rt.Find("Label");
			Text t = own != null ? own.GetComponent<Text>() : rt.GetComponentInChildren<Text>(true);
			if (t == null)
			{
				return 0f;
			}
			return Mathf.Max(0f, (t.preferredWidth - rt.rect.width) * 0.5f);
		}

		private sealed class ActionClick
		{
			private readonly int _index;

			internal ActionClick(int index)
			{
				_index = index;
			}

			internal void Run()
			{
				if (EventSystem.current != null)
				{
					EventSystem.current.SetSelectedGameObject(null);
				}
				UiActions.Invoke(_index);
			}
		}

		// Selectables already on the gadget panel when we build: the vanilla buttons we were placed next to. Not treated as overlap.
		private static void RememberVanilla()
		{
			_vanilla.Clear();
			for (int i = 0; i < _anchor.childCount; i++)
			{
				_vanilla.Add(_anchor.GetChild(i).gameObject.GetInstanceID());
			}
		}

		// Base title: copy the game's own gadget button (Story) so hover, colours and label style match. False = use the plain button.
		private static bool BuildClone(Vector3 leftLocal)
		{
			Button src = FindCloneSource();
			Sprite sp = LoadSprite();
			if (src == null || sp == null)
			{
				ULog.Info("title", "clone skipped (" + (src == null ? "no gadget button with icon and label" : "no icon") + "), plain button used");
				return false;
			}
			string lang;
			string label = TitleLabel(out lang);
			RectTransform rt = MakeClone(src, leftLocal.x, sp, label, lang, "NailOptimizationButton", new UnityAction(OnClick));
			if (rt == null)
			{
				return false;
			}
			GuiPanel.SetToggleArea(rt, CanvasCam());
			_button = rt.gameObject;
			_own.Add(rt);
			return true;
		}

		// First active gadget button with a label and an icon (Story); never one of ours.
		private static Button FindCloneSource()
		{
			for (int i = 0; i < _anchor.childCount; i++)
			{
				Transform c = _anchor.GetChild(i);
				if (!c.gameObject.activeInHierarchy || IsOwn(c))
				{
					continue;
				}
				Button cb = c.GetComponent<Button>();
				if (cb != null && c.GetComponentInChildren<Text>(true) != null && FirstSpriteImage(c) != null)
				{
					return cb;
				}
			}
			return null;
		}

		private static bool IsOwn(Transform t)
		{
			for (int i = 0; i < _own.Count; i++)
			{
				if (_own[i] != null && t.IsChildOf(_own[i]))
				{
					return true;
				}
			}
			return false;
		}

		// Clone of src with our icon, label and click, right edge at rightX (anchor local). Null on failure (logged).
		private static RectTransform MakeClone(Button src, float rightX, Sprite sp, string label, string lang, string name, UnityAction click)
		{
			GameObject go = null;
			try
			{
				go = UnityEngine.Object.Instantiate<GameObject>(src.gameObject, _anchor, false);
				go.name = name;
				Transform root = go.transform;
				int kept = 0;
				int dropped = 0;
				StringBuilder calls = new StringBuilder();
				EventTrigger et = go.GetComponent<EventTrigger>();
				if (et != null && et.triggers != null)
				{
					for (int i = et.triggers.Count - 1; i >= 0; i--)
					{
						EventTrigger.Entry en = et.triggers[i];
						DescribeCalls(en, calls);
						// PointerEnter is kept even when it calls outside: the source button's hover sound [Inferred], hover has no game action.
						if (CallsOnlyInside(en.callback, root) || en.eventID == EventTriggerType.PointerEnter)
						{
							kept++;
						}
						else
						{
							et.triggers.RemoveAt(i);
							dropped++;
						}
					}
				}
				ULog.Info("title", "clone triggers of " + src.name + ": " + (calls.Length > 0 ? calls.ToString() : "none"));
				Button b = go.GetComponent<Button>();
				b.onClick = new Button.ButtonClickedEvent();
				b.onClick.AddListener(click);
				Navigation nav = new Navigation();
				nav.mode = Navigation.Mode.None;
				b.navigation = nav;
				Image icon = FirstSpriteImage(root);
				icon.sprite = sp;
				icon.preserveAspect = true;
				StringBuilder removed = new StringBuilder();
				Text t = go.GetComponentInChildren<Text>(true);
				// Reverse order and DestroyImmediate: LocalizeTextLoadScript requires FontLoadScript, so the dependent goes first.
				Component[] cs = t.GetComponents<Component>();
				for (int i = cs.Length - 1; i >= 0; i--)
				{
					Component c = cs[i];
					if (c is Transform || c is CanvasRenderer || c is Text || c is Shadow)
					{
						continue;
					}
					Behaviour bh = c as Behaviour;
					if (bh != null)
					{
						bh.enabled = false;
					}
					removed.Append(c.GetType().Name).Append(';');
					UnityEngine.Object.DestroyImmediate(c);
				}
				t.text = label;
				// Label size: the game's nominal size times LabelScale (1.75 chosen in game by the user), bold, drawn past the box edges.
				int max = t.fontSize;
				t.resizeTextForBestFit = false;
				t.fontSize = Mathf.Max(1, Mathf.RoundToInt(max * LabelScale));
				t.fontStyle = FontStyle.Bold;
				t.horizontalOverflow = HorizontalWrapMode.Overflow;
				t.verticalOverflow = VerticalWrapMode.Overflow;
				RectTransform rt = go.GetComponent<RectTransform>();
				rt.localPosition = new Vector3(rightX - (1f - rt.pivot.x) * rt.rect.width, rt.localPosition.y, rt.localPosition.z);
				ULog.Info("title", "clone ok from " + src.name + " animator=" + (go.GetComponent<Animator>() != null) + " triggers kept=" + kept + " dropped=" + dropped + " removed=" + (removed.Length > 0 ? removed.ToString() : "none") + " label=" + label + " font=" + max + "->" + t.fontSize + " labelW=" + t.rectTransform.rect.width.ToString("F0", CultureInfo.InvariantCulture) + " lang=" + lang + " localPos=" + rt.localPosition.ToString("F1") + " world=" + rt.position.ToString("F2"));
				return rt;
			}
			catch (Exception e)
			{
				if (go != null)
				{
					UnityEngine.Object.Destroy(go);
				}
				ULog.Error("title", "clone failed, plain button used: " + e);
				return null;
			}
		}

		private static Image FirstSpriteImage(Transform t)
		{
			Image[] imgs = t.GetComponentsInChildren<Image>(true);
			for (int i = 0; i < imgs.Length; i++)
			{
				if (imgs[i].sprite != null)
				{
					return imgs[i];
				}
			}
			return null;
		}

		// "eventID:TargetType.Method;" per persistent listener, for the clone log line.
		private static void DescribeCalls(EventTrigger.Entry en, StringBuilder sb)
		{
			if (en == null || en.callback == null)
			{
				return;
			}
			int n = en.callback.GetPersistentEventCount();
			for (int i = 0; i < n; i++)
			{
				UnityEngine.Object o = en.callback.GetPersistentTarget(i);
				sb.Append(en.eventID.ToString()).Append(':').Append(o != null ? o.GetType().Name : "null").Append('.').Append(en.callback.GetPersistentMethodName(i)).Append(';');
			}
		}

		// True when every persistent listener targets an object inside the clone (its own animator, image...). Story's handlers point outside.
		private static bool CallsOnlyInside(UnityEventBase ev, Transform root)
		{
			if (ev == null)
			{
				return true;
			}
			int n = ev.GetPersistentEventCount();
			for (int i = 0; i < n; i++)
			{
				UnityEngine.Object o = ev.GetPersistentTarget(i);
				Component c = o as Component;
				GameObject g = o as GameObject;
				Transform tr = c != null ? c.transform : (g != null ? g.transform : null);
				if (tr == null || !tr.IsChildOf(root))
				{
					return false;
				}
			}
			return true;
		}

		// en by default; ru only when the game reports it. Any error or other language -> en.
		private static string TitleLabel(out string lang)
		{
			lang = "en";
			try
			{
				GlobalGameManager.LanguageFont lf = GlobalGameManager.currentLanguageFont;
				if (!object.ReferenceEquals(lf, null) && !string.IsNullOrEmpty(lf.language))
				{
					lang = lf.language.Trim().ToLowerInvariant();
				}
			}
			catch (Exception)
			{
				lang = "en";
			}
			if (string.Equals(lang, "ru", StringComparison.Ordinal))
			{
				return "\u041E\u043F\u0442\u0438\u043C\u0438\u0437\u0430\u0446\u0438\u044F";
			}
			return "Optimization";
		}

		// Another mod's button on the same title canvas (any Selectable outside the vanilla gadgets) covering ours: ours stays next to
		// the game's gadgets, theirs is moved left just past ours, once. Other canvases (our own panels) are ignored.
		private static void CheckOverlap()
		{
			RectTransform own = _button.transform as RectTransform;
			if (own == null || !_button.activeInHierarchy)
			{
				return;
			}
			Canvas myCanvas = own.GetComponentInParent<Canvas>();
			if (myCanvas == null)
			{
				return;
			}
			Canvas myRoot = myCanvas.rootCanvas;
			// AbnoSeal's intro animation sets its button's position every frame and removes itself at the end.
			if (AbnoSealAnimating())
			{
				return;
			}
			// A mod that puts its button back: restore our x once more (y is left alone, hover moves it).
			for (int k = 0; k < _movedT.Count; k++)
			{
				Transform mt = _movedT[k];
				if (mt == null || Mathf.Abs(mt.position.x - _movedX[k]) < 0.01f)
				{
					continue;
				}
				ULog.Info("title", "other button " + mt.name + " was put back to x=" + mt.position.x.ToString("F2", CultureInfo.InvariantCulture) + ", moving again to x=" + _movedX[k].ToString("F2", CultureInfo.InvariantCulture));
				Vector3 mp = mt.position;
				mt.position = new Vector3(_movedX[k], mp.y, mp.z);
				_moves++;
				return;
			}
			// "Ours" = the base button plus the action buttons left of it.
			Rect mine = ScreenRect(own);
			own.GetWorldCorners(_wc);
			float ourLeft = Mathf.Min(_wc[0].x, _wc[2].x);
			float ourWidth = Mathf.Abs(_wc[2].x - _wc[0].x);
			for (int j = 0; j < _own.Count; j++)
			{
				RectTransform o = _own[j];
				if (o == null || o == own || !o.gameObject.activeInHierarchy)
				{
					continue;
				}
				Rect r = ScreenRect(o);
				mine = Rect.MinMaxRect(Mathf.Min(mine.xMin, r.xMin), Mathf.Min(mine.yMin, r.yMin), Mathf.Max(mine.xMax, r.xMax), Mathf.Max(mine.yMax, r.yMax));
				o.GetWorldCorners(_wc);
				ourLeft = Mathf.Min(ourLeft, Mathf.Min(_wc[0].x, _wc[2].x));
			}
			Selectable[] ss = UnityEngine.Object.FindObjectsOfType<Selectable>();
			for (int i = 0; i < ss.Length; i++)
			{
				Selectable s = ss[i];
				Transform st = s.transform;
				if (IsOwn(st) || (st.parent == _anchor && _vanilla.Contains(st.gameObject.GetInstanceID())) || _moved.Contains(st.gameObject.GetInstanceID()) || st.name == "NoPlagueButton")
				{
					continue;
				}
				RectTransform srt = st as RectTransform;
				if (srt == null)
				{
					continue;
				}
				Canvas oc = srt.GetComponentInParent<Canvas>();
				if (oc == null || !object.ReferenceEquals(oc.rootCanvas, myRoot))
				{
					continue;
				}
				Rect other = ScreenRect(srt);
				if (other.width * other.height > mine.width * mine.height * 4f || !other.Overlaps(mine))
				{
					continue;
				}
				srt.GetWorldCorners(_wc);
				float theirRight = Mathf.Max(_wc[0].x, _wc[2].x);
				float shift = theirRight - ourLeft + ourWidth * MoveGapShare;
				st.position = st.position - new Vector3(shift, 0f, 0f);
				_moved.Add(st.gameObject.GetInstanceID());
				_movedT.Add(st);
				_movedX.Add(st.position.x);
				_moves++;
				ULog.Info("title", "moved other button " + s.name + " left by " + shift.ToString("F2", CultureInfo.InvariantCulture) + " world (overlap screen=" + other.ToString() + ") move=" + _moves + " now=" + st.position.ToString("F2"));
				return;
			}
			MoveAbnoSeal(mine, ourLeft, ourWidth);
		}

		private static bool AbnoSealAnimating()
		{
			GameObject np = GameObject.Find("NoPlagueButton");
			if (np == null)
			{
				return false;
			}
			MonoBehaviour[] parts = np.GetComponents<MonoBehaviour>();
			for (int i = 0; i < parts.Length; i++)
			{
				if (parts[i] != null && parts[i].GetType().Name == "MovableImage")
				{
					return true;
				}
			}
			return false;
		}

		// AbnoSeal's button has a label wider than it: both count, and our labels too.
		private static void MoveAbnoSeal(Rect mine, float ourLeft, float ourWidth)
		{
			GameObject np = GameObject.Find("NoPlagueButton");
			if (np == null || _moved.Contains(np.GetInstanceID()))
			{
				return;
			}
			RectTransform srt = np.transform as RectTransform;
			if (srt == null)
			{
				return;
			}
			Rect other = ScreenRect(srt);
			srt.GetWorldCorners(_wc);
			float theirRight = Mathf.Max(_wc[0].x, _wc[2].x);
			RectTransform[] kids = srt.GetComponentsInChildren<RectTransform>(false);
			for (int i = 0; i < kids.Length; i++)
			{
				Rect r = ScreenRect(kids[i]);
				other = Rect.MinMaxRect(Mathf.Min(other.xMin, r.xMin), Mathf.Min(other.yMin, r.yMin), Mathf.Max(other.xMax, r.xMax), Mathf.Max(other.yMax, r.yMax));
				kids[i].GetWorldCorners(_wc);
				theirRight = Mathf.Max(theirRight, Mathf.Max(_wc[0].x, _wc[2].x));
			}
			RectTransform last = _own[_own.Count - 1];
			float margin = last != null ? LabelOverhang(last) * last.lossyScale.x : 0f;
			if (last != null && last.rect.width > 0f)
			{
				mine.xMin -= LabelOverhang(last) * ScreenRect(last).width / last.rect.width;
			}
			if (!other.Overlaps(mine))
			{
				return;
			}
			float shift = theirRight - ourLeft + margin + ourWidth * MoveGapShare;
			Transform st = np.transform;
			st.position = st.position - new Vector3(shift, 0f, 0f);
			_moved.Add(np.GetInstanceID());
			_movedT.Add(st);
			_movedX.Add(st.position.x);
			_moves++;
			ULog.Info("title", "moved NoPlagueButton left by " + shift.ToString("F2", CultureInfo.InvariantCulture) + " world (overlap screen=" + other.ToString() + ") move=" + _moves + " now=" + st.position.ToString("F2"));
		}

		private static Rect ScreenRect(RectTransform rt)
		{
			rt.GetWorldCorners(_wc);
			Camera cam = null;
			Canvas c = rt.GetComponentInParent<Canvas>();
			if (c != null && c.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
			{
				cam = c.rootCanvas.worldCamera;
			}
			Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, _wc[0]);
			Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, _wc[2]);
			return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
		}

		private static void OnClick()
		{
			if (EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
			UiHost.Toggle();
		}
	}
}
