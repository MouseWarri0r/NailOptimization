// LogViewer: NailOptimization module (api level 6). In-game log window with two tabs: NailOptimization.log and BaseMod Log.txt.
// Entry points come from Core UiActions: a title button left of Optimization and a slot in the NailUI day box.
// 1.1, LogViewer.AdvancedLog (default off, read at game start): own log in <NailOptimization>/Logs/ instead of BaseMod Log.txt
// (Harmony on LobotomyBaseMod.ModDebug, Application.logMessageReceived), shown in tab 2 with a column of warnings, errors and
// mod file diagnostics (ModScan), and an api 6 badge on the Logs buttons. Off: no Harmony patch, behavior of 1.0.
// 1.2, LogViewer.HideBenign (default on, live): known harmless Unity warnings/errors (Benign, plus LogViewer.BenignPatterns)
// are written to the advanced log as "INFO unity benign: <first line>", outside the error column and the badge.
// 1.4, HideBenign also covers the missing script warnings of day start and the BgmManager "Clip Is Null" error.
// 1.3, tab 3 "Profiler": Logs/Profiler.log of the PerfTools module, with a start/stop button (PerfTools.Api by reflection).
// No lock / ?. / interpolation / nameof / expression-bodied members / out var.
// Build: reference NailOptimization.dll, 0Harmony, Assembly-CSharp, UnityEngine, UnityEngine.UI, System.Xml. Output goes to <NailOptimization>/modules/.
// Files next to NailOptimization.dll:
//   Localization/<lang>/LogViewer.xml   (en = fallback)
//   Images/log_icon.png                 (title button and day slot icon)
// Files are read on a background thread: one open+read on the main thread cost 7.5-11.6 ms (probe P1). The worker uses System.IO
// and strings only; every Unity call and every list the UI reads stays on the main thread.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Xml;
using Harmony;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using N = NailOptimization;

namespace LogViewer
{
	// Kept empty: module DLLs ship an empty Harmony_Patch (dnSpy edit target; Add_On does not scan modules/).
	public class Harmony_Patch
	{
	}

	public class LogViewerModule : N.ModuleBase
	{
		public const string Id = "LogViewer";

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
				return "1.8";
			}
		}

		// Literal on purpose: a const from Core would be inlined at compile time.
		public override int RequiredApiLevel
		{
			get
			{
				return 7;
			}
		}

		public override void Init()
		{
			Win.Init();
		}
	}

	// Values copied from NailUI.Theme (modules do not reference each other).
	internal static class Theme
	{
		internal const float PanelW = 1040f;
		internal const float PanelH = 576f;
		internal const float RowH = 22f;
		internal const int TitleFont = 28;
		internal const int ButtonFont = 22;
		internal const int RowFont = 16;
		internal const int StatusFont = 15;
		internal const int WheelRows = 3;

		internal static readonly Color Solid = new Color(0f, 0f, 0f, 1f);
		internal static readonly Color Back = new Color(0f, 0f, 0f, 0.97f);
		internal static readonly Color Teal = new Color(0.18f, 0.74f, 0.64f, 1f);
		internal static readonly Color TealHot = new Color(0.37f, 0.88f, 0.78f, 1f);
		internal static readonly Color TealDark = new Color(0.09f, 0.34f, 0.29f, 1f);
		internal static readonly Color Yellow = new Color(1f, 1f, 0.63f, 1f);
		internal static readonly Color Dim = new Color(0.86f, 0.85f, 0.51f, 1f);
		internal static readonly Color Text = new Color(0.85f, 0.84f, 0.5f, 1f);
		internal static readonly Color DimText = new Color(0.43f, 0.43f, 0.28f, 1f);
		internal static readonly Color Red = new Color(1f, 0.36f, 0.3f, 1f);

		// Line colours by level (Lv.*).
		internal static readonly Color LineNormal = new Color(0.86f, 0.86f, 0.86f, 1f);
		internal static readonly Color LineWarn = new Color(1f, 0.85f, 0.25f, 1f);
		internal static readonly Color LineSafe = new Color(0.72f, 0.9f, 0.25f, 1f);
		internal static readonly Color LineError = new Color(1f, 0.36f, 0.3f, 1f);
		internal static readonly Color LineStack = new Color(0.5f, 0.5f, 0.5f, 1f);
		internal static readonly Color LineStackError = new Color(0.72f, 0.3f, 0.26f, 1f);
	}

	internal static class Lv
	{
		// Warnings known to change nothing in the game; shown yellow-green. Localization warnings stay out of this list.
		private static readonly string[] SafeWarns = new string[]
		{
			" detect CONTESTED ",
			" unity Provided type name is null or empty",
			" unity Releasing render texture that is set to be RenderTexture.active"
		};
		internal const byte Safe = 5;
		internal const byte Normal = 0;
		internal const byte Warn = 1;
		internal const byte Error = 2;
		internal const byte Stack = 3;
		internal const byte StackError = 4;

		internal static bool IsSafeWarn(string line, int from)
		{
			for (int i = 0; i < SafeWarns.Length; i++)
			{
				if (line.IndexOf(SafeWarns[i], from, StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		internal static Color Of(byte lv)
		{
			switch (lv)
			{
				case Warn:
					return Theme.LineWarn;
				case Safe:
					return Theme.LineSafe;
				case Error:
					return Theme.LineError;
				case Stack:
					return Theme.LineStack;
				case StackError:
					return Theme.LineStackError;
				default:
					return Theme.LineNormal;
			}
		}
	}

	// Left-column entry: absolute line number (Removed + index), level and short text.
	internal sealed class Mark
	{
		internal int Abs;
		internal byte Level;
		internal string Text;
	}

	// ---------- one log file ----------
	internal sealed class LogTab
	{
		internal const int MaxIssues = 2000;
		internal const int MarkChars = 400;
		internal const int MaxChunk = 1 << 20;
		internal const int MaxLineChars = 1000;

		internal string Path;
		internal bool BaseMod;
		// Advanced log: WARN/ERROR header lines become column entries; "diag" lines are grouped by kind.
		internal bool Adv;
		internal readonly List<Mark> Issues = new List<Mark>();
		internal readonly List<string> DiagKinds = new List<string>();
		internal readonly List<List<Mark>> DiagByKind = new List<List<Mark>>();
		internal int DiagCount;
		internal int MarkVersion;

		// Main thread only.
		internal readonly List<string> Lines = new List<string>();
		internal readonly List<byte> Levels = new List<byte>();
		internal int Version;
		internal List<string> Backlog = new List<string>();
		internal int BacklogPos;
		internal string Status = string.Empty;
		internal byte OwnLevel;
		internal int EntryStart = -1;
		internal bool InStack;
		internal bool EntryError;
		// Index of an empty line that followed message lines; decided by the next line (LogException or a blank line inside a message).
		private int _emptyAt = -1;
		private int _failLogs;

		// Worker only (ReadGate held).
		internal readonly object ReadGate = new object();
		internal long Offset;
		internal bool MissingReported;
		// A large file is read in MaxChunk steps without the poll pause until it is caught up.
		internal volatile bool More;

		// Shared (Gate held).
		internal readonly object Gate = new object();
		internal List<string> Pending = new List<string>();
		internal bool PendingReset;
		internal string PendingStatus;
		// Clear: a pass started before it is dropped (generation), the next pass jumps to the end of the file.
		internal int ClearGen;
		internal bool SkipRequested;
		private int _passGen;

		// Worker: reads complete lines added since Offset. A shorter file means it was truncated (new game start): read from 0.
		internal void Poll()
		{
			Monitor.Enter(ReadGate);
			try
			{
				bool skip;
				Monitor.Enter(Gate);
				try
				{
					_passGen = ClearGen;
					skip = SkipRequested;
					SkipRequested = false;
				}
				finally
				{
					Monitor.Exit(Gate);
				}
				PollInner(skip);
			}
			catch (Exception e)
			{
				Publish(null, false, "read failed: " + e.GetType().Name + ": " + e.Message);
			}
			finally
			{
				Monitor.Exit(ReadGate);
			}
		}

		private void PollInner(bool skip)
		{
			More = false;
			FileInfo fi = new FileInfo(Path);
			if (!fi.Exists)
			{
				if (!MissingReported)
				{
					MissingReported = true;
					Publish(null, Offset > 0L, "missing");
					Offset = 0L;
				}
				return;
			}
			if (MissingReported)
			{
				MissingReported = false;
				Publish(null, false, string.Empty);
			}
			long len = fi.Length;
			if (skip)
			{
				Offset = len;
				return;
			}
			bool reset = false;
			if (len < Offset)
			{
				Offset = 0L;
				reset = true;
			}
			if (len == Offset)
			{
				if (reset)
				{
					Publish(null, true, null);
				}
				return;
			}
			long want = len - Offset;
			int size = want > MaxChunk ? MaxChunk : (int)want;
			byte[] buf = new byte[size];
			int got = 0;
			using (FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				fs.Seek(Offset, SeekOrigin.Begin);
				while (got < size)
				{
					int n = fs.Read(buf, got, size - got);
					if (n <= 0)
					{
						break;
					}
					got += n;
				}
			}
			int end = -1;
			for (int i = got - 1; i >= 0; i--)
			{
				if (buf[i] == (byte)'\n')
				{
					end = i;
					break;
				}
			}
			if (end < 0)
			{
				// A partial last line waits for its newline, unless it fills a whole chunk.
				if (got < MaxChunk)
				{
					if (reset)
					{
						Publish(null, true, null);
					}
					return;
				}
				end = got - 1;
			}
			string text = Encoding.UTF8.GetString(buf, 0, end + 1);
			Offset += end + 1;
			More = Offset < len;
			string[] parts = text.Split('\n');
			List<string> lines = new List<string>(parts.Length);
			for (int i = 0; i < parts.Length; i++)
			{
				if (i == parts.Length - 1 && parts[i].Length == 0)
				{
					break;
				}
				lines.Add(Clean(parts[i]));
			}
			Publish(lines, reset, null);
		}

		private void Publish(List<string> lines, bool reset, string status)
		{
			Monitor.Enter(Gate);
			try
			{
				if (_passGen != ClearGen)
				{
					lines = null;
				}
				if (reset)
				{
					Pending.Clear();
					PendingReset = true;
				}
				if (lines != null)
				{
					Pending.AddRange(lines);
				}
				if (status != null)
				{
					PendingStatus = status;
				}
			}
			finally
			{
				Monitor.Exit(Gate);
			}
		}

		// Drops '\r', Unity rich-text tags and caps the length (rows never wrap).
		private static string Clean(string s)
		{
			if (s.Length > 0 && s[s.Length - 1] == '\r')
			{
				s = s.Substring(0, s.Length - 1);
			}
			if (s.IndexOf('<') >= 0)
			{
				s = StripTags(s);
			}
			if (s.Length > MaxLineChars)
			{
				s = s.Substring(0, MaxLineChars) + " ...";
			}
			return s;
		}

		private static readonly string[] _openTags = new string[] { "<color=", "<size=" };
		private static readonly string[] _plainTags = new string[] { "</color>", "</size>", "<b>", "</b>", "<i>", "</i>" };

		private static string StripTags(string s)
		{
			StringBuilder sb = new StringBuilder(s.Length);
			int i = 0;
			while (i < s.Length)
			{
				if (s[i] == '<')
				{
					int skip = TagLength(s, i);
					if (skip > 0)
					{
						i += skip;
						continue;
					}
				}
				sb.Append(s[i]);
				i++;
			}
			return sb.ToString();
		}

		private static int TagLength(string s, int at)
		{
			for (int k = 0; k < _plainTags.Length; k++)
			{
				if (string.CompareOrdinal(s, at, _plainTags[k], 0, _plainTags[k].Length) == 0)
				{
					return _plainTags[k].Length;
				}
			}
			for (int k = 0; k < _openTags.Length; k++)
			{
				if (string.CompareOrdinal(s, at, _openTags[k], 0, _openTags[k].Length) == 0)
				{
					int close = s.IndexOf('>', at);
					return close > at ? close - at + 1 : 0;
				}
			}
			return 0;
		}

		// Main thread: takes what the worker published. Returns true when the visible data changed.
		internal bool Drain(int maxLines, int cap)
		{
			bool changed = false;
			if (BacklogPos >= Backlog.Count)
			{
				bool reset = false;
				string status = null;
				Monitor.Enter(Gate);
				try
				{
					if (Pending.Count > 0)
					{
						List<string> t = Backlog;
						t.Clear();
						Backlog = Pending;
						Pending = t;
						BacklogPos = 0;
					}
					reset = PendingReset;
					PendingReset = false;
					status = PendingStatus;
					PendingStatus = null;
				}
				finally
				{
					Monitor.Exit(Gate);
				}
				if (reset)
				{
					ClearLines();
					changed = true;
				}
				if (status != null)
				{
					Status = status;
					changed = true;
					if (status.StartsWith("read failed", StringComparison.Ordinal) && _failLogs < 5)
					{
						_failLogs++;
						N.ULog.Warn("logviewer", status + " (" + Path + ")");
					}
				}
			}
			int n = 0;
			while (BacklogPos < Backlog.Count && n < maxLines)
			{
				Append(Backlog[BacklogPos]);
				BacklogPos++;
				n++;
			}
			if (n > 0)
			{
				if (Status.Length > 0)
				{
					Status = string.Empty;
				}
				Trim(cap);
				changed = true;
			}
			if (changed)
			{
				Version++;
			}
			return changed;
		}

		// Visual clear: drops everything read so far; the file is not touched and later lines still arrive.
		internal void ClearView()
		{
			Monitor.Enter(Gate);
			try
			{
				Pending.Clear();
				ClearGen++;
				SkipRequested = true;
			}
			finally
			{
				Monitor.Exit(Gate);
			}
			Backlog.Clear();
			BacklogPos = 0;
			ClearLines();
			Version++;
		}

		private void ClearLines()
		{
			Lines.Clear();
			Levels.Clear();
			Issues.Clear();
			DiagKinds.Clear();
			DiagByKind.Clear();
			DiagCount = 0;
			MarkVersion++;
			OwnLevel = Lv.Normal;
			EntryStart = -1;
			InStack = false;
			EntryError = false;
			_emptyAt = -1;
		}

		internal int Removed;

		private void Trim(int cap)
		{
			if (Lines.Count <= cap)
			{
				return;
			}
			int remove = Mathf.Max(cap / 10, Lines.Count - cap);
			Lines.RemoveRange(0, remove);
			Levels.RemoveRange(0, remove);
			if (EntryStart >= 0)
			{
				EntryStart = Mathf.Max(0, EntryStart - remove);
			}
			if (_emptyAt >= 0)
			{
				_emptyAt = Mathf.Max(0, _emptyAt - remove);
			}
			Removed += remove;
			int drop = 0;
			while (drop < Issues.Count && Issues[drop].Abs < Removed)
			{
				drop++;
			}
			if (drop > 0)
			{
				Issues.RemoveRange(0, drop);
				MarkVersion++;
			}
		}

		private void Append(string line)
		{
			if (BaseMod)
			{
				AppendBaseMod(line);
			}
			else
			{
				AppendOwn(line);
			}
		}

		// NailOptimization.log: "[HH:mm:ss.fff f=N] LEVEL src msg"; other lines (exception text) keep the previous level.
		private void AppendOwn(string line)
		{
			if (line.Length > 0 && line[0] == '[')
			{
				int k = line.IndexOf("] ", StringComparison.Ordinal);
				OwnLevel = Lv.Normal;
				if (k > 0)
				{
					if (string.CompareOrdinal(line, k + 2, "ERROR", 0, 5) == 0)
					{
						OwnLevel = Lv.Error;
					}
					else if (string.CompareOrdinal(line, k + 2, "WARN", 0, 4) == 0)
					{
						OwnLevel = Lv.IsSafeWarn(line, k + 2) ? Lv.Safe : Lv.Warn;
					}
					if (Adv && OwnLevel != Lv.Normal)
					{
						AddMark(line, k);
					}
				}
			}
			Lines.Add(line);
			Levels.Add(OwnLevel);
		}

		// "[HH:mm:ss.fff f=N] LEVEL src rest": diag lines go to DiagByKind (first token of rest = kind), the others to Issues.
		private void AddMark(string line, int k)
		{
			int lvEnd = line.IndexOf(' ', k + 2);
			if (lvEnd < 0)
			{
				return;
			}
			int srcEnd = line.IndexOf(' ', lvEnd + 1);
			if (srcEnd < 0)
			{
				return;
			}
			string time = k > 9 ? line.Substring(1, 8) : string.Empty;
			string rest = line.Substring(srcEnd + 1);
			Mark m = new Mark();
			m.Abs = Removed + Lines.Count;
			m.Level = OwnLevel;
			if (srcEnd - lvEnd - 1 == 4 && string.CompareOrdinal(line, lvEnd + 1, "diag", 0, 4) == 0)
			{
				int sp = rest.IndexOf(' ');
				string kind = sp > 0 ? rest.Substring(0, sp) : rest;
				m.Text = Short(sp > 0 ? rest.Substring(sp + 1) : rest);
				int ki = DiagKinds.IndexOf(kind);
				if (ki < 0)
				{
					DiagKinds.Add(kind);
					DiagByKind.Add(new List<Mark>());
					ki = DiagKinds.Count - 1;
				}
				DiagByKind[ki].Add(m);
				DiagCount++;
			}
			else
			{
				m.Text = time + " " + Short(rest);
				Issues.Add(m);
				if (Issues.Count > MaxIssues)
				{
					Issues.RemoveRange(0, MaxIssues / 10);
				}
			}
			MarkVersion++;
		}

		private static string Short(string s)
		{
			return s.Length > MarkChars ? s.Substring(0, MarkChars) + "..." : s;
		}

		// BaseMod Log.txt (format observed in probe P1): message line(s), then "   at ..." stack lines from Environment.StackTrace.
		// Error = a stack line of ModDebug.Debug_LogError, or an empty line right after message lines that is not followed by a stack
		// (Debug.LogException); the newest such entry is coloured when the next line arrives. Only the last message line turns red.
		private void AppendBaseMod(string line)
		{
			bool stack = line.StartsWith("   at ", StringComparison.Ordinal);
			if (_emptyAt >= 0)
			{
				int at = _emptyAt;
				_emptyAt = -1;
				if (!stack)
				{
					MarkError(Mathf.Max(EntryStart, at - 1), at);
					EntryStart = -1;
					InStack = false;
					EntryError = false;
				}
			}
			if (stack)
			{
				if (EntryStart < 0)
				{
					EntryStart = Lines.Count;
				}
				InStack = true;
				Lines.Add(line);
				Levels.Add(EntryError ? Lv.StackError : Lv.Stack);
				if (!EntryError && line.IndexOf("Debug_LogError", StringComparison.Ordinal) >= 0)
				{
					MarkEntryError();
				}
				return;
			}
			if (line.Length == 0)
			{
				if (!InStack && EntryStart >= 0)
				{
					_emptyAt = Lines.Count;
				}
				else
				{
					EntryStart = -1;
					InStack = false;
					EntryError = false;
				}
				Lines.Add(line);
				Levels.Add(Lv.Normal);
				return;
			}
			if (InStack || EntryStart < 0)
			{
				EntryStart = Lines.Count;
				InStack = false;
				EntryError = false;
			}
			Lines.Add(line);
			Levels.Add(Lv.Normal);
		}

		// Colours the stack of the entry and one message line directly above it: stackless BaseMod lines before an error
		// cannot be told from a multi-line message, so they keep the default colour.
		private void MarkEntryError()
		{
			EntryError = true;
			int from = Lines.Count - 1;
			while (from > 0 && from > EntryStart && Lines[from - 1].StartsWith("   at ", StringComparison.Ordinal))
			{
				from--;
			}
			if (from > 0 && from > EntryStart && Lines[from - 1].Length > 0)
			{
				from--;
			}
			MarkError(from, Levels.Count);
		}

		private void MarkError(int from, int to)
		{
			for (int i = Mathf.Max(0, from); i < to; i++)
			{
				byte lv = Levels[i];
				Levels[i] = lv == Lv.Stack || lv == Lv.StackError ? Lv.StackError : Lv.Error;
			}
		}
	}

	// ---------- background reader ----------
	internal static class Reader
	{
		private static volatile int _gen;
		private static volatile int _pollMs = 500;
		private static LogTab[] _tabs;

		internal static int PollMs
		{
			set
			{
				_pollMs = value;
			}
		}

		internal static void Start(LogTab[] tabs)
		{
			_tabs = tabs;
			_gen++;
			Thread t = new Thread(new ParameterizedThreadStart(Run));
			t.IsBackground = true;
			t.Name = "LogViewerReader";
			t.Start(_gen);
		}

		// The running thread ends after its current sleep; a newer Start never waits for it (ReadGate keeps polls apart).
		internal static void Stop()
		{
			_gen++;
		}

		private static void Run(object state)
		{
			int gen = (int)state;
			LogTab[] tabs = _tabs;
			while (gen == _gen)
			{
				bool more = false;
				for (int i = 0; i < tabs.Length && gen == _gen; i++)
				{
					tabs[i].Poll();
					more = more || tabs[i].More;
				}
				Thread.Sleep(more ? 1 : _pollMs);
			}
		}
	}

	// ---------- advanced log: writer (LogViewer.AdvancedLog) ----------
	// Own log file in <NailOptimization>/Logs/. Lines from any thread are queued; one worker thread owns the file.
	// The worker never calls Unity or ULog: its notes are handed to the main thread (PumpNotes).
	internal static class AdvLog
	{
		internal const int MaxQueued = 100000;
		internal const int KeepFiles = 5;
		internal const double RepeatSeconds = 2.0;

		internal static string FilePath = string.Empty;
		internal static DateTime StartTime;
		internal static bool Running;
		// Writer failure: the ModDebug prefixes let BaseMod write Log.txt again.
		internal static volatile bool Failed;

		private static readonly object _gate = new object();
		private static List<string> _queue = new List<string>();
		private static int _dropped;
		private static bool _stop;
		private static Thread _thread;
		private static int _mainThread;
		private static string _importPath;
		// Repeat collapse (_gate held).
		private static string _lastKey;
		private static string _lastHead;
		private static int _repeat;
		private static DateTime _lastAt;
		private static DateTime _firstAt;
		// Notes for NailOptimization.log, written by the main thread (_gate held).
		private static readonly List<string> _notes = new List<string>();
		private static volatile int _noteCount;
		// Badge: WARN/ERROR lines since the window was last opened.
		private static int _newWarn;
		private static int _newErr;
		private static readonly char[] _nl = new char[] { '\n' };

		internal static void Start(string dir, string importPath)
		{
			_mainThread = Thread.CurrentThread.ManagedThreadId;
			StartTime = DateTime.Now;
			FilePath = Path.Combine(dir, "NailLog_" + StartTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".log");
			_importPath = importPath;
			Running = true;
			Thread t = new Thread(new ThreadStart(Run));
			t.IsBackground = true;
			t.Name = "AdvLogWriter";
			_thread = t;
			t.Start();
		}

		// Any thread. level: INFO, WARN or ERROR; extra: continuation text (stack), may be null.
		internal static void Add(string level, string src, string msg, string extra)
		{
			if (Failed)
			{
				return;
			}
			if (msg == null)
			{
				msg = "NULL";
			}
			string frame = Thread.CurrentThread.ManagedThreadId == _mainThread ? FrameText() : "?";
			DateTime now = DateTime.Now;
			string key = level + " " + src + " " + msg;
			bool repeated = false;
			bool dropped = false;
			Monitor.Enter(_gate);
			try
			{
				if (_lastKey != null && string.Equals(key, _lastKey, StringComparison.Ordinal) && (now - _lastAt).TotalSeconds < RepeatSeconds)
				{
					if (_repeat == 0)
					{
						_firstAt = now;
					}
					_repeat++;
					_lastAt = now;
					repeated = true;
				}
				else
				{
					FlushRepeatLocked(now);
					_lastKey = key;
					_lastHead = level + " " + src;
					_lastAt = now;
					if (_queue.Count >= MaxQueued)
					{
						_dropped++;
						dropped = true;
					}
					else
					{
						_queue.Add(Format(now, frame, level, src, msg, extra));
						Monitor.Pulse(_gate);
					}
				}
			}
			finally
			{
				Monitor.Exit(_gate);
			}
			// The badge counts entries, not repetitions of one collapsed line.
			if (!dropped && !repeated)
			{
				Count(level);
			}
		}

		private static void FlushRepeatLocked(DateTime now)
		{
			if (_repeat > 0)
			{
				_queue.Add(Format(now, "?", "INFO", "advlog", "previous " + _lastHead + " line repeated " + _repeat.ToString(CultureInfo.InvariantCulture) + " more times", null));
				_repeat = 0;
			}
		}

		private static string FrameText()
		{
			try
			{
				return Time.frameCount.ToString(CultureInfo.InvariantCulture);
			}
			catch (Exception)
			{
				return "?";
			}
		}

		// Same header as NailOptimization.log ("[HH:mm:ss.fff f=N] LEVEL src msg"); continuation lines are indented by two spaces.
		internal static string Format(DateTime at, string frame, string level, string src, string msg, string extra)
		{
			StringBuilder sb = new StringBuilder(64 + msg.Length);
			sb.Append('[').Append(at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" f=").Append(frame).Append("] ");
			sb.Append(level).Append(' ').Append(src).Append(' ');
			AppendBody(sb, msg, true);
			if (!string.IsNullOrEmpty(extra))
			{
				AppendBody(sb, extra, false);
			}
			return sb.ToString();
		}

		private static void AppendBody(StringBuilder sb, string s, bool first)
		{
			string[] parts = s.Split(_nl);
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i];
				if (p.Length > 0 && p[p.Length - 1] == '\r')
				{
					p = p.Substring(0, p.Length - 1);
				}
				if (first && i == 0)
				{
					sb.Append(p);
					continue;
				}
				if (p.Length == 0 && i == parts.Length - 1)
				{
					continue;
				}
				sb.Append("\r\n  ").Append(p);
			}
		}

		private static void Count(string level)
		{
			if (string.Equals(level, "ERROR", StringComparison.Ordinal))
			{
				Interlocked.Increment(ref _newErr);
			}
			else if (string.Equals(level, "WARN", StringComparison.Ordinal))
			{
				Interlocked.Increment(ref _newWarn);
			}
		}

		internal static void ReadBadge(out int warn, out int err)
		{
			warn = Interlocked.CompareExchange(ref _newWarn, 0, 0);
			err = Interlocked.CompareExchange(ref _newErr, 0, 0);
		}

		internal static void ResetBadge()
		{
			Interlocked.Exchange(ref _newWarn, 0);
			Interlocked.Exchange(ref _newErr, 0);
		}

		private static void Note(string s)
		{
			Monitor.Enter(_gate);
			try
			{
				_notes.Add(s);
				_noteCount = _notes.Count;
			}
			finally
			{
				Monitor.Exit(_gate);
			}
		}

		// Main thread, every frame: one int test when there is nothing to report.
		internal static void PumpNotes()
		{
			if (_noteCount == 0)
			{
				return;
			}
			List<string> take = null;
			Monitor.Enter(_gate);
			try
			{
				take = new List<string>(_notes);
				_notes.Clear();
				_noteCount = 0;
			}
			finally
			{
				Monitor.Exit(_gate);
			}
			for (int i = 0; i < take.Count; i++)
			{
				if (take[i].StartsWith("ERROR ", StringComparison.Ordinal))
				{
					N.ULog.Error("logviewer", take[i].Substring(6));
				}
				else
				{
					N.ULog.Info("logviewer", take[i]);
				}
			}
		}

		internal static void Shutdown()
		{
			if (!Running)
			{
				return;
			}
			Monitor.Enter(_gate);
			try
			{
				_stop = true;
				Monitor.PulseAll(_gate);
			}
			finally
			{
				Monitor.Exit(_gate);
			}
			Thread t = _thread;
			if (t != null)
			{
				t.Join(500);
			}
		}

		private static void Run()
		{
			StreamWriter w = null;
			try
			{
				string dir = Path.GetDirectoryName(FilePath);
				Directory.CreateDirectory(dir);
				Rotate(dir);
				w = new StreamWriter(FilePath, false, new UTF8Encoding(false));
				w.NewLine = "\r\n";
				Import(w);
				w.Flush();
				Note("advanced log writing " + FilePath);
				List<string> batch = new List<string>();
				while (true)
				{
					bool stop;
					int dropped;
					Monitor.Enter(_gate);
					try
					{
						if (_queue.Count == 0 && !_stop)
						{
							Monitor.Wait(_gate, 100);
						}
						DateTime now = DateTime.Now;
						if (_repeat > 0 && (_stop || (now - _firstAt).TotalSeconds >= RepeatSeconds))
						{
							FlushRepeatLocked(now);
						}
						List<string> t = _queue;
						_queue = batch;
						batch = t;
						stop = _stop;
						dropped = _dropped;
						_dropped = 0;
					}
					finally
					{
						Monitor.Exit(_gate);
					}
					for (int i = 0; i < batch.Count; i++)
					{
						w.WriteLine(batch[i]);
					}
					if (dropped > 0)
					{
						w.WriteLine(Format(DateTime.Now, "?", "WARN", "advlog", "queue full, dropped " + dropped.ToString(CultureInfo.InvariantCulture) + " lines", null));
					}
					if (batch.Count > 0 || dropped > 0)
					{
						w.Flush();
					}
					batch.Clear();
					if (stop)
					{
						break;
					}
				}
			}
			catch (Exception e)
			{
				Failed = true;
				Note("ERROR advanced log writer failed, BaseMod Log.txt is written again: " + e);
			}
			finally
			{
				if (w != null)
				{
					try
					{
						w.Close();
					}
					catch (Exception)
					{
					}
				}
			}
		}

		// Keeps the newest KeepFiles - 1 old files; the new one makes KeepFiles.
		private static void Rotate(string dir)
		{
			string[] files = Directory.GetFiles(dir, "NailLog_*.log");
			Array.Sort(files, StringComparer.Ordinal);
			int del = files.Length - (KeepFiles - 1);
			int done = 0;
			for (int i = 0; i < del; i++)
			{
				try
				{
					File.Delete(files[i]);
					done++;
				}
				catch (Exception e)
				{
					Note("old log not deleted " + files[i] + ": " + e.GetType().Name);
				}
			}
			if (done > 0)
			{
				Note("old logs deleted " + done.ToString(CultureInfo.InvariantCulture));
			}
		}

		// Copies what BaseMod wrote to Log.txt before our init. One header line per entry (message + its "at" stack lines):
		// ERROR for a Debug_LogError stack, a known loader error prefix or an exception text, else INFO.
		private static void Import(StreamWriter w)
		{
			DateTime now = DateTime.Now;
			string path = _importPath;
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				w.WriteLine(Format(now, "?", "INFO", "import", "BaseMod Log.txt not found: " + path, null));
				return;
			}
			string text;
			using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
				{
					text = r.ReadToEnd();
				}
			}
			string[] lines = text.Replace("\r", string.Empty).Split(_nl);
			w.WriteLine(Format(now, "?", "INFO", "import", "BaseMod Log.txt before init: " + path + ", " + lines.Length.ToString(CultureInfo.InvariantCulture) + " lines", null));
			int entries = 0;
			int errors = 0;
			int i = 0;
			while (i < lines.Length)
			{
				string head = lines[i];
				i++;
				if (head.Length == 0)
				{
					continue;
				}
				if (IsStack(head))
				{
					w.WriteLine("  " + head);
					continue;
				}
				bool error = AdvHooks.IsErrorText(head) || head.IndexOf("Exception", StringComparison.Ordinal) >= 0;
				StringBuilder rest = new StringBuilder();
				int stackLines = 0;
				bool envStack = false;
				while (i < lines.Length && IsStack(lines[i]))
				{
					if (lines[i].IndexOf("Debug_LogError", StringComparison.Ordinal) >= 0)
					{
						error = true;
					}
					if (lines[i].IndexOf("get_StackTrace", StringComparison.Ordinal) >= 0)
					{
						envStack = true;
					}
					stackLines++;
					rest.Append("\r\n  ").Append(lines[i]);
					i++;
				}
				if (stackLines > 0 && !envStack)
				{
					error = true;
				}
				// Written by Add_On.LoadMod before any mod runs; FastFix deals with the clash later and logs what it did.
				if (head.IndexOf("Initerror", StringComparison.Ordinal) >= 0 && head.IndexOf("same key", StringComparison.Ordinal) >= 0 && IsEffectClash(rest.ToString()) && EffectNamesOn())
				{
					error = false;
					head += " (effect name clash, see the fastfix effect names line)";
				}
				w.WriteLine(Format(now, "?", error ? "ERROR" : "INFO", "import", head, null) + rest.ToString());
				entries++;
				if (error)
				{
					errors++;
					Count("ERROR");
				}
			}
			Note("import " + entries.ToString(CultureInfo.InvariantCulture) + " entries errors=" + errors.ToString(CultureInfo.InvariantCulture) + " from " + path);
		}

		private static bool IsEffectClash(string stack)
		{
			return stack.IndexOf("SkeletonDataAsset].Add", StringComparison.Ordinal) >= 0 && stack.IndexOf("Add_On.LoadMod", StringComparison.Ordinal) >= 0;
		}

		// FastFix binds its flags after this import, so the file is read. A file that cannot be read counts as off: the error stays an error.
		private static bool EffectNamesOn()
		{
			try
			{
				string modules = Path.GetDirectoryName(typeof(LogViewerModule).Assembly.Location);
				string cfg = File.ReadAllText(Path.Combine(Path.GetDirectoryName(modules), "NailOptimization.cfg.txt"));
				return cfg.IndexOf("FastFix.Enabled=false", StringComparison.OrdinalIgnoreCase) < 0 && cfg.IndexOf("FastFix.EffectNames=false", StringComparison.OrdinalIgnoreCase) < 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static bool IsStack(string s)
		{
			// No TrimStart(): an empty params call compiles to Array.Empty, which the game's mscorlib lacks.
			int i = 0;
			while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
			{
				i++;
			}
			return i > 0 && string.CompareOrdinal(s, i, "at ", 0, 3) == 0;
		}
	}

	// ---------- advanced log: capture hooks ----------
	// 1.2: known harmless Unity warnings and errors (ROADMAP section 1). Built once before the Unity handler subscribes; read-only after.
	internal static class Benign
	{
		internal static N.Flag Flag;
		private const int MaxUser = 32;
		private static string[] _cond;
		// Substring of the top stack frame (first line of the stack), or null = any stack.
		private static string[] _top;
		private static readonly char[] _sep = new char[] { '|' };
		private static readonly char[] _ws = new char[] { ' ', '\t', '\r', '\n' };

		// Main thread, module init. userList: LogViewer.BenignPatterns, condition substrings separated by '|'.
		internal static void Init(N.Flag flag, string userList)
		{
			List<string> cond = new List<string>();
			List<string> top = new List<string>();
			// Title NRE in the LMM part of NewTitleScript.Start (task title-start-nre); frame Start_Patch1 with AbnormalitySealMod.
			cond.Add("NullReferenceException");
			top.Add("NewTitleScript.Start");
			// Resources.Load false error of this install; loads succeed.
			cond.Add("resources.assets' is corrupted!");
			top.Add(null);
			// Spine AtlasAsset / SkeletonDataAsset from the same Resources.Load calls.
			cond.Add("has a different serialization layout when loading.");
			top.Add(null);
			// Day start.
			cond.Add("DontDestroyOnLoad only work for root GameObjects");
			top.Add(null);
			// Day start: objects of the game's assets with a script missing from the build.
			cond.Add("The referenced script on this Behaviour");
			top.Add(null);
			// BgmManager.PlayBgm: logs this and plays a fallback clip.
			cond.Add("Clip Is Null");
			top.Add(null);
			int builtin = cond.Count;
			int user = 0;
			if (!string.IsNullOrEmpty(userList))
			{
				string[] parts = userList.Split(_sep);
				for (int i = 0; i < parts.Length; i++)
				{
					string p = parts[i].Trim(_ws);
					if (p.Length == 0)
					{
						continue;
					}
					if (user >= MaxUser)
					{
						N.ULog.Warn("logviewer", "benign patterns above " + MaxUser + " ignored");
						break;
					}
					cond.Add(p);
					top.Add(null);
					user++;
				}
			}
			_cond = cond.ToArray();
			_top = top.ToArray();
			Flag = flag;
			N.ULog.Info("logviewer", "benign patterns builtin=" + builtin + " user=" + user + " on=" + flag.On);
		}

		// Any thread.
		internal static bool Match(string condition, string stackTrace)
		{
			string[] cond = _cond;
			string[] top = _top;
			if (cond == null || top == null || condition == null)
			{
				return false;
			}
			for (int i = 0; i < cond.Length; i++)
			{
				if (condition.IndexOf(cond[i], StringComparison.Ordinal) < 0)
				{
					continue;
				}
				string t = top[i];
				if (t == null)
				{
					return true;
				}
				if (stackTrace == null)
				{
					continue;
				}
				int end = stackTrace.IndexOf('\n');
				if (end < 0)
				{
					end = stackTrace.Length;
				}
				if (stackTrace.IndexOf(t, 0, end, StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		internal static string FirstLine(string s)
		{
			int k = s.IndexOf('\n');
			if (k < 0)
			{
				return s;
			}
			if (k > 0 && s[k - 1] == '\r')
			{
				k--;
			}
			return s.Substring(0, k);
		}
	}

	internal static class AdvHooks
	{
		internal const string HarmonyId = "Lobotomy.NailOptimization.LogViewer";
		private static bool installed;
		// GetTextAppendFailEmpty depth: its misses are expected. Reset by the driver every frame (a lost Postfix cannot stick).
		internal static int FailEmptyDepth;
		private static readonly Dictionary<string, bool> _seenText = new Dictionary<string, bool>(StringComparer.Ordinal);
		private static int _textFails;
		private static volatile int _unityFails;
		private static int _logFails;
		private static int _hookFails;
		private static bool _scanned;
		private static string _scanLang;
		private static int _nextLangCheck;
		private static readonly string[] _errorPrefixes = new string[] { "CDLerror", "AddCreature error", "EDL Load Error", "LTDerror", "CM.LODerror" };

		internal static bool IsErrorText(string s)
		{
			for (int i = 0; i < _errorPrefixes.Length; i++)
			{
				if (s.StartsWith(_errorPrefixes[i], StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		// Main thread, module init. False = the feature stays off (BaseMod Log.txt as before).
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static bool Install(string importPath)
		{
			if (installed)
			{
				return false;
			}
			installed = true;
			Type md = typeof(LobotomyBaseMod.ModDebug);
			MethodBase log = N.PatchUtil.Resolve("logviewer", md, "Log", new Type[] { typeof(string) });
			if (object.ReferenceEquals(log, null))
			{
				N.ULog.Warn("logviewer", "advanced log off: ModDebug.Log(string) not found");
				return false;
			}
			string owner = ForeignPrefix(log);
			if (owner != null)
			{
				N.ULog.Warn("logviewer", "advanced log off: ModDebug.Log already has a Prefix by " + owner + " (LogSpamFilter or similar); BaseMod Log.txt stays");
				return false;
			}
			HarmonyInstance h = HarmonyInstance.Create(HarmonyId);
			if (Patch(h, log, "PrefixLog", null) == 0)
			{
				N.ULog.Warn("logviewer", "advanced log off: ModDebug.Log prefix not installed; BaseMod Log.txt stays");
				return false;
			}
			// BaseMod writes only on the main thread, which is here: Log.txt is complete when the worker imports it.
			AdvLog.Start(Path.Combine(N.UiHost.RootDir, "Logs"), importPath);
			AdvLog.Add("INFO", "advlog", "init, first toucher of Add_On (probe P1):", Environment.StackTrace);
			bool unity = false;
			try
			{
				Application.logMessageReceived += new Application.LogCallback(OnUnityLog);
				unity = true;
			}
			catch (Exception e)
			{
				N.ULog.Error("logviewer", "logMessageReceived subscribe failed: " + e);
			}
			int n = 1;
			if (unity)
			{
				n += Patch(h, N.PatchUtil.Resolve("logviewer", md, "Debug_Log", new Type[] { typeof(object) }), "PrefixSkip", null);
				n += Patch(h, N.PatchUtil.Resolve("logviewer", md, "Debug_LogError", new Type[] { typeof(object) }), "PrefixSkip", null);
				n += Patch(h, N.PatchUtil.Resolve("logviewer", md, "Debug_LogException", new Type[] { typeof(Exception) }), "PrefixSkip", null);
			}
			Type tm = typeof(LocalizeTextDataModel);
			n += Patch(h, N.PatchUtil.Resolve("logviewer", tm, "GetText", new Type[] { typeof(string) }), null, "PostfixGetText");
			MethodBase fe = N.PatchUtil.Resolve("logviewer", tm, "GetTextAppendFailEmpty", new Type[] { typeof(string[]) });
			n += Patch(h, fe, "PrefixFailEmpty", "PostfixFailEmpty");
			// No patch on loaders with try/catch (GL-53: the Harmony copy drops exception clauses); the scan starts from the driver.
			N.ULog.Info("logviewer", "advanced hook installed " + n + "/6 handler=" + unity + " file=" + AdvLog.FilePath);
			return true;
		}

		private static int Patch(HarmonyInstance h, MethodBase target, string pre, string post)
		{
			if (object.ReferenceEquals(target, null))
			{
				return 0;
			}
			BindingFlags f = BindingFlags.Public | BindingFlags.Static;
			HarmonyMethod hp = pre != null ? new HarmonyMethod(typeof(AdvHooks).GetMethod(pre, f)) : null;
			HarmonyMethod hq = post != null ? new HarmonyMethod(typeof(AdvHooks).GetMethod(post, f)) : null;
			return N.PatchUtil.Install(h, "logviewer", target, hp, hq) ? 1 : 0;
		}

		// Owner of a Prefix another mod put on the target, or null.
		private static string ForeignPrefix(MethodBase m)
		{
			try
			{
				Patches p = PatchProcessor.IsPatched(m);
				if (p == null || p.Prefixes == null)
				{
					return null;
				}
				for (int i = 0; i < p.Prefixes.Count; i++)
				{
					if (!string.Equals(p.Prefixes[i].owner, HarmonyId, StringComparison.Ordinal))
					{
						return p.Prefixes[i].owner;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn("logviewer", "patch info of ModDebug.Log not read: " + e.GetType().Name + " " + e.Message);
			}
			return null;
		}

		// ModDebug.Log(string msg): full replacement. The Harmony copy of the original lost the clauses of its using block (GL-53),
		// so it never runs: on a writer failure this Prefix writes Log.txt itself like vanilla (fail-open).
		public static bool PrefixLog(string msg)
		{
			if (!AdvLog.Failed)
			{
				try
				{
					string s = msg == null ? "NULL" : msg;
					AdvLog.Add(IsErrorText(s) ? "ERROR" : "INFO", "basemod", s, null);
					return false;
				}
				catch (Exception)
				{
					_logFails++;
				}
			}
			WriteBaseModLog(msg);
			return false;
		}

		// Vanilla ModDebug.Log body: FileInit when Log.txt is missing, then append one line.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void WriteBaseModLog(string msg)
		{
			try
			{
				if (!LobotomyBaseMod.ModDebug.CheckLogFileExist())
				{
					LobotomyBaseMod.ModDebug.FileInit();
				}
				using (StreamWriter w = new StreamWriter(LobotomyBaseMod.ModDebug.LogFilePath, true))
				{
					w.WriteLine(msg);
				}
			}
			catch (Exception)
			{
				_logFails++;
			}
		}

		// Debug_Log / Debug_LogError / Debug_LogException: the Unity handler already has the line (H2).
		public static bool PrefixSkip()
		{
			return AdvLog.Failed || _unityFails >= 5;
		}

		// Never calls Debug.* (it would re-enter this handler).
		private static void OnUnityLog(string condition, string stackTrace, LogType type)
		{
			if (_unityFails >= 5)
			{
				return;
			}
			try
			{
				string level;
				string extra = null;
				if (type == LogType.Log)
				{
					level = "INFO";
				}
				else
				{
					level = type == LogType.Warning ? "WARN" : "ERROR";
					extra = stackTrace;
					N.Flag hide = Benign.Flag;
					if (hide != null && hide.On && Benign.Match(condition, stackTrace))
					{
						level = "INFO";
						extra = null;
						condition = "benign: " + Benign.FirstLine(condition);
					}
					else if (type == LogType.Error && string.IsNullOrEmpty(extra))
					{
						extra = new System.Diagnostics.StackTrace(1, false).ToString();
					}
				}
				AdvLog.Add(level, "unity", condition, extra);
			}
			catch (Exception)
			{
				_unityFails++;
			}
		}

		// Runtime missing text (Q8 b): one line per id. Only the "UNKNOWN" result is examined (one length test per call).
		public static void PostfixGetText(string id, string __result)
		{
			if (FailEmptyDepth > 0 || _textFails >= 3 || __result == null || __result.Length != 7 || !string.Equals(__result, "UNKNOWN", StringComparison.Ordinal))
			{
				return;
			}
			try
			{
				LocalizeTextDataModel tm = LocalizeTextDataModel.instance;
				if (tm == null || !tm.loaded || string.IsNullOrEmpty(id) || _seenText.Count >= 2000 || _seenText.ContainsKey(id))
				{
					return;
				}
				_seenText[id] = true;
				string asm;
				string caller = Caller(out asm);
				// Vanilla and BaseMod probe keys on purpose (SefiraBossManager.SetKeyCounts counts them until a miss): INFO only.
				string level = string.Equals(asm, "Assembly-CSharp", StringComparison.Ordinal) ? "INFO" : "WARN";
				AdvLog.Add(level, "diag", "missing-text id=" + id + " caller=" + caller, null);
			}
			catch (Exception e)
			{
				_textFails++;
				N.ULog.Warn("logviewer", "GetText postfix failed (" + _textFails + "): " + e.GetType().Name + " " + e.Message);
			}
		}

		// First frame outside LocalizeTextDataModel and this class; asm = its assembly name.
		private static string Caller(out string asm)
		{
			asm = string.Empty;
			System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(2, false);
			for (int i = 0; i < st.FrameCount; i++)
			{
				MethodBase mb = st.GetFrame(i).GetMethod();
				if (object.ReferenceEquals(mb, null))
				{
					continue;
				}
				Type t = mb.DeclaringType;
				if (object.ReferenceEquals(t, null) || object.ReferenceEquals(t, typeof(LocalizeTextDataModel)) || object.ReferenceEquals(t, typeof(AdvHooks)))
				{
					continue;
				}
				asm = t.Assembly.GetName().Name;
				return t.FullName + "." + mb.Name + " (" + asm + ")";
			}
			return "?";
		}

		public static void PrefixFailEmpty()
		{
			FailEmptyDepth++;
		}

		public static void PostfixFailEmpty()
		{
			if (FailEmptyDepth > 0)
			{
				FailEmptyDepth--;
			}
		}

		// Driver, every frame while the advanced log runs. LoadStaticData runs inside GlobalGameManager.Awake, so the first frame
		// with an instance has all mod data loaded. Then the language is compared every 120 frames: a change clears the
		// missing-text ids and checks the files of the new language.
		internal static void TickScan()
		{
			if (_hookFails >= 3)
			{
				return;
			}
			int f = Time.frameCount;
			if (_scanned && f < _nextLangCheck)
			{
				return;
			}
			_nextLangCheck = f + 120;
			try
			{
				GlobalGameManager g = GlobalGameManager.instance;
				if (g == null)
				{
					return;
				}
				string lang = g.GetCurrentLanguage();
				if (!_scanned)
				{
					_scanned = true;
					_scanLang = lang;
					RequestScan("first frame");
				}
				else if (!string.Equals(lang, _scanLang, StringComparison.Ordinal))
				{
					_scanLang = lang;
					_seenText.Clear();
					RequestScan("language change");
				}
			}
			catch (Exception e)
			{
				_hookFails++;
				N.ULog.Error("logviewer", "scan tick failed (" + _hookFails + "): " + e);
			}
		}

		private static void RequestScan(string why)
		{
			if (_hookFails >= 3)
			{
				return;
			}
			try
			{
				ScanInput s = Snapshot();
				AdvLog.Add("INFO", "diag", "scan requested by " + why + " lang=" + s.Lang + " mods=" + s.Mods.Count.ToString(CultureInfo.InvariantCulture), null);
				ModScan.Request(s);
			}
			catch (Exception e)
			{
				_hookFails++;
				N.ULog.Error("logviewer", "scan request failed (" + _hookFails + "): " + e);
			}
		}

		// Main thread: strings only, so the worker never touches game objects.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static ScanInput Snapshot()
		{
			ScanInput s = new ScanInput();
			s.Lang = GlobalGameManager.instance.GetCurrentLanguage();
			s.Mods = new List<ScanMod>();
			Add_On a = Add_On.instance;
			if (a != null && a.ModList != null)
			{
				for (int i = 0; i < a.ModList.Count; i++)
				{
					ModInfo mi = a.ModList[i];
					if (mi == null || mi.modpath == null)
					{
						continue;
					}
					ScanMod m = new ScanMod();
					m.Folder = mi.modpath.Name;
					m.Id = mi.modid != null ? mi.modid : string.Empty;
					m.Path = mi.modpath.FullName;
					s.Mods.Add(m);
				}
			}
			s.BaseModsDir = Path.Combine(Application.dataPath, "BaseMods");
			s.ErrorReport = Add_On.Error_Report;
			s.Since = AdvLog.StartTime;
			s.Report = new ScanReport(Report);
			return s;
		}

		private static void Report(string level, string kind, string text)
		{
			AdvLog.Add(level, "diag", kind + " " + text, null);
		}
	}

	// ---------- mod file diagnostics ----------
	// Worker thread, System.IO / System.Xml only (no Unity, no LogViewer types): reusable by the load-guard task.
	// Checks the folders the game's loaders read, in ModList (= DirList) order, and reports through ScanInput.Report.
	internal delegate void ScanReport(string level, string kind, string text);

	internal sealed class ScanMod
	{
		internal string Folder;
		internal string Id;
		internal string Path;
	}

	internal sealed class ScanInput
	{
		internal string Lang;
		internal List<ScanMod> Mods;
		internal string BaseModsDir;
		internal string ErrorReport;
		internal DateTime Since;
		internal ScanReport Report;
	}

	internal static class ModScan
	{
		// InfoGuard renames fatal files to "*~<4 CJK chars>~*" (SkipMark) during the creature load; they are not ours to report.
		private const string SkipMark = "~\u574F\u6863\u8DF3\u8FC7~";
		private const int MaxDupLines = 200;
		// BaseMod error files can be written after the creature load (LoadModIsolate): checked this long after the scan.
		private const int ErrorFilesDelayMs = 15000;
		private static readonly object _gate = new object();
		private static bool _running;
		private static ScanInput _pending;

		private sealed class ListEntry
		{
			internal ScanMod Mod;
			internal string File;
			internal string Src;
			internal string Stat;
		}

		internal static void Request(ScanInput s)
		{
			Monitor.Enter(_gate);
			try
			{
				if (_running)
				{
					_pending = s;
					return;
				}
				_running = true;
			}
			finally
			{
				Monitor.Exit(_gate);
			}
			Thread t = new Thread(new ParameterizedThreadStart(Run));
			t.IsBackground = true;
			t.Name = "ModScan";
			t.Start(s);
		}

		private static void Run(object state)
		{
			ScanInput s = (ScanInput)state;
			while (s != null)
			{
				try
				{
					Scan(s);
					Thread.Sleep(ErrorFilesDelayMs);
					ErrorFiles(s);
				}
				catch (Exception e)
				{
					s.Report("ERROR", "scan-failed", e.ToString());
				}
				Monitor.Enter(_gate);
				try
				{
					s = _pending;
					_pending = null;
					if (s == null)
					{
						_running = false;
					}
				}
				finally
				{
					Monitor.Exit(_gate);
				}
			}
		}

		private sealed class Ctx
		{
			internal ScanInput In;
			internal int Files;
			internal int Problems;
		}

		private static void Problem(Ctx c, string level, string kind, ScanMod m, string file, string detail)
		{
			c.Problems++;
			c.In.Report(level, kind, "mod=" + (m != null ? m.Folder : "-") + " file=" + Rel(c, file) + " " + detail);
		}

		private static string Rel(Ctx c, string path)
		{
			if (path == null)
			{
				return "-";
			}
			string p = path.Replace('\\', '/');
			string root = c.In.BaseModsDir != null ? c.In.BaseModsDir.Replace('\\', '/').TrimEnd('/') : null;
			if (root != null && p.Length > root.Length + 1 && p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
			{
				return p.Substring(root.Length + 1);
			}
			return p;
		}

		// File.ReadAllText + LoadXml like the loaders; null = malformed (reported with line and position).
		private static XmlDocument Load(Ctx c, ScanMod m, string file, string consequence)
		{
			c.Files++;
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(File.ReadAllText(file));
				return doc;
			}
			catch (XmlException e)
			{
				Problem(c, "ERROR", "malformed-xml", m, file, e.Message + " -> " + consequence);
			}
			catch (Exception e)
			{
				Problem(c, "ERROR", "unreadable-file", m, file, e.GetType().Name + ": " + e.Message);
			}
			return null;
		}

		private static string[] FilesOf(string dir, bool xmlOrTxt, bool recursive)
		{
			if (!Directory.Exists(dir))
			{
				return new string[0];
			}
			string[] all = Directory.GetFiles(dir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			List<string> r = new List<string>(all.Length);
			for (int i = 0; i < all.Length; i++)
			{
				string name = Path.GetFileName(all[i]);
				if (name.IndexOf(SkipMark, StringComparison.Ordinal) >= 0)
				{
					continue;
				}
				if (xmlOrTxt && name.IndexOf(".xml", StringComparison.Ordinal) < 0 && name.IndexOf(".txt", StringComparison.Ordinal) < 0)
				{
					continue;
				}
				r.Add(all[i]);
			}
			return r.ToArray();
		}

		private static void AddTo(Dictionary<string, List<string>> map, string key, string folder)
		{
			List<string> l;
			if (!map.TryGetValue(key, out l))
			{
				l = new List<string>();
				map[key] = l;
			}
			if (!l.Contains(folder))
			{
				l.Add(folder);
			}
		}

		private static void Scan(ScanInput s)
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Ctx c = new Ctx();
			c.In = s;
			string lang = s.Lang;
			Dictionary<string, List<string>> textIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> modIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> modIdsWithData = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> statFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> infoFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, string> infoPath = new Dictionary<string, string>(StringComparer.Ordinal);
			Dictionary<string, string> otherLang = new Dictionary<string, string>(StringComparer.Ordinal);
			List<KeyValuePair<ScanMod, string>> byName = new List<KeyValuePair<ScanMod, string>>();
			Dictionary<string, List<string>> creatureIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			Dictionary<string, List<string>> equipIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			List<ListEntry> entries = new List<ListEntry>();
			for (int k = 0; k < s.Mods.Count; k++)
			{
				ScanMod m = s.Mods[k];
				if (m.Id.Length > 0)
				{
					AddTo(modIds, m.Id, m.Folder);
					if (FilesOf(Path.Combine(Path.Combine(m.Path, "Creature"), "CreatureList"), true, false).Length > 0 || FilesOf(Path.Combine(Path.Combine(m.Path, "Equipment"), "txts"), true, false).Length > 0)
					{
						AddTo(modIdsWithData, m.Id, m.Folder);
					}
				}
				// Localize/<lang>/** : every file, one try for all mods (LTDerror.txt).
				string loc = Path.Combine(m.Path, "Localize");
				if (Directory.Exists(loc))
				{
					string ld = Path.Combine(loc, lang);
					if (!Directory.Exists(ld))
					{
						Problem(c, "WARN", "missing-language", m, ld, "no Localize/" + lang + " folder: the mod's texts are missing in this language (no fallback for mods)");
					}
					else
					{
						string[] lf = FilesOf(ld, false, true);
						for (int i = 0; i < lf.Length; i++)
						{
							XmlDocument doc = Load(c, m, lf[i], "loading of all later mod Localize files stops (LTDerror.txt)");
							if (doc == null)
							{
								continue;
							}
							if (doc.SelectSingleNode("localize") == null)
							{
								Problem(c, "ERROR", "wrong-root", m, lf[i], "root is not <localize> -> loading of all later mod Localize files stops");
								continue;
							}
							XmlNodeList texts = doc.SelectNodes("/localize/text");
							for (int t = 0; t < texts.Count; t++)
							{
								XmlAttribute id = texts[t].Attributes != null ? texts[t].Attributes["id"] : null;
								if (id == null)
								{
									Problem(c, "ERROR", "text-without-id", m, lf[i], "<text> number " + (t + 1).ToString(CultureInfo.InvariantCulture) + " has no id -> loading of all later mod Localize files stops");
									break;
								}
								AddTo(textIds, id.Value, m.Folder);
							}
						}
					}
				}
				string eq = Path.Combine(m.Path, "Equipment");
				if (Directory.Exists(eq))
				{
					string[] xf = FilesOf(Path.Combine(Path.Combine(eq, "xmls"), lang), true, false);
					for (int i = 0; i < xf.Length; i++)
					{
						XmlDocument doc = Load(c, m, xf[i], "LoadLocalizeTextData throws: no localization is installed at all");
						if (doc != null && doc.SelectSingleNode("localize") == null)
						{
							Problem(c, "ERROR", "wrong-root", m, xf[i], "root is not <localize> -> LoadLocalizeTextData throws: no localization is installed at all");
						}
					}
					string[] tf = FilesOf(Path.Combine(eq, "txts"), true, false);
					for (int i = 0; i < tf.Length; i++)
					{
						XmlDocument doc = Load(c, m, tf[i], "EDL Load Error: no equipment table at all, vanilla included");
						if (doc == null || m.Id.Length > 0 || doc.DocumentElement == null)
						{
							continue;
						}
						XmlNodeList items = doc.DocumentElement.ChildNodes;
						for (int t = 0; t < items.Count; t++)
						{
							XmlAttribute id = items[t].Attributes != null ? items[t].Attributes["id"] : null;
							if (id != null)
							{
								AddTo(equipIds, id.Value, m.Folder);
							}
						}
					}
				}
				string cr = Path.Combine(m.Path, "Creature");
				if (Directory.Exists(cr))
				{
					string[] gf = FilesOf(Path.Combine(cr, "CreatureGen"), true, false);
					for (int i = 0; i < gf.Length; i++)
					{
						XmlDocument doc = Load(c, m, gf[i], "CDLerror: no creature list is installed");
						if (doc == null)
						{
							continue;
						}
						CheckInts(c, m, gf[i], doc.SelectNodes("/All/add"));
						CheckInts(c, m, gf[i], doc.SelectNodes("/All/remove"));
					}
					string[] lf = FilesOf(Path.Combine(cr, "CreatureList"), true, false);
					for (int i = 0; i < lf.Length; i++)
					{
						XmlDocument doc = Load(c, m, lf[i], "CDLerror: no creature list is installed");
						if (doc == null)
						{
							continue;
						}
						XmlNodeList list = doc.SelectNodes("/creature_list/creature");
						for (int t = 0; t < list.Count; t++)
						{
							XmlAttribute src = list[t].Attributes != null ? list[t].Attributes["src"] : null;
							XmlNode stat = list[t].SelectSingleNode("stat");
							if (src == null || src.Value.Length == 0)
							{
								Problem(c, "ERROR", "creature-without-src", m, lf[i], "<creature> number " + (t + 1).ToString(CultureInfo.InvariantCulture) + " has no src -> CDLerror: no creature list is installed");
								continue;
							}
							if (stat == null || stat.InnerText.Trim().Length == 0)
							{
								Problem(c, "ERROR", "creature-without-stat", m, lf[i], "src=" + src.Value + " has no <stat> -> CDLerror: no creature list is installed");
								continue;
							}
							ListEntry e = new ListEntry();
							e.Mod = m;
							e.File = lf[i];
							e.Src = src.Value;
							e.Stat = stat.InnerText;
							entries.Add(e);
						}
					}
					string[] sf = FilesOf(Path.Combine(cr, "Creatures"), false, false);
					for (int i = 0; i < sf.Length; i++)
					{
						AddTo(statFiles, Path.GetFileName(sf[i]), m.Folder);
						byName.Add(new KeyValuePair<ScanMod, string>(m, sf[i]));
					}
					string[] inf = FilesOf(Path.Combine(Path.Combine(cr, "CreatureInfo"), lang), false, false);
					for (int i = 0; i < inf.Length; i++)
					{
						string name = Path.GetFileName(inf[i]);
						AddTo(infoFiles, name, m.Folder);
						if (!infoPath.ContainsKey(name))
						{
							infoPath[name] = inf[i];
						}
						byName.Add(new KeyValuePair<ScanMod, string>(m, inf[i]));
					}
					string infoRoot = Path.Combine(cr, "CreatureInfo");
					string[] langDirs = Directory.Exists(infoRoot) ? Directory.GetDirectories(infoRoot) : new string[0];
					for (int i = 0; i < langDirs.Length; i++)
					{
						string other = Path.GetFileName(langDirs[i]);
						if (other == lang)
						{
							continue;
						}
						string[] of = FilesOf(langDirs[i], false, false);
						for (int t = 0; t < of.Length; t++)
						{
							string name = Path.GetFileName(of[t]);
							if (!otherLang.ContainsKey(name) || other == "en")
							{
								otherLang[name] = other;
							}
						}
					}
				}
				string si = Path.Combine(m.Path, "SefiraIsolate");
				if (Directory.Exists(si))
				{
					string[] names = new string[] { "add.xml", "add.txt", "replace.xml", "replace.txt" };
					for (int i = 0; i < names.Length; i++)
					{
						string f = Path.Combine(si, names[i]);
						if (File.Exists(f))
						{
							Load(c, m, f, "isolate files of all mods are not loaded (LMIerror.txt / LMIRerror.txt)");
						}
					}
				}
			}
			// Creature list entries: info and stat files are taken from the first mod that has them (any mod, not the declaring one).
			Dictionary<string, string> infoIdCache = new Dictionary<string, string>(StringComparer.Ordinal);
			HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < entries.Count; i++)
			{
				ListEntry e = entries[i];
				usedNames.Add(e.Src + ".xml");
				usedNames.Add(e.Src + ".txt");
				usedNames.Add(e.Stat + ".xml");
				usedNames.Add(e.Stat + ".txt");
				string infoName = infoPath.ContainsKey(e.Src + ".xml") ? e.Src + ".xml" : (infoPath.ContainsKey(e.Src + ".txt") ? e.Src + ".txt" : null);
				if (infoName == null)
				{
					string fallback;
					if (otherLang.TryGetValue(e.Src + ".xml", out fallback) || otherLang.TryGetValue(e.Src + ".txt", out fallback))
					{
						Problem(c, "WARN", "creature-info-other-language", e.Mod, e.File, "src=" + e.Src + ": no " + lang + " file, LoadGuard loads the " + fallback + " one; with LoadGuard off the whole creature load aborts");
					}
					else
					{
						Problem(c, "ERROR", "missing-creature-info", e.Mod, e.File, "src=" + e.Src + ": no Creature/CreatureInfo/*/" + e.Src + ".xml|.txt in any mod -> the whole creature load aborts");
					}
				}
				else
				{
					string cid;
					if (!infoIdCache.TryGetValue(infoName, out cid))
					{
						cid = InfoId(infoPath[infoName]);
						infoIdCache[infoName] = cid;
					}
					if (cid != null)
					{
						AddTo(creatureIds, e.Mod.Id + "|" + cid, e.Mod.Folder + "/" + Path.GetFileName(e.File));
					}
				}
				if (!statFiles.ContainsKey(e.Stat + ".txt") && !statFiles.ContainsKey(e.Stat + ".xml"))
				{
					Problem(c, "ERROR", "missing-creature-stat", e.Mod, e.File, "src=" + e.Src + " stat=" + e.Stat + ": no Creature/Creatures/" + e.Stat + ".txt|.xml in any mod -> the whole creature load aborts");
				}
			}
			// Info and stat files are opened only by these names; other files there (Childname.txt, salary) are never parsed.
			for (int i = 0; i < byName.Count; i++)
			{
				if (usedNames.Contains(Path.GetFileName(byName[i].Value)))
				{
					Load(c, byName[i].Key, byName[i].Value, "CDLerror: no creature list is installed");
				}
				else
				{
					c.Files++;
				}
			}
			// Duplicates (later wins unless stated).
			Dups(c, modIdsWithData, "ERROR", "duplicate-modid", "id {0} in mods with creature or equipment files {1}: the tables of the earlier mods are dropped");
			foreach (KeyValuePair<string, List<string>> kv in modIdsWithData)
			{
				if (kv.Value.Count > 1)
				{
					modIds.Remove(kv.Key);
				}
			}
			Dups(c, modIds, "WARN", "duplicate-modid", "id {0} in mods {1}: shared asset key, no table is dropped");
			Dups(c, textIds, "WARN", "duplicate-text-id", "text id {0} in mods {1}: the last one wins");
			Dups(c, creatureIds, "WARN", "duplicate-creature-id", "modid|id {0} declared in {1}");
			Dups(c, equipIds, "WARN", "duplicate-equipment-id", "id {0} in mods without modid {1}: the last one wins");
			Dups(c, statFiles, "WARN", "shared-stat-file", "Creature/Creatures/{0} in mods {1}: the first one is used for every mod");
			Dups(c, infoFiles, "WARN", "shared-info-file", "Creature/CreatureInfo/" + lang + "/{0} in mods {1}: the first one is used for every mod");
			s.Report("INFO", "scan", "done lang=" + lang + " mods=" + s.Mods.Count.ToString(CultureInfo.InvariantCulture) + " files=" + c.Files.ToString(CultureInfo.InvariantCulture) + " problems=" + c.Problems.ToString(CultureInfo.InvariantCulture) + " ms=" + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
		}

		private static void CheckInts(Ctx c, ScanMod m, string file, XmlNodeList nodes)
		{
			for (int i = 0; i < nodes.Count; i++)
			{
				int v;
				if (!int.TryParse(nodes[i].InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
				{
					Problem(c, "ERROR", "bad-number", m, file, "<" + nodes[i].Name + ">" + nodes[i].InnerText + " is not an integer -> CDLerror: no creature list is installed");
				}
			}
		}

		// /creature/info@id of a CreatureInfo file; null when unreadable (already reported).
		private static string InfoId(string path)
		{
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(File.ReadAllText(path));
				XmlNode info = doc.SelectSingleNode("/creature/info");
				XmlAttribute id = info != null && info.Attributes != null ? info.Attributes["id"] : null;
				return id != null ? id.Value : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void Dups(Ctx c, Dictionary<string, List<string>> map, string level, string kind, string pattern)
		{
			int lines = 0;
			int more = 0;
			foreach (KeyValuePair<string, List<string>> kv in map)
			{
				if (kv.Value.Count < 2)
				{
					continue;
				}
				if (lines >= MaxDupLines)
				{
					more++;
					continue;
				}
				lines++;
				c.Problems++;
				c.In.Report(level, kind, pattern.Replace("{0}", kv.Key).Replace("{1}", string.Join(", ", kv.Value.ToArray())));
			}
			if (more > 0)
			{
				c.In.Report(level, kind, "and " + more.ToString(CultureInfo.InvariantCulture) + " more");
			}
		}

		// D5: BaseMod error files written during this session (they carry no file path; the scan lines above name the file).
		private static void ErrorFiles(ScanInput s)
		{
			string[] paths = new string[]
			{
				(s.ErrorReport != null ? s.ErrorReport : s.BaseModsDir + "/") + "LTDerror.txt",
				Path.Combine(s.BaseModsDir, "LMIerror.txt"),
				Path.Combine(s.BaseModsDir, "LMIRerror.txt")
			};
			for (int i = 0; i < paths.Length; i++)
			{
				string shown = ShownPath(s, paths[i]);
				try
				{
					if (!File.Exists(paths[i]) || File.GetLastWriteTime(paths[i]) < s.Since)
					{
						continue;
					}
					string text = File.ReadAllText(paths[i]);
					if (text.Length > 4000)
					{
						text = text.Substring(0, 4000) + " ...";
					}
					s.Report("ERROR", "loader-error", "file=" + shown + " " + text);
				}
				catch (Exception e)
				{
					s.Report("WARN", "loader-error", "file=" + shown + " not read: " + e.GetType().Name);
				}
			}
		}

		// "BaseMods/<rest>" for a file under the BaseMods folder, else the full path with forward slashes.
		private static string ShownPath(ScanInput s, string path)
		{
			string p = path.Replace('\\', '/');
			string root = s.BaseModsDir != null ? s.BaseModsDir.Replace('\\', '/').TrimEnd('/') : null;
			if (root != null && p.Length > root.Length + 1 && p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
			{
				return "BaseMods/" + p.Substring(root.Length + 1);
			}
			return p;
		}
	}

	// ---------- localization ----------
	internal static class Loc
	{
		private static readonly Dictionary<string, string> _en = new Dictionary<string, string>();
		private static readonly Dictionary<string, string> _lang = new Dictionary<string, string>();
		internal static string Lang = "en";

		internal static void Load()
		{
			Lang = LangCode();
			_en.Clear();
			_lang.Clear();
			string root = Path.Combine(N.UiHost.RootDir, "Localization");
			Read(Path.Combine(Path.Combine(root, "en"), "LogViewer.xml"), _en);
			if (!string.Equals(Lang, "en", StringComparison.Ordinal))
			{
				Read(Path.Combine(Path.Combine(root, Lang), "LogViewer.xml"), _lang);
			}
			N.ULog.Info("logviewer", "loc lang=" + Lang + " en=" + _en.Count + " lang=" + _lang.Count);
		}

		private static void Read(string path, Dictionary<string, string> into)
		{
			try
			{
				if (!File.Exists(path))
				{
					N.ULog.Warn("logviewer", "localization not found: " + path);
					return;
				}
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				XmlNodeList list = doc.GetElementsByTagName("Text");
				for (int i = 0; i < list.Count; i++)
				{
					XmlElement e = list[i] as XmlElement;
					if (e == null)
					{
						continue;
					}
					string id = e.GetAttribute("id");
					if (id.Length > 0)
					{
						into[id] = e.InnerText;
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn("logviewer", "localization read failed " + path + ": " + e.GetType().Name + " " + e.Message);
			}
		}

		internal static string Str(string id, string fallback)
		{
			string s;
			if (_lang.TryGetValue(id, out s) && s.Length > 0)
			{
				return s;
			}
			if (_en.TryGetValue(id, out s) && s.Length > 0)
			{
				return s;
			}
			return fallback;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static string LangCode()
		{
			try
			{
				// The font set's language is "en" for cn, jp, cn_tr, vn and bg (GlobalGameManager.GetLanguageFont): the game's own language comes first.
				GlobalGameManager g = GlobalGameManager.instance;
				string gl = object.ReferenceEquals(g, null) ? null : g.GetCurrentLanguage();
				if (!string.IsNullOrEmpty(gl))
				{
					return gl.Trim().ToLowerInvariant();
				}
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
	}

	// ---------- uGUI helpers (same shapes as NailUI.Mk) ----------
	internal static class Mk
	{
		internal static RectTransform Rt(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		// Top-left anchored rectangle in reference pixels of the panel.
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

		internal static Text Txt(RectTransform rt, string s, Font f, int size, Color c, TextAnchor a)
		{
			Text t = rt.gameObject.AddComponent<Text>();
			t.font = f;
			t.fontSize = size;
			t.color = c;
			t.alignment = a;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Truncate;
			t.supportRichText = false;
			t.raycastTarget = false;
			t.text = s;
			return t;
		}

		internal static RectTransform Box(Transform parent, string name, Color border, Color fill, float thick, bool raycast)
		{
			RectTransform outer = Rt(name, parent);
			Img(outer, border, raycast);
			RectTransform inner = Rt("Fill", outer);
			Stretch(inner, thick, thick, thick, thick);
			Img(inner, fill, false);
			return outer;
		}

		internal static void NoNav(Selectable s)
		{
			Navigation nav = new Navigation();
			nav.mode = Navigation.Mode.None;
			s.navigation = nav;
		}

		// Teal framed text button.
		internal static Button Btn(Transform parent, string name, float x, float y, float w, float h, Font f, string caption, UnityAction click, out Text label)
		{
			RectTransform rt = Box(parent, name, Theme.Teal, Theme.Solid, 2f, true);
			Px(rt, x, y, w, h);
			RectTransform lt = Rt("Label", rt);
			Stretch(lt, 4f, 2f, 4f, 2f);
			label = Txt(lt, caption, f, Theme.ButtonFont, Theme.Yellow, TextAnchor.MiddleCenter);
			Button b = rt.gameObject.AddComponent<Button>();
			Image border = rt.GetComponent<Image>();
			border.color = Color.white;
			b.targetGraphic = border;
			b.transition = Selectable.Transition.ColorTint;
			ColorBlock cb = ColorBlock.defaultColorBlock;
			cb.normalColor = Theme.Teal;
			cb.highlightedColor = Theme.TealHot;
			cb.pressedColor = Theme.Yellow;
			cb.disabledColor = Theme.DimText;
			cb.colorMultiplier = 1f;
			cb.fadeDuration = 0.05f;
			b.colors = cb;
			NoNav(b);
			b.onClick.AddListener(click);
			return b;
		}
	}

	// ---------- the window ----------
	internal static class Win
	{
		private const int DrainPerFrame = 5000;
		// Smallest scrollbar handle, share of the track (long logs).
		private const float MinBarSize = 0.05f;
		private const string ActionId = "logs";
		private const string IconPath = "Images/log_icon.png";

		private static N.Flag _enabled;
		private static N.Flag _advFlag;
		private static N.Flag _hideBenign;
		private static N.IntParam _maxLines;
		private static N.IntParam _pollMs;
		// Advanced log active for this session (decided once in Init).
		private static bool _adv;
		private const float ColW = 240f;
		private const float ColGap = 6f;
		private const float FlashSeconds = 0.8f;
		private static RectTransform _frameRt;
		private static RectTransform _colFrame;
		private static RectTransform _colView;
		private static Text[] _colRows;
		private static int[] _colAbs;
		private static int _colTop;
		private static int _drawnColTop = -1;
		private static int _drawnMarkVer = -1;
		// Wrapped column entries (D-6): hidden measuring text, cached last top row, entry bullet.
		private static Text _colMeasure;
		private static int _colMaxTop;
		private static int _colMaxTopVer = -1;
		private static string _bullet = "- ";
		private static int _flashAbs = -1;
		private static float _flashUntil;
		private static float _bodyX;
		private static float _bodyY;
		private static float _frameW;
		private static float _bodyH;
		private static int _pushedWarn = -1;
		// Wrapped lines (D-2): hidden measuring text and the cached last top line.
		private static Text _measure;
		private static int _maxTop;
		private static int _maxTopTab = -1;
		private static int _maxTopVersion = -1;
		private static int _maxTopCount = -1;
		private static int _pushedErr = -1;

		private static readonly LogTab[] _tabs = new LogTab[3];
		private static int _active;
		private static bool _open;
		private static bool _dead;
		private static int _errors;
		private static int _openFrame;
		private static int _escClosedFrame = -1;
		private static Scene _scene;
		private static int _blockLogs;

		private static GameObject _root;
		private static RectTransform _panelRt;
		private static RectTransform _viewRt;
		private static Scrollbar _bar;
		private static bool _suppressBar;
		private static Text[] _rows;
		private static int _visible;
		private static Text _status;
		private static Text _openLabel;
		private static GameObject _profBtn;
		private static Text _profLabel;
		private static float _profNext;
		private static Image[] _tabFill = new Image[3];
		private static Text[] _tabText = new Text[3];
		private static Font _font;
		private static Font _mono;

		// View state: first visible line, data version and tab last drawn.
		private static int _top;
		private static int _drawnTop = -1;
		private static int _drawnVersion = -1;
		private static int _drawnTab = -1;
		private static int _drawnRemoved;
		private static bool _follow = true;

		internal static void Init()
		{
			_enabled = N.Config.BindFlag("LogViewer.Enabled", true, "Log window (NailOptimization.log and BaseMod Log.txt) with a title button and a day-box slot. Read at game start.");
			_maxLines = N.Config.BindInt("LogViewer.MaxLines", 100000, 10000, 500000, 10000, "Lines kept per tab; the oldest 10 percent are dropped above it.");
			_pollMs = N.Config.BindInt("LogViewer.PollMs", 500, 100, 5000, 100, "Milliseconds between file checks while the window is open (background thread).");
			_advFlag = N.Config.BindFlag("LogViewer.AdvancedLog", false, "Own log in Logs/ next to NailOptimization.dll instead of BaseMod Log.txt (5 files kept), a column of warnings, errors and mod file diagnostics, an alert badge on the Logs buttons. Needs LogViewer.Enabled. Read at game start.");
			_hideBenign = N.Config.BindFlag("LogViewer.HideBenign", true, "Advanced log: known harmless Unity warnings and errors (title NullReferenceException, resources.assets corrupted, serialization layout, DontDestroyOnLoad, missing scripts, Clip Is Null) are written as INFO 'benign:' lines without stack, outside the error column and the badge.");
			string benignUser = N.Config.GetString("LogViewer.BenignPatterns", string.Empty, "Advanced log: extra harmless message substrings for LogViewer.HideBenign, separated by '|'. Read at game start.");
			if (!_enabled.On)
			{
				N.ULog.Info("logviewer", "disabled by LogViewer.Enabled=false, no action registered");
				return;
			}
			string baseModLog = BaseModLogPath();
			if (_advFlag.On)
			{
				try
				{
					Benign.Init(_hideBenign, benignUser);
					_adv = AdvHooks.Install(baseModLog);
				}
				catch (Exception e)
				{
					// A throw after the writer started still leaves the capture running: show its file.
					_adv = AdvLog.Running;
					N.ULog.Error("logviewer", "advanced log install failed: " + e);
				}
			}
			_tabs[0] = new LogTab();
			_tabs[0].Path = Path.Combine(N.UiHost.RootDir, "NailOptimization.log");
			_tabs[1] = new LogTab();
			if (_adv)
			{
				_tabs[1].Adv = true;
				_tabs[1].Path = AdvLog.FilePath;
			}
			else
			{
				_tabs[1].BaseMod = true;
				_tabs[1].Path = baseModLog;
			}
			_tabs[2] = new LogTab();
			_tabs[2].Path = Path.Combine(Path.Combine(N.UiHost.RootDir, "Logs"), "Profiler.log");
			N.ULog.Info("logviewer", "paths own=" + _tabs[0].Path + (_adv ? " advanced=" : " basemod=") + _tabs[1].Path + " profiler=" + _tabs[2].Path);
			if (N.UiActions.Add(ActionId, "Logs", "\u041B\u043E\u0433\u0438", IconPath, new UnityAction(Toggle), new N.EscapeQuery(OwnsEscape)))
			{
				N.ULog.Info("logviewer", "action registered " + ActionId);
				N.UiActions.SetWheel(ActionId, new N.EscapeQuery(OwnsWheel));
			}
			GameObject go = new GameObject("LogViewerDriver");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<LogViewerDriver>();
		}

		// ModDebug.LogFilePath when BaseMod is loaded (observed equal to the composed path), else persistentDataPath/LobotomyBaseMod/Log.txt.
		private static string BaseModLogPath()
		{
			try
			{
				Type t = N.TypeCache.Resolve("LobotomyBaseMod.ModDebug");
				if (!object.ReferenceEquals(t, null))
				{
					PropertyInfo p = t.GetProperty("LogFilePath", BindingFlags.Public | BindingFlags.Static);
					if (!object.ReferenceEquals(p, null))
					{
						string s = p.GetValue(null, null) as string;
						if (!string.IsNullOrEmpty(s))
						{
							return s;
						}
					}
				}
			}
			catch (Exception e)
			{
				N.ULog.Warn("logviewer", "ModDebug.LogFilePath not read: " + e.GetType().Name + " " + e.Message);
			}
			return Path.Combine(Path.Combine(Application.persistentDataPath, "LobotomyBaseMod"), "Log.txt");
		}

		// EscapeQuery for the host's PlaySpeedSettingUI guard: the window is open now (its Tick runs later) or was closed by this press.
		private static bool OwnsEscape()
		{
			if (!Input.GetKeyDown(KeyCode.Escape))
			{
				return false;
			}
			return _open || _escClosedFrame == Time.frameCount;
		}

		// Wheel query for the host's camera guard: the window is open and the cursor is over the panel.
		private static bool OwnsWheel()
		{
			return _open && _panelRt != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, null);
		}

		internal static void Toggle()
		{
			if (_dead)
			{
				return;
			}
			if (_open)
			{
				Close("toggle");
				return;
			}
			string why = BlockedReason(true);
			if (why != null)
			{
				if (_blockLogs < 8)
				{
					_blockLogs++;
					N.ULog.Info("logviewer", "open blocked (" + why + ")");
				}
				return;
			}
			Open();
		}

		private static void Open()
		{
			// The game language changed since the window was built: build it again with the new texts.
			if (_root != null && !string.Equals(Loc.LangCode(), Loc.Lang, StringComparison.Ordinal))
			{
				N.ULog.Info("logviewer", "language changed " + Loc.Lang + " -> " + Loc.LangCode() + ": window rebuilt");
				UnityEngine.Object.Destroy(_root);
				_root = null;
			}
			if (_root == null)
			{
				try
				{
					Build();
				}
				catch (Exception)
				{
					// A half-built window is dropped so the next open builds it again.
					if (_root != null)
					{
						UnityEngine.Object.Destroy(_root);
						_root = null;
					}
					throw;
				}
			}
			_root.SetActive(true);
			_open = true;
			_openFrame = Time.frameCount;
			_scene = SceneManager.GetActiveScene();
			_drawnVersion = -1;
			Reader.PollMs = _pollMs.Value;
			Reader.Start(_tabs);
			if (_adv)
			{
				AdvLog.ResetBadge();
				PushBadge(0, 0);
			}
			N.ULog.Info("logviewer", "opened tab=" + _active);
		}

		private static void Close(string why)
		{
			_open = false;
			Reader.Stop();
			if (_root != null)
			{
				_root.SetActive(false);
			}
			EventSystem es = EventSystem.current;
			if (es != null)
			{
				es.SetSelectedGameObject(null);
			}
			N.ULog.Info("logviewer", "closed (" + why + ")");
		}

		// Called every frame by the driver; closed = one bool test.
		internal static void Tick()
		{
			if (!_open || _dead)
			{
				return;
			}
			try
			{
				if (_root == null)
				{
					_open = false;
					Reader.Stop();
					return;
				}
				if (Input.GetKeyDown(KeyCode.Escape))
				{
					_escClosedFrame = Time.frameCount;
					Close("escape");
					return;
				}
				if (SceneManager.GetActiveScene() != _scene)
				{
					Close("scene change");
					return;
				}
				string why = BlockedReason(false);
				if (why != null)
				{
					Close("blocked: " + why);
					return;
				}
				Vector2 mouse = Input.mousePosition;
				if (Time.frameCount > _openFrame + 1 && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mouse, null) && !N.UiActions.OverArea(mouse))
				{
					Close("outside click");
					return;
				}
				Reader.PollMs = _pollMs.Value;
				for (int i = 0; i < _tabs.Length; i++)
				{
					_tabs[i].Drain(DrainPerFrame, _maxLines.Value);
				}
				TickProf();
				float wheel = Input.mouseScrollDelta.y;
				if (wheel != 0f && _colFrame != null && _colFrame.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_colFrame, mouse, null))
				{
					_colTop -= wheel > 0f ? Theme.WheelRows : -Theme.WheelRows;
				}
				else if (wheel != 0f && RectTransformUtility.RectangleContainsScreenPoint(_viewRt, mouse, null))
				{
					ScrollTo(_top - (wheel > 0f ? Theme.WheelRows : -Theme.WheelRows));
				}
				if (_flashAbs >= 0 && Time.unscaledTime > _flashUntil)
				{
					_flashAbs = -1;
					_drawnTop = -1;
				}
				Redraw();
				RedrawColumn();
			}
			catch (Exception e)
			{
				_errors++;
				N.ULog.Error("logviewer", "Tick threw (" + _errors + "): " + e);
				if (_errors >= 5)
				{
					_dead = true;
					N.ULog.Error("logviewer", "window disabled after repeated errors");
					try
					{
						Close("errors");
					}
					catch (Exception)
					{
					}
				}
			}
		}

		// Lines wrap (D-2): the last top line is found by measuring from the end until the view is full. Cached per data version.
		private static int MaxTop(LogTab t)
		{
			int count = t.Lines.Count;
			if (_maxTopTab == _active && _maxTopVersion == t.Version && _maxTopCount == count)
			{
				return _maxTop;
			}
			_maxTopTab = _active;
			_maxTopVersion = t.Version;
			_maxTopCount = count;
			float viewH = _viewRt.rect.height;
			float h = 0f;
			int top = count;
			int limit = Mathf.Max(0, count - _rows.Length);
			while (top > limit)
			{
				float lh = Measure(t.Lines[top - 1]);
				if (h + lh > viewH && top < count)
				{
					break;
				}
				h += lh;
				top--;
			}
			_maxTop = Mathf.Min(top, Mathf.Max(0, count - 1));
			if (count == 0)
			{
				_maxTop = 0;
			}
			return _maxTop;
		}

		// Height of one wrapped line at the view width (hidden measuring text, same font and size as the rows).
		private static float Measure(string line)
		{
			_measure.text = line;
			return Mathf.Max(Theme.RowH, _measure.preferredHeight);
		}

		private static void ScrollTo(int top)
		{
			LogTab t = _tabs[_active];
			int max = MaxTop(t);
			_top = Mathf.Clamp(top, 0, max);
			_follow = _top >= max;
		}

		// Re-binds the pooled rows only when the first line, the data or the tab changed.
		private static void Redraw()
		{
			LogTab t = _tabs[_active];
			if (_drawnTab != _active)
			{
				_drawnTab = _active;
				_drawnRemoved = t.Removed;
				_follow = true;
				_drawnVersion = -1;
			}
			if (t.Removed != _drawnRemoved)
			{
				_top -= t.Removed - _drawnRemoved;
				_drawnRemoved = t.Removed;
			}
			int max = MaxTop(t);
			if (_follow)
			{
				_top = max;
			}
			_top = Mathf.Clamp(_top, 0, max);
			if (_top == _drawnTop && t.Version == _drawnVersion)
			{
				return;
			}
			_drawnTop = _top;
			_drawnVersion = t.Version;
			int count = t.Lines.Count;
			// Rows are laid out top-down; each takes the wrapped height of its line until the view is full.
			float viewH = _viewRt.rect.height;
			float y = 0f;
			for (int r = 0; r < _rows.Length; r++)
			{
				int i = _top + r;
				Text row = _rows[r];
				if (i < count && y < viewH)
				{
					row.text = t.Lines[i];
					row.color = _flashAbs >= 0 && t.Removed + i == _flashAbs ? Theme.TealHot : Lv.Of(t.Levels[i]);
					float h = Mathf.Max(Theme.RowH, row.preferredHeight);
					RectTransform rr = row.rectTransform;
					rr.anchoredPosition = new Vector2(0f, -y);
					rr.sizeDelta = new Vector2(0f, h);
					y += h;
				}
				else if (row.text.Length > 0)
				{
					row.text = string.Empty;
				}
			}
			_suppressBar = true;
			_bar.size = count > 0 ? Mathf.Clamp((float)_visible / (float)count, MinBarSize, 1f) : 1f;
			_bar.value = max > 0 ? (float)_top / (float)max : 0f;
			_bar.interactable = max > 0;
			_suppressBar = false;
			if (t.Status.Length > 0)
			{
				_status.text = string.Equals(t.Status, "missing", StringComparison.Ordinal) ? Loc.Str("NotFound", "File not found:") + " " + t.Path : t.Status;
			}
			else
			{
				_status.text = count.ToString(CultureInfo.InvariantCulture) + " " + Loc.Str("Lines", "lines") + "   " + t.Path;
			}
		}

		// Rows: "Diagnostics (n)", per kind "  kind (n)" + its entries, then "Errors and warnings (n)" + entries.
		private static int ColumnRows(LogTab t)
		{
			int n = 2 + t.Issues.Count;
			for (int k = 0; k < t.DiagKinds.Count; k++)
			{
				n += 1 + t.DiagByKind[k].Count;
			}
			return n;
		}

		private static string ColumnRow(LogTab t, int idx, out Color color, out int abs)
		{
			abs = -1;
			color = Theme.Yellow;
			if (idx == 0)
			{
				return Loc.Str("ColDiag", "Diagnostics") + " (" + t.DiagCount.ToString(CultureInfo.InvariantCulture) + ")";
			}
			idx--;
			for (int k = 0; k < t.DiagKinds.Count; k++)
			{
				List<Mark> list = t.DiagByKind[k];
				if (idx == 0)
				{
					color = Theme.Dim;
					return "  " + t.DiagKinds[k] + " (" + list.Count.ToString(CultureInfo.InvariantCulture) + ")";
				}
				idx--;
				if (idx < list.Count)
				{
					Mark m = list[idx];
					abs = m.Abs;
					color = Lv.Of(m.Level);
					return "    " + _bullet + m.Text;
				}
				idx -= list.Count;
			}
			if (idx == 0)
			{
				return Loc.Str("ColIssues", "Errors and warnings") + " (" + t.Issues.Count.ToString(CultureInfo.InvariantCulture) + ")";
			}
			idx--;
			if (idx < t.Issues.Count)
			{
				Mark m = t.Issues[idx];
				abs = m.Abs;
				color = Lv.Of(m.Level);
				return "  " + _bullet + m.Text;
			}
			return string.Empty;
		}

		// Entries wrap (D-6): the last top row is found by measuring from the end until the column is full. Cached per MarkVersion.
		private static int ColMaxTop(LogTab t, int total)
		{
			if (_colMaxTopVer == t.MarkVersion)
			{
				return _colMaxTop;
			}
			_colMaxTopVer = t.MarkVersion;
			float viewH = _colView.rect.height;
			float h = 0f;
			int top = total;
			int limit = Mathf.Max(0, total - _colRows.Length);
			while (top > limit)
			{
				Color c;
				int abs;
				_colMeasure.text = ColumnRow(t, top - 1, out c, out abs);
				float lh = Mathf.Max(Theme.RowH, _colMeasure.preferredHeight);
				if (h + lh > viewH && top < total)
				{
					break;
				}
				h += lh;
				top--;
			}
			_colMaxTop = Mathf.Min(top, Mathf.Max(0, total - 1));
			return _colMaxTop;
		}

		// Re-binds the column rows only when the entries or the column scroll changed; rows are laid out top-down by wrapped height.
		private static void RedrawColumn()
		{
			if (!_adv || _active != 1 || _colRows == null)
			{
				return;
			}
			LogTab t = _tabs[1];
			int total = ColumnRows(t);
			_colTop = Mathf.Clamp(_colTop, 0, ColMaxTop(t, total));
			if (t.MarkVersion == _drawnMarkVer && _colTop == _drawnColTop)
			{
				return;
			}
			_drawnMarkVer = t.MarkVersion;
			_drawnColTop = _colTop;
			float viewH = _colView.rect.height;
			float y = 0f;
			for (int r = 0; r < _colRows.Length; r++)
			{
				int i = _colTop + r;
				Text row = _colRows[r];
				RectTransform rr = (RectTransform)row.rectTransform.parent;
				if (i < total && y < viewH)
				{
					Color c;
					int abs;
					row.text = ColumnRow(t, i, out c, out abs);
					row.color = c;
					_colAbs[r] = abs;
					float h = Mathf.Max(Theme.RowH, row.preferredHeight);
					rr.anchoredPosition = new Vector2(0f, -y);
					rr.sizeDelta = new Vector2(0f, h);
					y += h;
				}
				else
				{
					if (row.text.Length > 0)
					{
						row.text = string.Empty;
					}
					// Zero height: an unused row must not cover a visible row's click area.
					rr.sizeDelta = new Vector2(0f, 0f);
					_colAbs[r] = -1;
				}
			}
		}

		private sealed class ColClick
		{
			private readonly int _row;

			internal ColClick(int row)
			{
				_row = row;
			}

			internal void Run()
			{
				OnColumnRow(_row);
			}
		}

		// Scrolls the log so the entry's line is the top row and flashes it once.
		private static void OnColumnRow(int r)
		{
			Deselect();
			int abs = _colAbs[r];
			if (abs < 0)
			{
				return;
			}
			LogTab t = _tabs[1];
			int idx = abs - t.Removed;
			if (idx < 0 || idx >= t.Lines.Count)
			{
				_status.text = Loc.Str("Trimmed", "This line is no longer kept in the window.");
				return;
			}
			ScrollTo(idx);
			_follow = false;
			_flashAbs = abs;
			_flashUntil = Time.unscaledTime + FlashSeconds;
			_drawnTop = -1;
		}

		// Driver, every frame while the advanced log runs: the badge is pushed to Core at most every 10 frames.
		internal static void TickBadge()
		{
			if ((Time.frameCount % 10) != 0)
			{
				return;
			}
			if (_open)
			{
				AdvLog.ResetBadge();
			}
			int warn;
			int err;
			AdvLog.ReadBadge(out warn, out err);
			PushBadge(warn, err);
		}

		private static void PushBadge(int warn, int err)
		{
			if (warn == _pushedWarn && err == _pushedErr)
			{
				return;
			}
			_pushedWarn = warn;
			_pushedErr = err;
			N.UiActions.SetBadges(ActionId, warn, err);
		}

		private static void OnBar(float v)
		{
			if (_suppressBar)
			{
				return;
			}
			ScrollTo(Mathf.RoundToInt(v * MaxTop(_tabs[_active])));
		}

		private static void OnTab0()
		{
			SelectTab(0);
		}

		private static void OnTab1()
		{
			SelectTab(1);
		}

		private static void OnTab2()
		{
			SelectTab(2);
		}

		private static void OnProf()
		{
			Deselect();
			string r = ProfApi.Toggle();
			N.ULog.Info("logviewer", "profiler button: " + r);
			_profNext = 0f;
		}

		// Profiler button label, twice a second while tab 3 is shown.
		private static void TickProf()
		{
			if (_active != 2 || _profLabel == null || Time.unscaledTime < _profNext)
			{
				return;
			}
			_profNext = Time.unscaledTime + 0.5f;
			_profLabel.text = ProfApi.Running() ? Loc.Str("ProfStop", "Stop profiler") : Loc.Str("ProfStart", "Start profiler");
		}

		private static void SelectTab(int i)
		{
			Deselect();
			if (_active == i)
			{
				return;
			}
			_active = i;
			for (int k = 0; k < _tabFill.Length; k++)
			{
				_tabFill[k].color = k == i ? Theme.TealDark : Theme.Solid;
				_tabText[k].color = k == i ? Theme.Yellow : Theme.DimText;
			}
			_openLabel.text = i == 0 ? Loc.Str("OpenFolder", "Open log folder") : Loc.Str("OpenFile", "Open log file");
			if (_profBtn != null)
			{
				_profBtn.SetActive(i == 2 && ProfApi.Present);
				_profNext = 0f;
			}
			_drawnTop = -1;
			if (_colFrame != null)
			{
				bool col = i == 1;
				_colFrame.gameObject.SetActive(col);
				float x = col ? _bodyX + ColW + ColGap : _bodyX;
				Mk.Px(_frameRt, x, _bodyY, _frameW - (x - _bodyX), _bodyH);
				_drawnMarkVer = -1;
			}
		}

		private static void OnClose()
		{
			Close("close button");
		}

		private static void OnClear()
		{
			Deselect();
			_tabs[_active].ClearView();
			_top = 0;
			_follow = true;
			N.ULog.Info("logviewer", "view cleared tab=" + _active + " (file untouched)");
		}

		// Tab 0: the folder of NailOptimization.log; tab 1: Log.txt itself (folder if the call throws).
		private static void OnOpen()
		{
			Deselect();
			LogTab t = _tabs[_active];
			// OpenURL does not report shell failures: a missing file falls back to its folder here.
			string target = _active == 0 || !File.Exists(t.Path) ? Path.GetDirectoryName(t.Path) : t.Path;
			try
			{
				Application.OpenURL("file:///" + target.Replace('\\', '/'));
				N.ULog.Info("logviewer", "open " + target);
			}
			catch (Exception e)
			{
				string dir = Path.GetDirectoryName(t.Path);
				N.ULog.Warn("logviewer", "open failed (" + e.GetType().Name + "), fallback to folder " + dir);
				try
				{
					Application.OpenURL("file:///" + dir.Replace('\\', '/'));
				}
				catch (Exception e2)
				{
					N.ULog.Error("logviewer", "open folder failed: " + e2);
				}
			}
		}

		private static void Deselect()
		{
			EventSystem es = EventSystem.current;
			if (es != null)
			{
				es.SetSelectedGameObject(null);
			}
		}

		// ---------- gates (same checks as NailUI.Gui.BlockedReason) ----------
		private static LoadingScreen _ls;
		private static int _lsNextFind;

		private static string BlockedReason(bool refresh)
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

		// ---------- build ----------
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Font TitleFont()
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
				N.ULog.Warn("logviewer", "TITLE font lookup failed: " + e.GetType().Name);
			}
			return null;
		}

		private static void Build()
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			Loc.Load();
			_mono = Resources.GetBuiltinResource<Font>("Arial.ttf");
			_font = TitleFont();
			if (_font == null)
			{
				_font = _mono;
			}

			_root = new GameObject("LogViewerRoot", typeof(RectTransform));
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
			RectTransform rootRt = (RectTransform)_root.transform;

			// panel: yellow border + black fill (blocks clicks on the interface behind it)
			_panelRt = Mk.Rt("Panel", rootRt);
			_panelRt.anchorMin = new Vector2(0.5f, 0.5f);
			_panelRt.anchorMax = new Vector2(0.5f, 0.5f);
			_panelRt.pivot = new Vector2(0.5f, 0.5f);
			_panelRt.anchoredPosition = Vector2.zero;
			_panelRt.sizeDelta = new Vector2(Theme.PanelW, Theme.PanelH);
			Mk.Img(_panelRt, Theme.Yellow, true);
			RectTransform fill = Mk.Rt("Fill", _panelRt);
			Mk.Stretch(fill, 3f, 3f, 3f, 3f);
			Mk.Img(fill, Theme.Back, false);

			// header: title, tabs, close
			RectTransform tt = Mk.Rt("Title", _panelRt);
			Mk.Px(tt, 22f, 12f, 196f, 44f);
			Mk.Txt(tt, Loc.Str("Title", "Logs"), _font, Theme.TitleFont, Theme.Yellow, TextAnchor.MiddleLeft);
			Text lbl;
			Mk.Btn(_panelRt, "Tab0", 226f, 14f, 250f, 40f, _font, Loc.Str("TabOwn", "NailOptimization.log"), new UnityAction(OnTab0), out lbl);
			_tabText[0] = lbl;
			_tabFill[0] = _panelRt.Find("Tab0/Fill").GetComponent<Image>();
			Mk.Btn(_panelRt, "Tab1", 484f, 14f, 250f, 40f, _font, _adv ? Loc.Str("TabAdvanced", "Advanced log") : Loc.Str("TabBaseMod", "BaseMod Log.txt"), new UnityAction(OnTab1), out lbl);
			_tabText[1] = lbl;
			_tabFill[1] = _panelRt.Find("Tab1/Fill").GetComponent<Image>();
			Mk.Btn(_panelRt, "Tab2", 742f, 14f, 220f, 40f, _font, Loc.Str("TabProfiler", "Profiler"), new UnityAction(OnTab2), out lbl);
			_tabText[2] = lbl;
			_tabFill[2] = _panelRt.Find("Tab2/Fill").GetComponent<Image>();
			Button close = Mk.Btn(_panelRt, "Close", Theme.PanelW - 62f, 14f, 44f, 40f, _font, "X", new UnityAction(OnClose), out lbl);
			lbl.color = Theme.Red;
			ColorBlock ccb = close.colors;
			ccb.normalColor = Theme.Red;
			close.colors = ccb;

			// body: framed view with pooled rows and a scrollbar
			const float bodyX = 18f;
			const float bodyY = 66f;
			const float barW = 16f;
			float bodyW = Theme.PanelW - 2f * bodyX;
			float bodyH = Theme.PanelH - bodyY - 66f;
			RectTransform frame = Mk.Box(_panelRt, "ViewFrame", Theme.Teal, Theme.Solid, 2f, true);
			Mk.Px(frame, bodyX, bodyY, bodyW - barW - 6f, bodyH);
			_frameRt = frame;
			_bodyX = bodyX;
			_bodyY = bodyY;
			_frameW = bodyW - barW - 6f;
			_bodyH = bodyH;
			_viewRt = Mk.Rt("View", frame);
			Mk.Stretch(_viewRt, 8f, 4f, 8f, 4f);
			_viewRt.gameObject.AddComponent<RectMask2D>();
			float viewH = bodyH - 8f;
			// _visible whole rows fit; one more pooled row shows the cut-off line below them.
			_visible = Mathf.Max(1, Mathf.FloorToInt(viewH / Theme.RowH));
			int n = _visible + 1;
			_rows = new Text[n];
			for (int r = 0; r < n; r++)
			{
				RectTransform rr = Mk.Rt("Row" + r, _viewRt);
				rr.anchorMin = new Vector2(0f, 1f);
				rr.anchorMax = new Vector2(1f, 1f);
				rr.pivot = new Vector2(0f, 1f);
				rr.anchoredPosition = new Vector2(0f, -r * Theme.RowH);
				rr.sizeDelta = new Vector2(0f, Theme.RowH);
				_rows[r] = Mk.Txt(rr, string.Empty, _mono, Theme.RowFont, Theme.LineNormal, TextAnchor.MiddleLeft);
				_rows[r].horizontalOverflow = HorizontalWrapMode.Wrap;
				_rows[r].verticalOverflow = VerticalWrapMode.Overflow;
			}
			RectTransform mr = Mk.Rt("Measure", _viewRt);
			mr.anchorMin = new Vector2(0f, 1f);
			mr.anchorMax = new Vector2(1f, 1f);
			mr.pivot = new Vector2(0f, 1f);
			mr.sizeDelta = new Vector2(0f, Theme.RowH);
			_measure = Mk.Txt(mr, string.Empty, _mono, Theme.RowFont, new Color(0f, 0f, 0f, 0f), TextAnchor.UpperLeft);
			_measure.horizontalOverflow = HorizontalWrapMode.Wrap;
			_measure.verticalOverflow = VerticalWrapMode.Overflow;
			if (_adv)
			{
				BuildColumn(n);
			}
			RectTransform sbr = Mk.Box(_panelRt, "Bar", Theme.Dim, Theme.Solid, 2f, true);
			Mk.Px(sbr, bodyX + bodyW - barW, bodyY, barW, bodyH);
			RectTransform area = Mk.Rt("Area", sbr);
			Mk.Stretch(area, 3f, 3f, 3f, 3f);
			RectTransform handle = Mk.Rt("Handle", area);
			handle.sizeDelta = Vector2.zero;
			Image hi = Mk.Img(handle, Theme.Yellow, true);
			_bar = sbr.gameObject.AddComponent<Scrollbar>();
			_bar.handleRect = handle;
			_bar.targetGraphic = hi;
			_bar.direction = Scrollbar.Direction.TopToBottom;
			_bar.transition = Selectable.Transition.None;
			Mk.NoNav(_bar);
			_bar.onValueChanged.AddListener(new UnityAction<float>(OnBar));

			// footer: status, Open, Clear
			RectTransform st = Mk.Rt("Status", _panelRt);
			Mk.Px(st, bodyX + 4f, Theme.PanelH - 54f, Theme.PanelW - 774f, 40f);
			_status = Mk.Txt(st, string.Empty, _mono, Theme.StatusFont, Theme.DimText, TextAnchor.MiddleLeft);
			_status.horizontalOverflow = HorizontalWrapMode.Wrap;
			Mk.Btn(_panelRt, "Open", Theme.PanelW - 488f, Theme.PanelH - 56f, 250f, 42f, _font, Loc.Str("OpenFolder", "Open log folder"), new UnityAction(OnOpen), out _openLabel);
			Mk.Btn(_panelRt, "Clear", Theme.PanelW - 228f, Theme.PanelH - 56f, 210f, 42f, _font, Loc.Str("Clear", "Clear"), new UnityAction(OnClear), out lbl);
			Mk.Btn(_panelRt, "Profiler", Theme.PanelW - 748f, Theme.PanelH - 56f, 250f, 42f, _font, Loc.Str("ProfStart", "Start profiler"), new UnityAction(OnProf), out _profLabel);
			_profBtn = _panelRt.Find("Profiler").gameObject;
			_profBtn.SetActive(false);

			_active = -1;
			SelectTab(0);
			_root.SetActive(false);
			N.ULog.Info("logviewer", "built in " + sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms rows pool=" + n + " font=" + (_font != null ? _font.name : "null") + " column=" + _adv);
		}

		// Left column of the advanced tab: pooled clickable rows, wheel scroll (no bar).
		private static void BuildColumn(int n)
		{
			_colFrame = Mk.Box(_panelRt, "ColumnFrame", Theme.Teal, Theme.Solid, 2f, true);
			Mk.Px(_colFrame, _bodyX, _bodyY, ColW, _bodyH);
			_colView = Mk.Rt("View", _colFrame);
			Mk.Stretch(_colView, 6f, 4f, 6f, 4f);
			_colView.gameObject.AddComponent<RectMask2D>();
			_colRows = new Text[n];
			_colAbs = new int[n];
			for (int r = 0; r < n; r++)
			{
				RectTransform rr = Mk.Rt("Row" + r, _colView);
				rr.anchorMin = new Vector2(0f, 1f);
				rr.anchorMax = new Vector2(1f, 1f);
				rr.pivot = new Vector2(0f, 1f);
				rr.anchoredPosition = new Vector2(0f, -r * Theme.RowH);
				rr.sizeDelta = new Vector2(0f, Theme.RowH);
				Image hit = Mk.Img(rr, new Color(0f, 0f, 0f, 0f), true);
				Button b = rr.gameObject.AddComponent<Button>();
				b.targetGraphic = hit;
				b.transition = Selectable.Transition.None;
				Mk.NoNav(b);
				b.onClick.AddListener(new UnityAction(new ColClick(r).Run));
				RectTransform tr = Mk.Rt("Text", rr);
				Mk.Stretch(tr, 0f, 0f, 0f, 0f);
				_colRows[r] = Mk.Txt(tr, string.Empty, _mono, Theme.StatusFont, Theme.Text, TextAnchor.MiddleLeft);
				_colRows[r].horizontalOverflow = HorizontalWrapMode.Wrap;
				_colRows[r].verticalOverflow = VerticalWrapMode.Overflow;
				_colAbs[r] = -1;
			}
			RectTransform mr = Mk.Rt("Measure", _colView);
			mr.anchorMin = new Vector2(0f, 1f);
			mr.anchorMax = new Vector2(1f, 1f);
			mr.pivot = new Vector2(0f, 1f);
			mr.sizeDelta = new Vector2(0f, Theme.RowH);
			_colMeasure = Mk.Txt(mr, string.Empty, _mono, Theme.StatusFont, new Color(0f, 0f, 0f, 0f), TextAnchor.UpperLeft);
			_colMeasure.horizontalOverflow = HorizontalWrapMode.Wrap;
			_colMeasure.verticalOverflow = VerticalWrapMode.Overflow;
			// U+25C6 bullet when the font has it (char code keeps the source ASCII).
			char bullet = (char)0x25C6;
			_bullet = _mono != null && _mono.HasCharacter(bullet) ? bullet.ToString() + " " : "- ";
			_colFrame.gameObject.SetActive(false);
		}
	}

	// PerfTools.Api of the PerfTools module, resolved once by reflection (no compile-time reference).
	internal static class ProfApi
	{
		private static bool _looked;
		private static Type _api;

		internal static bool Present
		{
			get
			{
				if (!_looked)
				{
					_looked = true;
					_api = N.TypeCache.Resolve("PerfTools.Api");
				}
				return !object.ReferenceEquals(_api, null);
			}
		}

		internal static bool Running()
		{
			if (!Present)
			{
				return false;
			}
			try
			{
				PropertyInfo p = _api.GetProperty("ProfRunning", BindingFlags.Public | BindingFlags.Static);
				return !object.ReferenceEquals(p, null) && (bool)p.GetValue(null, null);
			}
			catch (Exception)
			{
				return false;
			}
		}

		internal static string Toggle()
		{
			if (!Present)
			{
				return "PerfTools not loaded";
			}
			try
			{
				MethodInfo m = _api.GetMethod(Running() ? "ProfStop" : "ProfStart", BindingFlags.Public | BindingFlags.Static);
				return object.ReferenceEquals(m, null) ? "PerfTools.Api method missing" : (string)m.Invoke(null, null);
			}
			catch (Exception e)
			{
				N.ULog.Error("logviewer", "profiler toggle failed: " + e);
				return "error";
			}
		}
	}

	public class LogViewerDriver : MonoBehaviour
	{
		public void Update()
		{
			if (AdvLog.Running)
			{
				AdvHooks.FailEmptyDepth = 0;
				AdvHooks.TickScan();
				AdvLog.PumpNotes();
				Win.TickBadge();
			}
			Win.Tick();
		}

		// Final flush of the advanced log (the worker is a background thread).
		public void OnApplicationQuit()
		{
			AdvLog.Shutdown();
		}
	}
}
