using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 35 (idea "in-game guide" on docs/mod-comparison/ideas.html). The guide window is reached by name, so this file builds before the window
    // exists, and the rows fail until it does. The Floors tab's button is not driven: the scenario calls the window's open method, as the button does.
    public partial class RuntimeValidation
    {
        // The guide window's type. A missing type throws, so the row reports it.
        private static Type GuideWindowType()
        {
            return typeof(FloorPolicy).Assembly.GetType("ZLevels.FloorsGuideWindow")
                ?? throw new InvalidOperationException("the mod has no ZLevels.FloorsGuideWindow class");
        }

        // Opens the guide the way the Floors tab's button does, then checks that the window is on the window stack.
        private static string CheckGuideOpens()
        {
            try
            {
                Type guide = GuideWindowType();
                System.Reflection.MethodInfo open = guide.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?? throw new InvalidOperationException("FloorsGuideWindow has no public static Open");
                open.Invoke(null, null);
                return Find.WindowStack.Windows.Any(window => window.GetType() == guide)
                    ? "PASS: the guide window is on the window stack"
                    : "FAIL: Open did not put the guide window on the window stack";
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                return "FAIL: " + (exception.InnerException ?? exception).Message;
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
        }

        // The guide text must name the stairs, the floors, Stay on this floor, Work on this floor, and the two floor keys, N and M.
        private static string CheckGuideText()
        {
            try
            {
                System.Reflection.FieldInfo field = GuideWindowType().GetField("GuideText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?? throw new InvalidOperationException("FloorsGuideWindow has no public static GuideText");
                string text = (string)field.GetValue(null) ?? "";
                List<string> missing = new List<string>();
                foreach (string topic in new[] { "stairs", "floor", "stay on this floor", "work on this floor" })
                {
                    if (text.IndexOf(topic, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        missing.Add(topic);
                    }
                }
                if (!Regex.IsMatch(text, @"\bN\b"))
                {
                    missing.Add("the N key");
                }
                if (!Regex.IsMatch(text, @"\bM\b"))
                {
                    missing.Add("the M key");
                }
                return missing.Count == 0
                    ? "PASS: the text names the stairs, the floors, Stay on this floor, Work on this floor, N and M"
                    : "FAIL: the text does not name " + string.Join(", ", missing);
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
        }

        private void RunFloorsGuideCheck()
        {
            const string label = "floors guide";
            string opened = CheckGuideOpens();
            Record("guide opens", opened);
            string text = CheckGuideText();
            Record("guide text covers stairs, floors, stay on floor, work on floor, and the keys", text);
            Record(label, opened.StartsWith("PASS") && text.StartsWith("PASS") ? "PASS" : "FAIL: a row above failed");
            Root.Shutdown();
        }
    }
}
