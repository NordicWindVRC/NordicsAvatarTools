using System;
using UnityEditor;
using UnityEngine;

namespace NordicWind.SubmeshSplitter.Editor
{
    // Makes a stopped build visible. NDMF records the exception and blocks the upload, but
    // the message is easy to miss. In Play Mode it is worse: Av3Emulator runs the SDK build
    // callbacks, a failed callback makes VRCFury destroy the avatar, and the emulator then
    // logs a MissingReferenceException every frame. So show one dialog with the real
    // problem and, in Play Mode, leave Play Mode once it is dismissed.
    internal static class SplitBuildFailure
    {
        internal static void Raise(string message)
        {
            if (!Application.isBatchMode)
            {
                bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
                EditorUtility.DisplayDialog("Nordic's Submesh Splitter",
                    message + (playing ? "\n\nPlay Mode will be stopped." : "\n\nThe build was stopped."), "OK");
                if (playing) EditorApplication.delayCall += EditorApplication.ExitPlaymode;
            }
            throw new InvalidOperationException("[Nordic's Submesh Splitter] " + message);
        }
    }
}
