# Tharsis compatibility patches

This embedded package is based on Alchemy 2.1.0 at commit
`d9f5a2b8f9b7e79ac7fc7eb188b607bd60910398`.

The project-local suffix is `2.1.0-tharsis.2`. It contains the following
Unity 6000 compatibility changes:

- use `EditorUtility.EntityIdToObject` instead of the obsolete instance-ID API;
- use `Selection.Contains(EntityId)` where the integer overload is obsolete;
- subscribe to `hierarchyWindowItemByEntityIdOnGUI` on Unity 6000.4 and newer.
- use `AdvancedDropdownItem.childList` on Unity 6000.5 and newer.
- remove the `TabGroup` HelpBox label from its actual UI Toolkit parent because
  Unity 6000.5 no longer exposes that label as a direct child.

Alchemy's Inspector behavior is otherwise unchanged. Remove this embedded
package and restore the pinned official Git dependency once an upstream release
contains equivalent compatibility changes.
