#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NordicWind.TextureWrapper
{
    /// <summary>Synchronizes PNG assets within Unity's Assets folder. Ownership and cleanup stay with the caller.</summary>
    public sealed class PngAssetTextureExporter : INordicsTextureExporter
    {
        private readonly string _directory;
        public PngAssetTextureExporter(string assetDirectory)
        {
            if(string.IsNullOrWhiteSpace(assetDirectory) || !(assetDirectory=="Assets" || assetDirectory.StartsWith("Assets/",StringComparison.Ordinal)))
                throw new ArgumentException("Exporter directory must be inside Unity's Assets folder.",nameof(assetDirectory));
            _directory=assetDirectory.TrimEnd('/');
        }
        public Texture2D Synchronize(NordicsTextureWrapper wrapper,string name,bool pixelsChanged,bool settingsChanged)
        {
            if(wrapper==null)throw new ArgumentNullException(nameof(wrapper));
            if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Contains("/")||name.Contains("\\")||name=="..")throw new ArgumentException("Invalid filename.",nameof(name));
            wrapper.Settings.Validate();
            string assetPath=_directory+"/"+name+".png";
            string absolute=Path.GetFullPath(assetPath);
            string baseDir=Path.GetFullPath(_directory);
            if(!absolute.StartsWith(baseDir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Path escapes export directory.");
            Directory.CreateDirectory(baseDir);
            bool existed=File.Exists(absolute);
            if(pixelsChanged||!existed)
            {
                Color[] colors=wrapper.GetEncodedPixels();
                // RGB24 produces an RGB PNG; RGBA32 preserves alpha.
                var staging=new Texture2D(wrapper.Width,wrapper.Height,wrapper.GetOutputTextureFormat(),false,true);
                try
                {
                    staging.SetPixels(colors);staging.Apply(false,false);
                    File.WriteAllBytes(absolute,staging.EncodeToPNG());
                }
                finally {UnityEngine.Object.DestroyImmediate(staging);}
                AssetDatabase.ImportAsset(assetPath,ImportAssetOptions.ForceSynchronousImport);
            }
            if(settingsChanged||!existed||pixelsChanged)
            {
                var importer=AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if(importer==null)throw new InvalidOperationException("PNG importer not found: "+assetPath);
                TextureSettings s=wrapper.Settings;
                importer.textureType=s.IsNormal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=!s.IsNormal&&s.SRGB;
                importer.mipmapEnabled=s.Mipmaps;
                importer.streamingMipmaps=s.Mipmaps&&s.StreamingMipmaps;
                importer.filterMode=s.FilterMode;
                importer.wrapMode=s.WrapMode;
                importer.anisoLevel=s.AnisoLevel;
                importer.alphaSource = wrapper.GetOutputTextureFormat() == TextureFormat.RGB24
                    ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency=s.AlphaIsTransparency && !s.IsNormal
                    && wrapper.GetOutputTextureFormat() == TextureFormat.RGBA32;
                importer.textureCompression=s.Compression==TextureCompression.None?TextureImporterCompression.Uncompressed:s.Compression==TextureCompression.Low?TextureImporterCompression.CompressedLQ:s.Compression==TextureCompression.High?TextureImporterCompression.CompressedHQ:TextureImporterCompression.Compressed;
                importer.crunchedCompression=s.Crunch&&s.Compression!=TextureCompression.None;
                importer.SaveAndReimport();
            }
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if(texture==null)throw new InvalidOperationException("Imported PNG did not produce a Texture2D: "+assetPath);
            return texture;
        }
    }

    public sealed class MemoryTextureExporter : INordicsTextureExporter, IDisposable
    {
        private Texture2D _texture;
        public Texture2D Synchronize(NordicsTextureWrapper wrapper,string name,bool pixelsChanged,bool settingsChanged)
        {
            if(wrapper==null)throw new ArgumentNullException(nameof(wrapper));
            TextureFormat desiredFormat = wrapper.GetOutputTextureFormat();
            if(_texture==null||_texture.width!=wrapper.Width||_texture.height!=wrapper.Height
                || _texture.format != desiredFormat || (_texture.mipmapCount > 1) != wrapper.Settings.Mipmaps)
            {
                Dispose();_texture=new Texture2D(wrapper.Width,wrapper.Height,desiredFormat,wrapper.Settings.Mipmaps,!wrapper.Settings.SRGB||wrapper.Settings.IsNormal);
                pixelsChanged=true;
            }
            if(pixelsChanged){_texture.SetPixels(wrapper.GetEncodedPixels());_texture.Apply(wrapper.Settings.Mipmaps,false);}
            _texture.filterMode=wrapper.Settings.FilterMode;_texture.wrapMode=wrapper.Settings.WrapMode;_texture.anisoLevel=wrapper.Settings.AnisoLevel;
            _texture.name=name;return _texture;
        }
        public void Dispose(){if(_texture!=null)UnityEngine.Object.DestroyImmediate(_texture);_texture=null;}
    }
}
#endif
