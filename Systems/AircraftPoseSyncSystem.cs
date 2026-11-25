using Game;
using Game.Common;
using Game.Objects;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.Systems
{
    // Native vehicle rendering interpolates TransformFrame snapshots, not just Transform.
    // Reconcile those snapshots immediately before culling, only for the controlled aircraft.
    public sealed partial class AircraftPoseSyncSystem : GameSystemBase
    {
        private float nextWarning;
        private Entity lastOwnedEntity = Entity.Null;
        private float3 lastOwnedPosition;
        private quaternion lastOwnedRotation;
        private float nextPushbackLog;
        private readonly AircraftRenderPose visualPose = new AircraftRenderPose();

        internal bool TryGetRenderPose(Entity entity, out float3 position, out quaternion rotation)
        {
            position = visualPose.Position;
            rotation = visualPose.Rotation;
            return visualPose.Entity == entity && entity != Entity.Null;
        }
        // Stopped vehicle/prop archetypes may cache render batches until explicitly dirtied.
        // Refresh only mod-owned entities whose pose changes; never mark ordinary takeover vehicles.
        internal static void InvalidateOwnedPose(EntityManager em, Entity e)
        {
            if (!em.HasComponent<Updated>(e)) em.AddComponent<Updated>(e);
            if (!em.HasComponent<BatchesUpdated>(e)) em.AddComponent<BatchesUpdated>(e);
        }
        protected override void OnUpdate()
        {
            var flight = World.GetExistingSystemManaged<AircraftTakeoverSystem>();
            if (flight == null || !flight.Active) { lastOwnedEntity = Entity.Null; visualPose.Reset(); return; }
            flight.AdvancePushback();
            Entity entity = flight.Controlled;
            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity) || !EntityManager.HasComponent<Transform>(entity)) { visualPose.Reset(); return; }
            var position = flight.Position;
            var rotation = flight.Attitude;
            if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(rotation.value))) return;
            var transform = EntityManager.GetComponentData<Transform>(entity);
            if (math.abs(math.dot(transform.m_Rotation.value, rotation.value)) < .96f && UnityEngine.Time.realtimeSinceStartup >= nextWarning)
            {
                nextWarning = UnityEngine.Time.realtimeSinceStartup + 3f;
                Mod.Log.Warn($"Aircraft pose diverged before rendering ({entity}); reconciling simulation and render pose.");
            }
            // Smooth presentation independently of the existing simulation and input rates.
            // Parked aircraft and pushback use the exact pose (pushback already advances per frame).
            visualPose.Advance(entity, position, rotation, UnityEngine.Time.realtimeSinceStartup,
                UnityEngine.Time.unscaledDeltaTime, flight.IsOwnedFlight && flight.Grounded && flight.ParkingBrake);
            position = visualPose.Position;
            rotation = visualPose.Rotation;
            transform.m_Position = position;
            transform.m_Rotation = rotation;
            EntityManager.SetComponentData(entity, transform);
            if (EntityManager.HasComponent<Moving>(entity))
            {
                var moving = EntityManager.GetComponentData<Moving>(entity);
                moving.m_Velocity = flight.Velocity;
                EntityManager.SetComponentData(entity, moving);
            }
            if (EntityManager.HasBuffer<TransformFrame>(entity))
            {
                var frames = EntityManager.GetBuffer<TransformFrame>(entity);
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = frames[i];
                    frame.m_Position = position;
                    frame.m_Rotation = rotation;
                    // All slots deliberately have the SAME position. Native CalculateTransform
                    // uses cubic Bezier velocity tangents; nonzero tangents here produce an
                    // artificial forward/backward excursion between identical endpoints.
                    // Moving still carries the real flight velocity for simulation.
                    frame.m_Velocity = float3.zero;
                    frames[i] = frame; // Preserve native state, activity and flags.
                }
            }
            if (EntityManager.HasComponent<InterpolatedTransform>(entity))
            {
                var render = EntityManager.GetComponentData<InterpolatedTransform>(entity);
                render.m_Position = position;
                render.m_Rotation = rotation;
                EntityManager.SetComponentData(entity, render);
            }
            if (flight.IsOwnedFlight)
            {
                bool changed = lastOwnedEntity != entity || math.distancesq(lastOwnedPosition, position) > 1e-8f ||
                    math.abs(math.dot(lastOwnedRotation.value, rotation.value)) < .9999999f;
                if (changed)
                {
                    InvalidateOwnedPose(EntityManager, entity);
                    lastOwnedEntity = entity; lastOwnedPosition = position; lastOwnedRotation = rotation;
                }
                if (flight.PushbackDirection != 0 && UnityEngine.Time.realtimeSinceStartup >= nextPushbackLog)
                {
                    nextPushbackLog = UnityEngine.Time.realtimeSinceStartup + 2f;
                    Mod.Log.Info($"Pushback tick {entity}: position={position}, velocity={flight.Velocity}, Moving={EntityManager.HasComponent<Moving>(entity)}, frames={EntityManager.HasBuffer<TransformFrame>(entity)}");
                }
                World.GetExistingSystemManaged<StartFlight.StartFlightSystem>()?.AdvanceServices();
            }
            else lastOwnedEntity = Entity.Null;
            // The follow camera uses exactly the same rendered pose, not a second predictor.
            World.GetExistingSystemManaged<AircraftCameraSystem>()?.UpdateControlledView(entity, position, rotation);
        }
    }
}
