using UnityEditor;
using UnityEngine;

public sealed class SwordsmanModelImporter : AssetPostprocessor
{
    private bool IsSwordsman => assetPath.EndsWith("/Resources/Models/Swordsman.fbx",
        System.StringComparison.OrdinalIgnoreCase);

    private void OnPreprocessModel()
    {
        if (!IsSwordsman) return;
        ModelImporter importer = (ModelImporter)assetImporter;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.useFileScale = true;
        importer.globalScale = 1f;
    }

    private void OnPreprocessAnimation()
    {
        if (!IsSwordsman) return;
        ModelImporter importer = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            bool loop = clip.name.Contains("Idle_TwoHand_Sword") || clip.name.Contains("Walk_");
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.keepOriginalOrientation = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
    }
}
