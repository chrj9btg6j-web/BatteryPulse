using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BatteryPulse
{
    /// <summary>A short-lived view of the two Codex quota windows.</summary>
    public sealed class CodexUsageSnapshot
    {
        public double? FiveHourRemainingPercent { get; private set; }
        public double? WeeklyRemainingPercent { get; private set; }
        public DateTime RetrievedAtUtc { get; private set; }
        public DateTime? FiveHourResetsAtUtc { get; private set; }
        public DateTime? WeeklyResetsAtUtc { get; private set; }
        public string Status { get; private set; }

        public static readonly CodexUsageSnapshot Unavailable = new CodexUsageSnapshot(
            null, null, DateTime.MinValue, null, null, "unavailable");

        internal CodexUsageSnapshot(
            double? fiveHourRemainingPercent,
            double? weeklyRemainingPercent,
            DateTime retrievedAtUtc,
            DateTime? fiveHourResetsAtUtc,
            DateTime? weeklyResetsAtUtc,
            string status)
        {
            FiveHourRemainingPercent = fiveHourRemainingPercent;
            WeeklyRemainingPercent = weeklyRemainingPercent;
            RetrievedAtUtc = retrievedAtUtc.Kind == DateTimeKind.Utc
                ? retrievedAtUtc
                : retrievedAtUtc.ToUniversalTime();
            FiveHourResetsAtUtc = fiveHourResetsAtUtc;
            WeeklyResetsAtUtc = weeklyResetsAtUtc;
            Status = status ?? "unavailable";
        }

        public string FormatFiveHour(DateTime nowUtc)
        {
            return Format(FiveHourRemainingPercent, FiveHourResetsAtUtc, nowUtc);
        }

        public string FormatWeekly(DateTime nowUtc)
        {
            return Format(WeeklyRemainingPercent, WeeklyResetsAtUtc, nowUtc);
        }

        private string Format(double? percent, DateTime? resetAtUtc, DateTime nowUtc)
        {
            if (!percent.HasValue || !resetAtUtc.HasValue || RetrievedAtUtc == DateTime.MinValue)
                return "--";

            if (nowUtc.Kind != DateTimeKind.Utc) nowUtc = nowUtc.ToUniversalTime();
            double age = (nowUtc - RetrievedAtUtc).TotalSeconds;
            if (age < -30 || age > 180 || resetAtUtc.Value <= nowUtc) return "--";
            if (double.IsNaN(percent.Value) || double.IsInfinity(percent.Value)) return "--";

            int wholePercent = (int)Math.Floor(Math.Max(0, Math.Min(100, percent.Value)));
            return wholePercent.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Reads the signed-in Codex account through the local Codex app-server.
    /// Polls only while enabled, using one short-lived process per refresh.
    /// </summary>
    public sealed class CodexUsageReader : IDisposable
    {
        private const int RequestTimeoutSeconds = 20;
        private const int MaxOutputCharacters = 2 * 1024 * 1024;
        private const int MaxLineCharacters = 256 * 1024;
        private readonly object gate = new object();
        private readonly Action<CodexUsageSnapshot> onUpdated;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private bool started;
        private bool enabled;
        private bool disposed;
        private Process activeProcess;
        private CodexUsageSnapshot snapshot = CodexUsageSnapshot.Unavailable;

        public CodexUsageSnapshot Snapshot
        {
            get { lock (gate) return snapshot; }
        }

        public CodexUsageReader(Action<CodexUsageSnapshot> onUpdated)
        {
            this.onUpdated = onUpdated;
        }

        public void Start()
        {
            lock (gate)
            {
                if (disposed || started) return;
                started = true;
                ThreadPool.QueueUserWorkItem(WorkerLoop);
            }
            wake.Set();
        }

        public void SetEnabled(bool value)
        {
            Process cancel = null;
            lock (gate)
            {
                if (disposed) return;
                enabled = value;
                if (!enabled) cancel = activeProcess;
            }

            if (cancel != null) TryKill(cancel);
            wake.Set();
        }

        public void Dispose()
        {
            Process cancel;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                enabled = false;
                cancel = activeProcess;
            }
            if (cancel != null) TryKill(cancel);
            wake.Set();
        }

        private void WorkerLoop(object ignored)
        {
            int failures = 0;
            while (true)
            {
                if (IsDisabledOrDisposed())
                {
                    lock (gate) if (disposed) return;
                    wake.WaitOne();
                    continue;
                }

                CodexUsageSnapshot result = ReadOnce(FindCodexExecutable(), this, null);
                if (IsDisabledOrDisposed()) continue;

                if (result != null)
                {
                    failures = 0;
                    Publish(result);
                    if (WaitForNextRefresh(60)) continue;
                }
                else
                {
                    failures = Math.Min(failures + 1, 4);
                    Publish(CodexUsageSnapshot.Unavailable);
                    if (WaitForNextRefresh(GetRetryDelaySeconds(failures))) continue;
                }
                return;
            }
        }

        private bool IsDisabledOrDisposed()
        {
            lock (gate) return disposed || !enabled;
        }

        private bool WaitForNextRefresh(int seconds)
        {
            wake.WaitOne(TimeSpan.FromSeconds(seconds));
            lock (gate) return !disposed;
        }

        private void Publish(CodexUsageSnapshot value)
        {
            Action<CodexUsageSnapshot> callback;
            lock (gate)
            {
                if (disposed || !enabled) return;
                snapshot = value;
                callback = onUpdated;
            }
            if (callback != null)
            {
                try { callback(value); }
                catch { }
            }
        }

        internal static int GetRetryDelaySeconds(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0) return 60;
            if (consecutiveFailures == 1) return 120;
            if (consecutiveFailures == 2) return 240;
            if (consecutiveFailures == 3) return 480;
            return 900;
        }

        internal static string FindCodexExecutable()
        {
            string explicitPath = Environment.GetEnvironmentVariable("BATTERYPULSE_CODEX_PATH");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return File.Exists(explicitPath) && string.Equals(Path.GetFileName(explicitPath), "codex.exe", StringComparison.OrdinalIgnoreCase)
                    ? explicitPath
                    : null;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string binDirectory = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            var candidates = new List<FileInfo>();
            try
            {
                if (Directory.Exists(binDirectory))
                {
                    foreach (string versionDirectory in Directory.GetDirectories(binDirectory))
                    {
                        try
                        {
                            string candidate = Path.Combine(versionDirectory, "codex.exe");
                            if (File.Exists(candidate)) candidates.Add(new FileInfo(candidate));
                        }
                        catch { }
                    }
                }
            }
            catch { }

            candidates.Sort(delegate(FileInfo left, FileInfo right)
            {
                return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            });
            if (candidates.Count > 0) return candidates[0].FullName;

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                try
                {
                    string candidate = Path.Combine(directory.Trim().Trim('"'), "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        internal static CodexUsageSnapshot ReadOnce(string executablePath)
        {
            return ReadOnce(executablePath, null, null);
        }

        internal static CodexUsageSnapshot ReadOnce(string executablePath, Action<string> diagnostic)
        {
            return ReadOnce(executablePath, null, diagnostic);
        }

        private static CodexUsageSnapshot ReadOnce(
            string executablePath,
            CodexUsageReader owner,
            Action<string> diagnostic)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                Report(diagnostic, "executable-unavailable");
                return null;
            }

            Process process = null;
            var output = new BoundedJsonLines(MaxOutputCharacters, MaxLineCharacters, diagnostic);
            Thread stdoutThread = null;
            Thread stderrThread = null;
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "app-server --listen stdio://",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process = new Process { StartInfo = start };
                if (!process.Start())
                {
                    Report(diagnostic, "process-start-failed");
                    return null;
                }
                Report(diagnostic, "process-started");

                if (owner != null && !owner.RegisterProcess(process))
                {
                    TryKill(process);
                    return null;
                }

                stdoutThread = new Thread(new ThreadStart(delegate { output.ReadLines(process.StandardOutput); }))
                {
                    IsBackground = true,
                    Name = "BatteryPulse Codex output reader"
                };
                // Drain stderr without retaining it: server diagnostics can include
                // local paths and must never enter the status bar or a log file.
                stderrThread = new Thread(new ThreadStart(delegate { Drain(process.StandardError, diagnostic); }))
                {
                    IsBackground = true,
                    Name = "BatteryPulse Codex error drain"
                };
                stdoutThread.Start();
                stderrThread.Start();
                Report(diagnostic, "output-readers-started");

                DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(RequestTimeoutSeconds);
                process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"batterypulse_usage\",\"version\":\"2.2.2.6\"}}}");
                process.StandardInput.Flush();
                Report(diagnostic, "initialize-sent");
                string line;
                IDictionary<string, object> response;
                if (!ReadResponse(output, 1, deadlineUtc, owner, out line, out response))
                {
                    Report(diagnostic, output.Overflowed ? "initialize-output-limit" : "initialize-no-response");
                    ReportProcessState(process, diagnostic);
                    return null;
                }
                if (response.ContainsKey("error"))
                {
                    Report(diagnostic, "initialize-rejected");
                    return null;
                }
                Report(diagnostic, "initialize-received");

                process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                process.StandardInput.Flush();
                Report(diagnostic, "rate-limits-sent");
                if (!ReadResponse(output, 2, deadlineUtc, owner, out line, out response))
                {
                    Report(diagnostic, output.Overflowed ? "rate-limits-output-limit" : "rate-limits-no-response");
                    ReportProcessState(process, diagnostic);
                    return null;
                }
                if (response.ContainsKey("error"))
                {
                    Report(diagnostic, "rate-limits-rejected");
                    return null;
                }
                Report(diagnostic, "rate-limits-received");

                CodexUsageSnapshot result;
                if (!TryParseResponse(line, DateTime.UtcNow, out result))
                {
                    Report(diagnostic, "rate-limits-unrecognized");
                    return null;
                }
                Report(diagnostic, "quota-parsed");
                return result;
            }
            catch
            {
                Report(diagnostic, "request-failed");
                return null;
            }
            finally
            {
                if (process != null)
                {
                    try { process.StandardInput.Close(); } catch { }
                    try { if (!process.WaitForExit(500)) TryKill(process); } catch { TryKill(process); }
                    try { process.WaitForExit(500); } catch { }
                    if (stdoutThread != null) try { stdoutThread.Join(500); } catch { }
                    if (stderrThread != null) try { stderrThread.Join(500); } catch { }
                    output.Dispose();
                    ReportProcessState(process, diagnostic);
                    if (owner != null) owner.UnregisterProcess(process);
                    try { process.Dispose(); } catch { }
                }
            }
        }

        private static void Report(Action<string> diagnostic, string stage)
        {
            if (diagnostic == null) return;
            try { diagnostic(stage); }
            catch { }
        }

        private static void ReportProcessState(Process process, Action<string> diagnostic)
        {
            try
            {
                if (!process.HasExited)
                {
                    Report(diagnostic, "process-still-running");
                    return;
                }
                Report(diagnostic, "process-exited");
                Report(diagnostic, "process-exit-code-" + process.ExitCode.ToString(CultureInfo.InvariantCulture));
            }
            catch { Report(diagnostic, "process-state-unavailable"); }
        }

        private bool RegisterProcess(Process process)
        {
            lock (gate)
            {
                if (disposed || !enabled) return false;
                activeProcess = process;
                return true;
            }
        }

        private void UnregisterProcess(Process process)
        {
            lock (gate)
            {
                if (ReferenceEquals(activeProcess, process)) activeProcess = null;
            }
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { }
        }

        private static void Drain(StreamReader reader, Action<string> diagnostic)
        {
            try
            {
                char[] buffer = new char[2048];
                int count;
                long characters = 0;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    characters += count;
                Report(diagnostic, "stderr-characters-" + Math.Min(characters, 999999).ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }

        private static bool ReadResponse(
            BoundedJsonLines output,
            int expectedId,
            DateTime deadlineUtc,
            CodexUsageReader owner,
            out string responseLine,
            out IDictionary<string, object> response)
        {
            responseLine = null;
            response = null;
            while (DateTime.UtcNow < deadlineUtc)
            {
                if (owner != null && owner.IsDisabledOrDisposed()) return false;
                string next = output.TakeLine(100);
                if (output.Overflowed) return false;
                if (next == null)
                {
                    if (output.Completed) return false;
                    continue;
                }

                IDictionary<string, object> message;
                try { message = new JavaScriptSerializer().DeserializeObject(next) as IDictionary<string, object>; }
                catch { continue; }
                if (message == null) continue;

                object idValue;
                if (!message.TryGetValue("id", out idValue)) continue;
                double id;
                if (!TryNumber(idValue, out id) || id != expectedId) continue;
                responseLine = next;
                response = message;
                return true;
            }
            return false;
        }

        internal static bool TryParseResponse(string json, DateTime retrievedAtUtc, out CodexUsageSnapshot snapshot)
        {
            snapshot = CodexUsageSnapshot.Unavailable;
            try
            {
                IDictionary<string, object> response = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
                if (response == null) return false;
                object resultValue;
                if (!response.TryGetValue("result", out resultValue)) return false;
                IDictionary<string, object> result = resultValue as IDictionary<string, object>;
                if (result == null) return false;

                object bucketsValue;
                IDictionary<string, object> buckets = null;
                if (result.TryGetValue("rateLimitsByLimitId", out bucketsValue))
                {
                    IDictionary<string, object> byId = bucketsValue as IDictionary<string, object>;
                    if (byId == null || !byId.TryGetValue("codex", out bucketsValue)) return false;
                    buckets = bucketsValue as IDictionary<string, object>;
                }
                else
                {
                    if (!result.TryGetValue("rateLimits", out bucketsValue)) return false;
                    buckets = bucketsValue as IDictionary<string, object>;
                }
                if (buckets == null) return false;

                double? fiveHour = null;
                double? weekly = null;
                DateTime? fiveReset = null;
                DateTime? weekReset = null;
                ReadWindow(buckets, "primary", ref fiveHour, ref weekly, ref fiveReset, ref weekReset);
                ReadWindow(buckets, "secondary", ref fiveHour, ref weekly, ref fiveReset, ref weekReset);
                if (!fiveHour.HasValue && !weekly.HasValue) return false;

                snapshot = new CodexUsageSnapshot(fiveHour, weekly, retrievedAtUtc, fiveReset, weekReset, "available");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReadWindow(
            IDictionary<string, object> bucket,
            string key,
            ref double? fiveHour,
            ref double? weekly,
            ref DateTime? fiveReset,
            ref DateTime? weekReset)
        {
            object value;
            if (!bucket.TryGetValue(key, out value)) return;
            IDictionary<string, object> window = value as IDictionary<string, object>;
            if (window == null) return;

            object durationValue;
            object usedValue;
            if (!window.TryGetValue("windowDurationMins", out durationValue) ||
                !window.TryGetValue("usedPercent", out usedValue)) return;

            double duration;
            double used;
            if (!TryNumber(durationValue, out duration) || !TryNumber(usedValue, out used)) return;

            object resetValue;
            DateTime? reset = null;
            double resetSeconds;
            if (window.TryGetValue("resetsAt", out resetValue) && TryNumber(resetValue, out resetSeconds))
            {
                try
                {
                    DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    reset = epoch.AddSeconds(resetSeconds);
                }
                catch { }
            }

            double remaining = Math.Max(0, Math.Min(100, 100 - used));
            if (Math.Abs(duration - 300) < 0.01 && !fiveHour.HasValue)
            {
                fiveHour = remaining;
                fiveReset = reset;
            }
            else if (Math.Abs(duration - 10080) < 0.01 && !weekly.HasValue)
            {
                weekly = remaining;
                weekReset = reset;
            }
        }

        private static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (value == null || value is bool || value is string) return false;
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }
            catch { return false; }
        }

        private sealed class BoundedJsonLines
        {
            private readonly int maxTotal;
            private readonly int maxLine;
            private readonly Action<string> diagnostic;
            private readonly Queue<string> lines = new Queue<string>();
            private readonly AutoResetEvent ready = new AutoResetEvent(false);
            private int totalCharacters;
            private bool overflowed;
            private bool completed;

            internal bool Overflowed { get { lock (lines) return overflowed; } }
            internal bool Completed { get { lock (lines) return completed; } }

            internal BoundedJsonLines(int maxTotal, int maxLine, Action<string> diagnostic)
            {
                this.maxTotal = maxTotal;
                this.maxLine = maxLine;
                this.diagnostic = diagnostic;
            }

            internal void Dispose()
            {
                try { ready.Dispose(); } catch { }
            }

            private void SignalReady()
            {
                try { ready.Set(); } catch { }
            }

            internal void Accept(string line)
            {
                lock (lines)
                {
                    if (line == null)
                    {
                        completed = true;
                        SignalReady();
                        return;
                    }

                    Report(diagnostic, "stdout-line-received");

                    int lineLength = line.Length + 1;
                    if (line.Length > maxLine || Interlocked.Add(ref totalCharacters, lineLength) > maxTotal)
                        overflowed = true;
                    else
                        lines.Enqueue(line);
                }
                SignalReady();
            }

            internal void ReadLines(StreamReader reader)
            {
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) Accept(line);
                }
                catch { }
                finally { Accept(null); }
            }

            internal string TakeLine(int waitMilliseconds)
            {
                lock (lines)
                {
                    if (lines.Count > 0) return lines.Dequeue();
                    if (overflowed) return null;
                }
                ready.WaitOne(waitMilliseconds);
                lock (lines) return lines.Count > 0 ? lines.Dequeue() : null;
            }
        }
    }
}
