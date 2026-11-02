using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ZLevelsRimatomics
{
    // Outlines the partner cells of the viewed level: green where the level also holds a usable cooling column, amber where it does not.
    // Map.MapUpdate calls DynamicDrawManager.DrawDynamicThings only for the map in view, inside its draw block, so this postfix runs once per
    // frame for that map. An exception would reach the game's draw loop, so the postfix catches it and logs the first one only.
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
        private static readonly List<IntVec3> pairedCells = new List<IntVec3>();
        private static readonly List<IntVec3> unpairedCells = new List<IntVec3>();
        private static bool failureLogged;

        // One plane per open edge, drawn with Graphics.DrawMesh. GenDraw.DrawFieldEdges draws the same planes with Graphics.DrawMeshInstanced.
        // With that call, two of five look runs lost the GPU device (20261102-215009 and 20261102-215926), so this draw does not use instancing.
        // An edge shared with another cell of the list is skipped, and so is an edge that leads off the map, as DrawFieldEdges skips them.
        private static void DrawOutline(List<IntVec3> cells, Color color, Map map)
        {
            if (cells.Count == 0) return;
            edgeTexture ??= (Texture2D)MatLoader.LoadMat(FieldEdgeMaterialPath).mainTexture;
            // Without a texture, MatFrom logs an error on every frame, so the missing texture is thrown here and logged once by Postfix.
            if (edgeTexture == null) throw new InvalidOperationException("the outline texture " + FieldEdgeMaterialPath + " has no texture");
            Material material = MaterialPool.MatFrom(edgeTexture, ShaderDatabase.Transparent, color, OutlineRenderQueue);
            foreach (IntVec3 cell in cells)
            {
                Vector3 position = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                for (int side = 0; side < 4; side++)
                {
                    IntVec3 neighbour = cell + GenAdj.CardinalDirections[side];
                    if (!neighbour.InBounds(map) || cells.Contains(neighbour)) continue;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, new Rot4(side).AsQuat, Vector3.one), material, 0);
                }
            }
        }

        public static void Postfix(Map ___map)
        {
            try
            {
                if (___map == null || ___map.Disposed || ___map != Find.CurrentMap) return;
                pairedCells.Clear();
                unpairedCells.Clear();
                foreach (CoolingTarget target in CoolingLoopRegistry.HighlightTargets(___map))
                {
                    if (target.Paired) pairedCells.Add(target.Cell);
                    else unpairedCells.Add(target.Cell);
                }
                DrawOutline(pairedCells, PairedColor, ___map);
                DrawOutline(unpairedCells, UnpairedColor, ___map);
            }
            catch (Exception exception)
            {
                // Logged once. Later frames that fail draw no outline and log nothing, so the log holds the first cause only.
                if (failureLogged) return;
                failureLogged = true;
                Log.Error("Z-Levels partner highlight failed, so no partner outline is drawn: " + exception);
            }
        }
    }
}
