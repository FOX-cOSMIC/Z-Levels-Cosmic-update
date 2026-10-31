using Verse;

namespace ZLevels
{
    // The one pocket map that every vault entrance shares. The first entrance creates it, and later entrances use it.
    public class VaultPocket : GameComponent
    {
        private Map pocket;

        public VaultPocket()
        {
        }

        public VaultPocket(Game game)
        {
        }

        public Map Pocket
        {
            get
            {
                if (pocket != null && pocket.Parent?.HasMap == false)
                {
                    pocket = null;
                }
                return pocket;
            }
            set
            {
                pocket = value;
            }
        }

        // Saves made before this component existed have none, so add one when it is missing.
        public static VaultPocket Get()
        {
            VaultPocket component = Current.Game.GetComponent<VaultPocket>();
            if (component == null)
            {
                component = new VaultPocket(Current.Game);
                Current.Game.components.Add(component);
            }
            return component;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pocket, "vaultPocket");
        }
    }
}
