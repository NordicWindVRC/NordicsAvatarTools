using System.Linq;
using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    internal static class AtlasBuildCleanup
    {
        internal static void RemoveBuildComponents(GameObject avatarRoot)
        {
            // NDMF supplies the build clone; never touch the scene avatar.
            foreach (var output in avatarRoot.GetComponentsInChildren<NordicsAtlasOutput>(true).ToArray())
                if (output != null) Object.DestroyImmediate(output);
            foreach (var atlaser in avatarRoot.GetComponentsInChildren<NordicsAdvancedAtlaser>(true).ToArray())
                if (atlaser != null) Object.DestroyImmediate(atlaser);
        }
    }
}
