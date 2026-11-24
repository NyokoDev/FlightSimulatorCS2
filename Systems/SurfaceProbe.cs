using System;
using System.Reflection;
using Unity.Mathematics;
using Unity.Entities;
namespace CS2FlightTakeover.Systems {
// Runtime signature discovery avoids binding to an unverified game API overload.
internal sealed class SurfaceProbe {
 readonly object system; readonly MethodInfo get, sample; readonly bool water;
 public SurfaceProbe(World world,bool water) {
 this.water=water;
 var asm=typeof(Game.GameSystemBase).Assembly;
 var type=asm.GetType("Game.Simulation."+(water?"WaterSystem":"TerrainSystem"));
 var utils=asm.GetType("Game.Simulation."+(water?"WaterUtils":"TerrainUtils"));
 if(type==null||utils==null)return;
 foreach(var m in typeof(World).GetMethods()) if(m.Name=="GetExistingSystemManaged"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){system=m.MakeGenericMethod(type).Invoke(world,null);break;}
 if(system==null)return;
 foreach(var g in type.GetMethods()) {
 if(g.Name!=(water?"GetSurfaceData":"GetHeightData")||g.ReturnType==typeof(void)||g.ContainsGenericParameters)continue;
 var ps=g.GetParameters(); if(Array.Exists(ps,p=>!p.IsOptional && p.ParameterType!=typeof(bool) && !(p.IsOut&&p.ParameterType.GetElementType().FullName=="Unity.Jobs.JobHandle")))continue;
 foreach(var s in utils.GetMethods(BindingFlags.Public|BindingFlags.Static)) {
 if(s.Name!="SampleHeight"||s.ReturnType!=typeof(float)||s.ContainsGenericParameters)continue;
 var a=s.GetParameters(); if(a.Length!=2)continue;
 var t=a[0].ParameterType; if(t.IsByRef)t=t.GetElementType();
 if(t!=g.ReturnType || (a[1].ParameterType!=typeof(float3)&&a[1].ParameterType!=typeof(float2)))continue;
 get=g;sample=s;return;
 }
 }
 }
 public bool Read(float3 pos,out float height) {
 height=0;if(get==null)return false;
 try {var p=get.GetParameters();var args=new object[p.Length];for(int i=0;i<p.Length;i++)args[i]=p[i].IsOut?Activator.CreateInstance(p[i].ParameterType.GetElementType()):p[i].ParameterType==typeof(bool)?(object)false:p[i].DefaultValue;
 var data=get.Invoke(system,args);for(int i=0;i<p.Length;i++)if(p[i].IsOut&&args[i]!=null)args[i].GetType().GetMethod("Complete",Type.EmptyTypes)?.Invoke(args[i],null);
 height=(float)sample.Invoke(null,new object[]{data,sample.GetParameters()[1].ParameterType==typeof(float2)?(object)pos.xz:pos});return math.isfinite(height);
 }catch{return false;}
 }
}
}
