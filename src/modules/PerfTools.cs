// PerfTools: NailOptimization module (api level 3). F3 performance overlay, Logs/Perf.log, targeted profiler (SlowProbe successor).
// Harmony 1.0.9.1. No lock / ?. / interpolation. Fail-open in every patch.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine, UnityEngine.UI. Output goes to <NailOptimization>/modules/.
//
// Overlay: own Canvas without a raycaster (never blocks clicks), text 4x/s, frame/tick graph one texture column per frame.
// Tick ms = time spent in GameManager.FixedUpdate during the frame (Prefix priority 800, Postfix priority 0).
// Profiler: targets from PerfTools.targets.txt, patched on the first session start; 64 static slots (no __originalMethod
// in 1.0.9.1). Targets with exception handlers are skipped (the patched copy loses them, GL-53), except finally blocks
// that only dispose a System.Collections.Generic enumerator. All file writes happen in the driver, outside measured code.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Harmony;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using N = NailOptimization;

namespace PerfTools
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class PerfToolsModule : N.ModuleBase
	{
		public const string Id = "PerfTools";
		public const string Ver = "1.0";
		public const string HarmonyId = "Lobotomy.NailOptimization.PerfTools";

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
				return Ver;
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

		public override string[] Targets
		{
			get
			{
				return new string[] { "GameManager.FixedUpdate" };
			}
		}

		public override void Init()
		{
			Cfg.Bind();
			if (!Cfg.Enabled.On)
			{
				N.ULog.Info(Id, "disabled by PerfTools.Enabled=false");
				return;
			}
			Paths.Init();
			Prof.MainThread = Thread.CurrentThread.ManagedThreadId;
			int hooked = InstallTick();
			GameObject go = new GameObject("NailOptimization.PerfTools");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<PerfToolsDriver>();
			N.ULog.Info(Id, "PerfTools " + Ver + " installed: tick hook " + hooked + "/1, key " + Cfg.Key.Text + ", profiler key " + Cfg.ProfKey.Text + ", targets file " + Paths.Targets);
		}

		public override void OnConfigChanged(string key)
		{
			if (string.Equals(key, "PerfTools.Corner", StringComparison.Ordinal)
				|| string.Equals(key, "PerfTools.Scale", StringComparison.Ordinal))
			{
				Overlay.Relayout();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int InstallTick()
		{
			Type gm = N.TypeCache.Resolve("GameManager");
			if (object.ReferenceEquals(gm, null))
			{
				N.ULog.Error(Id, "type not found: GameManager (tick ms unavailable)");
				return 0;
			}
			MethodBase m = N.PatchUtil.Resolve(Id, gm, "FixedUpdate", Type.EmptyTypes);
			if (object.ReferenceEquals(m, null))
			{
				return 0;
			}
			HarmonyInstance h = HarmonyInstance.Create(HarmonyId);
			BindingFlags pub = BindingFlags.Public | BindingFlags.Static;
			HarmonyMethod pre = N.PatchUtil.WithPriority(typeof(TickHook).GetMethod("Pre", pub), 800);
			HarmonyMethod post = N.PatchUtil.WithPriority(typeof(TickHook).GetMethod("Post", pub), 0);
			return N.PatchUtil.Install(h, Id, m, pre, post) ? 1 : 0;
		}
	}

	// ---------- config ----------
	internal sealed class KeySpec
	{
		internal string Text = string.Empty;
		internal KeyCode Key = KeyCode.None;
		internal bool Shift;
		internal bool Ctrl;
		internal bool Alt;

		internal static KeySpec Parse(string text, string def)
		{
			KeySpec k = new KeySpec();
			if (!k.TryParse(text))
			{
				N.ULog.Warn(PerfToolsModule.Id, "bad key '" + text + "', using " + def);
				k = new KeySpec();
				k.TryParse(def);
			}
			return k;
		}

		private bool TryParse(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			string[] parts = text.Split('+');
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i].Trim();
				string l = p.ToLowerInvariant();
				if (l == "shift" || l == "leftshift" || l == "rightshift")
				{
					Shift = true;
				}
				else if (l == "ctrl" || l == "control" || l == "leftcontrol" || l == "rightcontrol" || l == "leftctrl" || l == "rightctrl")
				{
					Ctrl = true;
				}
				else if (l == "alt" || l == "leftalt" || l == "rightalt")
				{
					Alt = true;
				}
				else
				{
					if (Key != KeyCode.None)
					{
						return false;
					}
					try
					{
						Key = (KeyCode)Enum.Parse(typeof(KeyCode), p, true);
					}
					catch (Exception)
					{
						return false;
					}
				}
			}
			if (Key == KeyCode.None)
			{
				return false;
			}
			Text = text.Trim();
			return true;
		}

		internal bool Down()
		{
			if (!Input.GetKeyDown(Key))
			{
				return false;
			}
			bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
			bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
			return shift == Shift && ctrl == Ctrl && alt == Alt;
		}
	}

	internal static class Cfg
	{
		internal static N.Flag Enabled;
		internal static KeySpec Key;
		internal static KeySpec ProfKey;
		internal static N.IntParam Corner;
		internal static N.FloatParam Scale;
		internal static N.Flag LogPerf;
		internal static N.IntParam SpikeMs;
		internal static N.IntParam ProfSummarySec;
		internal static N.IntParam ProfMaxSec;
		internal static N.IntParam ProfMaxEvents;
		internal static N.IntParam ProfMaxKB;

		internal static void Bind()
		{
			Enabled = N.Config.BindFlag("PerfTools.Enabled", true, "Performance overlay (key PerfTools.Key), Logs/Perf.log and the profiler. Read at game start.");
			Key = KeySpec.Parse(N.Config.GetString("PerfTools.Key", "F3", "Key that shows or hides the performance overlay (Unity KeyCode name, modifiers Shift+/Ctrl+/Alt+). Read at game start."), "F3");
			ProfKey = KeySpec.Parse(N.Config.GetString("PerfTools.ProfKey", "Shift+F3", "Key that starts or stops a profiler session. Read at game start."), "Shift+F3");
			if (Key.Key == ProfKey.Key && Key.Shift == ProfKey.Shift && Key.Ctrl == ProfKey.Ctrl && Key.Alt == ProfKey.Alt)
			{
				N.ULog.Warn(PerfToolsModule.Id, "PerfTools.Key and PerfTools.ProfKey are the same (" + Key.Text + "): the key starts the profiler only");
			}
			Corner = N.Config.BindInt("PerfTools.Corner", 3, 0, 3, 1, "Overlay corner: 0 top left, 1 top right, 2 bottom left, 3 bottom right.");
			Scale = N.Config.BindFloat("PerfTools.Scale", 1f, 0.6f, 2f, 0.1f, "Overlay size multiplier.");
			LogPerf = N.Config.BindFlag("PerfTools.LogPerf", true, "While the overlay is visible or a profiler session runs, one line per second goes to Logs/Perf.log.");
			SpikeMs = N.Config.BindInt("PerfTools.SpikeMs", 50, 20, 1000, 5, "A frame at least this long (ms) is written to Logs/Perf.log as a SPIKE line.");
			ProfSummarySec = N.Config.BindInt("PerfTools.ProfSummarySec", 10, 1, 120, 1, "Seconds between profiler summary tables.");
			ProfMaxSec = N.Config.BindInt("PerfTools.ProfMaxSec", 120, 0, 3600, 10, "A profiler session stops by itself after this many seconds. 0 = no limit.");
			ProfMaxEvents = N.Config.BindInt("PerfTools.ProfMaxEvents", 500, 0, 20000, 100, "Most slow-call event lines per profiler session; summaries continue after the cap.");
			ProfMaxKB = N.Config.BindInt("PerfTools.ProfMaxKB", 1024, 64, 16384, 64, "Profiler.log size limit in KB per session; after it only the final summary is written.");
		}
	}

	internal static class Paths
	{
		internal static string Logs;
		internal static string PerfLog;
		internal static string ProfLog;
		internal static string Targets;

		internal static void Init()
		{
			Logs = Path.Combine(N.UiHost.RootDir, "Logs");
			PerfLog = Path.Combine(Logs, "Perf.log");
			ProfLog = Path.Combine(Logs, "Profiler.log");
			Targets = Path.Combine(N.UiHost.RootDir, "PerfTools.targets.txt");
		}

		// Moves the previous file to <name>.prev.log and opens a new one.
		internal static StreamWriter Open(string path)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string prev = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".prev.log");
			try
			{
				if (File.Exists(path))
				{
					if (File.Exists(prev))
					{
						File.Delete(prev);
					}
					File.Move(path, prev);
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn(PerfToolsModule.Id, "rotate " + path + " failed: " + e.Message);
			}
			StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false));
			w.AutoFlush = false;
			return w;
		}
	}

	internal static class Fmt
	{
		internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		internal static string F1(double v)
		{
			return v.ToString("0.0", Inv);
		}

		internal static string F2(double v)
		{
			return v.ToString("0.00", Inv);
		}

		internal static string Mb(long bytes)
		{
			if (bytes <= 0)
			{
				return "n/a";
			}
			return (bytes / 1048576.0).ToString("0", Inv);
		}
	}

	// ---------- tick hook (GameManager.FixedUpdate) ----------
	public static class TickHook
	{
		internal static bool On;
		internal static long FrameTicks;
		internal static int FrameSteps;

		public static void Pre(out long __state)
		{
			__state = On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				FrameTicks += System.Diagnostics.Stopwatch.GetTimestamp() - __state;
				FrameSteps++;
			}
		}
	}

	// ---------- process memory (Windows) ----------
	internal static class NativeMem
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct Counters
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
		}

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
		private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);

		private static bool _failed;

		// Working set and commit (private bytes) of the game process; false when unavailable.
		internal static bool Read(out long workingSet, out long commit)
		{
			workingSet = 0;
			commit = 0;
			if (_failed)
			{
				return false;
			}
			try
			{
				Counters c = new Counters();
				c.cb = (uint)Marshal.SizeOf(typeof(Counters));
				if (!K32GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb))
				{
					_failed = true;
					N.ULog.Warn(PerfToolsModule.Id, "K32GetProcessMemoryInfo returned false; process memory shown as n/a");
					return false;
				}
				workingSet = (long)c.WorkingSetSize.ToUInt64();
				commit = (long)c.PagefileUsage.ToUInt64();
				return true;
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Warn(PerfToolsModule.Id, "process memory unavailable: " + e.GetType().Name + ": " + e.Message);
				return false;
			}
		}
	}

	// ---------- sampler ----------
	internal static class Sampler
	{
		internal const int Ring = 300;
		internal static readonly float[] FrameMs = new float[Ring];
		internal static readonly float[] TickMs = new float[Ring];
		internal static readonly bool[] Gc = new bool[Ring];
		internal static int Head;
		internal static int Filled;

		// One-second window.
		private static readonly float[] _win = new float[4096];
		private static readonly float[] _sort = new float[4096];
		private static int _winN;
		private static double _winTick;
		private static double _winTickMax;
		private static int _winSteps;
		private static int _winGc;
		private static float _winStart = -1f;

		// Last completed second (overlay and Perf.log).
		internal static double Fps;
		internal static double FrameAvg;
		internal static double FrameP95;
		internal static double FrameMax;
		internal static double TickAvg;
		internal static double TickMax;
		internal static double StepsPerFrame;
		internal static int GcPerSec;
		internal static long MonoUsed;
		internal static long MonoHeap;
		internal static long UnityAlloc;
		internal static long UnityReserved;
		internal static long WorkingSet;
		internal static long Commit;
		internal static int Seconds;

		private static long _pendTicks;
		private static int _pendSteps;
		private static int _lastGc = -1;
		private static bool _pendGc;
		private static bool _running;

		internal static void SetRunning(bool on)
		{
			if (on == _running)
			{
				return;
			}
			_running = on;
			TickHook.On = on;
			TickHook.FrameTicks = 0;
			TickHook.FrameSteps = 0;
			_pendTicks = 0;
			_pendSteps = 0;
			_lastGc = -1;
			_winN = 0;
			_winStart = -1f;
		}

		internal static bool Running
		{
			get
			{
				return _running;
			}
		}

		// Called once per frame from the driver's Update. Time.unscaledDeltaTime of this frame covers the previous frame,
		// whose FixedUpdate time was collected by the previous call: the two are paired.
		// Returns true when a one-second window was completed.
		internal static bool Sample(out float frameMs, out float tickMs, out bool gc)
		{
			frameMs = Time.unscaledDeltaTime * 1000f;
			tickMs = (float)(_pendTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			int steps = _pendSteps;
			gc = _pendGc;
			_pendTicks = TickHook.FrameTicks;
			_pendSteps = TickHook.FrameSteps;
			TickHook.FrameTicks = 0;
			TickHook.FrameSteps = 0;
			int gcNow = GC.CollectionCount(0);
			_pendGc = _lastGc >= 0 && gcNow != _lastGc;
			_lastGc = gcNow;

			FrameMs[Head] = frameMs;
			TickMs[Head] = tickMs;
			Gc[Head] = gc;
			Head = (Head + 1) % Ring;
			if (Filled < Ring)
			{
				Filled++;
			}

			float now = Time.realtimeSinceStartup;
			if (_winStart < 0f)
			{
				_winStart = now;
				_winN = 0;
				_winTick = 0;
				_winTickMax = 0;
				_winSteps = 0;
				_winGc = 0;
				return false;
			}
			if (_winN < _win.Length)
			{
				_win[_winN++] = frameMs;
			}
			_winTick += tickMs;
			if (tickMs > _winTickMax)
			{
				_winTickMax = tickMs;
			}
			_winSteps += steps;
			if (gc)
			{
				_winGc++;
			}
			float span = now - _winStart;
			if (span < 1f || _winN == 0)
			{
				return false;
			}
			CloseWindow(span);
			_winStart = now;
			_winN = 0;
			_winTick = 0;
			_winTickMax = 0;
			_winSteps = 0;
			_winGc = 0;
			return true;
		}

		private static void CloseWindow(float span)
		{
			double sum = 0;
			float max = 0f;
			for (int i = 0; i < _winN; i++)
			{
				float v = _win[i];
				sum += v;
				if (v > max)
				{
					max = v;
				}
				_sort[i] = v;
			}
			Array.Sort(_sort, 0, _winN);
			int p = (int)Math.Ceiling(_winN * 0.95) - 1;
			if (p < 0)
			{
				p = 0;
			}
			Fps = _winN / span;
			FrameAvg = sum / _winN;
			FrameP95 = _sort[p];
			FrameMax = max;
			TickAvg = _winTick / _winN;
			TickMax = _winTickMax;
			StepsPerFrame = (double)_winSteps / _winN;
			GcPerSec = _winGc;
			Seconds++;
			try
			{
				MonoUsed = Profiler.GetMonoUsedSizeLong();
				MonoHeap = Profiler.GetMonoHeapSizeLong();
				UnityAlloc = Profiler.GetTotalAllocatedMemoryLong();
				UnityReserved = Profiler.GetTotalReservedMemoryLong();
			}
			catch (Exception)
			{
				MonoUsed = 0;
			}
			// The Mono heap counters read 0 in a release player: fall back to the GC (used only; heap unknown).
			if (MonoUsed <= 0)
			{
				MonoUsed = GC.GetTotalMemory(false);
			}
			long ws;
			long commit;
			if (NativeMem.Read(out ws, out commit))
			{
				WorkingSet = ws;
				Commit = commit;
			}
		}

		// Mean frame ms of the last n frames (overlay "now" value).
		internal static double RecentAvg(int n)
		{
			if (Filled == 0)
			{
				return 0;
			}
			if (n > Filled)
			{
				n = Filled;
			}
			double s = 0;
			for (int i = 1; i <= n; i++)
			{
				s += FrameMs[(Head - i + Ring) % Ring];
			}
			return s / n;
		}
	}

	// ---------- game context (read-only, guarded) ----------
	internal static class Ctx
	{
		internal static string Scene = string.Empty;

		internal static string Phase()
		{
			try
			{
				GameManager gm = GameManager.currentGameManager;
				if (gm == null)
				{
					return "-";
				}
				return gm.state.ToString();
			}
			catch (Exception)
			{
				return "?";
			}
		}

		internal static int Day()
		{
			try
			{
				if (PlayerModel.instance == null)
				{
					return -1;
				}
				return PlayerModel.instance.GetDay() + 1;
			}
			catch (Exception)
			{
				return -1;
			}
		}
	}

	// ---------- Perf.log ----------
	internal static class PerfLog
	{
		private static StreamWriter _w;
		private static bool _failed;
		private static readonly StringBuilder _sb = new StringBuilder(256);
		private static int _spikesThisSec;
		// Frames after an own expensive action (overlay build, profiler install) are not reported as spikes.
		internal static int SkipSpikes;

		internal static bool Open
		{
			get
			{
				return _w != null;
			}
		}

		internal static void EnsureOpen()
		{
			if (_w != null || _failed)
			{
				return;
			}
			try
			{
				_w = Paths.Open(Paths.PerfLog);
				_w.WriteLine("# PerfTools " + PerfToolsModule.Ver + " Perf.log started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv)
					+ " unity=" + Application.unityVersion + " screen=" + Screen.width + "x" + Screen.height
					+ " quality=" + QualitySettings.GetQualityLevel() + " vsync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate);
				_w.WriteLine("# columns: t(s since start) scene phase day speed fps frame(avg/p95/max ms) tick(avg/max ms, FixedUpdate) steps/frame gc/s mono(used/heap MB) unity(alloc/reserved MB) proc(ws/commit MB) prof");
				_w.WriteLine("# SPIKE lines: one frame >= PerfTools.SpikeMs; tick = FixedUpdate ms of that frame; gc = a GC ran in that frame");
				_w.Flush();
				N.ULog.Info(PerfToolsModule.Id, "perf log " + Paths.PerfLog);
			}
			catch (Exception e)
			{
				_failed = true;
				N.ULog.Error(PerfToolsModule.Id, "perf log open failed: " + e.Message);
			}
		}

		internal static void Note(string text)
		{
			if (_w == null)
			{
				return;
			}
			try
			{
				_w.WriteLine("# " + Time.realtimeSinceStartup.ToString("0.00", Fmt.Inv) + " " + text);
				_w.Flush();
			}
			catch (Exception)
			{
			}
		}

		internal static void Spike(float frameMs, float tickMs, bool gc)
		{
			if (SkipSpikes > 0)
			{
				SkipSpikes--;
				return;
			}
			if (_w == null || _spikesThisSec >= 20)
			{
				return;
			}
			_spikesThisSec++;
			try
			{
				_w.WriteLine("SPIKE t=" + Time.realtimeSinceStartup.ToString("0.00", Fmt.Inv) + " frame=" + Time.frameCount
					+ " ms=" + Fmt.F1(frameMs) + " tick=" + Fmt.F1(tickMs) + " gc=" + (gc ? "yes" : "no"));
			}
			catch (Exception)
			{
			}
		}

		internal static void Second()
		{
			_spikesThisSec = 0;
			if (_w == null)
			{
				return;
			}
			try
			{
				_sb.Length = 0;
				_sb.Append("t=").Append(Time.realtimeSinceStartup.ToString("0.0", Fmt.Inv));
				_sb.Append(" scene=").Append(Ctx.Scene);
				_sb.Append(" phase=").Append(Ctx.Phase());
				_sb.Append(" day=").Append(Ctx.Day());
				_sb.Append(" speed=").Append(Fmt.F1(Time.timeScale));
				_sb.Append(" fps=").Append(Fmt.F1(Sampler.Fps));
				_sb.Append(" frame=").Append(Fmt.F1(Sampler.FrameAvg)).Append('/').Append(Fmt.F1(Sampler.FrameP95)).Append('/').Append(Fmt.F1(Sampler.FrameMax));
				_sb.Append(" tick=").Append(Fmt.F2(Sampler.TickAvg)).Append('/').Append(Fmt.F1(Sampler.TickMax));
				_sb.Append(" steps=").Append(Fmt.F2(Sampler.StepsPerFrame));
				_sb.Append(" gc=").Append(Sampler.GcPerSec);
				_sb.Append(" mono=").Append(Fmt.Mb(Sampler.MonoUsed)).Append('/').Append(Fmt.Mb(Sampler.MonoHeap));
				_sb.Append(" unity=").Append(Fmt.Mb(Sampler.UnityAlloc)).Append('/').Append(Fmt.Mb(Sampler.UnityReserved));
				_sb.Append(" proc=").Append(Fmt.Mb(Sampler.WorkingSet)).Append('/').Append(Fmt.Mb(Sampler.Commit));
				_sb.Append(" prof=").Append(Prof.Armed ? "on" : "off");
				_w.WriteLine(_sb.ToString());
				_w.Flush();
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfToolsModule.Id, "perf log write failed, closed: " + e.Message);
				Close();
				_failed = true;
			}
		}

		internal static void Close()
		{
			if (_w == null)
			{
				return;
			}
			try
			{
				_w.Flush();
				_w.Close();
			}
			catch (Exception)
			{
			}
			_w = null;
		}
	}

	// ---------- overlay ----------
	internal static class Overlay
	{
		private const int GW = 300;
		private const int GH = 48;
		private const int Gap = 4;
		private const int TH = GH * 2 + Gap;
		private const float PanelW = 380f;
		private const float FrameScaleMs = 100f;
		private const float TickScaleMs = 33.3f;

		private static GameObject _root;
		private static RectTransform _panel;
		private static CanvasScaler _scaler;
		private static Text _text;
		private static Texture2D _tex;
		private static RawImage _graph;
		private static Color32[] _col;
		private static int _x;
		private static float _nextText;
		private static readonly StringBuilder _sb = new StringBuilder(512);
		internal static bool Visible;

		private static readonly Color32 Bg = new Color32(0, 0, 0, 0);
		private static readonly Color32 Green = new Color32(90, 220, 90, 255);
		private static readonly Color32 Yellow = new Color32(240, 210, 60, 255);
		private static readonly Color32 Red = new Color32(240, 70, 60, 255);
		private static readonly Color32 Blue = new Color32(90, 170, 255, 255);
		private static readonly Color32 Magenta = new Color32(255, 60, 255, 255);
		private static readonly Color32 Guide = new Color32(255, 255, 255, 70);

		internal static void Toggle()
		{
			SetVisible(!Visible);
		}

		internal static void SetVisible(bool on)
		{
			if (on && _root == null)
			{
				Build();
			}
			Visible = on;
			if (_root != null)
			{
				_root.SetActive(on);
			}
			_nextText = 0f;
			N.ULog.Info(PerfToolsModule.Id, "overlay " + (on ? "shown" : "hidden"));
			PerfLog.Note("overlay " + (on ? "shown" : "hidden"));
			PerfLog.SkipSpikes = 2;
		}

		private static void Build()
		{
			_root = new GameObject("PerfToolsOverlay");
			UnityEngine.Object.DontDestroyOnLoad(_root);
			Canvas canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			// Below the NailOptimization windows (30000+), above the game UI. No GraphicRaycaster: never takes clicks.
			canvas.sortingOrder = 29000;
			_scaler = _root.AddComponent<CanvasScaler>();
			_scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			CanvasGroup cg = _root.AddComponent<CanvasGroup>();
			cg.blocksRaycasts = false;
			cg.interactable = false;

			GameObject panel = new GameObject("Panel");
			panel.transform.SetParent(_root.transform, false);
			_panel = panel.AddComponent<RectTransform>();
			Image bg = panel.AddComponent<Image>();
			bg.color = new Color(0f, 0f, 0f, 0.55f);
			bg.raycastTarget = false;

			Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			GameObject tgo = new GameObject("Text");
			tgo.transform.SetParent(panel.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 1f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 1f);
			trt.anchoredPosition = new Vector2(0f, -4f);
			trt.sizeDelta = new Vector2(-12f, 112f);
			_text = tgo.AddComponent<Text>();
			_text.font = font;
			_text.fontSize = 13;
			_text.color = Color.white;
			_text.supportRichText = true;
			_text.alignment = TextAnchor.UpperLeft;
			_text.horizontalOverflow = HorizontalWrapMode.Overflow;
			_text.verticalOverflow = VerticalWrapMode.Overflow;
			_text.raycastTarget = false;
			Shadow sh = tgo.AddComponent<Shadow>();
			sh.effectColor = new Color(0f, 0f, 0f, 0.9f);

			_tex = new Texture2D(GW, TH, TextureFormat.RGBA32, false);
			_tex.wrapMode = TextureWrapMode.Repeat;
			_tex.filterMode = FilterMode.Point;
			Color32[] clear = new Color32[GW * TH];
			for (int i = 0; i < clear.Length; i++)
			{
				clear[i] = Bg;
			}
			_tex.SetPixels32(clear);
			_tex.Apply(false);
			_col = new Color32[TH];

			GameObject ggo = new GameObject("Graph");
			ggo.transform.SetParent(panel.transform, false);
			RectTransform grt = ggo.AddComponent<RectTransform>();
			grt.anchorMin = new Vector2(0.5f, 0f);
			grt.anchorMax = new Vector2(0.5f, 0f);
			grt.pivot = new Vector2(0.5f, 0f);
			grt.anchoredPosition = new Vector2(0f, 6f);
			grt.sizeDelta = new Vector2(PanelW - 12f, TH);
			_graph = ggo.AddComponent<RawImage>();
			_graph.texture = _tex;
			_graph.raycastTarget = false;

			AddLabel(panel.transform, font, "frame ms (16.7 / 33.3 / 66.7, top 100)", 6f + GH + Gap + GH - 12f);
			AddLabel(panel.transform, font, "tick ms (FixedUpdate, 8.3 / 16.7)", 6f + GH - 12f);
			Relayout();
			N.ULog.Info(PerfToolsModule.Id, "overlay built");
		}

		private static void AddLabel(Transform parent, Font font, string text, float y)
		{
			GameObject go = new GameObject("Label");
			go.transform.SetParent(parent, false);
			RectTransform rt = go.AddComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.5f, 0f);
			rt.anchorMax = new Vector2(0.5f, 0f);
			rt.pivot = new Vector2(0.5f, 0f);
			rt.anchoredPosition = new Vector2(0f, y);
			rt.sizeDelta = new Vector2(PanelW - 18f, 12f);
			Text t = go.AddComponent<Text>();
			t.font = font;
			t.fontSize = 10;
			t.color = new Color(1f, 1f, 1f, 0.6f);
			t.alignment = TextAnchor.UpperLeft;
			t.text = text;
			t.raycastTarget = false;
		}

		internal static void Relayout()
		{
			if (_panel == null)
			{
				return;
			}
			_scaler.scaleFactor = Cfg.Scale.Value;
			int c = Cfg.Corner.Value;
			float ax = (c == 0 || c == 2) ? 0f : 1f;
			float ay = (c == 0 || c == 1) ? 1f : 0f;
			_panel.anchorMin = new Vector2(ax, ay);
			_panel.anchorMax = new Vector2(ax, ay);
			_panel.pivot = new Vector2(ax, ay);
			_panel.anchoredPosition = new Vector2(ax == 0f ? 6f : -6f, ay == 0f ? 6f : -6f);
			_panel.sizeDelta = new Vector2(PanelW, 124f + TH);
		}

		// Called every frame while visible.
		internal static void Frame(float frameMs, float tickMs, bool gc)
		{
			if (_tex == null)
			{
				return;
			}
			int fh = Height(frameMs, FrameScaleMs);
			int th = Height(tickMs, TickScaleMs);
			Color32 fc = frameMs <= 16.7f ? Green : (frameMs <= 33.3f ? Yellow : Red);
			Color32 tc = tickMs <= 8.3f ? Blue : (tickMs <= 16.7f ? Yellow : Red);
			int g1 = Height(16.7f, FrameScaleMs);
			int g2 = Height(33.3f, FrameScaleMs);
			int g3 = Height(66.7f, FrameScaleMs);
			int t1 = Height(8.3f, TickScaleMs);
			int t2 = Height(16.7f, TickScaleMs);
			for (int y = 0; y < GH; y++)
			{
				// Lower graph: tick; upper graph: frame (texture y grows upward).
				Color32 v = y < th ? tc : Bg;
				if (v.a == 0 && (y == t1 || y == t2))
				{
					v = Guide;
				}
				_col[y] = v;
				Color32 u = y < fh ? fc : Bg;
				if (u.a == 0 && (y == g1 || y == g2 || y == g3))
				{
					u = Guide;
				}
				_col[GH + Gap + y] = u;
			}
			for (int y = GH; y < GH + Gap; y++)
			{
				_col[y] = Bg;
			}
			if (gc)
			{
				_col[TH - 1] = Magenta;
				_col[TH - 2] = Magenta;
			}
			_tex.SetPixels32(_x, 0, 1, TH, _col);
			_tex.Apply(false);
			_x = (_x + 1) % GW;
			_graph.uvRect = new Rect((float)_x / GW, 0f, 1f, 1f);
		}

		private static int Height(float ms, float scale)
		{
			int h = (int)(ms / scale * GH);
			if (h < 0)
			{
				return 0;
			}
			return h > GH ? GH : h;
		}

		internal static void Text()
		{
			if (_text == null)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (now < _nextText)
			{
				return;
			}
			_nextText = now + 0.25f;
			double recent = Sampler.RecentAvg(15);
			_sb.Length = 0;
			_sb.Append("<b>NailOptimization PerfTools</b>  <color=#aaaaaa>[").Append(Cfg.Key.Text).Append("]</color>\n");
			_sb.Append("FPS <b>").Append(Fmt.F1(recent > 0 ? 1000.0 / recent : 0)).Append("</b> (1s ").Append(Fmt.F1(Sampler.Fps)).Append(")  frame ");
			_sb.Append(Fmt.F1(Sampler.FrameAvg)).Append(" ms p95 ").Append(Fmt.F1(Sampler.FrameP95)).Append(" max ").Append(Fmt.F1(Sampler.FrameMax)).Append('\n');
			_sb.Append("Tick ").Append(Fmt.F2(Sampler.TickAvg)).Append(" ms/frame  max ").Append(Fmt.F1(Sampler.TickMax));
			_sb.Append("  steps ").Append(Fmt.F2(Sampler.StepsPerFrame)).Append("  speed x").Append(Fmt.F1(Time.timeScale)).Append('\n');
			_sb.Append("GC ").Append(Sampler.GcPerSec).Append("/s (total ").Append(GC.CollectionCount(0)).Append(")  Mono ");
			_sb.Append(Fmt.Mb(Sampler.MonoUsed));
			if (Sampler.MonoHeap > 0)
			{
				_sb.Append('/').Append(Fmt.Mb(Sampler.MonoHeap));
			}
			_sb.Append(" MB\n");
			_sb.Append("Unity ").Append(Fmt.Mb(Sampler.UnityAlloc)).Append('/').Append(Fmt.Mb(Sampler.UnityReserved)).Append(" MB  Process ");
			_sb.Append(Fmt.Mb(Sampler.WorkingSet)).Append(" MB (commit ").Append(Fmt.Mb(Sampler.Commit)).Append(")\n");
			_sb.Append(Ctx.Scene).Append("  ").Append(Ctx.Phase());
			int day = Ctx.Day();
			if (day > 0)
			{
				_sb.Append("  day ").Append(day);
			}
			_sb.Append("  profiler ");
			if (Prof.Armed)
			{
				_sb.Append("<color=#ff7070>REC ").Append((int)Prof.SessionSeconds).Append("s</color> ").Append(Prof.Used).Append(" slots");
			}
			else
			{
				_sb.Append("off [").Append(Cfg.ProfKey.Text).Append(']');
			}
			_text.text = _sb.ToString();
		}
	}

	// ---------- public API (LobBridge dbg prof / LogViewer, by reflection) ----------
	public static class Api
	{
		public static bool OverlayVisible
		{
			get
			{
				return Overlay.Visible;
			}
		}

		public static void OverlayToggle()
		{
			Overlay.Toggle();
		}

		public static void OverlaySet(bool on)
		{
			Overlay.SetVisible(on);
		}

		public static bool ProfRunning
		{
			get
			{
				return Prof.Armed;
			}
		}

		public static string ProfStart()
		{
			return Prof.Start();
		}

		public static string ProfStop()
		{
			return Prof.Stop("api");
		}

		public static string ProfStatus()
		{
			return Prof.Status();
		}

		public static string AddTarget(string spec)
		{
			return Prof.AddTarget(spec);
		}

		public static string ProfLogPath
		{
			get
			{
				return Paths.ProfLog;
			}
		}

		public static string PerfLogPath
		{
			get
			{
				return Paths.PerfLog;
			}
		}

		// One line of the last completed second (same fields as a Perf.log line).
		public static string Snapshot()
		{
			return "fps=" + Fmt.F1(Sampler.Fps) + " frame=" + Fmt.F1(Sampler.FrameAvg) + "/" + Fmt.F1(Sampler.FrameP95) + "/" + Fmt.F1(Sampler.FrameMax)
				+ " tick=" + Fmt.F2(Sampler.TickAvg) + "/" + Fmt.F1(Sampler.TickMax) + " gc=" + Sampler.GcPerSec
				+ " mono=" + Fmt.Mb(Sampler.MonoUsed) + " proc=" + Fmt.Mb(Sampler.WorkingSet) + " sampler=" + (Sampler.Running ? "on" : "off");
		}
	}

	// ---------- profiler ----------
	internal sealed class TargetSpec
	{
		internal string Line = string.Empty;
		internal string TypeSpec = string.Empty;
		internal string Method = string.Empty;
		internal double ThresholdMs = 5.0;
		internal bool Callers;
		internal bool Force;
		internal int Params = -1;
	}

	internal static class Prof
	{
		internal const int Slots = 64;
		private const int Buckets = 240;
		private const int EvCap = 4096;
		private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		internal static bool Armed;
		internal static int MainThread;
		internal static int Used;

		private static readonly string[] _name = new string[Slots];
		private static readonly long[] _thr = new long[Slots];
		private static readonly bool[] _callers = new bool[Slots];
		private static readonly int[] _overloads = new int[Slots];
		// Window (since the last summary) and session counters.
		private static readonly long[] _wCalls = new long[Slots];
		private static readonly long[] _wTotal = new long[Slots];
		private static readonly long[] _wMax = new long[Slots];
		private static readonly long[] _wOver = new long[Slots];
		private static readonly int[] _wHist = new int[Slots * Buckets];
		private static readonly long[] _sCalls = new long[Slots];
		private static readonly long[] _sTotal = new long[Slots];
		private static readonly long[] _sMax = new long[Slots];
		private static readonly long[] _sOver = new long[Slots];
		private static readonly int[] _sHist = new int[Slots * Buckets];
		private static readonly long[] _offThread = new long[Slots];

		// Event ring (filled in Postfixes, drained by the driver).
		private static readonly int[] _evSlot = new int[EvCap];
		private static readonly float[] _evMs = new float[EvCap];
		private static readonly int[] _evFrame = new int[EvCap];
		private static readonly int[] _evGc = new int[EvCap];
		private static readonly float[] _evT = new float[EvCap];
		private static readonly string[] _evCaller = new string[EvCap];
		private static int _evHead;
		private static int _evCount;
		private static long _evLost;
		private static int _stacksThisSec;

		private static bool _installed;
		private static HarmonyInstance _harmony;
		private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
		private static readonly List<string> _report = new List<string>();

		private static StreamWriter _w;
		private static long _written;
		private static int _events;
		private static bool _eventsCapped;
		private static bool _sizeCapped;
		private static float _start;
		private static float _nextSummary;
		private static int _session;

		internal static float SessionSeconds
		{
			get
			{
				return Armed ? Time.realtimeSinceStartup - _start : 0f;
			}
		}

		// ----- hot path (Postfix of every slot) -----
		internal static void End(int slot, long start)
		{
			if (!Armed)
			{
				return;
			}
			long d = System.Diagnostics.Stopwatch.GetTimestamp() - start;
			if (Thread.CurrentThread.ManagedThreadId != MainThread)
			{
				_offThread[slot]++;
				return;
			}
			_wCalls[slot]++;
			_wTotal[slot] += d;
			if (d > _wMax[slot])
			{
				_wMax[slot] = d;
			}
			_wHist[slot * Buckets + Bucket(d)]++;
			if (d < _thr[slot])
			{
				return;
			}
			_wOver[slot]++;
			int i = (_evHead + _evCount) % EvCap;
			if (_evCount == EvCap)
			{
				_evLost++;
				return;
			}
			_evCount++;
			_evSlot[i] = slot;
			_evMs[i] = (float)(d * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			_evFrame[i] = Time.frameCount;
			_evGc[i] = GC.CollectionCount(0);
			_evT[i] = Time.realtimeSinceStartup;
			_evCaller[i] = null;
			if (_callers[slot] && _stacksThisSec < 20 && !_eventsCapped && !_sizeCapped)
			{
				_stacksThisSec++;
				_evCaller[i] = Callers.Chain(_name[slot]);
			}
		}

		// Bucket of a duration: microseconds, 8 sub-buckets per power of two (about 12% resolution).
		private static int Bucket(long ticks)
		{
			long us = ticks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
			if (us < 8)
			{
				return (int)(us < 0 ? 0 : us);
			}
			int h = 3;
			while (h < 40 && (us >> (h + 1)) != 0)
			{
				h++;
			}
			int b = (h - 2) * 8 + (int)((us >> (h - 3)) & 7);
			return b >= Buckets ? Buckets - 1 : b;
		}

		private static double BucketUpperMs(int b)
		{
			if (b < 8)
			{
				return (b + 1) / 1000.0;
			}
			int h = b / 8 + 2;
			long mant = b % 8;
			return ((8 + mant + 1) << (h - 3)) / 1000.0;
		}

		private static double Percentile(int[] hist, int slot, long calls, double q)
		{
			if (calls <= 0)
			{
				return 0;
			}
			long need = (long)Math.Ceiling(calls * q);
			long acc = 0;
			int b0 = slot * Buckets;
			for (int b = 0; b < Buckets; b++)
			{
				acc += hist[b0 + b];
				if (acc >= need)
				{
					return BucketUpperMs(b);
				}
			}
			return BucketUpperMs(Buckets - 1);
		}

		// ----- session control (main thread) -----
		internal static string Start()
		{
			if (Armed)
			{
				return "already running";
			}
			if (!_installed)
			{
				InstallFromFile();
			}
			if (Used == 0)
			{
				return "no targets patched (see " + Paths.Targets + " and NailOptimization.log)";
			}
			_session++;
			Array.Clear(_wCalls, 0, Slots);
			Array.Clear(_wTotal, 0, Slots);
			Array.Clear(_wMax, 0, Slots);
			Array.Clear(_wOver, 0, Slots);
			Array.Clear(_wHist, 0, _wHist.Length);
			Array.Clear(_sCalls, 0, Slots);
			Array.Clear(_sTotal, 0, Slots);
			Array.Clear(_sMax, 0, Slots);
			Array.Clear(_sOver, 0, Slots);
			Array.Clear(_sHist, 0, _sHist.Length);
			Array.Clear(_offThread, 0, Slots);
			_evHead = 0;
			_evCount = 0;
			_evLost = 0;
			_events = 0;
			_eventsCapped = false;
			_sizeCapped = false;
			_written = 0;
			try
			{
				_w = Paths.Open(Paths.ProfLog);
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfToolsModule.Id, "profiler log open failed: " + e.Message);
				return "log open failed: " + e.Message;
			}
			_start = Time.realtimeSinceStartup;
			_nextSummary = _start + Cfg.ProfSummarySec.Value;
			Write("# PerfTools " + PerfToolsModule.Ver + " profiler session " + _session + " started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv)
				+ " scene=" + Ctx.Scene + " phase=" + Ctx.Phase() + " day=" + Ctx.Day() + " speed=" + Fmt.F1(Time.timeScale));
			Write("# limits: summary every " + Cfg.ProfSummarySec.Value + " s, auto stop " + Cfg.ProfMaxSec.Value + " s, events " + Cfg.ProfMaxEvents.Value + ", size " + Cfg.ProfMaxKB.Value + " KB");
			Write("# times are inclusive (nested probed calls are counted in their callers); p50/p95 from a histogram (~12%)");
			for (int i = 0; i < _report.Count; i++)
			{
				Write("# " + _report[i]);
			}
			Write("# columns: name calls total_ms avg_ms p50 p95 max_ms over_threshold");
			Flush();
			Armed = true;
			PerfLog.SkipSpikes = 2;
			N.ULog.Info(PerfToolsModule.Id, "profiler session " + _session + " started, " + Used + " targets");
			PerfLog.Note("profiler session " + _session + " started");
			return "started session " + _session + ", " + Used + " targets, log " + Paths.ProfLog;
		}

		internal static string Stop(string why)
		{
			if (!Armed)
			{
				return "not running";
			}
			Armed = false;
			DrainEvents();
			_final = true;
			Summary("window", false);
			Summary("SESSION TOTAL " + Fmt.F1(Time.realtimeSinceStartup - _start) + " s", true);
			long off = 0;
			for (int i = 0; i < Used; i++)
			{
				off += _offThread[i];
			}
			Write("# session " + _session + " stopped (" + why + "), events written " + _events + (_eventsCapped ? " (capped)" : string.Empty)
				+ ", lost (ring full) " + _evLost + ", off-main-thread calls " + off);
			Flush();
			_final = false;
			try
			{
				_w.Close();
			}
			catch (Exception)
			{
			}
			_w = null;
			N.ULog.Info(PerfToolsModule.Id, "profiler session " + _session + " stopped (" + why + ")");
			PerfLog.Note("profiler session " + _session + " stopped (" + why + ")");
			return "stopped session " + _session + " (" + why + "), log " + Paths.ProfLog;
		}

		internal static string Status()
		{
			return (Armed ? "running " + Fmt.F1(SessionSeconds) + " s" : "stopped") + ", session " + _session + ", slots " + Used + "/" + Slots
				+ ", installed " + _installed + ", events " + _events + ", log " + Paths.ProfLog;
		}

		// Driver tick: summaries, event drain, auto stop.
		internal static void Tick(bool newSecond)
		{
			if (newSecond)
			{
				_stacksThisSec = 0;
			}
			if (!Armed)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (newSecond)
			{
				DrainEvents();
				Flush();
			}
			if (now >= _nextSummary)
			{
				_nextSummary = now + Cfg.ProfSummarySec.Value;
				DrainEvents();
				Summary("window", false);
				Flush();
			}
			if (Cfg.ProfMaxSec.Value > 0 && now - _start >= Cfg.ProfMaxSec.Value)
			{
				Stop("time limit " + Cfg.ProfMaxSec.Value + " s");
			}
		}

		private static void DrainEvents()
		{
			while (_evCount > 0)
			{
				int i = _evHead;
				_evHead = (_evHead + 1) % EvCap;
				_evCount--;
				if (_events >= Cfg.ProfMaxEvents.Value)
				{
					if (!_eventsCapped)
					{
						_eventsCapped = true;
						Write("# events capped at " + Cfg.ProfMaxEvents.Value + " (PerfTools.ProfMaxEvents); summaries continue");
					}
					continue;
				}
				_events++;
				string line = "[f=" + _evFrame[i] + " t=" + _evT[i].ToString("0.00", Fmt.Inv) + " gc=" + _evGc[i] + "] " + _name[_evSlot[i]] + " " + Fmt.F1(_evMs[i]) + " ms";
				if (_evCaller[i] != null)
				{
					line += "  <- " + _evCaller[i];
					_evCaller[i] = null;
				}
				Write(line);
			}
		}

		// Writes the window table (and folds it into the session), or the session table.
		private static void Summary(string title, bool session)
		{
			long[] calls = session ? _sCalls : _wCalls;
			long[] total = session ? _sTotal : _wTotal;
			long[] max = session ? _sMax : _wMax;
			long[] over = session ? _sOver : _wOver;
			int[] hist = session ? _sHist : _wHist;
			double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			// Order by total time, descending (selection over at most 64 slots, no allocation beyond the index array).
			int[] order = new int[Used];
			int n = 0;
			for (int i = 0; i < Used; i++)
			{
				if (calls[i] > 0)
				{
					order[n++] = i;
				}
			}
			for (int a = 0; a < n; a++)
			{
				for (int b = a + 1; b < n; b++)
				{
					if (total[order[b]] > total[order[a]])
					{
						int t = order[a];
						order[a] = order[b];
						order[b] = t;
					}
				}
			}
			Write("== " + title + " t=" + Time.realtimeSinceStartup.ToString("0.0", Fmt.Inv) + " frame=" + Time.frameCount + " (" + n + " active targets)");
			for (int k = 0; k < n; k++)
			{
				int i = order[k];
				Write("  " + _name[i] + " calls=" + calls[i] + " total=" + Fmt.F1(total[i] * f) + " avg=" + (total[i] * f / calls[i]).ToString("0.000", Fmt.Inv)
					+ " p50=" + Percentile(hist, i, calls[i], 0.5).ToString("0.000", Fmt.Inv) + " p95=" + Percentile(hist, i, calls[i], 0.95).ToString("0.000", Fmt.Inv)
					+ " max=" + Fmt.F2(max[i] * f) + " over=" + over[i]);
			}
			if (!session)
			{
				for (int i = 0; i < Used; i++)
				{
					_sCalls[i] += _wCalls[i];
					_sTotal[i] += _wTotal[i];
					if (_wMax[i] > _sMax[i])
					{
						_sMax[i] = _wMax[i];
					}
					_sOver[i] += _wOver[i];
					_wCalls[i] = 0;
					_wTotal[i] = 0;
					_wMax[i] = 0;
					_wOver[i] = 0;
				}
				for (int j = 0; j < _wHist.Length; j++)
				{
					_sHist[j] += _wHist[j];
				}
				Array.Clear(_wHist, 0, _wHist.Length);
			}
		}

		private static void Write(string line)
		{
			if (_w == null)
			{
				return;
			}
			if (_written >= Cfg.ProfMaxKB.Value * 1024L && !_final)
			{
				if (!_sizeCapped)
				{
					_sizeCapped = true;
					try
					{
						_w.WriteLine("# size limit " + Cfg.ProfMaxKB.Value + " KB reached (PerfTools.ProfMaxKB); only the final summary follows");
					}
					catch (Exception)
					{
					}
				}
				return;
			}
			try
			{
				_w.WriteLine(line);
				_written += line.Length + 2;
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfToolsModule.Id, "profiler log write failed, log closed: " + e.Message);
				try
				{
					_w.Close();
				}
				catch (Exception)
				{
				}
				_w = null;
			}
		}

		// Set while Stop writes the final summary: it is written past the size limit.
		private static bool _final;

		private static void Flush()
		{
			if (_w == null)
			{
				return;
			}
			try
			{
				_w.Flush();
			}
			catch (Exception)
			{
			}
		}

		// ----- targets -----
		internal static string AddTarget(string spec)
		{
			if (!_installed)
			{
				InstallFromFile();
			}
			TargetSpec t = ParseLine(spec);
			if (t == null)
			{
				return "bad spec: " + spec;
			}
			string r = Install(t);
			if (Armed)
			{
				Write("# added at run time: " + r);
			}
			return r;
		}

		private static void InstallFromFile()
		{
			_installed = true;
			_harmony = HarmonyInstance.Create(PerfToolsModule.HarmonyId + ".Profiler");
			if (!File.Exists(Paths.Targets))
			{
				WriteDefaultTargets();
			}
			string[] lines;
			try
			{
				lines = File.ReadAllLines(Paths.Targets);
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfToolsModule.Id, "targets file read failed: " + e.Message);
				return;
			}
			for (int i = 0; i < lines.Length; i++)
			{
				string l = lines[i].Trim();
				if (l.Length == 0 || l[0] == '#')
				{
					continue;
				}
				TargetSpec t = ParseLine(l);
				if (t == null)
				{
					Report("line " + (i + 1) + " ignored (bad format): " + l);
					continue;
				}
				Install(t);
			}
			N.ULog.Info(PerfToolsModule.Id, "profiler targets installed: " + Used + " slots used");
		}

		private static void Report(string s)
		{
			_report.Add(s);
			N.ULog.Info(PerfToolsModule.Id, "profiler " + s);
		}

		private static TargetSpec ParseLine(string line)
		{
			string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return null;
			}
			int dot = parts[0].LastIndexOf('.');
			if (dot <= 0 || dot == parts[0].Length - 1)
			{
				return null;
			}
			TargetSpec t = new TargetSpec();
			t.Line = line.Trim();
			t.TypeSpec = parts[0].Substring(0, dot);
			t.Method = parts[0].Substring(dot + 1);
			for (int i = 1; i < parts.Length; i++)
			{
				string p = parts[i].ToLowerInvariant();
				double ms;
				if (p == "callers")
				{
					t.Callers = true;
				}
				else if (p == "force")
				{
					t.Force = true;
				}
				else if (p.StartsWith("params=", StringComparison.Ordinal))
				{
					int np;
					if (!int.TryParse(p.Substring(7), NumberStyles.Integer, Fmt.Inv, out np))
					{
						return null;
					}
					t.Params = np;
				}
				else if (double.TryParse(p, NumberStyles.Float, Fmt.Inv, out ms) && ms >= 0)
				{
					t.ThresholdMs = ms;
				}
				else
				{
					return null;
				}
			}
			return t;
		}

		private static string Install(TargetSpec t)
		{
			string label = t.TypeSpec + "." + t.Method;
			for (int i = 0; i < Used; i++)
			{
				if (string.Equals(_name[i], label, StringComparison.Ordinal))
				{
					return label + ": already patched (slot " + i + ")";
				}
			}
			if (Used >= Slots)
			{
				Report("skipped " + label + ": no free slot (" + Slots + " max)");
				return label + ": no free slot";
			}
			Type type = ResolveType(t.TypeSpec);
			if (object.ReferenceEquals(type, null))
			{
				Report("skipped " + label + ": type not found");
				return label + ": type not found";
			}
			List<MethodInfo> found = new List<MethodInfo>();
			MethodInfo[] all = type.GetMethods(All);
			for (int i = 0; i < all.Length; i++)
			{
				MethodInfo m = all[i];
				if (m.Name != t.Method || m.IsAbstract || m.ContainsGenericParameters)
				{
					continue;
				}
				if (t.Params >= 0 && m.GetParameters().Length != t.Params)
				{
					continue;
				}
				found.Add(m);
			}
			if (found.Count == 0)
			{
				Report("skipped " + label + ": method not found (declared on " + type.FullName + ")");
				return label + ": method not found";
			}
			int slot = Used;
			Type slotType = SlotTable.Get(slot);
			HarmonyMethod pre = N.PatchUtil.WithPriority(slotType.GetMethod("Pre", BindingFlags.Public | BindingFlags.Static), 800);
			HarmonyMethod post = N.PatchUtil.WithPriority(slotType.GetMethod("Post", BindingFlags.Public | BindingFlags.Static), 0);
			int ok = 0;
			StringBuilder skipped = new StringBuilder();
			for (int i = 0; i < found.Count; i++)
			{
				MethodInfo m = found[i];
				string why = Safety.Check(m);
				string sig = m.Name + "(" + m.GetParameters().Length + ")";
				if (why != null && !t.Force)
				{
					skipped.Append(' ').Append(sig).Append(": unsafe, ").Append(why).Append(';');
					continue;
				}
				try
				{
					_harmony.Patch(m, pre, post, null);
					ok++;
					if (why != null)
					{
						N.ULog.Warn(PerfToolsModule.Id, "profiler FORCED patch of " + label + " " + sig + " despite: " + why + " (GL-53: its handlers do not run while patched)");
					}
				}
				catch (Exception e)
				{
					skipped.Append(' ').Append(sig).Append(": patch failed ").Append(e.GetType().Name).Append(';');
				}
			}
			if (ok == 0)
			{
				Report("skipped " + label + ":" + skipped);
				return label + ": not patched:" + skipped;
			}
			_name[slot] = label;
			_thr[slot] = (long)(t.ThresholdMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
			_callers[slot] = t.Callers;
			_overloads[slot] = ok;
			Used++;
			string res = "slot " + slot + " " + label + " [" + type.FullName + ", " + type.Assembly.GetName().Name + "] overloads=" + ok + " threshold=" + t.ThresholdMs.ToString("0.###", Fmt.Inv) + "ms"
				+ (t.Callers ? " callers" : string.Empty) + (t.Force ? " FORCE" : string.Empty) + (skipped.Length > 0 ? " (skipped:" + skipped + ")" : string.Empty);
			Report(res);
			return res;
		}

		// "@Owner.member" = the type of field/property 'member' of class Owner; otherwise a type name (global types first).
		private static Type ResolveType(string spec)
		{
			if (spec.Length > 1 && spec[0] == '@')
			{
				int dot = spec.IndexOf('.');
				if (dot < 0)
				{
					return null;
				}
				Type owner = FindType(spec.Substring(1, dot - 1));
				if (object.ReferenceEquals(owner, null))
				{
					return null;
				}
				string member = spec.Substring(dot + 1);
				BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				FieldInfo fi = owner.GetField(member, f);
				if (!object.ReferenceEquals(fi, null))
				{
					return fi.FieldType;
				}
				PropertyInfo pi = owner.GetProperty(member, f);
				if (!object.ReferenceEquals(pi, null))
				{
					return pi.PropertyType;
				}
				return null;
			}
			return FindType(spec);
		}

		private static Type FindType(string name)
		{
			Type cached;
			if (_types.TryGetValue(name, out cached))
			{
				return cached;
			}
			Type found = N.TypeCache.Resolve(name);
			if (object.ReferenceEquals(found, null))
			{
				Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < asms.Length && object.ReferenceEquals(found, null); i++)
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
					for (int j = 0; j < types.Length; j++)
					{
						Type t = types[j];
						if (!object.ReferenceEquals(t, null) && t.Name == name)
						{
							found = t;
							break;
						}
					}
				}
			}
			_types[name] = found;
			return found;
		}

		private static void WriteDefaultTargets()
		{
			string[] lines = new string[]
			{
				"# PerfTools profiler targets (NailOptimization). One target per line:",
				"#   Type.Method [threshold_ms] [callers] [params=N] [force]",
				"# threshold_ms  calls at or above it are written as event lines (default 5); every call is counted in the summaries.",
				"# callers       event lines also get the caller chain (a stack walk per event, at most 20 per second).",
				"# params=N      only overloads with N parameters (default: all overloads of that name).",
				"# force         patch even when the method has exception handlers. UNSAFE: Harmony 1.0.9.1 drops them while",
				"#               patched (a catch stops catching, a lock is never released). Default: such methods are skipped.",
				"# Type can be '@Owner.member' = the type of field/property 'member' of class Owner (e.g. @WorkerUnit.animChanger).",
				"# Targets are patched at the first profiler session (PerfTools.ProfKey, default Shift+F3) and stay patched until the",
				"# game is closed; while no session runs they cost one flag check per call. Edit, then restart the game,",
				"# or add one live: LobBridge 'dbg prof add Type.Method 2'. Max 64 targets. Lines starting with # are ignored.",
				"",
				"# --- frame roots (cheap, always useful) ---",
				"GameManager.FixedUpdate 30",
				"# Creature/Agent/OfficerManager.OnFixedUpdate, AgentModel.OnFixedUpdate and CreatureModel.OnFixedUpdate contain",
				"# exception handlers (GL-53): they are never patched (the skip is reported); probe their callees instead.",
				"OfficerModel.OnFixedUpdate 5",
				"",
				"# --- per-frame unit and UI costs (measured 2026-10-28: the frame cost is mostly outside FixedUpdate) ---",
				"AgentUnit.Update 2",
				"WorkerSpriteSetter.LateUpdate 3",
				"AnimatorUtil.HasParameter 1",
				"SkeletonRenderer.LateUpdate 3",
				"SoundEffectPlayer.PlayOnce_Mod 3 params=6",
				"MovableObjectNode.GetDistance 1 params=2",
				"OrdealManager.OnFixedUpdate 10",
				"",
				"# --- agents, panic, pathing (SlowProbe v8 list; enable what you investigate) ---",
				"#AgentModel.ProcessAction 5",
				"#WorkerCommandQueue.Execute 5",
				"GraphAstar.SearchPath 3",
				"#PanicViolence.Execute 3",
				"#PanicRoaming.Execute 3",
				"#PanicOpenIsolate.Execute 3",
				"#PanicSuicideExecutor.Execute 3",
				"#AgentModel.OnDie 5",
				"#AgentModel.Panic 5",
				"#AgentModel.PanicReadyComplete 5",
				"#AgentModel.MoveToNode 3 callers",
				"#PassageObjectModel.GetEnteredTargets 1.5",
				"#WorkerModel.TakeDamage 1.5 callers",
				"#MapGraph.GetCreatureRoamingPoint 3",
				"#OrdealCreatureModel.OnFixedUpdate 5",
				"",
				"# --- animation, sound, Spine ---",
				"#@WorkerUnit.animChanger.ChangeAnimator 3 callers",
				"#AtlasUtilities.ToAtlasRegionPMAClone 5 callers",
				"#SoundEffectPlayer.Play 5",
				"#SkeletonRenderer.Awake 20",
				"#Prefab.LoadPrefab 50",
				"",
				"# --- effects (NailOptimization modules act here) ---",
				"#DamageEffect.Invoker 1",
				"#DamageParticleEffect.Invoker 1",
				"#EffectInvoker.Invoker 1",
				"#AgentModel.MakeRecoverEffect 1",
			};
			try
			{
				File.WriteAllLines(Paths.Targets, lines);
				N.ULog.Info(PerfToolsModule.Id, "default targets file written: " + Paths.Targets);
			}
			catch (Exception e)
			{
				N.ULog.Error(PerfToolsModule.Id, "default targets file write failed: " + e.Message);
			}
		}
	}

	// GL-53 check: Harmony 1.0.9.1 copies the original without its exception handlers.
	internal static class Safety
	{
		// null = safe; otherwise the reason.
		internal static string Check(MethodInfo m)
		{
			if ((m.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
			{
				return "extern/internal call";
			}
			MethodBody body;
			try
			{
				body = m.GetMethodBody();
			}
			catch (Exception e)
			{
				return "body not readable (" + e.GetType().Name + ")";
			}
			if (body == null)
			{
				return "no IL body";
			}
			IList<ExceptionHandlingClause> clauses = body.ExceptionHandlingClauses;
			if (clauses == null || clauses.Count == 0)
			{
				return null;
			}
			byte[] il = body.GetILAsByteArray();
			for (int i = 0; i < clauses.Count; i++)
			{
				ExceptionHandlingClause c = clauses[i];
				if (c.Flags != ExceptionHandlingClauseOptions.Finally)
				{
					return (c.Flags == ExceptionHandlingClauseOptions.Clause ? "catch" : c.Flags.ToString().ToLowerInvariant()) + " handler (GL-53)";
				}
				if (!EnumeratorDispose(m, il, c.HandlerOffset, c.HandlerLength))
				{
					return "finally that is not a collection enumerator Dispose (lock/using, GL-53)";
				}
			}
			return null;
		}

		// Accepts exactly: ldloca.s X | ldloca X ; constrained. T ; callvirt IDisposable::Dispose ; endfinally
		// where T is a nested Enumerator of System.Collections.Generic (its Dispose is a no-op).
		private static bool EnumeratorDispose(MethodInfo m, byte[] il, int off, int len)
		{
			if (il == null || off < 0 || off + len > il.Length)
			{
				return false;
			}
			int p = off;
			if (len == 14 && il[p] == 0x12)
			{
				p += 2;
			}
			else if (len == 16 && il[p] == 0xFE && il[p + 1] == 0x0D)
			{
				p += 4;
			}
			else
			{
				return false;
			}
			if (il[p] != 0xFE || il[p + 1] != 0x16)
			{
				return false;
			}
			int typeTok = BitConverter.ToInt32(il, p + 2);
			p += 6;
			if (il[p] != 0x6F)
			{
				return false;
			}
			int methTok = BitConverter.ToInt32(il, p + 1);
			p += 5;
			if (il[p] != 0xDC)
			{
				return false;
			}
			try
			{
				Module mod = m.Module;
				Type[] targs = m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
				Type[] margs = m.IsGenericMethod ? m.GetGenericArguments() : null;
				Type t = mod.ResolveType(typeTok, targs, margs);
				MethodBase d = mod.ResolveMethod(methTok, targs, margs);
				if (object.ReferenceEquals(d, null) || d.Name != "Dispose")
				{
					return false;
				}
				string full = t.FullName;
				if (full == null)
				{
					full = t.Namespace + "." + t.Name;
				}
				return t.Name.StartsWith("Enumerator", StringComparison.Ordinal) && full.StartsWith("System.Collections.Generic.", StringComparison.Ordinal);
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	internal static class Callers
	{
		// Caller chain of a probed call, taken in its Postfix: skips own frames, Harmony and the probed method's wrapper.
		internal static string Chain(string label)
		{
			try
			{
				int dot = label.LastIndexOf('.');
				string own = dot >= 0 ? label.Substring(dot + 1) : label;
				System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(false);
				StringBuilder sb = new StringBuilder();
				int found = 0;
				bool ownSkipped = false;
				bool inFixed = false;
				string root = null;
				for (int i = 0; i < st.FrameCount; i++)
				{
					System.Diagnostics.StackFrame fr = st.GetFrame(i);
					if (object.ReferenceEquals(fr, null))
					{
						continue;
					}
					MethodBase mb = fr.GetMethod();
					if (object.ReferenceEquals(mb, null))
					{
						continue;
					}
					Type t = mb.DeclaringType;
					if (object.ReferenceEquals(t, null))
					{
						continue;
					}
					string ns = t.Namespace;
					if (ns == "PerfTools" || ns == "Harmony" || ns == "System.Reflection")
					{
						continue;
					}
					if (!ownSkipped)
					{
						ownSkipped = true;
						if (mb.Name.StartsWith(own, StringComparison.Ordinal))
						{
							continue;
						}
					}
					if (t.Name == "GameManager" && mb.Name.StartsWith("FixedUpdate", StringComparison.Ordinal))
					{
						inFixed = true;
					}
					string name = t.Name + "." + mb.Name;
					if (found < 6)
					{
						if (found > 0)
						{
							sb.Append(" <- ");
						}
						sb.Append(name);
						found++;
					}
					else
					{
						root = name;
					}
				}
				if (found == 0)
				{
					sb.Append('?');
				}
				if (root != null)
				{
					sb.Append(" ... root=").Append(root);
				}
				sb.Append(inFixed ? " [FU]" : " [noFU]");
				return sb.ToString();
			}
			catch (Exception)
			{
				return "?";
			}
		}
	}

	// ---------- driver ----------
	public class PerfToolsDriver : MonoBehaviour
	{
		private int _errors;

		public void Awake()
		{
			SceneManager.activeSceneChanged += OnScene;
			Ctx.Scene = SceneManager.GetActiveScene().name;
		}

		private void OnScene(Scene a, Scene b)
		{
			Ctx.Scene = b.name;
			PerfLog.Note("scene " + b.name);
		}

		public void Update()
		{
			try
			{
				if (!N.UiHost.HotkeyBlocked)
				{
					if (Cfg.ProfKey.Down())
					{
						string r = Prof.Armed ? Prof.Stop("key") : Prof.Start();
						N.ULog.Info(PerfToolsModule.Id, "profiler key: " + r);
					}
					else if (Cfg.Key.Down())
					{
						Overlay.Toggle();
					}
				}
				bool need = Overlay.Visible || Prof.Armed;
				Sampler.SetRunning(need);
				bool second = false;
				if (need)
				{
					float frameMs;
					float tickMs;
					bool gc;
					second = Sampler.Sample(out frameMs, out tickMs, out gc);
					if (Cfg.LogPerf.On)
					{
						PerfLog.EnsureOpen();
						if (frameMs >= Cfg.SpikeMs.Value && frameMs >= Sampler.FrameAvg * 1.8 && Time.frameCount > 2)
						{
							PerfLog.Spike(frameMs, tickMs, gc);
						}
						if (second)
						{
							PerfLog.Second();
						}
					}
					if (Overlay.Visible)
					{
						Overlay.Frame(frameMs, tickMs, gc);
						Overlay.Text();
					}
				}
				Prof.Tick(second);
			}
			catch (Exception e)
			{
				_errors++;
				if (_errors <= 5)
				{
					N.ULog.Error(PerfToolsModule.Id, "driver error: " + e);
				}
			}
		}

		public void OnApplicationQuit()
		{
			try
			{
				if (Prof.Armed)
				{
					Prof.Stop("quit");
				}
				PerfLog.Close();
			}
			catch (Exception)
			{
			}
		}
	}

	internal static class SlotTable
	{
		internal static Type Get(int i)
		{
			return Types[i];
		}

		private static readonly Type[] Types = new Type[]
		{
			typeof(S00),
			typeof(S01),
			typeof(S02),
			typeof(S03),
			typeof(S04),
			typeof(S05),
			typeof(S06),
			typeof(S07),
			typeof(S08),
			typeof(S09),
			typeof(S10),
			typeof(S11),
			typeof(S12),
			typeof(S13),
			typeof(S14),
			typeof(S15),
			typeof(S16),
			typeof(S17),
			typeof(S18),
			typeof(S19),
			typeof(S20),
			typeof(S21),
			typeof(S22),
			typeof(S23),
			typeof(S24),
			typeof(S25),
			typeof(S26),
			typeof(S27),
			typeof(S28),
			typeof(S29),
			typeof(S30),
			typeof(S31),
			typeof(S32),
			typeof(S33),
			typeof(S34),
			typeof(S35),
			typeof(S36),
			typeof(S37),
			typeof(S38),
			typeof(S39),
			typeof(S40),
			typeof(S41),
			typeof(S42),
			typeof(S43),
			typeof(S44),
			typeof(S45),
			typeof(S46),
			typeof(S47),
			typeof(S48),
			typeof(S49),
			typeof(S50),
			typeof(S51),
			typeof(S52),
			typeof(S53),
			typeof(S54),
			typeof(S55),
			typeof(S56),
			typeof(S57),
			typeof(S58),
			typeof(S59),
			typeof(S60),
			typeof(S61),
			typeof(S62),
			typeof(S63)
		};
	}

	// Profiler slots: one class per target (Harmony 1.0.9.1 gives a patch no handle to its original). Generated: S00..S63.
	public static class S00
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(0, __state);
			}
		}
	}

	public static class S01
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(1, __state);
			}
		}
	}

	public static class S02
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(2, __state);
			}
		}
	}

	public static class S03
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(3, __state);
			}
		}
	}

	public static class S04
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(4, __state);
			}
		}
	}

	public static class S05
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(5, __state);
			}
		}
	}

	public static class S06
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(6, __state);
			}
		}
	}

	public static class S07
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(7, __state);
			}
		}
	}

	public static class S08
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(8, __state);
			}
		}
	}

	public static class S09
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(9, __state);
			}
		}
	}

	public static class S10
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(10, __state);
			}
		}
	}

	public static class S11
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(11, __state);
			}
		}
	}

	public static class S12
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(12, __state);
			}
		}
	}

	public static class S13
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(13, __state);
			}
		}
	}

	public static class S14
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(14, __state);
			}
		}
	}

	public static class S15
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(15, __state);
			}
		}
	}

	public static class S16
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(16, __state);
			}
		}
	}

	public static class S17
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(17, __state);
			}
		}
	}

	public static class S18
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(18, __state);
			}
		}
	}

	public static class S19
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(19, __state);
			}
		}
	}

	public static class S20
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(20, __state);
			}
		}
	}

	public static class S21
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(21, __state);
			}
		}
	}

	public static class S22
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(22, __state);
			}
		}
	}

	public static class S23
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(23, __state);
			}
		}
	}

	public static class S24
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(24, __state);
			}
		}
	}

	public static class S25
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(25, __state);
			}
		}
	}

	public static class S26
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(26, __state);
			}
		}
	}

	public static class S27
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(27, __state);
			}
		}
	}

	public static class S28
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(28, __state);
			}
		}
	}

	public static class S29
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(29, __state);
			}
		}
	}

	public static class S30
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(30, __state);
			}
		}
	}

	public static class S31
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(31, __state);
			}
		}
	}

	public static class S32
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(32, __state);
			}
		}
	}

	public static class S33
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(33, __state);
			}
		}
	}

	public static class S34
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(34, __state);
			}
		}
	}

	public static class S35
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(35, __state);
			}
		}
	}

	public static class S36
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(36, __state);
			}
		}
	}

	public static class S37
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(37, __state);
			}
		}
	}

	public static class S38
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(38, __state);
			}
		}
	}

	public static class S39
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(39, __state);
			}
		}
	}

	public static class S40
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(40, __state);
			}
		}
	}

	public static class S41
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(41, __state);
			}
		}
	}

	public static class S42
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(42, __state);
			}
		}
	}

	public static class S43
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(43, __state);
			}
		}
	}

	public static class S44
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(44, __state);
			}
		}
	}

	public static class S45
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(45, __state);
			}
		}
	}

	public static class S46
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(46, __state);
			}
		}
	}

	public static class S47
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(47, __state);
			}
		}
	}

	public static class S48
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(48, __state);
			}
		}
	}

	public static class S49
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(49, __state);
			}
		}
	}

	public static class S50
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(50, __state);
			}
		}
	}

	public static class S51
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(51, __state);
			}
		}
	}

	public static class S52
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(52, __state);
			}
		}
	}

	public static class S53
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(53, __state);
			}
		}
	}

	public static class S54
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(54, __state);
			}
		}
	}

	public static class S55
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(55, __state);
			}
		}
	}

	public static class S56
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(56, __state);
			}
		}
	}

	public static class S57
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(57, __state);
			}
		}
	}

	public static class S58
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(58, __state);
			}
		}
	}

	public static class S59
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(59, __state);
			}
		}
	}

	public static class S60
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(60, __state);
			}
		}
	}

	public static class S61
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(61, __state);
			}
		}
	}

	public static class S62
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(62, __state);
			}
		}
	}

	public static class S63
	{
		public static void Pre(out long __state)
		{
			__state = Prof.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
		}

		public static void Post(long __state)
		{
			if (__state != 0L)
			{
				Prof.End(63, __state);
			}
		}
	}
}
