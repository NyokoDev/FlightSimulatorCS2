using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.StartFlight {
public sealed partial class StartFlightSystem {
    readonly GroundServicePresets groundPresets=new GroundServicePresets();
    float3[] presetTargets=GroundServicePresets.Defaults();
    string presetAircraft="",presetKey="default",presetNotice="";
    bool presetAuto=true,presetDirty;
    int presetRevision;
    static string PF(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    void BeginGroundPreset(Entity prefab){
        groundPresets.Load();
        presetAircraft=prefabs.TryGetPrefab(prefab,out PrefabBase p)&&p!=null?Clean(p.name):"Unknown aircraft";
        presetKey=groundPresets.Automatic(presetAircraft);presetAuto=true;presetDirty=false;presetTargets=groundPresets.Targets(presetKey);presetRevision++;
        presetNotice=groundPresets.Error!=""?groundPresets.Error:groundPresets.IsSaved(presetKey)?"Saved preset loaded":"No saved preset for this selection; adjust and save targets.";
        Mod.Log.Info($"Ground preset session {ServiceSession}: aircraft={presetAircraft}, detected={GroundServicePresets.Detect(presetAircraft).Variant}, selected={presetKey}, saved={groundPresets.IsSaved(presetKey)}");
    }
    void AppendPresetRows(List<string> rows){
        if(State=="Idle")return;
        rows.Add(string.Join("|","G",presetRevision.ToString(CultureInfo.InvariantCulture),Clean(presetKey),presetAuto?"1":"0",Clean(presetAircraft),Clean(groundPresets.Label(presetKey,presetAircraft)),presetDirty?"1":"0",Clean(presetNotice),groundPresets.IsSaved(presetKey)?"1":"0"));
        for(int i=0;i<4;i++){var t=presetTargets[i];rows.Add(string.Join("|","T",i.ToString(CultureInfo.InvariantCulture),PF(t.x),PF(t.y),PF(t.z)));}
        foreach(var key in groundPresets.Keys(presetAircraft))rows.Add(string.Join("|","R",Clean(key),Clean(groundPresets.Label(key,presetAircraft)),groundPresets.IsSaved(key)?"1":"0"));
    }
    bool PresetHeader(string[] p)=>State=="Flying"&&p.Length>=2&&int.TryParse(p[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out int session)&&session==ServiceSession&&int.TryParse(p[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int rev)&&rev==presetRevision;
    public void EditGroundPreset(string value){
        var p=(value??"").Split('|');if(p.Length!=6||!PresetHeader(p)||!int.TryParse(p[2],out int slot)||slot<0||slot>=4||!Number(p[3],out float x)||!Number(p[4],out float z)||!Number(p[5],out float h))return;
        var t=new float3(x,z,h);if(!GroundServicePresets.Valid(t))return;
        if(math.all(presetTargets[slot]==t))return;
        presetTargets[slot]=t;presetDirty=true;presetNotice="Unsaved target changes";
    }
    public void SelectGroundPreset(string value){
        var p=(value??"").Split('|');if(p.Length!=3||!PresetHeader(p)||!CanService)return;
        bool auto=p[2]=="auto";string key=auto?groundPresets.Automatic(presetAircraft):p[2];if(!groundPresets.Known(key,presetAircraft))return;
        presetKey=key;presetAuto=auto;presetTargets=groundPresets.Targets(key);presetDirty=false;presetRevision++;
        presetNotice=groundPresets.IsSaved(key)?"Saved preset loaded":"No saved preset; adjust and save targets.";
        // Reposition only already requested/previewed services. Do not spawn unrequested services.
        pendingPositions.Clear();pendingDispatches.Clear();
        foreach(var pair in activeServices){
            var m=pair.Value;if(m.Failed||(!m.PendingCreation&&!LiveService(m))||!int.TryParse(pair.Key,out int i)||i<0||i>=4||!prefabs.TryGetPrefab(m.Prefab,out PrefabBase prefab)||prefab==null)continue;
            var t=presetTargets[i];QueueServicePosition(string.Join("|",ServiceSession.ToString(CultureInfo.InvariantCulture),Key(m.Prefab),pair.Key,PF(t.x),PF(t.y),PF(t.z),Clean(prefab.name)));
        }
    }
    public void SaveGroundPreset(string value){
        var p=(value??"").Split('|');if(p.Length!=15||!PresetHeader(p))return;
        string action=p[2],key=presetKey,label=groundPresets.Label(key,presetAircraft);
        if(action.StartsWith("new:",StringComparison.Ordinal)){
            label=Clean(action.Substring(4)).Trim();if(label.Length==0||label.Length>80){presetNotice="Enter a preset name (1–80 characters).";return;}
            key="custom:"+Guid.NewGuid().ToString("N");
        }else if(action=="exact"){key=GroundServicePresets.ExactKey(presetAircraft);label="This aircraft: "+presetAircraft;}
        else if(action!="save")return;
        var targets=new float3[4];for(int i=0;i<4;i++){int j=3+i*3;if(!Number(p[j],out float x)||!Number(p[j+1],out float z)||!Number(p[j+2],out float h)||!GroundServicePresets.Valid(new float3(x,z,h))){presetNotice="Invalid target values";return;}targets[i]=new float3(x,z,h);}
        if(!groundPresets.Save(key,label,targets)){presetNotice=groundPresets.Error!=""?groundPresets.Error:"Preset could not be saved";return;}
        if(key!=presetKey)presetAuto=false;
        presetKey=key;presetTargets=targets;presetDirty=false;presetRevision++;presetNotice="Saved to ModsData/NFS";
    }
}
}
