using MobileDemo.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;

namespace MobileDemo.Editor
{
    // The first file that actually lives in MobileDemo.Editor. ARCHITECTURE.md §11 reserved the
    // assembly for this from the first line of code, and §15 declines Odin Inspector on the
    // grounds that hand-written tooling "is the flex here" -- a claim this file is the first thing
    // to back.
    //
    // It exists for three jobs the runtime deliberately cannot do:
    //  - Numbered waypoints. EnemyPath.OnDrawGizmos says why it cannot label its own nodes:
    //    Handles.Label needs UnityEditor, which a runtime assembly may not reference.
    //  - Re-baking after a drag. systems/enemy.md names this as the tool's hard requirement --
    //    EnemyPath.Waypoints is a cache, so a moved handle is invisible until Bake runs.
    //  - Adding and removing a waypoint as one action. The array is Transform[], so by hand that
    //    is "make a child, then remember to add it to the array", and a forgotten second half is
    //    silent: the node draws nothing and enemies walk straight past where it should be.
    //
    // Base class written as UnityEditor.Editor on purpose: unqualified `Editor` inside a namespace
    // called MobileDemo.Editor binds to the namespace and fails to compile.
    [CustomEditor(typeof(EnemyPath))]
    public sealed class PathEditor : UnityEditor.Editor
    {
        // Named to match what the prefabs already hold (Waypoint00..Waypoint12), so an inserted
        // node is indistinguishable from an authored one.
        const string WaypointPrefix = "Waypoint";

        SerializedProperty waypoints;
        int editIndex;

        void OnEnable() => waypoints = serializedObject.FindProperty("waypoints");

        public override void OnInspectorGUI()
        {
            // The default array editor stays. This tool adds to hand-authoring rather than
            // replacing it -- retargeting one slot at an existing Transform is still quickest here.
            DrawDefaultInspector();

            serializedObject.Update();
            DrawWarnings();
            DrawButtons();
            serializedObject.ApplyModifiedProperties();
        }

        // Two checks, and both are cases the runtime is deliberately silent about -- so the
        // inspector is the only place they can be caught before they cost a play session.
        void DrawWarnings()
        {
            if (waypoints.arraySize < 2)
            {
                EditorGUILayout.HelpBox(
                    "A path needs at least two waypoints. EnemyPath logs an error and bakes an "
                    + "empty array, and Bootstrap then refuses to run the level.",
                    MessageType.Error);
                return;
            }

            // EnemyPath.Bake guards the array's *length*, not its entries, so a filled array with
            // one empty slot passes that guard and then throws a NullReferenceException on the
            // bake. OnDrawGizmos skips nulls, so the scene view looks fine right up to Play.
            for (int i = 0; i < waypoints.arraySize; i++)
            {
                if (waypoints.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Waypoint {i} is empty. EnemyPath.Bake checks the array length but not its "
                        + "entries, so this throws on the first bake rather than logging.",
                        MessageType.Error);
                    break;
                }
            }

            // A zero-length segment takes PlacementRules.DistanceToSegmentSqr's degenerate branch,
            // which collapses the segment to a point -- so the road's no-build clearance quietly
            // shrinks around a duplicated node, with nothing logged anywhere.
            for (int i = 1; i < waypoints.arraySize; i++)
            {
                Transform previous = waypoints.GetArrayElementAtIndex(i - 1).objectReferenceValue as Transform;
                Transform current = waypoints.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (previous == null || current == null)
                {
                    continue;
                }

                if ((Vector2)current.position == (Vector2)previous.position)
                {
                    EditorGUILayout.HelpBox(
                        $"Waypoints {i - 1} and {i} sit on the same point. PlacementRules treats a "
                        + "zero-length segment as a point, so the road's build clearance shrinks "
                        + "there without any warning at runtime.",
                        MessageType.Warning);
                    break;
                }
            }
        }

        void DrawButtons()
        {
            EnemyPath path = (EnemyPath)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Append waypoint"))
            {
                Insert(path, waypoints.arraySize);
            }

            // An index rather than only "at the end", because the edit a twelve-wave difficulty
            // pass actually needs is a corner added mid-road -- and doing that by hand means
            // appending, then dragging the slot up the array one step at a time.
            using (new EditorGUI.DisabledScope(waypoints.arraySize == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                editIndex = Mathf.Clamp(
                    EditorGUILayout.IntField("At index", editIndex), 0, Mathf.Max(0, waypoints.arraySize - 1));

                if (GUILayout.Button("Insert"))
                {
                    Insert(path, editIndex);
                }

                if (GUILayout.Button("Remove"))
                {
                    Remove(path, editIndex);
                }
            }

            EditorGUILayout.HelpBox(
                "Drag the numbered handles in the scene view. Every edit re-bakes, so a change is "
                + "live for the next wave -- enemies already walking keep the road they spawned on.",
                MessageType.Info);
        }

        // One undo group per edit, so Ctrl-Z takes back the child object and the array slot
        // together. Without the group they are two steps and the first Ctrl-Z leaves an orphan.
        void Insert(EnemyPath path, int index)
        {
            Undo.SetCurrentGroupName("Insert waypoint");
            int group = Undo.GetCurrentGroup();

            GameObject waypoint = new GameObject($"{WaypointPrefix}{index:00}");
            Undo.RegisterCreatedObjectUndo(waypoint, "Create waypoint");
            Undo.SetTransformParent(waypoint.transform, path.transform, "Parent waypoint");

            // Offset from the previous node rather than dropped at the origin: a new handle on top
            // of an old one is the coincident-waypoint warning above, created by the tool itself.
            waypoint.transform.position = NextPosition(path, index);

            serializedObject.Update();
            waypoints.InsertArrayElementAtIndex(index);
            waypoints.GetArrayElementAtIndex(index).objectReferenceValue = waypoint.transform;
            serializedObject.ApplyModifiedProperties();

            Renumber(path);
            Rebake(path);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = path.gameObject;
        }

        void Remove(EnemyPath path, int index)
        {
            if (index < 0 || index >= waypoints.arraySize)
            {
                return;
            }

            Undo.SetCurrentGroupName("Remove waypoint");
            int group = Undo.GetCurrentGroup();

            Transform doomed = waypoints.GetArrayElementAtIndex(index).objectReferenceValue as Transform;

            serializedObject.Update();

            // Twice on an object reference: the first assigns null, the second removes the slot.
            // A single call on a populated element is the classic Unity array-editing surprise.
            waypoints.DeleteArrayElementAtIndex(index);
            if (waypoints.arraySize > index
                && waypoints.GetArrayElementAtIndex(index).objectReferenceValue == doomed)
            {
                waypoints.DeleteArrayElementAtIndex(index);
            }

            serializedObject.ApplyModifiedProperties();

            // Only a child of this path is destroyed. A slot retargeted at some other Transform is
            // not this tool's to delete.
            if (doomed != null && doomed.parent == path.transform)
            {
                Undo.DestroyObjectImmediate(doomed.gameObject);
            }

            Renumber(path);
            Rebake(path);
            Undo.CollapseUndoOperations(group);
        }

        void OnSceneGUI()
        {
            EnemyPath path = (EnemyPath)target;
            serializedObject.Update();

            for (int i = 0; i < waypoints.arraySize; i++)
            {
                Transform waypoint = waypoints.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (waypoint == null)
                {
                    continue;
                }

                Handles.color = Color.Lerp(Color.green, Color.red, i / Mathf.Max(1f, waypoints.arraySize - 1f));
                Handles.Label(waypoint.position + Vector3.up * 0.35f, $"{i}");

                float size = HandleUtility.GetHandleSize(waypoint.position) * 0.12f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(
                    waypoint.position, size, Vector3.zero, Handles.SphereHandleCap);
                if (!EditorGUI.EndChangeCheck())
                {
                    continue;
                }

                Undo.RecordObject(waypoint, "Move waypoint");

                // z is held rather than taken from the handle. The board is 2D and the bake
                // truncates z anyway, so a drag in a rotated scene view would otherwise push a
                // waypoint off the plane where nothing at runtime would ever show it.
                waypoint.position = new Vector3(moved.x, moved.y, waypoint.position.z);
                Rebake(path);
            }
        }

        // Children are renamed to match their index, so the hierarchy stays readable after an
        // insert in the middle. The array is authoritative for the bake -- the names are for the
        // human, which is exactly why they must not be allowed to lie.
        void Renumber(EnemyPath path)
        {
            serializedObject.Update();
            for (int i = 0; i < waypoints.arraySize; i++)
            {
                Transform waypoint = waypoints.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (waypoint == null || waypoint.parent != path.transform)
                {
                    continue;
                }

                string wanted = $"{WaypointPrefix}{i:00}";
                if (waypoint.name != wanted)
                {
                    Undo.RecordObject(waypoint.gameObject, "Renumber waypoint");
                    waypoint.name = wanted;
                }
            }
        }

        Vector3 NextPosition(EnemyPath path, int index)
        {
            serializedObject.Update();
            int previous = Mathf.Min(index, waypoints.arraySize) - 1;
            if (previous >= 0
                && waypoints.GetArrayElementAtIndex(previous).objectReferenceValue is Transform anchor)
            {
                return anchor.position + Vector3.right;
            }

            return path.transform.position;
        }

        // Guarded rather than called unconditionally, because Bake logs an error under two
        // waypoints and throws on a null entry -- and a path being briefly invalid is the normal
        // state of one being authored, not something worth a red console line per keystroke.
        static void Rebake(EnemyPath path)
        {
            SerializedObject serialized = new SerializedObject(path);
            SerializedProperty array = serialized.FindProperty("waypoints");
            if (array.arraySize < 2)
            {
                return;
            }

            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    return;
                }
            }

            path.Bake();
        }
    }
}
