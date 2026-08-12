# ProxyCore Unlock System — Preventive Fix Recommendations

## Background

A class of bug was discovered where cloak definitions auto-unlocked at game start despite having prerequisites set. The root cause: `DefinitionUnlockedCondition` assets were wired to `QuestDefinition` targets — but since `QuestDefinition._isUnlockedByDefault = true`, calling `UnlockManager.IsUnlocked(anyQuest)` always returns `true` at game start, regardless of quest completion state.

The correct condition type for quest-based prerequisites is `QuestCompletedCondition`, which calls `QuestManager.IsQuestCompleted()`. The `QuestDefinitionEdgeStrategy` creates this correct type when drawing edges from quest nodes in the graph with Pass = "QuestIsCompleted". The wrong condition type was created because the edge was drawn with Pass = "Unlocked" (DefaultDefinitionEdgeStrategy) instead.

This document describes tooling additions that would detect and prevent this mistake at the project level, without modifying the ProxyCore package itself.

---

## What the System Lacks

| Gap | Impact |
|---|---|
| No validation that a condition's target/source type is semantically appropriate | Wrong conditions are silently accepted |
| No editor startup scan for broken prerequisite wiring | Bugs persist undetected until Play mode |
| The Cleanup Dialog only finds unreferenced conditions, not wrong-typed ones | A referenced-but-wrong condition passes cleanup |
| The "Refresh" button in the Unlock Graph only redraws — it does not simulate runtime evaluation | Graph looks correct in Edit mode while Play mode behaves differently |
| `DefinitionUnlockedCondition` targeting a `QuestDefinition` is always trivially TRUE | No runtime warning is emitted |

---

## Recommendation 1 — `OnValidate` on `QuestCompletedCondition`

Add `OnValidate()` to `QuestCompletedCondition` to flag invalid state in the Inspector immediately:

```csharp
// Assets/Scripts/Unlock Conditions/QuestCompletedCondition.cs
#if UNITY_EDITOR
private void OnValidate() {
    if (_quest == null)
        Debug.LogWarning($"[QuestCompletedCondition] '{name}' has no quest assigned.", this);
}
#endif
```

Extend `DefinitionUnlockedCondition` (package type) the same way via a custom validation script:

```csharp
// Assets/Scripts/Editor/UnlockConditionValidator.cs
using UnityEditor;
using UnityEngine;
using ProxyCore;

[CustomEditor(typeof(DefinitionUnlockedCondition))]
internal class DefinitionUnlockedConditionEditor : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();
        var cond = (DefinitionUnlockedCondition)target;
        var so = new SerializedObject(cond);
        var targetProp = so.FindProperty("_target");
        var targetDef = targetProp?.objectReferenceValue;

        if (targetDef is QuestDefinition) {
            EditorGUILayout.HelpBox(
                "Target is a QuestDefinition. DefinitionUnlockedCondition uses IsUnlocked() " +
                "which is always TRUE for quests (_isUnlockedByDefault = true). " +
                "Use QuestCompletedCondition instead.",
                MessageType.Error);
        }
    }
}
```

This surfaces the error whenever the condition asset is selected in the Project window.

---

## Recommendation 2 — Editor Startup Scan

Add an `[InitializeOnLoad]` script that scans all prerequisite conditions on Editor startup and logs errors for semantic mismatches. Run this once per domain reload so it catches problems introduced since the last session.

```csharp
// Assets/Scripts/Editor/UnlockIntegrityChecker.cs
using System.Linq;
using UnityEditor;
using UnityEngine;
using ProxyCore;

[InitializeOnLoad]
internal static class UnlockIntegrityChecker {
    static UnlockIntegrityChecker() {
        // Defer until asset database is ready
        EditorApplication.delayCall += RunCheck;
    }

    private static void RunCheck() {
        int errorCount = 0;

        // Find all DefinitionUnlockedCondition assets
        foreach (string guid in AssetDatabase.FindAssets("t:DefinitionUnlockedCondition")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cond = AssetDatabase.LoadAssetAtPath<DefinitionUnlockedCondition>(path);
            if (cond == null) continue;

            var so = new SerializedObject(cond);
            var targetDef = so.FindProperty("_target")?.objectReferenceValue;

            if (targetDef is QuestDefinition) {
                Debug.LogError(
                    $"[UnlockIntegrity] '{cond.name}' is a DefinitionUnlockedCondition targeting " +
                    $"QuestDefinition '{targetDef.name}'. This always evaluates TRUE. " +
                    $"Replace with QuestCompletedCondition.\nPath: {path}",
                    cond);
                errorCount++;
            }
        }

        // Find definitions with no prerequisites that are not isUnlockedByDefault
        // (catches cases where prerequisites were accidentally cleared)
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Only check project assets, skip packages
            if (!path.StartsWith("Assets/")) continue;

            var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (obj is not IHasPrerequisites prereqs || obj is not IUnlockable unlockable)
                continue;

            bool hasNoPrereqs = prereqs.Prerequisites == null || prereqs.Prerequisites.Count == 0;
            bool notDefault = !unlockable.IsUnlockedByDefault;
            bool autoUnlocks = prereqs.AutoUnlock;

            if (hasNoPrereqs && notDefault && autoUnlocks) {
                // This item will never auto-unlock — silent permanent lock
                // (Only warn for non-Bone-White; filter by type if needed)
                Debug.LogWarning(
                    $"[UnlockIntegrity] '{obj.name}' has AutoUnlock=true, " +
                    $"IsUnlockedByDefault=false, but no prerequisites. It will never unlock.\n" +
                    $"Path: {path}",
                    obj);
            }
        }

        if (errorCount == 0)
            Debug.Log("[UnlockIntegrity] All unlock conditions passed integrity check.");
    }
}
```

**Note**: The empty-prerequisites check has false-positive risk for definitions intentionally kept permanently locked. Add a `[Serializable] bool _intentionallyLocked` field to `CloakDefinition` (or a marker interface) if precision is needed.

---

## Recommendation 3 — Extend the Condition Cleanup Dialog

The existing `ConditionCleanupDialog` (accessible via the Cleanup toolbar button in the Unlock Graph) finds unreferenced conditions. Extend it with a second pass that checks referenced conditions for type mismatches.

Since `ConditionCleanupDialog` is package code, this needs to be a separate Editor window or a menu item that mirrors the pattern:

```csharp
// Assets/Scripts/Editor/UnlockConditionAuditWindow.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ProxyCore;

public class UnlockConditionAuditWindow : EditorWindow {
    private struct Issue {
        public string Description;
        public Object Asset;
    }

    private List<Issue> _issues = new();

    [MenuItem("ProxyCore/Unlock Condition Audit")]
    public static void Open() => GetWindow<UnlockConditionAuditWindow>("Condition Audit");

    private void OnGUI() {
        if (GUILayout.Button("Run Audit")) RunAudit();

        EditorGUILayout.LabelField($"Issues found: {_issues.Count}", EditorStyles.boldLabel);
        foreach (var issue in _issues) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(issue.Description, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Select", GUILayout.Width(60)))
                Selection.activeObject = issue.Asset;
            EditorGUILayout.EndHorizontal();
        }
    }

    private void RunAudit() {
        _issues.Clear();

        foreach (string guid in AssetDatabase.FindAssets("t:DefinitionUnlockedCondition")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cond = AssetDatabase.LoadAssetAtPath<DefinitionUnlockedCondition>(path);
            if (cond == null) continue;

            var so = new SerializedObject(cond);
            var target = so.FindProperty("_target")?.objectReferenceValue;

            if (target is QuestDefinition)
                _issues.Add(new Issue {
                    Description = $"[WRONG TYPE] '{cond.name}' targets QuestDefinition '{target.name}' — use QuestCompletedCondition",
                    Asset = cond
                });

            if (target == cond)
                _issues.Add(new Issue {
                    Description = $"[SELF-REF] '{cond.name}' targets itself — will never be TRUE",
                    Asset = cond
                });
        }

        foreach (string guid in AssetDatabase.FindAssets("t:QuestCompletedCondition")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cond = AssetDatabase.LoadAssetAtPath<QuestCompletedCondition>(path);
            if (cond == null) continue;

            var so = new SerializedObject(cond);
            var quest = so.FindProperty("_quest")?.objectReferenceValue;

            if (quest == null)
                _issues.Add(new Issue {
                    Description = $"[NULL QUEST] '{cond.name}' has no quest assigned — always FALSE",
                    Asset = cond
                });
        }

        Repaint();
    }
}
```

---

## Recommendation 4 — Simulate Runtime State in the Unlock Graph

The "Refresh" button in the Unlock Graph calls `RefreshCatalogEntries()`, which rebuilds the visual display but does **not** call `EvaluateAutoTriggers()`. This causes the graph to show "locked" state in Edit mode for conditions that would trivially pass at runtime.

Add a second button via `IDefinitionEdgeStrategy` extension or a standalone menu item:

```csharp
// Assets/Scripts/Editor/UnlockRuntimeSimulator.cs
using UnityEditor;
using UnityEngine;
using ProxyCore;

public static class UnlockRuntimeSimulator {
    [MenuItem("ProxyCore/Simulate Auto-Unlock (Edit Mode)")]
    public static void SimulateAutoUnlock() {
        var manager = AssetDatabase.LoadAssetAtPath<UnlockManager>(
            AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets("t:UnlockManager")[0]));

        if (manager == null) {
            Debug.LogError("[UnlockSimulator] UnlockManager asset not found.");
            return;
        }

        // Print which items would auto-unlock with no prior state
        Debug.Log("[UnlockSimulator] Items that pass ALL prerequisites with no unlock state:");

        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;

            var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (obj is not IHasPrerequisites prereqs || obj is not IUnlockable unlockable)
                continue;
            if (!prereqs.AutoUnlock || unlockable.IsUnlockedByDefault) continue;

            bool allPass = true;
            foreach (var cond in prereqs.Prerequisites) {
                if (cond == null) { allPass = false; break; }
                try {
                    if (!cond.Evaluate()) { allPass = false; break; }
                }
                catch {
                    allPass = false; break;
                }
            }

            if (allPass)
                Debug.LogWarning(
                    $"[UnlockSimulator] '{obj.name}' would AUTO-UNLOCK immediately " +
                    $"(all {prereqs.Prerequisites.Count} conditions currently evaluate TRUE).",
                    obj);
        }
    }
}
```

Run this from the menu before entering Play mode to catch any condition that trivially passes in the current Editor state.

---

## Recommendation 5 — `QuestDefinitionEdgeStrategy` Self-Check

Add a guard inside `QuestDefinitionEdgeStrategy.GetOrCreateCondition()` that deletes any pre-existing `DefinitionUnlockedCondition` targeting the same quest (the wrong type) before creating the correct one. This handles the case where a user re-draws an edge that previously used the wrong strategy:

```csharp
// In QuestDefinitionEdgeStrategy.GetOrCreateCondition():
public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
    if (source is not QuestDefinition questDefinition) return null;

    // Delete any wrong-type condition targeting this quest
    foreach (string guid in AssetDatabase.FindAssets("t:DefinitionUnlockedCondition")) {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var duc = AssetDatabase.LoadAssetAtPath<DefinitionUnlockedCondition>(path);
        if (duc == null) continue;
        var so = new SerializedObject(duc);
        if (so.FindProperty("_target")?.objectReferenceValue == questDefinition) {
            AssetDatabase.DeleteAsset(path);
            Debug.Log($"[QuestStrategy] Removed wrong-type condition '{duc.name}' targeting quest '{questDefinition.name}'.");
        }
    }

    // Existing correct-type check + creation (unchanged)
    var existing = FindExistingConditionAsset(questDefinition);
    if (existing != null) return existing;
    // ... rest of creation code
}
```

---

## Priority Order

| Priority | Recommendation | Effort | Value |
|---|---|---|---|
| High | Rec 2 — Editor startup scan | Low (one new file) | Catches all future mismatches on domain reload |
| High | Rec 1 — Custom Inspector warning | Low (one new file) | Immediate visual feedback in Inspector |
| Medium | Rec 3 — Condition Audit Window | Medium | Actionable audit on demand |
| Medium | Rec 4 — Runtime Simulator | Medium | Shows true auto-unlock state before Play mode |
| Low | Rec 5 — Strategy self-cleanup | Low (edit existing file) | Defensive repair when re-drawing edges |

Recommendations 1 and 2 together cover the discovery gap: Rec 2 catches the bug on next domain reload, Rec 1 surfaces it when the asset is inspected directly.
