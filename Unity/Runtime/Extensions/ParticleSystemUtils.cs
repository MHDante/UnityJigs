using UnityEngine;

namespace UnityJigs.Extensions
{
    public static class ParticleSystemUtils
    {
        public static void ApplyMultiplier(this ref ParticleSystem.MinMaxCurve curve, float multiplier)
        {
            curve.constant *= multiplier;
            curve.constantMax *= multiplier;
            curve.constantMin *= multiplier;
            curve.curveMultiplier *= multiplier;
        }
    }
}
