using System;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Fills the vault pocket with rock, the way the underground levels are filled, but with no caves. Ore lumps are scattered through the rock.
    // The room at the centre is left as open concrete for the exit. The rock types are read from the surface that the entrance stands on.
    public class GenStep_VaultRock : GenStep
    {
        public override int SeedPart => 7731043;

        public override void Generate(Map map, GenStepParams parms)
        {
            Building_VaultEntrance entrance = PocketMapUtility.currentlyGeneratingPortal as Building_VaultEntrance;
            if (entrance == null)
            {
                throw new InvalidOperationException("The vault rock generator ran without a vault entrance.");
            }
            // The pocket's own tile is not a world tile, so the rock types come from the surface tile instead.
            RockNoises.Init(entrance.Map);
            map.regionAndRoomUpdater.Enabled = false;
            try
            {
                IntVec3 centre = map.Center;
                foreach (IntVec3 cell in map.AllCells)
                {
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThick);
                    if (InRoom(cell, centre))
                    {
                        continue;
                    }
                    ThingDef rock = GenStep_RocksFromGridUnderground.RockDefAt(cell);
                    map.terrainGrid.SetTerrain(cell, rock.building.naturalTerrain ?? TerrainDefOf.Concrete);
                    GenSpawn.Spawn(rock, cell, map);
                }
                GenStep_ScatterLumpsMineableUnderground ore = new GenStep_ScatterLumpsMineableUnderground
                {
                    maxValue = float.MaxValue
                };
                ore.countPer10kCellsRange = new FloatRange(16f, 16f);
                ore.Generate(map, parms);
                // The ore lumps grow past the rock they start in, so they can reach the room. The room is cleared after them.
                foreach (IntVec3 cell in map.AllCells)
                {
                    if (!InRoom(cell, centre))
                    {
                        continue;
                    }
                    Building edifice = cell.GetEdifice(map);
                    if (edifice is Mineable)
                    {
                        edifice.Destroy(DestroyMode.Vanish);
                    }
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
            }
            finally
            {
                map.regionAndRoomUpdater.Enabled = true;
            }
        }

        private static bool InRoom(IntVec3 cell, IntVec3 centre)
        {
            return cell.DistanceTo(centre) <= VaultExitSpot.RoomRadius;
        }
    }
}
