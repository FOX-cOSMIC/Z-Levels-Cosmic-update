using System;
using Verse;

namespace ZLevels
{
    // Puts a colonist, and optionally a second thing, back on the floor and cell it had before a search. Wrap the loop that moves the
    // colonist in a using statement. Dispose runs when the statement ends, also when the loop throws, so a failed search cannot leave the
    // colonist on another floor (idea "always move the colonist back" on docs/mod-comparison/ideas.html).
    public sealed class FloorRestore : IDisposable
    {
        private readonly Thing thing;
        private readonly Map map;
        private readonly IntVec3 position;
        private readonly Thing otherThing;
        private readonly Map otherMap;
        private readonly IntVec3 otherPosition;

        public FloorRestore(Thing thing, Map map, IntVec3 position)
            : this(thing, map, position, null, null, IntVec3.Invalid)
        {
        }

        public FloorRestore(Thing thing, Map map, IntVec3 position, Thing otherThing, Map otherMap, IntVec3 otherPosition)
        {
            this.thing = thing;
            this.map = map;
            this.position = position;
            this.otherThing = otherThing;
            this.otherMap = otherMap;
            this.otherPosition = otherPosition;
        }

        public void Dispose()
        {
            ZUtils.TeleportThing(thing, map, position);
            if (otherThing != null)
            {
                ZUtils.TeleportThing(otherThing, otherMap, otherPosition);
            }
        }
    }
}
