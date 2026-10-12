using UnityEditor;
using UnityEngine;

namespace TinyTribe.EditorTools
{
    /// <summary>
    /// Imports every picture under Assets/Resources/Art as crisp pixel art: 16 pixels per unit,
    /// no smoothing, no compression, original size. The game cuts the sheets into tiles itself
    /// (see PackArt), so each file stays a single sprite.
    /// </summary>
    sealed class ArtImportSettings : AssetPostprocessor
    {
        const string ArtFolder = "Assets/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            // The game copies tiles out of the sheets into its own atlas, which needs the pixels.
            importer.isReadable = true;
        }
    }
}
