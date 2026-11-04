using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // Vanilla JobDriver_SocialRelax reserves its chair with pawn.CurJob. Z-Levels runs this check on a queued job before the job
    // becomes the colonist's current job, when pawn.CurJob may be null or another job. This prefix runs the same steps with the
    // driver's own job. When the chair or the drink cannot be reserved, it releases the job's reservations, as vanilla
    // TryTakeOrderedJob does. When the job is current, the result matches vanilla. The chair call passes the caller's errorOnFailed, so a refused chair is logged only when the caller asks for errors. Vanilla's chair call always logs.
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
            bool reserved = pawn.ReserveSittableOrSpot(cell, job, errorOnFailed);
            if (reserved)
            {
                job.SetTarget(TargetIndex.B, cell);
                bool hasDrink = job.GetTarget(TargetIndex.C).HasThing;
                reserved = !hasDrink || pawn.Reserve(job.GetTarget(TargetIndex.C), job, 1, -1, null, errorOnFailed);
            }
            if (!reserved)
            {
                pawn.ClearReservationsForJob(job);
            }
            __result = reserved;
            return false;
        }
    }
}
