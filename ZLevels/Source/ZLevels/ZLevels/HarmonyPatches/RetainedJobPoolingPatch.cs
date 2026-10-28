using HarmonyLib;
using Verse;
using Verse.AI;

namespace ZLevels
{
    [HarmonyPatch(typeof(JobMaker), nameof(JobMaker.ReturnToPool))]
    public static class RetainedJobPoolingPatch
    {
        public static bool Prefix(Job job)
        {
            if (job == null || Current.ProgramState != ProgramState.Playing || Current.Game == null)
                return true;
            var trackers = ZUtils.ZTracker?.jobTracker;
            if (trackers == null)
                return true;
            foreach (var tracker in trackers.Values)
            {
                if (tracker.activeJobs == null || tracker.activeJobs.Count == 0)
                    continue;
                if (ReferenceEquals(tracker.mainJob, job) || tracker.activeJobs.Contains(job))
                    return false;
            }
            return true;
        }
    }
}
