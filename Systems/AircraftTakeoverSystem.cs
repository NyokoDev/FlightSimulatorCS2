using Game;
using Game.Objects;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.Systems
{
    // Experimental direct ECS movement. CS2 AI can still overwrite these values.
    // Do not use on an important save: route restoration is not implemented.
    public sealed partial class AircraftTakeoverSystem : GameSystemBase
    {
        public bool Active { get; private set; }
        public Entity Controlled { get; private set; } = Entity.Null;
        public float Throttle { get; private set; } = 0.55f;
        public float Speed { get; private set; }
        public float PitchDegrees { get; private set; }
        public float BankDegrees { get; private set; }
        public float HeadingDegrees { get; private set; }
        private float targetThrottle = .55f, elapsed;
        private float3 velocity;
        public float VerticalSpeed => velocity.y;
        public float3 Velocity => velocity;
        public string BoundaryMode = "Assisted";
        public float HalfExtent = 7000f, WarningDistance = 1000f;
        public float BoundaryDistance => HalfExtent - math.max(math.abs(_position.x), math.abs(_position.z));
        public void SetThrottle(float value) { if (math.isfinite(value)) targetThrottle = math.saturate(value); }
        public void ReturnToMap() {
            if (!Active || !EntityManager.Exists(Controlled)) return;
            _position.x = math.clamp(_position.x, -HalfExtent*.7f, HalfExtent*.7f);
            _position.z = math.clamp(_position.z, -HalfExtent*.7f, HalfExtent*.7f);
        }
        public void ConfigureBoundary(string value) {
            var parts = value.Split('|');
            if (parts.Length != 2) return;
            if (parts[0] == "Mode" && (parts[1] == "Assisted" || parts[1] == "Warning" || parts[1] == "Wrap" || parts[1] == "Unrestricted")) BoundaryMode = parts[1];
            if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n) && math.isfinite(n)) {
                if (parts[0] == "Extent") HalfExtent = math.clamp(n, 500f, 50000f);
                if (parts[0] == "Warning") WarningDistance = math.clamp(n, 100f, 5000f);
            }
        }
        public float PitchInput, RollInput, YawInput, ThrottleInput;
        private float3 _position;
        public float3 Position => _position;
        private SurfaceProbe terrain, water;
        public string SurfaceStatus = "Surface detection unavailable";
        public bool CollisionEnabled = true;
        private bool impact;
        public string ImpactAlert { get; private set; } = "";
        private float alertUntil;
        private quaternion _rotation;

        public bool Grounded { get; private set; }
        public bool ParkingBrake { get; set; }
        private bool dedicated;
        public bool IsOwnedFlight => Active && dedicated;
        public quaternion Attitude => _rotation;
        // Independent airborne takeover attitude. Ground operations never write these angles.
        private float takeoverHeading, takeoverPitch, takeoverBank;
        private static float SafeAxis(float v) => math.isfinite(v) ? math.clamp(v, -1f, 1f) : 0f;

        private void InitTakeoverAttitude()
        {
            var forward = math.mul(_rotation, new float3(0, 0, 1));
            var right = math.mul(_rotation, new float3(1, 0, 0));
            takeoverHeading = math.atan2(forward.x, forward.z);
            takeoverPitch = math.clamp(math.asin(math.clamp(forward.y, -1f, 1f)), math.radians(-75f), math.radians(75f));
            var levelRight = new float3(math.cos(takeoverHeading), 0, -math.sin(takeoverHeading));
            var levelUp = math.cross(forward, levelRight);
            takeoverBank = math.clamp(math.atan2(-math.dot(right, levelUp), math.dot(right, levelRight)), math.radians(-85f), math.radians(85f));
        }

        private void UpdateTakeoverAttitude(float yaw, float dt, float blend)
        {
            // World-up heading and independent pitch/bank prevent local-axis roll/yaw coupling
            // from accumulating an unintended inverted attitude.
            takeoverHeading += math.radians(yaw * dt * blend);
            takeoverHeading = math.atan2(math.sin(takeoverHeading), math.cos(takeoverHeading));
            takeoverPitch = math.clamp(takeoverPitch + math.radians(SafeAxis(PitchInput) * dt * 18f * blend), math.radians(-75f), math.radians(75f));
            takeoverBank = math.clamp(takeoverBank + math.radians(SafeAxis(RollInput) * dt * 45f * blend), math.radians(-85f), math.radians(85f));
            _rotation = math.mul(math.mul(quaternion.RotateY(takeoverHeading), quaternion.RotateX(-takeoverPitch)), quaternion.RotateZ(-takeoverBank));
        }
        public int PushbackDirection {get;set;} = 0;
        public bool Begin(Entity candidate, bool owned = false)
        {
            var selection = World.GetExistingSystemManaged<AircraftSelectionSystem>();
            if (selection == null || !selection.HasSelection || selection.Selected != candidate ||
                !EntityManager.Exists(candidate) || !EntityManager.HasComponent<Transform>(candidate))
                return false;

            Stop();
            var transform = EntityManager.GetComponentData<Transform>(candidate);
            _position = transform.m_Position;
            _rotation = transform.m_Rotation;
            if (!math.all(math.isfinite(_rotation.value)) || math.lengthsq(_rotation.value) < 0.0001f)
                _rotation = quaternion.identity;
            else _rotation = math.normalize(_rotation);
            Controlled = candidate;
            Speed = EntityManager.HasComponent<Moving>(candidate)
                ? math.length(EntityManager.GetComponentData<Moving>(candidate).m_Velocity)
                : 35f;
            velocity = EntityManager.HasComponent<Moving>(candidate) ? EntityManager.GetComponentData<Moving>(candidate).m_Velocity : math.mul(_rotation,new float3(0,0,Speed));
            elapsed = 0f;
            impact=false; ImpactAlert=""; alertUntil=0;
            try { terrain=new SurfaceProbe(World,false); water=new SurfaceProbe(World,true); } catch(System.Exception e) { Mod.Log.Warn(e.Message); }
            Throttle = targetThrottle = math.saturate((10f + Speed*.065f)/24f);
            PitchDegrees = BankDegrees = 0f;
            var f = math.mul(_rotation,new float3(0,0,1));
            HeadingDegrees = math.degrees(math.atan2(f.x,f.z));
            // Existing aircraft orientation is the starting attitude.
            dedicated = owned; Grounded = owned; ParkingBrake = owned;
            if (!owned) InitTakeoverAttitude();
            if (owned) { Speed = 0; velocity = float3.zero; Throttle = targetThrottle = 0; }
            Active = true;
            Mod.Log.Info($"Experimental takeover started: {candidate.Index}:{candidate.Version}");
            return true;
        }

        public void Stop()
        {
            if (Active) Mod.Log.Warn("Experimental takeover stopped. CS2 AI routing may not resume correctly; reload the save if needed.");
            Active = false;
            PushbackDirection = 0;
            lastPushbackFrame = -1;
            Grounded = false; ParkingBrake = false; dedicated = false;
            Controlled = Entity.Null;
            PitchInput = RollInput = YawInput = ThrottleInput = 0f;
        }

        private int lastPushbackFrame = -1;
        public void SetPushback(int direction)
        {
            if (!IsOwnedFlight || direction < 0 || direction > 3) return;
            if (direction != 0 && (!Grounded || Speed >= .5f)) return;
            PushbackDirection = direction;
            ParkingBrake = true;
            if (direction == 0) { velocity = float3.zero; Speed = 0; }
            else { Throttle = targetThrottle = 0; }
            Mod.Log.Info($"Pushback command {direction}, aircraft {Controlled}, position {_position}");
        }

        // Run once per rendered frame even if the city simulation is paused.
        internal void AdvancePushback()
        {
            if (!IsOwnedFlight || !Grounded || PushbackDirection == 0 ||
                !EntityManager.Exists(Controlled) || EntityManager.HasComponent<Game.Common.Deleted>(Controlled)) return;
            int frame = UnityEngine.Time.frameCount;
            if (lastPushbackFrame == frame) return;
            lastPushbackFrame = frame;
            float dt = math.clamp(UnityEngine.Time.unscaledDeltaTime, 0f, .05f);
            if (dt <= 0f) return;
            quaternion rot = math.normalize(math.mul(_rotation, quaternion.RotateY(math.radians((PushbackDirection-2)*8f*dt))));
            float3 forward = math.mul(rot, new float3(0,0,1));
            float3 pos = _position - forward*2f*dt;
            if (terrain != null && terrain.Read(pos, out float height)) pos.y = height;
            velocity = (pos - _position) / dt;
            _position = pos; _rotation = rot; Speed = 0;
            HeadingDegrees = (math.degrees(math.atan2(forward.x,forward.z))+360f)%360f;
        }

        protected override void OnUpdate()
        {
            if (!Active) return;
            if (!EntityManager.Exists(Controlled) || !EntityManager.HasComponent<Transform>(Controlled))
            {
                Stop();
                return;
            }

            float dt = math.clamp(SystemAPI.Time.DeltaTime, 0f, 0.05f);
            // Owned pushback advances in PreCulling, not GameSimulation. Do not double-step.
            if (dedicated && Grounded && PushbackDirection != 0) return;
            if (dt <= 0f) return;

            if(dedicated && Grounded && ParkingBrake){Speed=0;velocity=float3.zero;EntityManager.SetComponentData(Controlled,new Transform{m_Position=_position,m_Rotation=_rotation});if(EntityManager.HasComponent<Moving>(Controlled)){var m=EntityManager.GetComponentData<Moving>(Controlled);m.m_Velocity=float3.zero;EntityManager.SetComponentData(Controlled,m);}return;}
            if (UnityEngine.Time.realtimeSinceStartup >= alertUntil) ImpactAlert="";
            elapsed += dt;
            float blend = math.saturate(elapsed / 2f);
            targetThrottle = math.saturate(targetThrottle + SafeAxis(ThrottleInput) * dt * .35f);
            Throttle = math.lerp(Throttle, targetThrottle, 1f-math.exp(-dt*1.5f));
            Speed = math.clamp(Speed + (Throttle*24f-10f-Speed*.065f)*dt*blend, 0f, 180f);
            if (dedicated && Grounded && ParkingBrake) Speed = 0;
            float3 forward = math.mul(_rotation,new float3(0,0,1));
            float yaw = SafeAxis(YawInput)*20f + math.sin(math.radians(BankDegrees))*Speed*.18f;
            if (BoundaryMode == "Assisted" && BoundaryDistance < WarningDistance) {
                float desired = math.atan2(-_position.x,-_position.z);
                float current = math.atan2(forward.x,forward.z);
                float error = math.degrees(math.atan2(math.sin(desired-current),math.cos(desired-current)));
                yaw = math.lerp(yaw,math.clamp(error*1.5f,-45f,45f),math.saturate(1f-BoundaryDistance/math.max(100f,WarningDistance)));
            }
            if (!dedicated)
                UpdateTakeoverAttitude(yaw, dt, blend);
            else
                _rotation = math.normalize(math.mul(_rotation, quaternion.EulerXYZ(math.radians(-SafeAxis(PitchInput)*dt*18f*blend),math.radians(yaw*dt*blend),math.radians(-SafeAxis(RollInput)*dt*45f*blend))));
            if (dedicated && Grounded && (Speed < 45f || PitchInput <= 0f)) { var f = math.mul(_rotation,new float3(0,0,1)); _rotation = quaternion.RotateY(math.atan2(f.x,f.z)); }
            forward = math.mul(_rotation,new float3(0,0,1));
            HeadingDegrees = (math.degrees(math.atan2(forward.x,forward.z))+360f)%360f;
            PitchDegrees = math.degrees(math.asin(math.clamp(forward.y,-1f,1f)));
            var right = math.mul(_rotation,new float3(1,0,0));
            BankDegrees = !dedicated ? math.degrees(takeoverBank) : math.degrees(math.atan2(-right.y,math.sqrt(right.x*right.x+right.z*right.z)));
            velocity = math.lerp(velocity,forward*Speed,(1f-math.exp(-dt*2f))*blend);
            if (dedicated && Grounded && ParkingBrake) velocity = float3.zero;
            var next = _position + velocity*dt;
            float ground = 0f, sea = 0f;

            bool hasGround = terrain != null && terrain.Read(next, out ground);
            bool hasWater = water != null && water.Read(next, out sea);

            SurfaceStatus = hasGround
                ? (hasWater
                    ? "Ground and water detection active"
                    : "Ground detection active; water unavailable")
                : "Ground detection unavailable";

            // Maintain the existing collision warnings.
            if (CollisionEnabled && (hasGround || hasWater))
            {
                float floor = hasGround
                    ? ground
                    : float.NegativeInfinity;

                if (hasWater && sea > floor)
                    floor = sea;

                if (next.y < floor + 3f &&
                    velocity.y < 0f &&
                    _position.y >= floor + 3f)
                {
                    if (!impact)
                    {
                        ImpactAlert = hasWater && (!hasGround || sea > ground)
                            ? "Water impact — continue flying"
                            : "Ground impact — continue flying";

                        alertUntil = UnityEngine.Time.realtimeSinceStartup + 5f;
                        impact = true;
                    }
                }
            }

            // Prevent the aircraft from penetrating the terrain.
            // Horizontal movement, throttle and steering remain unchanged.
            if (hasGround)
            {
                float groundClearance = dedicated ? 0f : 3f;
                float minimumAltitude = ground + groundClearance;

                if (dedicated && Grounded && (Speed < 45f || forward.y <= .03f)) { next.y = ground; velocity.y = 0; }
                if (next.y <= minimumAltitude)
                {
                    next.y = minimumAltitude;

                    // Cancel downward movement only.
                    // Positive vertical movement remains available for takeoff.
                    velocity.y = math.max(0f, velocity.y);
                    if (dedicated) Grounded = true;
                }
            }

            if (dedicated && hasGround && next.y > ground + .1f) Grounded = false;
            if (!CollisionEnabled ||
                (!hasGround && !hasWater) ||
                next.y > math.max(
                    hasGround ? ground : float.NegativeInfinity,
                    hasWater ? sea : float.NegativeInfinity
                ) + 5f)
            {
                impact = false;
            }

            _position = next;
            if (BoundaryMode == "Wrap") {
                _position.x = math.fmod(math.fmod(_position.x+HalfExtent,2f*HalfExtent)+2f*HalfExtent,2f*HalfExtent)-HalfExtent;
                _position.z = math.fmod(math.fmod(_position.z+HalfExtent,2f*HalfExtent)+2f*HalfExtent,2f*HalfExtent)-HalfExtent;
            } else if (BoundaryMode == "Assisted" && BoundaryDistance < 0f) ReturnToMap();
            var transform = EntityManager.GetComponentData<Transform>(Controlled);
            transform.m_Position = _position;
            transform.m_Rotation = _rotation;
            EntityManager.SetComponentData(Controlled, transform);
            if (EntityManager.HasComponent<Moving>(Controlled))
            {
                var moving = EntityManager.GetComponentData<Moving>(Controlled);
                moving.m_Velocity = velocity;
                EntityManager.SetComponentData(Controlled, moving);
            }
        }
    }
}
