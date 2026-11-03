using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace NordicWind.AvatarStatistics.Editor
{
    // CRITICAL: never remove/reorder this late callback. NDMF sees pre-compression
    // synced parameter memory; VRCFury compression is NOT an NDMF operation.
    // No VRCFury assembly reference. Runs after known late SDK processors
    // (for example VRCFury ParameterCompressor at int.MaxValue - 100).
    public sealed class AvatarStatisticsLateSdkCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 10;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            ParameterFinalizationRegistry.OnLateSdkPreprocess(avatarGameObject);
            return true;
        }
    }
}
