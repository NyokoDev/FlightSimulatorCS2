using Game;

namespace CS2FlightTakeover.StartFlight
{
    // Native permanent definitions must be emitted before GenerateObjectsSystem.
    // No active tool changes, no raw MeshBatch indices and no city-wide render resets.
    public sealed partial class GroundServiceCreationSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<StartFlightSystem>()?.EmitNativeServiceDefinitions();
        }
    }
}
