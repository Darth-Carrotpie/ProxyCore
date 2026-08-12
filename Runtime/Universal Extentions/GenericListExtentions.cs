using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace ProxyCore
{
    public static class GenericListExtensions
    {
        //Remove a list from a list:
        public static void RemoveList<T>(this List<T> listToRemoveFrom, List<T> listToRemove)
        {
            foreach (T item in listToRemove)
            {
                listToRemoveFrom.Remove(item);
            }
        }
        //Clear all null values from the list
        public static void DropNa<T>(this List<T> listToRemoveFrom)
        {
            List<T> tmpList = new List<T>(listToRemoveFrom);
            foreach (T item in tmpList)
            {
                if (item == null)
                    listToRemoveFrom.Remove(item);
            }
        }
        //move element to last element in list
        public static void Move<T>(this List<T> list, int oldIndex, int newIndex)
        {
            T item = list[oldIndex];
            list.RemoveAt(oldIndex);
            list.Insert(newIndex, item);
        }
        //return list randomized order
        public static List<T> Shuffle<T>(this List<T> listToRemoveFrom)
        {
            List<T> outputList = new List<T>();
            List<T> items = new List<T>(listToRemoveFrom);
            foreach (T item in listToRemoveFrom)
            {
                T randomItem = items[Random.Range(0, items.Count)];
                outputList.Add(randomItem);
                items.Remove(randomItem);
            }
            return outputList;
        }
        public static T[,] ToSquareArray<T>(this IList<T> source)
        {
            if (source == null)
            {
                throw new System.ArgumentNullException("source");
            }

            int size = Mathf.CeilToInt(Mathf.Sqrt(source.Count()));

            var result = new T[size, size];
            int step = 0;
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    result[i, j] = source[step];
                    step++;
                }
            }

            return result;
        }

        public static List<T> FilterByPropertyVal<T>(this List<T> listToFilter, Dictionary<string, string> propsDict)
        {
            //prop GetValue will return the Getter of property. GetSetValue will return Setter. So if there is no getter it will return null.
            List<PropertyInfo> props = typeof(T).GetUnreflectedProperties().Where(a => a.PropertyType == typeof(string)).ToList();
            for (int i = 0; i < props.Count; i++)
            {
                //Debug.Log("Trying to get props[i] val: " + props[i]);
            }

            if (props.Count != propsDict.Count)
            {
                Debug.LogError("Incorrect amount of values to filter. Must be equal length of property count: " + props.Count + "; Given:" + propsDict.Count);
                return null;
            }
            List<T> matchingObjs = new List<T>(listToFilter);
            for (int i = 0; i < props.Count; i++)
            {
                string val = propsDict[props[i].Name.ToString()];
                if (val.Length > 0 && val != props[i].Name.ToString())
                {
                    matchingObjs = matchingObjs.Where(l => props[i].GetValue(l).ToString().Contains(val)).ToList();
                }
            }
            Debug.Log("Post Filter: " + matchingObjs.Count);
            return matchingObjs;
        }

    }
}