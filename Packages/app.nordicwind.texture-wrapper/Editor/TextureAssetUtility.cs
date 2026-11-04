#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace NordicWind.TextureWrapper
{
    /// <summary>Loads texture assets with the data type their import settings describe.</summary>
    public static class TextureAssetUtility
    {
        /// <summary>
        /// The data type of a texture, from its importer when it is a texture asset:
        /// <see cref="TextureDataType.NormalTangent"/> for normal maps, otherwise
        /// <see cref="TextureDataType.Color"/> for sRGB textures and
        /// <see cref="TextureDataType.LinearData"/> for linear ones.
        /// </summary>
        public static TextureDataType GetDataType(Texture2D texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null && importer.textureType == TextureImporterType.NormalMap)
                return TextureDataType.NormalTangent;
            return GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)
                ? TextureDataType.Color : TextureDataType.LinearData;
        }

        /// <summary>Loads a texture with the data type from <see cref="GetDataType"/>.
        /// Read/Write doesn't need to be enabled.</summary>
        public static NordicsTextureWrapper Load(Texture2D texture, bool readOnly = true)
            => new NordicsTextureWrapper(texture, GetDataType(texture), readOnly);
    }
}
#endif
