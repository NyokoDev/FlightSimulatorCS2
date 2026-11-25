using System;
using System.Collections.Generic;
using System.Globalization;
using Game.UI;
using Game;
using Game.Objects;
using Game.Common;
using Game.Rendering;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using CS2FlightTakeover.Systems;

namespace CS2FlightTakeover.StartFlight {
public sealed partial class StartFlightSystem : GameSystemBase {
    EntityQuery objects;
    PrefabSystem prefabs;
    readonly Dictionary<string,Entity> aircraft = new Dictionary<string,Entity>();
    readonly Dictionary<string,Entity> stands = new Dictionary<string,Entity>();
    readonly Dictionary<string,Entity> services = new Dictionary<string,Entity>();
    sealed class ServiceMotion { public Entity Entity, Owner, Prefab; public int Session; public bool PendingCreation; public float3 Start, Target; public float Heading, StartHeading; public float Time; public bool Preview, Failed; public int SettleFrames; public bool HasPose, RenderErrorLogged; public float3 PosePosition, PoseVelocity; public quaternion PoseRotation; public float NextRenderAudit, NextRenderRetry; public int RenderRetries, RenderAudits, SuppressionCount; public bool? LastCullPassed; }
    sealed class NativeServiceTicket {
        public string Slot="";
        public ServiceMotion Motion = null!;
        public Entity Definition;
        public int Seed;
        public int EmittedFrame=-1;
        public float Deadline;
        public float3 Position;
        public bool Expired; public float NextPoll;
    }
    readonly List<NativeServiceTicket> nativeTickets = new List<NativeServiceTicket>();
    EntityQuery nativeObjects, nativeDefinitions;
    sealed class Retirement { public int Session; public float NextCheck; public int Retries; public bool Warned; }
    readonly Dictionary<Entity,Retirement> retiringServices = new Dictionary<Entity,Retirement>();
    readonly Dictionary<string,string> pendingDispatches = new Dictionary<string,string>();
    readonly Dictionary<string,string> pendingPositions = new Dictionary<string,string>();
    private SurfaceProbe serviceTerrain;
    private int lastServiceFrame = -1;
    public bool CanService => ServiceBlockReason == "";
    public string ServiceBlockReason {
        get {
            if(State!="Flying")return "Flight is not ready";
            if(Flight==null||!Flight.IsOwnedFlight||Flight.Controlled!=Plane||Plane==Entity.Null||!EntityManager.Exists(Plane)||EntityManager.HasComponent<Deleted>(Plane))return "No active owned aircraft";
            if(!Flight.Grounded)return "Aircraft is airborne";
            if(Flight.PushbackDirection!=0)return "Stop pushback first";
            if(!Flight.ParkingBrake)return "Engage the parking brake";
            if(!math.isfinite(Flight.Speed)||Flight.Speed>=.5f)return "Stop the aircraft first";
            return "";
        }
    }
    string serviceMessage="";
    static string Clean(string value)=>(value??"").Replace("|"," ").Replace("\r"," ").Replace("\n"," ");
    // One scalar binding is delivered atomically, including current aircraft, readiness, catalog and service rows.
    public string GroundSnapshot {
        get {
            var rows=new List<string>{string.Join("|",ServiceSession.ToString(CultureInfo.InvariantCulture),State,CanService?"1":"0",Clean(ServiceBlockReason),Clean(serviceMessage),Key(Plane))};
            foreach(var item in Services)rows.Add("P|"+item);
            for(int i=0;i<4;i++){
                string key=i.ToString(CultureInfo.InvariantCulture), status="Not requested", asset="";
                if(activeServices.TryGetValue(key,out var m)&&m.PendingCreation){
                    asset=Key(m.Prefab);status="Initializing";
                }else if(activeServices.TryGetValue(key,out m)&&LiveService(m)){
                    if(EntityManager.HasComponent<PrefabRef>(m.Entity))asset=Key(EntityManager.GetComponentData<PrefabRef>(m.Entity).m_Prefab);
                    status=m.Preview?"Preview":m.Time>=100000?"Arrived":"On the way";
                }else if(activeServices.ContainsKey(key))status="Failed: entity removed";
                if(serviceErrors.TryGetValue(key,out var error))status=error;
                rows.Add("S|"+key+"|"+Clean(status)+"|"+asset);
            }
            AppendPresetRows(rows);
            return string.Join("\n",rows);
        }
    }
    readonly Dictionary<string,ServiceMotion> activeServices = new Dictionary<string,ServiceMotion>();
    public int ServiceSession {get;private set;}
    public string Departure {get;private set;} = "";
    public int DepartureRevision {get;private set;}
    static bool Number(string s,out float v)=>float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&math.isfinite(v);
    public bool PickingDeparture {get;private set;}
    public bool HasDeparture {get;private set;}
    float3 departurePosition;
    AircraftPlacementTool Placement => World.GetExistingSystemManaged<AircraftPlacementTool>();
    public void CaptureDeparture(string value){
        if(value=="cancel"){PlacementStopped();Placement?.RequestDisable();return;}
        if(State!="Idle"||Flight.Active||Placement==null||PickingDeparture)return;
        HasDeparture=false;Departure="";PickingDeparture=true;Status="Click terrain to select departure; right-click to cancel.";
        Camera?.StopFollow();
        Placement.Activate();
    }
    public void PlacementHoverChanged(bool valid){if(PickingDeparture)Status=valid?"Click the blue marker to select departure. Right-click to cancel.":"Move the cursor over terrain. Right-click to cancel.";}
    public void MaintainPlacementActivation(){if(PickingDeparture)Placement?.EnsureActivation();}
    public void PlacementStopped(){if(PickingDeparture){PickingDeparture=false;Status="Departure selection cancelled";}}
    public void SelectDeparture(float3 position){
        if(!PickingDeparture||!math.all(math.isfinite(position)))return;
        departurePosition=position;HasDeparture=true;PickingDeparture=false;
        Departure=position.x.ToString("R",CultureInfo.InvariantCulture)+"|"+position.z.ToString("R",CultureInfo.InvariantCulture);
        DepartureRevision++;Status="Custom departure selected";Mod.Log.Info($"Native departure raycast: {position}");
    }
    readonly Dictionary<string,string> serviceErrors = new Dictionary<string,string>();
    // Session prefix prevents UI from interpreting a previous flight's binding as current.
    string SessionRow(string key,string value)=>ServiceSession.ToString(CultureInfo.InvariantCulture)+"|"+key+"|"+value;
    public string[] ServiceStates { get { var rows=new List<string>(); foreach(var pair in serviceErrors) rows.Add(SessionRow(pair.Key,pair.Value)); foreach(var pair in activeServices) if(!serviceErrors.ContainsKey(pair.Key)) rows.Add(SessionRow(pair.Key,pair.Value.PendingCreation?"Initializing":!LiveService(pair.Value)?"Failed: entity removed":pair.Value.Preview?"Preview":pair.Value.Time>=100000?"Arrived":"On the way")); return rows.ToArray(); } }
    public string[] ServiceAssets { get { var rows=new List<string>();foreach(var pair in activeServices)if(pair.Value.PendingCreation)rows.Add(SessionRow(pair.Key,Key(pair.Value.Prefab)));else if(LiveService(pair.Value)&&EntityManager.HasComponent<PrefabRef>(pair.Value.Entity))rows.Add(SessionRow(pair.Key,Key(EntityManager.GetComponentData<PrefabRef>(pair.Value.Entity).m_Prefab)));return rows.ToArray(); } }
    readonly List<Entity> spawned = new List<Entity>();
    public string[] Aircraft = Array.Empty<string>(), Stands = Array.Empty<string>(), Services = Array.Empty<string>();
    public string State {get;private set;} = "Idle";
    public string Status {get;private set;} = "Select a departure position";
    public Entity Plane {get;private set;} = Entity.Null;
    AircraftTakeoverSystem Flight => World.GetExistingSystemManaged<AircraftTakeoverSystem>();
    AircraftCameraSystem Camera => World.GetExistingSystemManaged<AircraftCameraSystem>();
    AircraftSelectionSystem Selection => World.GetExistingSystemManaged<AircraftSelectionSystem>();
    protected override void OnCreate(){base.OnCreate();objects=GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Transform>());prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
        nativeObjects=GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<PseudoRandomSeed>(),ComponentType.ReadOnly<Transform>(),ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
        nativeDefinitions=GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
    }
    static string Key(Entity e)=>e.Index+":"+e.Version;
    public void Scan(){
        aircraft.Clear();stands.Clear();
        var a=new List<string>();var b=new List<string>();
        // Include registered prefab entities, even when no instance is placed.
        using(var all=EntityManager.GetAllEntities(Allocator.Temp)) foreach(var e in all){
            if(!prefabs.TryGetPrefab(e,out PrefabBase p)||p==null)continue;
            string name=(p.name??"").Replace("|"," ");
            if(p is AirplanePrefab){aircraft[Key(e)]=e;a.Add(Key(e)+"|"+name+"|"+(World.GetExistingSystemManaged<ImageSystem>()?.GetThumbnail(e)??""));}

        }
        using(var es=objects.ToEntityArray(Allocator.Temp)) foreach(var e in es){
            var r=EntityManager.GetComponentData<PrefabRef>(e);if(!prefabs.TryGetPrefab(r.m_Prefab,out PrefabBase p)||p==null)continue;
            string n=(p.name??"").ToLowerInvariant();if(!(n.Contains("runway")||n.Contains("stand")||n.Contains("gate")||n.Contains("aircraft parking")))continue;
            stands[Key(e)]=e;b.Add(Key(e)+"|"+p.name.Replace("|"," "));
        }
        a.Sort();b.Sort();Aircraft=a.ToArray();Stands=b.ToArray();RefreshServiceCatalog();if(State=="Idle"&&!HasDeparture&&!PickingDeparture)Status="Select a departure position";
    }
    void RefreshServiceCatalog(){
        var found=new Dictionary<string,Entity>();var rows=new List<string>();
        using(var all=EntityManager.GetAllEntities(Allocator.Temp))foreach(var e in all){
            if(!EntityManager.HasComponent<ObjectData>(e)||EntityManager.HasComponent<Deleted>(e))continue;
            if(!prefabs.TryGetPrefab(e,out PrefabBase p)||p==null||p is AirplanePrefab)continue;
            string name=Clean(p.name),n=name.ToLowerInvariant();
            if(n.Contains("mesh")||n.Contains("lod")||!(n.Contains("catering")||n.Contains("baggage")||n.Contains("stairs")))continue;
            found[Key(e)]=e;rows.Add(Key(e)+"|"+name);
        }
        // Publish only after a complete scan; no empty catalog during refresh.
        services.Clear();foreach(var pair in found)services.Add(pair.Key,pair.Value);
        rows.Sort();Services=rows.ToArray();
    }
    public void RefreshGroundAssets(){
        try{RefreshServiceCatalog();serviceMessage=Services.Length==0?"No ground handling prefabs found":"Ground handling assets refreshed";Mod.Log.Info($"Ground session {ServiceSession}: catalog {Services.Length} prefabs");}
        catch(Exception ex){serviceMessage="Asset refresh failed";Mod.Log.Warn("Ground asset refresh failed: "+ex);}
    }
    Entity Spawn(Entity prefab,float3 pos,quaternion rot){
        // Use the prefab runtime archetype; prefer stopped vehicle archetypes to avoid live routes.
        if(!EntityManager.HasComponent<ObjectData>(prefab)) throw new InvalidOperationException("Prefab has no runtime ObjectData");
        var archetype=EntityManager.HasComponent<MovingObjectData>(prefab)
            ? EntityManager.GetComponentData<MovingObjectData>(prefab).m_StoppedArchetype
            : EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;
        var e=EntityManager.CreateEntity(archetype);
        spawned.Add(e);
        EntityManager.SetComponentData(e,new PrefabRef{m_Prefab=prefab});
        EntityManager.SetComponentData(e,new Transform{m_Position=pos,m_Rotation=rot});
        if(!EntityManager.HasComponent<Game.Common.Created>(e)) EntityManager.AddComponent<Game.Common.Created>(e);
        if(!EntityManager.HasComponent<Game.Common.Updated>(e)) EntityManager.AddComponent<Game.Common.Updated>(e);
        Mod.Log.Info($"StartFlight spawned {e} from {prefab} at {pos}");
        return e;
    }
    public void Begin(string value){
        if(State!="Idle"||Flight.Active)return;
        var p=value.Split('|');if(PickingDeparture||!HasDeparture||p.Length!=2||!aircraft.TryGetValue(p[0],out var prefab)||!EntityManager.Exists(prefab)||!Number(p[1],out float heading)){Status="Select an aircraft and departure location first.";return;}
        try{
            // A new flight never inherits old ground entities, animation state or queued UI events.
            Dismiss();lastServiceFrame=-1;serviceTerrain=null;serviceMessage="";ServiceSession++;
            BeginGroundPreset(prefab);
            Mod.Log.Info($"Ground service session {ServiceSession} starting at {departurePosition}");
            var t=new Transform{m_Position=departurePosition,m_Rotation=quaternion.RotateY(math.radians(heading))};var f=math.mul(t.m_Rotation,new float3(0,0,1));
            Plane=Spawn(prefab,t.m_Position,quaternion.RotateY(math.atan2(f.x,f.z)));
            Selection.SelectOwned(Plane);Camera.SetMode("Cinematic");Camera.FollowEnabled=true;State="Cinematic";Status="Ready to fly";
        }catch(Exception e){Mod.Log.Warn("StartFlight spawn failed: "+e);End();Status=e.Message;}
    }
    public void Ready(){
        if(State!="Cinematic")return;
        if(!Flight.Begin(Plane,true)){Mod.Log.Warn($"Ground session {ServiceSession}: Ready failed for {Plane}");End();return;}
        Camera.SetMode("Chase");State="Flying";Status="Parking brake on";
        // Fresh catalog at every successful Ready, independent of whether the planner was mounted.
        RefreshGroundAssets();lastServiceFrame=-1;
        Mod.Log.Info($"Ground session {ServiceSession}: Ready owner={Plane}, controlled={Flight.Controlled}, position={Flight.Position}, available={CanService}, reason={ServiceBlockReason}, prefabs={Services.Length}");
    }
    static bool ServiceKey(string key)=>key=="0"||key=="1"||key=="2"||key=="3";
    bool LiveService(ServiceMotion m)=>m.Session==ServiceSession&&m.Owner==Plane&&m.Entity!=Entity.Null&&m.Entity!=Plane&&EntityManager.Exists(m.Entity)&&!EntityManager.HasComponent<Deleted>(m.Entity)&&EntityManager.HasComponent<Transform>(m.Entity);
    // Payload: session|prefab|slot|target X|target Z|heading|asset name.
    // Session validation is strict; prefab names provide a recovery path for stale handles.
    bool ReadRequest(string value,out string key,out Entity prefab,out float3 targetOffset,out string reason)
    {
        key="";prefab=Entity.Null;targetOffset=float3.zero;reason="";
        var p=(value??"").Split('|');
        if(p.Length!=7||!ServiceKey(p[2])){reason="Invalid request format";return false;}
        key=p[2];
        if(!int.TryParse(p[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out int session)||session!=ServiceSession){reason="Flight session changed; request again";return false;}
        if(!Number(p[3],out float x)||!Number(p[4],out float z)||!Number(p[5],out float yaw)||math.abs(x)>100||math.abs(z)>100||math.abs(yaw)>180){reason="Invalid target offsets";return false;}
        if(!ValidServicePrefab(p[1],p[6],out prefab)){
            RefreshServiceCatalog();
            if(!ValidServicePrefab(p[1],p[6],out prefab)){
                foreach(var row in Services){var parts=row.Split('|');if(parts.Length==2&&string.Equals(parts[1],p[6],StringComparison.OrdinalIgnoreCase)&&ValidServicePrefab(parts[0],p[6],out prefab))break;}
            }
        }
        if(prefab==Entity.Null){reason="Ground handling asset not found; refresh assets";return false;}
        targetOffset=new float3(x,z,yaw);return true;
    }
    bool ValidServicePrefab(string key,string name,out Entity prefab){
        prefab=Entity.Null;
        if(!services.TryGetValue(key,out var e)||!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e)||!EntityManager.HasComponent<ObjectData>(e)||!prefabs.TryGetPrefab(e,out PrefabBase p)||p==null||p is AirplanePrefab||!string.Equals(Clean(p.name),name,StringComparison.OrdinalIgnoreCase))return false;
        prefab=e;return true;
    }
    bool CheckRequest(string value,out string key,out Entity prefab,out float3 offset,bool report=true){
        key="";prefab=Entity.Null;offset=float3.zero;
        try{
            if(!ReadRequest(value,out key,out prefab,out offset,out string reason)){
                // Stale events must not change a current slot's state.
                if(report){serviceMessage=reason;Mod.Log.Warn($"Ground session {ServiceSession}: rejected {value}: {reason}");}return false;
            }
            if(!CanService){if(report){serviceMessage=ServiceBlockReason;serviceErrors[key]=ServiceBlockReason;Mod.Log.Warn($"Ground session {ServiceSession}: rejected slot {key}: {ServiceBlockReason}");}return false;}
            return true;
        }catch(Exception ex){if(report){serviceMessage="Ground request validation failed";Mod.Log.Warn("Ground request validation: "+ex);}return false;}
    }
    void CalculatePositions(string key,float3 targetOffset,out float3 target,out float3 start,out float heading,out float startHeading)
    {
        // Use the currently controlled aircraft, never the previous departure or old service entity.
        var origin=Flight.Position;
        var f=math.mul(Flight.Attitude,new float3(0,0,1));
        float yaw=math.atan2(f.x,f.z);
        var groundRotation=quaternion.RotateY(yaw);
        target=origin+math.mul(groundRotation,new float3(targetOffset.x,0,targetOffset.y));
        // Automatic nearby staging on the target's side, with different longitudinal slots.
        float side=math.abs(targetOffset.x)>.1f?math.sign(targetOffset.x):(key=="1"||key=="2"?-1f:1f);
        float stagingZ=key=="0"?24f:key=="1"?36f:key=="2"?-36f:-24f;
        start=origin+math.mul(groundRotation,new float3(side*40f,0,stagingZ));
        serviceTerrain??=new SurfaceProbe(World,false);
        target.y=serviceTerrain.Read(target,out float h)?h:origin.y;
        start.y=serviceTerrain.Read(start,out h)?h:origin.y;
        heading=yaw+math.radians(targetOffset.z);
        float3 direction=target-start;startHeading=math.lengthsq(direction.xz)>.01f?math.atan2(direction.x,direction.z):heading;
    }
    ServiceMotion GetOrSpawnService(string key,Entity prefab,float3 position,float heading)
    {
        activeServices.TryGetValue(key,out var old);
        if(old!=null && old.Prefab==prefab && !old.Failed && (old.PendingCreation || LiveService(old)))return old;
        // Only native static-object definitions are accepted here. Aircraft spawning is unchanged.
        if(!prefabs.TryGetPrefab(prefab,out PrefabBase p)||!(p is StaticObjectPrefab))
            throw new InvalidOperationException("Ground service requires a static object prefab");
        if(old!=null&&old.Entity!=Entity.Null&&old.Entity!=Plane){RetireService(old.Entity);spawned.Remove(old.Entity);}
        var motion=new ServiceMotion{Entity=Entity.Null,Owner=Plane,Prefab=prefab,Session=ServiceSession,PendingCreation=true};
        activeServices[key]=motion;
        nativeTickets.Add(new NativeServiceTicket{Slot=key,Motion=motion,Position=position,Deadline=UnityEngine.Time.realtimeSinceStartup+15f});
        return motion;
    }
    bool CurrentTicket(NativeServiceTicket ticket)=>State=="Flying"&&ticket.Motion.Session==ServiceSession&&ticket.Motion.Owner==Plane&&activeServices.TryGetValue(ticket.Slot,out var m)&&ReferenceEquals(m,ticket.Motion)&&m.PendingCreation;
    // A native permanent definition is processed by GenerateObjectsSystem in Modification1.
    // Its random seed (16 bits) is retained on the created object's PseudoRandomSeed.
    // Reserve a seed not used by any current instance/definition of this prefab. Never copy render batch indices.
    int ReserveNativeSeed(Entity prefab)
    {
        var occupied=new HashSet<int>();
        using(var prefabRefs=nativeObjects.ToComponentDataArray<PrefabRef>(Allocator.Temp))
        using(var seeds=nativeObjects.ToComponentDataArray<PseudoRandomSeed>(Allocator.Temp))
            for(int i=0;i<prefabRefs.Length;i++)if(prefabRefs[i].m_Prefab==prefab)occupied.Add(seeds[i].m_Seed);
        using(var definitions=nativeDefinitions.ToEntityArray(Allocator.Temp))
            foreach(var e in definitions){var d=EntityManager.GetComponentData<CreationDefinition>(e);if(d.m_Prefab==prefab)occupied.Add((ushort)d.m_RandomSeed);}
        foreach(var t in nativeTickets)if(t.Motion.Prefab==prefab&&t.Seed>0)occupied.Add(t.Seed);
        for(int seed=1;seed<=ushort.MaxValue;seed++)if(!occupied.Contains(seed))return seed;
        throw new InvalidOperationException("No unique ground service creation seed available");
    }
    // Called BEFORE the native GenerateObjectsSystem; the definition lives for one modification cycle.
    internal void EmitNativeServiceDefinitions()
    {
        foreach(var ticket in nativeTickets){
            if(ticket.EmittedFrame>=0||!CurrentTicket(ticket)||!ticket.Motion.HasPose)continue;
            if(!CanService)continue;
            var m=ticket.Motion;
            Entity definition=Entity.Null;
            try{
                ticket.Seed=ReserveNativeSeed(m.Prefab);ticket.Position=m.PosePosition;
                definition=EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition,new CreationDefinition{m_Prefab=m.Prefab,m_Flags=CreationFlags.Permanent,m_RandomSeed=ticket.Seed});
                EntityManager.AddComponentData(definition,new ObjectDefinition{m_Position=ticket.Position,m_Rotation=m.PoseRotation,m_Probability=100,m_PrefabSubIndex=-1,m_Scale=1f,m_Intensity=1f,m_ParentMesh=-1,m_IsDecoration=false});
                // Native permanent definitions intentionally include Deleted: consumed once, then cleaned up by the game.
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponent<Deleted>(definition);
                ticket.Definition=definition;ticket.EmittedFrame=UnityEngine.Time.frameCount;
                ticket.Deadline=UnityEngine.Time.realtimeSinceStartup+15f;
                Mod.Log.Info($"Ground native definition session={m.Session} slot={ticket.Slot} prefab={m.Prefab} seed={ticket.Seed} definition={definition} position={ticket.Position}");
            }catch(Exception ex){
                if(definition!=Entity.Null&&EntityManager.Exists(definition))EntityManager.DestroyEntity(definition); // not a runtime prop; generation has not run yet
                ticket.Expired=true;m.PendingCreation=false;m.Failed=true;serviceErrors[ticket.Slot]="Failed: native definition";
                Mod.Log.Warn($"Ground native definition failed slot={ticket.Slot}: {ex}");
            }
            break; // at most one new definition per native modification pass
        }
    }
    // Capture the real, native-initialized object, not a guessed entity or render index.
    // Keep emitted cancelled/expired tickets until resolved so a late result is retired safely.
    internal void ResolveNativeServiceDefinitions()
    {
        if(nativeTickets.Count==0)return;
        float now=UnityEngine.Time.realtimeSinceStartup;
        for(int i=nativeTickets.Count-1;i>=0;i--){
            var ticket=nativeTickets[i];var m=ticket.Motion;bool current=CurrentTicket(ticket);
            if(ticket.EmittedFrame<0){
                if(!current||now>ticket.Deadline){if(current){m.PendingCreation=false;m.Failed=true;serviceErrors[ticket.Slot]="Failed: native creation not scheduled";}nativeTickets.RemoveAt(i);}continue;
            }
            if(now<ticket.NextPoll)continue;ticket.NextPoll=now+(ticket.Expired?2f:.2f);
            if(EntityManager.Exists(ticket.Definition)){
                if(!ticket.Expired&&now>ticket.Deadline){ticket.Expired=true;if(current){m.PendingCreation=false;m.Failed=true;serviceErrors[ticket.Slot]="Failed: native definition not consumed";}Mod.Log.Warn($"Ground native definition not consumed {ticket.Definition}");}continue;
            } // native cleanup has not yet consumed the definition
            Entity match=Entity.Null;int count=0;
            using(var entities=nativeObjects.ToEntityArray(Allocator.Temp))
            using(var prefabRefs=nativeObjects.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            using(var seeds=nativeObjects.ToComponentDataArray<PseudoRandomSeed>(Allocator.Temp))
                for(int j=0;j<entities.Length;j++){
                    var e=entities[j];
                    if(e==Plane||prefabRefs[j].m_Prefab!=m.Prefab||seeds[j].m_Seed!=ticket.Seed)continue;
                    var t=EntityManager.GetComponentData<Transform>(e);
                    if(!math.all(math.isfinite(t.m_Position))||math.distancesq(t.m_Position,ticket.Position)>4f)continue;
                    match=e;count++;
                }
            if(count==1){
                nativeTickets.RemoveAt(i);
                if(!current||ticket.Expired){RetireService(match);Mod.Log.Info($"Ground native late/cancelled object retired {match} session={m.Session} slot={ticket.Slot}");continue;}
                m.Entity=match;m.PendingCreation=false;m.SettleFrames=3;spawned.Add(match);
                // Start normal movement/preview only after the game initialized the static object.
                try{ApplyServicePose(match,m.PosePosition,m.PoseRotation,m.PoseVelocity);}catch(Exception ex){m.Failed=true;serviceErrors[ticket.Slot]="Failed: initial native pose";Mod.Log.Warn($"Ground native initial pose failed {match}: {ex}");}
                Mod.Log.Info($"Ground native materialized session={m.Session} slot={ticket.Slot} entity={match} seed={ticket.Seed} prefab={m.Prefab} position={ticket.Position}");
            }else if(!ticket.Expired&&now>ticket.Deadline){
                ticket.Expired=true;
                if(current){m.PendingCreation=false;m.Failed=true;serviceErrors[ticket.Slot]="Failed: native object not found";}
                Mod.Log.Warn($"Ground native materialization timeout session={m.Session} slot={ticket.Slot} prefab={m.Prefab} seed={ticket.Seed} matches={count}");
            }else if(ticket.Expired&&now>ticket.Deadline+30f){
                Mod.Log.Warn($"Ground native late-result window closed: session={m.Session} slot={ticket.Slot} seed={ticket.Seed} matches={count}");nativeTickets.RemoveAt(i);
            }
        }
    }
    public void Service(string value)
    {
        if(!CheckRequest(value,out var key,out _,out _))return;
        pendingPositions.Remove(key);pendingDispatches[key]=value;
        if(serviceErrors.TryGetValue(key,out var error)&&error=="Failed: no render batches"&&activeServices.TryGetValue(key,out var old))old.Failed=true;
        serviceErrors.Remove(key);serviceMessage="Dispatch queued";
        Mod.Log.Info($"Ground session {ServiceSession}: queued dispatch {key} owner={Plane}");
    }
    void DispatchService(string value)
    {
        if(!CheckRequest(value,out var key,out var prefab,out var targetOffset))return;
        serviceErrors.Remove(key);serviceMessage="";
        try
        {
            CalculatePositions(key,targetOffset,out var target,out var start,out float heading,out float startHeading);
            var m=GetOrSpawnService(key,prefab,start,startHeading);
            m.Start=start;m.Target=target;m.Heading=heading;m.StartHeading=startHeading;m.Time=0;m.Preview=false;m.Failed=false;m.SettleFrames=0;
            WriteServicePose(m,start,quaternion.RotateY(startHeading),float3.zero);
            Mod.Log.Info($"Ground session {ServiceSession}: dispatch {key} {m.Entity} for {Plane}, start={start}, target={target}");
        }
        catch(Exception ex){serviceErrors[key]="Failed: "+ex.Message;serviceMessage="Dispatch failed";Mod.Log.Warn("Ground service failed: "+ex);}
    }
    // Coalesce live slider events. A preview may create the service if none exists yet.
    public void QueueServicePosition(string value)
    {
        if(!CheckRequest(value,out var key,out _,out _))return;
        pendingDispatches.Remove(key);pendingPositions[key]=value;
    }
    void ApplyServicePosition(string value)
    {
        if(!CheckRequest(value,out var key,out var prefab,out var targetOffset))return;
        CalculatePositions(key,targetOffset,out var target,out var start,out float heading,out float startHeading);
        bool alreadyDispatched=activeServices.TryGetValue(key,out var old)&&(old.PendingCreation||LiveService(old))&&!old.Preview&&old.Prefab==prefab;
        var m=GetOrSpawnService(key,prefab,target,heading);
        m.Start=start;m.Target=target;m.Heading=heading;m.StartHeading=startHeading;m.Time=100000;m.Preview=!alreadyDispatched;m.Failed=false;m.SettleFrames=3;
        WriteServicePose(m,target,quaternion.RotateY(heading),float3.zero);
        serviceErrors.Remove(key);serviceMessage="";
        if(!alreadyDispatched&&m!=old)Mod.Log.Info($"Ground session {ServiceSession}: preview {key} {m.Entity} at {target} for {Plane}");
    }
    void WriteServicePose(ServiceMotion m,float3 pos,quaternion rot,float3 velocity)
    {
        if((!m.PendingCreation&&!LiveService(m))||!math.all(math.isfinite(pos))||!math.all(math.isfinite(rot.value))||!math.all(math.isfinite(velocity)))return;
        m.HasPose=true;m.PosePosition=pos;m.PoseRotation=rot;m.PoseVelocity=velocity;
        if(!m.PendingCreation)ApplyServicePose(m.Entity,pos,rot,velocity);
    }
    void ApplyServicePose(Entity e,float3 pos,quaternion rot,float3 velocity)
    {
        if(e==Plane||!EntityManager.Exists(e)||EntityManager.HasComponent<Deleted>(e)||!EntityManager.HasComponent<Transform>(e)||!math.all(math.isfinite(pos))||!math.all(math.isfinite(rot.value)))return;
        var t=EntityManager.GetComponentData<Transform>(e);
        t.m_Position=pos;t.m_Rotation=rot;EntityManager.SetComponentData(e,t);
        if(EntityManager.HasComponent<Moving>(e)){var moving=EntityManager.GetComponentData<Moving>(e);moving.m_Velocity=velocity;EntityManager.SetComponentData(e,moving);}
        if(EntityManager.HasBuffer<TransformFrame>(e)){var frames=EntityManager.GetBuffer<TransformFrame>(e);for(int i=0;i<frames.Length;i++){var frame=frames[i];frame.m_Position=pos;frame.m_Rotation=rot;frame.m_Velocity=velocity;frames[i]=frame;}}
        if(EntityManager.HasComponent<InterpolatedTransform>(e)){var render=EntityManager.GetComponentData<InterpolatedTransform>(e);render.m_Position=pos;render.m_Rotation=rot;EntityManager.SetComponentData(e,render);}
        // Ordinary prop movement requests a spatial update; do not invalidate geometry batches on every slider tick.
        if(!EntityManager.HasComponent<Updated>(e))EntityManager.AddComponent<Updated>(e);
    }
    internal void AdvanceServices()
    {
        if(State!="Flying"||(activeServices.Count==0&&pendingPositions.Count==0&&pendingDispatches.Count==0))return;
        int frame=UnityEngine.Time.frameCount;if(lastServiceFrame==frame)return;lastServiceFrame=frame;
        if(!CanService){if(pendingDispatches.Count>0||pendingPositions.Count>0){serviceMessage=ServiceBlockReason;Mod.Log.Warn($"Ground session {ServiceSession}: queued commands cancelled: {ServiceBlockReason}");}pendingDispatches.Clear();pendingPositions.Clear();return;}
        foreach(var pair in pendingDispatches)DispatchService(pair.Value);
        pendingDispatches.Clear();
        foreach(var pair in pendingPositions){try{ApplyServicePosition(pair.Value);}catch(Exception ex){serviceErrors[pair.Key]="Failed: position update";Mod.Log.Warn("Service positioning: "+ex);}}
        pendingPositions.Clear();
        serviceTerrain??=new SurfaceProbe(World,false);
        float dt=math.clamp(UnityEngine.Time.unscaledDeltaTime,0f,.05f);if(dt<=0)return;
        foreach(var pair in activeServices)
        {
            var m=pair.Value;if(m.Failed||m.PendingCreation)continue;
            if(!LiveService(m)){m.Failed=true;serviceErrors[pair.Key]="Failed: entity removed";Mod.Log.Warn($"Ground session {ServiceSession}: service {pair.Key} lost entity {m.Entity} owner={m.Owner}, expected owner={Plane}");continue;}
            try{
            // Reassert a newly placed/arrived pose on the next few frames after native initialization.
            if(m.Preview||m.Time>=100000){if(m.SettleFrames>0){WriteServicePose(m,m.Target,quaternion.RotateY(m.Heading),float3.zero);m.SettleFrames--;}continue;}
            m.Time+=dt;float duration=math.max(1,math.distance(m.Start,m.Target)/4f);float u=math.saturate(m.Time/duration);
            var pos=math.lerp(m.Start,m.Target,u*u*(3-2*u));if(serviceTerrain.Read(pos,out float h))pos.y=h;
            var dir=m.Target-m.Start;float yaw=math.atan2(dir.x,dir.z);
            var approach=math.slerp(quaternion.RotateY(m.StartHeading),quaternion.RotateY(yaw),math.saturate(u*5));
            var rot=math.slerp(approach,quaternion.RotateY(m.Heading),math.saturate((u-.8f)*5));
            var old=m.HasPose?m.PosePosition:m.Start;
            WriteServicePose(m,pos,rot,u>=1?float3.zero:(pos-old)/dt);if(u>=1){m.Time=100000;m.SettleFrames=3;Mod.Log.Info($"Ground session {ServiceSession}: arrived {pair.Key} {m.Entity} owner={Plane} position={pos}");}
            }catch(Exception ex){m.Failed=true;serviceErrors[pair.Key]="Failed: service movement";Mod.Log.Warn($"Ground session {ServiceSession}: service {pair.Key} update failed: {ex}");}
        }
    }
    // Runs before native PreCullingSystem, even when a service has reached its target.
    // The movement loop's frame guard must never suppress this render reconciliation.
    internal void MaintainServiceRendering()
    {
        if(State!="Flying"||activeServices.Count==0)return;
        float now=UnityEngine.Time.realtimeSinceStartup;
        foreach(var pair in activeServices) {
            var m=pair.Value;
            if(!LiveService(m)||m.Failed||!m.HasPose)continue;
            try {
                var e=m.Entity;
                bool overridden=EntityManager.HasComponent<Overridden>(e);
                bool unspawned=EntityManager.HasComponent<Unspawned>(e);
                if(overridden)EntityManager.RemoveComponent<Overridden>(e);
                // Ground services are explicitly placed, not waiting for a native trip.
                if(unspawned)EntityManager.RemoveComponent<Unspawned>(e);
                if(overridden||unspawned) {
                    m.SuppressionCount++;
                    if(m.SuppressionCount==1||m.SuppressionCount%120==0)
                        Mod.Log.Info($"Ground render restored {e}: slot={pair.Key}, Overridden={overridden}, Unspawned={unspawned}, count={m.SuppressionCount}");
                }
                var t=EntityManager.GetComponentData<Transform>(e);
                bool drift=math.distancesq(t.m_Position,m.PosePosition)>1e-6f||!math.all(math.isfinite(t.m_Rotation.value))||math.abs(math.dot(t.m_Rotation.value,m.PoseRotation.value))<.999999f;
                if(EntityManager.HasComponent<InterpolatedTransform>(e)) {
                    var r=EntityManager.GetComponentData<InterpolatedTransform>(e);
                    drift|=math.distancesq(r.m_Position,m.PosePosition)>1e-6f||!math.all(math.isfinite(r.m_Rotation.value))||math.abs(math.dot(r.m_Rotation.value,m.PoseRotation.value))<.999999f;
                }
                if(drift)ApplyServicePose(e,m.PosePosition,m.PoseRotation,m.PoseVelocity);
                else if(overridden||unspawned)AircraftPoseSyncSystem.InvalidateOwnedPose(EntityManager,e);
                bool hasCull=EntityManager.HasComponent<CullingInfo>(e);
                var c=hasCull?EntityManager.GetComponentData<CullingInfo>(e):default;
                int groups=EntityManager.HasBuffer<MeshGroup>(e)?EntityManager.GetBuffer<MeshGroup>(e,true).Length:-1;
                int batches=-1, validBatches=0;
                if(EntityManager.HasBuffer<MeshBatch>(e)) {
                    var batchBuffer=EntityManager.GetBuffer<MeshBatch>(e,true);batches=batchBuffer.Length;
                    for(int i=0;i<batches;i++) {var b=batchBuffer[i];if(b.m_MeshGroup!=byte.MaxValue&&b.m_MeshIndex!=byte.MaxValue&&b.m_GroupIndex>=0&&b.m_InstanceIndex>=0)validBatches++;}
                }
                bool passed=hasCull&&c.m_PassedCulling!=0;
                // Do not fabricate mesh indices, culling indices, or force culling flags.
                // Request a limited number of refreshes if the game's renderer has not registered the object.
                if(passed&&validBatches>0){m.RenderRetries=0;if(serviceErrors.TryGetValue(pair.Key,out var renderError)&&renderError=="Failed: no render batches")serviceErrors.Remove(pair.Key);}
                else if(passed&&m.RenderRetries>=8&&!serviceErrors.ContainsKey(pair.Key)){serviceErrors[pair.Key]="Failed: no render batches";Mod.Log.Warn($"Ground native renderer has no batches for {e}, slot={pair.Key}");}
                else if(now>=m.NextRenderRetry&&m.RenderRetries<8) {
                    m.NextRenderRetry=now+.75f;m.RenderRetries++;
                    AircraftPoseSyncSystem.InvalidateOwnedPose(EntityManager,e);
                }
                bool changed=m.LastCullPassed.HasValue&&m.LastCullPassed.Value!=passed;
                if(now>=m.NextRenderAudit&&(m.RenderAudits<5||changed)) {
                    m.NextRenderAudit=now+2f;m.RenderAudits++;m.LastCullPassed=passed;
                    Mod.Log.Info($"Ground render audit session={ServiceSession} slot={pair.Key} entity={e}: position={m.PosePosition}, culling={hasCull}, passed={passed}, index={c.m_CullingIndex}, mask={c.m_Mask}, bounds={c.m_Bounds}, radius={c.m_Radius}, groups={groups}, batches={batches}, validBatches={validBatches}, restored={m.SuppressionCount}, retries={m.RenderRetries}, nativeCreated={EntityManager.HasComponent<Created>(e)}");
                }
            } catch(Exception ex) {
                if(!m.RenderErrorLogged){m.RenderErrorLogged=true;Mod.Log.Warn($"Ground render reconciliation failed for {m.Entity}: {ex}");}
            }
        }
    }
    void Retire(Entity e){if(EntityManager.Exists(e)&&!EntityManager.HasComponent<Game.Common.Deleted>(e))EntityManager.AddComponent<Game.Common.Deleted>(e);}
    void RetireService(Entity e){
        if(e==Plane||!EntityManager.Exists(e))return;
        Retire(e);
        if(!retiringServices.ContainsKey(e))retiringServices.Add(e,new Retirement{Session=ServiceSession,NextCheck=UnityEngine.Time.realtimeSinceStartup+2f});
    }
    // Deleted is a request, not confirmation of destruction. Track the exact versioned entity
    // until the game removes it. Never retain old service motion or use the old entity in a new flight.
    void AuditRetirements(){
        if(retiringServices.Count==0)return;
        var removed=new List<Entity>();float now=UnityEngine.Time.realtimeSinceStartup;
        foreach(var pair in retiringServices){
            var e=pair.Key;var r=pair.Value;
            if(!EntityManager.Exists(e)){removed.Add(e);Mod.Log.Info($"Ground session {r.Session}: confirmed removal {e}");continue;}
            if(e==Plane){removed.Add(e);continue;}
            if(now<r.NextCheck)continue;r.NextCheck=now+5f;
            if(!EntityManager.HasComponent<Deleted>(e)&&r.Retries<2){r.Retries++;try{Retire(e);Mod.Log.Warn($"Ground session {r.Session}: retry retirement {e}");}catch(Exception ex){Mod.Log.Warn($"Ground session {r.Session}: retirement retry failed for {e}: {ex}");}}
            else if(!r.Warned){r.Warned=true;Mod.Log.Warn($"Ground session {r.Session}: native removal still pending for {e}; Deleted={EntityManager.HasComponent<Deleted>(e)}");}
        }
        foreach(var e in removed)retiringServices.Remove(e);
    }
    public void Dismiss(){
        pendingDispatches.Clear();pendingPositions.Clear();serviceErrors.Clear();activeServices.Clear();lastServiceFrame=-1;serviceMessage="";
        int retired=0;
        for(int i=spawned.Count-1;i>=0;i--){var e=spawned[i];if(e==Plane)continue;if(EntityManager.Exists(e)){RetireService(e);retired++;}spawned.RemoveAt(i);}
        if(retired>0)Mod.Log.Info($"Ground session {ServiceSession}: retired {retired} service entities");
    }
    public void End(){
        pendingDispatches.Clear();pendingPositions.Clear();serviceTerrain=null;lastServiceFrame=-1;
        if(PickingDeparture){PlacementStopped();Placement?.RequestDisable();}
        if(State!="Idle"||Plane!=Entity.Null){if(Flight!=null&&Flight.IsOwnedFlight&&Flight.Controlled==Plane)Flight.Stop();if(Selection!=null&&Selection.Selected==Plane){Camera?.StopFollow();Selection.Release();}if(Camera?.Mode=="Cinematic")Camera.SetMode("Chase");}
        Dismiss();foreach(var e in spawned)if(EntityManager.Exists(e))Retire(e);spawned.Clear();
        Mod.Log.Info($"Ground session {ServiceSession}: ended owner={Plane}");Plane=Entity.Null;State="Idle";HasDeparture=false;Departure="";departurePosition=float3.zero;Status="Select a departure position";
    }
    // Also called in UIUpdate so external exit/deletion is cleaned up with a paused simulation.
    public void MaintainSession(){
        ResolveNativeServiceDefinitions();
        AuditRetirements();
        if(State!="Idle"&&(!EntityManager.Exists(Plane)||EntityManager.HasComponent<Deleted>(Plane)||(State=="Flying"&&(!Flight.IsOwnedFlight||Flight.Controlled!=Plane))))End();
        else if(State=="Flying"&&Flight.Speed>1f&&(activeServices.Count>0||pendingPositions.Count>0||pendingDispatches.Count>0))Dismiss();
    }
    protected override void OnUpdate(){MaintainSession();}
    protected override void OnDestroy(){End();base.OnDestroy();}
}}
