using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal static class YBEditorGUI
    {
        public static void StatusRow(string label, bool ok, string okText = "Ready", string badText = "Missing")
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(170));
                var old = GUI.color;
                GUI.color = ok ? new Color(0.65f, 1f, 0.65f) : new Color(1f, 0.65f, 0.55f);
                EditorGUILayout.LabelField(ok ? "✓ " + okText : "✕ " + badText, EditorStyles.boldLabel);
                GUI.color = old;
            }
        }

        public static void Section(string title)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        public static void Info(string text)
        {
            EditorGUILayout.HelpBox(text, MessageType.Info);
        }
    }
}
