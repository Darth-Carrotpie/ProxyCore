using UnityEngine;

namespace ScriptableWithID
{
    public class ScriptableObjectWithID : ScriptableObject
    {
        [field: SerializedID] [field: SerializeField]
        public int ID { get; set; }

        public bool IsValidID() =>
            ID != 0;

        public void SetIndex(int index) =>
            ID = index;
    }

    public class SerializedIDAttribute : PropertyAttribute
    {
    }
}
