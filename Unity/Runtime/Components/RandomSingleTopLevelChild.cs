using UnityEngine;

namespace UnityJigs.Components
{
    public class RandomSingleTopLevelChild : MonoBehaviour
    {
        private void Awake()
        {
            var childCount = transform.childCount;
            if (childCount == 0) return;

            var chosenIndex = Random.Range(0, childCount);
            for (var i = 0; i < childCount; i++)
                transform.GetChild(i).gameObject.SetActive(i == chosenIndex);
        }
    }
}
