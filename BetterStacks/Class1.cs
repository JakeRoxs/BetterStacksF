using BetterStacks;
using BetterStacks.Networking;
using HarmonyLib;
using MelonLoader;

using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Stations;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.UI.Items;
using System.Reflection;
using System.Reflection.Emit;
using S1API.Lifecycle;
using System.Linq;


[assembly: MelonInfo(typeof(BetterStacksMod), "Better Stacks", "0.0.1", "Zarnes")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterStacks;

public class BetterStacksMod : MelonMod
{
    private static ModConfig _config = new ModConfig();
    // Store original stack limits so re-applying config uses the original base value (pre-modification).
    private static Dictionary<string, int> _originalStackLimits = new Dictionary<string, int>();

    // Indicates whether the current instance is being constrained by a host/server-authoritative config.
    public static bool ServerAuthoritative { get; internal set; } = false;

    // Convenience: whether the loaded mod config requests server-authoritative behavior.
    public static bool ServerAuthoritativeEnabled => _config?.EnableServerAuthoritativeConfig ?? false;

    public override void OnInitializeMelon()
    {
        _config = LoadConfig();

        // Initialize SteamNetworkAdapter directly (uses SteamNetworkLib.dll). Fall back to local adapter if not available.
        var steamAdapter = new SteamNetworkAdapter();
        NetworkingManager.Initialize(steamAdapter);
        if (!NetworkingManager.CurrentAdapter.IsInitialized)
        {
            MelonLogger.Msg("[Better Stacks] Steam adapter not available — falling back to local adapter.");
            NetworkingManager.Initialize(new LocalNetworkAdapter());
        }
        else
        {
            MelonLogger.Msg("[Better Stacks] SteamNetworkAdapter initialized.");
        }

        // Log the loaded configuration so we can verify which category multipliers are active at runtime.
        MelonLogger.Msg($"[Better Stacks] Loaded config: {JsonConvert.SerializeObject(_config)}");

        // If we're the session host, immediately broadcast the authoritative HostConfig so clients apply the same settings.
        if (NetworkingManager.CurrentAdapter?.IsHost ?? false)
        {
            NetworkingManager.BroadcastHostConfig(new HostConfig { Config = _config });
            MelonLogger.Msg("[Better Stacks] Broadcasted HostConfig (host).");
        }

        var harmony = new HarmonyLib.Harmony("com.zarnes.betterstacks");
        FileLog.LogWriter = new StreamWriter("harmony.log") { AutoFlush = true };

        var method = AccessTools.Method(typeof(ItemUIManager), "UpdateCashDragAmount");
        method = AccessTools.Method(typeof(ItemUIManager), "StartDragCash");
        method = AccessTools.Method(typeof(ItemUIManager), "EndCashDrag");


        // Use S1API lifecycle to apply stack overrides once at load time
        GameLifecycle.OnPreLoad += ApplyStackOverrides;



        // Patch Mixing Station capacity
        harmony.Patch(
            AccessTools.Method(typeof(MixingStation), "Start"),
            prefix: new HarmonyMethod(typeof(BetterStacksMod), nameof(PatchMixingStationCapacity))
        );

        // Patch Drying Rack capacity
        harmony.Patch(
            AccessTools.Method(typeof(DryingRackCanvas), "SetIsOpen"),
            prefix: new HarmonyMethod(typeof(BetterStacksMod), nameof(PatchDryingRackCapacity))
        );

        // Delivery stack limit handled by S1API.ApplyStackOverrides



        harmony.Patch(
            AccessTools.Method(typeof(ItemUIManager), "UpdateCashDragAmount"),
            transpiler: new HarmonyMethod(typeof(BetterStacksMod), nameof(TranspilerPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ItemUIManager), "StartDragCash"),
            transpiler: new HarmonyMethod(typeof(BetterStacksMod), nameof(TranspilerPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ItemUIManager), "EndCashDrag"),
            transpiler: new HarmonyMethod(typeof(BetterStacksMod), nameof(TranspilerPatch))
        );

    }

    public static ModConfig LoadConfig()
    {
        try
        {
            PreferencesMapper.EnsureRegistered();
            var cfg = PreferencesMapper.ReadFromPreferences();
            EnsureCategoryMultipliers(cfg, addEnumKeys: true);
            return cfg;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[Better Stacks] Failed to read MelonPreferences, falling back to defaults: {ex.Message}");
            var cfg = new ModConfig();
            EnsureCategoryMultipliers(cfg, addEnumKeys: true);
            return cfg;
        }
    }

    // Ensure CategoryMultipliers exists and merge legacy typed properties into the dictionary.
    // If addEnumKeys is true, ensure an entry exists for every EItemCategory enum name.
    private static bool EnsureCategoryMultipliers(ModConfig cfg, bool addEnumKeys = true)
    {
        bool changed = false;
        if (cfg.CategoryMultipliers == null)
        {
            cfg.CategoryMultipliers = new Dictionary<string, int>();
            changed = true;
        }

        void AddIfMissing(string key, int val)
        {
            if (!cfg.CategoryMultipliers.ContainsKey(key))
            {
                cfg.CategoryMultipliers[key] = val;
                changed = true;
            }
        }

        if (addEnumKeys)
        {
            foreach (var name in Enum.GetNames(typeof(EItemCategory)))
            {
                if (!cfg.CategoryMultipliers.ContainsKey(name))
                {
                    cfg.CategoryMultipliers[name] = 1;
                    changed = true;
                }
            }
        }

        return changed;
    }

    // Apply stack overrides using the global mod config file (registered on OnPreLoad).
    // This is the method subscribed to GameLifecycle.OnPreLoad.
    private static void ApplyStackOverrides()
    {
        ApplyStackOverridesUsing(_config);
    }

    // Public helper so other systems (Saveable, network) can apply a supplied ModConfig.
    public static void ApplyStackOverridesUsing(ModConfig cfg)
    {
        try
        {
            // Ensure incoming config has CategoryMultipliers populated so lookups below are reliable.
            EnsureCategoryMultipliers(cfg, addEnumKeys: false);

            // Diagnostic: enumerate *all* item definitions (not only storable) to detect categories/types present at this lifecycle point.
            var allDefs = S1API.Items.ItemManager.GetAllItemDefinitions().ToList();
            var categoryCounts = allDefs.GroupBy(d => (EItemCategory)d.Category).ToDictionary(g => g.Key, g => g.Count());
            MelonLogger.Msg($"[Better Stacks] ItemManager total definitions={allDefs.Count}, categories present={string.Join(", ", categoryCounts.Select(kv => kv.Key + "=" + kv.Value))}");

            // Log specifically for categories that appear in config but were missing from the previous summary.
            foreach (var cat in new[] { EItemCategory.Product, EItemCategory.Packaging, EItemCategory.Consumable, EItemCategory.Cash })
            {
                var matches = allDefs.Where(d => (EItemCategory)d.Category == cat).ToList();
                if (matches.Count == 0)
                    MelonLogger.Msg($"[Better Stacks] Diagnostic: no definitions found with category {cat}");
                else
                {
                    var types = matches.Select(m => m.GetType().Name).Distinct();
                    MelonLogger.Msg($"[Better Stacks] Diagnostic: {matches.Count} defs for {cat}, types=[{string.Join(',', types)}], examples=[{string.Join(',', matches.Take(8).Select(m => m.Name + "(" + m.GetType().Name + ")"))}]");
                }
            }

            // Select any item definition that exposes a StackLimit (property or field) so ProductDefinition
            // and other non-storable types that still have StackLimit are processed.
            var defs = allDefs.Where(d => d.GetType().GetProperty("StackLimit") != null || d.GetType().GetField("StackLimit") != null).ToList();
            MelonLogger.Msg($"[Better Stacks] Found {defs.Count} item definitions with StackLimit at ApplyStackOverridesUsing");

            var processedByCategory = new Dictionary<EItemCategory, int>();
            var changedByCategory = new Dictionary<EItemCategory, int>();
            var changedNamesByCategory = new Dictionary<EItemCategory, List<string>>();

            foreach (var def in defs)
            {
                var category = (EItemCategory)def.Category;
                processedByCategory.TryGetValue(category, out var pCount);
                processedByCategory[category] = pCount + 1;

                var defType = def.GetType();

                // Skip 'effect' style Product entries (these represent effect metadata, not stackable items).
                if ((def.Name != null && def.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                    || defType.Name.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MelonLogger.Msg($"[Better Stacks] Skipping non-stackable effect definition: {def.Name} ({defType.Name})");
                    continue;
                }

                var stackProp = defType.GetProperty("StackLimit");
                var stackField = stackProp == null ? defType.GetField("StackLimit") : null;

                // read current stack limit via reflection (support property or field)
                if ((stackProp == null || !stackProp.CanRead) && stackField == null)
                {
                    // nothing we can do for this definition
                    continue;
                }

                int currentStack;
                try
                {
                    if (stackProp != null)
                        currentStack = Convert.ToInt32(stackProp.GetValue(def));
                    else
                        currentStack = Convert.ToInt32(stackField.GetValue(def));
                }
                catch
                {
                    // unexpected value/type — skip this definition
                    MelonLogger.Msg($"[Better Stacks] Skipping {def.Name} ({defType.Name}) — unable to read StackLimit");
                    continue;
                }

                // Store original stack limit so reapplying uses the original base (pre-mod) value.
                if (!_originalStackLimits.ContainsKey(def.ID))
                    _originalStackLimits[def.ID] = currentStack;

                int originalLimit = _originalStackLimits[def.ID];

                // Determine modifier using CategoryMultipliers (keyed by enum name). Fall back to defaults.
                int modifier = 1;
                var key = category.ToString();
                if (cfg.CategoryMultipliers != null && cfg.CategoryMultipliers.TryGetValue(key, out var m))
                    modifier = Math.Max(1, m);
                else if (_config?.CategoryMultipliers != null && _config.CategoryMultipliers.TryGetValue(key, out var m2))
                    modifier = Math.Max(1, m2);

                int newLimit = Math.Max(1, originalLimit * modifier);

                if (newLimit != currentStack)
                {
                    changedByCategory.TryGetValue(category, out var cCount);
                    changedByCategory[category] = cCount + 1;

                    if (!changedNamesByCategory.TryGetValue(category, out var list))
                    {
                        list = new List<string>();
                        changedNamesByCategory[category] = list;
                    }
                    if (list.Count < 10)
                        list.Add($"{def.Name}({originalLimit}->{newLimit})");

                    MelonLogger.Msg($"[Better Stacks] Set {def.Name} ({def.Category}) stack limit from {currentStack} to {newLimit}");

                    // attempt to write back via property or field
                    try
                    {
                        if (stackProp != null && stackProp.CanWrite)
                            stackProp.SetValue(def, newLimit);
                        else if (stackField != null)
                            stackField.SetValue(def, newLimit);
                        else
                            MelonLogger.Msg($"[Better Stacks] Cannot set StackLimit on {def.Name} ({defType.Name}) — member is read-only.");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[Better Stacks] Failed to set StackLimit on {def.Name} ({defType.Name}): {ex.Message}");
                    }
                }
            }

            // Summary logging so we can see which categories were present/changed at OnPreLoad.
            MelonLogger.Msg("[Better Stacks] ApplyStackOverrides summary:");
            foreach (var kv in processedByCategory.OrderBy(k => k.Key.ToString()))
            {
                changedByCategory.TryGetValue(kv.Key, out var changed);
                changedNamesByCategory.TryGetValue(kv.Key, out var samples);
                string sampleStr = samples is null || samples.Count == 0 ? "" : string.Join(", ", samples);
                MelonLogger.Msg($"[Better Stacks]  {kv.Key}: processed={kv.Value}, changed={changed}, examples=[{sampleStr}]");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Better Stacks] ApplyStackOverridesUsing failed: {ex}");
        }
    }

    // Update internal config from a host-provided authoritative config and re-apply overrides.
    public static void UpdateConfigFromHost(ModConfig cfg)
    {
        if (cfg == null) return;
        _config = cfg;
        ApplyStackOverridesUsing(cfg);
    }

    public override void OnUpdate()
    {
        // Drive incoming SteamNetworkLib callbacks / message processing.
        try { NetworkingManager.CurrentAdapter?.ProcessIncomingMessages(); } catch { }

        // Poll MelonPreferences for live edits (e.g. from modsapp) and apply/revert as needed.
        try { PreferencesMapper.PollAndApplyChanges(); } catch { }
    }

    public override void OnDeinitializeMelon()
    {
        // Ensure networking adapter is cleanly shut down.
        NetworkingManager.Shutdown();
    }

    //private static bool SetQuantityPatch(ItemInstance __instance, int quantity) 
    //{
    //    int stackLimit = __instance.StackLimit;
    //    //MelonLogger.Msg($"SetQuantity called on {__instance.Name}, stack limit is {stackLimit}");

    //    if (quantity < 0)
    //    {
    //        MelonLogger.Error("SetQuantity called with negative quantity");
    //        return false;
    //    }
    //    quantity = Math.Min(quantity, stackLimit);
    //    __instance.Quantity = quantity;
    //    __instance.InvokeDataChange();
    //    return false;
    //}

    private static bool ChangeQuantityPatch(ItemInstance __instance, int change)
    {
        int num = __instance.Quantity + change;
        if (num < 0)
        {
            MelonLogger.Error("ChangeQuantity resulted in negative quantity");
            return false;
        }
        __instance.Quantity = num;
        __instance.InvokeDataChange();
        return false;
    }

    public static bool PatchMixingStationCapacity(MixingStation __instance)
    {
        __instance.MixTimePerItem /= _config.MixingStationSpeed;
        __instance.MixTimePerItem = Math.Max(1, __instance.MixTimePerItem);
        __instance.MaxMixQuantity = __instance.MaxMixQuantity * _config.MixingStationCapacity;
        //MelonLogger.Msg($"Set mixing station capacity to {__instance.MaxMixQuantity}");
        return true;
    }

    public static void PatchDryingRackCapacity(DryingRackCanvas __instance, DryingRack rack, bool open)
    {
        //MelonLogger.Msg($"On DryingRackCanvas.SetIsOpen");
        if (rack is not null)
        {
            int desired = _config.DryingRackCapacity * 20;
            if (rack.ItemCapacity != desired)
            {
                rack.ItemCapacity = desired;
                //MelonLogger.Msg($"Set drying rack capacity to {rack.ItemCapacity}");
            }
        }
    }

    //public static bool DeliveryLimitPatch(DeliveryShop __instance)
    //{
    //    int totalStacks = 0;
    //    foreach (ListingEntry? listingEntry in __instance.listingEntries._items)
    //    {
    //        if (listingEntry is null || listingEntry.SelectedQuantity == 0)
    //            continue;

    //        StorableItemDefinition item = listingEntry.MatchingListing.Item;
    //        int stackCapacity = item.StackLimit * GetCapacityModifier(item.Category);
    //        int stacksNeeded = (int) Math.Ceiling((double)listingEntry.SelectedQuantity / stackCapacity);
    //        totalStacks += stacksNeeded;
    //    }

    //    MelonLogger.Msg($"Order need {totalStacks} stacks");
    //    return totalStacks <= DeliveryShop.DELIVERY_VEHICLE_SLOT_CAPACITY;
    //}

    // ListingEntry stack-limit patch removed — handled by S1API.ApplyStackOverrides
    private static int GetCapacityModifier(EItemCategory category)
    {
        var key = category.ToString();
        if (_config?.CategoryMultipliers != null && _config.CategoryMultipliers.TryGetValue(key, out var val))
            return Math.Max(1, val);

        return 1;
    }

    static IEnumerable<CodeInstruction> TranspilerPatch(IEnumerable<CodeInstruction> instructions)
    {
        MelonLogger.Msg("[Better Stacks] Transpiler called");
        foreach (CodeInstruction instruction in instructions)
        {
            MelonLogger.Msg($"Opcode: {instruction.opcode}, Operand: {instruction.operand}");
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 1000f)
                yield return new CodeInstruction(OpCodes.Ldc_R4, (float)5000);
            else
                yield return instruction;
        }
    }


}



public class ModConfig
{
    // Primary dynamic category multipliers keyed by EItemCategory name.
    public Dictionary<string, int> CategoryMultipliers { get; set; } = new Dictionary<string, int>();

    // When true the host may assert authoritative control over this mod's config via HostConfig messages.
    public bool EnableServerAuthoritativeConfig { get; set; } = false;

    public int MixingStationCapacity { get; set; } = 1;
    public int MixingStationSpeed { get; set; } = 3;

    public int DryingRackCapacity { get; set; } = 1;
}