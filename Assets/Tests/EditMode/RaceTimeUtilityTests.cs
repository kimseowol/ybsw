// PRD-001
using NUnit.Framework;

namespace Ybsw.Racing.Tests
{
    public sealed class RaceTimeUtilityTests
    {
        [Test]
        public void CalculateElapsed_UsesServerTimeDifference()
        {
            Assert.That(RaceTimeUtility.CalculateElapsed(10d, 22.345d), Is.EqualTo(12.345d).Within(0.0001d));
        }

        [Test]
        public void CalculateElapsed_ClampsNegativeResult()
        {
            Assert.That(RaceTimeUtility.CalculateElapsed(10d, 9d), Is.Zero);
        }

        [TestCase(5d, 5)]
        [TestCase(4.01d, 5)]
        [TestCase(4d, 4)]
        [TestCase(0.01d, 1)]
        [TestCase(0d, 0)]
        public void GetCountdownDisplayNumber_ShowsEachWholeStep(double remainingTime, int expected)
        {
            Assert.That(RaceTimeUtility.GetCountdownDisplayNumber(remainingTime), Is.EqualTo(expected));
        }

        [TestCase(0d, "00:00.000")]
        [TestCase(12.345d, "00:12.345")]
        [TestCase(65.432d, "01:05.432")]
        public void FormatElapsed_FormatsRaceRecord(double elapsedTimeInSeconds, string expected)
        {
            Assert.That(RaceTimeUtility.FormatElapsed(elapsedTimeInSeconds), Is.EqualTo(expected));
        }
    }
}
