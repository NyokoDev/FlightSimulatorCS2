using System;
using System.Collections.Generic;
using System.Globalization;
using Game;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
namespace CS2FlightTakeover.Systems {
public sealed partial class NavigationSystem : GameSystemBase {
 EntityQuery query; PrefabSystem prefabs; float nextScan;
 readonly List<string> markers=new List<string>();
 public string[] Markers => markers.ToArray();
 protected override void OnCreate(){base.OnCreate();query=GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Transform>());prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();}
 protected override void OnUpdate(){var flight=World.GetExistingSystemManaged<AircraftTakeoverSystem>();if(flight==null||!flight.Active)return;float now=UnityEngine.Time.realtimeSinceStartup;if(now<nextScan)return;nextScan=now+3;markers.Clear();
 using(var refs=query.ToComponentDataArray<PrefabRef>(Allocator.Temp))using(var transforms=query.ToComponentDataArray<Transform>(Allocator.Temp)){
 for(int i=0;i<refs.Length;i++){if(!prefabs.TryGetPrefab(refs[i].m_Prefab,out PrefabBase p)||p==null)continue;string name=p.name??"";string n=name.ToLowerInvariant();bool outside=n.Contains("airplaneoutsideconnection")||n.Contains("airplane outside connection");if(!outside&&!n.Contains("airport")&&!n.Contains("airfield"))continue;if(n.Contains("prop")||n.Contains("sign")||n.Contains("parking")||n.Contains("runway")||n.Contains("fence"))continue;var pos=transforms[i].m_Position;if(math.distance(pos.xz,flight.Position.xz)>25000)continue;markers.Add(string.Join("|",outside?"Outside connection":name.Replace("|"," "),pos.x.ToString(CultureInfo.InvariantCulture),pos.z.ToString(CultureInfo.InvariantCulture),outside?"exit":"airport"));if(markers.Count>=256)break;}
 }}
}
}
