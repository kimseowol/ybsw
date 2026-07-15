using UnityEditor;
using UnityEngine;
#if UNITY_6000_4_OR_NEWER
using HierarchyItemId = UnityEngine.EntityId;
#else
using HierarchyItemId = System.Int32;
#endif

namespace Alchemy.Editor
{
    /// <summary>
    /// Base class for adding custom drawing processing to hierarchy items.
    /// </summary>
    public abstract class HierarchyDrawer
    {
        public abstract void OnGUI(HierarchyItemId instanceID, Rect selectionRect);

        protected static Rect GetBackgroundRect(Rect selectionRect)
        {
            return selectionRect.AddXMax(20f);
        }

        protected static void DrawBackground(HierarchyItemId instanceID, Rect selectionRect)
        {
            var backgroundRect = GetBackgroundRect(selectionRect);

            Color backgroundColor;
            var e = Event.current;
            var isHover = backgroundRect.Contains(e.mousePosition);

#if UNITY_6000_4_OR_NEWER
            if (Selection.Contains(instanceID))
#elif UNITY_6000_3_OR_NEWER
            if (Selection.Contains((EntityId)instanceID))
#else
            if (Selection.Contains(instanceID))
#endif
            {
                backgroundColor = EditorColors.HighlightBackground;
            }
            else if (isHover)
            {
                backgroundColor = EditorColors.HighlightBackgroundInactive;
            }
            else
            {
                backgroundColor = EditorColors.WindowBackground;
            }

            EditorGUI.DrawRect(backgroundRect, backgroundColor);
        }
    }
}
