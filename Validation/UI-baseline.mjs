/**
 * Cities: Skylines II UI Module
 * Id: curbside
 * Author: The Curbside Mod
 * Version: 0.5.1.51
 * Dependencies:
 */
const React=window.React;
const {bindValue,useValue,trigger}=window["cs2/api"];
const h=React.createElement;
const toolsBinding=bindValue("curbside","tools",[]);
const statusBinding=bindValue("curbside","status","Choose a tool.");
const activeBinding=bindValue("curbside","active",-1);
const diagnosticBinding=bindValue("curbside","diagnosticsEnabled",false);
const featuresBinding=bindValue("curbside","roadFeatures","");
const groups=["Markings","Parking","Crosswalks","Traffic","Decoration","Visibility","Restore"];
const order={Markings:["Inspect road","White line","Yellow line","Double yellow line","Double white line","White passing line","Yellow passing line","White standard-dash line","White short-dash line","White long-dash line","Yellow standard-dash line","Yellow short-dash line","Yellow long-dash line"],Parking:["Marked parking bays","T-mark parking bays","Full-depth T parking bays","Parking dividers to curb","Parking dividers only","Unmarked parking bays","Parking-end hatching","Yellow parking-end hatching","No parking"],Visibility:["Hide selected line","Hide all road lines","Hide vanilla lines","Hide lane and edge lines","Hide edge lines","Hide centerline","Hide lane arrows","Hide speed decals","Hide arrows and speed decals"],Restore:["Restore parking defaults","Restore line defaults","Reset Curbside upgrades"]};
const priority=t=>{const index=(order[t.group]||[]).indexOf(t.title);return index>=0?index:t.id;};
const button={border:"1rem solid #647585",borderRadius:"5rem",background:"#263442",color:"#f4f7fa",padding:"7rem 10rem",cursor:"pointer",fontSize:"14rem",boxSizing:"border-box",whiteSpace:"normal",overflowWrap:"break-word",wordBreak:"normal",minWidth:0};
function CurbsidePanel(){
 const [open,setOpen]=React.useState(false),[group,setGroup]=React.useState("Markings");
 const diagnostics=useValue(diagnosticBinding)===true;const roadFeatures=useValue(featuresBinding);
 const tools=useValue(toolsBinding)||[],status=useValue(statusBinding),active=useValue(activeBinding);
 const changeOpen=value=>{setOpen(value);trigger("curbside","setOpen",value);};
 React.useEffect(()=>()=>trigger("curbside","setOpen",false),[]);
 const selected=tools.find(t=>t.id===active);
 return h("div",{style:{position:"relative",pointerEvents:"auto",fontFamily:"sans-serif",color:"#f4f7fa"}},
  h("button",{type:"button",title:"The Curbside Mod — upgrade tools",style:{...button,margin:"3rem",fontWeight:"bold",background:open?"#37627a":"#263442"},onClick:()=>changeOpen(!open),"aria-expanded":open},"Curbside"),
  open&&h("section",{"aria-label":"Curbside upgrade tools",style:{position:"absolute",top:"44rem",left:"0rem",width:"390rem",maxWidth:"calc(100vw - 24rem)",boxSizing:"border-box",maxHeight:"70vh",overflowY:"auto",overflowX:"hidden",whiteSpace:"normal",overflowWrap:"break-word",background:"#141d27",border:"1rem solid #647585",borderRadius:"8rem",padding:"14rem",boxShadow:"0rem 6rem 20rem #0008"}},
   h("div",{style:{display:"flex",justifyContent:"space-between",alignItems:"center",gap:"8rem",minWidth:0,marginBottom:"10rem"}},h("strong",{style:{fontSize:"18rem",minWidth:0,overflowWrap:"break-word"}},"The Curbside Mod"),h("button",{type:"button",style:button,title:"Close panel; keep the active road tool",onClick:()=>changeOpen(false)},"Close")),
   h("div",{style:{display:"flex",flexWrap:"wrap",gap:"4rem",marginBottom:"10rem"}},groups.map(g=>h("button",{key:g,type:"button",style:{...button,background:g===group?"#37627a":button.background},onClick:()=>setGroup(g)},g))),
   h("div",null,tools.filter(t=>t.group===group).sort((a,b)=>priority(a)-priority(b)).map(t=>h("button",{key:t.id,type:"button",disabled:!t.available,title:t.available?t.hint:"Unavailable while loading or locked. "+t.hint,style:{...button,display:"flex",alignItems:"center",textAlign:"left",width:"100%",marginBottom:"5rem",opacity:t.available?1:.45,background:t.id===active?"#37627a":button.background},onClick:()=>trigger("curbside","select",t.id)},h("img",{src:t.icon,alt:"",style:{width:"40rem",height:"40rem",flexShrink:0,marginRight:"9rem"}}),h("strong",{style:{flexGrow:1,flexShrink:1,minWidth:0,whiteSpace:"normal",fontSize:"14rem",lineHeight:"18rem"}},t.title)))),

   !tools.length&&h("p",null,"Road tools are loading…"),
   h("p",{style:{fontSize:"13rem",lineHeight:"18rem",borderTop:"1rem solid #647585",paddingTop:"10rem"}},selected?selected.hint:status),
   selected&&status!==selected.hint&&h("p",{role:"status",style:{fontSize:"13rem",lineHeight:"18rem",whiteSpace:"normal"}},status),
   roadFeatures&&h("details",{open:selected?.title==="Inspect road",style:{borderTop:"1rem solid #647585",padding:"8rem 0",fontSize:"13rem",lineHeight:"19rem"}},h("summary",{style:{cursor:"pointer",fontWeight:"bold"}},"Applied tools"),h("p",{style:{whiteSpace:"pre-wrap",overflowWrap:"anywhere",margin:"6rem 0"}},roadFeatures)),
   h("button",{type:"button",style:button,onClick:()=>trigger("curbside","leaveTool")},"Leave active tool"),
   h("button",{type:"button",style:button,onClick:()=>{const next=!diagnostics;trigger("curbside","diagnostics",next);}},diagnostics?"Diagnostics: on":"Diagnostics: off"),
   diagnostics&&h("button",{type:"button",style:button,onClick:()=>trigger("curbside","snapshot")},"Write diagnostic snapshot"),
   diagnostics&&h("button",{type:"button",style:button,title:"Create a separate diagnostic save using the native save pipeline, without a thumbnail",onClick:()=>trigger("curbside","saveWithoutPreview")},"Save test without thumbnail"),
   h("p",{style:{fontSize:"12rem",lineHeight:"17rem",color:"#b4c1cc"}},"Hover a road to preview the target. Click applies; right-click removes or restores as described above. Texture and line settings: Options → The Curbside Mod.")));
}
export default function register(moduleRegistry){moduleRegistry.append("GameTopLeft",CurbsidePanel);}
