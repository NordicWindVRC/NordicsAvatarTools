using System;
using System.Collections.Generic;
using NordicWind.TextureWrapper;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Within one source's evaluation (AtlasMapBaker), what several parts read is made once: loaded
    // textures, sampled textures, mask bus channels and painted vertex colors. Outside a scope
    // nothing is kept. The bake runs on one thread. Steps of the evaluation that take 100 ms or
    // more get a timing line of their own.
    public static class AtlasEvaluationCache
    {
        private static readonly Dictionary<object, NordicsTextureWrapper> Entries = new Dictionary<object, NordicsTextureWrapper>();
        private static int depth;
        private static List<string> timings;
        private static string label;

        // `timings` (optional) gets the slow steps' lines, named after `label`.
        public static IDisposable Scope(List<string> timings = null, string label = null)
        {
            if (depth++ == 0)
            {
                AtlasEvaluationCache.timings = timings;
                AtlasEvaluationCache.label = label;
            }
            return new End();
        }

        /// <summary>Names the evaluation the next steps' timing lines belong to.</summary>
        public static void Name(string name)
        {
            if (depth > 0) label = name;
        }

        /// <summary>Times one step of the evaluation; null outside a scope with timings.</summary>
        public static IDisposable Step(string step)
        {
            AtlasProgress.Detail(step);
            return timings == null ? null : new SlowStep(label + ": " + step, timings);
        }

        private sealed class SlowStep : IDisposable
        {
            private readonly string name;
            private readonly List<string> lines;
            private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            public SlowStep(string name, List<string> lines)
            {
                this.name = name;
                this.lines = lines;
            }

            public void Dispose()
            {
                if (!watch.IsRunning) return;
                watch.Stop();
                if (watch.Elapsed.TotalMilliseconds >= 100) lines.Add($"[Time] {name}: {watch.Elapsed.TotalMilliseconds:0} ms");
            }
        }

        private sealed class End : IDisposable
        {
            private bool done;

            public void Dispose()
            {
                if (done) return;
                done = true;
                if (--depth > 0) return;
                foreach (var entry in Entries.Values) entry.Dispose();
                Entries.Clear();
                timings = null;
                label = null;
            }
        }

        /// <summary>A copy of the map for <paramref name="key"/>, which the caller owns and may change;
        /// made on first use. Null when <paramref name="make"/> gives null.</summary>
        public static NordicsTextureWrapper Copy(object key, Func<NordicsTextureWrapper> make)
        {
            if (depth == 0) return make();
            if (!Entries.TryGetValue(key, out var map))
            {
                map = make();
                if (map == null) return null;
                Entries[key] = map;
            }
            return map.Clone();
        }

        /// <summary>The map for <paramref name="key"/> itself, only to be read. <paramref name="owned"/>
        /// is true outside a scope, where the caller disposes it.</summary>
        public static NordicsTextureWrapper Shared(object key, Func<NordicsTextureWrapper> make, out bool owned)
        {
            owned = depth == 0;
            if (owned) return make();
            if (!Entries.TryGetValue(key, out var map))
            {
                map = make();
                Entries[key] = map;
            }
            return map;
        }
    }
}
