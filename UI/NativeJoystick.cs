using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace CS2FlightTakeover.UI {
 internal sealed class NativeJoystick {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Caps {
   public ushort mid,pid; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string name;
   public uint xmin,xmax,ymin,ymax,zmin,zmax,buttons,periodMin,periodMax,rmin,rmax,umin,umax,vmin,vmax,flags,maxAxes,axes,maxButtons;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string registry;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string oem;
  }
  [StructLayout(LayoutKind.Sequential)] struct State { public uint size,flags,x,y,z,r,u,v,buttons,button,pov,reserved1,reserved2; }
  [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
  [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern uint joyGetDevCapsW(UIntPtr id,out Caps caps,uint size);
  [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id,ref State state);
  internal sealed class Device { public uint Slot; public string Key,Name; public float[] Values=new float[6]; public bool[] Present=new bool[6]; public uint Buttons,Pov; internal uint[] Min,Max; }
  internal sealed class Result { public List<Device> Devices=new List<Device>(); public string Message; }
  internal static Result Scan(){var result=new Result(); try {uint count=joyGetNumDevs(); for(uint i=0;i<Math.Min(count,64u);i++){Caps c; if(joyGetDevCapsW(new UIntPtr(i),out c,(uint)Marshal.SizeOf(typeof(Caps)))!=0)continue; var d=new Device{Slot=i,Key="WinMM-"+c.mid.ToString("X4")+"-"+c.pid.ToString("X4")+"-"+i,Name=c.name+" [WinMM MID="+c.mid.ToString("X4")+" PID="+c.pid.ToString("X4")+", slot "+i+", "+c.axes+" axes, "+c.buttons+" buttons]",Min=new[]{c.xmin,c.ymin,c.zmin,c.rmin,c.umin,c.vmin},Max=new[]{c.xmax,c.ymax,c.zmax,c.rmax,c.umax,c.vmax}}; d.Present=new[]{true,true,(c.flags&1)!=0,(c.flags&2)!=0,(c.flags&4)!=0,(c.flags&8)!=0}; if(Poll(d))result.Devices.Add(d); } result.Message=result.Devices.Count==0?"Windows WinMM: no responding joystick. Check joy.cpl and PC mode.":"Windows WinMM: "+result.Devices.Count+" responding device(s)"; }catch(Exception e){result.Message="WinMM initialization failed: "+e.Message;}return result; }
  internal static bool Poll(Device d){var s=new State{size=(uint)Marshal.SizeOf(typeof(State)),flags=0xFF};if(joyGetPosEx(d.Slot,ref s)!=0)return false; uint[] raw={s.x,s.y,s.z,s.r,s.u,s.v};for(int i=0;i<6;i++)d.Values[i]=d.Max[i]>d.Min[i]?Math.Max(-1f,Math.Min(1f,2f*((float)raw[i]-d.Min[i])/(d.Max[i]-d.Min[i])-1f)):0;d.Buttons=s.buttons;d.Pov=s.pov;return true;}
 }
}
