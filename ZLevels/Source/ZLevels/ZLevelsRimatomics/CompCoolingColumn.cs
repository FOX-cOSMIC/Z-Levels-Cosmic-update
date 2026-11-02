using Verse;

namespace ZLevelsRimatomics
{
    // One cooling column. It is a Rimatomics coolant pipe, and it joins the loops that CoolingLoopRegistry pairs.
    public class CompCoolingColumn : ThingComp
    {
        // The Rimatomics pipe comp on this column. A reference-type field, so this class loads without Rimatomics.
        public Rimatomics.CompPipe pipe;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pipe = parent.GetComp<Rimatomics.CompPipe>();
            CoolingLoopRegistry.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            CoolingLoopRegistry.Deregister(this);
            base.PostDeSpawn(map, mode);
        }
    }
}
