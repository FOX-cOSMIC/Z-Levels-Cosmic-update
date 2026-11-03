using Verse;

namespace ZLevelsRimatomics
{
    // One cooling column. It is a Rimatomics coolant pipe, and it joins the loops that CoolingLoopRegistry pairs.
    public class CompCoolingColumn : ThingComp
    {
        // The Rimatomics pipe comp on this column. A reference-type field, so this class loads without Rimatomics.
        public Rimatomics.CompPipe pipe;

        // The game tick at which the column spawned, or null when no game tick could be read. The no-net notice waits for a grace period after this tick. The tick
        // is not saved, so a load sets it again, and each load restarts the grace period.
        public int? SpawnedAtTick;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SpawnedAtTick = Find.TickManager?.TicksGame;
            pipe = parent.GetComp<Rimatomics.CompPipe>();
            if (pipe == null)
            {
                // Without a pipe the column cannot join a net, so it stays out of the registry. The line goes through the gate that the other lines use.
                Patch_CoolingNet.LogMissingPipe(this);
                return;
            }
            CoolingLoopRegistry.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            CoolingLoopRegistry.Deregister(this);
            base.PostDeSpawn(map, mode);
        }
    }
}
