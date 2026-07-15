// PRD-001
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Ybsw.Racing
{
    public sealed class RaceCamera : MonoBehaviour
    {
        const float FollowDistance = 5.06f;
        const float FocusHeight = 0.7f;
        const float DefaultPitchInDegrees = 24.5f;

        Transform target;
        Vector3 followVelocity;
        float orbitYawInDegrees;
        float orbitPitchInDegrees = DefaultPitchInDegrees;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            followVelocity = Vector3.zero;
            orbitYawInDegrees = 0f;
            orbitPitchInDegrees = DefaultPitchInDegrees;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            ApplyOrbitInput();

            Vector3 focusPosition = target.position + Vector3.up * FocusHeight;
            float targetYawInDegrees = target.eulerAngles.y + orbitYawInDegrees;
            Quaternion orbitRotation = Quaternion.Euler(
                orbitPitchInDegrees,
                targetYawInDegrees,
                0f);
            Vector3 desiredPosition = focusPosition + orbitRotation * Vector3.back * FollowDistance;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref followVelocity,
                0.12f);
            transform.rotation = Quaternion.LookRotation(focusPosition - transform.position);
        }

        void ApplyOrbitInput()
        {
            Vector2 dragDelta = ReadOrbitDragDelta();
            if (dragDelta.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector2 orbitAngles = RaceInputUtility.CalculateOrbitAngles(
                new Vector2(orbitYawInDegrees, orbitPitchInDegrees),
                dragDelta,
                new Vector2(Screen.width, Screen.height));
            orbitYawInDegrees = orbitAngles.x;
            orbitPitchInDegrees = orbitAngles.y;
        }

        static Vector2 ReadOrbitDragDelta()
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                for (int index = 0; index < touchscreen.touches.Count; index++)
                {
                    TouchControl touch = touchscreen.touches[index];
                    if (!touch.press.isPressed)
                    {
                        continue;
                    }

                    Vector2 touchPosition = touch.position.ReadValue();
                    if (!IsInMovementControlZone(touchPosition))
                    {
                        return touch.delta.ReadValue();
                    }
                }
            }

#if UNITY_EDITOR
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                Vector2 mousePosition = mouse.position.ReadValue();
                if (!IsInMovementControlZone(mousePosition))
                {
                    return mouse.delta.ReadValue();
                }
            }
#endif

            return Vector2.zero;
        }

        static bool IsInMovementControlZone(Vector2 pointerPosition)
        {
            return RaceInputUtility.IsInMovementControlZone(
                pointerPosition,
                new Vector2(Screen.width, Screen.height));
        }
    }
}
