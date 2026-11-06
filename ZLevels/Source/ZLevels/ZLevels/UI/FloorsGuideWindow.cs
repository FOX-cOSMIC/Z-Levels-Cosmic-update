using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    // Idea "in-game guide" (docs/mod-comparison/ideas.html). The guide explains the floors in plain words. The Floors tab's Guide button opens it, and
    // the validation scenario opens it by name. The keys named in the text are the defaults. A player can rebind them in the options.
    public class FloorsGuideWindow : Window
    {
        public const string GuideText =
            "Floors\n" +
            "The surface is the map you start on. The lower floor lies under it, and the upper floor is built over it. Each floor is a separate map, " +
            "and a colony can use all of them.\n\n" +
            "Stairs\n" +
            "Stairs join two floors. Stairs down lead to the floor below, and stairs up lead to the floor above. A colonist walks to the stairs, uses " +
            "them, and comes out at the other end. Select a stairs piece and use View the connected floor to look at the other end.\n\n" +
            "Keys\n" +
            "By default, N moves the view one floor up, and M moves it one floor down. The camera and the selected designator stay where they are " +
            "when you switch.\n\n" +
            "Stay on this floor\n" +
            "Select a colonist and turn on Stay on this floor. The colonist's jobs then stay on the floor where it stands. Turn it off to let the " +
            "colonist work on other floors.\n\n" +
            "Work on this floor\n" +
            "Select colonists and click a stairs piece, then choose Work on this floor. The selected colonists stay on the stairs' floor for 100 seconds.\n\n" +
            "Call colonists here\n" +
            "Select colonists, choose Call colonists here, and pick a standable spot on the floor you are viewing. A colonist on another floor takes the " +
            "stairs first.\n\n" +
            "Floors tab\n" +
            "The Floors tab shows the floor where each colonist sleeps, eats, has fun and meditates, and which floors each work type may use.";

        public FloorsGuideWindow()
        {
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(600f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(inRect, GuideText);
        }

        // Opens the guide. The Floors tab's Guide button calls this, and the validation scenario calls it by name.
        public static void Open()
        {
            Find.WindowStack.Add(new FloorsGuideWindow());
        }
    }
}
