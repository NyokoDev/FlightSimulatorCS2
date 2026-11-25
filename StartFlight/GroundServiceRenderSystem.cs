using Game;

namespace CS2FlightTakeover.StartFlight
{
    // Scoped to owned service objects; no changes to normal aircraft or city props.
    public sealed partial class GroundServiceRenderSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<StartFlightSystem>()?.MaintainServiceRendering();
        }
    }
}
