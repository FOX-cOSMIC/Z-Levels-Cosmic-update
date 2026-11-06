using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 27 (idea "game events to check" on docs/mod-comparison/ideas.html). Each event is tested first. The code changes only where a row
    // fails. Each scenario starts from a command-line flag in RuntimeValidation's dispatch and writes its rows with Record().
    public partial class RuntimeValidation
    {
        // Task 27 c (falling objects). A chunk falls under a constructed roof on the surface. The roof breaks on the landing floor around the
        // impact cell. The other floor keeps its roofs at the blast, and the landing floor keeps the roofs beyond it.
        private int skyStage;
        private int skyDueTick;
        private bool skyDone;
        private Map skySurface;
        private Map skyLower;
        private Map skyLanding;
        private IntVec3 skyCenter;
        private Skyfaller skyChunk;
        private CellRect skyFootprint;
        private CellRect skyBlast;
        private readonly Dictionary<IntVec3, RoofDef> skyBeforeSurface = new Dictionary<IntVec3, RoofDef>();
        private readonly Dictionary<IntVec3, RoofDef> skyBeforeLower = new Dictionary<IntVec3, RoofDef>();

        private void RunSkyfallerRoofTick()
        {
            if (skyDone)
            {
                return;
            }
            try
            {
                if (skyStage == 0)
                {
                    SetUpSkyfallerRoof();
                    skyStage = 1;
                    skyDueTick = Find.TickManager.TicksGame + 2400;
                    return;
                }
                if (skyChunk.Spawned)
                {
                    if (Find.TickManager.TicksGame > skyDueTick)
                    {
                        FinishSkyfallerRoofCheck("the chunk is still falling after 2400 ticks");
                    }
                    return;
                }
                FinishSkyfallerRoofCheck(null);
            }
            catch (Exception exception)
            {
                skyDone = true;
                Record("skyfaller roof", "FAIL: " + exception);
                Log.Error("Z-Levels skyfaller roof validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        // A 3 by 3 open block on the surface gets a constructed roof in a radius of 4 around its centre. The lower floor's roofs are recorded
        // before the chunk is spawned at the centre.
        private void SetUpSkyfallerRoof()
        {
            skySurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, skySurface, 1);
            SetUpLowerStairs(skySurface, tracker, out skyLower);
            RequireFloors(tracker, skySurface, 2);
            IntVec3 corner = FindOpenBlock(skySurface, 3, 3);
            if (!corner.IsValid)
            {
                throw new InvalidOperationException("no open 3 by 3 block on the surface for the chunk");
            }
            skyCenter = corner + new IntVec3(1, 0, 1);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(skyCenter, 4f, useCenter: true))
            {
                if (!cell.InBounds(skySurface))
                {
                    continue;
                }
                skySurface.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                skyBeforeSurface[cell] = skySurface.roofGrid.RoofAt(cell);
                skyBeforeLower[cell] = skyLower.roofGrid.RoofAt(cell);
            }
            ThingDef chunkDef = DefDatabase<ThingDef>.GetNamed("ShipChunkIncoming");
            skyChunk = SkyfallerMaker.SpawnSkyfaller(chunkDef, skyCenter, skySurface);
            skyLanding = skyChunk.Map;
            skyFootprint = skyChunk.OccupiedRect();
            skyBlast = skyFootprint.ExpandedBy(1);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Record("skyfaller roof setup", "INFO: the chunk spawns at " + skyCenter + " on " + (skyLanding == skySurface ? "the surface" : "the lower floor")
                + "; the roof there is " + (skyLanding.roofGrid.RoofAt(skyCenter) == null ? "none" : skyLanding.roofGrid.RoofAt(skyCenter).defName)
                + "; the lower floor's roof there is " + (skyLower.roofGrid.RoofAt(skyCenter) == null ? "none" : skyLower.roofGrid.RoofAt(skyCenter).defName));
        }

        private void FinishSkyfallerRoofCheck(string failure)
        {
            skyDone = true;
            if (failure != null)
            {
                Record("skyfaller roof", "FAIL: " + failure);
                finished = true;
                Root.Shutdown();
                return;
            }
            Map landing = skyLanding;
            Map other = landing == skySurface ? skyLower : skySurface;
            Dictionary<IntVec3, RoofDef> landingBefore = landing == skySurface ? skyBeforeSurface : skyBeforeLower;
            Dictionary<IntVec3, RoofDef> otherBefore = landing == skySurface ? skyBeforeLower : skyBeforeSurface;

            List<IntVec3> footprintRoofs = skyFootprint.Cells.Where(cell => cell.InBounds(landing) && landing.roofGrid.RoofAt(cell) != null).ToList();
            Record("the roof under the chunk breaks on the landing floor", footprintRoofs.Count == 0
                ? "PASS"
                : "FAIL: " + footprintRoofs.Count + " roof cells under the chunk stay, first at " + footprintRoofs[0]);

            List<IntVec3> otherChanged = otherBefore.Keys
                .Where(cell => other.roofGrid.RoofAt(cell) != otherBefore[cell]).ToList();
            Record("only the landing floor's roof breaks", otherChanged.Count == 0
                ? "PASS: the other floor's roofs at the blast are unchanged"
                : "FAIL: " + otherChanged.Count + " roof cells changed on the other floor, first at " + otherChanged[0]);

            List<IntVec3> beyondChanged = landingBefore.Keys
                .Where(cell => !skyBlast.Contains(cell) && landing.roofGrid.RoofAt(cell) != landingBefore[cell]).ToList();
            Record("roofs beyond the blast stay on the landing floor", beyondChanged.Count == 0
                ? "PASS"
                : "FAIL: " + beyondChanged.Count + " roof cells beyond the blast changed, first at " + beyondChanged[0]);

            bool ok = footprintRoofs.Count == 0 && otherChanged.Count == 0 && beyondChanged.Count == 0;
            Record("skyfaller roof", ok ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        // Task 27 b (mood thoughts and alerts that might assume one map). A lover on the lower floor and a lover on the surface are partners. The
        // lower lover's want-to-sleep-together thought must still be active, because the partner counts whatever floor the partner stands on.
        private void RunMoodOtherFloorCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, lower.Center, 6f);
                IntVec3 lowerCell = FindReachableCellNear(lower, lower.Center, new List<IntVec3>());
                IntVec3 surfaceCell = FindReachableCellNear(surface, surface.Center, new List<IntVec3>());
                if (!lowerCell.IsValid || !surfaceCell.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell for a lover on each floor");
                }
                Pawn lowerLover = SpawnPawnAt(lower, lowerCell);
                Pawn surfaceLover = SpawnPawnAt(surface, surfaceCell);
                lowerLover.relations.AddDirectRelation(PawnRelationDefOf.Lover, surfaceLover);
                lowerLover.relations.everSeenByPlayer = true;
                surfaceLover.relations.everSeenByPlayer = true;
                bool onDifferentFloors = lowerLover.MapHeld == lower && surfaceLover.MapHeld == surface;
                Record("the lovers stand on different floors", onDifferentFloors
                    ? "PASS"
                    : "FAIL: the lovers are on " + lowerLover.MapHeld + " and " + surfaceLover.MapHeld);
                bool partners = LovePartnerRelationUtility.ExistingLovePartners(lowerLover, allowDead: false)
                    .Any(relation => relation.otherPawn == surfaceLover);
                Record("the lovers are partners across floors", partners
                    ? "PASS"
                    : "FAIL: the lower lover has no partner on the surface");
                ThoughtState state = DefDatabase<ThoughtDef>.GetNamed("WantToSleepWithSpouseOrLover").Worker.CurrentState(lowerLover);
                Record("the want-to-sleep-together thought is active for the lower lover", state.Active
                    ? "PASS"
                    : "FAIL: the thought is " + state);
                bool ok = onDifferentFloors && partners && state.Active;
                Record("mood other floor", ok ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("mood other floor", "FAIL: " + exception);
                Log.Error("Z-Levels mood other floor validation failed: " + exception);
            }
            finished = true;
            Root.Shutdown();
        }

        // Task 27 a (parties, weddings, rituals and title ceremonies on another floor). A party and a wedding are held on the lower floor, with
        // guests and a couple on that floor. A colonist on the surface is the control. Rituals need Ideology data, which this install lacks, and
        // the title ceremony needs a throne quest, so neither is run here.
        private int evStage;
        private int evDueTick;
        private bool evDone;
        private Map evSurface;
        private Map evLower;
        private Pawn evOrganizer;
        private Pawn evGuest;
        private Pawn evBride;
        private Pawn evGroom;
        private Pawn evSpectator;
        private Lord evParty;
        private Lord evWedding;

        private void RunEventsOtherFloorTick()
        {
            if (evDone)
            {
                return;
            }
            try
            {
                if (evStage == 0)
                {
                    SetUpEventsOtherFloor();
                    evStage = 1;
                    evDueTick = Find.TickManager.TicksGame + 2400;
                    return;
                }
                if (Find.TickManager.TicksGame < evDueTick)
                {
                    return;
                }
                FinishEventsOtherFloor();
            }
            catch (Exception exception)
            {
                evDone = true;
                Record("events other floor", "FAIL: " + exception);
                Log.Error("Z-Levels events other floor validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void SetUpEventsOtherFloor()
        {
            evSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, evSurface, 1);
            SetUpLowerStairs(evSurface, tracker, out evLower);
            RequireFloors(tracker, evSurface, 2);
            CarveAround(evLower, evLower.Center, 9f);
            List<IntVec3> used = new List<IntVec3>();
            IntVec3 partySpot = TakeCellNear(evLower, evLower.Center + new IntVec3(-5, 0, 0), used);
            IntVec3 weddingSpot = TakeCellNear(evLower, evLower.Center + new IntVec3(5, 0, 0), used);
            evOrganizer = SpawnPawnAt(evLower, TakeCellNear(evLower, evLower.Center + new IntVec3(-3, 0, -3), used));
            evGuest = SpawnPawnAt(evLower, TakeCellNear(evLower, evLower.Center + new IntVec3(-3, 0, 3), used));
            evBride = SpawnPawnAt(evLower, TakeCellNear(evLower, evLower.Center + new IntVec3(3, 0, -3), used));
            evGroom = SpawnPawnAt(evLower, TakeCellNear(evLower, evLower.Center + new IntVec3(3, 0, 3), used));
            evSpectator = SpawnPawnAt(evSurface, TakeCellNear(evSurface, evSurface.Center, new List<IntVec3>()));
            evBride.relations.AddDirectRelation(PawnRelationDefOf.Lover, evGroom);
            evParty = LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_Joinable_Party(partySpot, evOrganizer, GatheringDefOf.Party), evLower,
                new List<Pawn> { evOrganizer });
            evWedding = LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_Joinable_MarriageCeremony(evBride, evGroom, weddingSpot), evLower,
                new List<Pawn> { evBride, evGroom });
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Record("events other floor setup", "INFO: a party (organizer " + evOrganizer + ") and a wedding (" + evBride + " and " + evGroom
                + ") start on the lower floor; the guest is " + evGuest + "; the surface control is " + evSpectator);
        }

        private void FinishEventsOtherFloor()
        {
            evDone = true;
            Map partyMap = evParty.Map;
            Map weddingMap = evWedding.Map;
            bool onLower = partyMap == evLower && weddingMap == evLower;
            Record("the party and the wedding start on the lower floor", onLower
                ? "PASS"
                : "FAIL: the party is on " + partyMap + ", the wedding on " + weddingMap);
            Lord guestLord = evGuest.GetLord();
            bool guestJoined = guestLord == evParty || guestLord == evWedding;
            Record("a lower guest joins a gathering on the lower floor", guestJoined
                ? "PASS"
                : "FAIL: the guest is in " + (guestLord == null ? "no lord" : guestLord.LordJob.GetType().Name));
            bool coupleOnLower = evBride.MapHeld == evLower && evGroom.MapHeld == evLower;
            Record("the couple stays on the lower floor", coupleOnLower
                ? "PASS"
                : "FAIL: the bride is on " + evBride.MapHeld + ", the groom on " + evGroom.MapHeld);
            Lord spectatorLord = evSpectator.GetLord();
            bool spectatorOut = spectatorLord != evParty && spectatorLord != evWedding;
            Record("a surface colonist does not join a lower gathering", spectatorOut
                ? "PASS"
                : "FAIL: the surface colonist is in a lower gathering");
            List<Pawn> strays = evParty.ownedPawns.Concat(evWedding.ownedPawns).Where(pawn => pawn.MapHeld != evLower).ToList();
            Record("no gathering pulls a pawn off the lower floor", strays.Count == 0
                ? "PASS"
                : "FAIL: " + strays.Count + " pawns of the lower gatherings are off the lower floor, first " + strays[0]);
            bool ok = onLower && guestJoined && coupleOnLower && spectatorOut && strays.Count == 0;
            Record("events other floor", ok ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        private static IntVec3 TakeCellNear(Map map, IntVec3 near, List<IntVec3> used)
        {
            IntVec3 cell = FindReachableCellNear(map, near, used);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell near " + near + " on " + map);
            }
            used.Add(cell);
            return cell;
        }

        // Task 27 d (thieves and kidnappers carrying the target down before leaving). A thief on the lower floor carries a colonist and holds the
        // exit lord. The thief takes the stairs toward the surface, and the colonist must still be carried when the thief arrives.
        private int thfStage;
        private int thfDueTick;
        private bool thfDone;
        private bool thfArrived;
        private bool thfCarried;
        private Map thfSurface;
        private Map thfLower;
        private Pawn thfThief;
        private Pawn thfTarget;
        private string thfLastTrace = "";

        private void RunThiefCarryTick()
        {
            if (thfDone)
            {
                return;
            }
            try
            {
                if (thfStage == 0)
                {
                    SetUpThiefCarry();
                    thfStage = 1;
                    thfDueTick = Find.TickManager.TicksGame + 9000;
                    return;
                }
                string trace = DescribeJob(thfThief.CurJob) + " on " + thfThief.MapHeld + " at " + thfThief.Position + ", carrying "
                    + (thfThief.carryTracker.CarriedThing == null ? "nothing" : thfThief.carryTracker.CarriedThing.ToString());
                if (trace != thfLastTrace)
                {
                    thfLastTrace = trace;
                    Record("job trace", "INFO: " + trace);
                }
                if (thfThief.MapHeld == thfSurface)
                {
                    thfArrived = true;
                    thfCarried = thfThief.carryTracker.CarriedThing == thfTarget && thfTarget.MapHeld == thfSurface;
                    FinishThiefCarry();
                    return;
                }
                if (Find.TickManager.TicksGame > thfDueTick)
                {
                    FinishThiefCarry();
                }
            }
            catch (Exception exception)
            {
                thfDone = true;
                Record("thief carry", "FAIL: " + exception);
                Log.Error("Z-Levels thief carry validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void SetUpThiefCarry()
        {
            thfSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, thfSurface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(thfSurface, tracker, out thfLower);
            RequireFloors(tracker, thfSurface, 2);
            CarveAround(thfLower, stairs.Position, 6f);
            List<IntVec3> used = new List<IntVec3>();
            Faction thiefFaction = Find.FactionManager.RandomNonHostileFaction(allowNonHumanlike: false);
            if (thiefFaction == null)
            {
                throw new InvalidOperationException("no non-hostile humanlike faction for a thief");
            }
            IntVec3 thiefCell = TakeCellNear(thfLower, stairs.Position + new IntVec3(3, 0, 0), used);
            IntVec3 targetCell = TakeCellNear(thfLower, stairs.Position + new IntVec3(3, 0, 1), used);
            thfThief = PawnGenerator.GeneratePawn(thiefFaction.def.basicMemberKind ?? PawnKindDefOf.Colonist, thiefFaction);
            GenSpawn.Spawn(thfThief, thiefCell, thfLower);
            thfTarget = SpawnPawnAt(thfLower, targetCell);
            thfTarget.DeSpawn(DestroyMode.Vanish);
            if (!thfThief.carryTracker.TryStartCarry(thfTarget))
            {
                throw new InvalidOperationException("the thief could not pick up the colonist");
            }
            LordMaker.MakeNewLord(thiefFaction, new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: true), thfLower, new List<Pawn> { thfThief });
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Record("thief carry setup", "INFO: a thief from " + thiefFaction.Name + " carries " + thfTarget + " on the lower floor with the exit lord; the thief's duty is "
                + (thfThief.mindState.duty == null ? "none" : thfThief.mindState.duty.def.defName));
        }

        private void FinishThiefCarry()
        {
            thfDone = true;
            Record("the thief takes the colonist down the stairs", thfArrived
                ? "PASS: the thief reached the surface"
                : "FAIL: the thief did not reach the surface in 9000 ticks; it is on " + (thfThief.MapHeld == null ? "no map" : thfThief.MapHeld.ToString())
                    + " with " + DescribeJob(thfThief.CurJob));
            Record("the carried colonist reaches the surface with the thief", thfCarried
                ? "PASS"
                : "FAIL: the colonist is on " + (thfTarget.MapHeld == null ? "no map" : thfTarget.MapHeld.ToString()) + ", carried by the thief: "
                    + (thfThief.carryTracker.CarriedThing == thfTarget));
            bool ok = thfArrived && thfCarried;
            Record("thief carry", ok ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        // Task 27 e (children, mech charging and roping animals from another floor). The roping, charging and child jobs take a target that the
        // pawn can reach and reserve. The mod lets a pawn reach a target on another floor by its stairs route (ReachAndReservationPatches.cs).
        // Checked here: a surface colonist reaches and reserves an animal on the lower floor that has a stairs route, and cannot reach one in
        // sealed rock with no route. Mech charging and the child routines need Biotech setups and are not run here.
        private void RunChildAnimalMechCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, stairs.Position, 6f);
                List<IntVec3> used = new List<IntVec3>();
                IntVec3 beside = TakeCellNear(lower, stairs.Position + new IntVec3(3, 0, 0), used);
                IntVec3 routedCell = TakeCellNear(lower, stairs.Position + new IntVec3(3, 0, -2), used);
                Record("reach diagnostic", "INFO: stairs cell walkable " + stairs.Position.Walkable(lower) + "; beside walkable " + beside.Walkable(lower)
                    + "; beside reaches the routed cell " + lower.reachability.CanReach(beside, routedCell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))
                    + "; stairs cell reaches the routed cell " + lower.reachability.CanReach(stairs.Position, routedCell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)));
                IntVec3 pocket = FindIsolatedRockCell(lower, beside);
                CarveAround(lower, pocket, 1.5f);
                bool pocketIsolated = !lower.reachability.CanReach(beside, pocket, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors));
                Record("the pocket has no route from the stairs", pocketIsolated
                    ? "PASS"
                    : "FAIL: the pocket connects to the stairs, so the control is not valid");
                Pawn routedAnimal = GenSpawnWildAnimal(lower, routedCell);
                Pawn pocketAnimal = GenSpawnWildAnimal(lower, pocket);
                Pawn surfaceColonist = SpawnPawnAt(surface, TakeCellNear(surface, surface.Center, new List<IntVec3>()));
                Record("reach diagnostic", "INFO: the surface colonist stands at " + surfaceColonist.Position + " on " + surface
                    + "; the lower floor reaches the animal from those coordinates " + lower.reachability.CanReach(surfaceColonist.Position, routedAnimal, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))
                    + ", from the stairs cell " + lower.reachability.CanReach(stairs.Position, routedAnimal, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)));
                bool reaches = surfaceColonist.CanReach(routedAnimal, PathEndMode.Touch, Danger.Deadly);
                bool reserves = surfaceColonist.CanReserve(routedAnimal);
                bool pocketReached = surfaceColonist.CanReach(pocketAnimal, PathEndMode.Touch, Danger.Deadly);
                Record("a surface colonist reaches an animal on the lower floor by the stairs", reaches
                    ? "PASS"
                    : "FAIL: the surface colonist cannot reach the animal beside the stairs");
                Record("a surface colonist reserves that animal", reserves
                    ? "PASS"
                    : "FAIL: the surface colonist cannot reserve the animal beside the stairs");
                Record("a surface colonist cannot reach an animal in rock with no route", pocketReached
                    ? "FAIL: the surface colonist reaches the animal in the sealed pocket"
                    : "PASS");
                bool ok = pocketIsolated && reaches && reserves && !pocketReached;
                Record("child animal mech", ok ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("child animal mech", "FAIL: " + exception);
                Log.Error("Z-Levels child animal mech validation failed: " + exception);
            }
            finished = true;
            Root.Shutdown();
        }

        private static Pawn GenSpawnWildAnimal(Map map, IntVec3 cell)
        {
            Pawn animal = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Muffalo"), null);
            GenSpawn.Spawn(animal, cell, map);
            return animal;
        }

        // A rock cell 12 or more cells from the start whose 7 by 7 square is all rock, so carving a small pocket there leaves it sealed.
        private static IntVec3 FindIsolatedRockCell(Map map, IntVec3 from)
        {
            for (int dx = -30; dx <= 30; dx += 2)
            {
                for (int dz = -30; dz <= 30; dz += 2)
                {
                    IntVec3 cell = map.Center + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map) || cell.DistanceTo(from) < 12f || !(cell.GetEdifice(map) is Mineable))
                    {
                        continue;
                    }
                    bool sealed7 = true;
                    for (int ox = -3; ox <= 3 && sealed7; ox++)
                    {
                        for (int oz = -3; oz <= 3 && sealed7; oz++)
                        {
                            IntVec3 around = cell + new IntVec3(ox, 0, oz);
                            if (around.InBounds(map) && !(around.GetEdifice(map) is Mineable))
                            {
                                sealed7 = false;
                            }
                        }
                    }
                    if (sealed7)
                    {
                        return cell;
                    }
                }
            }
            throw new InvalidOperationException("no sealed rock cell away from the stairs on the lower floor");
        }
    }
}
