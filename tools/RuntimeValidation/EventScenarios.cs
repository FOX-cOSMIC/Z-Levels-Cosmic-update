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
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, lower.Center, 6f);
                // The surface origin sits ten cells from the stairs, as the floors tab's does. A rock centre under a roof has no reachable cell,
                // and carving keeps the centre, so the origin is opened as well.
                IntVec3 surfaceOrigin = OnMapCell(surface, stairs.Position + new IntVec3(10, 0, 0));
                CarveAround(surface, surfaceOrigin, 6f);
                OpenSearchOrigin(surface, surfaceOrigin);
                IntVec3 lowerCell = FindReachableCellNear(lower, lower.Center, new List<IntVec3>());
                IntVec3 surfaceCell = FindReachableCellNear(surface, surfaceOrigin, new List<IntVec3>());
                if (!lowerCell.IsValid || !surfaceCell.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell for a lover on each floor; lower " + DescribeSearchOrigin(lower, lower.Center)
                        + "; surface " + DescribeSearchOrigin(surface, surfaceOrigin));
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
            // FindReachableCellNear measures reachability from near, so an origin off the map can never succeed.
            if (!near.InBounds(map))
            {
                throw new InvalidOperationException("the origin " + near + " is off " + map + "; pick a cell on the map");
            }
            IntVec3 cell = FindReachableCellNear(map, near, used);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell near " + near + " on " + map + "; " + DescribeSearchOrigin(map, near));
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

        // Task 28 (fighting across floors), with decision D6 answered (a) on 2026-11-07: the rule stays, so a shot across solid ground is refused.
        // Two checks around the stairs cell L. First, a hostile pawn on the lower floor shoots at a colonist on the surface. The line of fire from
        // the lower floor crosses solid ground, so the shot must be refused and the colonist unhurt. The shooter must stay on the lower floor, on its
        // attack job, for the whole window, so the refusal is the rule and not a shooter that gave up. Second, a colonist on the lower floor starts a
        // melee attack on a hostile pawn on the surface, diagonally across the stairs cell. The attack should be refused.
        // The two checks run one after the other, so the surface hostile cannot interfere with the shot target.
        private int fightStage;
        private int fightDueTick;
        private int fightHits;
        private int fightJobLost;
        private int fightFirstHitTick;
        private int fightShotStart;
        private float fightTargetLast;
        private bool fightDone;
        private bool fightShotRefused;
        private float fightTargetBefore;
        private float fightEnemyBefore;
        private Map fightSurface;
        private Map fightLower;
        private IntVec3 fightStairs;
        private Pawn fightShooter;
        private Pawn fightTarget;
        private Pawn fightMelee;
        private Pawn fightEnemy;
        private bool fightMeleeSeen;
        private bool fightMeleeRefused;
        private int fightControlDue;
        private float fightControlBefore;
        private Pawn fightControlColonist;
        private Pawn fightControlEnemy;
        private int fightControlStart;
        private List<string> fightControlJobs = new List<string>();
        private bool fightMeleeStartedAtOnce;
        private bool fightControlStartedAtOnce;
        private string fightLineFound = "nothing";
        private int fightOffFloor;

        private void RunCrossFloorFightTick()
        {
            if (fightDone)
            {
                return;
            }
            try
            {
                if (fightStage == 0)
                {
                    SetUpShotAcrossStairs();
                    fightStage = 1;
                    return;
                }
                if (fightStage == 1)
                {
                    SampleShot();
                    if (Find.TickManager.TicksGame >= fightDueTick)
                    {
                        FinishShotAcrossStairs();
                        fightStage = 2;
                    }
                    return;
                }
                if (fightStage == 2)
                {
                    SetUpMeleeAcrossStairs();
                    fightStage = 3;
                    return;
                }
                if (fightStage == 3)
                {
                    if (fightMelee.CurJob != null && fightMelee.CurJob.def == JobDefOf.AttackMelee)
                    {
                        fightMeleeSeen = true;
                    }
                    if (Find.TickManager.TicksGame >= fightDueTick)
                    {
                        FinishMeleeAcrossStairs();
                        fightStage = 4;
                    }
                    return;
                }
                if (fightStage == 4)
                {
                    SetUpSameFloorMelee();
                    fightStage = 5;
                    return;
                }
                if (fightStage == 5)
                {
                    SampleControlJob();
                    if (Find.TickManager.TicksGame >= fightControlDue)
                    {
                        FinishSameFloorMelee();
                    }
                    return;
                }
            }
            catch (Exception exception)
            {
                fightDone = true;
                Record("cross floor fight", "FAIL: " + exception);
                Log.Error("Z-Levels cross floor fight validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void SetUpShotAcrossStairs()
        {
            fightSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, fightSurface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(fightSurface, tracker, out fightLower);
            RequireFloors(tracker, fightSurface, 2);
            fightStairs = stairs.Position;
            CarveAround(fightLower, fightStairs, 6f);
            // SetUpLowerStairs takes the first open block, and this run put the stairs at (2, 0, 2), next to the corner. The shot's cells
            // therefore go toward the map centre, so they stay on the map.
            int inward = fightStairs.x < fightSurface.Center.x ? 1 : -1;
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy == null)
            {
                throw new InvalidOperationException("no enemy humanlike faction for the shooter");
            }
            List<IntVec3> used = new List<IntVec3>();
            fightShooter = PawnGenerator.GeneratePawn(enemy.def.basicMemberKind ?? PawnKindDefOf.Colonist, enemy);
            IntVec3 shooterCell = TakeCellNear(fightLower, fightStairs + new IntVec3(2 * inward, 0, 0), used);
            GenSpawn.Spawn(fightShooter, shooterCell, fightLower);
            // A hostile pawn with no lord may walk off a map by its own choice, so the defend-point lord holds the shooter at its cell. The wander
            // radius is one cell: with three, run 20261106-162135 ended with the shooter on the surface.
            LordMaker.MakeNewLord(enemy, new LordJob_DefendPoint(shooterCell, wanderRadius: 1f, addFleeToil: false), fightLower, new List<Pawn> { fightShooter });
            fightShooter.equipment.DestroyAllEquipment();
            ThingWithComps revolver = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Revolver"));
            fightShooter.equipment.AddEquipment(revolver);
            fightLineFound = ClearShotLine(inward);
            fightTarget = SpawnPawnAt(fightSurface, TakeCellNear(fightSurface, fightStairs + new IntVec3(4 * inward, 0, 0), used));
            fightTargetBefore = fightTarget.health.summaryHealth.SummaryHealthPercent;
            fightDueTick = Find.TickManager.TicksGame + 1800;
            fightShotStart = Find.TickManager.TicksGame;
            fightTargetLast = fightTargetBefore;
            CheckOpenAirShot();
            CheckHostileFindsColonist(tracker);
            // The shooter holds its cell on an attack job. AttackStatic stops the pawn's movement at its start, so the hostile does not walk
            // up the stairs to reach the colonist, and every shot comes from the lower floor.
            Job hold = JobMaker.MakeJob(JobDefOf.AttackStatic, fightTarget);
            hold.maxNumStaticAttacks = 1000;
            fightShooter.jobs.StartJob(hold, JobCondition.InterruptForced);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Record("cross floor fight setup", "INFO: the shooter from " + enemy.Name + " stands on the lower floor at " + fightShooter.Position
                + ", the colonist on the surface at " + fightTarget.Position + ", the stairs at " + fightStairs
                + "; the surface line held " + fightLineFound);
        }

        // One frame of the shot phase: whether the shooter is still on the lower floor and on its attack job, and the frame when the colonist first
        // loses health.
        private void SampleShot()
        {
            if (fightShooter.MapHeld != fightLower)
            {
                fightOffFloor++;
            }
            if (fightShooter.CurJob == null || fightShooter.CurJob.def != JobDefOf.AttackStatic)
            {
                fightJobLost++;
            }
            float health = fightTarget.health.summaryHealth.SummaryHealthPercent;
            if (health < fightTargetLast - 0.0001f)
            {
                if (fightHits == 0)
                {
                    fightFirstHitTick = Find.TickManager.TicksGame - fightShotStart;
                }
                fightHits++;
            }
            fightTargetLast = health;
        }

        private void FinishShotAcrossStairs()
        {
            bool hit = fightHits > 0;
            // The refusal counts only if the shooter held its cell on the lower floor, on its attack job, the whole time. Otherwise a shooter that
            // gave up would show no hit as well.
            bool stayed = fightOffFloor == 0 && fightJobLost == 0;
            // The line of fire the shooter's verb computes from the lower floor, and the surface ground under the shooter's cell. The rule must refuse it.
            bool lineRefused = false;
            string lineOfFire = "no line of fire checked";
            if (fightShooter.Spawned)
            {
                Verb shotVerb = fightShooter.equipment.PrimaryEq.PrimaryVerb;
                bool lineClear = shotVerb.TryFindShootLineFromTo(fightShooter.Position, fightTarget, out ShootLine shootLine);
                lineRefused = !lineClear;
                lineOfFire = "the line of fire from the lower floor is " + (lineClear ? "clear" : "refused") + "; the surface ground under the shooter is "
                    + fightSurface.terrainGrid.TerrainAt(fightShooter.Position).defName;
            }
            string shooterState = fightShooter.Spawned
                ? "the shooter is on " + fightShooter.MapHeld + " at " + fightShooter.Position
                : "the shooter is not spawned";
            string floors = fightOffFloor == 0 ? "the shooter stayed on the lower floor" : "the shooter was on another floor for " + fightOffFloor + " frames";
            string jobs = fightJobLost == 0 ? "it held its attack job" : "it lost its attack job for " + fightJobLost + " frames";
            string firstHit = fightHits == 0 ? "no hit" : fightHits + " frames with a hit, the first " + fightFirstHitTick + " ticks in";
            string detail = firstHit + "; " + floors + "; " + jobs + "; " + shooterState + "; " + lineOfFire + "; the surface line held " + fightLineFound;
            fightShotRefused = !hit && stayed && lineRefused;
            Record("a shot from the lower floor across solid ground is refused, as the rule says", fightShotRefused
                ? "PASS: the colonist was not hit; " + detail
                : "FAIL: " + (hit ? "the colonist lost health from the lower floor, so the shot got through"
                    : !stayed ? "the shooter left the lower floor or its attack job, so the refusal proves nothing"
                    : "the line of fire from the lower floor was not refused, or the shooter was not spawned") + "; " + detail);
            // A colonist that the shots killed has already left the map, so only pawns that are still spawned are despawned here.
            if (fightShooter.Spawned) fightShooter.DeSpawn(DestroyMode.Vanish);
            if (fightTarget.Spawned) fightTarget.DeSpawn(DestroyMode.Vanish);
        }

        // The shot through open air. The surface cells on the line from the stairs to the colonist, other than its two ends, are made open air
        // for the check. The verb of the shooter's revolver is then asked for its line, and the cells are put back in the finally block. The
        // rule refuses every line between levels, so the line must be refused. The row fails when the line is allowed.
        private bool fightOpenAirRefused;

        private void CheckOpenAirShot()
        {
            Verb verb = fightShooter.TryGetAttackVerb(fightTarget, allowManualCastWeapons: true);
            if (verb == null)
            {
                fightOpenAirRefused = false;
                Record("a shot from the lower floor through open air is refused, as the rule says", "FAIL: the shooter has no attack verb at the colonist");
                return;
            }
            Dictionary<IntVec3, TerrainDef> ground = new Dictionary<IntVec3, TerrainDef>();
            bool allowed;
            bool canHit;
            try
            {
                foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(fightStairs, fightTarget.Position))
                {
                    if (cell == fightStairs || cell == fightTarget.Position || !cell.InBounds(fightSurface))
                    {
                        continue;
                    }
                    ground[cell] = cell.GetTerrain(fightSurface);
                    fightSurface.terrainGrid.SetTerrain(cell, ZLevelsDefOf.ZL_OutsideTerrain);
                }
                allowed = verb.TryFindShootLineFromTo(fightShooter.Position, new LocalTargetInfo(fightTarget), out ShootLine line);
                // CanHitTarget is the gate that starts a shot, so it is asked too. It starts nothing.
                canHit = verb.CanHitTarget(new LocalTargetInfo(fightTarget));
            }
            finally
            {
                foreach (KeyValuePair<IntVec3, TerrainDef> cell in ground)
                {
                    fightSurface.terrainGrid.SetTerrain(cell.Key, cell.Value);
                }
            }
            fightOpenAirRefused = ground.Count > 0 && !allowed && !canHit;
            string detail = ground.Count + " open cells on the line; the line was " + (allowed ? "allowed" : "refused") + "; CanHitTarget " + canHit;
            Record("a shot from the lower floor through open air is refused, as the rule says", fightOpenAirRefused
                ? "PASS: the shot is refused; " + detail
                : "FAIL: " + (ground.Count == 0 ? "the line has no cells between the stairs and the colonist" : "the shot would get through levels") + "; " + detail);
        }

        // The search for a colonist on another floor. The colony AI's job giver is asked for a job for the hostile on the lower floor. Vanilla
        // searches one map, so the surface colonist is found only by the attack loop in CombatPatches.cs. The row fails when no job goes to the
        // colonist. The tracker entry the search leaves is reset afterwards.
        private bool fightFoundColonist;

        private void CheckHostileFindsColonist(ZLevelsManager tracker)
        {
            // TryGiveJob is protected, so it is reached through Traverse. The Harmony patches on it still run. The target is the one the tracker
            // holds for the hostile. It may be any colonist on the surface, since the search takes the first one it finds.
            JobGiver_AIFightEnemies giver = new JobGiver_AIFightEnemies();
            Job found = HarmonyLib.Traverse.Create(giver).Method("TryGiveJob", new object[] { fightShooter }).GetValue<Job>();
            Thing sought = null;
            if (found != null && tracker.jobTracker != null && tracker.jobTracker.TryGetValue(fightShooter, out JobTracker record))
            {
                sought = record.targetDest.Thing;
            }
            tracker.ResetJobTrackerFor(fightShooter);
            Pawn colonist = sought as Pawn;
            fightFoundColonist = colonist != null && colonist.Faction == Faction.OfPlayer && colonist.MapHeld == fightSurface;
            string target = sought == null ? "none" : sought.LabelShort + " on " + sought.MapHeld;
            Record("a hostile on the lower floor finds a colonist on the surface, as the search should", fightFoundColonist
                ? "PASS: the search sends the hostile after a colonist on the surface, " + target + "; the first job is " + DescribeJob(found)
                : "FAIL: no job sends the hostile after a colonist on the surface; the target is " + target + "; the first job is " + DescribeJob(found));
        }

        // Clears the surface cells one cell either side of the line from the stairs to the shot's target, and returns what was there. A rock or
        // a tree on that line would stop the shot. That is terrain on the colony's map, not the cross-floor shot the scenario tests.
        private string ClearShotLine(int inward)
        {
            List<string> found = new List<string>();
            for (int k = 1; k <= 5; k++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    IntVec3 cell = fightStairs + new IntVec3(k * inward, 0, dz);
                    if (!cell.InBounds(fightSurface))
                    {
                        continue;
                    }
                    foreach (Thing thing in fightSurface.thingGrid.ThingsAt(cell).ToList())
                    {
                        if (thing is Pawn)
                        {
                            continue;
                        }
                        found.Add(thing.def.defName + " at " + cell);
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            return found.Count == 0 ? "nothing" : string.Join(", ", found);
        }

        private void SetUpMeleeAcrossStairs()
        {
            List<IntVec3> used = new List<IntVec3>();
            fightMelee = SpawnPawnAt(fightLower, TakeCellNear(fightLower, fightStairs + new IntVec3(1, 0, 1), used));
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy == null)
            {
                throw new InvalidOperationException("no enemy humanlike faction for the melee target");
            }
            fightEnemy = PawnGenerator.GeneratePawn(enemy.def.basicMemberKind ?? PawnKindDefOf.Colonist, enemy);
            GenSpawn.Spawn(fightEnemy, TakeCellNear(fightSurface, fightStairs + new IntVec3(-1, 0, -1), used), fightSurface);
            fightEnemyBefore = fightEnemy.health.summaryHealth.SummaryHealthPercent;
            fightMelee.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, fightEnemy), JobCondition.InterruptForced);
            fightMeleeStartedAtOnce = fightMelee.CurJob != null && fightMelee.CurJob.def == JobDefOf.AttackMelee;
            fightDueTick = Find.TickManager.TicksGame + 1200;
            Record("cross floor fight melee setup", "INFO: the colonist on the lower floor at " + fightMelee.Position + " starts a melee attack on "
                + fightEnemy.Name + " on the surface at " + fightEnemy.Position + ", across the stairs cell " + fightStairs);
        }

        private void FinishMeleeAcrossStairs()
        {
            float after = fightEnemy.health.summaryHealth.SummaryHealthPercent;
            bool damaged = Math.Abs(after - fightEnemyBefore) >= 0.0001f;
            bool taken = fightMeleeStartedAtOnce || fightMeleeSeen;
            fightMeleeRefused = !fightEnemy.Dead && !damaged && !taken;
            Record("melee across the stairs cell is refused, as it should", fightMeleeRefused
                ? "PASS: the attack job was never taken and the target took no damage; the colonist's job is " + DescribeJob(fightMelee.CurJob)
                    + " on " + fightMelee.MapHeld
                : "FAIL: the attack job was taken or the target took damage across the stairs cell; the colonist is on " + fightMelee.MapHeld);
        }

        // A control for the melee row, on the lower floor: a colonist and a hostile stand on the same floor, next to each other, and the colonist
        // starts the same attack job. If this does not land a hit, the refusal row says nothing about the stairs.
        private void SetUpSameFloorMelee()
        {
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            if (enemy == null)
            {
                throw new InvalidOperationException("no enemy humanlike faction for the control hostile");
            }
            int inward = fightStairs.x < fightLower.Center.x ? 1 : -1;
            int inwardZ = fightStairs.z < fightLower.Center.z ? 1 : -1;
            List<IntVec3> used = new List<IntVec3>();
            IntVec3 attackerCell = TakeCellNear(fightLower, fightStairs + new IntVec3(3 * inward, 0, 3 * inwardZ), used);
            IntVec3 hostileCell = IntVec3.Invalid;
            foreach (IntVec3 step in new[] { new IntVec3(0, 0, 1), new IntVec3(1, 0, 0), new IntVec3(0, 0, -1), new IntVec3(-1, 0, 0) })
            {
                IntVec3 candidate = attackerCell + step;
                if (candidate.InBounds(fightLower) && candidate.Standable(fightLower) && candidate.GetEdifice(fightLower) == null
                    && candidate.GetFirstItem(fightLower) == null)
                {
                    hostileCell = candidate;
                    break;
                }
            }
            if (!hostileCell.IsValid)
            {
                throw new InvalidOperationException("no open cell next to the control colonist at " + attackerCell);
            }
            fightControlColonist = SpawnPawnAt(fightLower, attackerCell);
            fightControlEnemy = PawnGenerator.GeneratePawn(enemy.def.basicMemberKind ?? PawnKindDefOf.Colonist, enemy);
            GenSpawn.Spawn(fightControlEnemy, hostileCell, fightLower);
            fightControlBefore = fightControlEnemy.health.summaryHealth.SummaryHealthPercent;
            fightControlColonist.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee, fightControlEnemy), JobCondition.InterruptForced);
            fightControlStartedAtOnce = fightControlColonist.CurJob != null && fightControlColonist.CurJob.def == JobDefOf.AttackMelee;
            fightControlStart = Find.TickManager.TicksGame;
            fightControlDue = fightControlStart + 600;
            string weapon = fightControlEnemy.equipment.Primary == null ? "no weapon" : fightControlEnemy.equipment.Primary.Label;
            Record("same floor melee control setup", "INFO: the colonist at " + attackerCell + " starts a melee attack on " + fightControlEnemy.Name
                + " at " + hostileCell + ", both on the lower floor; the hostile has " + weapon);
        }

        // Records each job the control colonist holds, with the tick, counted from the start of the control, when it first appears.
        private void SampleControlJob()
        {
            string job = fightControlColonist.CurJob == null ? "none" : fightControlColonist.CurJob.def.defName;
            if (!fightControlJobs.Any(entry => entry.StartsWith(job + "@", StringComparison.Ordinal)))
            {
                fightControlJobs.Add(job + "@" + (Find.TickManager.TicksGame - fightControlStart));
            }
        }

        private void FinishSameFloorMelee()
        {
            fightDone = true;
            float after = fightControlEnemy.health.summaryHealth.SummaryHealthPercent;
            bool landed = after < fightControlBefore;
            bool taken = fightControlStartedAtOnce || fightControlJobs.Any(entry => entry.StartsWith("AttackMelee@", StringComparison.Ordinal));
            string seen = string.Join(", ", fightControlJobs);
            Record("melee on one floor is taken, as a control", taken
                ? "PASS: the attack job was taken; the hostile " + (landed ? "lost health" : "took no damage") + "; jobs seen: " + seen
                : "FAIL: the attack job was never taken on one floor; jobs seen: " + seen + "; the colonist's job is " + DescribeJob(fightControlColonist.CurJob));
            bool ok = fightShotRefused && fightOpenAirRefused && fightFoundColonist && fightMeleeRefused && taken;
            Record("cross floor fight", ok ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }
    }
}
