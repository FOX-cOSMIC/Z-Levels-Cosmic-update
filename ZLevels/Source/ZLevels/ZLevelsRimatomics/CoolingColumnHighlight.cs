using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ZLevelsRimatomics
{
    // Outlines the partner cells of the viewed level: green where the level also holds a usable cooling column, amber where it does not.
    // Map.MapUpdate calls DynamicDrawManager.DrawDynamicThings only for the map in view, inside its draw block, so this postfix runs once per
    // frame for that map. An exception would reach the game's draw loop, so the postfix catches it and logs each distinct failure once per map.
    [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
    public static class CoolingColumnHighlight
    {
        private static readonly Color PairedColor = new Color(0.25f, 0.9f, 0.25f);
        private static readonly Color UnpairedColor = new Color(1f, 0.75f, 0f);
        // The material and queue that GenDraw.DrawFieldEdges uses for its outline. MaterialPool.MatFrom cannot find this texture by its path,
        // so the texture comes from the material that MatLoader loads, as GenDraw does.
        private const string FieldEdgeMaterialPath = "Misc/FieldEdge";
        private const int OutlineRenderQueue = 2900;
        private static Texture2D edgeTexture;
        private static InvalidOperationException edgeTextureFault;
        private static readonly List<IntVec3> pairedCells = new List<IntVec3>();
        private static readonly List<IntVec3> unpairedCells = new List<IntVec3>();
        // The failures already logged on each map. A failure is one exception type with one message. The same failure on the same map logs once,
        // and a different failure on the same map logs again.
        private static readonly ConditionalWeakTable<Map, HashSet<string>> loggedFailures = new ConditionalWeakTable<Map, HashSet<string>>();
        // The outcome of the last frame that ran for a map, for the validation harness. LastMap is the map that frame ran for. FinishedLastFrame is
        // true when the pass ran to its end. PlanesLastFrame counts each Graphics.DrawMesh call in that frame. DrawnCellsLastFrame lists the cells
        // that drew at least one plane, so the harness compares the cells themselves and not only their count.
        public static Map LastMap;
        public static bool FinishedLastFrame;
        public static int PlanesLastFrame;
        public static readonly List<IntVec3> DrawnCellsLastFrame = new List<IntVec3>();

        // The field-edge texture, as GenDraw.DrawFieldEdges takes it: the main texture of the material that MatLoader loads for Misc/FieldEdge.
        // When the path does not load, MatLoader returns BaseContent.BadMat, the placeholder, and warns on every call. So the failure is kept, and
        // later frames throw it again without calling MatLoader. Postfix logs it once for each map.
        private static Texture2D FieldEdgeTexture()
        {
            if (edgeTexture != null) return edgeTexture;
            if (edgeTextureFault != null) throw edgeTextureFault;
            Material material = MatLoader.LoadMat(FieldEdgeMaterialPath);
            var texture = material.mainTexture as Texture2D;
            if (ReferenceEquals(material, BaseContent.BadMat) || texture == null || ReferenceEquals(texture, BaseContent.BadTex))
            {
                edgeTextureFault = new InvalidOperationException("the outline material " + FieldEdgeMaterialPath
                    + " did not load its field-edge texture, so MatLoader gave the placeholder or no texture");
                throw edgeTextureFault;
            }
            edgeTexture = texture;
            return texture;
        }

        // One plane per open side, drawn with Graphics.DrawMesh. GenDraw.DrawFieldEdges draws the same planes with Graphics.DrawMeshInstanced.
        // With that call, two of five look runs lost the GPU device (20261102-215009 and 20261102-215926), so this draw does not use instancing.
        // The sides come from OutlineRule, which skips a side that leads off the map or onto another cell of the list, as DrawFieldEdges does.
        // Each cell that draws at least one plane is added to DrawnCellsLastFrame.
        private static void DrawOutline(List<IntVec3> cells, Color color, Map map)
        {
            if (cells.Count == 0) return;
            Material material = MaterialPool.MatFrom(FieldEdgeTexture(), ShaderDatabase.Transparent, color, OutlineRenderQueue);
            foreach (IntVec3 cell in cells)
            {
                Vector3 position = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                int openSides = OutlineRule.OpenSides(cell.x, cell.z,
                    (x, z) => new IntVec3(x, 0, z).InBounds(map),
                    (x, z) => cells.Contains(new IntVec3(x, 0, z)));
                bool drewPlane = false;
                for (int side = 0; side < 4; side++)
                {
                    if ((openSides & (1 << side)) == 0) continue;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, new Rot4(side).AsQuat, Vector3.one), material, 0);
                    PlanesLastFrame++;
                    drewPlane = true;
                }
                if (drewPlane) DrawnCellsLastFrame.Add(cell);
            }
        }

        public static void Postfix(Map ___map)
        {
            if (___map == null) return;
            try
            {
                if (___map.Disposed || ___map != Find.CurrentMap) return;
                LastMap = ___map;
                FinishedLastFrame = false;
                PlanesLastFrame = 0;
                DrawnCellsLastFrame.Clear();
                pairedCells.Clear();
                unpairedCells.Clear();
                foreach (CoolingTarget target in CoolingLoopRegistry.HighlightTargets(___map))
                {
                    if (target.Paired) pairedCells.Add(target.Cell);
                    else unpairedCells.Add(target.Cell);
                }
                DrawOutline(pairedCells, PairedColor, ___map);
                DrawOutline(unpairedCells, UnpairedColor, ___map);
                FinishedLastFrame = true;
            }
            catch (Exception exception)
            {
                LogFailureOnce(___map, exception);
            }
        }

        // Logs a failure the first time its exception type and message reach a map. The same failure on the same map logs nothing more.
        private static void LogFailureOnce(Map map, Exception exception)
        {
            HashSet<string> logged = loggedFailures.GetValue(map, _ => new HashSet<string>());
            if (!logged.Add(exception.GetType().FullName + ": " + exception.Message)) return;
            Log.Error("Z-Levels partner highlight failed on map " + map.uniqueID + ", so no partner outline is drawn: " + exception);
        }
    }
}
