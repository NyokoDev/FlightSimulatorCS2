using Colossal.Logging;
using Game;
using Game.Modding;
using CS2FlightTakeover.Systems;
using CS2FlightTakeover.UI;

namespace CS2FlightTakeover
{
    public sealed class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger("CS2FlightTakeover").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("Flight Takeover: registering systems");
            updateSystem.UpdateAfter<AircraftDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<AircraftSelectionSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<FlightInputSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<AircraftTakeoverSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<AircraftPoseSyncSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateBefore<StartFlight.GroundServiceCreationSystem, Game.Tools.GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateBefore<StartFlight.GroundServiceRenderSystem, Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAt<StartFlight.AircraftPlacementTool>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<StartFlight.StartFlightSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<AircraftCameraSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<NavigationSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<FlightUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose() { Log.Info("Flight Takeover unloaded"); }
    }
}
