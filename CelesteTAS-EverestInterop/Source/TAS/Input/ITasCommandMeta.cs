using StudioCommunication;
using StudioCommunication.Util;
using System.Collections.Generic;

namespace TAS.Input;

/// Describes additional information about a command, for Studio to use
public abstract class ITasCommandMeta {
    public virtual string Insert { get; } = "";
    public virtual bool HasArguments { get; }

    /// Produces a hash for the specified arguments, to cache arguments
    public virtual int GetHash(string[] args, string filePath, int fileLine) {
        // Exclude the last argument, since we're currently editing that
        int accum = 17;
        for (int i = 0; i < args.Length - 1; i++) {
            accum = 31 * accum + args[i].GetStableHashCode();
        }

        return accum;
    }

    /// Incrementally yields entries for auto-completion with the current arguments
    public virtual IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
        yield break;
    }
}
