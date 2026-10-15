using System;
using System.Collections.Generic;
using Game.Domain;
using Game.Domain.MasterData;

namespace Game.Core.Assets
{
    public sealed class AssetManifestValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    public static class AssetManifestValidator
    {
        public static AssetManifestValidationResult Validate(
            AssetCatalog catalog,
            MasterDatabase master = null,
            bool isReleaseMode = false)
        {
            var result = new AssetManifestValidationResult();
            if (catalog == null)
            {
                result.Errors.Add("AssetCatalog is null.");
                return result;
            }

            foreach (var kvp in catalog.Entries)
            {
                var entry = kvp.Value;

                // D3-2: 資料夾分級下限檢查
                // tier2_tease/, tier3_climax_poses/, tier4_aftermath/ 來源之素材必須標為 R18
                if (!string.IsNullOrEmpty(entry.Source))
                {
                    string srcNorm = entry.Source.Replace('\\', '/');
                    if (srcNorm.Contains("tier2_tease/") ||
                        srcNorm.Contains("tier3_climax_poses/") ||
                        srcNorm.Contains("tier4_aftermath/"))
                    {
                        if (entry.Rating < Rating.R18)
                        {
                            result.Errors.Add($"ERR_RATING_BELOW_FOLDER_MINIMUM: Asset '{entry.Key}' from source '{entry.Source}' is marked as {entry.Rating}, but folder requires minimum R18.");
                        }
                    }
                }

                // D3-4: 發行模式檢查
                if (isReleaseMode)
                {
                    if (string.Equals(entry.Status, "placeholder", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"ERR_PLACEHOLDER_IN_RELEASE: Asset '{entry.Key}' has status 'placeholder' in release mode.");
                    }
                    else if (string.Equals(entry.Status, "draft", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"WARN_DRAFT_IN_RELEASE: Asset '{entry.Key}' has status 'draft' in release mode.");
                    }
                }
            }

            // D3-3: 主資料 art key 是否存在於 manifest (僅警示)
            if (master != null)
            {
                var referencedArtKeys = CollectMasterArtKeys(master);
                foreach (string artKey in referencedArtKeys)
                {
                    if (!catalog.Entries.ContainsKey(artKey))
                    {
                        result.Warnings.Add($"WARN_MISSING_MANIFEST_KEY: MasterData art key '{artKey}' is not declared in asset manifest.");
                    }
                }
            }

            return result;
        }

        public static HashSet<string> CollectMasterArtKeys(MasterDatabase master)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (master == null) return set;

            foreach (var ps in master.PortraitStages.Values)
            {
                if (ps.Art != null)
                {
                    if (!string.IsNullOrEmpty(ps.Art.Stand)) set.Add(ps.Art.Stand);
                    if (!string.IsNullOrEmpty(ps.Art.Bust)) set.Add(ps.Art.Bust);
                    if (!string.IsNullOrEmpty(ps.Art.Live2d)) set.Add(ps.Art.Live2d);
                }
            }

            foreach (var cos in master.Costumes.Values)
            {
                if (cos.Art != null)
                {
                    if (!string.IsNullOrEmpty(cos.Art.Stand)) set.Add(cos.Art.Stand);
                    if (!string.IsNullOrEmpty(cos.Art.Bust)) set.Add(cos.Art.Bust);
                    if (!string.IsNullOrEmpty(cos.Art.Live2d)) set.Add(cos.Art.Live2d);
                }
            }

            foreach (var scene in master.Scenes.Values)
            {
                if (scene.Variants != null)
                {
                    foreach (var v in scene.Variants.Values)
                    {
                        if (!string.IsNullOrEmpty(v)) set.Add(v);
                    }
                }
            }

            foreach (var stage in master.Stages.Values)
            {
                if (!string.IsNullOrEmpty(stage.Bg)) set.Add(stage.Bg);
            }

            return set;
        }
    }
}
