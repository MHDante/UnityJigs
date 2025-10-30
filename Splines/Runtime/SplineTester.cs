using UnityEngine;
using UnityEngine.Splines;

namespace UnityJigs.Splines
{
    [AddComponentMenu("")]
    public class SplineTester : MonoBehaviour
    {
        public SplineContainer Path = null!;

        private void OnDrawGizmosSelected()
        {
            if(!Path) return;

            using var _ = Path.GetLocalMinima(out var list, transform.position, 4, 1f);

            foreach (var (point, _) in list)
            {
                Debug.DrawRay(point, Vector3.up, Color.red);
            }
        }
    }
}
