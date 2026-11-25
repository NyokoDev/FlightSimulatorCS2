using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Unity.Mathematics;

namespace CS2FlightTakeover.StartFlight {
// Persistent targets only. No ECS handles, world positions, or staging coordinates are serialized.
internal sealed class GroundServicePresets {
    internal static readonly string[] Slots={"Catering","ForwardStairs","RearStairs","BaggageLoader"};
    internal static float3[] Defaults()=>new[]{new float3(12,5,0),new float3(-5,12,90),new float3(-5,-12,90),new float3(5,-8,-90)};
    internal sealed class Entry { public string Key="", Label=""; public float3[] Targets=Defaults(); }
    internal static readonly (string Key,string Label)[] Builtins={
        ("default","Default"),("737","Boeing 737 family"),("737-800","Boeing 737-800"),("737-max8","Boeing 737 MAX 8"),
        ("787","Boeing 787 family"),("787-8","Boeing 787-8"),("787-9","Boeing 787-9"),("787-10","Boeing 787-10"),
        ("a350","Airbus A350 family"),("a350-900","Airbus A350-900"),("a350-1000","Airbus A350-1000")};
    readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
    internal string FilePath=>Path.Combine(UnityEngine.Application.persistentDataPath,"ModsData","NFS","GroundServicePresets.xml");
    internal string Error {get;private set;}="";
    bool readFailed;
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static bool Parse(string? s,out float f)=>float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out f)&&math.isfinite(f);
    internal static bool Valid(float3 v)=>math.all(math.isfinite(v))&&math.abs(v.x)<=100&&math.abs(v.y)<=100&&math.abs(v.z)<=180;
    internal static string ExactKey(string name)=>"aircraft:"+(name??"").Trim().ToUpperInvariant();
    internal static (string Variant,string Family) Detect(string name) {
        var n=(name??"").ToUpperInvariant();
        bool M(string p)=>Regex.IsMatch(n,p,RegexOptions.CultureInvariant);
        // Match full model designations before shorter ICAO/family aliases. Numeric boundaries avoid 7370/7870.
        if(M(@"(?<!\d)737[\s_-]*MAX[\s_-]*8(?!\d)|(?<!\d)737[\s_-]+8(?!\d)|(?<![A-Z0-9])B38M(?![A-Z0-9])|(?<![A-Z0-9])MAX[\s_-]*8(?!\d)"))return ("737-max8","737");
        if(M(@"(?<!\d)737[\s_-]*8\d{2}(?!\d)|(?<![A-Z0-9])B?738(?![A-Z0-9])"))return ("737-800","737");
        if(M(@"(?<!\d)787[\s_-]*10(?!\d)|(?<![A-Z0-9])B78X(?![A-Z0-9])"))return ("787-10","787");
        if(M(@"(?<!\d)787[\s_-]*9(?!\d)|(?<![A-Z0-9])B?789(?![A-Z0-9])"))return ("787-9","787");
        if(M(@"(?<!\d)787[\s_-]*8(?!\d)|(?<![A-Z0-9])B?788(?![A-Z0-9])"))return ("787-8","787");
        if(M(@"A[\s_-]*350[\s_-]*10\d{2}(?!\d)|(?<![A-Z0-9])A35K(?![A-Z0-9])"))return ("a350-1000","a350");
        if(M(@"A[\s_-]*350[\s_-]*9\d{2}(?!\d)|(?<![A-Z0-9])A359(?![A-Z0-9])"))return ("a350-900","a350");
        if(M(@"(?<!\d)737(?!\d)|(?<![A-Z0-9])B?73[3-9](?![A-Z0-9])|(?<![A-Z0-9])(?:B)?73(?![A-Z0-9])"))return ("737","737");
        if(M(@"(?<!\d)787(?!\d)"))return ("787","787");
        if(M(@"A[\s_-]*350(?!\d)"))return ("a350","a350");
        return ("default","default");
    }
    internal string Automatic(string name) {
        var exact=ExactKey(name);var d=Detect(name);
        if(entries.ContainsKey(exact))return exact;
        if(entries.ContainsKey(d.Variant))return d.Variant;
        if(entries.ContainsKey(d.Family))return d.Family;
        // Keep the detected slot selected so Save creates a model-specific preset. Generic targets are just a starting point.
        return d.Variant;
    }
    internal string Label(string key,string name){
        if(entries.TryGetValue(key,out var e))return e.Label;
        if(key==ExactKey(name))return "This aircraft: "+name;
        foreach(var b in Builtins)if(b.Key==key)return b.Label;
        return key;
    }
    internal bool IsSaved(string key)=>entries.ContainsKey(key);
    internal bool Known(string key,string name)=>entries.ContainsKey(key)||Builtins.Any(b=>b.Key==key)||key==ExactKey(name);
    internal float3[] Targets(string key){
        if(entries.TryGetValue(key,out var e))return (float3[])e.Targets.Clone();
        string family=key.StartsWith("737-",StringComparison.Ordinal)?"737":key.StartsWith("787-",StringComparison.Ordinal)?"787":key.StartsWith("a350-",StringComparison.Ordinal)?"a350":"default";
        if(entries.TryGetValue(family,out e))return (float3[])e.Targets.Clone();
        if(entries.TryGetValue("default",out e))return (float3[])e.Targets.Clone();
        return Defaults();
    }
    internal IEnumerable<string> Keys(string name){foreach(var b in Builtins)yield return b.Key;var exact=ExactKey(name);if(exact!="aircraft:")yield return exact;foreach(var e in entries.Values.OrderBy(e=>e.Label,StringComparer.OrdinalIgnoreCase))if(e.Key!=exact&&!Builtins.Any(b=>b.Key==e.Key))yield return e.Key;}
    internal void Load(){
        entries.Clear();Error="";readFailed=false;
        try{
            if(!File.Exists(FilePath))return;
            var root=XDocument.Load(FilePath).Root;
            if(root==null||root.Name!="GroundServicePresets"||(string?)root.Attribute("version")!="1")throw new InvalidDataException("Unsupported preset file format");
            foreach(var node in root.Elements("Preset")){
                string key=(string?)node.Attribute("id")??"",label=(string?)node.Attribute("name")??"";
                if(key.Length==0||label.Length==0||key.Contains("|")||label.Contains("|")||entries.ContainsKey(key))throw new InvalidDataException("Invalid preset identifier");
                var targets=Defaults();var seen=new HashSet<int>();
                foreach(var s in node.Elements("Service")){
                    int i=Array.IndexOf(Slots,(string?)s.Attribute("id"));
                    if(i<0||!seen.Add(i)||!Parse((string?)s.Attribute("x"),out float x)||!Parse((string?)s.Attribute("z"),out float z)||!Parse((string?)s.Attribute("heading"),out float h)||!Valid(new float3(x,z,h)))throw new InvalidDataException("Invalid service target in "+key);
                    targets[i]=new float3(x,z,h);
                }
                if(seen.Count!=Slots.Length)throw new InvalidDataException("Missing service targets in "+key);
                entries.Add(key,new Entry{Key=key,Label=label,Targets=targets});
            }
            Mod.Log.Info($"Ground presets: loaded {entries.Count} from {FilePath}");
        }catch(Exception ex){entries.Clear();readFailed=true;Error="Could not read presets; existing file preserved.";Mod.Log.Warn("Ground preset load: "+ex);}
    }
    internal bool Save(string key,string label,float3[] targets){
        if(readFailed)return false;
        if(targets.Length!=4||targets.Any(v=>!Valid(v)))return false;
        var next=new Dictionary<string,Entry>(entries,StringComparer.OrdinalIgnoreCase){[key]=new Entry{Key=key,Label=label,Targets=(float3[])targets.Clone()}};
        string temp=FilePath+".tmp";
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var root=new XElement("GroundServicePresets",new XAttribute("version",1));
            foreach(var e in next.Values.OrderBy(e=>e.Key,StringComparer.OrdinalIgnoreCase)){
                var node=new XElement("Preset",new XAttribute("id",e.Key),new XAttribute("name",e.Label));
                for(int i=0;i<4;i++){var t=e.Targets[i];node.Add(new XElement("Service",new XAttribute("id",Slots[i]),new XAttribute("x",F(t.x)),new XAttribute("z",F(t.y)),new XAttribute("heading",F(t.z))));}root.Add(node);
            }
            new XDocument(root).Save(temp);
            if(File.Exists(FilePath))File.Replace(temp,FilePath,FilePath+".bak");else File.Move(temp,FilePath);
            entries.Clear();foreach(var e in next)entries.Add(e.Key,e.Value);Error="";Mod.Log.Info($"Ground preset saved: {key} ({FilePath})");return true;
        }catch(Exception ex){Error="Could not save preset; check the log and folder permissions.";Mod.Log.Warn("Ground preset save: "+ex);try{if(File.Exists(temp))File.Delete(temp);}catch{}return false;}
    }
}
}
