using UnityEngine;

namespace FuR.AmbientTraffic
{
    /// <summary>A serializable [min, max] float range. Drop-in replacement for the
    /// framework range type: exposes public Min/Max fields and a (min, max) constructor.</summary>
    [System.Serializable]
    public struct MinMaxRange
    {
        public float Min;
        public float Max;

        public MinMaxRange(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>Put on a <see cref="MinMaxRange"/> field to draw a two-handle slider
    /// clamped to [limit0, limit1]. Editor drawer lives in the Editor assembly.</summary>
    public class MinMaxRangeAttribute : PropertyAttribute
    {
        public readonly float Limit0;
        public readonly float Limit1;

        public MinMaxRangeAttribute(float limit0, float limit1)
        {
            Limit0 = limit0;
            Limit1 = limit1;
        }
    }
}
