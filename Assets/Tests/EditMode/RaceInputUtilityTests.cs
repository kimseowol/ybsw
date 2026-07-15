// PRD-001
using NUnit.Framework;
using UnityEngine;

namespace Ybsw.Racing.Tests
{
    public sealed class RaceInputUtilityTests
    {
        [Test]
        public void CalculateVirtualStick_ClampsPointerOutsideRadius()
        {
            Vector2 result = RaceInputUtility.CalculateVirtualStick(
                new Vector2(100f, 100f),
                new Vector2(300f, 100f),
                50f);

            Assert.That(result, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void CalculateVirtualStick_ReturnsZeroForInvalidRadius()
        {
            Vector2 result = RaceInputUtility.CalculateVirtualStick(Vector2.zero, Vector2.one, 0f);

            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ChooseMovementInput_PrefersActiveTouchInput()
        {
            Vector2 result = RaceInputUtility.ChooseMovementInput(Vector2.up, Vector2.right);

            Assert.That(result, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void ChooseMovementInput_UsesKeyboardWhenTouchIsIdle()
        {
            Vector2 result = RaceInputUtility.ChooseMovementInput(Vector2.up, Vector2.zero);

            Assert.That(result, Is.EqualTo(Vector2.up));
        }

        [TestCase(100f, 100f, true)]
        [TestCase(450f, 500f, true)]
        [TestCase(451f, 500f, false)]
        [TestCase(450f, 501f, false)]
        public void IsInMovementControlZone_SplitsMovementAndCameraAreas(
            float pointerX,
            float pointerY,
            bool expected)
        {
            bool result = RaceInputUtility.IsInMovementControlZone(
                new Vector2(pointerX, pointerY),
                new Vector2(1000f, 1000f));

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void CalculateOrbitAngles_AppliesNormalizedDrag()
        {
            Vector2 result = RaceInputUtility.CalculateOrbitAngles(
                new Vector2(10f, 30f),
                new Vector2(500f, 250f),
                new Vector2(1000f, 1000f));

            Assert.That(result.x, Is.EqualTo(100f).Within(0.001f));
            Assert.That(result.y, Is.EqualTo(10f).Within(0.001f));
        }

        [TestCase(1000f, 10f)]
        [TestCase(-1000f, 65f)]
        public void CalculateOrbitAngles_ClampsVerticalView(float dragY, float expectedPitch)
        {
            Vector2 result = RaceInputUtility.CalculateOrbitAngles(
                new Vector2(0f, 30f),
                new Vector2(0f, dragY),
                new Vector2(1000f, 1000f));

            Assert.That(result.y, Is.EqualTo(expectedPitch));
        }

        [Test]
        public void FilterInputForPhase_CountdownRemovesMovementAndAllowsReducedTurning()
        {
            Vector2 result = RaceInputUtility.FilterInputForPhase(Vector2.one, RacePhase.Countdown, false);

            Assert.That(result.x, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(result.y, Is.Zero);
        }

        [Test]
        public void FilterInputForPhase_RacingAllowsFullInput()
        {
            Vector2 input = new Vector2(-0.5f, 0.75f);

            Vector2 result = RaceInputUtility.FilterInputForPhase(input, RacePhase.Racing, false);

            Assert.That(result, Is.EqualTo(input));
        }

        [TestCase(RacePhase.Waiting)]
        [TestCase(RacePhase.Finished)]
        public void FilterInputForPhase_NonDrivingPhaseRemovesAllInput(RacePhase phase)
        {
            Vector2 result = RaceInputUtility.FilterInputForPhase(Vector2.one, phase, false);

            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FilterInputForPhase_FinishedPlayerCannotMove()
        {
            Vector2 result = RaceInputUtility.FilterInputForPhase(Vector2.one, RacePhase.Racing, true);

            Assert.That(result, Is.EqualTo(Vector2.zero));
        }
    }
}
