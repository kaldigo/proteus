using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Proteus.Services;

internal static class HandmadeYab
{
    // Chesder, https://www.xivmodarchive.com/modid/74534: Medium chest, loose skirt.
    // Pin the verified converted female model. Another edit to the same item must be measured again.
    internal const string LunarMedium = "9D9887EC3F1E9E36E0A193344417B7BD105844B8D4D77DA658BCEA21D0A912EE";

    private static bool Yab(string text) => Regex.IsMatch(text.Replace('_', ' '), @"\b(?:YAB\b|Yet Another Body\b)", RegexOptions.IgnoreCase);
    private static bool Other(string text) => Regex.IsMatch(text.Replace('_', ' '),
        @"\b(?:rue|ruexb|bibo|tre|tbse|uranus|lava(?:bod)?|mini|neolithe)\b", RegexOptions.IgnoreCase);

    internal static bool Declared(string? dir, string? name, string? file,
                                  IEnumerable<PenumbraModMeta.Redirect> redirects)
    {
        if (dir == null || file == null || dir.StartsWith(YabOutput.Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string title = dir + " " + name;
        var labels = redirects.Where(r => RefitCore.Normal(r.File) == RefitCore.Normal(file))
                              .Select(r => r.Source).Distinct().ToArray();
        if (labels.Length == 0) return false;
        // Mixed packs often label the option or the local file themselves.
        if (Other(file) || labels.Any(Other)) return false;
        if (Yab(file)) return true;
        if (labels.All(s => Yab(s) && !Other(s))) return true;
        return Yab(title) && !Other(title);
    }

    internal static bool Medium(BodyOption option)
        => option.Name.Equals("Medium", StringComparison.OrdinalIgnoreCase)
           || option.Name.Equals("Yiggle - Medium", StringComparison.OrdinalIgnoreCase);

    internal static AutoRefitDecisions.Pick? Read(BodyModIndex.Entry body, ModelParts garment,
        IReadOnlySet<string> bones, string slot, string race, BodyOption? target, byte[] original, out bool documented)
    {
        var options = body.Catalog.For(slot, race);
        documented = slot == "_top" && Convert.ToHexString(SHA256.HashData(original)) == LunarMedium;
        if (documented)
        {
            var medium = options.FirstOrDefault(o => o.Name.Equals("Medium", StringComparison.OrdinalIgnoreCase));
            if (medium != null && target != null && options.Contains(target) && Medium(target)
                && BodySizeMatch.SameRestingSkin(body.Catalog.PathOf(medium), body.Catalog.PathOf(target)))
                medium = target;
            return medium == null ? null : new(body.Dir, medium, BodySizeMatch.Confidence.Exact, false);
        }

        var rank = BodySizeMatch.Rank(garment, options, body.Catalog.PathOf, bones);
        if (!rank.Preselect || rank.Best is not { } best) return null;
        // Shared neck or seam geometry can score equally on several different chest or leg sizes.
        var ties = rank.Scores.Where(s => s.Rms <= MathF.Max(best.Rms, 0.000001f) * (1 + BodySizeMatch.SameAnswer));
        if (ties.Any(s => BodySizeMatch.ShapesDiffer(garment, body.Catalog.PathOf(best.Option), body.Catalog.PathOf(s.Option))))
            return null;
        var source = best.Option;
        if (target != null && options.Contains(target) && !BodySizeMatch.ShapesDiffer(garment, body.Catalog.PathOf(source), body.Catalog.PathOf(target)))
            source = target;
        return new(body.Dir, source, rank.Confidence, rank.FromCloth);
    }
}
