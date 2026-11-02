using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ZLevelsRimatomics
{
    // Outlines the partner cells of the viewed level: green where the level also holds a usable cooling column, amber where it does not.
    // Map.MapUpdate calls DynamicDrawManager.DrawDynamicThings only for the map in view, inside its draw block, so this postfix runs once per
    // frame for that map. An exception would reach the game's draw loop, so the postfix catches it and logs the first failure on each map.
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
        // The map that last logged a failure, and whether any failure has been logged. A failure logs when its map is not the one that logged last,
        // so a map that fails in every frame logs once, and a failure on another map logs again.
        private static bool failureLogged;
        private static Map failedMap;
        // The outcome of the last frame that ran for a map. The validation harness reads these to prove the outline was drawn. LastMap is the map
        // that frame ran for. TargetsLastFrame is the number of targets it found, or -1 when it did not reach the count. DrawnLastFrame is the
        // number of those targets whose outline pass completed.
        public static Map LastMap;
        public static int TargetsLastFrame = -1;
        public static int DrawnLastFrame;

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

        // One plane per open edge, drawn with Graphics.DrawMesh. GenDraw.DrawFieldEdges draws the same planes with Graphics.DrawMeshInstanced.
        // With that call, two of five look runs lost the GPU device (20261102-215009 and 20261102-215926), so this draw does not use instancing.
        // An edge shared with another cell of the list is skipped, and so is an edge that leads off the map, as DrawFieldEdges skips them.
        // Returns the number of cells whose outline was drawn. A cell with no open side of its own lies inside its neighbours' outline, so it counts too.
        private static int DrawOutline(List<IntVec3> cells, Color color, Map map)
        {
            if (cells.Count == 0) return 0;
            Material material = MaterialPool.MatFrom(FieldEdgeTexture(), ShaderDatabase.Transparent, color, OutlineRenderQueue);
            int outlined = 0;
            foreach (IntVec3 cell in cells)
            {
                Vector3 position = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                for (int side = 0; side < 4; side++)
                {
                    IntVec3 neighbour = cell + GenAdj.CardinalDirections[side];
                    if (!neighbour.InBounds(map) || cells.Contains(neighbour)) continue;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, new Rot4(side).AsQuat, Vector3.one), material, 0);
                }
                outlined++;
            }
            return outlined;
        }

        public static void Postfix(Map ___map)
        {
            try
            {
                if (___map == null || ___map.Disposed || ___map != Find.CurrentMap) return;
                LastMap = ___map;
                TargetsLastFrame = -1;
                DrawnLastFrame = 0;
                pairedCells.Clear();
                unpairedCells.Clear();
                foreach (CoolingTarget target in CoolingLoopRegistry.HighlightTargets(___map))
                {
                    if (target.Paired) pairedCells.Add(target.Cell);
                    else unpairedCells.Add(target.Cell);
                }
                TargetsLastFrame = pairedCells.Count + unpairedCells.Count;
                DrawnLastFrame += DrawOutline(pairedCells, PairedColor, ___map);
                DrawnLastFrame += DrawOutline(unpairedCells, UnpairedColor, ___map);
            }
            catch (Exception exception)
            {
                // Later frames on the same map that fail log nothing. A map that fails after another map logs again, so the log names each map that failed.
                if (failureLogged && ReferenceEquals(failedMap, ___map)) return;
                failureLogged = true;
                failedMap = ___map;
                string mapName = ___map == null ? "none" : ___map.uniqueID.ToString();
                Log.Error("Z-Levels partner highlight failed on map " + mapName + ", so no partner outline is drawn: " + exception);
            }
        }
    }
}
