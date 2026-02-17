using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        // Default path (same as before) can be overridden by first positional argument or --path
        string basePath = @"I:\SteamLibrary\steamapps\common\Schedule I\MelonLoader";
        string findTypesPattern = null;
        string findMembersPattern = null;
        bool outputJson = false;
        bool presetCapacity = false;

        // Simple arg parsing
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrWhiteSpace(a)) continue;
            if ((a == "-h") || (a == "--help"))
            {
                PrintUsage();
                return;
            }
            if (a.StartsWith("--path", StringComparison.OrdinalIgnoreCase))
            {
                var parts = a.Split('=', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    basePath = parts[1];
                else if (i + 1 < args.Length)
                    basePath = args[++i];
                continue;
            }
            if (a == "--find-types" && i + 1 < args.Length)
            {
                findTypesPattern = args[++i];
                continue;
            }
            if (a == "--find-members" && i + 1 < args.Length)
            {
                findMembersPattern = args[++i];
                continue;
            }
            if (a.StartsWith("--find-members=", StringComparison.OrdinalIgnoreCase))
            {
                findMembersPattern = a.Substring("--find-members=".Length);
                continue;
            }
            if (a.StartsWith("--find-types=", StringComparison.OrdinalIgnoreCase))
            {
                findTypesPattern = a.Substring("--find-types=".Length);
                continue;
            }
            if (a == "--json")
            {
                outputJson = true;
                continue;
            }
            if (a == "--preset" && i + 1 < args.Length)
            {
                var p = args[++i];
                if (string.Equals(p, "capacity", StringComparison.OrdinalIgnoreCase))
                    presetCapacity = true;
                continue;
            }
            if (a.StartsWith("--preset=", StringComparison.OrdinalIgnoreCase))
            {
                var p = a.Substring("--preset=".Length);
                if (string.Equals(p, "capacity", StringComparison.OrdinalIgnoreCase))
                    presetCapacity = true;
                continue;
            }
            // Positional base path (first non-flag arg)
            if (!a.StartsWith("--") && basePath == @"I:\SteamLibrary\steamapps\common\Schedule I\MelonLoader")
            {
                basePath = a;
                continue;
            }
        }

        if (presetCapacity && string.IsNullOrEmpty(findMembersPattern))
        {
            // sensible default for capacity-oriented searches
            findMembersPattern = "Capacity|ItemCapacity|SlotCount|StackLimit|Max.*Quantity|SetQuantity|AddItem|ChangeQuantity|MaxMixQuantity|MixTimePerItem";
        }

        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            try
            {
                string simple = new AssemblyName(resolveArgs.Name).Name + ".dll";
                string[] places = new[] {
                    Path.Combine(basePath, "net6", simple),
                    Path.Combine(basePath, "Il2CppAssemblies", simple),
                    Path.Combine(basePath, simple)
                };
                foreach (var p in places)
                {
                    if (File.Exists(p))
                        return Assembly.LoadFrom(p);
                }
            }
            catch { }
            return null;
        };

        string asmPath = Path.Combine(basePath, "Il2CppAssemblies", "Assembly-CSharp.dll");
        if (!File.Exists(asmPath))
        {
            Console.Error.WriteLine("Assembly not found: " + asmPath);
            return;
        }

        Assembly asm;
        try
        {
            asm = Assembly.LoadFrom(asmPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to load assembly: " + ex.Message);
            return;
        }

        // If no search flags provided, preserve original behaviour: list selected enums
        if (string.IsNullOrEmpty(findTypesPattern) && string.IsNullOrEmpty(findMembersPattern))
        {
            var enums = SafeGetTypes(asm)
                        .Where(t => t.IsEnum && (t.Name.Contains("Item") || t.Name.Contains("Category") || t.Name.Contains("Growing") || t.Name.Contains("Grow")));
            foreach (var e in enums)
            {
                Console.WriteLine("Type: " + e.FullName);
                foreach (var n in Enum.GetNames(e))
                    Console.WriteLine("  " + n);
                Console.WriteLine();
            }
            return;
        }

        var results = new List<MatchResult>();
        Regex typeRegex = null, memberRegex = null;
        if (!string.IsNullOrEmpty(findTypesPattern)) typeRegex = new Regex(findTypesPattern, RegexOptions.IgnoreCase);
        if (!string.IsNullOrEmpty(findMembersPattern)) memberRegex = new Regex(findMembersPattern, RegexOptions.IgnoreCase);

        foreach (var t in SafeGetTypes(asm))
        {
            if (typeRegex != null && !typeRegex.IsMatch(t.FullName ?? t.Name))
                continue;

            // If only types were requested, report the type itself (no members required)
            if (typeRegex != null && memberRegex == null)
            {
                results.Add(new MatchResult { TypeFullName = t.FullName, MemberName = "<TYPE>", Kind = t.IsEnum ? "enum" : "type", Signature = "" });
                continue;
            }

            // Search fields
            if (memberRegex != null)
            {
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var f in fields)
                {
                    if (memberRegex.IsMatch(f.Name))
                        results.Add(new MatchResult { TypeFullName = t.FullName, MemberName = f.Name, Kind = "field", Signature = f.FieldType.FullName });
                }

                var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var p in props)
                {
                    if (memberRegex.IsMatch(p.Name))
                        results.Add(new MatchResult { TypeFullName = t.FullName, MemberName = p.Name, Kind = "property", Signature = p.PropertyType.FullName });
                }

                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                {
                    if (memberRegex.IsMatch(m.Name))
                    {
                        var ps = m.GetParameters();
                        var sig = m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name)) + ")";
                        results.Add(new MatchResult { TypeFullName = t.FullName, MemberName = m.Name, Kind = "method", Signature = sig });
                    }
                }
            }
        }

        if (outputJson)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(results, options));
            return;
        }

        // Human readable output grouped by type
        var byType = results.GroupBy(r => r.TypeFullName).OrderBy(g => g.Key);
        foreach (var g in byType)
        {
            Console.WriteLine("Type: " + g.Key);
            foreach (var r in g)
            {
                Console.WriteLine($"  [{r.Kind}] {r.MemberName} { (string.IsNullOrEmpty(r.Signature) ? "" : "- " + r.Signature)}");
            }
            Console.WriteLine();
        }
    }

    static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage: InspectEnum [basePath] [--find-types <regex>] [--find-members <regex>] [--preset capacity] [--json]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  InspectEnum.exe \"C:\\Games\\Schedule I\\MelonLoader\" --find-members \"ItemCapacity|SlotCount|StackLimit\"");
        Console.WriteLine("  InspectEnum.exe --preset=capacity --json");
    }

    class MatchResult
    {
        public string TypeFullName { get; set; }
        public string MemberName { get; set; }
        public string Kind { get; set; }
        public string Signature { get; set; }
    }
}
