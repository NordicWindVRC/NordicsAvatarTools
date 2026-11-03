using System.Collections.Generic;

namespace NordicWind.AvatarStatistics.Editor
{
    public sealed class AvatarStatisticsReport
    {
        internal int TriangleCount, SkinnedCount, RegularCount, MaterialSlots, BlendShapeCount;
        internal readonly List<PerformanceMetric> Performance = new List<PerformanceMetric>();
        internal long TextureBytes, GpuTextureBytes, PcGpuTextureBytes, QuestGpuTextureBytes;
        internal int PcTextureFallbacks, QuestTextureFallbacks;
        internal bool ExpressionParametersAvailable;
        internal int SyncedParameterBits, CustomParameterCount;
        internal bool PostSdkParameterSnapshot;
        internal int BeforeSdkParameterBits;
        internal int ParameterWidthUnknownCount;
        internal readonly List<MeshEntry> Meshes = new List<MeshEntry>();
        internal readonly List<MaterialEntry> Materials = new List<MaterialEntry>();
        internal readonly List<TextureEntry> Textures = new List<TextureEntry>();
    }
    internal sealed class PerformanceMetric
    {
        internal string Name, Display, Note;
        internal double Value;
        internal double? PcValue, QuestValue;
        internal string PcDisplay, QuestDisplay;
        internal double[] Pc, Quest; // four published maxima: Excellent, Good, Medium, Poor
        internal bool Available = true, QuestApplicable = true;
        internal bool OfficialSdk, PcAvailable = true, QuestAvailable = true;
        internal bool IsBounds;
        internal float BoundsX, BoundsY, BoundsZ;
    }
    internal sealed class MeshEntry
    {
        internal string Path, Name, Type;
        internal int Triangles, Slots;
        internal readonly List<string> Submeshes = new List<string>();
        internal string AssetPath;
    }
    internal sealed class MaterialEntry
    {
        internal string Name, Shader, AssetPath;
        internal int Slots;
        internal readonly List<string> Textures = new List<string>();
    }
    internal sealed class TextureEntry
    {
        internal string Name, Format, AssetPath, Dimension;
        internal int Width, Height, Mips;
        internal long Bytes, GpuBytes, PcGpuBytes, QuestGpuBytes;
        internal bool PcFallback, QuestFallback;
        internal bool HasImporter, StreamingMipmaps, CrunchCompressed;
        internal int StreamingPriority, CrunchQuality;
        internal string MipmapFilter, DisplayType;
        internal long CrunchStorageBytes = -1;
        internal readonly List<string> References = new List<string>();
        internal readonly HashSet<string> MaterialKeys = new HashSet<string>();
    }
}
