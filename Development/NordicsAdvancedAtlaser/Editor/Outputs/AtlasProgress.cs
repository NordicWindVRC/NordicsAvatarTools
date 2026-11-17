using System;
using UnityEditor;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The bake's progress bar, with Cancel: the output being made, its texture, and the map and
    // source being baked. Cancel throws OperationCanceledException at the next source; the build
    // then stops and leaves the avatar as it was. Without Begin nothing is shown.
    public static class AtlasProgress
    {
        private static string title;
        private static int texture, textures;
        private static string textureName, current;
        private static float within;

        public static IDisposable Begin(string output, int textureCount)
        {
            title = "Nordic's Advanced Atlaser: " + output;
            textures = Math.Max(1, textureCount);
            texture = 0;
            textureName = null;
            return new End();
        }

        private sealed class End : IDisposable
        {
            private bool done;

            public void Dispose()
            {
                if (done) return;
                done = true;
                title = null;
                current = null;
                EditorUtility.ClearProgressBar();
            }
        }

        // The texture being made, 0-based.
        public static void Texture(int index, string name)
        {
            texture = index;
            textureName = name;
            Show($"{name}: starting", 0f);
        }

        // The source being baked into the texture, 0-based.
        public static void Source(string name, int index, int count)
        {
            current = $"{name}: source {index + 1} of {count}";
            within = count > 0 ? (float)index / count : 0f;
            Show(current, within);
        }

        // A step within the source being baked.
        public static void Detail(string step)
        {
            if (current != null) Show(current + ", " + step, within);
        }

        public static void Step(string step)
        {
            current = null;
            Show($"{textureName}: {step}", 1f);
        }

        private static void Show(string info, float withinTexture)
        {
            if (title == null) return;
            float progress = (texture + Math.Max(0f, Math.Min(1f, withinTexture))) / textures;
            if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
                throw new OperationCanceledException("The atlas bake was canceled; the avatar is left as it was.");
        }
    }
}
