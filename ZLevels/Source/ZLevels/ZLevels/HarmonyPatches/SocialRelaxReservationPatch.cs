using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // Vanilla JobDriver_SocialRelax reserves its chair with pawn.CurJob. Z-Levels runs this check on a queued job before the job
    // becomes the colonist's current job, so CurJob is null and the chair reservation is refused. This prefix runs the same steps
    // with the driver's own job. When the job is current, the result is the same as vanilla.
    [HarmonyPatch(typeof(JobDriver_SocialRelax), nameof(JobDriver_SocialRelax.TryMakePreToilReservations))]
    public static class JobDriverSocialRelaxReservationPatch
    {
        public static bool Prefix(JobDriver_SocialRelax __instance, bool errorOnFailed, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            Job job = __instance.job;
            IntVec3 cell = IntVec3.Invalid;
            bool hasChair = job.GetTarget(TargetIndex.B).HasThing;
            if (!hasChair || !Toils_Ingest.TryFindFreeSittingSpotOnThing(job.GetTarget(TargetIndex.B).Thing, pawn, out cell))
            {
                cell = job.GetTarget(TargetIndex.B).Cell;
            }
            if (!pawn.ReserveSittableOrSpot(cell, job))
            {
                __result = false;
                return false;
            }
            job.SetTarget(TargetIndex.B, cell);
            bool hasDrink = job.GetTarget(TargetIndex.C).HasThing;
            if (hasDrink && !pawn.Reserve(job.GetTarget(TargetIndex.C), job, 1, -1, null, errorOnFailed))
            {
                __result = false;
                return false;
            }
            __result = true;
            return false;
        }
    }
}
