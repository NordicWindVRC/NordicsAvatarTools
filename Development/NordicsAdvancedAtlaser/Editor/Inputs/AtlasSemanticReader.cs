using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Explicit source-to-semantic mapping helpers. Property IDs are used only by input parsers.
    /// Outputs see semantic feature IDs, typed parameters and canonical choice names.
    /// No automatic inference from unknown shader property names is performed.
    /// </summary>
    internal static class AtlasSemanticReader
    {
        public static AtlasSemanticFeature Feature(AtlasUnifiedMaterial output, string semanticId, bool enabled = true)
        {
            var feature = output.Features.Find(x => x.Id == semanticId);
            if (feature == null)
            {
                feature = new AtlasSemanticFeature { Id = semanticId, Enabled = enabled };
                output.Features.Add(feature);
            }
            return feature;
        }
        public static bool Toggle(Material material, string property, bool fallback = false)
        {
            return AtlasMaterialReader.Has(material, property)
                ? AtlasMaterialReader.Float(material, property, fallback ? 1f : 0f) > 0.5f
                : fallback;
        }
        public static void Number(Material m, AtlasSemanticFeature feature, string semantic, string source)
        {
            if (!AtlasMaterialReader.Has(m,source)) return;
            feature.Parameters[semantic] = new AtlasSemanticValue {
                Kind = AtlasSemanticValueKind.Number,
                Number = AtlasMaterialReader.Float(m,source,0f)
            };
        }
        public static void Boolean(Material m, AtlasSemanticFeature feature, string semantic, string source)
        {
            if (!AtlasMaterialReader.Has(m,source)) return;
            feature.Parameters[semantic] = new AtlasSemanticValue {
                Kind = AtlasSemanticValueKind.Boolean,
                Boolean = Toggle(m,source)
            };
        }
        public static void Color(Material m, AtlasSemanticFeature feature, string semantic, string source)
        {
            if (!AtlasMaterialReader.Has(m,source)) return;
            feature.Colors[semantic] = new AtlasColorInput {
                Color = AtlasMaterialReader.Color(m,source,UnityEngine.Color.white)
            };
        }
        public static void Vector(Material m, AtlasSemanticFeature feature, string semantic, string source)
        {
            if (!AtlasMaterialReader.Has(m,source) || m.shader == null) return;
            int index = m.shader.FindPropertyIndex(source);
            if (index < 0 || m.shader.GetPropertyType(index) != ShaderPropertyType.Vector) return;
            feature.Parameters[semantic] = new AtlasSemanticValue {
                Kind = AtlasSemanticValueKind.Vector, Vector = m.GetVector(source)
            };
        }
        public static void Texture(Material m, AtlasUnifiedMaterial output, AtlasSemanticFeature feature,
            string semantic, string source, AtlasColorInterpretation interpretation = AtlasColorInterpretation.LinearData)
        {
            var sample = AtlasMaterialReader.Sample(m,source,interpretation,output);
            if (sample != null) feature.Textures[semantic] = sample;
        }
        public static void Choice(Material m, AtlasSemanticFeature feature, string semantic,
            string source, string[] names, AtlasUnifiedMaterial output)
        {
            if (!AtlasMaterialReader.Has(m,source)) return;
            int index = Mathf.RoundToInt(AtlasMaterialReader.Float(m,source,0));
            if (names == null || index < 0 || index >= names.Length || string.IsNullOrEmpty(names[index]))
            {
                feature.Quality = AtlasNormalizationQuality.Unresolved;
                output.Issues.Add(new AtlasNormalizationIssue(feature.Id+"."+semantic,
                    AtlasNormalizationSeverity.Warning, "Source enum has a value not covered by its semantic mapping."));
                return;
            }
            feature.Parameters[semantic] = new AtlasSemanticValue {
                Kind = AtlasSemanticValueKind.Choice, Choice = names[index]
            };
        }
        // Read the semantic option label from a ShaderLab Enum/KeywordEnum/ThryWideEnum
        // declaration, rather than leaking its numeric index into output definitions.
        // Labels are canonicalized for cross-shader comparisons (Additive -> add, etc.).
        public static bool TryDeclaredChoice(Material m, string source, out string choice)
        {
            choice = null;
            if (!AtlasMaterialReader.Has(m,source) || m.shader == null) return false;
            int index=m.shader.FindPropertyIndex(source);
            if(index<0)return false;
            string[] attributes=m.shader.GetPropertyAttributes(index);
            if(attributes==null)return false;
            int selected=Mathf.RoundToInt(AtlasMaterialReader.Float(m,source,0f));
            foreach(var attribute in attributes)
            {
                if(string.IsNullOrEmpty(attribute))continue;
                bool keyword=attribute.StartsWith("KeywordEnum(",StringComparison.OrdinalIgnoreCase);
                bool paired=attribute.StartsWith("Enum(",StringComparison.OrdinalIgnoreCase) ||
                    attribute.StartsWith("ThryWideEnum(",StringComparison.OrdinalIgnoreCase);
                if(!keyword && !paired)continue;
                int open=attribute.IndexOf('(');
                int close=attribute.LastIndexOf(')');
                if(open<0||close<=open)continue;
                string[] parts=attribute.Substring(open+1,close-open-1).Split(',');
                if(keyword)
                {
                    if(selected<0||selected>=parts.Length)return false;
                    choice=CanonicalChoice(parts[selected]);
                    return !string.IsNullOrEmpty(choice);
                }
                // ShaderLab Enum labels are alternating (name, value) entries.
                for(int i=0;i+1<parts.Length;i+=2)
                {
                    float value;
                    if(!float.TryParse(parts[i+1].Trim(),NumberStyles.Float,
                        CultureInfo.InvariantCulture,out value))continue;
                    if(Mathf.RoundToInt(value)!=selected)continue;
                    choice=CanonicalChoice(parts[i]);
                    return !string.IsNullOrEmpty(choice);
                }
            }
            return false;
        }
        public static void DeclaredChoice(Material m, AtlasUnifiedMaterial output,
            AtlasSemanticFeature feature, string semantic, string source)
        {
            if(!AtlasMaterialReader.Has(m,source))return;
            string choice;
            if(TryDeclaredChoice(m,source,out choice))
                feature.Parameters[semantic]=new AtlasSemanticValue {Kind=AtlasSemanticValueKind.Choice,Choice=choice};
            else
            {
                feature.Quality=AtlasNormalizationQuality.Approximate;
                output.Issues.Add(new AtlasNormalizationIssue(feature.Id+"."+semantic,
                    AtlasNormalizationSeverity.Warning,
                    "An enum selector lacks a reliable declared option name; semantic choice was not guessed."));
            }
        }
        private static string CanonicalChoice(string raw)
        {
            string name=raw.Trim().ToLowerInvariant();
            if(name=="additive")return "add";
            if(name=="multiplicative")return "multiply";
            if(name=="alpha blended" || name=="alphablended")return "alpha";
            if(name=="multiplyx2")return "multiply-double";
            return Regex.Replace(name,@"[^a-z0-9]+","-").Trim('-');
        }
        public static void NumberSet(Material m, AtlasSemanticFeature feature, params string[] semanticAndProperty)
        {
            if (semanticAndProperty.Length % 2 != 0) throw new ArgumentException("Pairs expected");
            for(int i=0;i<semanticAndProperty.Length;i+=2) Number(m,feature,semanticAndProperty[i],semanticAndProperty[i+1]);
        }
        public static void ToggleSet(Material m, AtlasSemanticFeature feature, params string[] semanticAndProperty)
        {
            if (semanticAndProperty.Length % 2 != 0) throw new ArgumentException("Pairs expected");
            for(int i=0;i<semanticAndProperty.Length;i+=2) Boolean(m,feature,semanticAndProperty[i],semanticAndProperty[i+1]);
        }
    }
}
