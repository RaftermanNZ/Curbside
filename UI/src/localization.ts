// English IDs remain canonical: selection, order, categories and saved IDs never change.
export function createTranslator(table: Record<string,string>) {
 const entries=Object.entries(table).filter(([source,target])=>typeof target==="string"&&target.trim().length>0&&source!==target&&source.trim().length>1);
 if(!entries.length)return (text:string)=>text;
 const escape=(text:string)=>text.replace(/[.*+?^${}()|[\]\\]/g,"\\$&");
 // One pass: translated words are never translated again. Prefer complete phrases.
 const paths="(?:[A-Za-z]:[\\\\/][^\\r\\n\"<>]*|[^\\s\\\\/]+\\.(?:png|jpg|jpeg|dds|json|log|cok|fbx)\\b)";
 const pattern=new RegExp(paths+"|"+entries.sort((a,b)=>b[0].length-a[0].length).map(([text])=>
   (/^[A-Za-z]/.test(text)?"\\b":"")+escape(text)+(/[A-Za-z]$/.test(text)?"\\b":"")).join("|"),"g");
 const cache=new Map<string,string>();
 return (text:string)=>{
  if(!text)return text;
  if(Object.prototype.hasOwnProperty.call(table,text)&&table[text]?.trim())return table[text];
  const cached=cache.get(text);if(cached!==undefined)return cached;
  const result=text.replace(pattern,match=>table[match]??match);
  // Dynamic status text cannot grow a session-long unbounded cache.
  if(cache.size>=256)cache.clear();cache.set(text,result);return result;
 };
}
