using Colossal.UI.Binding;
using CS2FlightTakeover.Systems;
using Game;
using Game.Objects;
using Game.Prefabs;
using Game.UI;
using System;
using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.UI
{
    public sealed partial class FlightUISystem : UISystemBase
    {
        private StartFlight.StartFlightSystem StartFlightMode => World.GetExistingSystemManaged<StartFlight.StartFlightSystem>();
        private const string Group = "CS2FlightTakeover";
        private AircraftDiscoverySystem Discovery => World.GetExistingSystemManaged<AircraftDiscoverySystem>();
        private AircraftSelectionSystem Selection => World.GetExistingSystemManaged<AircraftSelectionSystem>();
        private AircraftTakeoverSystem Takeover => World.GetExistingSystemManaged<AircraftTakeoverSystem>();
        private AircraftCameraSystem Camera => World.GetExistingSystemManaged<AircraftCameraSystem>();
        private FlightInputSystem Input => World.GetExistingSystemManaged<FlightInputSystem>();

        private ImageSystem Images =>
    World.GetExistingSystemManaged<ImageSystem>();

        protected override void OnCreate()
        {
            base.OnCreate();
            AddUpdateBinding(new GetterValueBinding<string>(Group,"StartState",()=>StartFlightMode?.State??"Idle"));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"StartStatus",()=>StartFlightMode?.Status??""));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"StartAircraft",()=>StartFlightMode?.Aircraft??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"StartStands",()=>StartFlightMode?.Stands??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"StartServices",()=>StartFlightMode?.Services??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"GroundSnapshot",()=>StartFlightMode?.GroundSnapshot??"0|Idle|0|Flight is not ready||0:0"));
            AddBinding(new TriggerBinding<string>(Group,"SelectGroundPreset",v=>StartFlightMode?.SelectGroundPreset(v)));
            AddBinding(new TriggerBinding<string>(Group,"EditGroundPreset",v=>StartFlightMode?.EditGroundPreset(v)));
            AddBinding(new TriggerBinding<string>(Group,"SaveGroundPreset",v=>StartFlightMode?.SaveGroundPreset(v)));
            AddBinding(new TriggerBinding(Group,"RefreshGroundAssets",()=>StartFlightMode?.RefreshGroundAssets()));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"ServiceSession",()=> (StartFlightMode?.ServiceSession??0).ToString(CultureInfo.InvariantCulture)));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"ServiceStates",()=>StartFlightMode?.ServiceStates??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string[]>(Group,"ServiceAssets",()=>StartFlightMode?.ServiceAssets??Array.Empty<string>(),new ArrayWriter<string>(new StringWriter())));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"CustomDeparture",()=>StartFlightMode?.Departure??""));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"DepartureRevision",()=> (StartFlightMode?.DepartureRevision??0).ToString()));
            AddUpdateBinding(new GetterValueBinding<bool>(Group,"PickingDeparture",()=>StartFlightMode?.PickingDeparture??false));
            AddUpdateBinding(new GetterValueBinding<bool>(Group,"HasDeparture",()=>StartFlightMode?.HasDeparture??false));
            AddBinding(new TriggerBinding<string>(Group,"CaptureDeparture",v=>StartFlightMode?.CaptureDeparture(v)));
            AddBinding(new TriggerBinding(Group,"StartScan",()=>StartFlightMode?.Scan()));
            AddBinding(new TriggerBinding<string>(Group,"StartBegin",v=>StartFlightMode?.Begin(v)));
            AddBinding(new TriggerBinding(Group,"StartReady",()=>StartFlightMode?.Ready()));
            AddBinding(new TriggerBinding(Group,"StartEnd",()=>StartFlightMode?.End()));
            AddBinding(new TriggerBinding<string>(Group,"StartService",v=>StartFlightMode?.Service(v)));
            AddBinding(new TriggerBinding<string>(Group,"StartServicePosition",v=>StartFlightMode?.QueueServicePosition(v)));
            AddUpdateBinding(new GetterValueBinding<string>(Group,"PushbackState",()=> (Takeover?.IsOwnedFlight==true ? Takeover.PushbackDirection : 0).ToString()));
            AddUpdateBinding(new GetterValueBinding<bool>(Group,"GroundServiceAvailable",()=>StartFlightMode?.CanService??false));
            AddBinding(new TriggerBinding(Group,"StartDismiss",()=>StartFlightMode?.Dismiss()));
            AddUpdateBinding(new GetterValueBinding<bool>(Group,"ParkingBrake",()=>Takeover?.ParkingBrake??false));
            AddBinding(new TriggerBinding<string>(Group,"Pushback",v=>{if(StartFlightMode?.State=="Flying"&&Takeover!=null&&Takeover.IsOwnedFlight&&Takeover.Controlled==StartFlightMode.Plane&&int.TryParse(v,out int n)&&n>=0&&n<=3&&(n==0||(Takeover.Grounded&&Takeover.Speed<.5f))){Takeover.SetPushback(n);}}));
            AddBinding(new TriggerBinding(Group,"ToggleBrake",()=>{if(StartFlightMode?.State=="Flying"&&Takeover!=null&&Takeover.IsOwnedFlight&&Takeover.Controlled==StartFlightMode.Plane&&Takeover.Grounded&&Takeover.PushbackDirection==0)Takeover.ParkingBrake=!Takeover.ParkingBrake;}));
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
        protected override void OnUpdate()
        {
            StartFlightMode?.MaintainSession();
            StartFlightMode?.MaintainPlacementActivation();
            // Ground service processing is independent of vehicle visibility and PreCulling.
            // The existing render callback may also call it; the frame guard prevents double steps.
            StartFlightMode?.AdvanceServices();
            base.OnUpdate();
        }
        private string[] GetAircraft()
        {
            if (Discovery == null)
                return Array.Empty<string>();

            var entries = Discovery.Entries;
            var result = new string[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];

                string name = (e.Kind ?? "Aircraft")
                    .Replace("|", " ");

                string speed = e.Speed.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                );

                string thumbnail = GetAircraftThumbnail(e.Entity);

                result[i] = string.Join(
                    "|",
                    e.Entity.Index,
                    e.Entity.Version,
                    name,
                    speed,
                    thumbnail
                );
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
            if ((StartFlightMode != null && (StartFlightMode.State != "Idle" || StartFlightMode.PickingDeparture)) || (Takeover != null && Takeover.Active)) return;
            if (!TryParseEntity(id, out Entity entity) || Selection == null) return;
            if (!Selection.TrySelect(entity)) return;
            if (Takeover != null && Takeover.Active) Takeover.Stop();
            if (Camera != null) { if (Camera.Mode == "Cinematic") Camera.SetMode("Chase"); Camera.FollowEnabled = true; }
        }

        private string GetAircraftThumbnail(Entity aircraft)
        {
            if (aircraft == Entity.Null)
                return string.Empty;

            if (!EntityManager.Exists(aircraft))
                return string.Empty;

            if (!EntityManager.HasComponent<PrefabRef>(aircraft))
                return string.Empty;

            Entity prefab = EntityManager
                .GetComponentData<PrefabRef>(aircraft)
                .m_Prefab;

            if (prefab == Entity.Null)
                return string.Empty;

            if (!EntityManager.Exists(prefab))
                return string.Empty;

            if (Images == null)
                return string.Empty;

            return Images.GetThumbnail(prefab) ?? string.Empty;
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
            if ((StartFlightMode != null && (StartFlightMode.State != "Idle" || StartFlightMode.PickingDeparture)) || Takeover == null || Takeover.Active || Selection == null || !Selection.HasSelection) return;
            if (Takeover.Begin(Selection.Selected) && Camera != null) { if (Camera.Mode == "Cinematic") Camera.SetMode("Chase"); Camera.FollowEnabled = true; }
        }
        private void Stop() { Takeover?.Stop(); }
        private void ToggleFollow()
        {
            if (Camera == null) return;
            if (Camera.FollowEnabled) Camera.StopFollow();
            else if (Selection != null && Selection.HasSelection) Camera.FollowEnabled = true;
        }
        private void Release() { if(StartFlightMode?.PickingDeparture == true) StartFlightMode.CaptureDeparture("cancel"); if(StartFlightMode != null && StartFlightMode.State != "Idle") {StartFlightMode?.End();return;} Takeover?.Stop(); Camera?.StopFollow(); Selection?.Release(); }
    }
}
