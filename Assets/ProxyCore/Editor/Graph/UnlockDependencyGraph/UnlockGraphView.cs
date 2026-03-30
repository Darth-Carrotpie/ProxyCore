using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph
{
    /// <summary>
    /// Core GraphView for the Unlock Dependency Graph.
    /// Supports zoom, pan, selection, minimap, context-menu creation
    /// of definitions / conditions / groups, and edge management that
    /// round-trips to <see cref="UnlockCondition"/> ScriptableObjects.
    /// </summary>
    public sealed class UnlockGraphView : GraphView
    {
        // ── Lookups ──────────────────────────────────────────────────────
        private readonly Dictionary<string, DefinitionNode> _definitionNodes = new();
        private readonly Dictionary<string, ConditionNode> _conditionNodes = new();
        private readonly Dictionary<string, UnlockGraphGroup> _groups = new();
        private readonly Dictionary<string, SubGraphNode> _subGraphNodes = new();

        // When true, the graphViewChanged callback ignores edge removals
        // (used during ClearGraph to avoid deleting actual SO data).
        private bool _suppressChanges;

        // ── Layout persistence ───────────────────────────────────────────
        private UnlockGraphLayoutData _layoutData;

        // ── Path settings (set by window) ────────────────────────────────
        public string DefinitionsPath { get; set; } = "Assets";
        public string ConditionsPath { get; set; } = "Assets";

        // ── Events for the host window ───────────────────────────────────
        public event Action OnGraphChanged;

        private MiniMap _miniMap;

        // ════════════════════════════════════════════════════════════════
        // Construction
        // ════════════════════════════════════════════════════════════════

        public UnlockGraphView()
        {
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

            // Register undo
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        ~UnlockGraphView()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        // ════════════════════════════════════════════════════════════════
        // Port compatibility
        // ════════════════════════════════════════════════════════════════

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            ports.ForEach(port =>
            {
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

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            var mousePos = contentViewContainer.WorldToLocal(evt.mousePosition);

            // ── Create Definition ────────────────────────────────────
            var defTypes = FindConcreteTypes<BaseDefinition>(typeof(IUnlockable));
            foreach (var type in defTypes)
            {
                evt.menu.AppendAction($"Create Definition/{type.Name}",
                    _ => CreateDefinitionAsset(type, mousePos));
            }

            // ── Create Condition ─────────────────────────────────────
            var condTypes = FindConcreteTypes<UnlockCondition>();
            foreach (var type in condTypes)
            {
                evt.menu.AppendAction($"Create Condition/{type.Name}",
                    _ => CreateConditionAsset(type, mousePos));
            }

            // ── Group actions ────────────────────────────────────────
            if (selection.OfType<ISelectable>().Any(s => s is DefinitionNode or ConditionNode))
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Group Selected", _ => GroupSelectedNodes());

                foreach (var kvp in _groups)
                {
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

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_suppressChanges) return change;

            // ── Edges created (user dragged a new connection) ────────
            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                    HandleEdgeCreated(edge);
            }

            // ── Elements removed ─────────────────────────────────────
            if (change.elementsToRemove != null)
            {
                foreach (var element in change.elementsToRemove)
                {
                    if (element is Edge edge)
                        HandleEdgeRemoved(edge);
                }
            }

            // ── Nodes moved — persist positions ──────────────────────
            if (change.movedElements != null && _layoutData != null)
            {
                bool dirty = false;
                foreach (var el in change.movedElements)
                {
                    if (el is DefinitionNode dn)
                    {
                        _layoutData.SetNodePosition(dn.AssetGuid,
                            dn.GetPosition().position);
                        dirty = true;
                    }
                    else if (el is ConditionNode cn)
                    {
                        _layoutData.SetNodePosition(cn.AssetGuid,
                            cn.GetPosition().position);
                        dirty = true;
                    }
                    else if (el is UnlockGraphGroup grp)
                    {
                        var entry = _layoutData.GetGroupEntry(grp.GroupId);
                        if (entry != null)
                        {
                            entry.rect = grp.GetPosition();
                            dirty = true;
                        }
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            OnGraphChanged?.Invoke();
            return change;
        }

        // ════════════════════════════════════════════════════════════════
        // Edge → SO synchronisation
        // ════════════════════════════════════════════════════════════════

        private void HandleEdgeCreated(Edge edge)
        {
            // Definition Output → Definition Input  ⇒  create DefinitionUnlockedCondition
            if (edge.output?.node is DefinitionNode source &&
                edge.input?.node is DefinitionNode target)
            {
                CreateDependencyEdge(source.Definition, target.Definition);
            }
            // ConditionNode Output → DefinitionNode Input  ⇒  add condition to prereqs
            else if (edge.output?.node is ConditionNode condNode &&
                     edge.input?.node is DefinitionNode defNode)
            {
                AddConditionToDefinition(condNode.Condition, defNode.Definition);
            }
        }

        private void HandleEdgeRemoved(Edge edge)
        {
            if (edge.output?.node is DefinitionNode source &&
                edge.input?.node is DefinitionNode target)
            {
                RemoveDependencyEdge(source.Definition, target.Definition);
            }
            else if (edge.output?.node is ConditionNode condNode &&
                     edge.input?.node is DefinitionNode defNode)
            {
                RemoveConditionFromDefinition(condNode.Condition, defNode.Definition);
            }
        }

        /// <summary>
        /// Creates a <see cref="DefinitionUnlockedCondition"/> SO wiring
        /// <paramref name="source"/> as a prerequisite of <paramref name="target"/>.
        /// </summary>
        private void CreateDependencyEdge(BaseDefinition source, BaseDefinition target)
        {
            if (!(target is IHasPrerequisites))
            {
                Debug.LogWarning($"[UnlockGraph] {target.name} does not implement IHasPrerequisites.");
                return;
            }

            // Create the condition SO at the configured conditions path
            var condition = ScriptableObject.CreateInstance<DefinitionUnlockedCondition>();
            condition.name = $"{source.name}_Unlocked";

            var condSO = new SerializedObject(condition);
            condSO.FindProperty("_target").objectReferenceValue = source;
            condSO.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolderExists(ConditionsPath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{ConditionsPath}/{condition.name}.asset");
            AssetDatabase.CreateAsset(condition, assetPath);

            // Add condition to target's prerequisites
            AddConditionToDefinition(condition, target);

            AssetDatabase.SaveAssets();
        }

        private void AddConditionToDefinition(UnlockCondition condition, BaseDefinition target)
        {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null)
            {
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

        private void RemoveDependencyEdge(BaseDefinition source, BaseDefinition target)
        {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null) return;

            for (int i = prereqs.arraySize - 1; i >= 0; i--)
            {
                var element = prereqs.GetArrayElementAtIndex(i);
                var condObj = element.objectReferenceValue as DefinitionUnlockedCondition;
                if (condObj == null) continue;

                var condSO = new SerializedObject(condObj);
                var condTarget = condSO.FindProperty("_target").objectReferenceValue as BaseDefinition;
                if (condTarget == source)
                {
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

        private void RemoveConditionFromDefinition(UnlockCondition condition, BaseDefinition target)
        {
            var so = new SerializedObject(target);
            var prereqs = so.FindProperty("_prerequisites");
            if (prereqs == null) return;

            for (int i = prereqs.arraySize - 1; i >= 0; i--)
            {
                if (prereqs.GetArrayElementAtIndex(i).objectReferenceValue == condition)
                {
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

        private void CreateDefinitionAsset(Type type, Vector2 graphPos)
        {
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
            if (instance is BaseDefinition baseDef)
            {
                var node = new DefinitionNode(baseDef, guid);
                node.SetPosition(new Rect(graphPos, Vector2.zero));
                AddElement(node);
                _definitionNodes[guid] = node;

                if (_layoutData != null)
                {
                    _layoutData.SetNodePosition(guid, graphPos);
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            OnGraphChanged?.Invoke();
        }

        private void CreateConditionAsset(Type type, Vector2 graphPos)
        {
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
            var node = new ConditionNode(instance, guid);
            node.SetPosition(new Rect(graphPos, Vector2.zero));
            AddElement(node);
            _conditionNodes[guid] = node;

            if (_layoutData != null)
            {
                _layoutData.SetNodePosition(guid, graphPos);
                EditorUtility.SetDirty(_layoutData);
            }

            OnGraphChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════
        // Group management
        // ════════════════════════════════════════════════════════════════

        public void GroupSelectedNodes()
        {
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

        public void AddSelectedToGroup(string groupId)
        {
            if (!_groups.TryGetValue(groupId, out var group)) return;

            foreach (var sel in selection.OfType<Node>())
            {
                if (sel is DefinitionNode or ConditionNode)
                    group.AddElement(sel);
            }

            PersistGroupMembers(group);
        }

        public UnlockGraphGroup CreateGroupFromNodes(string name, Color color,
            List<Node> members, string existingGroupId = null)
        {
            UnlockGraphLayoutData.GroupLayoutEntry layoutEntry = null;

            if (existingGroupId != null && _layoutData != null)
                layoutEntry = _layoutData.GetGroupEntry(existingGroupId);

            if (layoutEntry == null && _layoutData != null)
            {
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

        private void PersistGroupMembers(UnlockGraphGroup group)
        {
            if (_layoutData == null) return;
            var entry = _layoutData.GetGroupEntry(group.GroupId);
            if (entry == null) return;

            Undo.RecordObject(_layoutData, "Update group members");
            entry.memberGuids = group.GetMemberGuids();
            entry.rect = group.GetPosition();
            EditorUtility.SetDirty(_layoutData);
        }

        private void OnGroupColorChanged(UnlockGraphGroup group, Color newColor)
        {
            if (_layoutData == null) return;
            var entry = _layoutData.GetGroupEntry(group.GroupId);
            if (entry == null) return;

            Undo.RecordObject(_layoutData, "Change group color");
            entry.color = newColor;
            EditorUtility.SetDirty(_layoutData);
        }

        // ════════════════════════════════════════════════════════════════
        // Group collapse / expand
        // ════════════════════════════════════════════════════════════════

        public void CollapseGroup(UnlockGraphGroup group)
        {
            var memberGuids = group.GetMemberGuids();
            if (memberGuids.Count == 0) return;

            // Persist collapsed state
            if (_layoutData != null)
            {
                var entry = _layoutData.GetGroupEntry(group.GroupId);
                if (entry != null)
                {
                    Undo.RecordObject(_layoutData, "Collapse group");
                    entry.collapsed = true;
                    entry.memberGuids = memberGuids;
                    entry.rect = group.GetPosition();
                    EditorUtility.SetDirty(_layoutData);
                }
            }

            // Compute centre of the group
            var groupRect = group.GetPosition();
            var centre = groupRect.center;

            // Collect edges that cross the group boundary
            var inEdges = new List<Edge>();
            var outEdges = new List<Edge>();
            CollectCrossGroupEdges(memberGuids, inEdges, outEdges);

            // Also collect edges INSIDE the group (both endpoints are members)
            var internalEdges = new List<Edge>();
            var memberSet = new HashSet<string>(memberGuids);
            foreach (var edge in edges.ToList())
            {
                string outGuid = GetNodeGuid(edge.output?.node);
                string inGuid = GetNodeGuid(edge.input?.node);
                if (outGuid != null && inGuid != null &&
                    memberSet.Contains(outGuid) && memberSet.Contains(inGuid))
                    internalEdges.Add(edge);
            }

            // Remove the group visual FIRST — Unity's Group removal
            // can reset contained element visibility.
            _suppressChanges = true;
            RemoveElement(group);
            _suppressChanges = false;
            _groups.Remove(group.GroupId);

            // Now hide member nodes (after group removal)
            foreach (var guid in memberGuids)
            {
                if (_definitionNodes.TryGetValue(guid, out var dn))
                {
                    dn.visible = false;
                    dn.style.display = DisplayStyle.None;
                }
                else if (_conditionNodes.TryGetValue(guid, out var cn))
                {
                    cn.visible = false;
                    cn.style.display = DisplayStyle.None;
                }
            }

            // Hide boundary and internal edges
            foreach (var edge in inEdges.Concat(outEdges).Concat(internalEdges))
            {
                edge.visible = false;
                edge.style.display = DisplayStyle.None;
            }

            // Create SubGraphNode
            var sgNode = new SubGraphNode(group.GroupId, group.title,
                group.GroupColor, memberGuids);
            sgNode.SetPosition(new Rect(centre, Vector2.zero));
            AddElement(sgNode);
            _subGraphNodes[group.GroupId] = sgNode;

            // Reconnect boundary edges to the sub-graph node
            foreach (var edge in inEdges)
            {
                var newEdge = new Edge { output = edge.output, input = sgNode.InputPort };
                AddElement(newEdge);
            }
            foreach (var edge in outEdges)
            {
                var newEdge = new Edge { output = sgNode.OutputPort, input = edge.input };
                AddElement(newEdge);
            }

            // Double-click to expand
            sgNode.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                    ExpandGroup(group.GroupId);
            });

            OnGraphChanged?.Invoke();
        }

        public void ExpandGroup(string groupId)
        {
            if (!_subGraphNodes.TryGetValue(groupId, out var sgNode)) return;

            var layoutEntry = _layoutData?.GetGroupEntry(groupId);
            if (layoutEntry == null) return;

            // Remove sub-graph node and its edges
            var edgesToRemove = edges.ToList()
                .Where(e => e.input?.node == sgNode || e.output?.node == sgNode)
                .ToList();
            foreach (var e in edgesToRemove)
                RemoveElement(e);
            RemoveElement(sgNode);
            _subGraphNodes.Remove(groupId);

            // Persist expanded state
            Undo.RecordObject(_layoutData, "Expand group");
            layoutEntry.collapsed = false;
            EditorUtility.SetDirty(_layoutData);

            // Show member nodes
            foreach (var guid in layoutEntry.memberGuids)
            {
                if (_definitionNodes.TryGetValue(guid, out var dn))
                {
                    dn.visible = true;
                    dn.style.display = DisplayStyle.Flex;
                }
                else if (_conditionNodes.TryGetValue(guid, out var cn))
                {
                    cn.visible = true;
                    cn.style.display = DisplayStyle.Flex;
                }
            }

            // Restore hidden edges
            foreach (var edge in edges.ToList())
            {
                if (!edge.visible)
                {
                    edge.visible = true;
                    edge.style.display = DisplayStyle.Flex;
                }
            }

            // Recreate the group visual
            var memberNodes = new List<Node>();
            foreach (var guid in layoutEntry.memberGuids)
            {
                if (_definitionNodes.TryGetValue(guid, out var dn))
                    memberNodes.Add(dn);
                else if (_conditionNodes.TryGetValue(guid, out var cn))
                    memberNodes.Add(cn);
            }

            CreateGroupFromNodes(layoutEntry.groupName, layoutEntry.color,
                memberNodes, groupId);

            OnGraphChanged?.Invoke();
        }

        private void CollectCrossGroupEdges(List<string> memberGuids,
            List<Edge> inEdges, List<Edge> outEdges)
        {
            var memberSet = new HashSet<string>(memberGuids);
            foreach (var edge in edges.ToList())
            {
                string outGuid = GetNodeGuid(edge.output?.node);
                string inGuid = GetNodeGuid(edge.input?.node);

                bool outInGroup = outGuid != null && memberSet.Contains(outGuid);
                bool inInGroup = inGuid != null && memberSet.Contains(inGuid);

                if (outInGroup && !inInGroup)
                    outEdges.Add(edge);
                else if (!outInGroup && inInGroup)
                    inEdges.Add(edge);
            }
        }

        private static string GetNodeGuid(Node node)
        {
            if (node is DefinitionNode dn) return dn.AssetGuid;
            if (node is ConditionNode cn) return cn.AssetGuid;
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        // Public API for the builder
        // ════════════════════════════════════════════════════════════════

        public void SetLayoutData(UnlockGraphLayoutData data) => _layoutData = data;

        public DefinitionNode AddDefinitionNode(BaseDefinition def, string guid, Vector2 pos)
        {
            var node = new DefinitionNode(def, guid);
            node.SetPosition(new Rect(pos, Vector2.zero));
            AddElement(node);
            _definitionNodes[guid] = node;
            return node;
        }

        public ConditionNode AddConditionNode(UnlockCondition cond, string guid, Vector2 pos)
        {
            var node = new ConditionNode(cond, guid);
            node.SetPosition(new Rect(pos, Vector2.zero));
            AddElement(node);
            _conditionNodes[guid] = node;
            return node;
        }

        public Edge AddEdge(Port output, Port input)
        {
            var edge = new Edge { output = output, input = input };
            edge.output.Connect(edge);
            edge.input.Connect(edge);
            AddElement(edge);
            return edge;
        }

        public DefinitionNode FindDefinitionNode(string guid)
        {
            _definitionNodes.TryGetValue(guid, out var node);
            return node;
        }

        public ConditionNode FindConditionNode(string guid)
        {
            _conditionNodes.TryGetValue(guid, out var node);
            return node;
        }

        public IReadOnlyDictionary<string, UnlockGraphGroup> Groups => _groups;

        // ════════════════════════════════════════════════════════════════
        // Clear
        // ════════════════════════════════════════════════════════════════

        public void ClearGraph()
        {
            _suppressChanges = true;
            try
            {
                _definitionNodes.Clear();
                _conditionNodes.Clear();
                _groups.Clear();
                _subGraphNodes.Clear();
                DeleteElements(graphElements.ToList());
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Undo
        // ════════════════════════════════════════════════════════════════

        private void OnUndoRedo()
        {
            // The host window should rebuild the graph
            OnGraphChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        private static List<Type> FindConcreteTypes<TBase>(Type requiredInterface = null)
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(TBase).IsAssignableFrom(t)) continue;
                    if (requiredInterface != null && !requiredInterface.IsAssignableFrom(t)) continue;
                    result.Add(t);
                }
            }
            return result.OrderBy(t => t.Name).ToList();
        }

        private static void RefreshAllRegistries()
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            var baseRegType = typeof(BaseRegistry<>);
            foreach (string guid in guids)
            {
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

        private static bool IsSubclassOfRawGeneric(Type toCheck, Type generic)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (cur == generic) return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        internal static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Replace('\\', '/').Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string FindUssPath(string fileName)
        {
            string[] guids = AssetDatabase.FindAssets($"{fileName} t:StyleSheet");
            if (guids.Length > 0)
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Simple input dialog — used for naming new assets and groups
    // ═══════════════════════════════════════════════════════════════════

    internal sealed class EditorInputDialog : EditorWindow
    {
        private string _result;
        private string _message;
        private string _defaultValue;
        private bool _confirmed;
        private bool _focused;

        public static string Show(string title, string message, string defaultValue)
        {
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

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_message);
            GUI.SetNextControlName("InputField");
            _result = EditorGUILayout.TextField(_result);

            if (!_focused)
            {
                EditorGUI.FocusTextInControl("InputField");
                _focused = true;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Return))
            {
                _confirmed = true;
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Escape))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
