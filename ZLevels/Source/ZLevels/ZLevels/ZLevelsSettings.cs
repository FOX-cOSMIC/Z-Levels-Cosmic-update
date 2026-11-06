using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace ZLevels
{
    class ZLevelsSettings : ModSettings
    {
        public bool DebugEnabled = false;
        public bool allowZLevelsInfestation = true;
        // Work at this priority or more important is searched on every floor before the colonist does lower-priority work on its own floor
        // (idea 8). RimWorld priority 1 is the most important and 4 the least. 0 turns the pass off.
        public int WorkGiverHighPriorityThreshold = 2;
        public override void ExposeData()
        {
            Scribe_Values.Look<bool>(ref DebugEnabled, "DebugEnabled", false);
            Scribe_Values.Look<bool>(ref allowZLevelsInfestation, "allowZLevelsInfestation", true);
            Scribe_Values.Look<int>(ref WorkGiverHighPriorityThreshold, "workGiverHighPriorityThreshold", 2);
            base.ExposeData();
        }

        // Draw the actual settings window that shows up after selecting Z-Levels in the list
        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("debugEnabledExplanation".Translate(), ref DebugEnabled);
            listingStandard.CheckboxLabeled("allowZLevelsInfestation".Translate(), ref allowZLevelsInfestation);
            float threshold = listingStandard.SliderLabeled("workGiverHighPriorityThresholdLabel".Translate(WorkGiverHighPriorityThreshold.ToString()), WorkGiverHighPriorityThreshold, 0f, 4f, 0.7f, "workGiverHighPriorityThresholdTooltip".Translate());
            WorkGiverHighPriorityThreshold = Mathf.RoundToInt(threshold);
            listingStandard.End();
        }
    }
}
