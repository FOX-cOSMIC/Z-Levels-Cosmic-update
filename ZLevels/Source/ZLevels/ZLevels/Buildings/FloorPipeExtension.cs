using Verse;

namespace ZLevels
{
    // The rate of a floor pipe, set in its def (decision DM18). The pipe moves this many items of its chosen resource per game day, from storage on
    // its sending floor to the other end. The value is in ZLevels/Common/Defs/Buildings/FloorPipe.xml.
    public class FloorPipeExtension : DefModExtension
    {
        public int itemsPerDay = 60;
    }
}
