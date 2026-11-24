using System;
using System.Globalization;
using Game.Objects;
using Unity.Mathematics;
using Colossal.UI.Binding;
using CS2FlightTakeover.Systems;
using Game;
using Game.UI;
using Unity.Entities;

namespace CS2FlightTakeover.UI
{
    public sealed partial class FlightUISystem : UISystemBase
    {
        private const string Group = "CS2FlightTakeover";
        private AircraftDiscoverySystem Discovery => World.GetExistingSystemManaged<AircraftDiscoverySystem>();
        private AircraftSelectionSystem Selection => World.GetExistingSystemManaged<AircraftSelectionSystem>();
        private AircraftTakeoverSystem Takeover => World.GetExistingSystemManaged<AircraftTakeoverSystem>();
        private AircraftCameraSystem Camera => World.GetExistingSystemManaged<AircraftCameraSystem>();
        private FlightInputSystem Input => World.GetExistingSystemManaged<FlightInputSystem>();
        protected override void OnCreate()
        {
            base.OnCreate();
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"NavigationMarkers",()=>World.GetExistingSystemManaged<NavigationSystem>()?.Markers??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"NavigationPosition",()=>Takeover==null?"0|0|0":string.Join("|",Takeover.Position.x.ToString(CultureInfo.InvariantCulture),Takeover.Position.z.ToString(CultureInfo.InvariantCulture),Takeover.HeadingDegrees.ToString(CultureInfo.InvariantCulture))));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"ImpactAlert",()=>Takeover?.ImpactAlert??""));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"SurfaceStatus",()=>Takeover?.SurfaceStatus??"Unavailable"));
            AddUpdateBinding(new GetterValueBinding<bool>(Group,"CollisionEnabled",()=>Takeover?.CollisionEnabled??true));
            AddBinding(new TriggerBinding(Group,"ToggleCollision",()=>{if(Takeover!=null)Takeover.CollisionEnabled=!Takeover.CollisionEnabled;}));
            AddBinding(new TriggerBinding<string>(Group,"ConfigureBoundary",v=>Takeover?.ConfigureBoundary(v)));
            AddBinding(new TriggerBinding(Group,"ReturnToMap",()=>Takeover?.ReturnToMap()));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"Boundary",()=>Takeover == null ? "Assisted|7000|1000|0|0" : string.Join("|",Takeover.BoundaryMode,Takeover.HalfExtent.ToString(CultureInfo.InvariantCulture),Takeover.WarningDistance.ToString(CultureInfo.InvariantCulture),Takeover.BoundaryDistance.ToString("0",CultureInfo.InvariantCulture),Takeover.VerticalSpeed.ToString("0.0",CultureInfo.InvariantCulture))));
            AddBinding(new TriggerBinding(Group,"DetectControllers",()=>Input?.Detect()));
            AddBinding(new TriggerBinding(Group,"ResetAssignments",()=>Input?.ResetAssignments()));
            AddBinding(new TriggerBinding(Group,"SaveProfile",()=>Input?.SaveProfile()));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"NativeStatus",()=>Input?.NativeStatus??"Unavailable"));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"RawInputs",()=>Input?.Raw()??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"Devices",()=>Input?.Devices()??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"Bindings",()=>Input?.Bindings()??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"InputLive",()=>Input?.Live??"0|0|0|0"));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"InputStatus",()=>Input?.Status??"Unavailable"));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"Learning",()=>Input?.Pending??""));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"Tuning",()=> (Input?.Deadzone??.05f).ToString(CultureInfo.InvariantCulture)+"|"+(Input?.Expo??.25f).ToString(CultureInfo.InvariantCulture)));
            AddBinding(new TriggerBinding<string>(Group,"Learn",v=>Input?.Learn(v)));
            AddBinding(new TriggerBinding<string>(Group,"Invert",v=>Input?.Invert(v)));
            AddBinding(new TriggerBinding<string>(Group,"ClearAxis",v=>Input?.Clear(v)));
            AddBinding(new TriggerBinding<string>(Group,"Tune",v=>Input?.Tune(v)));
            AddBinding(new TriggerBinding(Group,"CancelLearn",()=>Input?.Cancel()));
            AddBinding(new TriggerBinding<string>(Group,"CameraAngle",v=>Camera?.SetAngle(v)));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"CameraAngles",()=>Camera?.Angles??"0|18"));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group, "Aircraft", GetAircraft, new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string>(Group, "Telemetry", GetTelemetry));
            AddUpdateBinding(new GetterValueBinding<string>(Group, "Selected", () => Selection != null && Selection.HasSelection ? Selection.Selected.Index + ":" + Selection.Selected.Version : ""));
            AddUpdateBinding(new GetterValueBinding<bool>(Group, "Flying", () => Takeover != null && Takeover.Active));
            AddUpdateBinding(new GetterValueBinding<bool>(Group, "Following", () => Camera != null && Camera.FollowEnabled));
            AddUpdateBinding(new GetterValueBinding<string>(Group, "CameraDistance", () => (Camera?.Distance ?? 65f).ToString("0", CultureInfo.InvariantCulture)));
            AddBinding(new TriggerBinding<string>(Group, "SetCameraDistance", value => Camera?.SetDistance(value)));
            AddUpdateBinding(new GetterValueBinding<string>(Group, "CameraMode", () => Camera?.Mode ?? "Chase"));
            AddBinding(new TriggerBinding<string>(Group, "SetCameraMode", mode => { if (Camera != null && Selection != null && Selection.HasSelection) { Camera.SetMode(mode); Camera.FollowEnabled = true; } }));
            AddBinding(new TriggerBinding<string>(Group, "Select", SelectAircraft));
            AddBinding(new TriggerBinding(Group, "TakeControl", TakeControl));
            AddBinding(new TriggerBinding(Group, "Stop", Stop));
            AddBinding(new TriggerBinding(Group, "Follow", ToggleFollow));
            AddBinding(new TriggerBinding(Group, "Release", Release));
        }
        private string[] GetAircraft()
        {
            if (Discovery == null) return Array.Empty<string>();
            var entries = Discovery.Entries;
            var result = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                result[i] = e.Entity.Index + "|" + e.Entity.Version + "|" + (e.Kind ?? "Aircraft").Replace("|", " ") + "|" + e.Speed.ToString("0.0", CultureInfo.InvariantCulture);
            }
            return result;
        }
        private string GetTelemetry()
        {
            if (Takeover == null) return "0|0|0|0|0";
            return string.Join("|", Takeover.Speed.ToString("0.0", CultureInfo.InvariantCulture), Takeover.Throttle.ToString("0.000", CultureInfo.InvariantCulture), Takeover.PitchDegrees.ToString("0.0", CultureInfo.InvariantCulture), Takeover.BankDegrees.ToString("0.0", CultureInfo.InvariantCulture), Takeover.HeadingDegrees.ToString("0.0", CultureInfo.InvariantCulture), (Selection != null && Selection.HasSelection && EntityManager.HasComponent<Transform>(Selection.Selected) ? EntityManager.GetComponentData<Transform>(Selection.Selected).m_Position.y : 0f).ToString("0.0",CultureInfo.InvariantCulture));
        }
        private void SelectAircraft(string id)
        {
            if (Takeover != null && Takeover.Active) return;
            if (!TryParseEntity(id, out Entity entity) || Selection == null) return;
            if (!Selection.TrySelect(entity)) return;
            if (Takeover != null && Takeover.Active) Takeover.Stop();
            if (Camera != null) Camera.FollowEnabled = true;
        }
        private static bool TryParseEntity(string id, out Entity entity)
        {
            entity = Entity.Null;
            var parts = (id ?? "").Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int index) || !int.TryParse(parts[1], out int version)) return false;
            entity = new Entity { Index = index, Version = version };
            return true;
        }
        private void TakeControl()
        {
            if (Takeover == null || Selection == null || !Selection.HasSelection) return;
            if (Takeover.Begin(Selection.Selected) && Camera != null) Camera.FollowEnabled = true;
        }
        private void Stop() { Takeover?.Stop(); }
        private void ToggleFollow()
        {
            if (Camera == null) return;
            if (Camera.FollowEnabled) Camera.StopFollow();
            else if (Selection != null && Selection.HasSelection) Camera.FollowEnabled = true;
        }
        private void Release() { Takeover?.Stop(); Camera?.StopFollow(); Selection?.Release(); }
    }
}
