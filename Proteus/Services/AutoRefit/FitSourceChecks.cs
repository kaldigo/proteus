using System;
using System.Collections.Generic;

namespace Proteus.Services;

// A source check can peel multiple generated layers from one path, or change the drawn
// racial path entirely. Keep the original path checked when its generated layer returns.
internal sealed class FitSourceChecks
{
    private readonly Dictionary<string, long> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Revision, long Until)> pending = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> openPaths = new(StringComparer.OrdinalIgnoreCase);

    internal bool NeedsCheck(string path, long revision)
        => openPaths.Contains(path) || !paths.TryGetValue(path, out long seen) || seen != revision;

    internal bool WasRevealed(string path, string dir, long revision, out long until)
    {
        bool found = pending.TryGetValue(path + "|" + dir, out var stamp);
        until = stamp.Until;
        return found && stamp.Revision == revision;
    }

    internal void Revealed(string path, string dir, long revision, long until)
    {
        Checked(path, revision);
        openPaths.Add(path);
        pending[path + "|" + dir] = (revision, until);
    }

    internal void Checked(string path, long revision) => paths[path] = revision;
    internal void Settled()
    {
        pending.Clear();
        openPaths.Clear();
    }
    internal void Clear()
    {
        paths.Clear();
        Settled();
    }
}
