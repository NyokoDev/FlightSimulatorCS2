using Game;
using Unity.Entities;

namespace CS2FlightTakeover.Systems
{
    // Read-only selection: does not alter aircraft AI, passengers, or routing.
    public sealed partial class AircraftSelectionSystem : GameSystemBase
    {
        public Entity Selected { get; private set; } = Entity.Null;
        public bool HasSelection => Selected != Entity.Null && EntityManager.Exists(Selected);

        public bool TrySelect(Entity candidate)
        {
            if (!EntityManager.Exists(candidate)) return false;
            var discovery = World.GetExistingSystemManaged<AircraftDiscoverySystem>();
            if (discovery == null) return false;
            foreach (var entry in discovery.Entries)
            {
                if (entry.Entity != candidate) continue;
                Selected = candidate;
                Mod.Log.Info($"Flight Takeover selected {candidate.Index}:{candidate.Version}");
                return true;
            }
            return false;
        }

        public void Release() { Selected = Entity.Null; }

        protected override void OnUpdate()
        {
            if (Selected != Entity.Null && !EntityManager.Exists(Selected)) Release();
        }
    }
}
