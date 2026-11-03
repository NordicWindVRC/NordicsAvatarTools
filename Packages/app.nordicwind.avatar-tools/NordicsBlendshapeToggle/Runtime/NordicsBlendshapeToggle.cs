using nadena.dev.ndmf;
using UnityEngine;

namespace NordicWind.BlendshapeToggle
{
    public enum ToggleCenterMode { MeshCenter, SubmeshCenter, Transform }

    [AddComponentMenu("NordicWind/Nordic's Blendshape Toggle")]
    public sealed class NordicsBlendshapeToggle : MonoBehaviour, INDMFEditorOnly
    {
        public int materialSlot;
        public bool reverseBlendshape;
        public string blendshapeName = "NBT_Toggle";
        public ToggleCenterMode centerMode = ToggleCenterMode.SubmeshCenter;
        public Transform targetTransform;
        [Min(0)] public float collapsedScale = 0.001f;
    }
}
