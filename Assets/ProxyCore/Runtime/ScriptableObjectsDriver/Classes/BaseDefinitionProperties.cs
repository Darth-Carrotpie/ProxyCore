using UnityEngine;

namespace ProxyCore {
    public class BaseDefinitionProperties : ScriptableObject {
        [Header("Display")]
        [Tooltip("Display name shown in UI")]
        public string displayName;

        [Tooltip("Short name for compact UI")]
        public string shortName;

        [Tooltip("Icon representing this resource or modifier")]
        public Sprite icon;

        [TextArea]
        [Tooltip("Description of what this resource represents")]
        public string description;
    }
}
