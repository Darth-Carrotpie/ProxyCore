using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Core GraphView for the Unlock Dependency Graph.
    /// Supports zoom, pan, selection, minimap, context-menu creation
    /// of definitions / conditions / groups, and edge management that
    /// round-trips to <see cref="UnlockCondition"/> ScriptableObjects.
    /// </summary>
    public sealed class UnlockGraphView : GraphView {
        // ── Lookups ──────────────────────────────────────────────────────
        private readonly Dictionary<string, DefinitionNode> _definitionNodes = new();
        private readonly Dictionary<string, ConditionNode> _conditionNodes = new();
        private readonly Dictionary<string, UnlockGraphGroup> _groups = new();
        private readonly Dictionary<string, SubGraphNode> _subGraphNodes = new();

        // When true, the graphViewChanged callback ignores edge removals
        // (used during ClearGraph to avoid deleting actual SO data).
        private bool _suppressChanges;

        // Search window provider for SPACE / edge-drop menus
        private NodeSearchProvider _searchProvider;

        // Port that initiated a drag-to-empty (set by edge connector listener)
        private Port _pendingDropPort;

        // Cached reference to the host EditorWindow (needed for coordinate
        // conversions when the SearchWindow has stolen focus).
        internal EditorWindow HostWindow { get; set; }

        // ── Layout persistence ───────────────────────────────────────────
        internal UnlockGraphLayoutData _layoutData;

        // ── Path settings (set by window) ────────────────────────────────
        public string DefinitionsPath { get; set; } = "Assets";
        public string ConditionsPath { get; set; } = "Assets";

        // ── Events for the host window ───────────────────────────────────
        public event Action OnGraphChanged;

        internal void NotifyGraphChanged() => OnGraphChanged?.Invoke();

        private MiniMap _miniMap;

        // ════════════════════════════════════════════════════════════════
        // Construction
        // ════════════════════════════════════════════════════════════════

        public UnlockGraphView() {
            AddToClassList("unlock-graph-view");

            // Load stylesheet
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                FindUssPath("UnlockGraphStyles"));
            if (uss != null) styleSheets.Add(uss);

            // Standard manipulators
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Grid background
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // Minimap
            _miniMap = new MiniMap { anchored = true };
            _miniMap.SetPosition(new Rect(10, 30, 200, 140));
            Add(_miniMap);

            // GraphView change callback for position persistence
            graphViewChanged += OnGraphViewChanged;

            // focusable so we can receive key events
            focusable = true;

            // Search provider
            _searchProvider = ScriptableObject.CreateInstance<NodeSearchProvider>();
            _searchProvider.GraphView = this;
            nodeCreationRequest = ctx => {
                _pendingDropPort = null;
                SearchWindow.Open(
                    new SearchWindowContext(ctx.screenMousePosition), _searchProvider);
            };

            // Register undo
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        ~UnlockGraphView() {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        // ════════════════════════════════════════════════════════════════
        // Port compatibility
        // ════════════════════════════════════════════════════════════════

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) {
            var compatible = new List<Port>();
            ports.ForEach(port => {
                if (port == startPort) return;
                if (port.node == startPort.node) return;
                if (port.direction == startPort.direction) return;

                // DefinitionNode ↔ DefinitionNode or ConditionNode → DefinitionNode
                if (startPort.portType == typeof(BaseDefinition) &&
                    port.portType == typeof(BaseDefinition))
                    compatible.Add(port);
                else if (startPort.portType == typeof(UnlockCondition) &&
                         port.portType == typeof(BaseDefinition) &&
                         port.direction == Direction.Input)
                    compatible.Add(port);
                else if (startPort.portType == typeof(BaseDefinition) &&
                         port.portType == typeof(UnlockCondition) &&
                         startPort.direction == Direction.Input)
                    compatible.Add(port);
            });
            return compatible;
        }

        // ════════════════════════════════════════════════════════════════
        // Context menu
        // ════════════════════════════════════════════════════════════════

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt) {
            base.BuildContextualMenu(evt);

            var mousePos = contentViewContainer.WorldToLocal(evt.mousePosition);

            // ── Create Definition ────────────────────────────────────
            var defTypes = FindConcreteTypes<BaseDefinition>(typeof(IUnlockable));
            foreach (var type in defTypes) {
                evt.menu.AppendAction($"Create Definition/{type.Name}",
                    _ => CreateDefinitionAsset(type, mousePos));
            }

            // ── Create Condition ─────────────────────────────────────
            var condTypes = FindConcreteTypes<UnlockCondition>();
            foreach (var type in condTypes) {
                evt.menu.AppendAction($"Create Condition/{type.Name}",
                    _ => CreateConditionAsset(type, mousePos));
            }

            // ── Group actions ────────────────────────────────────────
            if (selection.OfType<ISelectable>().Any(s => s is DefinitionNode or ConditionNode)) {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Group Selected", _ => GroupSelectedNodes());

                foreach (var kvp in _groups) {
                    var gid = kvp.Key;
                    var gname = kvp.Value.title;
                    evt.menu.AppendAction($"Add Selected to Group/{gname}",
                        _ => AddSelectedToGroup(gid));
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // GraphView change handler — edge create / delete, node move
        // ════════════════════════════════════════════════════════════════

        private GraphViewChange OnGraphViewChanged(GraphViewChange change) {
            if (_suppressChanges) return change;

            // ── Edges created (user dragged a new connection) ────────
            if (change.edgesToCreate != null) {
                foreach (var edge in change.edgesToCreate)
                    HandleEdgeCreated(edge);
            }

            // ── Elements removed ─────────────────────────────────────
            if (change.elementsToRemove != null) {
                foreach (var element in change.elementsToRemove) {
                    if (element is Edge edge)
                        HandleEdgeRemoved(edge);
                }
            }

            // ── Nodes moved — persist positions ──────────────────────
            if (change.movedElements != null && _layoutData != null) {
                bool dirty = false;
                foreach (var el in change.movedElements) {
                    if (el is DefinitionNode dn) {
                        _layoutData.SetNodePosition(dn.AssetGuid,
                            dn.GetPosition().position);
                        dirty = true;
                    }
                    else if (el is ConditionNode cn) {
                        _layoutData.SetNodePosition(cn.NodeId,
                            cn.GetPosition().position);
                        dirty = true;
                    }
                    else if (el is UnlockGraphGroup grp) {
                        var entry = _layoutData.GetGroupEntry(grp.GroupId);
                        if (entry != null) {
                            entry.rect = grp.GetPosition();
                            dirty = true;
                        }
                    }
                }

                if (dirty) {
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            OnGraphChanged?.Invoke();
            return change;
        }

        // ════════════════════════════════════════════════════════════════
        // Edge → SO synchronisation
        // ════════════════════════════════════════════════════════════════

        private void HandleEdgeCreated(Edge edge) {
            // Definition Output → Definition Input  ⇒  create DefinitionUnlockedCondition
            if (edge.output?.node is DefinitionNode source &&
                edge.input?.node is DefinitionNode target) {
                CreateDependencyEdge(source.Definition, target.Definition);
            }
            // ConditionNode Output → DefinitionNode Input  ⇒  add condition to prereqs
            else if (edge.output?.node is ConditionNode condNode &&
                     edge.input?.node is DefinitionNode defNode) {
                AddConditionToDefinition(condNode.Condition, defNode.Definition);
            }
        }

        private void HandleEdgeRemoved(Edge edge) {
            if (edge.output?.node is DefinitionNode source &&
                edge.input?.node is DefinitionNode target) {
                RemoveDependencyEdge(source.Definition, target.Definition);
            }
            else if (edge.output?.node is ConditionNode condNode &&
                     edge.input?.node is DefinitionNode defNode) {
                RemoveConditionFromDefinition(condNode.Condition, defNode.Definition);
            }
        }

        /// <summary>
        /// Creates a <see cref="DefinitionUnlockedCondition"/> SO wiring
        /// <paramref name="source"/> as a prerequisite of <paramref name="target"/>.
        /// Reuses an existing condition asset if one already targets the same source.
        /// </summary>
        private void CreateDependencyEdge(BaseDefinition source, BaseDefinition target) {
            if (!(target is IHasPrerequisites hasPrereqs)) {
                Debug.LogWarning($"[UnlockGraph] {target.name} does not implement IHasPrerequisites.");
                return;
            }

            // Check if the target already has a prerequisite pointing at source
            if (hasPrereqs.Prerequisites != null) {
                foreach (var existing in hasPrereqs.Prerequisites) {
                    if (existing is DefinitionUnlockedCondition duc) {
                        var eso = new SerializedObject(duc);
                        var et = eso.FindProperty("_target").objectReferenceValue as BaseDefinition;
                        if (et == source) return; // already wired
                    }
                }
            }

            // Try to find an existing DefinitionUnlockedCondition asset
            // that already references the source definition
            DefinitionUnlockedCondition condition = FindExistingConditionAsset(source);

            if (condition == null) {
                condition = ScriptableObject.CreateInstance<DefinitionUnlockedCondition>();
                condition.name = $"{source.name}_Unlocked";

                var condSO = new SerializedObject(condition);
                condSO.FindProperty("_target").objectReferenceValue = source;
                condSO.ApplyModifiedPropertiesWithoutUndo();

                EnsureFolderExists(ConditionsPath);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{ConditionsPath}/{condition.name}.asset");
                AssetDatabase.CreateAsset(condition, assetPath);
            }

            // Add condition to target's prerequisites
            AddConditionToDefinition(condition, target);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Scans the asset database for a <see cref="DefinitionUnlockedCondition"/>
        /// whose _target field points at <paramref name="source"/>.
        /// </summary>
        private static DefinitionUnlockedCondition FindExistingConditionAsset(BaseDefinition source) {
            string[] guids = AssetDatabase.FindAssets("t:DefinitionUnlockedCondition");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var duc = AssetDatabase.LoadAssetAtPath<DefinitionUnlockedCondition>(path);
                if (duc == null) continue;
                var so = new SerializedObject(duc);
                var t = so.FindProperty("_target").objectReferenceValue as BaseDefinition;
                if (t == source) return duc;
            }
            return null;
        }

        private void AddConditionToDefinition(UnlockCondition condition, BaseDefinition target) {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null) {
                Debug.LogWarning($"[UnlockGraph] {target.name} has no _prerequisites field.");
                return;
            }

            Undo.RecordObject(target, "Add prerequisite");
            prereqs.InsertArrayElementAtIndex(prereqs.arraySize);
            prereqs.GetArrayElementAtIndex(prereqs.arraySize - 1)
                .objectReferenceValue = condition;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void RemoveDependencyEdge(BaseDefinition source, BaseDefinition target) {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null) return;

            for (int i = prereqs.arraySize - 1; i >= 0; i--) {
                var element = prereqs.GetArrayElementAtIndex(i);
                var condObj = element.objectReferenceValue as DefinitionUnlockedCondition;
                if (condObj == null) continue;

                var condSO = new SerializedObject(condObj);
                var condTarget = condSO.FindProperty("_target").objectReferenceValue as BaseDefinition;
                if (condTarget == source) {
                    Undo.RecordObject(target, "Remove prerequisite");
                    prereqs.DeleteArrayElementAtIndex(i);
                    // DeleteArrayElementAtIndex sets to null first; call again to actually shrink
                    if (i < prereqs.arraySize &&
                        prereqs.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        prereqs.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);

                    // Delete the condition asset
                    string condPath = AssetDatabase.GetAssetPath(condObj);
                    if (!string.IsNullOrEmpty(condPath))
                        AssetDatabase.DeleteAsset(condPath);
                    break;
                }
            }

            AssetDatabase.SaveAssets();
        }

        private void RemoveConditionFromDefinition(UnlockCondition condition, BaseDefinition target) {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null) return;

            for (int i = prereqs.arraySize - 1; i >= 0; i--) {
                if (prereqs.GetArrayElementAtIndex(i).objectReferenceValue == condition) {
                    Undo.RecordObject(target, "Remove condition prerequisite");
                    prereqs.DeleteArrayElementAtIndex(i);
                    if (i < prereqs.arraySize &&
                        prereqs.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        prereqs.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    break;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Asset creation from context menu
        // ════════════════════════════════════════════════════════════════

        private void CreateDefinitionAsset(Type type, Vector2 graphPos) {
            string assetName = type.Name;
            var nameInput = EditorInputDialog.Show("Create Definition",
                "Asset name:", assetName);
            if (string.IsNullOrWhiteSpace(nameInput)) return;

            var instance = ScriptableObject.CreateInstance(type);
            instance.name = nameInput;

            EnsureFolderExists(DefinitionsPath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{DefinitionsPath}/{nameInput}.asset");

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();

            // Refresh all registries so the new asset is picked up
            RefreshAllRegistries();

            // Add node at mouse position
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (instance is BaseDefinition baseDef) {
                var node = AddDefinitionNode(baseDef, guid, graphPos);

                if (_layoutData != null) {
                    _layoutData.SetNodePosition(guid, graphPos);
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            OnGraphChanged?.Invoke();
        }

        private void CreateConditionAsset(Type type, Vector2 graphPos) {
            string assetName = type.Name;
            var nameInput = EditorInputDialog.Show("Create Condition",
                "Asset name:", assetName);
            if (string.IsNullOrWhiteSpace(nameInput)) return;

            var instance = ScriptableObject.CreateInstance(type) as UnlockCondition;
            if (instance == null) return;
            instance.name = nameInput;

            EnsureFolderExists(ConditionsPath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{ConditionsPath}/{nameInput}.asset");

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            var node = AddConditionNode(instance, guid, graphPos);

            if (_layoutData != null) {
                _layoutData.SetNodePosition(node.NodeId, graphPos);
                EditorUtility.SetDirty(_layoutData);
            }

            OnGraphChanged?.Invoke();
        }

        // ── Search-window variants (called by NodeSearchProvider) ────

        internal void CreateDefinitionAssetFromSearch(Type type, Vector2 graphPos) {
            string assetName = type.Name;
            var nameInput = EditorInputDialog.Show("Create Definition",
                "Asset name:", assetName);
            if (string.IsNullOrWhiteSpace(nameInput)) return;

            var instance = ScriptableObject.CreateInstance(type);
            instance.name = nameInput;

            EnsureFolderExists(DefinitionsPath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{DefinitionsPath}/{nameInput}.asset");

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            RefreshAllRegistries();

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (instance is BaseDefinition baseDef) {
                var node = AddDefinitionNode(baseDef, guid, graphPos);
                CompletePendingConnection(node);
            }
            OnGraphChanged?.Invoke();
        }

        internal void CreateConditionAssetFromSearch(Type type, Vector2 graphPos) {
            string assetName = type.Name;
            var nameInput = EditorInputDialog.Show("Create Condition",
                "Asset name:", assetName);
            if (string.IsNullOrWhiteSpace(nameInput)) return;

            var instance = ScriptableObject.CreateInstance(type) as UnlockCondition;
            if (instance == null) return;
            instance.name = nameInput;

            EnsureFolderExists(ConditionsPath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{ConditionsPath}/{nameInput}.asset");

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            var node = AddConditionNode(instance, guid, graphPos);
            CompletePendingConnection(node);
            OnGraphChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════
        // Group management
        // ════════════════════════════════════════════════════════════════

        public void GroupSelectedNodes() {
            var selectedNodes = selection.OfType<Node>()
                .Where(n => n is DefinitionNode or ConditionNode)
                .ToList();

            if (selectedNodes.Count == 0) return;

            string groupName = EditorInputDialog.Show("Create Group",
                "Group name:", "New Group");
            if (string.IsNullOrWhiteSpace(groupName)) return;

            Color defaultColor = new Color(0.18f, 0.36f, 0.53f, 0.8f);
            CreateGroupFromNodes(groupName, defaultColor, selectedNodes);
        }

        public void AddSelectedToGroup(string groupId) {
            if (!_groups.TryGetValue(groupId, out var group)) return;

            foreach (var sel in selection.OfType<Node>()) {
                if (sel is DefinitionNode or ConditionNode)
                    group.AddElement(sel);
            }

            PersistGroupMembers(group);
        }

        public UnlockGraphGroup CreateGroupFromNodes(string name, Color color,
            List<Node> members, string existingGroupId = null) {
            UnlockGraphLayoutData.GroupLayoutEntry layoutEntry = null;

            if (existingGroupId != null && _layoutData != null)
                layoutEntry = _layoutData.GetGroupEntry(existingGroupId);

            if (layoutEntry == null && _layoutData != null) {
                Undo.RecordObject(_layoutData, "Create group");
                layoutEntry = _layoutData.CreateGroup(name, color);
                EditorUtility.SetDirty(_layoutData);
            }

            string gid = layoutEntry?.groupId ?? GUID.Generate().ToString();
            var group = new UnlockGraphGroup(gid, name, color);
            group.OnMinimizeRequested += CollapseGroup;
            group.OnColorChanged += OnGroupColorChanged;

            AddElement(group);
            _groups[gid] = group;

            foreach (var node in members)
                group.AddElement(node);

            PersistGroupMembers(group);
            return group;
        }

        private void PersistGroupMembers(UnlockGraphGroup group) {
            if (_layoutData == null) return;
            var entry = _layoutData.GetGroupEntry(group.GroupId);
            if (entry == null) return;

            Undo.RecordObject(_layoutData, "Update group members");
            entry.memberGuids = group.GetMemberGuids();
            entry.rect = group.GetPosition();
            EditorUtility.SetDirty(_layoutData);
        }

        private void OnGroupColorChanged(UnlockGraphGroup group, Color newColor) {
            if (_layoutData == null) return;
            var entry = _layoutData.GetGroupEntry(group.GroupId);
            if (entry == null) return;

            Undo.RecordObject(_layoutData, "Change group color");
            entry.color = newColor;
            EditorUtility.SetDirty(_layoutData);
        }

        private void OnDefinitionTypeColorChanged(DefinitionNode sourceNode, Color newColor) {
            if (_layoutData == null) return;

            string typeName = sourceNode.Definition.GetType().Name;

            Undo.RecordObject(_layoutData, "Change definition type color");
            _layoutData.SetDefinitionTypeColor(typeName, newColor);
            EditorUtility.SetDirty(_layoutData);

            // Propagate to all nodes of the same definition type
            foreach (var kvp in _definitionNodes) {
                var node = kvp.Value;
                if (node != sourceNode && node.Definition.GetType().Name == typeName)
                    node.SetTypeColor(newColor);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Group collapse / expand
        // ════════════════════════════════════════════════════════════════

        public void CollapseGroup(UnlockGraphGroup group) {
            var memberGuids = group.GetMemberGuids();
            if (memberGuids.Count == 0) return;

            // Persist collapsed state
            if (_layoutData != null) {
                var entry = _layoutData.GetGroupEntry(group.GroupId);
                if (entry != null) {
                    Undo.RecordObject(_layoutData, "Collapse group");
                    entry.collapsed = true;
                    entry.memberGuids = memberGuids;
                    entry.rect = group.GetPosition();
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            var groupRect = group.GetPosition();
            var centre = groupRect.center;
            var memberSet = new HashSet<string>(memberGuids);

            // Snapshot ALL edges touching member nodes, categorised as:
            //   boundary-in  (external → member)
            //   boundary-out (member → external)
            //   internal     (member → member)
            // Also save the full topology for restoring on expand.
            var boundaryInPorts = new List<Port>();   // external output ports
            var boundaryOutPorts = new List<Port>();   // external input ports
            var savedEdges = new List<(string outGuid, string inGuid)>();
            var edgesToRemove = new List<Edge>();

            foreach (var edge in edges.ToList()) {
                string outGuid = GetNodeGuid(edge.output?.node);
                string inGuid = GetNodeGuid(edge.input?.node);
                bool outInGroup = outGuid != null && memberSet.Contains(outGuid);
                bool inInGroup = inGuid != null && memberSet.Contains(inGuid);

                if (!outInGroup && !inInGroup) continue; // neither endpoint in group

                edgesToRemove.Add(edge);

                // Save topology for expand
                if (outGuid != null && inGuid != null)
                    savedEdges.Add((outGuid, inGuid));

                // Track external ports for SubGraphNode rerouting
                if (!outInGroup && inInGroup)
                    boundaryInPorts.Add(edge.output);
                else if (outInGroup && !inInGroup)
                    boundaryOutPorts.Add(edge.input);
            }

            _suppressChanges = true;
            try {
                // Remove group element
                RemoveElement(group);
                _groups.Remove(group.GroupId);

                // Remove all edges touching members
                foreach (var e in edgesToRemove)
                    RemoveElement(e);

                // Remove member nodes from the visual graph
                // (keep references in _definitionNodes/_conditionNodes)
                foreach (var guid in memberGuids) {
                    if (_definitionNodes.TryGetValue(guid, out var dn))
                        RemoveElement(dn);
                    else if (_conditionNodes.TryGetValue(guid, out var cn))
                        RemoveElement(cn);
                }

                // Create SubGraphNode
                var sgNode = new SubGraphNode(group.GroupId, group.title,
                    group.GroupColor, memberGuids);
                sgNode.SavedEdges = savedEdges;
                sgNode.SetPosition(new Rect(centre, Vector2.zero));
                AddElement(sgNode);
                _subGraphNodes[group.GroupId] = sgNode;

                // Reroute boundary edges to the sub-graph node
                foreach (var extPort in boundaryInPorts)
                    AddEdge(extPort, sgNode.InputPort);
                foreach (var extPort in boundaryOutPorts)
                    AddEdge(sgNode.OutputPort, extPort);
            }
            finally {
                _suppressChanges = false;
            }

            // Double-click to expand
            _subGraphNodes[group.GroupId].RegisterCallback<MouseDownEvent>(evt => {
                if (evt.clickCount == 2)
                    ExpandGroup(group.GroupId);
            });

            OnGraphChanged?.Invoke();
        }

        public void ExpandGroup(string groupId) {
            if (!_subGraphNodes.TryGetValue(groupId, out var sgNode)) return;

            var layoutEntry = _layoutData?.GetGroupEntry(groupId);
            if (layoutEntry == null) return;

            // Save the edge topology and member guids before removing
            var savedEdges = sgNode.SavedEdges;
            var memberGuids = new List<string>(sgNode.HiddenMemberGuids);

            _suppressChanges = true;
            try {
                // Remove sub-graph node and its edges
                var sgEdges = edges.ToList()
                    .Where(e => e.input?.node == sgNode || e.output?.node == sgNode)
                    .ToList();
                foreach (var e in sgEdges)
                    RemoveElement(e);
                RemoveElement(sgNode);
                _subGraphNodes.Remove(groupId);

                // Re-add member nodes to the graph
                foreach (var guid in memberGuids) {
                    if (_definitionNodes.TryGetValue(guid, out var dn))
                        AddElement(dn);
                    else if (_conditionNodes.TryGetValue(guid, out var cn))
                        AddElement(cn);
                }

                // Restore saved edges
                foreach (var (outGuid, inGuid) in savedEdges) {
                    var outPort = GetOutputPortByGuid(outGuid);
                    var inPort = GetInputPortByGuid(inGuid);
                    if (outPort != null && inPort != null)
                        AddEdge(outPort, inPort);
                }
            }
            finally {
                _suppressChanges = false;
            }

            // Persist expanded state
            Undo.RecordObject(_layoutData, "Expand group");
            layoutEntry.collapsed = false;
            EditorUtility.SetDirty(_layoutData);

            // Recreate the group visual
            var memberNodes = new List<Node>();
            foreach (var guid in memberGuids) {
                if (_definitionNodes.TryGetValue(guid, out var dn))
                    memberNodes.Add(dn);
                else if (_conditionNodes.TryGetValue(guid, out var cn))
                    memberNodes.Add(cn);
            }

            CreateGroupFromNodes(layoutEntry.groupName, layoutEntry.color,
                memberNodes, groupId);

            OnGraphChanged?.Invoke();
        }

        private Port GetOutputPortByGuid(string guid) {
            if (_definitionNodes.TryGetValue(guid, out var dn))
                return dn.OutputPort;
            if (_conditionNodes.TryGetValue(guid, out var cn))
                return cn.OutputPort;
            return null;
        }

        private Port GetInputPortByGuid(string guid) {
            if (_definitionNodes.TryGetValue(guid, out var dn))
                return dn.InputPort;
            // ConditionNodes have no input port
            return null;
        }

        private static string GetNodeGuid(Node node) {
            if (node is DefinitionNode dn) return dn.AssetGuid;
            if (node is ConditionNode cn) return cn.NodeId;
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        // Public API for the builder
        // ════════════════════════════════════════════════════════════════

        public void SetLayoutData(UnlockGraphLayoutData data) => _layoutData = data;

        private EdgeDropListener _edgeDropListener;
        private EdgeDropListener GetEdgeDropListener() {
            _edgeDropListener ??= new EdgeDropListener(this);
            return _edgeDropListener;
        }

        private void InstallEdgeConnector(Port port) {
            port.AddManipulator(new EdgeConnector<Edge>(GetEdgeDropListener()));
        }

        public DefinitionNode AddDefinitionNode(BaseDefinition def, string guid, Vector2 pos) {
            Color? typeColor = null;
            if (_layoutData != null && _layoutData.HasDefinitionTypeColor(def.GetType().Name))
                typeColor = _layoutData.GetDefinitionTypeColor(def.GetType().Name);

            var node = new DefinitionNode(def, guid, typeColor);
            node.SetPosition(new Rect(pos, Vector2.zero));
            InstallEdgeConnector(node.InputPort);
            InstallEdgeConnector(node.OutputPort);
            node.OnTypeColorChanged += OnDefinitionTypeColorChanged;
            AddElement(node);
            _definitionNodes[guid] = node;
            return node;
        }

        public ConditionNode AddConditionNode(UnlockCondition cond, string assetGuid, Vector2 pos, string nodeId = null) {
            if (string.IsNullOrEmpty(nodeId))
                nodeId = GenerateConditionNodeId(assetGuid);
            var node = new ConditionNode(cond, assetGuid, nodeId);
            node.SetPosition(new Rect(pos, Vector2.zero));
            InstallEdgeConnector(node.OutputPort);
            AddElement(node);
            _conditionNodes[nodeId] = node;
            return node;
        }

        private string GenerateConditionNodeId(string assetGuid) {
            if (!_conditionNodes.ContainsKey(assetGuid))
                return assetGuid;
            int counter = 1;
            while (_conditionNodes.ContainsKey($"{assetGuid}__{counter}"))
                counter++;
            return $"{assetGuid}__{counter}";
        }

        public Edge AddEdge(Port output, Port input) {
            var edge = new Edge { output = output, input = input };
            edge.output.Connect(edge);
            edge.input.Connect(edge);
            AddElement(edge);
            return edge;
        }

        public DefinitionNode FindDefinitionNode(string guid) {
            _definitionNodes.TryGetValue(guid, out var node);
            return node;
        }

        public ConditionNode FindConditionNode(string assetGuid) {
            // Fast path: first instance uses assetGuid as NodeId
            if (_conditionNodes.TryGetValue(assetGuid, out var node))
                return node;
            // Slower path: search by AssetGuid (for duplicate instances)
            foreach (var cn in _conditionNodes.Values)
                if (cn.AssetGuid == assetGuid) return cn;
            return null;
        }

        public ConditionNode FindConditionNodeById(string nodeId) {
            _conditionNodes.TryGetValue(nodeId, out var node);
            return node;
        }

        public List<ConditionNode> FindAllConditionNodes(string assetGuid) {
            return _conditionNodes.Values.Where(cn => cn.AssetGuid == assetGuid).ToList();
        }

        public IReadOnlyDictionary<string, UnlockGraphGroup> Groups => _groups;

        // ════════════════════════════════════════════════════════════════
        // Select underlying ScriptableObjects
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves the current graph selection to the underlying
        /// <see cref="UnityEngine.Object"/>s (definitions, conditions,
        /// edge conditions, layout data for groups).
        /// </summary>
        public List<UnityEngine.Object> GetSelectedObjects() {
            var objects = new List<UnityEngine.Object>();
            var seen = new HashSet<int>(); // instance IDs to deduplicate

            foreach (var sel in selection) {
                switch (sel) {
                    case DefinitionNode dn:
                        if (dn.Definition != null && seen.Add(dn.Definition.GetInstanceID()))
                            objects.Add(dn.Definition);
                        break;

                    case ConditionNode cn:
                        if (cn.Condition != null && seen.Add(cn.Condition.GetInstanceID()))
                            objects.Add(cn.Condition);
                        break;

                    case SubGraphNode sgn:
                        if (_layoutData != null && seen.Add(_layoutData.GetInstanceID()))
                            objects.Add(_layoutData);
                        break;

                    case UnlockGraphGroup grp:
                        if (_layoutData != null && seen.Add(_layoutData.GetInstanceID()))
                            objects.Add(_layoutData);
                        break;

                    case Edge edge:
                        ResolveEdgeObject(edge, objects, seen);
                        break;
                }
            }
            return objects;
        }

        private void ResolveEdgeObject(Edge edge, List<UnityEngine.Object> objects, HashSet<int> seen) {
            // Edge between two DefinitionNodes → find the DefinitionUnlockedCondition SO
            if (edge.output?.node is DefinitionNode srcDef &&
                edge.input?.node is DefinitionNode tgtDef) {
                var cond = FindConditionSO(srcDef.Definition, tgtDef.Definition);
                if (cond != null && seen.Add(cond.GetInstanceID()))
                    objects.Add(cond);
                return;
            }
            // Edge from ConditionNode → the condition SO itself
            if (edge.output?.node is ConditionNode cn) {
                if (cn.Condition != null && seen.Add(cn.Condition.GetInstanceID()))
                    objects.Add(cn.Condition);
            }
        }

        /// <summary>
        /// Finds the <see cref="DefinitionUnlockedCondition"/> in
        /// <paramref name="target"/>'s prerequisites that references
        /// <paramref name="source"/>.
        /// </summary>
        private static DefinitionUnlockedCondition FindConditionSO(
            BaseDefinition source, BaseDefinition target) {
            if (target is not IHasPrerequisites hasPrereqs) return null;
            var prereqs = hasPrereqs.Prerequisites;
            if (prereqs == null) return null;

            foreach (var cond in prereqs) {
                if (cond is DefinitionUnlockedCondition duc) {
                    var so = new SerializedObject(duc);
                    var condTarget = so.FindProperty("_target").objectReferenceValue as BaseDefinition;
                    if (condTarget == source)
                        return duc;
                }
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        // Clear
        // ════════════════════════════════════════════════════════════════

        public void ClearGraph() {
            _suppressChanges = true;
            try {
                _definitionNodes.Clear();
                _conditionNodes.Clear();
                _groups.Clear();
                _subGraphNodes.Clear();
                DeleteElements(graphElements.ToList());
            }
            finally {
                _suppressChanges = false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // SPACE key → node search window
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the node search window at the given screen position.
        /// Called by the host window's IMGUI keyboard handler.
        /// </summary>
        internal void OpenSearchWindow(Vector2 screenPos) {
            _pendingDropPort = null;
            SearchWindow.Open(
                new SearchWindowContext(screenPos), _searchProvider);
        }

        /// <summary>
        /// Called by edge connector listener when the user drops an edge
        /// on empty space. Stores the originating port and opens the
        /// search window so the created/picked node auto-connects.
        /// </summary>
        public void OnDroppedEdge(Edge edge, Vector2 screenPos) {
            // Determine which port was the origin (the one that has a node)
            _pendingDropPort = edge.output?.node != null ? edge.output :
                               edge.input?.node != null ? edge.input : null;
            if (_pendingDropPort == null) return;

            SearchWindow.Open(
                new SearchWindowContext(screenPos), _searchProvider);
        }

        /// <summary>
        /// Tries to auto-connect the pending drop port to a newly placed node.
        /// </summary>
        internal void CompletePendingConnection(Node newNode) {
            if (_pendingDropPort == null) return;

            Port targetPort = null;
            if (_pendingDropPort.direction == Direction.Output) {
                // We dragged from an output → connect to the new node's input
                if (newNode is DefinitionNode dn)
                    targetPort = dn.InputPort;
            }
            else {
                // Dragged from an input → connect to the new node's output
                if (newNode is DefinitionNode dn)
                    targetPort = dn.OutputPort;
                else if (newNode is ConditionNode cn)
                    targetPort = cn.OutputPort;
            }

            if (targetPort != null) {
                Edge newEdge;
                if (_pendingDropPort.direction == Direction.Output)
                    newEdge = AddEdge(_pendingDropPort, targetPort);
                else
                    newEdge = AddEdge(targetPort, _pendingDropPort);

                // Trigger the same SO creation as a manual drag
                HandleEdgeCreated(newEdge);
            }

            _pendingDropPort = null;
        }

        // ════════════════════════════════════════════════════════════════
        // Undo
        // ════════════════════════════════════════════════════════════════

        private void OnUndoRedo() {
            // The host window should rebuild the graph
            OnGraphChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        private static List<Type> FindConcreteTypes<TBase>(Type requiredInterface = null) {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types) {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(TBase).IsAssignableFrom(t)) continue;
                    if (requiredInterface != null && !requiredInterface.IsAssignableFrom(t)) continue;
                    result.Add(t);
                }
            }
            return result.OrderBy(t => t.Name).ToList();
        }

        private static void RefreshAllRegistries() {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            var baseRegType = typeof(BaseRegistry<>);
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null) continue;

                var type = so.GetType();
                if (!IsSubclassOfRawGeneric(type, baseRegType)) continue;

                var mi = type.GetMethod("RefreshDefinitions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                mi?.Invoke(so, null);
            }
            AssetDatabase.SaveAssets();
        }

        private static bool IsSubclassOfRawGeneric(Type toCheck, Type generic) {
            while (toCheck != null && toCheck != typeof(object)) {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (cur == generic) return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        internal static void EnsureFolderExists(string folderPath) {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Replace('\\', '/').Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++) {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string FindUssPath(string fileName) {
            string[] guids = AssetDatabase.FindAssets($"{fileName} t:StyleSheet");
            if (guids.Length > 0)
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Simple input dialog — used for naming new assets and groups
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class EditorInputDialog : EditorWindow {
        private string _result;
        private string _message;
        private string _defaultValue;
        private bool _confirmed;
        private bool _focused;

        public static string Show(string title, string message, string defaultValue) {
            var dlg = CreateInstance<EditorInputDialog>();
            dlg.titleContent = new GUIContent(title);
            dlg._message = message;
            dlg._defaultValue = defaultValue;
            dlg._result = defaultValue;
            dlg.minSize = new Vector2(320, 100);
            dlg.maxSize = new Vector2(320, 100);
            dlg.ShowModal();
            return dlg._confirmed ? dlg._result : null;
        }

        private void OnGUI() {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_message);
            GUI.SetNextControlName("InputField");
            _result = EditorGUILayout.TextField(_result);

            if (!_focused) {
                EditorGUI.FocusTextInControl("InputField");
                _focused = true;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Return)) {
                _confirmed = true;
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Escape)) {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Node Search Provider — used by SPACE key and edge-drop menus
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class NodeSearchProvider : ScriptableObject, ISearchWindowProvider {
        public UnlockGraphView GraphView;

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) {
            var tree = new List<SearchTreeEntry> {
                new SearchTreeGroupEntry(new GUIContent("Add Node"), 0)
            };

            // ── Existing Definitions ────────────────────────────────
            tree.Add(new SearchTreeGroupEntry(new GUIContent("Existing Definitions"), 1));
            var defGuids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in defGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not BaseDefinition bd || so is not IUnlockable) continue;
                // Skip if already in graph
                if (GraphView.FindDefinitionNode(AssetDatabase.AssetPathToGUID(path)) != null)
                    continue;
                tree.Add(new SearchTreeEntry(new GUIContent($"{bd.name}  ({bd.GetType().Name})")) {
                    level = 2,
                    userData = new ExistingDefEntry { Def = bd, Guid = guid }
                });
            }

            // ── Existing Conditions (non-DefinitionUnlockedCondition) ─
            // Duplicates are allowed — the same condition can appear as
            // multiple visual nodes on the graph.
            tree.Add(new SearchTreeGroupEntry(new GUIContent("Existing Conditions"), 1));
            var condGuids = AssetDatabase.FindAssets("t:UnlockCondition");
            foreach (string guid in condGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cond = AssetDatabase.LoadAssetAtPath<UnlockCondition>(path);
                if (cond == null || cond is DefinitionUnlockedCondition) continue;
                bool alreadyOnGraph = GraphView.FindConditionNode(
                    AssetDatabase.AssetPathToGUID(path)) != null;
                string label = alreadyOnGraph
                    ? $"{cond.name}  ({cond.GetType().Name})  [+dup]"
                    : $"{cond.name}  ({cond.GetType().Name})";
                tree.Add(new SearchTreeEntry(new GUIContent(label)) {
                    level = 2,
                    userData = new ExistingCondEntry { Condition = cond, Guid = guid }
                });
            }

            // ── Create New ──────────────────────────────────────────
            tree.Add(new SearchTreeGroupEntry(new GUIContent("Create New"), 1));

            var defTypes = FindConcreteTypes<BaseDefinition>(typeof(IUnlockable));
            foreach (var type in defTypes) {
                tree.Add(new SearchTreeEntry(new GUIContent($"Definition: {type.Name}")) {
                    level = 2,
                    userData = new CreateDefEntry { Type = type }
                });
            }

            var condTypes = FindConcreteTypes<UnlockCondition>();
            foreach (var type in condTypes) {
                tree.Add(new SearchTreeEntry(new GUIContent($"Condition: {type.Name}")) {
                    level = 2,
                    userData = new CreateCondEntry { Type = type }
                });
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context) {
            var screenPos = context.screenMousePosition;

            // Use the cached host window — EditorWindow.focusedWindow is the
            // SearchWindow popup at this point, which breaks coordinate math.
            var hostWindow = GraphView.HostWindow;
            Vector2 graphPos = Vector2.zero;

            if (hostWindow != null) {
                // screenPos → window-local (panel) coordinates
                var panelPos = screenPos - hostWindow.position.position;
                // panel coordinates → graph content coordinates
                graphPos = GraphView.contentViewContainer.WorldToLocal(panelPos);
            }

            switch (entry.userData) {
                case ExistingDefEntry ed:
                    PlaceExistingDefinition(ed, graphPos);
                    return true;
                case ExistingCondEntry ec:
                    PlaceExistingCondition(ec, graphPos);
                    return true;
                case CreateDefEntry cd:
                    GraphView.CreateDefinitionAssetFromSearch(cd.Type, graphPos);
                    return true;
                case CreateCondEntry cc:
                    GraphView.CreateConditionAssetFromSearch(cc.Type, graphPos);
                    return true;
            }
            return false;
        }

        private void PlaceExistingDefinition(ExistingDefEntry ed, Vector2 pos) {
            var node = GraphView.AddDefinitionNode(ed.Def, ed.Guid, pos);
            if (GraphView._layoutData != null) {
                GraphView._layoutData.SetNodePosition(ed.Guid, pos);
                EditorUtility.SetDirty(GraphView._layoutData);
            }
            GraphView.CompletePendingConnection(node);
            GraphView.NotifyGraphChanged();
        }

        private void PlaceExistingCondition(ExistingCondEntry ec, Vector2 pos) {
            var node = GraphView.AddConditionNode(ec.Condition, ec.Guid, pos);
            if (GraphView._layoutData != null) {
                GraphView._layoutData.SetNodePosition(node.NodeId, pos);
                EditorUtility.SetDirty(GraphView._layoutData);
            }
            GraphView.CompletePendingConnection(node);
            GraphView.NotifyGraphChanged();
        }

        // ── Helper types ─────────────────────────────────────────────
        private class ExistingDefEntry { public BaseDefinition Def; public string Guid; }
        private class ExistingCondEntry { public UnlockCondition Condition; public string Guid; }
        private class CreateDefEntry { public Type Type; }
        private class CreateCondEntry { public Type Type; }

        private static List<Type> FindConcreteTypes<TBase>(Type requiredInterface = null) {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types) {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(TBase).IsAssignableFrom(t)) continue;
                    if (requiredInterface != null && !requiredInterface.IsAssignableFrom(t)) continue;
                    result.Add(t);
                }
            }
            return result.OrderBy(t => t.Name).ToList();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Edge Connector Listener — handles drag-to-empty
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class EdgeDropListener : IEdgeConnectorListener {
        private readonly UnlockGraphView _graphView;
        private readonly GraphViewChange _change;

        public EdgeDropListener(UnlockGraphView graphView) {
            _graphView = graphView;
            _change.edgesToCreate = new List<Edge>();
        }

        public void OnDropOutsidePort(Edge edge, Vector2 position) {
            // position is in window coordinates; convert to screen space
            var hostWindow = _graphView.HostWindow;
            Vector2 screenPos;
            if (Event.current != null) {
                screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            }
            else if (hostWindow != null) {
                screenPos = position + hostWindow.position.position;
            }
            else {
                screenPos = GUIUtility.GUIToScreenPoint(position);
            }
            _graphView.OnDroppedEdge(edge, screenPos);
        }

        public void OnDrop(GraphView graphView, Edge edge) {
            _change.edgesToCreate.Clear();
            _change.edgesToCreate.Add(edge);
            graphView.graphViewChanged?.Invoke(_change);

            edge.output.Connect(edge);
            edge.input.Connect(edge);
            graphView.AddElement(edge);
        }
    }
}
