using Verse;

namespace ZLevelsRimatomics
{
    // Writes the held cooling log lines on every game tick. Without it, a held line waits for a cooling net to tick, and a column that spawns while the log is
    // silenced can leave its line held with no net to tick. RimWorld creates this component itself, in Game.FillComponents, through the (Game) constructor.
    // It names no type from the Rimatomics mod, so the class still loads when Rimatomics is absent.
    public class CoolingLogComponent : GameComponent
    {
        public CoolingLogComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            Patch_CoolingNet.TickFromComponent();
        }
    }
}
