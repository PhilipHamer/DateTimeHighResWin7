using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DateTimeHighRes
{
    /// <summary>
    /// Provides high-resolution estimate of time for Windows 7 and other OS versions that
    /// don't support GetSystemTimePreciseAsFileTime.
    /// </summary>
    public static class DateTimeHighResWin7
    {
        public static DateTime UtcNow
        {
            get
            {
                return MakeHighResolution(DateTime.UtcNow);
            }
        }

        public static DateTime Now
        {
            get
            {
                return MakeHighResolution(DateTime.Now);
            }
        }

        static DateTimeHighResWin7()
        {
            // This is the value to multiply by Stopwatch timestamp values to get DateTime ticks.
            TicksAdjustNormal = TimeSpan.TicksPerSecond * 1.0 / Stopwatch.Frequency;

            // This is a value less than TicksAdjustNormal used to scale back estimates when needed.
            TicksAdjustSlowdown = 1.0;

            // Our Stopwatch has to be high-resolution and have a good-enough frequency to get high-res estimates.
            Unreliable = !Stopwatch.IsHighResolution || TicksAdjustNormal < 1.414; // arbitrary number a little more than one (640K/10*2^15.5 ought to be enough for anybody)
        }

        private static readonly double TicksAdjustNormal;
        private static readonly double TicksAdjustSlowdown;
        private static readonly bool Unreliable;

        /// <summary>
        /// LastTicks and LastTS are used to estimate time values.
        /// LastTicks + (CurrentTS - LastTS) * Scale_Factor
        /// </summary>
        [ThreadStatic]
        private static long LastTicks;

        /// <summary>
        /// LastTS is the Stopwatch timestamp value observed when LastTicks was last assigned.
        /// </summary>
        [ThreadStatic]
        private static long LastTS;

        /// <summary>
        /// Set to true when our estimate overtakes the real system ticks value, so we can
        /// start scaling back future estimates until we converge with the real time.
        /// </summary>
        [ThreadStatic]
        private static bool AdjustTicks;

        /// <summary>
        /// Stores last real system ticks value observed.
        /// </summary>
        [ThreadStatic]
        private static long LastSystemTicks;

        private static DateTime MakeHighResolution(DateTime dt)
        {
            // we're given a DateTime, so let's quickly get a high-res timestamp to accompany it.
            long ts = Stopwatch.GetTimestamp();

            if (!Stopwatch.IsHighResolution) return dt;

            bool systemTicksChanged = false;
            if (dt.Ticks != LastSystemTicks)
            {
                LastSystemTicks = dt.Ticks;
                systemTicksChanged = true;
            }

            if (LastTS == 0L)
            {
                // first time through on this thread, so we'll return system ticks
                LastTicks = dt.Ticks;
                LastTS = ts;
                AdjustTicks = false;
            }
            else
            {
                // give our best estimate of what a new high-res ticks value might be
                long newTicks = LastTicks + (long)((ts - LastTS) * (AdjustTicks ? TicksAdjustSlowdown : TicksAdjustNormal));
                // if system ticks is not less then we'll trust system ticks
                if (dt.Ticks >= newTicks)
                {
                    LastTicks = dt.Ticks;
                    LastTS = ts;
                    AdjustTicks = false;
                }
                else
                {
                    // we calculated a value higher than what we have been given for system ticks.
                    // the most likely scenario is that this is a pretty good estimate and it lies somewhere 
                    // between system ticks we have now and the next system ticks we get.
                    // however, there is the possibility that we'll be returning a value higher than the next
                    // system ticks that comes through.
                    // ...this could be due to being just off a bit due to rounding or inopportune thread interruption,
                    // or it could be that the real time between the first time we see one system ticks value
                    // and the first time we see the next system ticks value is more than the difference between
                    // the two tick values. system ticks don't come in at regular intervals. we need to be prepared....
                    // we want to handle this scenario as best as possible while maintaining two invariants:
                    // 1. each value we return (on a particular thread) should be >= the previous value we returned.
                    // 2. the value we return should be close to the actual time (within range of system 
                    //    timer resolution)
                    // so here if a new system ticks has come in and it's less than what we calculated, then
                    // we'll begin to scale back our future calculations until we have recalibrated, and
                    // soon the system ticks will overtake our calculated values again.
                    if (systemTicksChanged)
                    {
                        // a new system ticks was observed and it's less than newTicks, so we will slow down
                        // our future calculations
                        LastTicks = newTicks;
                        LastTS = ts;
                        AdjustTicks = true;
                    }
                    dt = new DateTime(newTicks);
                }
            }

            return dt;
        }
    }
}
