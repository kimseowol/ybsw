// PRD-001
using UnityEngine;

namespace Ybsw.Racing
{
    public static class RaceInputUtility
    {
        const float CountdownTurnInputScale = 0.35f;
        const float HorizontalOrbitDegreesPerScreen = 180f;
        const float VerticalOrbitDegreesPerScreen = 100f;
        const float MinimumOrbitPitchInDegrees = 10f;
        const float MaximumOrbitPitchInDegrees = 65f;

        public static Vector2 CalculateVirtualStick(Vector2 center, Vector2 pointerPosition, float radius)
        {
            if (radius <= 0f)
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude((pointerPosition - center) / radius, 1f);
        }

        public static Vector2 ChooseMovementInput(Vector2 keyboardInput, Vector2 touchInput)
        {
            return touchInput.sqrMagnitude > 0.001f ? touchInput : keyboardInput;
        }

        public static bool IsInMovementControlZone(Vector2 pointerPosition, Vector2 screenSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return false;
            }

            float normalizedX = pointerPosition.x / screenSize.x;
            float normalizedY = pointerPosition.y / screenSize.y;
            return normalizedX >= 0f
                && normalizedY >= 0f
                && normalizedX <= 0.45f
                && normalizedY <= 0.5f;
        }

        public static Vector2 CalculateOrbitAngles(
            Vector2 currentAnglesInDegrees,
            Vector2 dragDeltaInPixels,
            Vector2 screenSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return currentAnglesInDegrees;
            }

            float yawInDegrees = currentAnglesInDegrees.x
                + dragDeltaInPixels.x / screenSize.x * HorizontalOrbitDegreesPerScreen;
            float pitchInDegrees = Mathf.Clamp(
                currentAnglesInDegrees.y
                    - dragDeltaInPixels.y / screenSize.y * VerticalOrbitDegreesPerScreen,
                MinimumOrbitPitchInDegrees,
                MaximumOrbitPitchInDegrees);
            return new Vector2(yawInDegrees, pitchInDegrees);
        }

        public static Vector2 FilterInputForPhase(Vector2 input, RacePhase phase, bool hasFinished)
        {
            if (hasFinished)
            {
                return Vector2.zero;
            }

            if (phase == RacePhase.Countdown)
            {
                return new Vector2(input.x * CountdownTurnInputScale, 0f);
            }

            return phase == RacePhase.Racing ? input : Vector2.zero;
        }
    }
}
