using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace BetterStacks.Patches
{
    public static class TranspilerPatches
    {
        public static IEnumerable<CodeInstruction> TranspilerPatch(IEnumerable<CodeInstruction> instructions)
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
}