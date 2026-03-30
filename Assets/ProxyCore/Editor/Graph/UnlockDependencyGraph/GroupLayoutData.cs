using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Serializable data class representing a single sub-graph group
    /// within the unlock dependency graph.  Stored inside
    /// <see cref="UnlockGraphLayoutData.GroupLayoutEntry"/>.
    /// This file exists as a convenient alias / partial-class hook;
    /// the canonical data lives in UnlockGraphLayoutData.
    /// </summary>
    [Serializable]
    public class GroupLayoutData
    {
        public string groupId;
        public string groupName;
        public Color color = new Color(0.18f, 0.36f, 0.53f, 0.8f);
        public bool collapsed;
        public Rect rect;
        public List<string> memberGuids = new List<string>();

        public GroupLayoutData() { }

        public GroupLayoutData(UnlockGraphLayoutData.GroupLayoutEntry entry)
        {
            groupId = entry.groupId;
            groupName = entry.groupName;
            color = entry.color;
            collapsed = entry.collapsed;
            rect = entry.rect;
            memberGuids = new List<string>(entry.memberGuids);
        }

        public void ApplyTo(UnlockGraphLayoutData.GroupLayoutEntry entry)
        {
            entry.groupName = groupName;
            entry.color = color;
            entry.collapsed = collapsed;
            entry.rect = rect;
            entry.memberGuids = new List<string>(memberGuids);
        }
    }
}
