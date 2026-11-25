using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2FlightTakeover.StartFlight
{
    // Native terrain tool pattern verified in yenyang/Anarchy, AnarchyComponentsToolSystem.
    public partial class AircraftPlacementTool : ToolBaseSystem
    {
        public override string toolID => "Nyoko Aircraft Placement";
        private ToolBaseSystem previousTool;
        private OverlayRenderSystem overlay;
        private bool pendingActivation, started, armed;
        private int retries;
        private bool lastValid;
        private bool loggedRaycast;
        private StartFlightSystem Owner => World.GetExistingSystemManaged<StartFlightSystem>();

        protected override void OnCreate()
        {
            Enabled = false;
            base.OnCreate();
            overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // ToolBaseSystem's own action proxies, not InputManager.FindAction built-ins.
            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;
            pendingActivation = false;
            started = true;
            armed = false;
            loggedRaycast = false;
            Mod.Log.Info("Aircraft placement active: inherited tool actions enabled");
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            started = false;
            armed = false;
            if (!pendingActivation) Owner?.PlacementStopped();
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public void Activate()
        {
            if (m_ToolSystem.activeTool != this)
                previousTool = m_ToolSystem.activeTool;
            pendingActivation = true;
            retries = 0;
            armed = false;
            m_ToolSystem.selected = Entity.Null;
            m_ToolSystem.activeTool = this;
        }

        // Like Anarchy, recover activation if the UI/game changes tools before we first run.
        // Limit retries so selecting a different native tool never produces a tug-of-war.
        public void EnsureActivation()
        {
            if (!pendingActivation || started || m_ToolSystem.activeTool == this) return;
            if (retries++ < 2) m_ToolSystem.activeTool = this;
            else
            {
                pendingActivation = false;
                Owner?.PlacementStopped();
                Mod.Log.Warn("Aircraft placement activation interrupted by another tool");
            }
        }

        public void RequestDisable()
        {
            pendingActivation = false;
            if (m_ToolSystem.activeTool == this)
                m_ToolSystem.activeTool = previousTool != null && previousTool != this ? previousTool : m_DefaultToolSystem;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            // Preserve the native collision masks and UI-disable flags set by the base.
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (Owner == null || !Owner.PickingDeparture)
            {
                RequestDisable();
                return inputDeps;
            }
            bool raycast = GetRaycastResult(out Entity entity, out RaycastHit hit);
            float3 position = new float3(hit.m_HitPosition.x, hit.m_Position.y, hit.m_HitPosition.z);
            // A terrain hit may not have an entity. Anarchy's terrain brush uses the
            // hit coordinates even in that case; never accept the default empty result.
            bool valid = math.all(math.isfinite(position)) &&
                math.all(math.isfinite(hit.m_HitPosition)) &&
                (raycast || math.any(hit.m_HitPosition != float3.zero));
            if (!loggedRaycast || valid != lastValid)
            {
                Mod.Log.Info($"Departure raycast: valid={valid}, result={raycast}, hit={hit.m_HitPosition}, surface={hit.m_Position}");
                Owner.PlacementHoverChanged(valid);
                loggedRaycast = true;
                lastValid = valid;
            }
            if (valid)
            {
                var buffer = overlay.GetBuffer(out JobHandle overlayDependency);
                var marker = new DepartureMarkerJob { Buffer = buffer, Position = position };
                inputDeps = IJobExtensions.Schedule(marker, JobHandle.CombineDependencies(inputDeps, overlayDependency));
                overlay.AddBufferWriter(inputDeps);
            }
            // Wait for the UI activation click to finish before accepting a world click.
            if (!armed)
            {
                if (!applyAction.IsPressed() && !secondaryApplyAction.IsPressed()) armed = true;
                return inputDeps;
            }
            if (secondaryApplyAction.WasReleasedThisFrame())
            {
                Owner.PlacementStopped();
                RequestDisable();
            }
            else if (applyAction.WasReleasedThisFrame())
            {
                Mod.Log.Info($"Departure click: valid={valid}, position={position}");
                if (valid)
                {
                    Owner.SelectDeparture(position);
                    RequestDisable();
                }
            }
            return inputDeps;
        }

        private struct DepartureMarkerJob : IJob
        {
            public OverlayRenderSystem.Buffer Buffer;
            public float3 Position;
            public void Execute()
            {
                Buffer.DrawCircle(new UnityEngine.Color(.24f, .72f, 1f, 1f), default, .35f, 0, new float2(0, 1), Position, 14f);
                Buffer.DrawCircle(new UnityEngine.Color(1f, 1f, 1f, 1f), default, .2f, 0, new float2(0, 1), Position, 2f);
            }
        }
    }
}
