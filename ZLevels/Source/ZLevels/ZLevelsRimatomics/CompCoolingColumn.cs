using Verse;

namespace ZLevelsRimatomics
{
    // One cooling column. Task 3 holds only its coolant pipe comp. Task 4 adds the pairing and the loop registration.
    public class CompCoolingColumn : ThingComp
    {
        // The Rimatomics pipe comp on this column. A reference-type field, so this class loads without Rimatomics.
        public Rimatomics.CompPipe pipe;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pipe = parent.GetComp<Rimatomics.CompPipe>();
        }
    }
}
