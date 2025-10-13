using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ProxyCore {
    [System.Serializable]
    public class SettableNameList {
        [SerializeField]
        public List<string> list;
        [SerializeField]
        public List<int> indexes;

        public bool Equals(SettableNameList other) {
            return indexes.SequenceEqual(other.indexes);
        }
        public override string ToString() {
            string output = "";
            foreach (string item in list) {
                output += item + ", ";
            }
            return output;
        }
    }
}
