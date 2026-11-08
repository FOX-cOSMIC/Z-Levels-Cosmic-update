using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    // Idea "stair transfer" (docs/mod-comparison/ideas.html; decision D5, answered 2026-11-06: send a chosen list now). A stairs piece's "Transfer
    // across the stairs" button opens this window. It lists the free colonists and the loose items on the stairs' floor, with a checkbox for each.
    // Send walks the checked colonists to the landing cell beside the other end, and marks the checked items for the other floor. An item the
    // other floor does not need is not marked, and Send counts it (decision DM20).
    public class StairTransferWindow : Window
    {
        private const float RowHeight = 26f;
        private const float ButtonHeight = 36f;

        private readonly Building_Stairs stairs;
        private readonly List<Pawn> listedColonists;
        private readonly List<Thing> listedItems;
        private readonly bool[] colonistChecked;
        private readonly bool[] itemChecked;
        private Vector2 scrollPosition;

        public StairTransferWindow(Building_Stairs stairs)
        {
            this.stairs = stairs;
            listedColonists = ColonistsOn(stairs.Map);
            listedItems = ItemsOn(stairs.Map);
            colonistChecked = new bool[listedColonists.Count];
            itemChecked = new bool[listedItems.Count];
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(460f, 540f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            string floor = OtherFloorName(stairs);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, RowHeight), "Check the colonists and items to send to " + floor + ".");

            Rect listRect = new Rect(inRect.x, inRect.y + RowHeight + 8f, inRect.width, inRect.height - RowHeight - 8f - ButtonHeight - 8f);
            float contentHeight = (listedColonists.Count + listedItems.Count + 3) * RowHeight + 8f;
            Rect content = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
            scrollPosition = GUI.BeginScrollView(listRect, scrollPosition, content);
            float y = 0f;
            Widgets.Label(new Rect(0f, y, content.width, RowHeight), "Colonists");
            y += RowHeight;
            for (int i = 0; i < listedColonists.Count; i++)
            {
                Widgets.CheckboxLabeled(new Rect(0f, y, content.width, RowHeight), listedColonists[i].LabelShortCap, ref colonistChecked[i]);
                y += RowHeight;
            }
            y += 8f;
            Widgets.Label(new Rect(0f, y, content.width, RowHeight), "Loose items");
            y += RowHeight;
            for (int i = 0; i < listedItems.Count; i++)
            {
                Widgets.CheckboxLabeled(new Rect(0f, y, content.width, RowHeight), listedItems[i].LabelCap, ref itemChecked[i]);
                y += RowHeight;
            }
            GUI.EndScrollView();

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - ButtonHeight, 220f, ButtonHeight), "Send checked"))
            {
                SendChecked();
            }
        }

        // Sends the checked colonists and items from the window's stairs, and tells the player what was sent.
        private void SendChecked()
        {
            if (stairs == null || !stairs.Spawned)
            {
                Close();
                return;
            }
            List<Pawn> chosenColonists = new List<Pawn>();
            for (int i = 0; i < listedColonists.Count; i++)
            {
                if (colonistChecked[i])
                {
                    chosenColonists.Add(listedColonists[i]);
                }
            }
            List<Thing> chosenItems = new List<Thing>();
            for (int i = 0; i < listedItems.Count; i++)
            {
                if (itemChecked[i])
                {
                    chosenItems.Add(listedItems[i]);
                }
            }
            TransferResult result = Send(stairs, chosenColonists, chosenItems);
            string message = result.ColonistsSent + " colonists sent to " + OtherFloorName(stairs) + ". " + result.ItemsMarked + " items marked for it. "
                + result.ItemsNotNeeded + " checked items were not needed there and were not sent.";
            if (result.ItemsGone > 0)
            {
                message += " " + result.ItemsGone + " checked items were no longer loose on this floor and were not sent.";
            }
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, historical: false);
            Close();
        }

        private static string OtherFloorName(Building_Stairs stairs)
        {
            Building_Stairs other = ZUtils.ZTracker.GetOtherEnd(stairs);
            return other == null ? "the other floor" : FloorsTab.FloorName(ZUtils.ZTracker.GetZIndexFor(other.Map));
        }

        // Opens the window for the stairs piece. The transfer button calls this.
        public static void Open(Building_Stairs stairs)
        {
            Find.WindowStack.Add(new StairTransferWindow(stairs));
        }

        // The free colonists spawned on the floor. A colonist who is downed or dead cannot walk, so is left out.
        public static List<Pawn> ColonistsOn(Map floor)
        {
            return floor.mapPawns.FreeColonistsSpawned.Where(pawn => !pawn.Downed && !pawn.Dead).ToList();
        }

        // The loose items on the floor, by the rule the haul to a floor designator uses.
        public static List<Thing> ItemsOn(Map floor)
        {
            return floor.listerThings.AllThings.Where(Designator_HaulToFloor.IsLooseItem).ToList();
        }

        // The cell beside the other end where a colonist waits: the first standable cell with no building, next to the other end. Falls back to the
        // other end's own cell when no neighbour is free. The same map gives the same cell every time.
        public static IntVec3 LandingCell(Building_Stairs other)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(other.Position, 1.5f, false))
            {
                if (cell.InBounds(other.Map) && cell.Standable(other.Map) && cell.GetEdifice(other.Map) == null)
                {
                    return cell;
                }
            }
            return other.Position;
        }

        // Walks the colonists to the landing cell beside the other end, and marks the items for the other floor. A colonist already there gets no
        // job, and an item the other floor does not need is left unmarked. Returns the counts.
        public static TransferResult Send(Building_Stairs stairs, IEnumerable<Pawn> colonists, IEnumerable<Thing> items)
        {
            TransferResult result = new TransferResult();
            Building_Stairs other = ZUtils.ZTracker.GetOtherEnd(stairs);
            if (other == null)
            {
                return result;
            }
            List<Pawn> sent = CallColonistsTargeter.CallTo(colonists, LandingCell(other), other.Map);
            result.ColonistsSent = sent.Count;
            // An item that is no longer loose on the floor (a colonist picked it up, or it was used, since the window opened) is counted as gone, and
            // is not marked. A despawned item has no map, so it must not reach the marking.
            List<Thing> all = items.Where(item => item != null).ToList();
            List<Thing> loose = all.Where(Designator_HaulToFloor.IsLooseItem).ToList();
            result.ItemsGone = all.Count - loose.Count;
            result.ItemsMarked = Designator_HaulToFloor.MarkItemsForFloor(loose, ZUtils.ZTracker.GetZIndexFor(other.Map));
            result.ItemsNotNeeded = loose.Count - result.ItemsMarked;
            return result;
        }
    }

    // What a transfer did: the colonists sent, the items marked for the other floor, and the items that were not marked (the other floor does not
    // need them, or they are already on it).
    public class TransferResult
    {
        public int ColonistsSent;
        public int ItemsMarked;
        public int ItemsNotNeeded;

        // Checked items that were no longer loose on the floor when Send ran: picked up, stored or used since the window opened.
        public int ItemsGone;
    }
}
