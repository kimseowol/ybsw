// PRD-001
using System;
using System.Globalization;

namespace Ybsw.Racing
{
    public static class RaceTimeUtility
    {
        public static double CalculateElapsed(double startedAt, double finishedAt)
        {
            return Math.Max(0d, finishedAt - startedAt);
        }

        public static int GetCountdownDisplayNumber(double remainingTimeInSeconds)
        {
            return (int)Math.Ceiling(Math.Max(0d, remainingTimeInSeconds));
        }

        public static string FormatElapsed(double elapsedTimeInSeconds)
        {
            long totalMilliseconds = (long)Math.Round(
                Math.Max(0d, elapsedTimeInSeconds) * 1000d,
                MidpointRounding.AwayFromZero);
            long minutes = totalMilliseconds / 60000L;
            long seconds = totalMilliseconds % 60000L / 1000L;
            long milliseconds = totalMilliseconds % 1000L;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}.{2:000}",
                minutes,
                seconds,
                milliseconds);
        }
    }
}
