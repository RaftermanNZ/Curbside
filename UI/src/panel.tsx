import React from "react";
import { bindValue, useValue, trigger } from "cs2/api";
import { Button, Panel, Tooltip, Scrollable } from "cs2/ui";
import { createTranslator } from "./localization";
import styles from "./panel.module.scss";
interface Tool { id:number; title:string; group:string; icon:string; hint:string; available:boolean; }
const h=React.createElement;
const toolsBinding=bindValue<Tool[]>("curbside","tools",[]);
const translationsBinding=bindValue<Record<string,string>>("curbside","translations",{});
const statusBinding=bindValue("curbside","status","Choose a tool.");
const activeBinding=bindValue("curbside","active",-1);
const diagnosticBinding=bindValue("curbside","diagnosticsEnabled",false);
const featuresBinding=bindValue("curbside","roadFeatures","");
const groups=["Markings","Parking","Crosswalks","Traffic","Decoration","Visibility","Restore"];
const order:Record<string,string[]>={Markings:["Inspect road","White line","Yellow line","Double yellow line","Double white line","White passing line","Yellow passing line","White standard-dash line","White short-dash line","White long-dash line","Yellow standard-dash line","Yellow short-dash line","Yellow long-dash line"],Parking:["Marked parking bays","T-mark parking bays","Full-depth T parking bays","Parking dividers to curb","Parking dividers only","Unmarked parking bays","Parking-end hatching","Yellow parking-end hatching","No parking"],Visibility:["Hide selected line","Hide all road lines","Hide vanilla lines","Hide lane and edge lines","Hide edge lines","Hide centerline","Hide lane arrows","Hide speed decals","Hide arrows and speed decals"],Restore:["Restore parking defaults","Restore line defaults","Reset Curbside upgrades"]};
const priority=(t:Tool)=>{const index=(order[t.group]||[]).indexOf(t.title);return index>=0?index:t.id;};
export function CurbsidePanel(){
 const translations=useValue(translationsBinding);
 const t=React.useMemo(()=>createTranslator(translations),[translations]);
 const [open,setOpen]=React.useState(false),[group,setGroup]=React.useState("Markings");
 const [featuresOpen,setFeaturesOpen]=React.useState(false),[diagnosticsOpen,setDiagnosticsOpen]=React.useState(false);
 const bodyRef=React.useRef<HTMLDivElement>(null);
 const [bodyHeight,setBodyHeight]=React.useState<number|undefined>(undefined);
 React.useEffect(()=>{
  const element=bodyRef.current;if(!open||!element||typeof ResizeObserver==="undefined")return;
  let observing=true;
  const observer=new ResizeObserver(entries=>{if(!observing)return;for(const entry of entries){if(entry.target===element){const height=Math.ceil(entry.contentRect.height);setBodyHeight(previous=>previous===height?previous:height);}}});
  observer.observe(element);
  return()=>{observing=false;observer.disconnect();};
 },[open,group]);
 const diagnostics=useValue(diagnosticBinding)===true;const roadFeatures=useValue(featuresBinding);
 const tools=useValue(toolsBinding)||[],status=useValue(statusBinding),active=useValue(activeBinding);
 const changeOpen=(value:boolean)=>{setOpen(value);trigger("curbside","setOpen",value);};
 React.useEffect(()=>()=>trigger("curbside","setOpen",false),[]);
 const selected=tools.find(t=>t.id===active);
 React.useEffect(()=>{if(selected?.title==="Inspect road")setFeaturesOpen(true);},[selected?.id]);
 const visible=tools.filter(t=>t.group===group).sort((a,b)=>priority(a)-priority(b));
 return h("div",{className:styles.root},
  h(Tooltip,{tooltip:t("Curbside road upgrades"),children:h(Button,{variant:"floating",src:"coui://mgr-icons/CurbsideInfo.svg",tinted:true,className:styles.launcher,selected:open,"aria-label":"Curbside","aria-expanded":open,"aria-controls":"curbside-upgrades",onSelect:()=>changeOpen(!open)})}),
  open&&h(Panel,{id:"curbside-upgrades",className:styles.panel,contentClassName:styles.content,header:"Curbside",onClose:()=>changeOpen(false),showCloseHint:false,allowFocusExit:true,footer:h("div",{className:styles.pinnedFooter},h(Button,{variant:"flat",className:styles.leave,disabled:active<0,onSelect:()=>trigger("curbside","leaveTool")},t("Leave active tool"))),"aria-label":t("Curbside road upgrades")},
   h("nav",{className:styles.categories,"aria-label":t("Upgrade categories")},groups.map(g=>h(Button,{key:g,variant:"flat",className:styles.category,selected:g===group,"aria-pressed":g===group,onSelect:()=>setGroup(g)},t(g)))),
   h(Scrollable,{key:group,vertical:true,horizontal:false,trackVisibility:"scrollable",className:styles.scrollArea,style:{height:bodyHeight===undefined?undefined:bodyHeight+"px"}},
   h("div",{className:styles.body,ref:bodyRef},
   h("div",{className:styles.list,"aria-label":t(group)+" "+t("upgrades")},
    visible.map(tool=>{
     const tile=h(Button,{variant:"flat",className:styles.upgrade,disabled:!tool.available,selected:tool.id===active,"aria-pressed":tool.id===active,onSelect:()=>trigger("curbside","select",tool.id)},
      h("img",{src:tool.icon,alt:"",className:styles.icon}),h("span",{className:styles.label},t(tool.title)));
     return h(Tooltip,{key:tool.id,tooltip:tool.available?t(tool.hint):t("Unavailable while loading or locked. ")+t(tool.hint),children:tile});
    }),
    !tools.length&&h("div",{className:styles.empty},t("Preparing road upgrades…"))),
   h("div",{className:styles.context},
    h("div",{className:styles.description},
     selected&&h("div",{className:styles.contextTitle},t(selected.title)),
     h("div",null,t(selected?selected.hint:status)),
     selected&&status!==selected.hint&&h("div",{role:"status",className:styles.status},t(status))),
    roadFeatures&&h("div",{className:styles.details},
     h(Button,{variant:"text",className:styles.foldout,"aria-expanded":featuresOpen,onSelect:()=>setFeaturesOpen(!featuresOpen)},featuresOpen?t("− Applied upgrades"):t("+ Applied upgrades")),
     featuresOpen&&h("div",{className:styles.features},t(roadFeatures)))),
   h("div",{className:styles.footer},
    h("div",{className:styles.diagnostics},
     h(Button,{variant:"text",className:styles.foldout,"aria-expanded":diagnosticsOpen,onSelect:()=>setDiagnosticsOpen(!diagnosticsOpen)},(diagnosticsOpen?t("− Diagnostics"):t("+ Diagnostics"))+(diagnostics?t(" · On"):"")),
     diagnosticsOpen&&h("div",{className:styles.diagnosticActions},
      h(Button,{variant:"flat",className:styles.secondary,"aria-pressed":diagnostics,onSelect:()=>trigger("curbside","diagnostics",!diagnostics)},diagnostics?t("Diagnostics: on"):t("Diagnostics: off")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,onSelect:()=>trigger("curbside","startLineRecording",false)},t("Start line recording")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,onSelect:()=>trigger("curbside","startLineRecording",true)},t("Record + screenshots every 30 seconds")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,onSelect:()=>trigger("curbside","markLineRecording")},t("Mark hovered intersection")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,onSelect:()=>trigger("curbside","stopLineRecording")},t("Stop and export recording")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,onSelect:()=>trigger("curbside","snapshot")},t("Write diagnostic snapshot")),
      diagnostics&&h(Button,{variant:"flat",className:styles.secondary,title:t("Create a separate diagnostic save using the native save pipeline, without a thumbnail"),onSelect:()=>trigger("curbside","saveWithoutPreview")},t("Save test without thumbnail")))),
    h("div",{className:styles.help},
     h("div",{className:styles.text},t("Click to apply. Right-click removes or restores.")),
     h("div",{className:styles.text},t("Settings: Options → The Curbside Mod"))))))));
}
