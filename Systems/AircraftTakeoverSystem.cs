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
        public string BoundaryMode = "Assisted";
        public float HalfExtent = 7000f, WarningDistance = 1000f;
        public float BoundaryDistance => HalfExtent - math.max(math.abs(_position.x), math.abs(_position.z));
        public void SetThrottle(float value) { targetThrottle = math.saturate(value); }
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

        public bool Begin(Entity candidate)
        {
            var selection = World.GetExistingSystemManaged<AircraftSelectionSystem>();
            if (selection == null || !selection.HasSelection || selection.Selected != candidate ||
                !EntityManager.Exists(candidate) || !EntityManager.HasComponent<Transform>(candidate))
                return false;

            Stop();
            var transform = EntityManager.GetComponentData<Transform>(candidate);
            _position = transform.m_Position;
            _rotation = transform.m_Rotation;
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
            Active = true;
            Mod.Log.Info($"Experimental takeover started: {candidate.Index}:{candidate.Version}");
            return true;
        }

        public void Stop()
        {
            if (Active) Mod.Log.Warn("Experimental takeover stopped. CS2 AI routing may not resume correctly; reload the save if needed.");
            Active = false;
            Controlled = Entity.Null;
            PitchInput = RollInput = YawInput = ThrottleInput = 0f;
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
            if (dt <= 0f) return;

            if (UnityEngine.Time.realtimeSinceStartup >= alertUntil) ImpactAlert="";
            elapsed += dt;
            float blend = math.saturate(elapsed / 2f);
            targetThrottle = math.saturate(targetThrottle + ThrottleInput * dt * .35f);
            Throttle = math.lerp(Throttle, targetThrottle, 1f-math.exp(-dt*1.5f));
            Speed = math.clamp(Speed + (Throttle*24f-10f-Speed*.065f)*dt*blend, 0f, 180f);
            float3 forward = math.mul(_rotation,new float3(0,0,1));
            float yaw = YawInput*20f + math.sin(math.radians(BankDegrees))*Speed*.18f;
            if (BoundaryMode == "Assisted" && BoundaryDistance < WarningDistance) {
                float desired = math.atan2(-_position.x,-_position.z);
                float current = math.atan2(forward.x,forward.z);
                float error = math.degrees(math.atan2(math.sin(desired-current),math.cos(desired-current)));
                yaw = math.lerp(yaw,math.clamp(error*1.5f,-45f,45f),math.saturate(1f-BoundaryDistance/math.max(100f,WarningDistance)));
            }
            _rotation = math.normalize(math.mul(_rotation, quaternion.EulerXYZ(math.radians(-PitchInput*dt*18f*blend),math.radians(yaw*dt*blend),math.radians(-RollInput*dt*45f*blend))));
            forward = math.mul(_rotation,new float3(0,0,1));
            HeadingDegrees = (math.degrees(math.atan2(forward.x,forward.z))+360f)%360f;
            PitchDegrees = math.degrees(math.asin(math.clamp(forward.y,-1f,1f)));
            var right = math.mul(_rotation,new float3(1,0,0));
            BankDegrees = math.degrees(math.atan2(-right.y,math.sqrt(right.x*right.x+right.z*right.z)));
            velocity = math.lerp(velocity,forward*Speed,(1f-math.exp(-dt*2f))*blend);
            var next = _position + velocity*dt;
            float ground=0, sea=0;
            bool hasGround=terrain!=null && terrain.Read(next,out ground);
            bool hasWater=water!=null && water.Read(next,out sea);
            SurfaceStatus=hasGround ? (hasWater?"Ground and water detection active":"Ground detection active; water unavailable") : "Ground detection unavailable";
            if(CollisionEnabled && (hasGround||hasWater)) {
                float floor=hasGround?ground:float.NegativeInfinity;
                if(hasWater && sea>floor)floor=sea;
                if(next.y<floor+3f && velocity.y<0f && _position.y>=floor+3f) {
                    if (!impact) {
                        ImpactAlert=hasWater&&(!hasGround||sea>ground)?"Water impact — continue flying":"Ground impact — continue flying";
                        alertUntil=UnityEngine.Time.realtimeSinceStartup+5f;
                        impact=true;
                    }
                }
            }
            if (!CollisionEnabled || (!hasGround && !hasWater) || next.y > math.max(hasGround?ground:float.NegativeInfinity,hasWater?sea:float.NegativeInfinity)+5f) impact=false;
            _position=next;
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
