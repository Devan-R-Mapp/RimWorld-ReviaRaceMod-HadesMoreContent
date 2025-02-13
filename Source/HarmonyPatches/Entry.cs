﻿using HarmonyLib;
using ReviaRace.Enums;
using ReviaRace.Genes;
using ReviaRace.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace ReviaRace.HarmonyPatches
{
    [StaticConstructorOnStartup]
    internal static class Entry
    {
        internal static bool NoProjectLimitations { get; set; }
        internal static bool NoCraftLimitation { get; set; }
        internal static BornSettingsEnum BornSettings {get;set;}
        private static readonly Type patchType = typeof(Entry);
        static Entry()
        {
            var harmony = new Harmony("ReviaRace");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_Researcher), nameof(WorkGiver_Researcher.ShouldSkip)),
                postfix: new HarmonyMethod(patchType, nameof(ShouldSkipResearchPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew)),
                postfix: new HarmonyMethod(patchType, nameof(PawnAllowedToStartAnewPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Hediff), nameof(Hediff.PostAdd)),
                postfix: new HarmonyMethod(patchType, nameof(HediffPostAdd)));
            harmony.Patch(AccessTools.Method(typeof(Hediff), nameof(Hediff.PostRemoved)),
                postfix: new HarmonyMethod(patchType, nameof(HediffPostRemove)));
            harmony.Patch(AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) }),
               prefix: new HarmonyMethod(patchType, nameof(PreGeneratePawn)));
            harmony.Patch(AccessTools.Method(typeof(PawnBioAndNameGenerator), "GiveAppropriateBioAndNameTo"),
   prefix: new HarmonyMethod(patchType, nameof(PreGiveAppropriateBioAndNameTo)));
            harmony.Patch(AccessTools.Method(typeof(PawnRelationWorker_Sibling), "GenerateParent"),
                            transpiler: new HarmonyMethod(patchType, nameof(GenerateParentTranspiler)));
            harmony.Patch(AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.AdjustXenotypeForFactionlessPawn)),
   postfix: new HarmonyMethod(patchType, nameof(AdjustXenotypeForFactionlessPawn_Postfix)));
            

            try
            {
                ((Action)(() =>
                {
                    if (LoadedModManager.RunningModsListForReading.Any(x => x.PackageId.Replace("_steam", "").Replace("_copy", "") == "sarg.alphagenes"))
                    {
                        harmony.Patch(AccessTools.Method(typeof(AlphaGenes.Gene_Randomizer), nameof(AlphaGenes.Gene_Randomizer.PostAdd)),
                             transpiler: new HarmonyMethod(patchType, nameof(Gene_Randomizer_Transpiler)));
                    }

                }))();
            }
            catch (TypeLoadException) { }
            
        }


        private static IEnumerable<CodeInstruction> GenerateParentTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        {
            MethodInfo pawnGenerateMI = typeof(PawnGenerator).GetMethod(nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) });
            foreach (var ins in instructions)
            {
                if (ins.Calls(pawnGenerateMI))
                    yield return CodeInstruction.Call(patchType, nameof(FixGenerationRequest));
                yield return ins;
            }
        }

        private static PawnGenerationRequest FixGenerationRequest(PawnGenerationRequest originalRequest)
        {
            if (originalRequest.FixedGender == Gender.Male && (originalRequest.KindDef.defName.StartsWith("Revia") || originalRequest.ForcedXenotype == Defs.XenotypeDef || (originalRequest.Faction?.def?.defName?.StartsWith("Revia") ?? false)))
            {
                originalRequest.KindDef = DefDatabase<PawnKindDef>.GetNamed("Slave");
                originalRequest.ForcedXenotype = null;
                originalRequest.AllowedXenotypes = Defs.XenotypesExcludeRevia;
                originalRequest.Faction = null;
            }
            return originalRequest;
        }

        public static void PreGiveAppropriateBioAndNameTo(Pawn pawn, string requiredLastName, ref FactionDef factionType, bool forceNoBackstory, bool newborn, XenotypeDef xenotype)
        {
            if (factionType.defName.StartsWith("Revia") && pawn.Faction == null && xenotype != Defs.XenotypeDef)
            {
                //Try to change factionType to some other
                Faction nonReviaFaction = null;
                int num = 0;
                do
                {
                    num++;
                    Faction faction;
                    if (Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, false, true, TechLevel.Undefined, false) && !faction.def.defName.StartsWith("Revia"))
                        nonReviaFaction = faction;
                } while (num < 100 && nonReviaFaction == null);
                factionType = nonReviaFaction.def;
            }
        }


        public static void PreGeneratePawn(ref PawnGenerationRequest request)
        {



            if ((request.ForcedXenotype?.Equals(Defs.XenotypeDef) ?? false) || (request.Faction?.def?.defName?.StartsWith("Revia") ?? false))
            {
                request.FixedGender = Gender.Female;
                request.ForcedXenotype = Defs.XenotypeDef;
            }
        }

        private static void HediffPostRemove(Hediff __instance)
        {
            if (__instance.pawn == null) return;
            ReviaBaseGene.RefreshAttackHediffs(__instance.pawn);
        }
        private static void HediffPostAdd(Hediff __instance)
        {
            if (__instance.pawn == null) return;
            ReviaBaseGene.RefreshAttackHediffs(__instance.pawn);
        }
        private static bool CanDoRecipe(Pawn pawn, RecipeDef recipe)
        {
            if ((recipe.defName.StartsWith("Revia") || (recipe?.ProducedThingDef?.defName?.StartsWith("Revia") ?? false)))
                return pawn.IsRevia();
            return true;
        }

        public static void PawnAllowedToStartAnewPostfix(Pawn p, Bill __instance, ref bool __result)
        {
            if (NoCraftLimitation) return;
            RecipeDef recipe = __instance.recipe;

            if (__result && recipe != null)
                __result = CanDoRecipe(p, recipe);
        }

        public static void ShouldSkipResearchPostfix(Pawn pawn, ref bool __result)
        {
            if (NoProjectLimitations) return;
            if (__result) return;
            ResearchProjectDef project = Find.ResearchManager?.currentProj;

            if (project?.defName?.StartsWith("Revia") ?? false) __result = !pawn.IsRevia();
        }

        public static IEnumerable<CodeInstruction> Gene_Randomizer_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo addGeneMI = typeof(Pawn_GeneTracker).GetMethod(nameof(Pawn_GeneTracker.AddGene), new Type[] { typeof(GeneDef), typeof(bool) });
            MethodInfo checkMI = patchType.GetMethod(nameof(GeneCanBeAdded));

            foreach (var instruction in instructions)
            {
                var potentialGeneAdd = instructions.SkipWhile(x => x != instruction).Take(9);
                if (potentialGeneAdd.Last().Calls(addGeneMI))
                {
                    var label = instructions.SkipWhile(x => x != instruction).Select(x => x.labels).FirstOrDefault(x => x != null && x.Count > 0)[0]; ;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return CodeInstruction.LoadField(typeof(Gene), nameof(Gene.pawn));
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 7);
                    yield return new CodeInstruction(OpCodes.Call, checkMI);
                    yield return new CodeInstruction(OpCodes.Brfalse, label);
                }
                yield return instruction;
            }
        }
        public static void AdjustXenotypeForFactionlessPawn_Postfix(Pawn pawn, ref PawnGenerationRequest request, ref XenotypeDef xenotype)
        {
            XenotypeDef xenotypeDef;
            if (pawn.gender == Gender.Male && xenotype == Defs.XenotypeDef)
            {
                int num = 0;
                while (xenotype == Defs.XenotypeDef && num < 100)
                {
                    if (DefDatabase<XenotypeDef>.AllDefs.TryRandomElementByWeight((XenotypeDef x) => x.factionlessGenerationWeight, out xenotypeDef))
                    {
                        xenotype = xenotypeDef;
                    }
                    num++;
                }
                if(xenotype==Defs.XenotypeDef)
                {
                    Log.Error("Tried to change revia xenotype to other, but failed");
                }
            }
        }
        public static bool GeneCanBeAdded(Pawn pawn, GeneDef gene)
        {
            if (ReviaBaseGene.RejectionType == Comps.RejectionType.NoRejection || pawn.gender == Gender.Female) return true; //If there is no rejection we dont care
            if (!typeof(ReviaBaseGene).IsAssignableFrom(gene.geneClass)) return true; // We dont care about not revian genes
            return false;
        }
         
        
        //public static IEnumerable<CodeInstruction> ApplyBirthOutcomeTranspiler(IEnumerable<CodeInstruction> instructions)
        //{

        //    foreach (var ci in instructions)
        //    {
        //        yield return ci;
        //        //if (ci.opcode == OpCodes.Initobj && ci.operand.ToString().Contains("Gender"))
        //        //{
        //        //    yield return new CodeInstruction(OpCodes.Ldarg_S, "geneticMother");
        //        //    yield return new CodeInstruction(OpCodes.Ldloc_S, 15);
        //        //    yield return CodeInstruction.Call(typeof(Entry), nameof(SelectGender));
        //        //    yield return new CodeInstruction(OpCodes.Stloc_S, 15);
        //        //}
        //    }
        //}
       
        


    }
}
