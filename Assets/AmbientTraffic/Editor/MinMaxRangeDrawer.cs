using UnityEditor;
using UnityEngine;

namespace FuR.AmbientTraffic.Editor
{
    /// <summary>Draws a MinMaxRange field as a two-handle slider flanked by editable
    /// min/max number fields, clamped to the attribute's limits.</summary>
    [CustomPropertyDrawer(typeof(MinMaxRangeAttribute))]
    public class MinMaxRangeDrawer : PropertyDrawer
    {
        const float FieldW = 50f;
        const float Pad = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (MinMaxRangeAttribute)attribute;
            var minProp = property.FindPropertyRelative("Min");
            var maxProp = property.FindPropertyRelative("Max");
            if (minProp == null || maxProp == null)
            {
                EditorGUI.LabelField(position, label.text, "Use [MinMaxRange] on a MinMaxRange field.");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            var r = EditorGUI.PrefixLabel(position, label);

            var minRect = new Rect(r.x, r.y, FieldW, r.height);
            var sliderRect = new Rect(r.x + FieldW + Pad, r.y, r.width - 2f * (FieldW + Pad), r.height);
            var maxRect = new Rect(r.xMax - FieldW, r.y, FieldW, r.height);

            float min = minProp.floatValue;
            float max = maxProp.floatValue;

            EditorGUI.BeginChangeCheck();
            min = EditorGUI.FloatField(minRect, min);
            EditorGUI.MinMaxSlider(sliderRect, ref min, ref max, attr.Limit0, attr.Limit1);
            max = EditorGUI.FloatField(maxRect, max);
            if (EditorGUI.EndChangeCheck())
            {
                min = Mathf.Clamp(min, attr.Limit0, attr.Limit1);
                max = Mathf.Clamp(max, attr.Limit0, attr.Limit1);
                if (min > max) min = max;
                minProp.floatValue = min;
                maxProp.floatValue = max;
            }

            EditorGUI.EndProperty();
        }
    }
}
