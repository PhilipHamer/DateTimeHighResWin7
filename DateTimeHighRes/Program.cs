using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DateTimeHighRes
{
    class Program
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        static extern uint NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern uint NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint ActualResolution);

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        static extern void GetSystemTimePreciseAsFileTime(out long filetime);

        const long ONE_MILLION = 1000000L;
        const long TEN_MILLION = 10 * ONE_MILLION;
        const long ONE_HUNDRED_MILLION = 10 * TEN_MILLION;
        const long ONE_BILLION = 10 * ONE_HUNDRED_MILLION;

        static void Main(string[] args)
        {
            try
            {
                uint minRes, maxRes, actualRes;
                if (0 == NtQueryTimerResolution(out minRes, out maxRes, out actualRes))
                {
                    Console.WriteLine("NtQueryTimerResolution: minRes={0}, maxRes={1}, actualRes={2}", minRes, maxRes, actualRes);
                }
            }
            catch (EntryPointNotFoundException) { }

            Normal();
            NormalMultiThread(Environment.ProcessorCount * 3);

            HighRes();
            HighResMultiThread(Environment.ProcessorCount * 3);

            PreciseTimeWin8();
            PreciseTimeWin8MultiThread(Environment.ProcessorCount * 3);

            CompareToWin8();
        }

        static bool IsWin8PreciseTimeAvailable()
        {
            try
            {
                long ft;
                GetSystemTimePreciseAsFileTime(out ft);
                return true;
            }
            catch (EntryPointNotFoundException) { }
            return false;
        }

        static DateTime GetWin8PreciseTimeUtcNow()
        {
            try
            {
                long ft;
                GetSystemTimePreciseAsFileTime(out ft);
                return DateTime.FromFileTimeUtc(ft);
            }
            catch (EntryPointNotFoundException) { }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Reports diffs as a map of diff to count.
        /// If summary, then groups diffs before reporting.
        /// </summary>
        static void ReportDiffs(string heading, IDictionary<long, long> diffs, bool summary = false)
        {
            int divider = summary ? (diffs.Count < 50 ? 1 : (diffs.Count < 1000 ? 100 : 1000)) : 1;
            var diffsGrouped = from d in diffs
                               let r = d.Key / divider
                               group d by r into dgrp
                               orderby dgrp.Key
                               select new { Diff = dgrp.Key * divider, Count = dgrp.Sum(d => d.Value) };
            Console.WriteLine(heading);
            foreach (var kvp in diffsGrouped)
            {
                Console.WriteLine("diff = {0}\t({1}ms)\tcount = {2}", kvp.Diff, kvp.Diff / TimeSpan.TicksPerMillisecond, kvp.Count);
            }
        }

        /// <summary>
        /// Report diffs as a sequence.
        /// If summary, then skips over diffs very close together, to get a sense of the sequence/trend.
        /// </summary>
        static void ReportDiffs(string heading, IEnumerable<long> diffs, bool summary = false)
        {
            long lastDiff = Int64.MaxValue;
            bool report = true;
            Console.WriteLine(heading);
            foreach (long diff in diffs)
            {
                if (summary)
                {
                    if (Math.Abs(diff - lastDiff) > Math.Max(20L, (long)(Math.Abs(diff) * 0.05)))
                    {
                        report = true;
                        lastDiff = diff;
                    }
                    else
                    {
                        report = false;
                    }
                }
                if (report)
                {
                    Console.WriteLine("diff = {0}\t({1}ms)", diff, diff / TimeSpan.TicksPerMillisecond);
                }
            }
        }

        /// <summary>
        /// Records diffs between ticks returned from DateTime.UtcNow.
        /// </summary>
        static IDictionary<long, long> DoNormalLoop(long loops)
        {
            Dictionary<long, long> diffs = new Dictionary<long, long>();

            DateTime dt1 = DateTime.UtcNow;
            for (long i = 1; i < loops; i++)
            {
                DateTime dt2 = DateTime.UtcNow;
                long diff = dt2.Ticks - dt1.Ticks;
                long count;
                diffs.TryGetValue(diff, out count);
                diffs[diff] = ++count;
                dt1 = dt2;
            }

            return diffs;
        }

        /// <summary>
        /// Records diffs between ticks returned from DateTimeHighResWin7.UtcNow.
        /// Also checks to ensure that we do not see a ticks value that is less than
        /// the previous value we saw (on this thread).
        /// In addition, we check to see that the ticks value we see is not far
        /// out of line with the actual time. Ideally, the difference should not be
        /// more than the 'minimum system resolution' as returned from
        /// NtQueryTimerResolution, but it can be -- especially in our multi-threaded
        /// stress tests -- if CPU usage spikes and/or we are doing lots of
        /// context switching. We will still assert that the difference shouldn't 
        /// be more than several seconds.
        /// </summary>
        static IDictionary<long, long> DoHighResLoop(long loops)
        {
            Dictionary<long, long> diffs = new Dictionary<long, long>();
            DateTime dt1 = DateTimeHighResWin7.UtcNow;
            for (long i = 1; i < loops; i++)
            {
                DateTime dt2 = DateTimeHighResWin7.UtcNow;
                DateTime now = DateTime.UtcNow;
                if (dt1 > dt2)
                {
                    Console.WriteLine("ERROR! Decreasing time found {0} > {1}", dt1.Ticks, dt2.Ticks);
                    Debugger.Break();
                }
                if (Math.Abs(dt2.Ticks - now.Ticks) > 5 * TimeSpan.TicksPerSecond)
                {
                    Console.WriteLine("WOW! Diff between forged time and real time is more than 5 seconds ({0} {1})", dt2.Ticks, now.Ticks);
                    Debugger.Break();
                }
                long diff = dt2.Ticks - dt1.Ticks;
                long count;
                diffs.TryGetValue(diff, out count);
                diffs[diff] = ++count;
                dt1 = dt2;
            }
            return diffs;
        }

        /// <summary>
        /// Records diffs between ticks returned from GetSystemTimePreciseAsFileTime.
        /// </summary>
        static IDictionary<long, long> DoWin8PreciseTimeLoop(long loops)
        {
            Dictionary<long, long> diffs = new Dictionary<long, long>();

            DateTime dt1 = GetWin8PreciseTimeUtcNow();
            for (long i = 1; i < loops; i++)
            {
                DateTime dt2 = GetWin8PreciseTimeUtcNow();
                long diff = dt2.Ticks - dt1.Ticks;
                long count;
                diffs.TryGetValue(diff, out count);
                diffs[diff] = ++count;
                dt1 = dt2;
            }

            return diffs;
        }

        /// <summary>
        /// Compares our DateTimeHighResWin7 to Win8's GetSystemTimePreciseAsFileTime.
        /// </summary>
        static IDictionary<long, long> CompareToWin8PreciseTimeLoop(long loops)
        {
            Dictionary<long, long> diffs = new Dictionary<long, long>();

            long ticks = DateTime.UtcNow.Ticks;
            bool doDiffs = false;
            for (long i = 0; i < loops; )
            {
                DateTime dtWin8 = GetWin8PreciseTimeUtcNow();
                DateTime dtWin7 = DateTimeHighResWin7.UtcNow;
                if (doDiffs)
                {
                    long diff = dtWin7.Ticks - dtWin8.Ticks;
                    diffs[i] = diff;
                    i++;
                }
                if (DateTime.UtcNow.Ticks != ticks)
                {
                    // wait till ticks changeover to start diffing so that comparisons will be truer
                    doDiffs = true;
                }
            }

            return diffs;
        }

        /// <summary>
        /// Run lots of loops to test DateTime.UtcNow ticks frequency.
        /// </summary>
        static void Normal()
        {
            var diffs = DoNormalLoop(ONE_HUNDRED_MILLION);
            ReportDiffs("Normal:", diffs);
        }

        /// <summary>
        /// Run lots of loops over multiple threads to test DateTime.UtcNow ticks frequency.
        /// </summary>
        static void NormalMultiThread(int numThreads)
        {
            var diffs = DoMultiThread(numThreads, TEN_MILLION, DoNormalLoop);
            ReportDiffs("NormalMultiThread:", diffs);
        }

        /// <summary>
        /// Run lots of loops to test DateTimeHighResWin7.UtcNow ticks frequency.
        /// </summary>
        static void HighRes()
        {
            var diffs = DoHighResLoop(ONE_HUNDRED_MILLION);
            ReportDiffs("HighRes:", diffs);
        }

        /// <summary>
        /// Run lots of loops over multiple threads to test DateTimeHighResWin7.UtcNow ticks frequency.
        /// </summary>
        static void HighResMultiThread(int numThreads)
        {
            var diffs = DoMultiThread(numThreads, TEN_MILLION, DoHighResLoop);
            ReportDiffs("HighResMultiThread:", diffs);
        }

        /// <summary>
        /// Run lots of loops to test GetSystemTimePreciseAsFileTime ticks frequency.
        /// </summary>
        static void PreciseTimeWin8()
        {
            if (IsWin8PreciseTimeAvailable())
            {
                var diffs = DoWin8PreciseTimeLoop(ONE_HUNDRED_MILLION);
                ReportDiffs("PreciseTimeWin8:", diffs);
            }
            else
            {
                Console.WriteLine("PreciseTimeWin8: GetSystemTimePreciseAsFileTime not available");
            }
        }

        /// <summary>
        /// Run lots of loops over multiple threads to test GetSystemTimePreciseAsFileTime ticks frequency.
        /// </summary>
        static void PreciseTimeWin8MultiThread(int numThreads)
        {
            if (IsWin8PreciseTimeAvailable())
            {
                var diffs = DoMultiThread(numThreads, TEN_MILLION, DoWin8PreciseTimeLoop);
                ReportDiffs("PreciseTimeWin8MultiThread:", diffs);
            }
            else
            {
                Console.WriteLine("PreciseTimeWin8MultiThread: GetSystemTimePreciseAsFileTime not available");
            }
        }

        /// <summary>
        /// Run lots of loops to compare our DateTimeHighResWin7 to GetSystemTimePreciseAsFileTime.
        /// </summary>
        static void CompareToWin8()
        {
            if (IsWin8PreciseTimeAvailable())
            {
                var diffs = CompareToWin8PreciseTimeLoop(ONE_MILLION);
                ReportDiffs("CompareToWin8:", diffs.Values, true);
            }
            else
            {
                Console.WriteLine("CompareToWin8: GetSystemTimePreciseAsFileTime not available");
            }
        }

        /// <summary>
        /// Starts multiple threads, waits for them to be ready, then begins executing 'func'.
        /// Returns diffs from all threads combined.
        /// </summary>
        static IDictionary<long, long> DoMultiThread(int numThreads, long loops, Func<long, IDictionary<long, long>> func)
        {
            IDictionary<long, long>[] diffs = new Dictionary<long, long>[numThreads];
            using (CountdownEvent countdownFinish = new CountdownEvent(numThreads))
            using (ManualResetEvent goEvent = new ManualResetEvent(false))
            using (CountdownEvent countdownStart = new CountdownEvent(numThreads))
            {
                for (int i = 0; i < numThreads; i++)
                {
                    int i_ = i;
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        countdownStart.Signal();
                        goEvent.WaitOne();
                        diffs[i_] = func(loops);
                        countdownFinish.Signal();
                    });
                }
                countdownStart.Wait();
                goEvent.Set();
                countdownFinish.Wait();
            }
            Dictionary<long, long> diffsCombined = new Dictionary<long, long>();
            foreach (var dict in diffs)
            {
                foreach (var kvp in dict)
                {
                    long count;
                    diffsCombined.TryGetValue(kvp.Key, out count);
                    diffsCombined[kvp.Key] = kvp.Value + count;
                }
            }
            return diffsCombined;
        }
    }
}
