// PRD-001
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Ybsw.Racing
{
    public sealed class RaceTouchControls : MonoBehaviour
    {
        [SerializeField] bool showInEditor = true;

        static Vector2 currentInput;

        public static Vector2 CurrentInput => currentInput;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            currentInput = Vector2.zero;
        }

        void OnDisable()
        {
            currentInput = Vector2.zero;
        }

        void Update()
        {
            if (!ShouldShowControls())
            {
                currentInput = Vector2.zero;
                return;
            }

            Vector2 pointerPosition;
            bool isPressed = TryReadMovementPointer(out pointerPosition);
            Vector2 center = GetStickCenter();
            float radius = GetStickRadius();

            currentInput = isPressed
                ? RaceInputUtility.CalculateVirtualStick(center, pointerPosition, radius)
                : Vector2.zero;
        }

        void OnGUI()
        {
            if (!ShouldShowControls())
            {
                return;
            }

            Vector2 center = GetStickCenter();
            float radius = GetStickRadius();
            Vector2 guiCenter = new Vector2(center.x, Screen.height - center.y);
            Vector2 knobCenter = guiCenter + new Vector2(currentInput.x, -currentInput.y) * radius * 0.62f;

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            GUI.Box(
                new Rect(guiCenter.x - radius, guiCenter.y - radius, radius * 2f, radius * 2f),
                "MOVE\nWASD");
            GUI.color = new Color(1f, 0.78f, 0.2f, 0.8f);
            float knobSize = radius * 0.62f;
            GUI.Box(
                new Rect(knobCenter.x - knobSize * 0.5f, knobCenter.y - knobSize * 0.5f, knobSize, knobSize),
                string.Empty);
            GUI.color = previousColor;
        }

        bool ShouldShowControls()
        {
            return Application.isMobilePlatform || showInEditor;
        }

        static bool TryReadMovementPointer(out Vector2 pointerPosition)
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
                    if (IsInMovementControlZone(touchPosition))
                    {
                        pointerPosition = touchPosition;
                        return true;
                    }
                }
            }

#if UNITY_EDITOR
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                Vector2 mousePosition = mouse.position.ReadValue();
                if (IsInMovementControlZone(mousePosition))
                {
                    pointerPosition = mousePosition;
                    return true;
                }
            }
#endif

            pointerPosition = Vector2.zero;
            return false;
        }

        static bool IsInMovementControlZone(Vector2 pointerPosition)
        {
            return RaceInputUtility.IsInMovementControlZone(
                pointerPosition,
                new Vector2(Screen.width, Screen.height));
        }

        static Vector2 GetStickCenter()
        {
            return new Vector2(Screen.width * 0.16f, Screen.height * 0.2f);
        }

        static float GetStickRadius()
        {
            return Mathf.Min(Screen.width, Screen.height) * 0.12f;
        }
    }
}
