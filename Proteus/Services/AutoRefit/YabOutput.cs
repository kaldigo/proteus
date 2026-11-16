using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Proteus.Services;

internal sealed record YabOutput(string Item, string Layer, int Priority, string Stamp)
{
    internal const string Base = "Base refits";
    internal const string Resize = "YAB size refits";
    internal const string Manual = "Manual corrections";
    internal const string Prefix = "Proteus Fit ";
    internal string Folder => RefitModService.OwnModsFolder + "/" + Layer;
    internal static bool IsYab(string body)
        => body.Contains("Yet Another Body", StringComparison.OrdinalIgnoreCase)
           || body.Contains("YAB", StringComparison.OrdinalIgnoreCase);
    internal static string Key(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    internal static int Above(int priority) => priority == int.MaxValue ? priority : priority + 1;

    internal static YabOutput? For(Configuration config, string version, string target, string source, string gamePath, byte[] bytes, int sourcePriority)
    {
        if (!IsYab(target)) return null;
        bool resize = IsYab(source);
        return new(Prefix + Path.GetFileNameWithoutExtension(gamePath), resize ? Resize : Base,
                   resize ? Math.Max(config.SizeFitPriority, Above(sourcePriority)) : config.BaseFitPriority,
                   Key(FormattableString.Invariant($"{version}|{config.UseYabField}|{config.AutoRefitPiercings}|{config.VanillaChestStrength:R}|{config.VanillaLegStrength:R}|") + source + Convert.ToHexString(SHA256.HashData(bytes))));
    }

    internal sealed record Origin(string GamePath, string? Directory, string? File);
    private static string Marker(string root, string file)
        => Path.Combine(root, "proteus-origin-" + Key(file.Replace('\\','/').ToLowerInvariant()) + ".json");

    internal static void Remember(string root, string file, Origin origin)
    {
        string marker = Marker(root, file);
        string temp = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(origin));
            System.IO.File.Move(temp, marker, true);
        }
        finally { System.IO.File.Delete(temp); }
    }

    internal static Origin? Read(string root, string file, string gamePath)
    {
        try
        {
            var origin = JsonSerializer.Deserialize<Origin>(System.IO.File.ReadAllText(Marker(root, file)));
            if (origin == null || !string.Equals(origin.GamePath, gamePath, StringComparison.OrdinalIgnoreCase)) return null;
            if (origin.Directory == null && origin.File == null) return origin;
            if (string.IsNullOrWhiteSpace(origin.Directory) || string.IsNullOrWhiteSpace(origin.File)
                || origin.Directory != Path.GetFileName(origin.Directory) || origin.Directory is "." or ".."
                || origin.File.Contains(':') || Path.IsPathRooted(origin.File)
                || Array.Exists(origin.File.Replace('\\','/').Split('/'), part => part is "." or ".." or "")) return null;
            return origin;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
