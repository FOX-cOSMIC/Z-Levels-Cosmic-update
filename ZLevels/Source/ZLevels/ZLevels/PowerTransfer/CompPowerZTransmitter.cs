using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    public class CompPowerZTransmitter : CompPowerPlant
    {
        PowerNet curPowerNet; 
        bool registered;
        public override float DesiredPowerOutput
        {
            get
            {
                return 0;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
        }

        public new CompProperties_PowerZTransmitter Props
        {
            get
            {
                return (CompProperties_PowerZTransmitter)this.props;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
        }

        public override void CompTick()
        {
            base.CompTick();
            var networks = ZUtils.ZTracker?.connectedPowerNets;
            if (networks == null)
            {
                return;
            }
            if (registered && (curPowerNet != PowerNet || !PowerOn))
            {
                networks.DeregisterTransmitter(this);
                registered = false;
            }
            curPowerNet = PowerNet;
            if (!registered && PowerNet != null && PowerOn)
            {
                networks.RegisterTransmitter(this);
                registered = true;
            }
        }

        public override void UpdateDesiredPowerOutput()
        {
            base.UpdateDesiredPowerOutput();
            if (PowerNet == null)
            {
                PowerOutput = 0f;
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            ZUtils.ZTracker?.connectedPowerNets?.DeregisterTransmitter(this);
            registered = false;
            curPowerNet = null;
            base.PostDeSpawn(map, mode);
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
        }
    }
}
