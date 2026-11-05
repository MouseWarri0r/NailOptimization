// NailUI: NailOptimization module (api level 6). Settings interface built to interface_draft.png.
// Harmony is used only for the optional OptionUI hooks. No lock / ?. / interpolation / nameof / expression-bodied members / out var.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine, UnityEngine.UI, System.Xml. Output goes to <NailOptimization>/modules/.
// Files next to NailOptimization.dll:
//   Localization/<lang>/NailUI.xml   (en = layout source: sections, groups, option placement; other languages override texts)
//   Images/config_icon.png           (header logo; same file as the title button icon)
//   Images/reset.png                 (168x145 Reset button; a teal placeholder is used when missing)
// Hook (optional, fail-open): PlaySpeedSettingUI.Update Prefix. That Update handles the Escape key (opens the game's Esc menu, pauses, stops the camera);
// it is skipped for the single frame of the Escape press that belongs to this panel.
// In-game button box: HudBox (v1.8), own overlay canvas under the mission frame, hidden by gates; opens the same panel.
// Behavior: values are staged in the UI and written with Apply (Config.SetFlag/SetInt/SetFloat). Done and a click outside discard staged values.
// 1.11: Apply opens a one-button notice listing applied restart="1" options; day-box action slots show the api 6 badge on their left.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Harmony;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using N = NailOptimization;

namespace NailUI
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class NailUiModule : N.ModuleBase
	{
		public const string Id = "NailUI";

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
				return "1.13";
			}
		}

		// Literal on purpose: a const from Core would be inlined at compile time.
		public override int RequiredApiLevel
		{
			get
			{
				return 8;
			}
		}

		public override void Init()
		{
			Gui.BindOptions();
			GameHooks.Install();
			if (!N.UiHost.Register(new NailUiHost()))
			{
				throw new InvalidOperationException("UI host was not registered");
			}
		}
	}

	internal sealed class NailUiHost : N.IUiHost
	{
		public bool IsOpen
		{
			get
			{
				return Gui.IsOpen;
			}
		}

		public void Toggle()
		{
			Gui.Toggle();
		}

		public void Tick()
		{
			Gui.Tick();
			DisplayOptions.Tick();
		}
	}

	// Screen options of the General section; they replace the Video panel of the game's Option window.
	// Frame pacing for Display.FpsSmooth. The game logic ticks at 50 Hz (fixedDeltaTime 0.02): above 50 FPS some frames get no
	// tick and units stand still in them, at 30-45 FPS frames get one or two ticks; both look uneven (r214, 2026-11-05).
	// A limit that divides the tick and that the machine holds gives the same number of ticks every frame: 50, else 25.
	// Down: the mean frame is over 21.5 ms (under ~46 FPS) in two 2 s windows in a row; single slow frames are normal with
	// the limiter (r215). Up: a 1.5 s probe at 50 after a wait that doubles after each failed probe (10 s up to 60 s); its
	// first 10 frames are skipped. Frames over 50 ms (GC pauses, loads) are hitches, not a lack of speed, and are not counted.
	internal static class SmoothCap
	{
		private const float SlowMean = 0.0215f;
		private const float SkipMs = 0.05f;
		private const int ProbeSkip = 10;
		private const float Window = 2f;
		private const float Probe = 1.5f;
		private const float WaitMin = 10f;
		private const float WaitMax = 60f;
		internal static bool Active;
		private static int _fps;
		private static bool _probing;
		private static float _windowEnd;
		private static float _probeAt;
		private static float _wait;
		private static float _last;
		private static int _frames;
		private static float _sum;
		private static int _skip;
		private static bool _slowBefore;

		internal static void SetActive(bool on)
		{
			Active = on;
			if (on)
			{
				_wait = WaitMin;
				_last = 0f;
				Set(50, Time.realtimeSinceStartup, false);
				N.ULog.Info("nailui", "display: smooth frame pacing on");
			}
		}

		private static void Set(int fps, float now, bool probing)
		{
			_fps = fps;
			_probing = probing;
			Application.targetFrameRate = fps;
			_frames = 0;
			_sum = 0f;
			_skip = probing ? ProbeSkip : 0;
			_windowEnd = now + (probing ? Probe : Window);
		}

		internal static void Frame(float now)
		{
			float dt = _last > 0f ? now - _last : 0f;
			_last = now;
			if (_fps == 25 && !_probing)
			{
				if (now >= _probeAt)
				{
					Set(50, now, true);
				}
				return;
			}
			if (_skip > 0)
			{
				_skip--;
				_windowEnd = now + Probe;
			}
			else if (dt > 0f && dt < SkipMs)
			{
				_frames++;
				_sum += dt;
			}
			if (now < _windowEnd)
			{
				return;
			}
			bool slow = _frames > 0 && _sum > _frames * SlowMean;
			if (!slow || (!_probing && !_slowBefore))
			{
				if (_probing)
				{
					_wait = WaitMin;
				}
				_slowBefore = slow;
				Set(50, now, false);
				return;
			}
			_slowBefore = false;
			_wait = _probing ? Mathf.Min(_wait * 2f, WaitMax) : WaitMin;
			_probeAt = now + _wait;
			Set(25, now, false);
		}
	}

	public static class DisplayOptions
	{
		internal const string ResolutionKey = "Display.Resolution";
		internal const string QualityKey = "Display.TextureQuality";
		internal const string ModeKey = "Display.WindowMode";
		private const int ModeFullscreen = 0;
		private const int ModeBorderless = 1;
		private const int GwlStyle = -16;
		private const uint BorderStyles = 0x00C00000u | 0x00040000u | 0x00080000u | 0x00020000u | 0x00010000u;
		private const uint SwpNoSize = 0x1u;
		private const uint SwpNoMove = 0x2u;
		private const uint SwpNoZOrder = 0x4u;
		private const uint SwpFrameChanged = 0x20u;
		private const uint SwpShowWindow = 0x40u;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr FindWindow(string className, string title);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentProcessId();

		[DllImport("user32.dll")]
		private static extern uint GetWindowLong(IntPtr window, int index);

		[DllImport("user32.dll")]
		private static extern uint SetWindowLong(IntPtr window, int index, uint value);

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int index);

		private static N.IntParam _resolution;
		private static N.IntParam _quality;
		private static N.IntParam _cap;
		private static N.IntParam _mode;
		private static N.Flag _vsync;
		private static N.Flag _smooth;
		private static int _lastResolution;
		private static int _lastQuality;
		private static int _lastCap;
		private static int _lastMode;
		private static int _borderlessStep;
		private static uint _savedStyle;
		private static bool _lastVsync;
		private static bool _started;
		private static float _next;

		internal static void Bind()
		{
			_resolution = N.Config.BindInt(ResolutionKey, 0, 0, 99999999, 1, "Screen resolution as width * 10000 + height, 0 = keep the game's own.");
			_mode = N.Config.BindInt(ModeKey, Screen.fullScreen ? ModeFullscreen : 2, 0, 2, 1, "Window mode: 0 fullscreen, 1 borderless fullscreen, 2 window.");
			_vsync = N.Config.BindFlag("Display.VSync", QualitySettings.vSyncCount > 0, "Frames wait for the monitor refresh. Off: frames are shown as soon as they are ready; tearing is possible.");
			_cap = N.Config.BindInt("Display.FpsCap", 0, 0, 300, 10, "Most frames per second, 0 = no limit. Has no effect while V-sync is on.");
			_smooth = N.Config.BindFlag("Display.FpsSmooth", false, "Even motion: 50 FPS (one game tick per frame) while the game keeps up, 25 when it cannot. Replaces the FPS limit; no effect with V-sync.");
			_quality = N.Config.BindInt(QualityKey, Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, 2), 0, 2, 1, "Texture quality: 0 high, 1 middle, 2 low.");
			_lastResolution = _resolution.Value;
			_lastQuality = _quality.Value;
			_lastMode = _mode.Value;
			_lastVsync = _vsync.On;
		}

		private static int Code(int width, int height)
		{
			return width * 10000 + height;
		}

		// The game itself forces 16:9 at start (GameSettingModel.FixInvalidResolution), so only those are offered.
		private static List<int> Resolutions()
		{
			List<int> list = new List<int>();
			foreach (Resolution r in Screen.resolutions)
			{
				int code = Code(r.width, r.height);
				if (r.width / 16 == r.height / 9 && !list.Contains(code))
				{
					list.Add(code);
				}
			}
			int current = Code(Screen.width, Screen.height);
			if (!list.Contains(current))
			{
				list.Add(current);
			}
			list.Sort();
			return list;
		}

		internal static int NextChoice(string key, int value)
		{
			if (key == QualityKey || key == ModeKey)
			{
				return value >= 2 ? 0 : value + 1;
			}
			if (key == FpsCounter.Key)
			{
				return value >= 3 ? 0 : value + 1;
			}
			List<int> list = Resolutions();
			int i = list.IndexOf(value < 10000 ? Code(Screen.width, Screen.height) : value);
			return list[i < 0 || i >= list.Count - 1 ? 0 : i + 1];
		}

		internal static string ChoiceText(string key, int value)
		{
			if (key == QualityKey)
			{
				return value <= 0 ? Gui.Str("QualityHigh", "High") : (value == 1 ? Gui.Str("QualityMiddle", "Middle") : Gui.Str("QualityLow", "Low"));
			}
			if (key == ModeKey)
			{
				return value <= 0 ? Gui.Str("ModeFullscreen", "Fullscreen") : (value == 1 ? Gui.Str("ModeBorderless", "Borderless fullscreen") : Gui.Str("ModeWindow", "Window"));
			}
			if (key == FpsCounter.Key)
			{
				return value <= 0 ? Gui.Str("FpsOff", "Off") : (value == 1 ? Gui.Str("FpsSimple", "Simple") : (value == 2 ? Gui.Str("FpsStats", "Average, min, max") : Gui.Str("FpsFull", "Full")));
			}
			if (value < 10000)
			{
				value = Code(Screen.width, Screen.height);
			}
			return value / 10000 + "x" + value % 10000;
		}

		internal static void Tick()
		{
			if (_resolution == null)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (SmoothCap.Active)
			{
				SmoothCap.Frame(now);
			}
			if (now < _next)
			{
				return;
			}
			_next = now + 0.5f;
			try
			{
				Apply();
			}
			catch (Exception e)
			{
				N.ULog.Warn("nailui", "display options: " + e.Message);
				_resolution = null;
			}
		}

		private static void Apply()
		{
			bool quality = !_started ? QualitySettings.GetQualityLevel() != _quality.Value : _quality.Value != _lastQuality;
			if (quality)
			{
				_lastQuality = _quality.Value;
				QualitySettings.SetQualityLevel(_quality.Value);
				N.ULog.Info("nailui", "display: quality level " + _quality.Value);
			}
			// A quality level carries its own V-sync value, so the actual value is compared.
			if ((QualitySettings.vSyncCount > 0) != _vsync.On)
			{
				QualitySettings.vSyncCount = _vsync.On ? 1 : 0;
			}
			bool smooth = _smooth.On && !_vsync.On;
			if (smooth != SmoothCap.Active)
			{
				SmoothCap.SetActive(smooth);
				// Back to the plain limit: apply it again.
				_lastCap = int.MinValue;
			}
			if (!smooth && _cap.Value != _lastCap)
			{
				_lastCap = _cap.Value;
				Application.targetFrameRate = _cap.Value > 0 ? _cap.Value : -1;
			}
			// Only an option the user changed is applied, so a value the window itself changed (Alt+Enter, Windows) is not fought back.
			bool valid = _resolution.Value >= 10000 && _resolution.Value % 10000 > 0;
			bool size = _resolution.Value != _lastResolution;
			bool borderless = _mode.Value == ModeBorderless;
			bool mode = _mode.Value != _lastMode;
			if (mode && _lastMode == ModeBorderless)
			{
				RestoreBorder();
			}
			_lastResolution = _resolution.Value;
			_lastMode = _mode.Value;
			if (!_started)
			{
				size = valid && Code(Screen.width, Screen.height) != _resolution.Value;
				mode = borderless || (valid && Screen.fullScreen != (_mode.Value == ModeFullscreen));
			}
			if (borderless)
			{
				// The window covers the monitor, so the resolution option does not apply here.
				if (mode)
				{
					Screen.SetResolution(Screen.width, Screen.height, false);
					_borderlessStep = 1;
				}
				else if (_borderlessStep > 0)
				{
					StepBorderless();
				}
			}
			else if (mode || (size && valid))
			{
				_borderlessStep = 0;
				int width = valid ? _resolution.Value / 10000 : Screen.width;
				int height = valid ? _resolution.Value % 10000 : Screen.height;
				bool full = mode ? _mode.Value == ModeFullscreen : Screen.fullScreen;
				Screen.SetResolution(width, height, full);
				N.ULog.Info("nailui", "display: " + width + "x" + height + " fullscreen=" + full);
			}
			_started = true;
		}

		private static IntPtr GameWindow()
		{
			IntPtr window = FindWindow("UnityWndClass", null);
			uint process;
			if (window != IntPtr.Zero && GetWindowThreadProcessId(window, out process) != 0u && process == GetCurrentProcessId())
			{
				return window;
			}
			return IntPtr.Zero;
		}

		// Unity 2017 has no borderless mode of its own. The monitor size is known only after an exclusive mode has ended, hence the steps a tick apart.
		private static void StepBorderless()
		{
			int width = GetSystemMetrics(0);
			int height = GetSystemMetrics(1);
			if (_borderlessStep == 1)
			{
				Screen.SetResolution(width, height, false);
				_borderlessStep = 2;
				return;
			}
			_borderlessStep = 0;
			IntPtr window = GameWindow();
			if (window == IntPtr.Zero)
			{
				N.ULog.Warn("nailui", "display: game window not found, borderless not applied");
				return;
			}
			uint style = GetWindowLong(window, GwlStyle);
			if ((style & BorderStyles) != 0u)
			{
				_savedStyle = style;
			}
			SetWindowLong(window, GwlStyle, style & ~BorderStyles);
			SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
			N.ULog.Info("nailui", "display: borderless " + width + "x" + height);
		}

		private static void RestoreBorder()
		{
			_borderlessStep = 0;
			IntPtr window = GameWindow();
			if (window != IntPtr.Zero && _savedStyle != 0u)
			{
				SetWindowLong(window, GwlStyle, _savedStyle);
				SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
			}
		}
	}

	// Optional hook. A failure here only disables Escape blocking (the panel still closes on Esc, the game's Esc menu opens as well).
	public static class GameHooks
	{
		public const string HarmonyId = "Lobotomy.NailOptimization.NailUI";

		internal static int Install()
		{
			try
			{
				return InstallInner();
			}
			catch (Exception e)
			{
				N.ULog.Warn("nailui", "game hook not installed: " + e.GetType().Name + " " + e.Message);
				return 0;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int InstallInner()
		{
			HarmonyInstance harmony = HarmonyInstance.Create(HarmonyId);
			int n = InstallSpeed(harmony) + InstallCamera(harmony);
			N.ULog.Info("nailui", "game hook installed " + n + "/2 (PlaySpeedSettingUI.Update, CameraMover.Update)");
			InstallOptionPanel(harmony);
			return n;
		}

		private static void InstallOptionPanel(HarmonyInstance harmony)
		{
			try
			{
				harmony.Patch(AccessTools.Method(typeof(OptionUI), "OnOpen"), null, new HarmonyMethod(typeof(GameHooks).GetMethod("HideVideoPanel", BindingFlags.Public | BindingFlags.Static)));
			}
			catch (Exception e)
			{
				N.ULog.Warn("nailui", "OptionUI hook not installed: " + e.Message);
			}
		}

		// The panel is the lowest parent of the three dropdowns; it has no field of its own.
		public static void HideVideoPanel(OptionUI __instance)
		{
			try
			{
				Transform t = __instance.Opt_Resolution.transform;
				while (t != null && !(__instance.Opt_Display.transform.IsChildOf(t) && __instance.Opt_Texture.transform.IsChildOf(t)))
				{
					t = t.parent;
				}
				if (t != null && !__instance.Opt_MasterVolume.transform.IsChildOf(t) && !__instance.Opt_Language.transform.IsChildOf(t) && !__instance.Opt_Tooltip.transform.IsChildOf(t) && !__instance.Opt_DlcRoot.transform.IsChildOf(t))
				{
					t.gameObject.SetActive(false);
					CenterPanels(t, __instance.Opt_MasterVolume.transform);
				}
				else
				{
					__instance.Opt_Resolution.gameObject.SetActive(false);
					__instance.Opt_Display.gameObject.SetActive(false);
					__instance.Opt_Texture.gameObject.SetActive(false);
				}
				// Init of the hidden dropdowns may have queued their values; Save must not apply them over the display options.
				Dictionary<OptionUI.OptionAction, object> queued = AccessTools.Field(typeof(OptionUI), "currentOptionSetted").GetValue(__instance) as Dictionary<OptionUI.OptionAction, object>;
				queued.Remove(OptionUI.OptionAction.RESOLUTION);
				queued.Remove(OptionUI.OptionAction.DISPLAYMODE);
				queued.Remove(OptionUI.OptionAction.TEXTUREQUALITY);
			}
			catch (Exception e)
			{
				N.ULog.Warn("nailui", "video panel: " + e.Message);
			}
		}

		// The remaining columns (Game, Audio) keep their three-column x; shift them so their span is centered. Idempotent: a centered span shifts by 0.
		// The hidden object sits inside the video column; the column is its ancestor whose parent also holds the audio column.
		private static void CenterPanels(Transform hidden, Transform audio)
		{
			while (hidden.parent != null && !audio.IsChildOf(hidden.parent))
			{
				hidden = hidden.parent;
			}
			Transform area = hidden.parent;
			if (area == null)
			{
				return;
			}
			float min = float.MaxValue;
			float max = float.MinValue;
			for (int i = 0; i < area.childCount; i++)
			{
				RectTransform rt = area.GetChild(i) as RectTransform;
				if (rt != null && rt != hidden && rt.gameObject.activeSelf)
				{
					min = Mathf.Min(min, rt.anchoredPosition.x - rt.rect.width * 0.5f);
					max = Mathf.Max(max, rt.anchoredPosition.x + rt.rect.width * 0.5f);
				}
			}
			if (min > max)
			{
				return;
			}
			float shift = -(min + max) * 0.5f;
			for (int i = 0; i < area.childCount; i++)
			{
				RectTransform rt = area.GetChild(i) as RectTransform;
				if (rt != null && rt != hidden && rt.gameObject.activeSelf)
				{
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + shift, rt.anchoredPosition.y);
				}
			}
		}

		private static int InstallSpeed(HarmonyInstance harmony)
		{
			Type ps = N.TypeCache.Resolve("PlaySpeedSettingUI");
			if (object.ReferenceEquals(ps, null))
			{
				N.ULog.Warn("nailui", "type not found: PlaySpeedSettingUI (the game's Esc menu cannot be blocked)");
				return 0;
			}
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			MethodInfo target = ps.GetMethod("Update", flags, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(target, null))
			{
				N.ULog.Warn("nailui", "PlaySpeedSettingUI.Update() not found, hook skipped");
				return 0;
			}
			HarmonyMethod pm = new HarmonyMethod(typeof(GameHooks).GetMethod("PrefixUpdate", BindingFlags.Public | BindingFlags.Static));
			return N.PatchUtil.Install(harmony, NailUiModule.Id, target, pm, null) ? 1 : 0;
		}

		// Camera guard (GL-51): CameraMover keeps one private bool lock with many writers, so StopMove/ReleaseMove are not used.
		// On a typing frame the Prefix clears _movable only if it is set, the Postfix sets it back in the same frame.
		private static int InstallCamera(HarmonyInstance harmony)
		{
			Type cm = N.TypeCache.Resolve("CameraMover");
			if (object.ReferenceEquals(cm, null))
			{
				N.ULog.Warn("nailui", "camera guard skipped: type CameraMover not found");
				return 0;
			}
			FieldInfo f = cm.GetField("_movable", BindingFlags.NonPublic | BindingFlags.Instance);
			if (object.ReferenceEquals(f, null) || !object.ReferenceEquals(f.FieldType, typeof(bool)))
			{
				N.ULog.Warn("nailui", "camera guard skipped: CameraMover._movable not found");
				return 0;
			}
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			MethodInfo target = cm.GetMethod("Update", flags, null, Type.EmptyTypes, null);
			if (object.ReferenceEquals(target, null))
			{
				N.ULog.Warn("nailui", "camera guard skipped: CameraMover.Update() not found");
				return 0;
			}
			_movableField = f;
			HarmonyMethod pre = new HarmonyMethod(typeof(GameHooks).GetMethod("PrefixCamera", BindingFlags.Public | BindingFlags.Static));
			HarmonyMethod post = new HarmonyMethod(typeof(GameHooks).GetMethod("PostfixCamera", BindingFlags.Public | BindingFlags.Static));
			return N.PatchUtil.Install(harmony, NailUiModule.Id, target, pre, post) ? 1 : 0;
		}

		private static FieldInfo _movableField;
		private static CameraMover _camHeld;
		private static bool _camTyping;
		private static int _camLogs;
		private static int _camWheelLogs;
		private static bool _hostSeen;
		private static bool _hostLost;
		private static int _actEscLogs;

		// PlaySpeedSettingUI.Update handles Space, Escape, Q, E and F1. On the Escape frame that belongs to the panel the whole
		// Update is skipped: the Esc menu does not open and neither the pause nor the camera stop happens.
		public static bool PrefixUpdate()
		{
			try
			{
				if (!N.UiHost.Registered)
				{
					// Core dropped the host: Gui.Tick no longer runs, so hide the day button once here.
					if (_hostSeen && !_hostLost)
					{
						_hostLost = true;
						HudBox.Shutdown();
						N.ULog.Warn("nailui", "ui host dropped: day button hidden, game input no longer blocked");
					}
					return true;
				}
				_hostSeen = true;
				if (Gui.BlockEscapeFrame())
				{
					return false;
				}
				// Api 5: an action window (e.g. LogViewer) owns this Escape press as well.
				if (N.UiActions.EscapeOwned())
				{
					if (_actEscLogs < 4)
					{
						_actEscLogs++;
						N.ULog.Info("nailui", "PlaySpeedSettingUI.Update skipped on Escape (action window)");
					}
					return false;
				}
				return true;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// Never skips CameraMover.Update: attach follow and forced camera moves run before its movable check.
		public static void PrefixCamera(CameraMover __instance)
		{
			try
			{
				ReleaseCamera();
				bool typing = Gui.Typing && N.UiHost.Registered;
				if (!typing && _camTyping)
				{
					_camTyping = false;
					LogCamera("camera released after typing");
				}
				// Wheel frame over the NailUI panel or an action window (api 5): hold the camera for this frame only, so it does not zoom.
				bool wheel = !typing && N.UiHost.Registered && Input.mouseScrollDelta.y != 0f && (Gui.WheelOverPanel() || N.UiActions.WheelOwned());
				if (!typing && !wheel)
				{
					return;
				}
				if (!(bool)_movableField.GetValue(__instance))
				{
					return;
				}
				_movableField.SetValue(__instance, false);
				_camHeld = __instance;
				if (typing && !_camTyping)
				{
					_camTyping = true;
					LogCamera("camera held while typing");
				}
				else if (wheel && _camWheelLogs < 2)
				{
					_camWheelLogs++;
					N.ULog.Info("nailui", "camera held on a wheel frame over a mod window");
				}
			}
			catch (Exception)
			{
			}
		}

		public static void PostfixCamera()
		{
			ReleaseCamera();
		}

		// Restores only the lock this guard took, and only if nothing set it since. Also called from Gui.Close and after a Gui.Tick error
		// (Harmony 1.0.9.1 runs no Postfix when the original throws).
		internal static void ReleaseCamera()
		{
			if (object.ReferenceEquals(_camHeld, null))
			{
				return;
			}
			CameraMover cm = _camHeld;
			_camHeld = null;
			try
			{
				if (cm != null && !(bool)_movableField.GetValue(cm))
				{
					_movableField.SetValue(cm, true);
				}
			}
			catch (Exception)
			{
			}
		}

		private static void LogCamera(string msg)
		{
			if (_camLogs < 6)
			{
				_camLogs++;
				N.ULog.Info("nailui", msg);
			}
		}
	}

	internal static class Theme
	{
		internal const float PanelW = 1040f;
		internal const float PanelH = 576f;
		internal const float MockW = 1747f;
		internal const float MockH = 967f;
		internal const float K = PanelW / MockW;

		internal const float LeftViewW = (552f - 33f) * K;
		internal const float LeftViewH = (858f - 106f) * K;
		internal const float RightViewW = (1675f - 630f) * K;
		internal const float RightViewH = (858f - 100f) * K;

		internal const float RowH = 40f;
		internal const float ItemH = 40f;
		internal const float GroupHead = 34f;
		internal const float GroupPad = 8f;
		internal const float GroupGap = 10f;
		internal const float ScrollStep = 48f;

		internal const int TitleFont = 28;
		internal const int SearchFont = 22;
		internal const int ListFont = 22;
		internal const int ButtonFont = 26;
		internal const int GroupFont = 18;
		internal const int RowFont = 18;
		internal const int ValueFont = 16;
		internal const int StatusFont = 15;
		internal const int TipFont = 15;

		internal static readonly Color Solid = new Color(0f, 0f, 0f, 1f);
		internal static readonly Color Back = new Color(0f, 0f, 0f, 0.97f);
		internal static readonly Color Teal = new Color(0.18f, 0.74f, 0.64f, 1f);
		internal static readonly Color TealHot = new Color(0.37f, 0.88f, 0.78f, 1f);
		internal static readonly Color TealDark = new Color(0.09f, 0.34f, 0.29f, 1f);
		internal static readonly Color Yellow = new Color(1f, 1f, 0.63f, 1f);
		internal static readonly Color Dim = new Color(0.86f, 0.85f, 0.51f, 1f);
		internal static readonly Color Text = new Color(0.85f, 0.84f, 0.5f, 1f);
		internal static readonly Color DimText = new Color(0.43f, 0.43f, 0.28f, 1f);
		internal static readonly Color Track = new Color(0.2f, 0.2f, 0.11f, 1f);
		internal static readonly Color Red = new Color(1f, 0.36f, 0.3f, 1f);
		internal static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
		internal static readonly Color Orange = new Color(0.996f, 0.576f, 0.259f, 1f);
	}

	// ---------- uGUI factory helpers ----------
	internal static class Mk
	{
		internal static RectTransform Rt(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		// Rectangle given in pixels of interface_draft.png (1747 x 967, origin top-left), as fractions of the panel.
		internal static void Frac(RectTransform rt, float x0, float y0, float x1, float y1)
		{
			rt.anchorMin = new Vector2(x0 / Theme.MockW, 1f - y1 / Theme.MockH);
			rt.anchorMax = new Vector2(x1 / Theme.MockW, 1f - y0 / Theme.MockH);
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		// Top-left anchored rectangle in reference pixels.
		internal static void Px(RectTransform rt, float x, float y, float w, float h)
		{
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = new Vector2(x, -y);
			rt.sizeDelta = new Vector2(w, h);
		}

		internal static void Stretch(RectTransform rt, float l, float t, float r, float b)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(l, b);
			rt.offsetMax = new Vector2(-r, -t);
		}

		internal static Image Img(RectTransform rt, Color c, bool raycast)
		{
			Image i = rt.gameObject.AddComponent<Image>();
			i.color = c;
			i.raycastTarget = raycast;
			return i;
		}

		internal static Text Txt(RectTransform rt, string s, int size, Color c, TextAnchor a)
		{
			Text t = rt.gameObject.AddComponent<Text>();
			t.font = Gui.Font;
			t.fontSize = size;
			t.color = c;
			t.alignment = a;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			t.supportRichText = false;
			t.raycastTarget = false;
			t.text = s;
			Gui.Texts.Add(t);
			return t;
		}

		// Border image (outer, child "Fill" is the inner area). Content is parented to the returned outer rect.
		internal static RectTransform Box(Transform parent, string name, Color border, Color fill, float thick, bool raycast)
		{
			RectTransform outer = Rt(name, parent);
			Img(outer, border, raycast);
			RectTransform inner = Rt("Fill", outer);
			Stretch(inner, thick, thick, thick, thick);
			Img(inner, fill, false);
			return outer;
		}

		// Shrinks the text to fit its rectangle (long translations). Wrap is required for best fit to consider the width.
		internal static void Fit(Text t, int minSize)
		{
			t.horizontalOverflow = HorizontalWrapMode.Wrap;
			t.resizeTextForBestFit = true;
			t.resizeTextMinSize = minSize;
			t.resizeTextMaxSize = t.fontSize;
		}

		// The caret is created as the FIRST child of the text's parent: that parent must be the filled area, not a sibling of the fill.
		internal static void Caret(InputField f)
		{
			f.caretWidth = 2;
			f.caretBlinkRate = 0.85f;
			f.customCaretColor = true;
			f.caretColor = Theme.Yellow;
			f.selectionColor = new Color(0.18f, 0.74f, 0.64f, 0.45f);
		}

		internal static void NoNav(Selectable s)
		{
			Navigation nav = new Navigation();
			nav.mode = Navigation.Mode.None;
			s.navigation = nav;
		}

		internal static void Tint(Button b, Image target, Color normal, Color high, Color pressed, Color disabled)
		{
			target.color = Color.white;
			b.targetGraphic = target;
			b.transition = Selectable.Transition.ColorTint;
			ColorBlock cb = ColorBlock.defaultColorBlock;
			cb.normalColor = normal;
			cb.highlightedColor = high;
			cb.pressedColor = pressed;
			cb.disabledColor = disabled;
			cb.colorMultiplier = 1f;
			cb.fadeDuration = 0.05f;
			b.colors = cb;
			NoNav(b);
		}
	}

	// ---------- data ----------
	internal sealed class OptLayout
	{
		internal string Section = string.Empty;
		internal string Group = string.Empty;
		internal int Order;
		internal bool Restart;
		internal bool Hidden;
		internal string Requires = string.Empty;
		internal string Control = string.Empty;
		internal string Name = string.Empty;
		internal string Desc = string.Empty;
	}

	internal sealed class LocData
	{
		internal string Lang = "en";
		internal string Title = "NailOptimization";
		internal string Version = string.Empty;
		internal bool EnLoaded;
		internal bool LangLoaded;
		internal readonly Dictionary<string, string> Ui = new Dictionary<string, string>(StringComparer.Ordinal);
		internal readonly Dictionary<string, string> SecTitle = new Dictionary<string, string>(StringComparer.Ordinal);
		internal readonly Dictionary<string, string> SecSide = new Dictionary<string, string>(StringComparer.Ordinal);
		internal readonly List<string> SecIds = new List<string>();
		internal readonly Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.Ordinal);
		internal readonly Dictionary<string, OptLayout> Opts = new Dictionary<string, OptLayout>(StringComparer.OrdinalIgnoreCase);
	}

	internal static class Loc
	{
		internal static LocData Load(string lang)
		{
			LocData d = new LocData();
			d.Lang = lang;
			string root = Path.Combine(N.UiHost.RootDir, "Localization");
			d.EnLoaded = ParseFile(Path.Combine(Path.Combine(root, "en"), "NailUI.xml"), d, true);
			if (!string.Equals(lang, "en", StringComparison.Ordinal))
			{
				d.LangLoaded = ParseFile(Path.Combine(Path.Combine(root, lang), "NailUI.xml"), d, false);
			}
			else
			{
				d.LangLoaded = d.EnLoaded;
			}
			if (d.SecIds.Count == 0)
			{
				AddSection(d, "General", "top");
				AddSection(d, "Performance", "top");
				AddSection(d, "Graphic", "top");
				AddSection(d, "Advanced", "top");
				AddSection(d, "Bugfixes", "top");
				AddSection(d, "Interface", "bottom");
				AddSection(d, "Experimental", "bottom");
				AddSection(d, "Debug", "bottom");
			}
			if (!d.SecSide.ContainsKey("Advanced"))
			{
				AddSection(d, "Advanced", "top");
			}
			return d;
		}

		private static void AddSection(LocData d, string id, string side)
		{
			if (!d.SecSide.ContainsKey(id))
			{
				d.SecIds.Add(id);
				d.SecSide[id] = side;
			}
		}

		private static bool ParseFile(string path, LocData d, bool layout)
		{
			try
			{
				if (!File.Exists(path))
				{
					N.ULog.Warn("nailui", "localization file not found: " + path);
					return false;
				}
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				XmlElement root = doc.DocumentElement;
				if (root == null)
				{
					return false;
				}
				string t = root.GetAttribute("title");
				if (t.Length > 0)
				{
					d.Title = t;
				}
				string v = root.GetAttribute("version");
				if (v.Length > 0)
				{
					d.Version = v;
				}
				XmlNodeList kids = root.ChildNodes;
				for (int i = 0; i < kids.Count; i++)
				{
					XmlElement sec = kids[i] as XmlElement;
					if (sec == null)
					{
						continue;
					}
					if (sec.Name == "ui")
					{
						ReadStrings(sec, "s", d.Ui);
					}
					else if (sec.Name == "sections")
					{
						ReadSections(sec, d, layout);
					}
					else if (sec.Name == "groups")
					{
						ReadStrings(sec, "group", d.Groups);
					}
					else if (sec.Name == "options")
					{
						ReadOptions(sec, d, layout);
					}
				}
				return true;
			}
			catch (Exception e)
			{
				N.ULog.Error("nailui", "localization parse failed " + path + ": " + e);
				return false;
			}
		}

		private static void ReadStrings(XmlElement parent, string tag, Dictionary<string, string> into)
		{
			XmlNodeList kids = parent.ChildNodes;
			for (int i = 0; i < kids.Count; i++)
			{
				XmlElement e = kids[i] as XmlElement;
				if (e == null || e.Name != tag)
				{
					continue;
				}
				string id = e.GetAttribute("id");
				string text = e.InnerText;
				if (id.Length > 0 && text.Length > 0)
				{
					into[id] = text;
				}
			}
		}

		private static void ReadSections(XmlElement parent, LocData d, bool layout)
		{
			XmlNodeList kids = parent.ChildNodes;
			for (int i = 0; i < kids.Count; i++)
			{
				XmlElement e = kids[i] as XmlElement;
				if (e == null || e.Name != "section")
				{
					continue;
				}
				string id = e.GetAttribute("id");
				if (id.Length == 0)
				{
					continue;
				}
				if (layout)
				{
					string side = e.GetAttribute("side");
					AddSection(d, id, side == "bottom" ? "bottom" : "top");
				}
				string text = e.InnerText;
				if (text.Length > 0)
				{
					d.SecTitle[id] = text;
				}
			}
		}

		private static void ReadOptions(XmlElement parent, LocData d, bool layout)
		{
			XmlNodeList kids = parent.ChildNodes;
			for (int i = 0; i < kids.Count; i++)
			{
				XmlElement e = kids[i] as XmlElement;
				if (e == null || e.Name != "option")
				{
					continue;
				}
				string key = e.GetAttribute("key");
				if (key.Length == 0)
				{
					continue;
				}
				OptLayout ol;
				if (!d.Opts.TryGetValue(key, out ol))
				{
					if (!layout)
					{
						continue;
					}
					ol = new OptLayout();
					d.Opts[key] = ol;
				}
				if (layout)
				{
					ol.Section = e.GetAttribute("section");
					ol.Group = e.GetAttribute("group");
					int ord;
					if (int.TryParse(e.GetAttribute("order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ord))
					{
						ol.Order = ord;
					}
					ol.Restart = e.GetAttribute("restart") == "1";
					ol.Hidden = e.GetAttribute("hidden") == "1";
					ol.Requires = e.GetAttribute("requires");
					ol.Control = e.GetAttribute("control");
				}
				XmlNodeList sub = e.ChildNodes;
				for (int j = 0; j < sub.Count; j++)
				{
					XmlElement c = sub[j] as XmlElement;
					if (c == null)
					{
						continue;
					}
					string text = c.InnerText.Trim();
					if (text.Length == 0)
					{
						continue;
					}
					if (c.Name == "name")
					{
						ol.Name = text;
					}
					else if (c.Name == "desc")
					{
						ol.Desc = text;
					}
				}
			}
		}
	}

	internal sealed class SecDef
	{
		internal string Id = string.Empty;
		internal string Side = "top";
		internal string Title = string.Empty;
		internal readonly List<OptDef> Opts = new List<OptDef>();
		internal GameObject Sel;
		internal Text Label;
	}

	internal sealed class SecClick
	{
		internal SecDef S;

		internal void OnClick()
		{
			Gui.OnSectionClick(S);
		}
	}

	internal sealed class ResetAllClick
	{
		internal void OnClick()
		{
			Gui.AskResetAll();
		}
	}

	internal sealed class OptDef
	{
		internal N.OptionInfo Info;
		internal string Section = "Advanced";
		internal string Group = "Other";
		internal string Name = string.Empty;
		internal string Desc = string.Empty;
		internal string NameLower = string.Empty;
		internal string DescLower = string.Empty;
		internal int Order;
		internal int Seq;
		internal bool Restart;
		internal string Control = string.Empty;

		internal bool HasPend;
		internal bool PB;
		internal int PI;
		internal float PF;
		internal bool BB;
		internal int BI;
		internal float BF;

		internal bool IsFlag
		{
			get
			{
				return Info.Kind == N.OptionKind.Flag;
			}
		}

		internal bool IsInt
		{
			get
			{
				return Info.Kind == N.OptionKind.Int;
			}
		}

		internal float Min
		{
			get
			{
				return IsInt ? (float)Info.IntParam.Min : Info.FloatParam.Min;
			}
		}

		internal float Max
		{
			get
			{
				return IsInt ? (float)Info.IntParam.Max : Info.FloatParam.Max;
			}
		}

		internal float Def
		{
			get
			{
				return IsInt ? (float)Info.IntParam.Def : Info.FloatParam.Def;
			}
		}

		internal bool CurB()
		{
			return HasPend ? PB : Info.Flag.On;
		}

		internal int CurI()
		{
			return HasPend ? PI : Info.IntParam.Value;
		}

		internal float CurF()
		{
			if (IsInt)
			{
				return HasPend ? (float)PI : (float)Info.IntParam.Value;
			}
			return HasPend ? PF : Info.FloatParam.Value;
		}

		internal string Fmt(float v)
		{
			if (IsInt)
			{
				return ((int)Math.Round((double)v)).ToString(CultureInfo.InvariantCulture);
			}
			return v.ToString("0.####", CultureInfo.InvariantCulture);
		}

		internal void StageB(bool v)
		{
			if (v == Info.Flag.On)
			{
				HasPend = false;
				return;
			}
			HasPend = true;
			PB = v;
			BB = Info.Flag.On;
		}

		internal void StageI(int v)
		{
			if (v < Info.IntParam.Min)
			{
				v = Info.IntParam.Min;
			}
			if (v > Info.IntParam.Max)
			{
				v = Info.IntParam.Max;
			}
			if (v == Info.IntParam.Value)
			{
				HasPend = false;
				return;
			}
			HasPend = true;
			PI = v;
			BI = Info.IntParam.Value;
		}

		internal void StageF(float v)
		{
			if (float.IsNaN(v))
			{
				return;
			}
			if (v < Info.FloatParam.Min)
			{
				v = Info.FloatParam.Min;
			}
			if (v > Info.FloatParam.Max)
			{
				v = Info.FloatParam.Max;
			}
			if (Math.Abs(v - Info.FloatParam.Value) < 0.00001f)
			{
				HasPend = false;
				return;
			}
			HasPend = true;
			PF = v;
			BF = Info.FloatParam.Value;
		}

		// Slider input: snap to the declared step.
		internal void StageFromSlider(float v)
		{
			if (IsInt)
			{
				int iv = Mathf.RoundToInt(v);
				int st = Info.IntParam.Step;
				if (st > 1)
				{
					iv = Info.IntParam.Min + Mathf.RoundToInt((float)(iv - Info.IntParam.Min) / (float)st) * st;
				}
				StageI(iv);
			}
			else
			{
				float st2 = Info.FloatParam.Step;
				float q = v;
				if (st2 > 0f)
				{
					q = Info.FloatParam.Min + (float)Math.Round((double)((v - Info.FloatParam.Min) / st2)) * st2;
				}
				StageF((float)Math.Round((double)q, 4));
			}
		}

		internal void StageDefault()
		{
			if (IsFlag)
			{
				StageB(Info.Flag.Def);
			}
			else if (IsInt)
			{
				StageI(Info.IntParam.Def);
			}
			else
			{
				StageF(Info.FloatParam.Def);
			}
		}

		// The live value moved away from what it was when the value was staged (cfg edited outside, or another module).
		internal bool ExternalChanged()
		{
			if (!HasPend)
			{
				return false;
			}
			if (IsFlag)
			{
				return Info.Flag.On != BB;
			}
			if (IsInt)
			{
				return Info.IntParam.Value != BI;
			}
			return Math.Abs(Info.FloatParam.Value - BF) > 0.00001f;
		}

		internal void ApplyPending()
		{
			if (!HasPend)
			{
				return;
			}
			if (IsFlag)
			{
				N.Config.SetFlag(Info.Flag, PB);
			}
			else if (IsInt)
			{
				N.Config.SetInt(Info.IntParam, PI);
			}
			else
			{
				N.Config.SetFloat(Info.FloatParam, PF);
			}
			HasPend = false;
		}
	}

	// ---------- scroll pane: masked content, a scrollbar, wheel from Gui.Tick ----------
	internal sealed class Pane
	{
		internal RectTransform Frame;
		internal RectTransform View;
		internal RectTransform Content;
		internal Scrollbar Bar;
		internal float ViewH;
		internal float ContentH;
		internal float Offset;
		private bool _suppress;

		internal static Pane Create(RectTransform panel, string name, float[] frame, float[] view, float[] bar, float viewH)
		{
			Pane p = new Pane();
			p.ViewH = viewH;

			RectTransform fr = Mk.Box(panel, name + "Frame", Theme.Teal, Theme.Solid, 3f, false);
			Mk.Frac(fr, frame[0], frame[1], frame[2], frame[3]);
			p.Frame = fr;

			RectTransform vw = Mk.Rt(name + "View", panel);
			Mk.Frac(vw, view[0], view[1], view[2], view[3]);
			vw.gameObject.AddComponent<RectMask2D>();
			p.View = vw;

			RectTransform content = Mk.Rt(name + "Content", vw);
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0f, 1f);
			content.anchoredPosition = Vector2.zero;
			content.sizeDelta = Vector2.zero;
			p.Content = content;

			RectTransform sbr = Mk.Box(panel, name + "Bar", Theme.Dim, Theme.Solid, 2f, true);
			Mk.Frac(sbr, bar[0], bar[1], bar[2], bar[3]);
			RectTransform area = Mk.Rt("Area", sbr);
			Mk.Stretch(area, 3f, 3f, 3f, 3f);
			RectTransform handle = Mk.Rt("Handle", area);
			handle.sizeDelta = Vector2.zero;
			Image hi = Mk.Img(handle, Theme.Yellow, true);
			Scrollbar sb = sbr.gameObject.AddComponent<Scrollbar>();
			sb.handleRect = handle;
			sb.targetGraphic = hi;
			sb.direction = Scrollbar.Direction.TopToBottom;
			sb.transition = Selectable.Transition.None;
			Mk.NoNav(sb);
			sb.onValueChanged.AddListener(new UnityAction<float>(p.OnBar));
			p.Bar = sb;
			p.Apply();
			return p;
		}

		internal void SetContentHeight(float h)
		{
			ContentH = h;
			Content.sizeDelta = new Vector2(0f, h);
			Offset = 0f;
			Apply();
		}

		internal void ScrollBy(float px)
		{
			Offset += px;
			Apply();
		}

		internal bool Contains(Vector2 screen)
		{
			return RectTransformUtility.RectangleContainsScreenPoint(Frame, screen, null);
		}

		internal bool ContainsView(Vector2 screen)
		{
			return RectTransformUtility.RectangleContainsScreenPoint(View, screen, null);
		}

		private void OnBar(float v)
		{
			if (_suppress)
			{
				return;
			}
			float max = Mathf.Max(0f, ContentH - ViewH);
			Offset = v * max;
			Content.anchoredPosition = new Vector2(0f, Offset);
		}

		private void Apply()
		{
			float max = Mathf.Max(0f, ContentH - ViewH);
			if (Offset < 0f)
			{
				Offset = 0f;
			}
			if (Offset > max)
			{
				Offset = max;
			}
			Content.anchoredPosition = new Vector2(0f, Offset);
			_suppress = true;
			Bar.size = ContentH > ViewH ? ViewH / ContentH : 1f;
			Bar.value = max > 0f ? Offset / max : 0f;
			Bar.interactable = max > 0.5f;
			_suppress = false;
		}
	}

	// ---------- option row ----------
	internal sealed class Row
	{
		internal static bool Suppress;

		internal OptDef D;
		internal RectTransform Rt;
		internal Text NameT;
		internal RectTransform NameRt;
		internal Slider Sld;
		internal InputField Field;
		internal Image FieldBorder;
		internal Button Tog;
		internal Image TogFill;
		internal Text TogText;
		internal Button Reset;
		internal Text KeyLabel;
		private bool _showSection;

		internal Row(OptDef d)
		{
			D = d;
		}

		internal void Build(Transform parent, float x, float y, float w, bool showSection)
		{
			_showSection = showSection;
			Rt = Mk.Rt("Row_" + D.Info.Key, parent);
			Mk.Px(Rt, x, y, w, Theme.RowH);

			RectTransform nr = Mk.Rt("Name", Rt);
			Mk.Px(nr, 0f, 0f, D.IsFlag ? w * 0.72f : w * 0.40f, Theme.RowH);
			NameRt = nr;
			NameT = Mk.Txt(nr, string.Empty, Theme.RowFont, Theme.Yellow, TextAnchor.MiddleLeft);
			NameT.horizontalOverflow = HorizontalWrapMode.Wrap;
			NameT.resizeTextForBestFit = true;
			NameT.resizeTextMinSize = 11;
			NameT.resizeTextMaxSize = Theme.RowFont;

			float cx = w * 0.42f;
			float cw = w * 0.31f;
			if (D.IsFlag)
			{
				BuildToggle(w * 0.75f, w * 0.12f);
			}
			else if (D.Control == "key")
			{
				BuildKey(cx, cw);
				BuildReset(w);
			}
			else if (D.Control == "choice")
			{
				BuildKey(cx, cw);
				Tog.onClick.RemoveAllListeners();
				Tog.onClick.AddListener(new UnityAction(OnChoiceClick));
				BuildReset(w);
			}
			else
			{
				BuildSlider(cx, cw);
				BuildField(w * 0.75f, w * 0.12f);
				BuildReset(w);
			}
			Refresh();
		}

		private void BuildToggle(float fx, float fw)
		{
			RectTransform tb = Mk.Box(Rt, "Toggle", Theme.Dim, Theme.Solid, 2f, true);
			Mk.Px(tb, fx, (Theme.RowH - 28f) * 0.5f, fw, 28f);
			Image outer = tb.GetComponent<Image>();
			TogFill = tb.GetChild(0).GetComponent<Image>();
			RectTransform lr = Mk.Rt("Label", tb);
			Mk.Stretch(lr, 2f, 0f, 2f, 0f);
			TogText = Mk.Txt(lr, string.Empty, Theme.ValueFont, Theme.Dim, TextAnchor.MiddleCenter);
			Mk.Fit(TogText, 10);
			Tog = tb.gameObject.AddComponent<Button>();
			Mk.Tint(Tog, outer, Theme.Dim, Theme.Yellow, Theme.Teal, Theme.TealDark);
			Tog.onClick.AddListener(new UnityAction(OnToggle));
		}

		// A button that shows the bound key; a click starts key capture (Gui.Tick), Esc cancels.
		private void BuildKey(float cx, float cw)
		{
			RectTransform kb = Mk.Box(Rt, "Key", Theme.Dim, Theme.Solid, 2f, true);
			Mk.Px(kb, cx, (Theme.RowH - 28f) * 0.5f, cw, 28f);
			Image outer = kb.GetComponent<Image>();
			RectTransform lr = Mk.Rt("Label", kb);
			Mk.Stretch(lr, 4f, 0f, 4f, 0f);
			KeyLabel = Mk.Txt(lr, string.Empty, Theme.ValueFont, Theme.Text, TextAnchor.MiddleCenter);
			Mk.Fit(KeyLabel, 10);
			Tog = kb.gameObject.AddComponent<Button>();
			Mk.Tint(Tog, outer, Theme.Dim, Theme.Yellow, Theme.Teal, Theme.TealDark);
			Tog.onClick.AddListener(new UnityAction(OnKeyClick));
		}

		private void OnChoiceClick()
		{
			try
			{
				D.StageI(DisplayOptions.NextChoice(D.Info.Key, D.CurI()));
				Refresh();
			}
			catch (Exception e)
			{
				Gui.Err("choice", e);
			}
		}

		private void OnKeyClick()
		{
			try
			{
				Gui.BeginBind(this);
			}
			catch (Exception e)
			{
				Gui.Err("keybind", e);
			}
		}

		private void BuildSlider(float cx, float cw)
		{
			RectTransform sr = Mk.Rt("Slider", Rt);
			Mk.Px(sr, cx, (Theme.RowH - 20f) * 0.5f, cw, 20f);

			RectTransform bg = Mk.Rt("Bg", sr);
			bg.anchorMin = new Vector2(0f, 0.3f);
			bg.anchorMax = new Vector2(1f, 0.7f);
			bg.offsetMin = Vector2.zero;
			bg.offsetMax = Vector2.zero;
			Mk.Img(bg, Theme.Track, true);

			RectTransform fa = Mk.Rt("FillArea", sr);
			fa.anchorMin = new Vector2(0f, 0.3f);
			fa.anchorMax = new Vector2(1f, 0.7f);
			fa.offsetMin = new Vector2(8f, 0f);
			fa.offsetMax = new Vector2(-8f, 0f);
			RectTransform fill = Mk.Rt("Fill", fa);
			fill.sizeDelta = Vector2.zero;
			Mk.Img(fill, Theme.Teal, false);

			RectTransform ha = Mk.Rt("HandleArea", sr);
			ha.anchorMin = Vector2.zero;
			ha.anchorMax = Vector2.one;
			ha.offsetMin = new Vector2(8f, 0f);
			ha.offsetMax = new Vector2(-8f, 0f);
			RectTransform hd = Mk.Rt("Handle", ha);
			hd.anchorMin = new Vector2(0f, 0f);
			hd.anchorMax = new Vector2(0f, 1f);
			hd.sizeDelta = new Vector2(16f, 0f);
			Image hdImg = Mk.Img(hd, Theme.Yellow, true);

			Sld = sr.gameObject.AddComponent<Slider>();
			Sld.fillRect = fill;
			Sld.handleRect = hd;
			Sld.targetGraphic = hdImg;
			Sld.direction = Slider.Direction.LeftToRight;
			Sld.transition = Selectable.Transition.None;
			Mk.NoNav(Sld);
			Sld.minValue = D.Min;
			Sld.maxValue = D.Max;
			Sld.wholeNumbers = D.IsInt;
			Sld.value = D.CurF();
			Sld.onValueChanged.AddListener(new UnityAction<float>(OnSlider));
		}

		private void BuildField(float fx, float fw)
		{
			RectTransform fb = Mk.Box(Rt, "Value", Theme.Dim, Theme.Solid, 2f, true);
			Mk.Px(fb, fx, (Theme.RowH - 28f) * 0.5f, fw, 28f);
			FieldBorder = fb.GetComponent<Image>();
			RectTransform fill = (RectTransform)fb.GetChild(0);
			RectTransform tr = Mk.Rt("Text", fill);
			Mk.Stretch(tr, 3f, 0f, 3f, 0f);
			Text tx = Mk.Txt(tr, string.Empty, Theme.ValueFont, Theme.Yellow, TextAnchor.MiddleCenter);
			Field = fb.gameObject.AddComponent<InputField>();
			Field.targetGraphic = FieldBorder;
			Field.textComponent = tx;
			Mk.Caret(Field);
			Field.lineType = InputField.LineType.SingleLine;
			Field.characterLimit = 9;
			Field.transition = Selectable.Transition.None;
			Mk.NoNav(Field);
			Field.onValidateInput = new InputField.OnValidateInput(Validate);
			Field.onEndEdit.AddListener(new UnityAction<string>(OnEndEdit));
		}

		private void BuildReset(float w)
		{
			float rh = 26f;
			float rw = rh * 168f / 145f;
			RectTransform rr = Mk.Rt("Reset", Rt);
			Mk.Px(rr, w - rw, (Theme.RowH - rh) * 0.5f, rw, rh);
			Reset = Gui.MakeResetButton(rr, true);
			Reset.onClick.AddListener(new UnityAction(OnReset));
		}

		internal void Refresh()
		{
			Suppress = true;
			try
			{
				string nm = D.Name;
				if (_showSection)
				{
					nm = nm + "  [" + Gui.SectionTitle(D.Section) + "]";
				}
				if (D.Restart)
				{
					nm = Gui.WarnGlyph + " " + nm;
				}
				NameT.text = nm;
				NameT.color = D.Restart ? Theme.Red : (D.HasPend ? Theme.Teal : Theme.Yellow);
				if (D.IsFlag)
				{
					bool on = D.CurB();
					TogFill.color = on ? Theme.Teal : Theme.Solid;
					TogText.text = on ? Gui.Str("On", "ON") : Gui.Str("Off", "OFF");
					TogText.color = on ? Theme.Solid : Theme.Dim;
				}
				else if (Sld != null)
				{
					float cur = D.CurF();
					Sld.value = cur;
					if (!Field.isFocused)
					{
						Field.text = D.Fmt(cur);
					}
				}
				else if (KeyLabel != null)
				{
					if (D.Control == "choice")
					{
						KeyLabel.text = DisplayOptions.ChoiceText(D.Info.Key, D.CurI());
						KeyLabel.color = Theme.Text;
					}
					else
					{
						KeyLabel.text = Gui.KeyText(this, (int)D.CurF());
						KeyLabel.color = Gui.IsBinding(this) ? Theme.Yellow : Theme.Text;
					}
				}
			}
			finally
			{
				Suppress = false;
			}
		}

		// True only over the glyphs of the option name (not over the empty part of the row).
		internal bool OverName(Vector2 screen)
		{
			if (!RectTransformUtility.RectangleContainsScreenPoint(NameRt, screen, null))
			{
				return false;
			}
			Vector2 local;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(NameRt, screen, null, out local))
			{
				return false;
			}
			float w = NameRt.rect.width;
			float h = NameRt.rect.height;
			float tw = Mathf.Min(NameT.preferredWidth, w);
			float th = Mathf.Min(NameT.preferredHeight, h);
			return local.x >= 0f && local.x <= tw && Mathf.Abs(local.y + h * 0.5f) <= th * 0.5f;
		}

		internal void UpdateFocus()
		{
			if (Field == null)
			{
				return;
			}
			Color c = Field.isFocused ? Theme.TealHot : Theme.Dim;
			if (FieldBorder.color != c)
			{
				FieldBorder.color = c;
			}
		}

		internal void CommitIfFocused()
		{
			if (Field != null && Field.isFocused)
			{
				CommitText(Field.text);
			}
		}

		private void OnToggle()
		{
			try
			{
				D.StageB(!D.CurB());
				Gui.OnPendingChanged();
			}
			catch (Exception e)
			{
				Gui.Err("toggle", e);
			}
		}

		private void OnSlider(float v)
		{
			if (Suppress)
			{
				return;
			}
			try
			{
				D.StageFromSlider(v);
				Gui.OnPendingChanged();
			}
			catch (Exception e)
			{
				Gui.Err("slider", e);
			}
		}

		private void OnReset()
		{
			try
			{
				D.StageDefault();
				Gui.OnPendingChanged();
			}
			catch (Exception e)
			{
				Gui.Err("reset", e);
			}
		}

		private void OnEndEdit(string s)
		{
			try
			{
				CommitText(s);
				Gui.QueueDeselect();
			}
			catch (Exception e)
			{
				Gui.Err("endedit", e);
			}
		}

		// Digits only; one decimal separator for floats; a minus sign only at the start and only when Min < 0.
		private char Validate(string text, int charIndex, char c)
		{
			if (c >= '0' && c <= '9')
			{
				return c;
			}
			if (c == '-' && D.Min < 0f && charIndex == 0 && text.IndexOf('-') < 0)
			{
				return c;
			}
			if (!D.IsInt && (c == '.' || c == ',') && text.IndexOf('.') < 0 && text.IndexOf(',') < 0)
			{
				return c;
			}
			return '\0';
		}

		private void CommitText(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				Refresh();
				return;
			}
			bool ok;
			if (D.IsInt)
			{
				int iv;
				ok = int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out iv);
				if (ok)
				{
					D.StageI(iv);
				}
			}
			else
			{
				float fv;
				ok = float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out fv);
				if (ok && !float.IsNaN(fv))
				{
					D.StageF((float)Math.Round((double)fv, 4));
				}
				else
				{
					ok = false;
				}
			}
			if (!ok)
			{
				Refresh();
				return;
			}
			Gui.OnPendingChanged();
		}
	}

	internal sealed class View
	{
		internal RectTransform Root;
		internal readonly List<Row> Rows = new List<Row>();
		internal float Height;
	}

	internal sealed class GroupSpec
	{
		internal string Title = string.Empty;
		internal readonly List<OptDef> Items = new List<OptDef>();
	}

	// ---------- the panel ----------
	// In-game button box (top right, under the mission frame). Own overlay canvas below the interface; hidden by the gates of HideReason.
	// Layout in interface units of the 1280x720 reference: the box hangs from the right screen edge, its top is the bottom edge of MissionUI's Panel (read live) plus Gap.
	// Text above the earned-energy bar, on the panel's canvas: the panel itself clips what is above the bar.
	internal static class FpsCounter
	{
		internal const string Key = "NailUI.FpsCounter";
		private const int Size = 1024;
		private const float Window = 5f;
		private const float Refresh = 0.5f;
		private const float Lift = 8f;

		private static N.IntParam _mode;
		private static readonly float[] _times = new float[Size];
		private static readonly float[] _sorted = new float[Size];
		private static int _head;
		private static int _filled;
		private static float _nextText;
		private static int _shownMode;
		private static Text _text;
		private static GameObject _panel;

		internal static void Bind()
		{
			_mode = N.Config.BindInt(Key, 0, 0, 3, 1, "FPS counter above the energy bar: 0 off, 1 simple, 2 average, min, max, 3 also 1% low and frame time.");
		}

		internal static void Tick()
		{
			int mode = _mode.Value;
			if (mode <= 0)
			{
				if (_text != null && _shownMode != 0)
				{
					_text.gameObject.SetActive(false);
					_shownMode = 0;
				}
				return;
			}
			_times[_head] = Time.unscaledDeltaTime;
			_head = (_head + 1) % Size;
			if (_filled < Size)
			{
				_filled++;
			}
			float now = Time.unscaledTime;
			if (now < _nextText && mode == _shownMode)
			{
				return;
			}
			_nextText = now + Refresh;
			if (_text == null && !Build())
			{
				return;
			}
			bool panel = _panel != null && _panel.activeInHierarchy;
			if (_text.gameObject.activeSelf != panel)
			{
				_text.gameObject.SetActive(panel);
			}
			_shownMode = mode;
			Show(mode);
		}

		private static bool Build()
		{
			GameStatusUI.GameStatusUI window = GameStatusUI.GameStatusUI.Window;
			GameStatusUI.EnergyController energy = window == null ? null : window.energyContorller;
			if (energy == null || energy.ActiveControl == null || energy.EnergyFill == null || energy.InnerText == null)
			{
				return false;
			}
			GameObject go = new GameObject("NailFpsCounter");
			go.transform.SetParent(energy.InnerText.canvas.transform, false);
			go.transform.SetAsLastSibling();
			_panel = energy.ActiveControl;
			_text = go.AddComponent<Text>();
			_text.font = energy.InnerText.font;
			_text.fontSize = Mathf.Max(10, energy.InnerText.fontSize / 2);
			_text.color = energy.Orange;
			_text.alignment = TextAnchor.LowerLeft;
			_text.horizontalOverflow = HorizontalWrapMode.Overflow;
			_text.verticalOverflow = VerticalWrapMode.Overflow;
			_text.raycastTarget = false;
			RectTransform box = _text.rectTransform;
			box.anchorMin = new Vector2(0.5f, 0.5f);
			box.anchorMax = new Vector2(0.5f, 0.5f);
			box.pivot = new Vector2(0f, 0f);
			Vector3[] corners = new Vector3[4];
			energy.EnergyFill.rectTransform.GetWorldCorners(corners);
			box.position = corners[1];
			box.anchoredPosition = box.anchoredPosition + new Vector2(0f, Lift);
			N.ULog.Info("nailui", "fps counter built, font " + _text.fontSize + ", color " + _text.color);
			_shownMode = 0;
			return true;
		}

		private static void Show(int mode)
		{
			// Walks back over the last seconds of frames.
			int count = 0;
			int recent = 0;
			float total = 0f;
			float recentTotal = 0f;
			for (int i = 1; i <= _filled && total < Window; i++)
			{
				float t = _times[(_head - i + Size) % Size];
				_sorted[count++] = t;
				total += t;
				if (recentTotal < Refresh)
				{
					recentTotal += t;
					recent++;
				}
			}
			if (count == 0 || total <= 0f || recentTotal <= 0f)
			{
				return;
			}
			string line = Mathf.RoundToInt(recent / recentTotal) + " FPS";
			if (mode >= 2)
			{
				Array.Sort(_sorted, 0, count);
				line += "  avg " + Mathf.RoundToInt(count / total) + "  min " + Fps(_sorted[count - 1]) + "  max " + Fps(_sorted[0]);
				if (mode >= 3)
				{
					// The average of the slowest 1% of frames.
					int slow = Mathf.Max(1, count / 100);
					float slowTotal = 0f;
					for (int i = 1; i <= slow; i++)
					{
						slowTotal += _sorted[count - i];
					}
					line += "  1% " + Fps(slowTotal / slow) + "  " + (1000f * recentTotal / recent).ToString("F1", CultureInfo.InvariantCulture) + " ms";
				}
			}
			_text.text = line;
		}

		private static int Fps(float time)
		{
			return time <= 0f ? 0 : Mathf.RoundToInt(1f / time);
		}
	}

	// Hides agent titles and clerk names. Units come and go, so the lists are walked again every second or so.
	internal static class UnitLabels
	{
		private const int Every = 60;

		private static N.Flag _titles;
		private static N.Flag _clerks;
		private static bool _titlesHidden;
		private static bool _clerksHidden;

		internal static void Bind()
		{
			_titles = N.Config.BindFlag("NailUI.HideAgentTitles", false, "Hide the title text under agents.");
			_clerks = N.Config.BindFlag("NailUI.HideClerkNames", false, "Hide the name text of clerks.");
		}

		internal static void Tick()
		{
			if (Time.frameCount % Every != 0)
			{
				return;
			}
			if (_titles.On || _titlesHidden)
			{
				Titles(!_titles.On);
				_titlesHidden = _titles.On;
			}
			if (_clerks.On || _clerksHidden)
			{
				Clerks(!_clerks.On);
				_clerksHidden = _clerks.On;
			}
		}

		private static void Titles(bool show)
		{
			if (AgentManager.instance == null)
			{
				return;
			}
			IList<AgentModel> list = AgentManager.instance.GetAgentList();
			for (int i = 0; i < list.Count; i++)
			{
				AgentUnit unit = list[i].GetUnit();
				Transform root = unit == null || unit.agentUI == null || unit.agentUI.ActiveControl == null ? null : unit.agentUI.ActiveControl.transform.Find("TitleRoot");
				if (root != null && root.gameObject.activeSelf != show)
				{
					root.gameObject.SetActive(show);
				}
			}
		}

		// The game switches the name's object itself, so the component is what stays off.
		private static void Clerks(bool show)
		{
			if (OfficerManager.instance == null)
			{
				return;
			}
			IList<OfficerModel> list = OfficerManager.instance.GetOfficerList();
			for (int i = 0; i < list.Count; i++)
			{
				OfficerUnit unit = list[i].GetUnit();
				if (unit != null && unit.ui != null && unit.ui.Name != null && unit.ui.Name.enabled != show)
				{
					unit.ui.Name.enabled = show;
				}
			}
		}
	}

	internal static class HudBox
	{
		private const float Border = 2f;
		private const float Pad = 2f;
		// Extra space between two slots.
		private const float Gap = 6f;
		private const float Margin = 6f;
		private const float RightMargin = 2f;
		private const float Lift = 30f;
		private const float HeaderBottom = 48f;
		private const int Order = 30000;
		private const int MaxLogs = 40;

		private static N.Flag _on;
		private static N.FloatParam _size;
		private static GameObject _go;
		private static RectTransform _box;
		private static readonly List<RectTransform> _slots = new List<RectTransform>();
		private static readonly Vector3[] _corners = new Vector3[4];
		private static bool _built;
		private static bool _shown;
		private static bool _dead;
		private static int _errors;
		private static int _nextEval;
		private static int _lw;
		private static int _lh;
		private static float _laidSize = -1f;
		private static int _logs;
		private static int _placeLogs;
		private static int _lastMode = -1;
		private static int _clicks;
		private static StoryUI _story;
		private static int _storyNext;
		private static int _actVersion = -1;
		// Api 6 badges: one text per action slot (index = UiActions index), left of the slot.
		private static readonly List<N.BadgeView> _slotBadges = new List<N.BadgeView>();
		private static int _badgeVer = -1;
		private static int _badgeErrors;

		internal static void Bind()
		{
			_on = N.Config.BindFlag("NailUI.ButtonEnabled", true, "Show the interface button in the day screen, under the mission panel.");
			_size = N.Config.BindFloat("NailUI.ButtonSize", 20f, 12f, 96f, 2f, "Size of the button icon in the day screen (interface units of a 1280x720 screen).");
		}

		internal static bool Contains(Vector2 screen)
		{
			return _shown && _box != null && RectTransformUtility.RectangleContainsScreenPoint(_box, screen, null);
		}

		internal static void Shutdown()
		{
			_dead = true;
			_shown = false;
			if (_go != null)
			{
				_go.SetActive(false);
			}
		}

		// Called every frame by Gui.Tick. Hidden: a short check every 4th frame. Shown: gates every frame, placement every 8th frame or on change.
		internal static void Tick()
		{
			if (_dead || _on == null)
			{
				return;
			}
			int f = Time.frameCount;
			if (!_shown && f < _nextEval)
			{
				return;
			}
			_nextEval = f + 4;
			try
			{
				string why = _on.On ? HideReason() : "disabled";
				if (why != null)
				{
					if (_shown)
					{
						SetShown(false, why);
					}
					return;
				}
				if (!_built)
				{
					Build();
				}
				if (N.UiActions.Version != _actVersion)
				{
					SyncActions();
				}
				if (N.UiActions.BadgeVersion != _badgeVer)
				{
					SyncBadges();
				}
				bool fresh = !_shown;
				float size = _size.Value;
				if (fresh || size != _laidSize || Screen.width != _lw || Screen.height != _lh || (f & 7) == 0)
				{
					if (size != _laidSize)
					{
						Layout(size);
					}
					Place(MissionUI.instance);
					_lw = Screen.width;
					_lh = Screen.height;
				}
				if (fresh)
				{
					SetShown(true, null);
				}
				else if (!Input.GetMouseButton(0))
				{
					DeselectOurs();
				}
			}
			catch (Exception e)
			{
				Fail(e);
			}
		}

		private static void Fail(Exception e)
		{
			_errors++;
			if (_errors <= 3)
			{
				N.ULog.Error("nailui", "hud threw (" + _errors + "): " + e);
			}
			if (_errors >= 3)
			{
				Shutdown();
				N.ULog.Error("nailui", "hud button disabled after repeated errors; the hotkey and the title button still work");
			}
		}

		private static void SetShown(bool on, string why)
		{
			_go.SetActive(on);
			_shown = on;
			if (!on)
			{
				DeselectOurs();
			}
			if (_logs < MaxLogs)
			{
				_logs++;
				N.ULog.Info("nailui", on ? "hud shown" : "hud hidden (" + why + ")");
			}
		}

		// null = the box may be shown.
		private static string HideReason()
		{
			MissionUI mu = MissionUI.instance;
			if (mu == null || mu.anim == null || !mu.anim.gameObject.activeInHierarchy)
			{
				return "nohud";
			}
			GameManager g = GameManager.currentGameManager;
			if (g == null || g.state == GameState.STOP)
			{
				return "stop";
			}
			if (!SpeedHudOn())
			{
				return "noday";
			}
			string gate = Gui.BlockedReason(false);
			if (gate != null)
			{
				return gate;
			}
			if (CreatureOpen())
			{
				return "creature";
			}
			if (AgentOpen())
			{
				return "agent";
			}
			if (StoryActive())
			{
				return "story";
			}
			return null;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool SpeedHudOn()
		{
			try
			{
				global::GameStatusUI.GameStatusUI w = global::GameStatusUI.GameStatusUI.Window;
				return w != null && w.playSpeedSetting != null && w.playSpeedSetting.gameObject.activeInHierarchy;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool CreatureOpen()
		{
			try
			{
				CreatureInfoWindow w = CreatureInfoWindow.CurrentWindow;
				return w != null && w.IsEnabled;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool AgentOpen()
		{
			try
			{
				AgentInfoWindow w = AgentInfoWindow.currentWindow;
				return w != null && w.IsEnabled;
			}
			catch (Exception)
			{
				return false;
			}
		}

		// [Unverified] no cutscene was observed: StoryUI (no static instance) is searched every 1800 frames while nothing is cached.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool StoryActive()
		{
			try
			{
				if (_story == null)
				{
					if (Time.frameCount < _storyNext)
					{
						return false;
					}
					_storyNext = Time.frameCount + 1800;
					_story = UnityEngine.Object.FindObjectOfType<StoryUI>();
				}
				return _story != null && _story.gameObject.activeInHierarchy;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void Build()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			_go = new GameObject("NailUiHud", typeof(RectTransform));
			_go.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(_go);
			Canvas canvas = _go.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = Order;
			CanvasScaler scaler = _go.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1280f, 720f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0.5f;
			_go.AddComponent<GraphicRaycaster>();
			_box = Mk.Box(_go.transform, "Box", Theme.Orange, Theme.Solid, Border, true);
			_box.anchorMin = new Vector2(1f, 1f);
			_box.anchorMax = new Vector2(1f, 1f);
			_box.pivot = new Vector2(1f, 1f);
			Sprite icon = Gui.IconSprite();
			AddSlot("Settings", icon, OnSettingsClick);
			_go.SetActive(false);
			_built = true;
			N.ULog.Info("nailui", "hud built in " + sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms icon=" + (icon != null ? "ok" : "missing"));
		}

		private static void AddSlot(string name, Sprite icon, UnityAction click)
		{
			RectTransform rt = Mk.Rt(name, _box);
			Image img = Mk.Img(rt, Color.white, true);
			if (icon != null)
			{
				img.sprite = icon;
				img.preserveAspect = true;
			}
			Button b = rt.gameObject.AddComponent<Button>();
			Color normal = new Color(0.88f, 0.88f, 0.88f, 1f);
			Mk.Tint(b, img, normal, new Color(1f, 0.78f, 0.55f, 1f), Theme.Orange, normal);
			b.onClick.AddListener(click);
			_slots.Add(rt);
		}

		// Api 5: one slot per UiActions entry under Settings; rebuilt when the registry changes.
		private static void SyncActions()
		{
			_actVersion = N.UiActions.Version;
			_slotBadges.Clear();
			_badgeVer = -1;
			for (int i = _slots.Count - 1; i >= 1; i--)
			{
				UnityEngine.Object.Destroy(_slots[i].gameObject);
				_slots.RemoveAt(i);
			}
			int n = N.UiActions.Count;
			for (int i = 0; i < n; i++)
			{
				string rel = N.UiActions.GetIcon(i);
				Sprite icon = string.IsNullOrEmpty(rel) ? null : Gui.LoadSprite(rel);
				if (icon == null)
				{
					icon = Gui.IconSprite();
				}
				AddSlot("Action_" + N.UiActions.GetId(i), icon, new UnityAction(new ActionClick(i).Run));
				N.UiActions.SetArea(_slots[_slots.Count - 1], null);
			}
			_laidSize = -1f;
			N.ULog.Info("nailui", "hud action slots=" + n);
		}

		// "N<sign> M<sign>" (warn yellow, error red) left of the action slot; hidden at 0/0.
		private static void SyncBadges()
		{
			_badgeVer = N.UiActions.BadgeVersion;
			try
			{
				int n = N.UiActions.Count;
				for (int i = 0; i < n && i + 1 < _slots.Count; i++)
				{
					while (_slotBadges.Count <= i)
					{
						_slotBadges.Add(null);
					}
					int warn;
					int err;
					N.UiActions.GetBadges(i, out warn, out err);
					N.BadgeView b = _slotBadges[i];
					if (b == null)
					{
						if (warn <= 0 && err <= 0)
						{
							continue;
						}
						RectTransform br = Mk.Rt("Badge", _slots[i + 1]);
						br.anchorMin = new Vector2(0f, 0.5f);
						br.anchorMax = new Vector2(0f, 0.5f);
						br.pivot = new Vector2(1f, 0.5f);
						br.anchoredPosition = new Vector2(-Border - Pad - 2f, 0f);
						br.sizeDelta = new Vector2(120f, 24f);
						// Gui.Font is set when the panel opens first (SyncFont); until then the game TITLE font, Arial last.
						Font f = Gui.Font != null ? Gui.Font : Gui.TitleFont();
						if (f == null)
						{
							f = Resources.GetBuiltinResource<Font>("Arial.ttf");
						}
						b = N.UiActions.CreateBadge(br, f, 16, true);
						_slotBadges[i] = b;
						N.ULog.Info("nailui", "hud badge created for " + N.UiActions.GetId(i) + " font=" + f.name);
					}
					b.Set(warn, err);
				}
			}
			catch (Exception e)
			{
				if (_badgeErrors < 3)
				{
					_badgeErrors++;
					N.ULog.Error("nailui", "hud badge update failed: " + e);
				}
			}
		}

		private sealed class ActionClick
		{
			private readonly int _index;

			internal ActionClick(int index)
			{
				_index = index;
			}

			// Only one window at a time: the panel closes before the action's window opens.
			internal void Run()
			{
				try
				{
					if (Gui.IsOpen)
					{
						Gui.Toggle();
					}
					N.UiActions.Invoke(_index);
				}
				catch (Exception e)
				{
					Gui.Err("hud action click", e);
				}
				DeselectOurs();
			}
		}

		private static void Layout(float size)
		{
			int n = _slots.Count;
			float gaps = n > 1 ? (n - 1) * Gap : 0f;
			_box.sizeDelta = new Vector2(size + 2f * (Border + Pad), 2f * Border + Pad + n * (size + Pad) + gaps);
			for (int i = 0; i < n; i++)
			{
				Mk.Px(_slots[i], Border + Pad, Border + Pad + i * (size + Pad + Gap), size, size);
			}
			_laidSize = size;
		}

		// Missions listed: top of the box = bottom of the lowest active list entry plus Margin, never below the mission frame
		// (fixed size 175x250 at scale 1.5). No active entry found: frame bottom minus Lift.
		private static void Place(MissionUI mu)
		{
			RectTransform frame = mu.anim.transform as RectTransform;
			if (frame == null)
			{
				return;
			}
			frame.GetWorldCorners(_corners);
			Camera cam = null;
			Canvas c = frame.GetComponentInParent<Canvas>();
			if (c != null)
			{
				Canvas r = c.rootCanvas;
				if (r.renderMode != RenderMode.ScreenSpaceOverlay)
				{
					cam = r.worldCamera;
				}
			}
			Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
			float scale = Mathf.Sqrt((Screen.width / 1280f) * (Screen.height / 720f));
			// Collapsed panel or an empty list: right under the visible header. Missions listed: under the last entry, so the list is never covered.
			int children = mu.listParent != null ? mu.listParent.childCount : -1;
			bool expanded = true;
			try
			{
				expanded = mu.anim.GetBool("Appear");
			}
			catch (Exception)
			{
			}
			bool high = !expanded || children == 0;
			float listBottom = -1f;
			if (!high)
			{
				float minY = float.MaxValue;
				for (int i = 0; i < children; i++)
				{
					RectTransform ch = mu.listParent.GetChild(i) as RectTransform;
					if (ch == null || !ch.gameObject.activeInHierarchy)
					{
						continue;
					}
					ch.GetWorldCorners(_corners);
					float y = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]).y;
					if (y < minY)
					{
						minY = y;
					}
				}
				if (minY != float.MaxValue)
				{
					listBottom = Mathf.Max(minY, bl.y);
				}
			}
			float top;
			if (high)
			{
				top = HeaderBottom;
			}
			else if (listBottom >= 0f)
			{
				top = (Screen.height - listBottom) / scale + Margin;
			}
			else
			{
				top = (Screen.height - bl.y) / scale - Lift;
			}
			float maxTop = Screen.height / scale - _box.sizeDelta.y;
			if (top > maxTop)
			{
				top = maxTop;
			}
			if (top < 0f)
			{
				top = 0f;
			}
			_box.anchoredPosition = new Vector2(-RightMargin, -top);
			int mode = high ? 1 : 0;
			if (_placeLogs < 12 && (_placeLogs < 3 || mode != _lastMode))
			{
				_placeLogs++;
				N.ULog.Info("nailui", "hud placed mode=" + (high ? "header" : "frame") + " appear=" + expanded + " children=" + children + " top=" + top.ToString("F1", CultureInfo.InvariantCulture) + " frameBottomPx=" + bl.y.ToString("F0", CultureInfo.InvariantCulture) + " listBottomPx=" + listBottom.ToString("F0", CultureInfo.InvariantCulture) + " scale=" + scale.ToString("F3", CultureInfo.InvariantCulture) + " box=" + _box.sizeDelta.x.ToString("F0", CultureInfo.InvariantCulture) + "x" + _box.sizeDelta.y.ToString("F0", CultureInfo.InvariantCulture) + " screen=" + Screen.width + "x" + Screen.height);
			}
			_lastMode = mode;
		}

		private static void OnSettingsClick()
		{
			try
			{
				if (_clicks < 10)
				{
					_clicks++;
					N.ULog.Info("nailui", "hud click -> toggle (panel open=" + Gui.IsOpen + ")");
				}
				Gui.Toggle();
			}
			catch (Exception e)
			{
				Gui.Err("hud click", e);
			}
			DeselectOurs();
		}

		// A selected button would react to the game's Submit (Space) later: never keep the focus on the box.
		private static void DeselectOurs()
		{
			EventSystem es = EventSystem.current;
			if (es == null)
			{
				return;
			}
			GameObject sel = es.currentSelectedGameObject;
			if (sel == null)
			{
				return;
			}
			for (int i = 0; i < _slots.Count; i++)
			{
				if (sel == _slots[i].gameObject)
				{
					es.SetSelectedGameObject(null);
					return;
				}
			}
		}
	}

	// Message window for other modules (LoadGuard load alerts), called by reflection: NailUI.AlertBox.Show(title, body).
	// Own overlay canvas above the settings panel; styled like the restart notice. OK or Escape closes it.
	public static class AlertBox
	{
		private static GameObject _go;
		private static Text _title;
		private static Text _body;

		public static bool Show(string title, string body)
		{
			try
			{
				if (_go == null)
				{
					Build();
				}
				_title.text = title;
				_body.text = body;
				_go.SetActive(true);
				return true;
			}
			catch (Exception e)
			{
				N.ULog.Error("nailui", "alert box failed: " + e);
				return false;
			}
		}

		public static bool IsOpen
		{
			get
			{
				return _go != null && _go.activeSelf;
			}
		}

		public static void Close()
		{
			if (_go != null)
			{
				_go.SetActive(false);
			}
		}

		private static void Build()
		{
			if (Gui.Font == null)
			{
				Gui.SyncFont();
			}
			_go = new GameObject("NailAlertBox", typeof(RectTransform));
			_go.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(_go);
			Canvas canvas = _go.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30005;
			CanvasScaler scaler = _go.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0.5f;
			_go.AddComponent<GraphicRaycaster>();
			RectTransform root = (RectTransform)_go.transform;
			RectTransform dim = Mk.Rt("Dim", root);
			Mk.Stretch(dim, 0f, 0f, 0f, 0f);
			Mk.Img(dim, new Color(0f, 0f, 0f, 0.72f), true);
			RectTransform box = Mk.Box(dim, "Box", Theme.Red, Theme.Solid, 3f, true);
			box.anchorMin = new Vector2(0.5f, 0.5f);
			box.anchorMax = new Vector2(0.5f, 0.5f);
			box.pivot = new Vector2(0.5f, 0.5f);
			box.anchoredPosition = Vector2.zero;
			box.sizeDelta = new Vector2(1000f, 600f);
			RectTransform tr = Mk.Rt("Title", box);
			Mk.Px(tr, 24f, 16f, 952f, 44f);
			_title = Mk.Txt(tr, string.Empty, 28, Theme.Red, TextAnchor.MiddleLeft);
			_title.supportRichText = true;
			Mk.Fit(_title, 16);
			RectTransform br = Mk.Rt("Body", box);
			Mk.Px(br, 24f, 70f, 952f, 440f);
			_body = Mk.Txt(br, string.Empty, 22, Theme.Yellow, TextAnchor.UpperLeft);
			_body.supportRichText = true;
			_body.horizontalOverflow = HorizontalWrapMode.Wrap;
			Mk.Fit(_body, 12);
			RectTransform b = Mk.Box(box, "OK", Theme.Teal, Theme.Solid, 3f, true);
			Mk.Px(b, 390f, 528f, 220f, 48f);
			Image bi = b.GetComponent<Image>();
			Button btn = b.gameObject.AddComponent<Button>();
			Mk.Tint(btn, bi, Theme.Teal, Theme.TealHot, Theme.TealDark, Theme.TealDark);
			btn.onClick.AddListener(new UnityAction(Close));
			RectTransform lr = Mk.Rt("Label", b);
			Mk.Stretch(lr, 4f, 2f, 4f, 2f);
			Text label = Mk.Txt(lr, "OK", Theme.ButtonFont, Theme.Yellow, TextAnchor.MiddleCenter);
			Mk.Fit(label, 14);
			_go.SetActive(false);
		}
	}

	internal static class Gui
	{
		internal static Font Font;
		internal static readonly List<Text> Texts = new List<Text>();
		// Always "(!)": the TITLE font reports U+26A0 but does not draw it (run 10).
		internal static string WarnGlyph = "(!)";

		private const string IconPath = "Images/config_icon.png";
		private const string ResetPath = "Images/reset.png";

		private static N.Flag _tipsFlag;
		private static N.FloatParam _tipDelay;
		private static N.Flag _force;
		private static N.Flag _hotOn;
		private static N.IntParam _hotKey;
		private static N.Counter _cOpens;
		private static N.Counter _cApplies;
		private static N.Counter _cKeys;

		private static GameObject _root;
		private static RectTransform _rootRt;
		private static RectTransform _panelRt;
		private static Pane _left;
		private static Pane _right;
		private static InputField _search;
		private static Image _searchBorder;
		private static Text _searchHint;
		private static Text _statusText;
		private static Text _modsText;
		private static Text _apiText;
		private static Button _applyBtn;
		private static Text _applyLabel;
		private static Text _doneLabel;
		private static RectTransform _tipRt;
		private static Text _tipText;
		private static RectTransform _confirm;
		private static Text _confirmText;
		private static Text _yesLabel;
		private static Text _noLabel;
		// Restart notice after Apply: same layout as the confirmation, one OK button.
		private static RectTransform _notice;
		private static Text _noticeText;
		private static Text _okLabel;
		private static readonly List<string> _restartNames = new List<string>();

		private static bool _built;
		private static bool _open;
		private static bool _dead;
		private static int _errors;
		private static int _openFrame;
		private static int _seenVersion;
		private static int _deselectAt;
		private static bool _suppressSearch;
		private static bool _needRestart;

		private static LocData _loc;
		private static string _lang = string.Empty;
		private static Font _arial;
		private static Sprite _icon;
		private static bool _iconTried;
		private static Sprite _reset;
		private static bool _resetTried;
		private static bool _fontLogged;

		private static readonly List<N.OptionInfo> _infos = new List<N.OptionInfo>();
		private static readonly List<OptDef> _defs = new List<OptDef>();
		private static readonly List<SecDef> _secs = new List<SecDef>();
		private static readonly Dictionary<string, View> _views = new Dictionary<string, View>(StringComparer.Ordinal);
		private static View _searchView;
		private static View _active;
		private static SecDef _current;
		private static string _query = string.Empty;
		private static int _infoCount = -1;

		// hover tooltip
		private static Row _hoverRow;
		private static float _hoverT;
		private static bool _tipShown;

		// game state (open gate, Escape)
		private static LoadingScreen _ls;
		private static int _lsNextFind;
		private static int _escClosedFrame = -1;
		private static Row _bindRow;
		private static readonly List<KeyCode> _bindKeys = new List<KeyCode>();
		private static int _blockLogs;
		private static int _typeBlockLogs;
		private static int _gateLogs;

		// probes
		private static bool _typing;
		private static bool _wasTyping;

		// A NailUI field or key capture has focus (read by the CameraMover.Update guard).
		internal static bool Typing
		{
			get
			{
				return _typing;
			}
		}
		private static float _tsAtFocus;
		private static int _typingLogs;
		private static int _wheelLogs;

		internal static bool IsOpen
		{
			get
			{
				return _open;
			}
		}

		// The panel is open and the cursor is over it (read by the CameraMover.Update guard on wheel frames).
		internal static bool WheelOverPanel()
		{
			return _open && _panelRt != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, null);
		}

		internal static void BindOptions()
		{
			_tipsFlag = N.Config.BindFlag("NailUI.Tooltips", true, "Show a description tooltip next to the mouse when it rests on an option.");
			_tipDelay = N.Config.BindFloat("NailUI.TooltipDelay", 0.4f, 0f, 2f, 0.1f, "Seconds the mouse must rest on an option before its tooltip appears.");
			_hotOn = N.Config.BindFlag("NailUI.HotkeyEnabled", true, "Open and close the interface with the hotkey. Off: the interface opens only with the title screen button.");
			_hotKey = N.Config.BindInt("NailUI.Hotkey", (int)N.UiHost.Hotkey, 1, 322, 1, "UnityEngine.KeyCode value of the interface hotkey. Change it in the interface (Interface section); default = Gui.Hotkey of the config.");
			_force = N.Config.BindFlag("NailUI.ForceOpen", false, "Debug: the hotkey opens the interface even during loading, day transitions and the game's settings screen.");
			DisplayOptions.Bind();
			HudBox.Bind();
			FpsCounter.Bind();
			UnitLabels.Bind();
			_cOpens = N.Counters.Get("NailUI", "opens");
			_cApplies = N.Counters.Get("NailUI", "applies");
			_cKeys = N.Counters.Get("NailUI", "appliedKeys");
		}

		internal static void Err(string where, Exception e)
		{
			_errors++;
			if (_errors <= 5)
			{
				N.ULog.Error("nailui", where + " threw (" + _errors + "): " + e);
			}
		}

		// ---------- hotkey binding ----------
		internal static bool IsBinding(Row r)
		{
			return object.ReferenceEquals(_bindRow, r);
		}

		internal static string KeyText(Row r, int code)
		{
			if (r != null && object.ReferenceEquals(_bindRow, r))
			{
				return Str("PressKey", "Press a key... (Esc = cancel)");
			}
			if (Enum.IsDefined(typeof(KeyCode), code))
			{
				return ((KeyCode)code).ToString();
			}
			return code.ToString(CultureInfo.InvariantCulture);
		}

		internal static void BeginBind(Row r)
		{
			if (_bindKeys.Count == 0)
			{
				Array all = Enum.GetValues(typeof(KeyCode));
				for (int i = 0; i < all.Length; i++)
				{
					KeyCode k = (KeyCode)all.GetValue(i);
					int v = (int)k;
					string nm = k.ToString();
					if (v < 1 || v > 322 || k == KeyCode.Escape || nm.StartsWith("Mouse", StringComparison.Ordinal) || nm.StartsWith("Joystick", StringComparison.Ordinal))
					{
						continue;
					}
					_bindKeys.Add(k);
				}
			}
			Row old = _bindRow;
			_bindRow = r;
			if (old != null)
			{
				old.Refresh();
			}
			r.Refresh();
		}

		private static void EndBind()
		{
			Row old = _bindRow;
			_bindRow = null;
			if (old != null)
			{
				old.Refresh();
			}
		}

		// true = the frame was consumed (the Escape press that cancels binding must not close the panel).
		private static bool ProcessBind()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				EndBind();
				return true;
			}
			if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
			{
				EndBind();
				return false;
			}
			for (int i = 0; i < _bindKeys.Count; i++)
			{
				if (Input.GetKeyDown(_bindKeys[i]))
				{
					OptDef d = _bindRow.D;
					d.StageI((int)_bindKeys[i]);
					EndBind();
					OnPendingChanged();
					return true;
				}
			}
			return false;
		}

		// ---------- defaults ----------
		internal static void ResetItems(List<OptDef> items)
		{
			try
			{
				for (int i = 0; i < items.Count; i++)
				{
					items[i].StageDefault();
				}
				OnPendingChanged();
			}
			catch (Exception e)
			{
				Err("reset group", e);
			}
		}

		internal static void ResetAll()
		{
			ResetItems(_defs);
		}

		// ---------- confirmation dialog for "reset all" ----------
		internal static void AskResetAll()
		{
			HideTip();
			_confirm.SetAsLastSibling();
			_confirm.gameObject.SetActive(true);
		}

		// Also hides the restart notice: both dialogs share Escape, wheel and tooltip handling.
		private static void HideConfirm()
		{
			if (_confirm != null && _confirm.gameObject.activeSelf)
			{
				_confirm.gameObject.SetActive(false);
			}
			if (_notice != null && _notice.gameObject.activeSelf)
			{
				_notice.gameObject.SetActive(false);
			}
		}

		private static bool ConfirmOpen()
		{
			return (_confirm != null && _confirm.gameObject.activeSelf) || (_notice != null && _notice.gameObject.activeSelf);
		}

		// Lists the applied options that take effect after a game restart (names as shown in the panel).
		private static void ShowRestartNotice()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(Str("RestartNotice", "These options take effect after a game restart:"));
			sb.Append('\n');
			for (int i = 0; i < _restartNames.Count; i++)
			{
				sb.Append("\n<color=#ff5c4d>- ").Append(_restartNames[i]).Append("</color>");
			}
			_noticeText.text = sb.ToString();
			HideTip();
			_notice.SetAsLastSibling();
			_notice.gameObject.SetActive(true);
			N.ULog.Info("nailui", "restart notice shown options=" + _restartNames.Count);
		}

		private static void OnNoticeOk()
		{
			HideConfirm();
		}

		private static void OnConfirmYes()
		{
			try
			{
				HideConfirm();
				ResetAll();
			}
			catch (Exception e)
			{
				Err("confirm yes", e);
			}
		}

		private static void OnConfirmNo()
		{
			HideConfirm();
		}

		private static Button DialogButton(RectTransform box, string name, float x, Color normal, Color high, Color pressed, out Text label)
		{
			RectTransform b = Mk.Box(box, name, normal, Theme.Solid, 3f, true);
			Mk.Px(b, x, 168f, 220f, 44f);
			Image bi = b.GetComponent<Image>();
			Button btn = b.gameObject.AddComponent<Button>();
			Mk.Tint(btn, bi, normal, high, pressed, pressed);
			RectTransform lr = Mk.Rt("Label", b);
			Mk.Stretch(lr, 4f, 2f, 4f, 2f);
			label = Mk.Txt(lr, name, Theme.ButtonFont, Theme.Yellow, TextAnchor.MiddleCenter);
			Mk.Fit(label, 14);
			return btn;
		}

		private static void BuildConfirm()
		{
			_confirm = Mk.Rt("Confirm", _panelRt);
			Mk.Stretch(_confirm, 0f, 0f, 0f, 0f);
			Mk.Img(_confirm, new Color(0f, 0f, 0f, 0.72f), true);
			RectTransform box = Mk.Box(_confirm, "Box", Theme.Red, Theme.Solid, 3f, true);
			box.anchorMin = new Vector2(0.5f, 0.5f);
			box.anchorMax = new Vector2(0.5f, 0.5f);
			box.pivot = new Vector2(0.5f, 0.5f);
			box.anchoredPosition = Vector2.zero;
			box.sizeDelta = new Vector2(560f, 230f);
			RectTransform tr = Mk.Rt("Text", box);
			Mk.Px(tr, 20f, 16f, 520f, 140f);
			_confirmText = Mk.Txt(tr, string.Empty, 20, Theme.Yellow, TextAnchor.UpperLeft);
			_confirmText.supportRichText = true;
			Mk.Fit(_confirmText, 12);
			Button yes = DialogButton(box, "Yes", 40f, Theme.Red, new Color(1f, 0.6f, 0.55f, 1f), new Color(0.5f, 0.15f, 0.12f, 1f), out _yesLabel);
			yes.onClick.AddListener(new UnityAction(OnConfirmYes));
			Button no = DialogButton(box, "No", 300f, Theme.Teal, Theme.TealHot, Theme.TealDark, out _noLabel);
			no.onClick.AddListener(new UnityAction(OnConfirmNo));
			_confirm.gameObject.SetActive(false);
		}

		private static void BuildNotice()
		{
			_notice = Mk.Rt("RestartNotice", _panelRt);
			Mk.Stretch(_notice, 0f, 0f, 0f, 0f);
			Mk.Img(_notice, new Color(0f, 0f, 0f, 0.72f), true);
			RectTransform box = Mk.Box(_notice, "Box", Theme.Red, Theme.Solid, 3f, true);
			box.anchorMin = new Vector2(0.5f, 0.5f);
			box.anchorMax = new Vector2(0.5f, 0.5f);
			box.pivot = new Vector2(0.5f, 0.5f);
			box.anchoredPosition = Vector2.zero;
			box.sizeDelta = new Vector2(560f, 230f);
			RectTransform tr = Mk.Rt("Text", box);
			Mk.Px(tr, 20f, 16f, 520f, 140f);
			_noticeText = Mk.Txt(tr, string.Empty, 20, Theme.Yellow, TextAnchor.UpperLeft);
			_noticeText.supportRichText = true;
			Mk.Fit(_noticeText, 12);
			Button ok = DialogButton(box, "OK", 170f, Theme.Teal, Theme.TealHot, Theme.TealDark, out _okLabel);
			ok.onClick.AddListener(new UnityAction(OnNoticeOk));
			_notice.gameObject.SetActive(false);
		}

		internal static Button MakeResetButton(RectTransform rr, bool usePng)
		{
			Image ri = Mk.Img(rr, Color.white, true);
			Button b = rr.gameObject.AddComponent<Button>();
			Sprite sp = usePng ? ResetSprite() : null;
			if (sp != null)
			{
				ri.sprite = sp;
				ri.preserveAspect = true;
				Mk.Tint(b, ri, new Color(0.85f, 0.85f, 0.85f, 1f), Color.white, new Color(0.6f, 0.6f, 0.6f, 1f), new Color(0.4f, 0.4f, 0.4f, 1f));
			}
			else
			{
				RectTransform lr = Mk.Rt("L", rr);
				Mk.Stretch(lr, 0f, 0f, 0f, 0f);
				Mk.Txt(lr, "R", 14, Theme.Solid, TextAnchor.MiddleCenter);
				Mk.Tint(b, ri, Theme.Teal, Theme.TealHot, Theme.TealDark, Theme.TealDark);
			}
			return b;
		}

		// ---------- open / close ----------
		internal static void Toggle()
		{
			if (_dead)
			{
				throw new InvalidOperationException("NailUI disabled after repeated errors");
			}
			if (_open)
			{
				Close("toggle");
				return;
			}
			string why = BlockedReason(true);
			if (why != null)
			{
				if (!_force.On)
				{
					if (_gateLogs < 8)
					{
						_gateLogs++;
						N.ULog.Info("nailui", "open blocked (" + why + "): " + StateProbe() + "; NailUI.ForceOpen=true overrides");
					}
					return;
				}
				N.ULog.Info("nailui", "forced open despite " + why + ": " + StateProbe());
			}
			Open();
		}

		// null = the interface may open now.
		internal static string BlockedReason(bool refresh)
		{
			if (LoadingActive(refresh))
			{
				return "loading";
			}
			if (OptionsActive())
			{
				return "options";
			}
			if (EscMenuActive())
			{
				return "escmenu";
			}
			return null;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool LoadingActive(bool refresh)
		{
			try
			{
				if (_ls == null && (refresh || Time.frameCount >= _lsNextFind))
				{
					_lsNextFind = Time.frameCount + 300;
					_ls = UnityEngine.Object.FindObjectOfType<LoadingScreen>();
				}
				return _ls != null && _ls.isLoading;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool OptionsActive()
		{
			try
			{
				OptionUI ui = OptionUI.Instance;
				return ui != null && ui.IsEnabled;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool EscMenuActive()
		{
			try
			{
				EscapeUI e = EscapeUI.instance;
				return e != null && e.IsOpened;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string StateProbe()
		{
			string gm = "none";
			string scene = string.Empty;
			try
			{
				GameManager g = GameManager.currentGameManager;
				if (g != null)
				{
					gm = g.state.ToString();
				}
			}
			catch (Exception)
			{
			}
			try
			{
				scene = SceneManager.GetActiveScene().name;
			}
			catch (Exception)
			{
			}
			return "loading=" + (_ls != null && _ls.isLoading) + " options=" + OptionsActive() + " escmenu=" + EscMenuActive() + " gm=" + gm + " scene=" + scene;
		}

		// PlaySpeedSettingUI.Update Prefix: true = skip this frame's Update. Always skipped while a NailUI field or key capture has focus (GL-50: Space would pause the game). Only on the frame of an Escape press that belongs
		// to the panel: the panel is open now (Update ran first) or was closed by this very Escape (Tick ran first).
		internal static bool BlockEscapeFrame()
		{
			if (!N.UiHost.Registered)
			{
				return false;
			}
			if (_typing)
			{
				if (_typeBlockLogs < 3)
				{
					_typeBlockLogs++;
					N.ULog.Info("nailui", "PlaySpeedSettingUI.Update skipped while typing");
				}
				return true;
			}
			if (!Input.GetKeyDown(KeyCode.Escape))
			{
				return false;
			}
			if (!_open && _escClosedFrame != Time.frameCount)
			{
				return false;
			}
			if (_blockLogs < 4)
			{
				_blockLogs++;
				N.ULog.Info("nailui", "PlaySpeedSettingUI.Update skipped on Escape (panel open=" + _open + ")");
			}
			return true;
		}

		private static void Open()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			float buildMs = 0f;
			if (!_built)
			{
				try
				{
					SyncFont();
					BuildSkeleton();
				}
				catch (Exception)
				{
					Teardown();
					throw;
				}
				buildMs = (float)sw.Elapsed.TotalMilliseconds;
			}
			SyncLanguageAndData();
			PruneTexts();
			ClearAllPending();
			HideConfirm();
			_root.SetActive(true);
			_open = true;
			_openFrame = Time.frameCount;
			_seenVersion = N.Options.Version;
			_query = string.Empty;
			SetSearchText(string.Empty);
			UpdateHint();
			SelectDefault();
			RefreshChrome();
			_cOpens.Inc();
			N.ULog.Info("nailui", "opened in " + sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms (skeleton " + buildMs.ToString("F1", CultureInfo.InvariantCulture) + " ms) lang=" + _lang + " options=" + _defs.Count + " " + StateProbe());
		}

		private static void Close(string why)
		{
			int pend = PendingCount();
			_open = false;
			_typing = false;
			_bindRow = null;
			HideConfirm();
			HideTip();
			ClearAllPending();
			if (_root != null)
			{
				SetSearchText(string.Empty);
				_root.SetActive(false);
			}
			_query = string.Empty;
			DeselectOwn();
			GameHooks.ReleaseCamera();
			N.ULog.Info("nailui", "closed (" + why + ") discarded=" + pend);
		}

		// Texts of destroyed search rows stay in the list; SyncFont prunes only when the font changes.
		private static void PruneTexts()
		{
			for (int i = Texts.Count - 1; i >= 0; i--)
			{
				if (Texts[i] == null)
				{
					Texts.RemoveAt(i);
				}
			}
		}

		private static void Teardown()
		{
			_open = false;
			_built = false;
			if (_root != null)
			{
				UnityEngine.Object.Destroy(_root);
			}
			_root = null;
			_rootRt = null;
			_panelRt = null;
			_left = null;
			_right = null;
			_search = null;
			_active = null;
			_searchView = null;
			_views.Clear();
			Texts.Clear();
		}

		// ---------- font and language ----------
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string LangCode()
		{
			try
			{
				GlobalGameManager.LanguageFont lf = GlobalGameManager.currentLanguageFont;
				if (!object.ReferenceEquals(lf, null) && !string.IsNullOrEmpty(lf.language))
				{
					return lf.language.Trim().ToLowerInvariant();
				}
			}
			catch (Exception)
			{
			}
			return "en";
		}

		private static bool LanguageOrFontChanged()
		{
			if (!string.Equals(LangCode(), _lang, StringComparison.Ordinal))
			{
				return true;
			}
			Font tf = TitleFont();
			return tf != null && tf != Font;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string LangProbe()
		{
			try
			{
				GlobalGameManager g = GlobalGameManager.instance;
				if (!object.ReferenceEquals(g, null))
				{
					return g.GetCurrentLanguage() + " / " + g.Language.ToString();
				}
			}
			catch (Exception e)
			{
				return "probe failed: " + e.GetType().Name;
			}
			return "no instance";
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static Font TitleFont()
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
				N.ULog.Warn("nailui", "TITLE font lookup failed: " + e.GetType().Name);
			}
			return null;
		}

		internal static void SyncFont()
		{
			Font f = TitleFont();
			if (f == null)
			{
				if (_arial == null)
				{
					_arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
				}
				f = _arial;
			}
			if (f == Font)
			{
				return;
			}
			Font = f;
			WarnGlyph = "(!)";
			if (!_fontLogged)
			{
				_fontLogged = true;
				N.ULog.Info("nailui", "font=" + (Font != null ? Font.name : "null") + " dynamic=" + Font.dynamic + " warnGlyph=" + Font.HasCharacter('\u26A0') + " cyrillic(\u042F\u044F)=" + Font.HasCharacter('\u042F') + "/" + Font.HasCharacter('\u044F') + " lang=" + LangCode() + " game=" + LangProbe());
			}
			for (int i = Texts.Count - 1; i >= 0; i--)
			{
				Text t = Texts[i];
				if (t == null)
				{
					Texts.RemoveAt(i);
				}
				else
				{
					t.font = Font;
				}
			}
		}

		internal static string Str(string id, string fallback)
		{
			string s;
			if (_loc != null && _loc.Ui.TryGetValue(id, out s) && s.Length > 0)
			{
				return FixGlyph(s);
			}
			return fallback;
		}

		private static string FixGlyph(string s)
		{
			if (WarnGlyph != "\u26A0" && s.IndexOf('\u26A0') >= 0)
			{
				return s.Replace("\u26A0", WarnGlyph);
			}
			return s;
		}

		internal static string SectionTitle(string id)
		{
			string s;
			if (_loc != null && _loc.SecTitle.TryGetValue(id, out s))
			{
				return s;
			}
			return id;
		}

		private static string GroupTitle(string id)
		{
			string s;
			if (_loc != null && _loc.Groups.TryGetValue(id, out s))
			{
				return s;
			}
			return id;
		}

		// Reloads localization/data when the game language, the font or the declared options changed.
		private static void SyncLanguageAndData()
		{
			SyncFont();
			string lang = LangCode();
			bool langChanged = _loc == null || !string.Equals(lang, _lang, StringComparison.Ordinal);
			if (langChanged)
			{
				_loc = Loc.Load(lang);
				_lang = lang;
				N.ULog.Info("nailui", "loc lang=" + lang + " en=" + _loc.EnLoaded + " lang=" + _loc.LangLoaded + " sections=" + _loc.SecIds.Count + " options=" + _loc.Opts.Count + " version=" + _loc.Version);
			}
			N.Options.Snapshot(_infos);
			if (langChanged || _infos.Count != _infoCount)
			{
				_infoCount = _infos.Count;
				RebuildData();
			}
			ApplyChromeText();
		}

		// ---------- data -> left list ----------
		private static int CompareOpt(OptDef a, OptDef b)
		{
			if (a.Order != b.Order)
			{
				return a.Order < b.Order ? -1 : 1;
			}
			return a.Seq < b.Seq ? -1 : (a.Seq > b.Seq ? 1 : 0);
		}

		// Simple names and file names (without extension) of the loaded assemblies, collected once: mods load before the panel exists.
		private static HashSet<string> _asmNames;

		// requires="A;B": true when any listed assembly is loaded (option of a fix for that mod).
		private static bool AnyAssemblyLoaded(string requires)
		{
			if (_asmNames == null)
			{
				HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < all.Length; i++)
				{
					string fn = all[i].FullName;
					if (fn != null)
					{
						int comma = fn.IndexOf(',');
						names.Add(comma > 0 ? fn.Substring(0, comma) : fn);
					}
					try
					{
						string loc = all[i].Location;
						if (!string.IsNullOrEmpty(loc))
						{
							names.Add(System.IO.Path.GetFileNameWithoutExtension(loc));
						}
					}
					catch (Exception)
					{
					}
				}
				_asmNames = names;
			}
			string[] parts = requires.Split(new char[] { ';' });
			for (int i = 0; i < parts.Length; i++)
			{
				if (_asmNames.Contains(parts[i].Trim()))
				{
					return true;
				}
			}
			return false;
		}

		private static void RebuildData()
		{
			DestroyViews();
			_defs.Clear();
			_secs.Clear();
			int unlisted = 0;
			for (int i = 0; i < _infos.Count; i++)
			{
				N.OptionInfo oi = _infos[i];
				OptLayout ol;
				bool listed = _loc.Opts.TryGetValue(oi.Key, out ol);
				if (listed && (ol.Hidden || (ol.Requires.Length > 0 && !AnyAssemblyLoaded(ol.Requires))))
				{
					continue;
				}
				OptDef d = new OptDef();
				d.Info = oi;
				d.Seq = i;
				if (listed)
				{
					if (ol.Section.Length > 0)
					{
						d.Section = ol.Section;
					}
					if (ol.Group.Length > 0)
					{
						d.Group = ol.Group;
					}
					d.Order = ol.Order;
					d.Restart = ol.Restart;
					d.Control = ol.Control;
					d.Name = ol.Name.Length > 0 ? ol.Name : oi.Key;
					d.Desc = ol.Desc;
				}
				else
				{
					unlisted++;
					d.Name = oi.Key;
					d.Desc = oi.Comment;
				}
				d.NameLower = d.Name.ToLowerInvariant();
				d.DescLower = d.Desc.ToLowerInvariant();
				_defs.Add(d);
			}

			Dictionary<string, SecDef> map = new Dictionary<string, SecDef>(StringComparer.Ordinal);
			for (int i = 0; i < _loc.SecIds.Count; i++)
			{
				string id = _loc.SecIds[i];
				SecDef s = new SecDef();
				s.Id = id;
				s.Side = _loc.SecSide[id];
				s.Title = SectionTitle(id);
				_secs.Add(s);
				map[id] = s;
			}
			for (int i = 0; i < _defs.Count; i++)
			{
				SecDef s;
				if (!map.TryGetValue(_defs[i].Section, out s))
				{
					s = map["Advanced"];
				}
				s.Opts.Add(_defs[i]);
			}
			for (int i = 0; i < _secs.Count; i++)
			{
				_secs[i].Opts.Sort(new Comparison<OptDef>(CompareOpt));
			}
			if (_current != null && !map.ContainsKey(_current.Id))
			{
				_current = null;
			}
			else if (_current != null)
			{
				_current = map[_current.Id];
			}
			BuildLeft();
			if (unlisted > 0)
			{
				N.ULog.Warn("nailui", unlisted + " option(s) have no entry in Localization/en/NailUI.xml and are listed under Advanced with their raw key");
			}
			N.ULog.Info("nailui", "data rebuilt options=" + _defs.Count + " unlisted=" + unlisted);
		}

		private static void ClearChildren(RectTransform rt)
		{
			for (int i = rt.childCount - 1; i >= 0; i--)
			{
				GameObject g = rt.GetChild(i).gameObject;
				g.SetActive(false);
				UnityEngine.Object.Destroy(g);
			}
		}

		private static void DestroyViews()
		{
			foreach (KeyValuePair<string, View> kv in _views)
			{
				if (kv.Value.Root != null)
				{
					kv.Value.Root.gameObject.SetActive(false);
					UnityEngine.Object.Destroy(kv.Value.Root.gameObject);
				}
			}
			_views.Clear();
			if (_searchView != null && _searchView.Root != null)
			{
				_searchView.Root.gameObject.SetActive(false);
				UnityEngine.Object.Destroy(_searchView.Root.gameObject);
			}
			_searchView = null;
			_active = null;
			_hoverRow = null;
		}

		private static void BuildLeft()
		{
			if (_left == null)
			{
				return;
			}
			ClearChildren(_left.Content);
			float w = Theme.LeftViewW;
			float y = 0f;
			for (int side = 0; side < 2; side++)
			{
				string sideName = side == 0 ? "top" : "bottom";
				List<SecDef> list = new List<SecDef>();
				for (int i = 0; i < _secs.Count; i++)
				{
					SecDef s = _secs[i];
					if (s.Side == sideName && (s.Opts.Count > 0 || s.Id == "General"))
					{
						list.Add(s);
					}
				}
				if (list.Count == 0)
				{
					continue;
				}
				float h = list.Count * Theme.ItemH + 12f;
				RectTransform g = Mk.Box(_left.Content, "Group" + side, Theme.Dim, Theme.Solid, 2f, false);
				Mk.Px(g, 0f, y, w, h);
				for (int i = 0; i < list.Count; i++)
				{
					BuildSectionItem(g, list[i], 6f, 6f + i * Theme.ItemH, w - 12f);
				}
				y += h + Theme.GroupGap;
			}
			_left.SetContentHeight(y);
		}

		private static void BuildSectionItem(RectTransform parent, SecDef s, float x, float y, float w)
		{
			RectTransform it = Mk.Rt("Item_" + s.Id, parent);
			Mk.Px(it, x, y, w, Theme.ItemH);
			Image hit = Mk.Img(it, Theme.Clear, true);
			RectTransform sel = Mk.Box(it, "Sel", Theme.Yellow, Theme.Solid, 2f, false);
			Mk.Stretch(sel, 0f, 0f, 0f, 0f);
			sel.gameObject.SetActive(false);
			RectTransform lr = Mk.Rt("Label", it);
			Mk.Stretch(lr, 12f, 0f, 6f, 0f);
			Text lab = Mk.Txt(lr, s.Title, Theme.ListFont, Theme.Text, TextAnchor.MiddleLeft);
			Mk.Fit(lab, 12);
			Button b = it.gameObject.AddComponent<Button>();
			b.targetGraphic = hit;
			b.transition = Selectable.Transition.None;
			Mk.NoNav(b);
			SecClick c = new SecClick();
			c.S = s;
			b.onClick.AddListener(new UnityAction(c.OnClick));
			s.Sel = sel.gameObject;
			s.Label = lab;
		}

		// ---------- views ----------
		private static void SelectDefault()
		{
			SecDef pick = null;
			for (int i = 0; i < _secs.Count; i++)
			{
				if (_secs[i].Id == "General")
				{
					pick = _secs[i];
					break;
				}
			}
			if (pick == null)
			{
				for (int i = 0; i < _secs.Count; i++)
				{
					if (_secs[i].Opts.Count > 0)
					{
						pick = _secs[i];
						break;
					}
				}
			}
			SelectSection(pick);
		}

		internal static void OnSectionClick(SecDef s)
		{
			try
			{
				if (_query.Length > 0)
				{
					_query = string.Empty;
					SetSearchText(string.Empty);
				}
				SelectSection(s);
			}
			catch (Exception e)
			{
				Err("section click", e);
			}
		}

		private static void SelectSection(SecDef s)
		{
			_current = s;
			UpdateHighlight();
			if (s == null)
			{
				ShowView(null);
				return;
			}
			View v;
			if (!_views.TryGetValue(s.Id, out v))
			{
				List<GroupSpec> groups = GroupsOf(s.Opts);
				v = BuildView("Sec_" + s.Id, groups, false, s.Opts.Count == 0 ? Str("NoOptions", "No options") : null, s.Id == "Advanced");
				_views[s.Id] = v;
			}
			ShowView(v);
		}

		private static void UpdateHighlight()
		{
			for (int i = 0; i < _secs.Count; i++)
			{
				SecDef s = _secs[i];
				if (s.Sel == null)
				{
					continue;
				}
				bool on = _query.Length == 0 && object.ReferenceEquals(s, _current);
				s.Sel.SetActive(on);
				s.Label.color = on ? Theme.Yellow : Theme.Text;
			}
		}

		private static List<GroupSpec> GroupsOf(List<OptDef> opts)
		{
			List<GroupSpec> groups = new List<GroupSpec>();
			for (int i = 0; i < opts.Count; i++)
			{
				OptDef d = opts[i];
				GroupSpec g = null;
				for (int j = 0; j < groups.Count; j++)
				{
					if (groups[j].Title == GroupTitle(d.Group))
					{
						g = groups[j];
						break;
					}
				}
				if (g == null)
				{
					g = new GroupSpec();
					g.Title = GroupTitle(d.Group);
					groups.Add(g);
				}
				g.Items.Add(d);
			}
			return groups;
		}

		private static View BuildView(string name, List<GroupSpec> groups, bool showSection, string emptyText, bool withResetAll)
		{
			View v = new View();
			RectTransform root = Mk.Rt("View_" + name, _right.Content);
			root.anchorMin = new Vector2(0f, 1f);
			root.anchorMax = new Vector2(1f, 1f);
			root.pivot = new Vector2(0f, 1f);
			root.anchoredPosition = Vector2.zero;
			v.Root = root;
			float gw = Theme.RightViewW;
			float y = 0f;
			if (groups.Count == 0 && emptyText != null)
			{
				RectTransform er = Mk.Rt("Empty", root);
				Mk.Px(er, 12f, 8f, gw - 24f, 30f);
				Mk.Txt(er, emptyText, Theme.RowFont, Theme.DimText, TextAnchor.MiddleLeft);
				y = 46f;
			}
			for (int gi = 0; gi < groups.Count; gi++)
			{
				GroupSpec gs = groups[gi];
				float h = Theme.GroupHead + gs.Items.Count * Theme.RowH + Theme.GroupPad;
				RectTransform frame = Mk.Box(root, "Group" + gi, Theme.Dim, Theme.Solid, 2f, false);
				Mk.Px(frame, 0f, y, gw, h);
				RectTransform tr = Mk.Rt("Title", frame);
				Mk.Px(tr, 12f, 4f, gw - 24f, 26f);
				Mk.Txt(tr, gs.Title, Theme.GroupFont, Theme.Teal, TextAnchor.MiddleLeft);
				for (int i = 0; i < gs.Items.Count; i++)
				{
					Row r = new Row(gs.Items[i]);
					r.Build(frame, 10f, Theme.GroupHead + i * Theme.RowH, gw - 20f, showSection);
					v.Rows.Add(r);
				}
				y += h + Theme.GroupGap;
			}
			if (withResetAll)
			{
				float dh = Theme.GroupHead + Theme.RowH + Theme.GroupPad;
				RectTransform df = Mk.Box(root, "GroupDefaults", Theme.Dim, Theme.Solid, 2f, false);
				Mk.Px(df, 0f, y, gw, dh);
				RectTransform dt = Mk.Rt("Title", df);
				Mk.Px(dt, 12f, 4f, gw - 24f, 26f);
				Mk.Txt(dt, Str("DefaultsGroup", "Defaults"), Theme.GroupFont, Theme.Teal, TextAnchor.MiddleLeft);
				RectTransform br = Mk.Box(df, "ResetAll", Theme.Teal, Theme.Solid, 2f, true);
				Mk.Px(br, 10f, Theme.GroupHead + 3f, gw - 20f, Theme.RowH - 6f);
				Image bi = br.GetComponent<Image>();
				Button bb = br.gameObject.AddComponent<Button>();
				Mk.Tint(bb, bi, Theme.Teal, Theme.TealHot, Theme.TealDark, Theme.TealDark);
				RectTransform bl = Mk.Rt("Label", br);
				Mk.Stretch(bl, 6f, 0f, 6f, 0f);
				Text bt = Mk.Txt(bl, Str("ResetAll", "Reset all options to defaults"), Theme.RowFont, Theme.Yellow, TextAnchor.MiddleCenter);
				Mk.Fit(bt, 11);
				ResetAllClick rc = new ResetAllClick();
				bb.onClick.AddListener(new UnityAction(rc.OnClick));
				y += dh + Theme.GroupGap;
			}
			v.Height = y;
			root.sizeDelta = new Vector2(0f, y);
			root.gameObject.SetActive(false);
			return v;
		}

		private static void ShowView(View v)
		{
			if (_active != null && _active.Root != null)
			{
				_active.Root.gameObject.SetActive(false);
			}
			_active = v;
			_hoverRow = null;
			HideTip();
			if (v == null)
			{
				_right.SetContentHeight(0f);
				return;
			}
			v.Root.gameObject.SetActive(true);
			_right.SetContentHeight(v.Height);
			RefreshRows();
		}

		private static void ShowSearch()
		{
			if (_searchView != null && _searchView.Root != null)
			{
				_searchView.Root.gameObject.SetActive(false);
				UnityEngine.Object.Destroy(_searchView.Root.gameObject);
			}
			List<OptDef> hits = new List<OptDef>();
			for (int i = 0; i < _defs.Count; i++)
			{
				OptDef d = _defs[i];
				if (d.NameLower.IndexOf(_query, StringComparison.Ordinal) >= 0 || d.DescLower.IndexOf(_query, StringComparison.Ordinal) >= 0)
				{
					hits.Add(d);
				}
			}
			List<GroupSpec> groups = new List<GroupSpec>();
			if (hits.Count > 0)
			{
				GroupSpec g = new GroupSpec();
				g.Title = Str("SearchResults", "Search results") + " (" + hits.Count + ")";
				g.Items.AddRange(hits);
				groups.Add(g);
			}
			_searchView = BuildView("Search", groups, true, Str("NoMatches", "Nothing found"), false);
			UpdateHighlight();
			ShowView(_searchView);
		}

		internal static void OnPendingChanged()
		{
			RefreshRows();
			RefreshChrome();
		}

		private static void RefreshRows()
		{
			if (_active == null)
			{
				return;
			}
			for (int i = 0; i < _active.Rows.Count; i++)
			{
				_active.Rows[i].Refresh();
			}
		}

		private static int PendingCount()
		{
			int n = 0;
			for (int i = 0; i < _defs.Count; i++)
			{
				if (_defs[i].HasPend)
				{
					n++;
				}
			}
			return n;
		}

		private static void ClearAllPending()
		{
			for (int i = 0; i < _defs.Count; i++)
			{
				_defs[i].HasPend = false;
			}
		}

		// ---------- search ----------
		private static void SetSearchText(string s)
		{
			if (_search == null)
			{
				return;
			}
			_suppressSearch = true;
			_search.text = s;
			_suppressSearch = false;
		}

		private static void OnSearchChanged(string s)
		{
			if (_suppressSearch)
			{
				return;
			}
			try
			{
				_query = s == null ? string.Empty : s.Trim().ToLowerInvariant();
				if (_query.Length == 0)
				{
					if (_current == null)
					{
						SelectDefault();
					}
					else
					{
						SelectSection(_current);
					}
				}
				else
				{
					ShowSearch();
				}
			}
			catch (Exception e)
			{
				Err("search", e);
			}
		}

		private static void OnSearchEnd(string s)
		{
			QueueDeselect();
		}

		// ---------- apply / done ----------
		private static void OnApplyClick()
		{
			try
			{
				if (_active != null)
				{
					for (int i = 0; i < _active.Rows.Count; i++)
					{
						_active.Rows[i].CommitIfFocused();
					}
				}
				int n = 0;
				int failed = 0;
				_restartNames.Clear();
				for (int i = 0; i < _defs.Count; i++)
				{
					OptDef d = _defs[i];
					if (!d.HasPend)
					{
						continue;
					}
					bool restart = d.Restart;
					string key = d.Info.Key;
					try
					{
						d.ApplyPending();
						n++;
						if (restart)
						{
							_needRestart = true;
							_restartNames.Add(d.Name.Length > 0 ? d.Name : key);
						}
					}
					catch (Exception e)
					{
						failed++;
						d.HasPend = false;
						N.ULog.Error("nailui", "apply failed for " + key + ": " + e);
					}
				}
				_seenVersion = N.Options.Version;
				_cApplies.Inc();
				_cKeys.Value += n;
				N.ULog.Info("nailui", "apply keys=" + n + " failed=" + failed + " save=" + N.Options.SaveState);
				OnPendingChanged();
				if (_restartNames.Count > 0)
				{
					ShowRestartNotice();
				}
			}
			catch (Exception e2)
			{
				Err("apply", e2);
			}
		}

		private static void OnDoneClick()
		{
			try
			{
				Close("done");
			}
			catch (Exception e)
			{
				Err("done", e);
			}
		}

		// ---------- chrome ----------
		private static string StateText(string s)
		{
			if (string.IsNullOrEmpty(s) || s == "saved")
			{
				return Str("State.saved", "saved");
			}
			if (s.StartsWith("writing", StringComparison.Ordinal))
			{
				return Str("State.writing", "writing...");
			}
			if (s.StartsWith("session only", StringComparison.Ordinal))
			{
				return Str("State.session", "session only");
			}
			if (s.StartsWith("NOT SAVED", StringComparison.Ordinal))
			{
				return Str("State.notsaved", "NOT SAVED") + s.Substring(9);
			}
			return s;
		}

		private static void RefreshChrome()
		{
			if (_statusText == null)
			{
				return;
			}
			int pend = PendingCount();
			StringBuilder sb = new StringBuilder();
			sb.Append(StateText(N.Options.SaveState));
			if (pend > 0)
			{
				sb.Append("   ").Append(Str("Pending", "{0} not applied").Replace("{0}", pend.ToString(CultureInfo.InvariantCulture)));
			}
			if (_needRestart)
			{
				sb.Append("   <color=#ff5c4d>").Append(WarnGlyph).Append(' ').Append(Str("Restart", "Restart required")).Append("</color>");
			}
			string line = sb.ToString();
			if (!string.Equals(_statusText.text, line, StringComparison.Ordinal))
			{
				_statusText.text = line;
			}
			_applyBtn.interactable = pend > 0;
			_applyLabel.color = pend > 0 ? Theme.Yellow : Theme.DimText;
			_modsText.text = Str("Modules", "modules") + " " + N.Modules.OkCount + "/" + N.Modules.Total;
			_apiText.text = "api " + N.Core.ApiLevel;
		}

		private static void ApplyChromeText()
		{
			if (_searchHint == null)
			{
				return;
			}
			_searchHint.text = Str("Search", "Search...");
			_applyLabel.text = Str("Apply", "Apply");
			_doneLabel.text = Str("Done", "Done");
			_confirmText.text = Str("ConfirmReset", "Reset all options to their default values?") + "\n\n<color=#ff5c4d>" + Str("ConfirmResetWarn", "All current settings, including the hotkey and module switches, will be replaced by the defaults. The change takes effect after Apply.") + "</color>";
			_yesLabel.text = Str("Yes", "Yes");
			_noLabel.text = Str("No", "No");
			_okLabel.text = Str("Ok", "OK");
		}

		// ---------- sprites ----------
		internal static Sprite LoadSprite(string rel)
		{
			try
			{
				string path = Path.Combine(N.UiHost.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(path))
				{
					N.ULog.Warn("nailui", "image not found: " + path + " (placeholder used)");
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				if (!tex.LoadImage(bytes))
				{
					UnityEngine.Object.Destroy(tex);
					N.ULog.Warn("nailui", "image decode failed: " + path + " (placeholder used)");
					return null;
				}
				tex.wrapMode = TextureWrapMode.Clamp;
				tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
				Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, (float)tex.width, (float)tex.height), new Vector2(0.5f, 0.5f), 100f);
				sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
				return sp;
			}
			catch (Exception e)
			{
				N.ULog.Warn("nailui", "image load failed " + rel + ": " + e.GetType().Name);
				return null;
			}
		}

		internal static Sprite ResetSprite()
		{
			if (!_resetTried)
			{
				_resetTried = true;
				_reset = LoadSprite(ResetPath);
			}
			return _reset;
		}

		internal static Sprite IconSprite()
		{
			if (!_iconTried)
			{
				_iconTried = true;
				_icon = LoadSprite(IconPath);
			}
			return _icon;
		}

		// ---------- skeleton ----------
		private static void BuildSkeleton()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			_root = new GameObject("NailUiRoot", typeof(RectTransform));
			_root.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(_root);
			Canvas canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30001;
			CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1280f, 720f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0.5f;
			_root.AddComponent<GraphicRaycaster>();
			_rootRt = (RectTransform)_root.transform;

			// panel: yellow border + black fill (blocks clicks on the interface behind it)
			_panelRt = Mk.Rt("Panel", _rootRt);
			_panelRt.anchorMin = new Vector2(0.5f, 0.5f);
			_panelRt.anchorMax = new Vector2(0.5f, 0.5f);
			_panelRt.pivot = new Vector2(0.5f, 0.5f);
			_panelRt.anchoredPosition = Vector2.zero;
			_panelRt.sizeDelta = new Vector2(Theme.PanelW, Theme.PanelH);
			Mk.Img(_panelRt, Theme.Yellow, true);
			RectTransform fill = Mk.Rt("Fill", _panelRt);
			Mk.Stretch(fill, 3f, 3f, 3f, 3f);
			Mk.Img(fill, Theme.Back, false);

			// search box
			RectTransform sb = Mk.Box(_panelRt, "Search", Theme.Teal, Theme.Solid, 3f, true);
			Mk.Frac(sb, 25f, 17f, 558f, 80f);
			_searchBorder = sb.GetComponent<Image>();
			RectTransform sfill = (RectTransform)sb.GetChild(0);
			RectTransform st = Mk.Rt("Text", sfill);
			Mk.Stretch(st, 9f, 2f, 9f, 2f);
			Text stx = Mk.Txt(st, string.Empty, Theme.SearchFont, Theme.Yellow, TextAnchor.MiddleLeft);
			RectTransform sh = Mk.Rt("Hint", sfill);
			Mk.Stretch(sh, 9f, 2f, 9f, 2f);
			_searchHint = Mk.Txt(sh, "Search...", Theme.SearchFont, Theme.DimText, TextAnchor.MiddleLeft);
			_search = sb.gameObject.AddComponent<InputField>();
			_search.targetGraphic = _searchBorder;
			_search.textComponent = stx;
			Mk.Caret(_search);
			_search.lineType = InputField.LineType.SingleLine;
			_search.characterLimit = 40;
			_search.transition = Selectable.Transition.None;
			Mk.NoNav(_search);
			_search.onValueChanged.AddListener(new UnityAction<string>(OnSearchChanged));
			_search.onEndEdit.AddListener(new UnityAction<string>(OnSearchEnd));

			// header: no title text or logo (D-6, user 2026-10-14)

			// left and right panes
			_left = Pane.Create(_panelRt, "Left", new float[] { 25f, 98f, 558f, 865f }, new float[] { 33f, 106f, 552f, 858f }, new float[] { 568f, 98f, 608f, 865f }, Theme.LeftViewH);
			_right = Pane.Create(_panelRt, "Right", new float[] { 620f, 92f, 1685f, 865f }, new float[] { 630f, 100f, 1675f, 858f }, new float[] { 1692f, 98f, 1730f, 865f }, Theme.RightViewH);

			// footer: status line and two short fields (the lines of the mock)
			RectTransform ss = Mk.Rt("Status", _panelRt);
			Mk.Frac(ss, 42f, 885f, 960f, 945f);
			_statusText = Mk.Txt(ss, string.Empty, Theme.StatusFont, Theme.Dim, TextAnchor.MiddleLeft);
			_statusText.supportRichText = true;
			RectTransform ms = Mk.Rt("Mods", _panelRt);
			Mk.Frac(ms, 965f, 885f, 1095f, 945f);
			_modsText = Mk.Txt(ms, string.Empty, Theme.StatusFont, Theme.DimText, TextAnchor.MiddleCenter);
			RectTransform ap = Mk.Rt("Api", _panelRt);
			Mk.Frac(ap, 1100f, 885f, 1240f, 945f);
			_apiText = Mk.Txt(ap, string.Empty, Theme.StatusFont, Theme.DimText, TextAnchor.MiddleCenter);

			// buttons
			_applyBtn = MakeButton("Apply", 1258f, 882f, 1458f, 950f, out _applyLabel);
			_applyBtn.onClick.AddListener(new UnityAction(OnApplyClick));
			Button done = MakeButton("Done", 1473f, 882f, 1685f, 950f, out _doneLabel);
			done.onClick.AddListener(new UnityAction(OnDoneClick));

			BuildConfirm();
			BuildNotice();

			// tooltip: last child of the canvas root, never a raycast target
			_tipRt = Mk.Box(_rootRt, "Tip", Theme.Teal, Theme.Solid, 2f, false);
			_tipRt.anchorMin = new Vector2(0.5f, 0.5f);
			_tipRt.anchorMax = new Vector2(0.5f, 0.5f);
			_tipRt.pivot = new Vector2(0f, 1f);
			_tipRt.sizeDelta = new Vector2(340f, 60f);
			RectTransform tr = Mk.Rt("Text", _tipRt);
			Mk.Stretch(tr, 8f, 8f, 8f, 8f);
			_tipText = Mk.Txt(tr, string.Empty, Theme.TipFont, Theme.Yellow, TextAnchor.UpperLeft);
			_tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
			_tipRt.gameObject.SetActive(false);

			_root.SetActive(false);
			_built = true;
			N.ULog.Info("nailui", "skeleton built in " + sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
		}

		private static Button MakeButton(string name, float x0, float y0, float x1, float y1, out Text label)
		{
			RectTransform b = Mk.Box(_panelRt, name, Theme.Teal, Theme.Solid, 3f, true);
			Mk.Frac(b, x0, y0, x1, y1);
			Image bi = b.GetComponent<Image>();
			Button btn = b.gameObject.AddComponent<Button>();
			Mk.Tint(btn, bi, Theme.Teal, Theme.TealHot, Theme.TealDark, Theme.TealDark);
			RectTransform lr = Mk.Rt("Label", b);
			Mk.Stretch(lr, 4f, 2f, 4f, 2f);
			label = Mk.Txt(lr, name, Theme.ButtonFont, Theme.Yellow, TextAnchor.MiddleCenter);
			Mk.Fit(label, 14);
			return btn;
		}

		// ---------- per-frame work while open ----------
		internal static void Tick()
		{
			if (_dead)
			{
				return;
			}
			try
			{
				N.UiHost.HotkeyEnabled = _hotOn.On;
				N.UiHost.HotkeyKey = (KeyCode)_hotKey.Value;
				_typing = _open && (_bindRow != null || AnyFieldFocused());
				N.UiHost.HotkeyBlocked = _typing;
				HudBox.Tick();
				FpsCounter.Tick();
				UnitLabels.Tick();
			}
			catch (Exception)
			{
				// Do not leave game input blocked; the host still counts the failure.
				_typing = false;
				N.UiHost.HotkeyBlocked = false;
				GameHooks.ReleaseCamera();
				throw;
			}
			if (!_open)
			{
				return;
			}
			try
			{
				if (ConfirmOpen() && Input.GetKeyDown(KeyCode.Escape))
				{
					HideConfirm();
					return;
				}
				if (_bindRow != null && ProcessBind())
				{
					return;
				}
				int v = N.Options.Version;
				if (v != _seenVersion)
				{
					_seenVersion = v;
					for (int i = 0; i < _defs.Count; i++)
					{
						if (_defs[i].ExternalChanged())
						{
							_defs[i].HasPend = false;
						}
					}
					RefreshRows();
					RefreshChrome();
				}

				if ((Time.frameCount & 15) == 0)
				{
					RefreshChrome();
				}
				if ((Time.frameCount & 15) == 7 && LanguageOrFontChanged())
				{
					SyncLanguageAndData();
					if (_query.Length > 0)
					{
						ShowSearch();
					}
					else
					{
						SelectSection(_current);
					}
					RefreshChrome();
				}

				Vector2 mouse = Input.mousePosition;
				float wheel = Input.mouseScrollDelta.y;
				if (wheel != 0f && !ConfirmOpen())
				{
					Pane target = null;
					if (_right.Contains(mouse))
					{
						target = _right;
					}
					else if (_left.Contains(mouse))
					{
						target = _left;
					}
					if (_wheelLogs < 4)
					{
						_wheelLogs++;
						N.ULog.Info("nailui", "wheel delta=" + wheel.ToString("F2", CultureInfo.InvariantCulture) + " pane=" + (target == null ? "none" : (object.ReferenceEquals(target, _right) ? "right" : "left")));
					}
					if (target != null)
					{
						target.ScrollBy(-wheel * Theme.ScrollStep);
					}
				}

				if (Input.GetKeyDown(KeyCode.Escape))
				{
					_escClosedFrame = Time.frameCount;
					Close("escape");
					return;
				}
				if (!_force.On)
				{
					string why = BlockedReason(false);
					if (why != null)
					{
						Close("blocked: " + why);
						return;
					}
				}

				if (Time.frameCount > _openFrame + 1 && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mouse, null) && !N.UiHost.OverToggleArea(mouse) && !HudBox.Contains(mouse))
				{
					Close("outside click");
					return;
				}

				UpdateFocusVisuals();
				ProbeTyping();
				if (_deselectAt != 0 && Time.frameCount >= _deselectAt)
				{
					_deselectAt = 0;
					DeselectIfIdle();
				}
				UpdateTip(mouse);
			}
			catch (Exception e)
			{
				Err("Tick", e);
				if (_errors >= 5)
				{
					_dead = true;
					HudBox.Shutdown();
					try
					{
						Close("errors");
					}
					catch (Exception)
					{
					}
					N.ULog.Error("nailui", "interface disabled after repeated errors; the built-in panel is used");
				}
			}
		}

		// ---------- keyboard focus ----------
		internal static void QueueDeselect()
		{
			_deselectAt = Time.frameCount + 2;
		}

		private static bool AnyFieldFocused()
		{
			if (_search != null && _search.isFocused)
			{
				return true;
			}
			if (_active != null)
			{
				for (int i = 0; i < _active.Rows.Count; i++)
				{
					Row r = _active.Rows[i];
					if (r.Field != null && r.Field.isFocused)
					{
						return true;
					}
				}
			}
			return false;
		}

		// The hint is not the InputField placeholder: it is hidden while the field has focus so it never overlaps the caret.
		private static void UpdateHint()
		{
			bool want = !_search.isFocused && string.IsNullOrEmpty(_search.text);
			if (_searchHint.enabled != want)
			{
				_searchHint.enabled = want;
			}
		}

		private static void UpdateFocusVisuals()
		{
			UpdateHint();
			Color c = _search.isFocused ? Theme.TealHot : Theme.Teal;
			if (_searchBorder.color != c)
			{
				_searchBorder.color = c;
			}
			if (_active != null)
			{
				for (int i = 0; i < _active.Rows.Count; i++)
				{
					_active.Rows[i].UpdateFocus();
				}
			}
		}

		// Typing probe: the game may react to typed characters (speed, pause). Logs a WARN when timeScale changed while a field was focused.
		private static void ProbeTyping()
		{
			bool typing = AnyFieldFocused();
			if (typing && !_wasTyping)
			{
				_tsAtFocus = Time.timeScale;
			}
			else if (!typing && _wasTyping)
			{
				float now = Time.timeScale;
				if (Mathf.Abs(now - _tsAtFocus) > 0.001f)
				{
					N.ULog.Warn("nailui", "timeScale changed while typing: " + _tsAtFocus.ToString("F2", CultureInfo.InvariantCulture) + " -> " + now.ToString("F2", CultureInfo.InvariantCulture) + " (game hotkeys react to typed characters)");
				}
				else if (_typingLogs < 4)
				{
					_typingLogs++;
					N.ULog.Info("nailui", "typing ended, timeScale unchanged (" + now.ToString("F2", CultureInfo.InvariantCulture) + ")");
				}
			}
			_wasTyping = typing;
		}

		// A selected input field that is not being typed in would re-activate on the game's Submit key; drop the selection.
		private static void DeselectIfIdle()
		{
			EventSystem es = EventSystem.current;
			if (es == null || _root == null)
			{
				return;
			}
			GameObject sel = es.currentSelectedGameObject;
			if (sel == null || !sel.transform.IsChildOf(_root.transform))
			{
				return;
			}
			InputField f = sel.GetComponent<InputField>();
			if (f != null && f.isFocused)
			{
				return;
			}
			es.SetSelectedGameObject(null);
		}

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

		// ---------- tooltip ----------
		private static void UpdateTip(Vector2 mouse)
		{
			if (ConfirmOpen())
			{
				_hoverRow = null;
				HideTip();
				return;
			}
			if (!_tipsFlag.On || _active == null || !_right.ContainsView(mouse))
			{
				_hoverRow = null;
				HideTip();
				return;
			}
			Row hit = null;
			for (int i = 0; i < _active.Rows.Count; i++)
			{
				Row r = _active.Rows[i];
				if (r.OverName(mouse))
				{
					hit = r;
					break;
				}
			}
			if (hit == null)
			{
				_hoverRow = null;
				HideTip();
				return;
			}
			if (!object.ReferenceEquals(hit, _hoverRow))
			{
				_hoverRow = hit;
				_hoverT = 0f;
				HideTip();
			}
			_hoverT += Time.unscaledDeltaTime;
			if (_hoverT >= _tipDelay.Value)
			{
				if (!_tipShown)
				{
					ShowTip(hit);
				}
				MoveTip(mouse);
			}
		}

		private static void ShowTip(Row r)
		{
			OptDef d = r.D;
			StringBuilder sb = new StringBuilder();
			string desc = d.Desc.Length > 0 ? d.Desc : d.Info.Comment;
			sb.Append(desc);
			if (sb.Length > 0)
			{
				sb.Append('\n');
			}
			if (d.IsFlag)
			{
				sb.Append(Str("Default", "Default")).Append(": ").Append(d.Info.Flag.Def ? Str("On", "ON") : Str("Off", "OFF"));
			}
			else if (d.Control == "key")
			{
				sb.Append(Str("Default", "Default")).Append(": ").Append(KeyText(null, d.Info.IntParam.Def));
			}
			else if (d.Control == "choice")
			{
				sb.Append(Str("Default", "Default")).Append(": ").Append(DisplayOptions.ChoiceText(d.Info.Key, d.Info.IntParam.Def));
			}
			else
			{
				sb.Append(Str("Default", "Default")).Append(": ").Append(d.Fmt(d.Def));
				sb.Append("   ").Append(Str("Range", "Range")).Append(": ").Append(d.Fmt(d.Min)).Append("..").Append(d.Fmt(d.Max));
			}
			if (d.Restart)
			{
				sb.Append('\n').Append(WarnGlyph).Append(' ').Append(Str("Restart", "Restart required"));
			}
			_tipText.text = sb.ToString();
			_tipRt.sizeDelta = new Vector2(340f, 60f);
			float h = _tipText.preferredHeight + 16f;
			_tipRt.sizeDelta = new Vector2(340f, Mathf.Max(h, 30f));
			_tipRt.gameObject.SetActive(true);
			_tipShown = true;
		}

		private static void MoveTip(Vector2 mouse)
		{
			Vector2 local;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, mouse, null, out local))
			{
				return;
			}
			Vector2 size = _tipRt.sizeDelta;
			Vector2 half = _rootRt.rect.size * 0.5f;
			float x = local.x + 18f;
			float y = local.y - 18f;
			if (x + size.x > half.x)
			{
				x = local.x - 18f - size.x;
			}
			if (y - size.y < -half.y)
			{
				y = local.y + 18f + size.y;
			}
			if (x < -half.x)
			{
				x = -half.x;
			}
			if (y > half.y)
			{
				y = half.y;
			}
			_tipRt.anchoredPosition = new Vector2(x, y);
		}

		private static void HideTip()
		{
			if (_tipShown && _tipRt != null)
			{
				_tipRt.gameObject.SetActive(false);
			}
			_tipShown = false;
		}
	}
}
