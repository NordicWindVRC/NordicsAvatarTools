using UnityEditor;
using UnityEngine;
using NordicWind.AvatarStatistics;

namespace NordicWind.AvatarStatistics.Editor
{
    [CustomEditor(typeof(NordicsAvatarStatistics))]
    internal sealed class NordicsAvatarStatisticsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Nordic's Avatar Statistics gathers mesh, material and texture statistics at NDMF PlatformFinish and displays one informational report in the NDMF Console. This component has no settings.", MessageType.Info);
            if (((NordicsAvatarStatistics)target).GetComponent("VRCAvatarDescriptor") == null)
                EditorGUILayout.HelpBox("Attach this component to the same GameObject as the VRC Avatar Descriptor. The descriptor object does not need to be the top-level Transform in the scene hierarchy.", MessageType.Warning);
        }
    }
}
