using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // The kinds of way between two floors (idea "more ways up" on docs/mod-comparison/ideas.html; decision D3). Each kind has an up piece, which stands
    // on the lower floor, and a down piece, which stands on the floor above it. Stairs and ladders share the classes and the pairing by cell
    // (ZLevelsManager.GetOtherEnd). A large kind is one building with a 2 by 2 footprint, and its partner covers the same four cells on the other
    // floor. Large kinds are not rotatable, so their footprint is the same on both floors (decision DM14).
    public static class WayUpKinds
    {
        // The other half of a way: the down piece of an up piece, and the up piece of a down piece. Null for any def that is not a way between floors.
        public static ThingDef PartnerOf(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }
            if (def == ZLevelsDefOf.ZL_StairsUp) return ZLevelsDefOf.ZL_StairsDown;
            if (def == ZLevelsDefOf.ZL_StairsDown) return ZLevelsDefOf.ZL_StairsUp;
            if (def == ZLevelsDefOf.ZL_SmallLadderUp) return ZLevelsDefOf.ZL_SmallLadderDown;
            if (def == ZLevelsDefOf.ZL_SmallLadderDown) return ZLevelsDefOf.ZL_SmallLadderUp;
            if (def == ZLevelsDefOf.ZL_LargeLadderUp) return ZLevelsDefOf.ZL_LargeLadderDown;
            if (def == ZLevelsDefOf.ZL_LargeLadderDown) return ZLevelsDefOf.ZL_LargeLadderUp;
            if (def == ZLevelsDefOf.ZL_LargeStairsUp) return ZLevelsDefOf.ZL_LargeStairsDown;
            if (def == ZLevelsDefOf.ZL_LargeStairsDown) return ZLevelsDefOf.ZL_LargeStairsUp;
            return null;
        }

        // True for the up piece of any kind.
        public static bool IsUp(ThingDef def)
        {
            return def != null && (def == ZLevelsDefOf.ZL_StairsUp || def == ZLevelsDefOf.ZL_SmallLadderUp
                || def == ZLevelsDefOf.ZL_LargeLadderUp || def == ZLevelsDefOf.ZL_LargeStairsUp);
        }

        // True for the down piece of any kind. The natural hole is not listed: it has no up piece to pair with.
        public static bool IsDown(ThingDef def)
        {
            return def != null && (def == ZLevelsDefOf.ZL_StairsDown || def == ZLevelsDefOf.ZL_SmallLadderDown
                || def == ZLevelsDefOf.ZL_LargeLadderDown || def == ZLevelsDefOf.ZL_LargeStairsDown);
        }

        // True when the kind covers more than one cell.
        public static bool IsLarge(ThingDef def)
        {
            return def != null && (def.size.x > 1 || def.size.z > 1);
        }

        // True when every cell of a large kind's footprint is inside the map, standable, and holds no building on the partner's floor. The placement
        // check asks this, so a large way is never placed without room for its partner (decision DM16).
        public static bool PartnerCellsOpen(ThingDef def, IntVec3 origin, Map partnerMap)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(origin, Rot4.North, def.size).Cells)
            {
                if (!cell.InBounds(partnerMap) || !cell.Standable(partnerMap))
                {
                    return false;
                }
                if (cell.GetThingList(partnerMap).Any(thing => thing.def.category == ThingCategory.Building))
                {
                    return false;
                }
            }
            return true;
        }

        // Makes the partner of a way on the other floor, on the same cells as the way, and returns it. It returns the partner already there when one
        // stands at the same origin, and null when the partner cannot be made, which for a large kind means its cells are not open. When
        // setOutsideTerrain is true, the partner's cells are set to the outside tile. The stairs' own spawn does this, and the teleport paths never
        // did, so they pass false.
        public static Building_Stairs SpawnPartner(Building_Stairs source, Map partnerMap, bool setOutsideTerrain)
        {
            ThingDef partnerDef = PartnerOf(source?.def);
            if (partnerDef == null || partnerMap == null || partnerMap == source.Map)
            {
                return null;
            }
            IntVec3 origin = source.Position;
            Thing existing = origin.GetThingList(partnerMap).FirstOrDefault(thing => thing.def == partnerDef);
            if (existing != null)
            {
                return existing as Building_Stairs;
            }
            if (IsLarge(partnerDef) && !PartnerCellsOpen(partnerDef, origin, partnerMap))
            {
                return null;
            }
            if (setOutsideTerrain)
            {
                foreach (IntVec3 cell in GenAdj.OccupiedRect(origin, Rot4.North, partnerDef.size).Cells)
                {
                    partnerMap.terrainGrid.SetTerrain(cell, ZLevelsDefOf.ZL_OutsideTerrainTwo);
                }
            }
            Thing made = ThingMaker.MakeThing(partnerDef, source.Stuff);
            if (!GenPlace.TryPlaceThing(made, origin, partnerMap, ThingPlaceMode.Direct))
            {
                return null;
            }
            made.SetFaction(source.Faction);
            return made as Building_Stairs;
        }
    }
}
