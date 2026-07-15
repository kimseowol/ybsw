using UnityEditor;
using UnityEngine;
using Alchemy.Hierarchy;
#if UNITY_6000_4_OR_NEWER
using HierarchyItemId = UnityEngine.EntityId;
#else
using HierarchyItemId = System.Int32;
#endif

namespace Alchemy.Editor
{
    public sealed class HierarchyHeaderDrawer : HierarchyDrawer
    {
        static Color HeaderColor => EditorGUIUtility.isProSkin ? new(0.45f, 0.45f, 0.45f, 0.5f) : new(0.55f, 0.55f, 0.55f, 0.5f);
        static GUIStyle labelStyle;

        public override void OnGUI(HierarchyItemId instanceID, Rect selectionRect)
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                };
            }

#if UNITY_6000_3_OR_NEWER
            var gameObject = EditorUtility.EntityIdToObject(instanceID) as GameObject;
#else
            var gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (gameObject == null) return;
            if (!gameObject.TryGetComponent<HierarchyHeader>(out _)) return;

            DrawBackground(instanceID, selectionRect);

            var headerRect = selectionRect.AddXMax(14f).AddYMax(-1f);
            EditorGUI.DrawRect(headerRect, HeaderColor);
            EditorGUI.LabelField(headerRect, gameObject.name, labelStyle);
        }
    }
}
