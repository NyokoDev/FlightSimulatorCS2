import React, {useMemo, useState} from "react";
import {createPortal} from "react-dom";
import {bindValue, useValue, trigger} from "cs2/api";
import type {ModRegistrar} from "cs2/modding";
import "./flight-panel.scss";
import { Button } from "cs2/ui";
import FlightTakeoverSVG from "./images/FlightTakeover.svg";
// Cohtml bindings may emit undefined while the backend is registering or unloading.
const EMPTY_ROWS: string[] = [];
function useSafeArray(binding: Parameters<typeof useValue<string[]>>[0]): string[] {
 const value = useValue(binding);
 return useMemo(() => Array.isArray(value) ? value.filter(v => typeof v === "string") : EMPTY_ROWS, [value]);
}
function useSafeString(binding: Parameters<typeof useValue<string>>[0], fallback: string): string {
 const value = useValue(binding);
 return typeof value === "string" ? value : fallback;
}
function useSafeBoolean(binding: Parameters<typeof useValue<boolean>>[0], fallback: boolean): boolean {
 const value = useValue(binding);
 return typeof value === "boolean" ? value : fallback;
}
const id = "CS2FlightTakeover";
const aircraft$ = bindValue<string[]>(id,"Aircraft",[]);
const selected$ = bindValue<string>(id,"Selected","");
const telemetry$ = bindValue<string>(id,"Telemetry","0|0|0|0|0");
const flying$ = bindValue<boolean>(id,"Flying",false);
const following$ = bindValue<boolean>(id,"Following",false);
const mode$ = bindValue<string>(id,"CameraMode","Chase");
const distance$ = bindValue<string>(id,"CameraDistance","65");
const devices$=bindValue<string[]>(id,"Devices",[]);
const bindings$=bindValue<string[]>(id,"Bindings",[]);
const live$=bindValue<string>(id,"InputLive","0|0|0|0");
const status$=bindValue<string>(id,"InputStatus","");
const learning$=bindValue<string>(id,"Learning","");
const tuning$=bindValue<string>(id,"Tuning","0.05|0.25");
const angles$=bindValue<string>(id,"CameraAngles","0|18");
const send=(name:string,value?:string)=>value===undefined?trigger(id,name):trigger(id,name,value);
function Range({label,value,min,max,step=1,change,unit="",live=false}:{label:string,value:number,min:number,max:number,step?:number,change:(n:number)=>void,unit?:string,live?:boolean}) {
 const [drag,setDrag]=useState<number|null>(null);
 const cleanup=React.useRef<(()=>void)|null>(null);
 React.useEffect(()=>()=>{if(cleanup.current)cleanup.current()},[]);
 const current=Math.max(min,Math.min(max,drag===null?(Number.isFinite(value)?value:min):drag));
 const percent=100*(current-min)/(max-min);
 const snap=(v:number)=>Math.max(min,Math.min(max,Number((min+Math.round((v-min)/step)*step).toFixed(6))));
 return <div className="ft-range"><div className="ft-range-label"><span>{label}</span><b>{Number(current.toFixed(2))}{unit}</b></div>
 <div className="ft-slider" role="slider" tabIndex={0} aria-label={label} aria-valuemin={min} aria-valuemax={max} aria-valuenow={current}
 onKeyDown={e=>{let v=current;if(e.key==="ArrowLeft"||e.key==="ArrowDown")v-=step;else if(e.key==="ArrowRight"||e.key==="ArrowUp")v+=step;else if(e.key==="Home")v=min;else if(e.key==="End")v=max;else return;e.preventDefault();e.stopPropagation();change(snap(v));}}
 onMouseDown={e=>{if(e.button!==0)return;e.preventDefault();e.stopPropagation();if(cleanup.current)cleanup.current();const rect=e.currentTarget.getBoundingClientRect();const at=(x:number)=>snap(min+(max-min)*(x-rect.left)/Math.max(1,rect.width));let latest=at(e.clientX);setDrag(latest);if(live)change(latest);const move=(ev:MouseEvent)=>{ev.preventDefault();ev.stopPropagation();const next=at(ev.clientX);if(next===latest)return;latest=next;setDrag(latest);if(live)change(latest)};const finish=()=>{document.removeEventListener("mousemove",move,true);document.removeEventListener("mouseup",up,true);window.removeEventListener("blur",finish);cleanup.current=null;setDrag(null);change(latest)};const up=(ev:MouseEvent)=>{ev.preventDefault();ev.stopPropagation();latest=at(ev.clientX);finish()};cleanup.current=()=>{document.removeEventListener("mousemove",move,true);document.removeEventListener("mouseup",up,true);window.removeEventListener("blur",finish);cleanup.current=null};document.addEventListener("mousemove",move,true);document.addEventListener("mouseup",up,true);window.addEventListener("blur",finish);}}>
 <div className="ft-slider-track"/><div className="ft-slider-fill" style={{width:percent+"%"}}/><div className="ft-slider-thumb" style={{left:percent+"%"}}/>
 </div><div className="ft-slider-ends"><small>{min}{unit}</small><small>{max}{unit}</small></div></div>
}
const nativeStatus$=bindValue<string>(id,"NativeStatus","Waiting for backend");
const rawInputs$=bindValue<string[]>(id,"RawInputs",[]);
function Controls(){
 const nativeStatus=useSafeString(nativeStatus$,"Waiting for backend"), rawInputs=useSafeArray(rawInputs$);
 const devices=useSafeArray(devices$), bindings=useSafeArray(bindings$), live=useSafeString(live$,"0|0|0|0").split("|").map(Number), status=useSafeString(status$,""), learning=useSafeString(learning$,""), tuning=useSafeString(tuning$,"0.05|0.25").split("|").map(Number);
 return <><div className="ft-section">CONNECTED DEVICES</div><div className="ft-status">{nativeStatus}</div><div className="ft-row"><button onClick={()=>send("DetectControllers")}>Detect Controllers</button><button onClick={()=>send("DetectControllers")}>Refresh / reconnect</button></div><div className="ft-status">{status}</div>{devices.length?devices.map((d,i)=><div className="ft-device" key={i}>{d}</div>):<p>No responding Windows controller. Connect a joystick, yoke, pedals or gamepad, then click Detect Controllers. HOTAS X: use PC mode.</p>}<div className="ft-section">AXIS ASSIGNMENTS</div><p>Click Assign and move one physical axis. Check direction using the live indicators.</p>{["Pitch","Roll","Yaw","Throttle"].map((a,i)=><div className="ft-axis" key={a}><div className="ft-line"><b>{a}</b><span>{(live[i]||0).toFixed(2)}</span></div><div className="ft-meter"><i style={{width:`${Math.min(100,Math.abs(live[i]||0)*100)}%`}}/></div><small>{bindings[i]&& !bindings[i].includes("Unbound")?"Assigned":"Not assigned"}</small><div className="ft-row"><button className={learning===a?"ft-selected":""} onClick={()=>send("Learn",a)}>{learning===a?"Move axis…":"Assign"}</button><button onClick={()=>send("Invert",a)}>Invert</button><button onClick={()=>send("ClearAxis",a)}>Clear</button></div></div>)}{learning&&<button onClick={()=>send("CancelLearn")}>Cancel assignment</button>}<Range label="Dead zone" value={Math.round((tuning[0]||0)*100)} min={0} max={25} unit="%" change={v=>send("Tune","Deadzone|"+v/100)}/><Range label="Response curve" value={Math.round((tuning[1]||0)*100)} min={0} max={100} unit="%" change={v=>send("Tune","Expo|"+v/100)}/><div className="ft-row"><button onClick={()=>send("ResetAssignments")}>Reset assignments</button><button onClick={()=>send("SaveProfile")}>Save profile</button></div></>;
}
const boundary$=bindValue<string>(id,"Boundary","Assisted|7000|1000|0|0");
const navMarkers$=bindValue<string[]>(id,"NavigationMarkers",[]);
const navPosition$=bindValue<string>(id,"NavigationPosition","0|0|0");
const surface$=bindValue<string>(id,"SurfaceStatus","Unavailable");
const collision$=bindValue<boolean>(id,"CollisionEnabled",true);
const impactAlert$=bindValue<string>(id,"ImpactAlert","");
function ImpactAlert(){const flying=useSafeBoolean(flying$,false),message=useSafeString(impactAlert$,"");return flying&&message?<div className="ft-impact-alert" role="alert"><b>⚠ Surface impact</b><span>{message}</span></div>:null;}
function Radar(){const flying=useSafeBoolean(flying$,false), markers=useSafeArray(navMarkers$), [x,z,h]=useSafeString(navPosition$,"0|0|0").split("|").map(Number), surface=useSafeString(surface$,"Unavailable");const [range,setRange]=useState(4000);const [north,setNorth]=useState(false);const [mode,extent]=useSafeString(boundary$,"Assisted|7000|1000|0|0").split("|");if(!flying)return null;const angle=(north?0:h)*Math.PI/180;const project=(a:number,b:number)=>({x:120+((a-x)*Math.cos(angle)-(b-z)*Math.sin(angle))*110/range,y:120-((a-x)*Math.sin(angle)+(b-z)*Math.cos(angle))*110/range});const rows=markers.map(m=>{const [name,a,b,type]=m.split("|");return {name,type,d:Math.hypot(Number(a)-x,Number(b)-z),...project(Number(a),Number(b))}}).sort((a,b)=>a.d-b.d);const nearest=rows.find(r=>r.type==="airport");const corners=[[-1,-1],[-1,1],[1,1],[1,-1]].map(([a,b])=>project(a*Number(extent),b*Number(extent)));return <div className="ft-radar"><div className="ft-line"><b>Navigation</b><span>{Math.round((h+360)%360)}°</span></div><svg viewBox="0 0 240 240" className="ft-radar-map"><rect width="240" height="240" fill="#202b2d"/><polygon points={corners.map(p=>`${p.x},${p.y}`).join(" ")} fill="#34413c" stroke="#c4a96b" strokeWidth="1"/>{[36,72,108].map(r=><circle key={r} cx="120" cy="120" r={r} fill="none" stroke="#ffffff" strokeOpacity=".18"/>)}<path d="M120 0V240M0 120H240" stroke="#ffffff" strokeOpacity=".12"/>{rows.filter(r=>r.x>8&&r.x<232&&r.y>8&&r.y<232).map((r,i)=><g key={i} transform={`translate(${r.x} ${r.y})`}>{r.type==="airport"?<><rect x="-12" y="-10" width="24" height="20" rx="5" fill="#17232d" stroke="#d7ebff" strokeWidth=".8"/><path d="M-9 -6H-2L-3 -2H-5L-4 6H-8L-7 -2H-8Z M-10 7H-2" fill="#d7ebff" stroke="#d7ebff" strokeWidth=".7"/><path d="M5 -7L6 -2L11 1V3L6 1V5L8 7H2L4 5V1L-1 3V1L4 -2Z" fill="#d7ebff"/></>:<><circle r="6" fill="#e8b76e"/><path d="M-4 0H4M0 -4V4" stroke="#202b2d" strokeWidth="1.5"/></>}</g>)}<g transform={`translate(120 120) rotate(${north?h:0})`}><path d="M0 -13L9 9L0 5L-9 9Z" fill="white" stroke="#111"/></g><text x="120" y="15" fill="white" textAnchor="middle" fontSize="10">{north?"N":"HDG"}</text></svg><div className="ft-line"><small>{nearest?nearest.name+" · "+(nearest.d/1000).toFixed(1)+" km":"No nearby airport found"}</small></div><div className="ft-row"><button onClick={()=>setRange(r=>Math.max(500,r/2))}>+</button><button onClick={()=>setRange(r=>Math.min(16000,r*2))}>−</button><button onClick={()=>setNorth(n=>!n)}>{north?"North up":"Heading up"}</button></div><small>{range/1000} km radius · schematic map</small><small>{surface}</small></div>}
function CollisionSettings(){const enabled=useSafeBoolean(collision$,true),status=useSafeString(surface$,"Unavailable");return <><div className="ft-section">Surface collision</div><button onClick={()=>send("ToggleCollision")}>{enabled?"Collision protection on":"Collision protection off"}</button><p>{status}</p></>}
function BoundaryPanel(){const [mode,extent,warning]=useSafeString(boundary$,"Assisted|7000|1000|0|0").split("|");const flying=useSafeBoolean(flying$,false);return <><div className="ft-section">MAP BOUNDARY PROTECTION</div><p>Estimated square boundary centered at map origin. Adjust to match your map.</p><div className="ft-row">{["Assisted","Warning","Wrap","Unrestricted"].map(m=><button key={m} className={mode===m?"ft-selected":""} onClick={()=>send("ConfigureBoundary","Mode|"+m)}>{m}</button>)}</div><Range label="Map half extent" value={Number(extent)} min={500} max={50000} step={100} unit=" m" change={v=>send("ConfigureBoundary","Extent|"+v)}/><Range label="Warning distance" value={Number(warning)} min={100} max={5000} step={100} unit=" m" change={v=>send("ConfigureBoundary","Warning|"+v)}/><button disabled={!flying} onClick={()=>send("ReturnToMap")}>Return to map</button><p>Assisted mode turns inward and recovers position if the boundary is crossed.</p></>}
function BoundaryReadout(){const [mode,,warning,distance,vs]=useSafeString(boundary$,"Assisted|7000|1000|0|0").split("|");return <><div className="ft-line"><span>Vertical speed</span><b>{Math.round(Number(vs)*196.85)} ft/min</b></div><div className="ft-line"><span>Estimated boundary</span><b>{distance} m</b></div>{mode!=="Unrestricted"&&Number(distance)<Number(warning)&&<div className="ft-status">MAP BOUNDARY WARNING</div>}<button onClick={()=>send("ReturnToMap")}>Return to map</button></>}
function Instruments(){const t=useSafeString(telemetry$,"0|0|0|0|0").split("|").map(Number), flying=useSafeBoolean(flying$,false); const pitch=t[2]||0, bank=t[3]||0;return <>{!flying?<p>Take control of an aircraft to display manual flight telemetry.</p>:<><div className="ft-instruments"><div><small>GROUND SPEED</small><strong>{Math.round((t[0]||0)*1.94384)}</strong><small>KT</small></div><div className="ft-horizon"><div className="ft-world" style={{transform:`rotate(${-bank}deg) translateY(${pitch*1.5}rem)`}}><div className="ft-sky"/><div className="ft-earth"/></div><div className="ft-cross">— ◇ —</div></div><div><small>MAP ALT</small><strong>{Math.round((t[5]||0)*3.28084)}</strong><small>FT</small></div></div><div className="ft-readouts"><span>HDG* <b>{Math.round(((t[4]||0)%360+360)%360)}°</b></span><span>PITCH <b>{pitch.toFixed(1)}°</b></span><span>BANK <b>{bank.toFixed(1)}°</b></span></div><div className="ft-line"><span>Throttle</span><b>{Math.round((t[1]||0)*100)}%</b></div><div className="ft-meter"><i style={{width:`${Math.max(0,Math.min(100,(t[1]||0)*100))}%`}}/></div><BoundaryReadout/><small>* Model heading; attitude is based on existing flight model.</small></>}</>}
function CameraPanel(){const selected=useSafeString(selected$,""), mode=useSafeString(mode$,"Chase"), following=useSafeBoolean(following$,false), distance=Number(useSafeString(distance$,"65"))||65, angles=useSafeString(angles$,"0|18").split("|").map(Number);return <><div className="ft-section">VIEW MODE</div><div className="ft-row">{["Chase","Orbit","Pilot"].map(m=><button key={m} disabled={!selected} className={following&&mode===m?"ft-selected":""} onClick={()=>send("SetCameraMode",m)}>{m}</button>)}</div>{!selected?<p>Select an aircraft to activate the camera.</p>:<><Range label="Distance" value={distance} min={10} max={2000} step={5} unit=" m" change={v=>send("SetCameraDistance",String(v))}/><Range label="Azimuth" value={angles[0]||0} min={-180} max={180} unit="°" change={v=>send("CameraAngle","Azimuth|"+v)}/><Range label="Elevation" value={angles[1]||0} min={-10} max={80} unit="°" change={v=>send("CameraAngle","Elevation|"+v)}/><div className="ft-row"><button onClick={()=>{send("SetCameraDistance","65");send("CameraAngle","Azimuth|0");send("CameraAngle","Elevation|18")}}>Recenter</button><button onClick={()=>send("Follow")}>{following?"Restore city camera":"Follow aircraft"}</button></div></>}</>}
function Aircraft() {
    const raw = useSafeArray(aircraft$);
    const selected = useSafeString(selected$,"");
    const flying = useSafeBoolean(flying$,false);

    const [query, setQuery] = useState("");

    const rows = useMemo(() => {
        return raw
            .map(s => {
                const [i, v, name, speed, ...thumbnailParts] =
                    s.split("|");

                return {
                    id: `${i}:${v}`,
                    name: name || "Aircraft",
                    speed: Number(speed) || 0,
                    thumbnail: thumbnailParts.join("|")
                };
            })
            .filter(r =>
                r.name.toLowerCase().includes(query.toLowerCase())
            );
    }, [raw, query]);

    return (
        <div className="ft-aircraft-menu">
            <div className="ft-aircraft-heading">
                <h2>Select aircraft</h2>
                <span>{raw.length} aircraft detected</span>
            </div>

            <input
                className="ft-search"
                placeholder="Search aircraft..."
                value={query}
                onChange={e => setQuery(e.target.value)}
            />

            <div className="ft-aircraft-grid">
                {rows.map(r => {
                    const active = selected === r.id;
                    const controlled = active && flying;
                    const locked = flying && !active;

                    return (
                        <div
                            key={r.id}
                            className={[
                                "ft-aircraft-card",
                                active ? "is-selected" : "",
                                controlled ? "is-controlled" : "",
                                locked ? "is-locked" : ""
                            ].join(" ")}
                        >
                            <div
                                className="ft-aircraft-preview"
                                onClick={() => {
                                    if (!flying)
                                        send("Select", r.id);
                                }}
                            >
                                <span
                                    className="ft-aircraft-placeholder"
                                    aria-hidden="true"
                                >
                                    ✈
                                </span>

                                {r.thumbnail && (
                                    <img
                                        key={r.thumbnail}
                                        src={r.thumbnail}
                                        alt={r.name}
                                        onError={e => {
                                            e.currentTarget.style.display =
                                                "none";
                                        }}
                                    />
                                )}

                                <span className="ft-aircraft-speed">
                                    {Math.round(r.speed * 1.94384)} KT
                                </span>

                                {controlled && (
                                    <span className="ft-aircraft-live">
                                        MANUAL CONTROL
                                    </span>
                                )}
                            </div>

                            <div className="ft-aircraft-details">
                                <strong>{r.name}</strong>

                                <small>
                                    {controlled
                                        ? "Manual flight active"
                                        : active
                                            ? "Selected aircraft"
                                            : "Available"}
                                </small>
                            </div>

                            <div className="ft-aircraft-actions">
                                <button
                                    className={
                                        controlled
                                            ? "ft-aircraft-release"
                                            : "ft-aircraft-primary"
                                    }
                                    disabled={locked}
                                    onClick={() => {
                                        if (controlled) {
                                            send("Release");
                                            return;
                                        }

                                        if (!active) {
                                            send("Select", r.id);
                                        }

                                        send("TakeControl");
                                    }}
                                >
                                    {controlled
                                        ? "Release"
                                        : "Take Control"}
                                </button>
                            </div>
                        </div>
                    );
                })}
            </div>

            {!rows.length && (
                <div className="ft-aircraft-empty">
                    No matching aircraft found.
                </div>
            )}
        </div>
    );
}
function Settings({scale,setScale}:{scale:number,setScale:(n:number)=>void}){const [category,setCategory]=useState("Joystick & HOTAS");const devices=useSafeArray(devices$), selected=useSafeString(selected$,""), status=useSafeString(status$,""), mode=useSafeString(mode$,"Chase");return <div className="ft-settings"><nav>{["General","Joystick & HOTAS","Keyboard","Flight Dynamics","Camera & Views","Interface","Diagnostics"].map(c=><button key={c} className={c===category?"ft-selected":""} onClick={()=>setCategory(c)}>{c}</button>)}</nav><section>{category==="Joystick & HOTAS"?<Controls/>:category==="Flight Dynamics"?<><BoundaryPanel/><CollisionSettings/></>:category==="Camera & Views"?<CameraPanel/>:category==="Interface"?<Range label="Interface scale" value={Math.round(scale*100)} min={80} max={140} step={5} unit="%" change={v=>setScale(v/100)}/>:category==="Keyboard"?<><div className="ft-section">CURRENT FALLBACK ASSIGNMENTS</div><p>Numpad 8 / 2 — Pitch</p><p>Numpad 4 / 6 — Roll</p><p>Numpad 7 / 9 — Yaw</p><p>Numpad + / − — Throttle</p><p>Numpad 5 — Neutral input</p><small>Keyboard remapping and exclusive game input ownership are not implemented in the existing backend.</small></>:category==="Diagnostics"?<><div className="ft-section">BACKEND STATUS</div><p>Input: {status}</p><p>Devices: {devices.join(", ")||"None"}</p><p>Aircraft: {selected?"Selected":"Not selected"}</p><p>Camera: {mode}</p><small>Windows-native WinMM enumeration; see Joystick & HOTAS for raw input diagnostics.</small></>:<><div className="ft-section">FLIGHT TAKEOVER</div><p>Experimental manual aircraft simulation. Use a test save; native routing restoration is not guaranteed.</p></>}</section></div>}
function Window({name,children,close,width,scale,offset,className=""}:{name:string,children:React.ReactNode,close:()=>void,width:number,scale:number,offset:number,className?:string}){const [collapsed,setCollapsed]=useState(false);const [pos,setPos]=useState(()=>({x:className==="ft-ground-window"?Math.max(20,window.innerWidth-430):20+offset*24,y:130+offset*28}));return <div className={"ft-window "+className} style={{left:pos.x,top:pos.y,width:`${width*scale}rem`,fontSize:`${15*scale}rem`}}><header onMouseDown={e=>{if(e.button!==0)return;const x=e.clientX-pos.x,y=e.clientY-pos.y;const move=(ev:MouseEvent)=>setPos({x:Math.max(0,Math.min(window.innerWidth-100,ev.clientX-x)),y:Math.max(0,Math.min(window.innerHeight-40,ev.clientY-y))});const up=()=>{document.removeEventListener("mousemove",move);document.removeEventListener("mouseup",up)};document.addEventListener("mousemove",move);document.addEventListener("mouseup",up);e.preventDefault()}}><b>{name}</b><div><button onMouseDown={e=>e.stopPropagation()} onClick={()=>setCollapsed(v=>!v)}>{collapsed?"+":"−"}</button><button onMouseDown={e=>e.stopPropagation()} onClick={close}>×</button></div></header>{!collapsed&&<div className="ft-content">{children}</div>}</div>}
const groundSnapshot$=bindValue<string>(id,"GroundSnapshot","0|Idle|0|Flight is not ready||0:0");
const startStatus$=bindValue<string>(id,"StartStatus","");
const startAircraft$=bindValue<string[]>(id,"StartAircraft",[]);
const startStands$=bindValue<string[]>(id,"StartStands",[]);
const brake$=bindValue<boolean>(id,"ParkingBrake",false);
const pickingDeparture$=bindValue<boolean>(id,"PickingDeparture",false);
const hasDeparture$=bindValue<boolean>(id,"HasDeparture",false);

const customDeparture$=bindValue<string>(id,"CustomDeparture","");
const defaults=[{label:"Catering truck",match:"prop_cateringgatenyk",x:12,z:5,yaw:0},{label:"Forward stairs",match:"stairs (airport ground handling pack)",x:-5,z:12,yaw:90},{label:"Rear stairs",match:"stairs (airport ground handling pack)",x:-5,z:-12,yaw:90},{label:"Baggage loader",match:"baggage loader (airport groundhandling pack)",x:5,z:-8,yaw:-90}];
const pushbackState$=bindValue<string>(id,"PushbackState","0");
function GearIcon(){return <svg className="ft-gear-icon" viewBox="0 0 24 24" fill="none" stroke="#e5e8eb" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 2.8l2.2.4.6 2.5 1.9 1.1 2.5-.7 1.5 1.7-.9 2.4.3 2.2 2 1.7-.7 2.2-2.6.3-1.5 1.6-.1 2.6-2.2.8-1.8-1.9-2.2-.1-1.9 1.8-2.1-.9v-2.6l-1.5-1.6-2.5-.5-.7-2.2 2.1-1.6.4-2.2-.8-2.5 1.6-1.6 2.5.8 1.9-1 .8-2.5z"/><circle cx="12" cy="12" r="3.2"/></svg>}
type ServiceProfile=typeof defaults[number];
type PositionField="x"|"z"|"yaw";
let savedOverrides:Record<number,string>={};
const servicePayload=(session:string,key:string,i:number,p:ServiceProfile,name:string)=>[session,key,i,p.x,p.z,p.yaw,name].join("|");
function parseGroundSnapshot(raw:string){
 const [head,...rows]=raw.split("\n");
 const [session="0",state="Idle",available="0",reason="",message="",owner=""]=head.split("|");
 const services:string[]=[],states:string[]=[],activeAssets:string[]=[];
 const targets=defaults.map(p=>({...p}));
 let preset={revision:"",key:"",auto:true,aircraft:"",label:"",dirty:false,notice:"",saved:false};
 const choices:{key:string;label:string;saved:boolean}[]=[];
 for(const row of rows){const [kind,key,value,asset,...rest]=row.split("|");
  if(kind==="P"&&key&&value)services.push(key+"|"+value);
  if(kind==="S"&&key!==undefined&&value){states.push(key+"|"+value);if(asset)activeAssets.push(key+"|"+asset)}
  if(kind==="G")preset={revision:key||"",key:value||"",auto:asset==="1",aircraft:rest[0]||"",label:rest[1]||"",dirty:rest[2]==="1",notice:rest[3]||"",saved:rest[4]==="1"};
  if(kind==="R"&&key&&value)choices.push({key,label:value,saved:asset==="1"});
  if(kind==="T"){const i=Number(key),x=Number(value),z=Number(asset),yaw=Number(rest[0]);if(Number.isInteger(i)&&i>=0&&i<4&&[x,z,yaw].every(Number.isFinite))targets[i]={...targets[i],x,z,yaw};}
 }
 return {session,state,canService:available==="1",reason,message,owner,services,states,activeAssets,preset,choices,targets};
}
function useGroundSnapshot(){const raw=useSafeString(groundSnapshot$,"0|Idle|0|Flight is not ready||0:0");return useMemo(()=>parseGroundSnapshot(raw),[raw])}
function normalizeAsset(name:string){return name.toLowerCase().replace(/[^a-z0-9]/g,"")}
function StartFlight({close}:{close:()=>void}){
 const {state,session,canService,reason,message,owner,services,states,activeAssets,preset,choices,targets}=useGroundSnapshot();
 const status=useSafeString(startStatus$,""),planes=useSafeArray(startAircraft$),brake=useSafeBoolean(brake$,false),flying=useSafeBoolean(flying$,false),departure=useSafeString(customDeparture$,"");
 const [expanded,setExpanded]=useState<number|null>(null);
 const [assetPicker,setAssetPicker]=useState(false);
 const pushback=Number(useSafeString(pushbackState$,"0"));

 const [plane,setPlane]=useState(""),[search,setSearch]=useState(""),[heading,setHeading]=useState("0"),[profiles,setProfiles]=useState(()=>defaults.map(p=>({...p}))),[overrides,setOverrides]=useState<Record<number,string>>(savedOverrides);
 const profileRef=React.useRef(profiles);
 const [loadedPreset,setLoadedPreset]=useState("");
 const [presetPicker,setPresetPicker]=useState(false),[newPreset,setNewPreset]=useState(false),[presetName,setPresetName]=useState("");
 const presetToken=session+":"+preset.revision;
 const presetReady=!!preset.revision&&loadedPreset===presetToken;
 React.useEffect(()=>{
  if(!preset.revision)return;
  const next=targets.map(p=>({...p}));profileRef.current=next;setProfiles(next);setLoadedPreset(presetToken);
 },[presetToken]);
 React.useEffect(()=>{setPresetPicker(false);setNewPreset(false);setPresetName("")},[session,state]);
 const selectPreset=(key:string)=>{if(!presetReady||!canService)return;send("SelectGroundPreset",[session,preset.revision,key].join("|"));setPresetPicker(false)};
 const savePreset=(action:string)=>{if(!presetReady)return;send("SaveGroundPreset",[session,preset.revision,action,...profileRef.current.reduce<number[]>((a,p)=>a.concat([p.x,p.z,p.yaw]),[])].join("|"));if(action.startsWith("new:")){setNewPreset(false);setPresetName("")}};
 React.useEffect(()=>{if(state==="Idle")send("StartScan")},[state]);
 React.useEffect(()=>{setExpanded(null);setAssetPicker(false)},[session,state]);
 const picking=useSafeBoolean(pickingDeparture$,false),hasDeparture=useSafeBoolean(hasDeparture$,false);
 React.useEffect(()=>()=>send("CaptureDeparture","cancel"),[]);
 const update=(i:number,key:string,name:string,k:PositionField,v:number)=>{
  if(!presetReady||!Number.isFinite(v))return;
  const next=profileRef.current.map((p,j)=>i===j?{...p,[k]:v}:p);
  profileRef.current=next;setProfiles(next);
  send("EditGroundPreset",[session,preset.revision,i,next[i].x,next[i].z,next[i].yaw].join("|"));
  // Send on every changed slider step; backend coalesces events by service each frame.
  if(canService&&key)send("StartServicePosition",servicePayload(session,key,i,next[i],name));
 };
 const selectAsset=(i:number,name:string)=>{const next={...savedOverrides,[i]:name};savedOverrides=next;setOverrides(next);setAssetPicker(false)};
 if(state==="Idle"&&picking)return <div style={{position:"absolute",top:"100px",left:"40%",pointerEvents:"none"}}><div style={{backgroundColor:"#18232f",padding:"12px",pointerEvents:"auto"}}><p>{status || "Move the cursor over terrain. Right-click to cancel."}</p><button onClick={()=>send("CaptureDeparture","cancel")}>Cancel</button></div></div>;
 if(state==="Idle")return <div className="ft-flight-planner"><header><div><small>NYOKO'S FLIGHT SIMULATOR</small><h1>Start a Flight</h1></div><button onClick={close}>Close ×</button></header><div className="ft-planner-body"><section className="ft-planner-aircraft"><h2>Select aircraft</h2><input className="ft-search" placeholder="Search aircraft" value={search} onChange={e=>setSearch(e.target.value)}/><div className="ft-plane-grid">{planes.filter(r=>r.toLowerCase().includes(search.toLowerCase())).map(row=>{const [key,name,...image]=row.split("|");return <button key={key} className={plane===key?"ft-selected":""} onClick={()=>setPlane(key)}>{image.join("|")?<img src={image.join("|")} onError={e=>{e.currentTarget.style.display="none"}}/>:<div className="ft-plane-placeholder">AIRCRAFT</div>}<b>{name}</b></button>})}</div></section><section className="ft-planner-location"><h2>Departure</h2><p>Choose any terrain location, not just an airport.</p><button onClick={()=>send("CaptureDeparture","begin")}>Choose location in city</button><p>{hasDeparture?"Selected: "+departure:"No departure selected"}</p><label>Heading (degrees)<input type="number" value={heading} onChange={e=>setHeading(e.target.value)}/></label><p>{status}</p><button onClick={()=>send("StartScan")}>Refresh aircraft</button><button className="ft-selected" disabled={flying||!plane||!hasDeparture||!heading.trim()||!Number.isFinite(Number(heading))} onClick={()=>send("StartBegin",[plane,heading].join("|"))}>Start flight</button></section></div></div>;
 return <Window key={session+":"+owner+":"+state} name={state==="Cinematic"?"PRE-FLIGHT":"GROUND OPERATIONS"} close={close} width={390} scale={1} offset={2} className="ft-ground-window">
 {state==="Cinematic"?<><p>{status}</p><button className="ft-selected ft-ground-full" onClick={()=>send("StartReady")}>Ready to Fly</button></>:<>
 <div className="ft-ground-brake"><div><b>Parking brake</b><small className={brake?"ft-service-arrived":""}>{brake?"Engaged":"Released"}</small></div><button disabled={pushback!==0} onClick={()=>send("ToggleBrake")}>{brake?"Release":"Apply"}</button></div>
 <div className="ft-ground-section"><div className="ft-line"><b>Pushback</b><small className={pushback?"ft-service-moving":""}>{pushback?"In progress":"Stopped"}</small></div><div className="ft-row ft-pushback-controls">{["Stop","Left","Straight","Right"].map((n,i)=><button key={n} className={pushback===i&&i!==0?"ft-selected":""} disabled={i===0?pushback===0:!canService&&pushback===0} onClick={()=>send("Pushback",String(i))}>{n}</button>)}</div></div>
 <div className="ft-ground-presets">
  <div className="ft-line"><b>Aircraft preset</b><small>{preset.auto?"Automatic":"Manual"}</small></div>
  <div className="ft-preset-name">{preset.label||"Loading aircraft preset..."}</div>
  <small>{preset.aircraft}</small><small className={preset.dirty?"ft-preset-dirty":""}>{preset.notice}</small>
  <div className="ft-row"><button className={preset.auto?"ft-selected":""} disabled={!presetReady||!canService} onClick={()=>selectPreset("auto")}>Automatic</button><button disabled={!presetReady||!canService} onClick={()=>setPresetPicker(v=>!v)}>{presetPicker?"Close presets":"Choose preset"}</button></div>
  {presetPicker&&<div className="ft-preset-list">{choices.map(c=><button key={c.key} className={c.key===preset.key?"ft-selected":""} onClick={()=>selectPreset(c.key)}><span>{c.label}</span><small>{c.saved?"Saved":"Not configured"}</small></button>)}</div>}
  <div className="ft-row"><button disabled={!presetReady} onClick={()=>savePreset("save")}>Save preset</button><button disabled={!presetReady} onClick={()=>setNewPreset(v=>!v)}>Save as new</button></div>
  <button className="ft-ground-full" disabled={!presetReady} onClick={()=>savePreset("exact")}>Save for this exact aircraft</button>
  {newPreset&&<div className="ft-preset-new"><input className="ft-search" placeholder="New preset name" maxLength={80} value={presetName} onChange={e=>setPresetName(e.target.value)}/><button disabled={!presetName.trim()||presetName.includes("|")} onClick={()=>savePreset("new:"+presetName.trim())}>Create preset</button></div>}
  <small>Saved presets store all four service targets. Changing preset replaces unsaved offsets.</small>
 </div>
 <div className="ft-ground-section"><div className="ft-line"><b>Ground services</b><button onClick={()=>send("RefreshGroundAssets")}>Refresh assets</button></div><small>{canService?"Select a service to configure or request.":reason||"Park the aircraft to service."}</small>{message&&<small>{message}</small>}{services.length===0&&<small>No ground handling assets found. Refresh assets.</small>}</div>
 {!presetReady?<small>Loading target positions...</small>:profiles.map((p,i)=>{
  const found=services.find(r=>normalizeAsset(r.split("|")[1]||"")===normalizeAsset(p.match));
  const override=overrides[i];
  const selectedAsset=override?services.find(r=>normalizeAsset(r.split("|")[1]||"")===normalizeAsset(override)):undefined;
  const key=(selectedAsset||found)?.split("|")[0]??"";
  const assetName=services.find(r=>r.split("|")[0]===key)?.split("|")[1]||"No asset selected";
  const reported=states.find(r=>r.split("|")[0]===String(i))?.split("|").slice(1).join("|")||"Not requested";
  const activeKey=activeAssets.find(r=>r.split("|")[0]===String(i))?.split("|")[1]||"";
  const replace=!!activeKey&&activeKey!==key;
  const present=!!activeKey&&!replace;
  const current=!present&&(reported==="Arrived"||reported==="On the way"||reported==="Preview")?"Not requested":reported;
  const fields:PositionField[]=["x","z","yaw"];
  return <section className="ft-service-config" key={i}>
   <div className="ft-service-row"><div className="ft-service-title"><b>{p.label}</b><small className={current==="Arrived"?"ft-service-arrived":current==="On the way"||current==="Preview"||current==="Initializing"?"ft-service-moving":current.indexOf("Failed")===0?"ft-service-error":""}>{key?current:"Asset not selected"}</small></div><button aria-label={"Configure "+p.label} title={"Configure "+p.label} className={"ft-service-gear "+(expanded===i?"ft-selected":"")} onClick={()=>{setExpanded(expanded===i?null:i);setAssetPicker(false)}}><GearIcon/></button><button className="ft-service-request" disabled={!key||!canService||(present&&(current==="On the way"||current==="Arrived"||current==="Initializing"))} onClick={()=>send("StartService",servicePayload(session,key,i,profileRef.current[i],assetName))}>{replace?"Replace":current==="Preview"?"Dispatch":current==="Arrived"?"Arrived":current==="On the way"?"En route":current==="Initializing"?"Preparing":"Request"}</button></div>
   {expanded===i&&<div className="ft-service-editor"><div className="ft-line"><small>Asset</small><button onClick={()=>setAssetPicker(v=>!v)}>{assetPicker?"Hide assets":"Change asset"}</button></div><div className="ft-service-asset-name">{assetName}</div>
    {assetPicker&&<div className="ft-service-options">{services.map(r=>{const [k,n]=r.split("|");return <button key={k} className={key===k?"ft-selected":""} onClick={()=>selectAsset(i,n)}>{n}</button>})}</div>}
    <b>Target position</b><small>{canService?"Drag to preview or reposition the service live.":"Park the aircraft to preview changes."}</small>
    <small>Relative to aircraft: +X right, +Z forward. Starting position is automatic.</small>
    {fields.map((k,j)=><Range key={k} label={["Lateral offset (X)","Longitudinal offset (Z)","Heading"][j]} value={p[k]} min={j===2?-180:-100} max={j===2?180:100} step={j===2?1:.5} unit={j===2?"°":" m"} live={true} change={v=>update(i,key,assetName,k,v)}/>)}
    <button className="ft-ground-full" disabled={!key||!canService} onClick={()=>send("StartServicePosition",servicePayload(session,key,i,profileRef.current[i],assetName))}>Preview target position</button>
   </div>}
  </section>
 })}
 <button className="ft-ground-full ft-dismiss" disabled={!activeAssets.length&&!states.some(r=>!r.endsWith("|Not requested"))} onClick={()=>send("StartDismiss")}>Dismiss services</button>
 </>}
 <button className="ft-ground-full ft-end-flight" onClick={()=>send("StartEnd")}>End flight</button>
 </Window>;
}
function Panel({close}:{close:()=>void}){const [windows,setWindows]=useState<string[]>(["Aircraft"]),[scale,setScale]=useState(1);const flying=useSafeBoolean(flying$,false), selected=useSafeString(selected$,"");const {state:startState,session,owner}=useGroundSnapshot();React.useEffect(()=>{if(startState==="Cinematic"||startState==="Flying")setWindows(w=>w.includes("Start a Flight")?w:[...w,"Start a Flight"])},[startState,session,owner]);const toggle=(n:string)=>setWindows(w=>w.includes(n)?w.filter(x=>x!==n):[...w,n]);return <div className="ft-root"><div className="ft-toolbar"><b>NYOKO'S FLIGHT SIMULATOR</b><span className={flying?"ft-live":""}>{flying?"MANUAL":"STANDBY"}</span>{["Start a Flight","Aircraft","Instruments","Camera","Settings"].map(n=><button key={n} className={windows.includes(n)?"ft-selected":""} onClick={()=>toggle(n)}>{n==="Settings"?<span className="ft-toolbar-settings"><GearIcon/> Settings</span>:n==="Aircraft"?"Flight Takeover":n}</button>)}<button disabled={!selected} onClick={()=>send("Release")}>Exit aircraft</button><button onClick={close}>×</button></div>{windows.filter(n=>n!=="Start a Flight"&&!(windows.includes("Start a Flight")&&n==="Aircraft")).map((n,i)=><Window key={n} name={n} width={n==="Settings"?650:n==="Instruments"?410:370} scale={scale} offset={i} close={()=>toggle(n)}>{n==="Aircraft"?<Aircraft/>:n==="Instruments"?<Instruments/>:n==="Camera"?<CameraPanel/>:<Settings scale={scale} setScale={setScale}/>}</Window>)}{windows.includes("Start a Flight")&&<StartFlight close={()=>toggle("Start a Flight")}/>}</div>}
function Launcher(){const [open,setOpen]=useState(false);return <><Button src={FlightTakeoverSVG} variant="floating" onClick={()=>setOpen(x=>!x)}/>{createPortal(<div className="ft-root"><Radar/><ImpactAlert/></div>,document.body)}{open&&createPortal(<Panel close={()=>setOpen(false)}/>,document.body)}</>}
const register:ModRegistrar=registry=>registry.append("GameTopLeft",Launcher);
export default register;
