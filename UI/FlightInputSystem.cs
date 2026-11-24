using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Game;
using CS2FlightTakeover.Systems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Unity.Mathematics;

namespace CS2FlightTakeover.UI
{
    public sealed partial class FlightInputSystem : GameSystemBase
    {
        private readonly string[] actions = {"Pitch", "Roll", "Yaw", "Throttle"};
        private readonly string[] paths = new string[4];
        private readonly bool[] inverted = new bool[4];
        private readonly Dictionary<string,float> baseline = new Dictionary<string,float>();
        private int learning = -1;
        public float Deadzone = .05f, Expo = .25f;
        public string Status = "Keyboard";
        public string Pending => learning < 0 ? "" : actions[learning];
        private NativeJoystick.Result native = new NativeJoystick.Result();
        private Task<NativeJoystick.Result> scan;
        private float nextScan;
        public string NativeStatus => scan != null ? "Scanning Windows controllers…" : native.Message ?? "Waiting for scan";
        public void Detect() { if(scan==null) scan=Task.Run(()=>NativeJoystick.Scan()); }
        public string[] Devices() { var list=new List<string>(); foreach(var d in native.Devices) list.Add(d.Name); return list.ToArray(); }
        public string[] Raw() { var list=new List<string>(); foreach(var d in native.Devices){for(int i=0;i<6;i++)if(d.Present[i])list.Add(d.Key+" / "+"XYZRUV"[i]+": "+d.Values[i].ToString("0.000",CultureInfo.InvariantCulture));list.Add(d.Key+" buttons: 0x"+d.Buttons.ToString("X8")+" POV: "+(d.Pov==65535?"center":(d.Pov/100f).ToString(CultureInfo.InvariantCulture)));}return list.ToArray(); }
        private IEnumerable<KeyValuePair<string,float>> Axes(){foreach(var d in native.Devices)for(int i=0;i<6;i++)if(d.Present[i])yield return new KeyValuePair<string,float>(d.Key+"/"+"XYZRUV"[i],d.Values[i]);}
        public void ResetAssignments(){for(int i=0;i<4;i++){paths[i]=null;inverted[i]=false;}Cancel();Save();}
        public void SaveProfile()=>Save();
        public string[] Bindings() { var r = new string[4]; for(int i=0;i<4;i++) r[i]=actions[i]+":"+(string.IsNullOrEmpty(paths[i])?"Unbound":paths[i])+":"+(inverted[i]?"1":"0"); return r; }
        public void Learn(string action) { learning=Array.IndexOf(actions,action); baseline.Clear(); foreach(var a in Axes()) baseline[a.Key]=a.Value; }
        public void Cancel() { learning=-1; }
        public void Invert(string action) { int i=Array.IndexOf(actions,action); if(i>=0) { inverted[i]=!inverted[i]; Save(); } }
        public void Clear(string action) { int i=Array.IndexOf(actions,action); if(i>=0) { paths[i]=null; Save(); } }
        public void Tune(string value) { var p=value.Split('|'); if(p.Length!=2 || !float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float v) || !math.isfinite(v)) return; if(p[0]=="Deadzone") Deadzone=math.clamp(v,0,.25f); if(p[0]=="Expo") Expo=math.clamp(v,0,1); Save(); }
        private const string Prefix="CS2FlightTakeover.Input.";
        protected override void OnCreate() { base.OnCreate(); for(int i=0;i<4;i++){ paths[i]=UnityEngine.PlayerPrefs.GetString(Prefix+actions[i],""); inverted[i]=UnityEngine.PlayerPrefs.GetInt(Prefix+actions[i]+"Inv",0)!=0; } Deadzone=UnityEngine.PlayerPrefs.GetFloat(Prefix+"Deadzone",.05f); Expo=UnityEngine.PlayerPrefs.GetFloat(Prefix+"Expo",.25f); }
        private void Save(){ for(int i=0;i<4;i++){ UnityEngine.PlayerPrefs.SetString(Prefix+actions[i],paths[i]??""); UnityEngine.PlayerPrefs.SetInt(Prefix+actions[i]+"Inv",inverted[i]?1:0); } UnityEngine.PlayerPrefs.SetFloat(Prefix+"Deadzone",Deadzone); UnityEngine.PlayerPrefs.SetFloat(Prefix+"Expo",Expo); UnityEngine.PlayerPrefs.Save(); }
        private float Shape(float v){ float a=math.saturate((math.abs(v)-Deadzone)/(1-Deadzone)); return math.sign(v)*math.lerp(a,a*a*a,Expo); }
        private readonly float[] values=new float[4];
        public string Live => string.Join("|",Array.ConvertAll(values,v=>v.ToString("0.00",CultureInfo.InvariantCulture)));
        protected override void OnUpdate()
        {
            if(scan!=null && scan.IsCompleted){native=scan.Result;scan=null;}
            float now=UnityEngine.Time.realtimeSinceStartup;
            if(now>=nextScan){nextScan=now+3f;Detect();}
            for(int i=native.Devices.Count-1;i>=0;i--)if(!NativeJoystick.Poll(native.Devices[i])){native.Devices.RemoveAt(i);native.Message="Controller disconnected; scanning again";Detect();}
            var t=World.GetExistingSystemManaged<AircraftTakeoverSystem>();
            var k=Keyboard.current;
            float p=0,r=0,y=0,th=0;
            if(k!=null){p=(k.numpad2Key.isPressed?1:0)-(k.numpad8Key.isPressed?1:0);r=(k.numpad6Key.isPressed?1:0)-(k.numpad4Key.isPressed?1:0);y=(k.numpad9Key.isPressed?1:0)-(k.numpad7Key.isPressed?1:0);th=(k.numpadPlusKey.isPressed?1:0)-(k.numpadMinusKey.isPressed?1:0);}
            values[0]=p;values[1]=r;values[2]=y;values[3]=0; bool absolute=false, hardware=false;
            foreach(var a in Axes()) { string key=a.Key; float v=a.Value; if(!math.isfinite(v)) continue;
                if(learning>=0 && baseline.TryGetValue(key,out float old) && math.abs(v-old)>.35f){paths[learning]=key; learning=-1; Save();}
                for(int i=0;i<4;i++) if(paths[i]==key){hardware=true; float n=inverted[i]?-v:v; values[i]=i==3?math.saturate((n+1)*.5f):Shape(n); if(i==3) absolute=true;}
            }
            Status=hardware?"Hardware active":"Keyboard / no mapped device";
            if(t==null||!t.Active)return;
            t.PitchInput=values[0];t.RollInput=values[1];t.YawInput=values[2];t.ThrottleInput=absolute?0:th;
            if(absolute && learning<0)t.SetThrottle(values[3]);
            if(learning>=0 || (k!=null&&k.numpad5Key.isPressed)) t.PitchInput=t.RollInput=t.YawInput=t.ThrottleInput=0;
        }
    }
}
