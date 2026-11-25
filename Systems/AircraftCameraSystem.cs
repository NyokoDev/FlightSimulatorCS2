using Game;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Transform = Game.Objects.Transform;

namespace CS2FlightTakeover.Systems
{
    public sealed partial class AircraftCameraSystem : GameSystemBase
    {
        public bool FollowEnabled { get; set; }
        public string Mode { get; private set; } = "Chase";
        public float Distance { get; private set; } = 65f;
        public void SetDistance(string value)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float distance) && !float.IsNaN(distance) && !float.IsInfinity(distance))
                Distance = Mathf.Clamp(distance, 10f, 2000f);
        }
        private float azimuth=0f, elevation=18f;
        public string Angles => azimuth.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+elevation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public void SetAngle(string value){var p=value.Split('|');if(p.Length!=2||!float.TryParse(p[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)||!math.isfinite(v))return;if(p[0]=="Azimuth")azimuth=math.clamp(v,-180,180);if(p[0]=="Elevation")elevation=math.clamp(v,-10,80);}
        private float cinematicStart;
        private Camera _camera;
        private Entity _target = Entity.Null;
        private Vector3 _savedPosition, _position;
        private Quaternion _savedRotation, _rotation;
        private float _savedNear;
        private bool _initialized;
        private Vector3 sampleCenter, sampleVelocity, smoothCenter;
        private Quaternion sampleAttitude, smoothAttitude;
        private float sampleTime, lastRenderTime, smoothDistance;
        private int lastFrame = -1;
        private bool controlledView;
        private Vector3 controlledOffset;
        public void SetMode(string mode)
        {
            if (mode != "Chase" && mode != "Pilot" && mode != "Orbit" && mode != "Cinematic") return;
            Mode = mode; if (mode == "Cinematic") cinematicStart = UnityEngine.Time.realtimeSinceStartup;
        }
        private void Restore()
        {
            Camera.onPreCull -= RenderView;
            RenderPipelineManager.beginCameraRendering -= RenderSRP;
            if (_camera != null && _initialized)
            {
                _camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
                _camera.nearClipPlane = _savedNear;
            }
            _camera = null; _initialized = false; _target = Entity.Null; controlledView = false;
        }
        public void StopFollow() { FollowEnabled = false; Restore(); }
        protected override void OnDestroy() { StopFollow(); base.OnDestroy(); }
        protected override void OnUpdate()
        {
            if (!FollowEnabled) { if (_initialized) Restore(); return; }
            var selection = World.GetExistingSystemManaged<AircraftSelectionSystem>();
            if (selection == null || !selection.HasSelection || !EntityManager.HasComponent<Transform>(selection.Selected)) { StopFollow(); return; }
            var t = EntityManager.GetComponentData<Transform>(selection.Selected);
            var flight = World.GetExistingSystemManaged<AircraftTakeoverSystem>();
            bool controlled = flight != null && flight.Active && flight.Controlled == selection.Selected;
            if (controlled)
            {
                t.m_Position = flight.Position;
                t.m_Rotation = flight.Attitude;
                var sync = World.GetExistingSystemManaged<AircraftPoseSyncSystem>();
                if (sync != null && sync.TryGetRenderPose(selection.Selected, out var p, out var q))
                {
                    t.m_Position = p;
                    t.m_Rotation = q;
                }
            }
            var center = new Vector3(t.m_Position.x,t.m_Position.y,t.m_Position.z);
            
            var cam = Camera.main;
            if (cam == null) return;
            if (!_initialized || _camera != cam || _target != selection.Selected)
            {
                Restore(); _camera = cam; _target = selection.Selected;
                _savedPosition = cam.transform.position; _savedRotation = cam.transform.rotation; _savedNear = cam.nearClipPlane;
                _position = _savedPosition; _rotation = _savedRotation; _initialized = true;
                sampleCenter = smoothCenter = center;
                sampleAttitude = smoothAttitude = new Quaternion(t.m_Rotation.value.x,t.m_Rotation.value.y,t.m_Rotation.value.z,t.m_Rotation.value.w);
                sampleVelocity = Vector3.zero;
                sampleTime = lastRenderTime = UnityEngine.Time.realtimeSinceStartup;
                smoothDistance = Distance; lastFrame = -1;
                Camera.onPreCull += RenderView;
                RenderPipelineManager.beginCameraRendering += RenderSRP;
            }
            if (controlled)
            {
                // PreCulling updates this camera together with the aircraft. Do not advance
                // a second independent world-space predictor from UIUpdate.
                if (controlledView) ApplyView();
                return;
            }
            if (controlledView)
            {
                controlledView = false;
                sampleCenter = smoothCenter = center;
                sampleVelocity = Vector3.zero;
                sampleTime = UnityEngine.Time.realtimeSinceStartup;
                sampleAttitude = smoothAttitude = new Quaternion(t.m_Rotation.value.x,t.m_Rotation.value.y,t.m_Rotation.value.z,t.m_Rotation.value.w);
                lastFrame = -1;
            }
            var now = UnityEngine.Time.realtimeSinceStartup;
            if ((center-sampleCenter).sqrMagnitude > 0.000001f) {
                float interval = now-sampleTime;
                if (interval > 0.0001f) sampleVelocity = Vector3.ClampMagnitude((center-sampleCenter)/interval, 250f);
                if ((center-sampleCenter).sqrMagnitude > 250000f) { smoothCenter=center; sampleVelocity=Vector3.zero; }
                sampleCenter=center; sampleTime=now;
            }
            sampleAttitude = new Quaternion(t.m_Rotation.value.x,t.m_Rotation.value.y,t.m_Rotation.value.z,t.m_Rotation.value.w);
            // UIUpdate is the primary camera driver; render callbacks only reapply.
            AdvanceView();
            ApplyView();
        }
        internal void UpdateControlledView(Entity entity, float3 position, quaternion attitude)
        {
            if (!_initialized || !FollowEnabled || _target != entity || _camera == null) return;
            var center = new Vector3(position.x, position.y, position.z);
            var rotation = new Quaternion(attitude.value.x, attitude.value.y, attitude.value.z, attitude.value.w);
            bool first = !controlledView;
            controlledView = true;
            bool advance = first || lastFrame != UnityEngine.Time.frameCount;
            float dt = advance ? Mathf.Clamp(UnityEngine.Time.unscaledDeltaTime, 0f, .1f) : 0f;
            lastFrame = UnityEngine.Time.frameCount;
            if (first) { smoothAttitude = rotation; smoothDistance = Distance; }
            else
            {
                smoothAttitude = Quaternion.Slerp(smoothAttitude, rotation, 1f - Mathf.Exp(-10f * dt));
                smoothDistance = Mathf.Lerp(smoothDistance, Distance, 1f - Mathf.Exp(-7f * dt));
            }
            // Smooth camera-relative offset, not the translating aircraft center. No
            // velocity prediction or world-position damping to fight the rendered model.
            var forward = (Mode == "Pilot" ? rotation : smoothAttitude) * Vector3.forward;
            var flat = new Vector3(forward.x, 0, forward.z).normalized;
            if (flat.sqrMagnitude < .01f) flat = Vector3.forward;
            var radial = Quaternion.AngleAxis(azimuth, Vector3.up) * (-flat);
            float angle = elevation * Mathf.Deg2Rad;
            var desiredOffset = radial * (smoothDistance * Mathf.Cos(angle)) + Vector3.up * (smoothDistance * Mathf.Sin(angle));
            if (Mode == "Cinematic")
            {
                float t = UnityEngine.Time.realtimeSinceStartup - cinematicStart;
                float r = Mathf.Lerp(140f, 48f, Mathf.Clamp01(t / 12f));
                desiredOffset = Quaternion.AngleAxis(t * 12f, Vector3.up) * (-flat) * r + Vector3.up * Mathf.Lerp(55f, 9f, Mathf.Clamp01(t / 12f));
            }
            if (Mode == "Pilot") desiredOffset = forward * 22f + Vector3.up * 4f;
            controlledOffset = first || Mode == "Pilot" ? desiredOffset : Vector3.Lerp(controlledOffset, desiredOffset, 1f - Mathf.Exp(-10f * dt));
            _position = center + controlledOffset;
            var look = Mode == "Pilot" ? forward : -controlledOffset.normalized;
            if (look.sqrMagnitude > .001f) _rotation = Quaternion.LookRotation(look, Vector3.up);
            ApplyView();
        }
        private void AdvanceView() {
            if (!_initialized || !FollowEnabled || lastFrame == UnityEngine.Time.frameCount) return;
            lastFrame = UnityEngine.Time.frameCount;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(UnityEngine.Time.unscaledDeltaTime,0f,.1f); lastRenderTime=now;
            // Predict between ECS updates, then damp corrections on each rendered frame.
            var predicted = sampleCenter + sampleVelocity * Mathf.Clamp(now-sampleTime,0f,.12f);
            smoothCenter = Vector3.Lerp(smoothCenter,predicted,1f-Mathf.Exp(-12f*dt));
            smoothAttitude = Quaternion.Slerp(smoothAttitude,sampleAttitude,1f-Mathf.Exp(-10f*dt));
            smoothDistance = Mathf.Lerp(smoothDistance,Distance,1f-Mathf.Exp(-7f*dt));
            var forward = smoothAttitude * Vector3.forward;
            var flat = new Vector3(forward.x,0,forward.z).normalized;
            if (flat.sqrMagnitude < .01f) flat = Vector3.forward;
            var radial = Quaternion.AngleAxis(azimuth,Vector3.up) * (-flat);
            var angle = elevation * Mathf.Deg2Rad;
            var desired = Mode == "Pilot" ? smoothCenter + forward*22f + Vector3.up*4f : smoothCenter + radial*(smoothDistance*Mathf.Cos(angle)) + Vector3.up*(smoothDistance*Mathf.Sin(angle));
            if (Mode == "Cinematic") { float t = now-cinematicStart; float a = t*12f; float r = Mathf.Lerp(140f,48f,Mathf.Clamp01(t/12f)); desired = smoothCenter + Quaternion.AngleAxis(a,Vector3.up)*(-flat)*r + Vector3.up*Mathf.Lerp(55f,9f,Mathf.Clamp01(t/12f)); }
            var look = Mode == "Pilot" ? forward : (smoothCenter-desired).normalized;
            _position = Vector3.Lerp(_position,desired,1f-Mathf.Exp(-10f*dt));
            if (look.sqrMagnitude > .001f) _rotation = Quaternion.Slerp(_rotation,Quaternion.LookRotation(look,Vector3.up),1f-Mathf.Exp(-10f*dt));
        }
        private void ApplyView()
        {
            if (!_initialized || _camera == null) return;
            _camera.transform.SetPositionAndRotation(_position,_rotation);
            _camera.nearClipPlane = Mathf.Min(_savedNear,0.3f);
        }
        private void RenderSRP(ScriptableRenderContext context, Camera cam) { RenderView(cam); }
        private void RenderView(Camera cam) { if (FollowEnabled && cam == _camera) ApplyView(); }
    }
}
