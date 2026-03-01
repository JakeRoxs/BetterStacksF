using System.Collections.Generic;
using System.IO;

using HarmonyLib;

namespace BetterStacksF.Saveables {
  /// <summary>
  /// Marker base class for all saveables defined by the BetterStacks mod.
  /// 
  /// The stock S1API pipeline creates one directory per saveable type; this
  /// subclass allows every derived object to be redirected into a single,
  /// shared subfolder (<c>Modded/Saveables/BetterStacksF</c>) within the
  /// chosen save slot.  Each saveable still writes its own JSON files, which
  /// avoids name collisions as long as field names are chosen sensibly.
  /// </summary>
  ///
  /// <remarks>
  /// Instances of this type contain no behaviour themselves – they exist only
  /// so that the Harmony patches in <see cref="BetterStacksSaveablePatches"/>
  /// can identify them and rewrite folder paths.  The constant value used for
  /// the shared folder is mirrored in the diagnostics script; update both when
  /// renaming the directory.
  /// </remarks>
  internal abstract class BetterStacksSaveable : S1API.Internal.Abstraction.Saveable {
    // no additional members; used purely for type identification
  }

  /// <summary>
  /// Harmony patches on <see cref="S1API.Internal.Abstraction.Saveable"/>
  /// that reroute folder paths for any instance deriving from
  /// <see cref="BetterStacksSaveable"/>.
  ///
  /// The shared directory name is kept in sync with the constant used by the
  /// diagnostics script so that our tooling can continue to locate the data.
  /// </summary>
  [HarmonyPatch]
  internal static class BetterStacksSaveablePatches {
    private const string SharedFolderName = "BetterStacksF";

    private static string GetSharedRoot(string baseFolder) {
      return Path.Combine(baseFolder, SharedFolderName);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(S1API.Internal.Abstraction.Saveable), "SaveInternal")]
    private static void SaveInternal_Prefix(S1API.Internal.Abstraction.Saveable __instance, ref string folderPath) {
      if (__instance is BetterStacksSaveable) {
        // `GetDirectoryName` returns null for root paths; fall back to the
        // original string in that unlikely case.
        string parent = Path.GetDirectoryName(folderPath) ?? folderPath;

        // ensure the shared directory exists before we hand the path off
        string target = GetSharedRoot(parent);
        Directory.CreateDirectory(target);

        // redirect the save operation
        folderPath = target;
      }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(S1API.Internal.Abstraction.Saveable), "LoadInternal")]
    private static void LoadInternal_Prefix(S1API.Internal.Abstraction.Saveable __instance, ref string folderPath) {
      if (__instance is BetterStacksSaveable) {
        // same parent logic as above
        string parent = Path.GetDirectoryName(folderPath) ?? folderPath;
        string target = GetSharedRoot(parent);

        // only redirect if the shared folder already exists – this avoids
        // introducing a non‑existent path during load.
        if (Directory.Exists(target))
          folderPath = target;
      }
    }
  }
}
