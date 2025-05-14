using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace UnityJigs.Extensions
{
    public static class AnimationCurveUtils
    {
        [ThreadStatic] private static float[]? _BuiltInSampler;

        public static float SampleAnimationCurveDistribution(this AnimationCurve curve, float? t = null, float[]? sampleArray = null)
        {
            sampleArray ??= _BuiltInSampler ??= new float[30];
            Array.Clear(sampleArray, 0, sampleArray.Length);
            var startTime = curve.keys[0].time;
            var endTime = curve.keys[curve.length - 1].time;
            t ??= Mathf.Lerp(startTime, endTime, Random.Range(0, 1f));
            t /= endTime;

            var interval = (endTime - startTime) / (sampleArray.Length - 1);
            IntegrateCurve(sampleArray, curve, startTime, interval);
            var matchingIndex = SampleIntegratedCurve(sampleArray, t!.Value);

            var matchingTimeStamp = startTime + matchingIndex * interval;
            return matchingTimeStamp;
        }

        /// <summary>
        /// Generate samples from probability distribution derived from a given AnimationCurve.
        /// </summary>
        /// <param name="integratedCurve">Numerical integration representing the AnimationCurve</param>
        /// <param name="uniformSample">A sample value between 0 and 1 generated from a uniform distribution</param>
        /// <returns>The generated sample</returns>
        public static float SampleIntegratedCurve(float[] integratedCurve, float uniformSample)
        {
            var scaledSample = uniformSample * integratedCurve[^1];

            for (var i = 0; i < integratedCurve.Length - 1; i++)
            {
                if (scaledSample > integratedCurve[i] && scaledSample < integratedCurve[i + 1])
                {
                    var valueDifference = integratedCurve[i + 1] - integratedCurve[i];
                    var upperWeight = (scaledSample - integratedCurve[i]) / valueDifference;
                    var lowerWeight = 1 - upperWeight;
                    var matchingIndex = i * lowerWeight + (i + 1) * upperWeight;
                    return matchingIndex;
                }
            }
            throw new ArithmeticException("Could not find matching timestamp.");
        }


        /// <summary>
        /// Numerically integrate a given AnimationCurve using the specified number of samples.
        /// Based on https://en.wikipedia.org/wiki/Numerical_integration and http://blog.s-schoener.com/2018-05-05-animation-curves/
        /// Using the trapezoidal rule for numerical interpolation
        /// </summary>
        /// <param name="array">The array to fill with integrated values</param>
        /// <param name="curve">The animation curve to sample integrate</param>
        /// <param name="startTime">The time attribute of the first key of the original AnimationCurve</param>
        /// <param name="interval">The interval at which the curve was sampled to fit the array</param>
        /// <exception cref="ArgumentException"></exception>
        public static void IntegrateCurve(float[] array, AnimationCurve curve, float startTime, float interval)

        {
            if (curve.length == 0)
            {
                throw new ArgumentException("The provided Animation Curve includes no keys.");
            }

            array[0] = 0;
            var previousValue = curve.Evaluate(startTime);

            for (var i = 1; i < array.Length; i++)
            {
                if (curve.length == 1)
                {
                    array[i] = previousValue;
                }
                else
                {
                    var currentTime = startTime + i * interval;
                    var currentValue = curve.Evaluate(currentTime);
                    array[i] = array[i - 1] + (previousValue + currentValue) * interval / 2;
                    previousValue = currentValue;
                }
            }
        }
    }
}
