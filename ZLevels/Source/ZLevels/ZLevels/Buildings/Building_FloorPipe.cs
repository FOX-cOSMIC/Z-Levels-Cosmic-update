using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // A pipe between two floors (idea "pipes between floors" on docs/mod-comparison/ideas.html; decision D4, answered 2026-11-06). One pipe is built on
    // the lower floor, and its other end stands on the same cell on the floor above (decision DM17). The end the player places is the source. The
    // source moves stored items of its chosen resource to the other end, at the rate in FloorPipeExtension (decision DM18). The resource is chosen
    // in game (decision DM19).
    public class Building_FloorPipe : Building
    {
        // The resource both ends move. Steel until another is chosen.
        public ThingDef Resource;

        // True on the end that sends. The other end receives. FlipDirection swaps the two, so exactly one end sends.
        public bool IsSource = true;

        // The part of an item the source has earned and has not sent yet.
        public float carry;

        private static List<ThingDef> resourceChoices;

        // The resources a pipe can carry: Steel first, then the raw resources with a stack limit above 1, by label (decision DM19).
        public static List<ThingDef> ResourceChoices
        {
            get
            {
                if (resourceChoices == null)
                {
                    ThingDef steel = ThingDefOf.Steel;
                    resourceChoices = new List<ThingDef> { steel };
                    resourceChoices.AddRange(DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(candidate => candidate != steel && candidate.stackLimit > 1 && candidate.thingCategories != null
                            && candidate.thingCategories.Contains(ThingCategoryDefOf.ResourcesRaw))
                        .OrderBy(candidate => candidate.label));
                }
                return resourceChoices;
            }
        }

        // The other end: the pipe at the same cell on the floor above or below. Null when there is none.
        public Building_FloorPipe Partner()
        {
            if (!Spawned)
            {
                return null;
            }
            foreach (Map other in new[] { ZUtils.ZTracker.GetUpperLevel(Map.Tile, Map), ZUtils.ZTracker.GetLowerLevel(Map.Tile, Map) })
            {
                if (other == null || other == Map)
                {
                    continue;
                }
                Building_FloorPipe pipe = Position.GetThingList(other).OfType<Building_FloorPipe>().FirstOrDefault();
                if (pipe != null)
                {
                    return pipe;
                }
            }
            return null;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                return;
            }
            if (Resource == null)
            {
                Resource = ThingDefOf.Steel;
            }
            // The end the player places is the source. Its other end is made on the floor above, unless a pipe stands there already.
            Map above = ZUtils.ZTracker.GetUpperLevel(map.Tile, map);
            if (above == null || above == map || Position.GetThingList(above).OfType<Building_FloorPipe>().Any())
            {
                return;
            }
            Thing made = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(made, Position, above, ThingPlaceMode.Direct))
            {
                return;
            }
            made.SetFaction(Faction);
            if (made is Building_FloorPipe otherEnd)
            {
                otherEnd.IsSource = false;
                otherEnd.Resource = Resource;
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // The two ends are one pipe, so the other end goes with this one (decision DM17).
            Building_FloorPipe partner = Partner();
            base.Destroy(mode);
            if (partner != null && partner.Spawned)
            {
                partner.Destroy(mode);
            }
        }

        public override void TickRare()
        {
            base.TickRare();
            Step(GenTicks.TickRareInterval);
        }

        // One step of the flow, for the given ticks. Only the source moves items, and only when the other end stands on the other floor. The items
        // it earns in the step are counted in carry. The whole items move, and any part that moves nothing is not carried over (decision DM18).
        public void Step(int ticks)
        {
            if (!IsSource || Resource == null || !Spawned || ticks <= 0)
            {
                return;
            }
            Building_FloorPipe receiver = Partner();
            if (receiver == null || !receiver.Spawned)
            {
                return;
            }
            carry += ItemsPerDay() * ticks / (float)GenDate.TicksPerDay;
            int count = (int)carry;
            carry -= count;
            if (count > 0)
            {
                FloorPipeMover.MoveItems(Resource, count, Map, receiver.Map, receiver.Position);
            }
        }

        // The items this pipe moves per game day, from its def (decision DM18).
        public int ItemsPerDay()
        {
            return def.GetModExtension<FloorPipeExtension>()?.itemsPerDay ?? 60;
        }

        // Sets the resource of both ends. The resource gizmo calls this.
        public void SetResource(ThingDef resource)
        {
            Resource = resource;
            Building_FloorPipe partner = Partner();
            if (partner != null)
            {
                partner.Resource = resource;
            }
        }

        // Swaps the sending end, so the other end becomes the source. The direction gizmo calls this.
        public void FlipDirection()
        {
            IsSource = !IsSource;
            Building_FloorPipe partner = Partner();
            if (partner != null)
            {
                partner.IsSource = !partner.IsSource;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!Spawned)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Resource: " + (Resource == null ? "none" : Resource.label),
                defaultDesc = "Choose the resource this pipe moves. Both ends of the pipe move the same one.",
                icon = TexCommand.Install,
                action = ChooseResource
            };
            yield return new Command_Action
            {
                defaultLabel = IsSource ? "Sends from this floor" : "Sends from the other floor",
                defaultDesc = "Flip which end sends. The end that sends takes stored items and moves them to the other end.",
                icon = TexCommand.Install,
                action = FlipDirection
            };
        }

        // Opens the list of resources a pipe can carry.
        private void ChooseResource()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (ThingDef choice in ResourceChoices)
            {
                ThingDef captured = choice;
                options.Add(new FloatMenuOption(captured.LabelCap, () => SetResource(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref Resource, "resource");
            Scribe_Values.Look(ref IsSource, "isSource", true);
            Scribe_Values.Look(ref carry, "carry", 0f);
        }
    }
}
