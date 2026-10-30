using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Serialization;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MedianGrassReplacement.Systems
{
    // Temporary visual labels. Native street names remain the sole saved source.
    public partial class SignalStreetNameSystem : Game.GameSystemBase
    {
        private sealed class Label
        {
            internal Entity Node, Road, Prefab;
            internal float3 Position;
            internal TextMeshPro Text;
            internal MeshFilter Surface;
            internal Mesh Mesh;
            internal string Name;internal bool RetryAtlas;
        }
        private readonly Dictionary<Entity,Label> labels=new Dictionary<Entity,Label>();
        private readonly List<Entity> removed=new List<Entity>();
        private TMP_FontAsset font;
        private Material material;
        private float next;
        private bool failed;
        private bool materialChecked;
        private byte[] coverage;
        private int atlasWidth,atlasHeight;
        private Dictionary<int4,List<int4>> glyphs=new Dictionary<int4,List<int4>>();
        private sealed class AtlasMask {internal byte[] Pixels;internal int Width,Height,GlyphCount,Characters,ForcedEpoch;internal uint Version;internal readonly Dictionary<int4,List<int4>> Glyphs=new Dictionary<int4,List<int4>>();}
        private readonly Dictionary<int,AtlasMask> masks=new Dictionary<int,AtlasMask>();
        private int epoch;
        private long atlasReadbacks,meshRebuilds;
        internal string CostStatus=>"streetNameLabels="+labels.Count+" atlasReadbacks="+atlasReadbacks+" meshRebuilds="+meshRebuilds+" atlasCache="+masks.Count;
        private uint atlasVersion;
        internal static void PhysicalCoverage(Color32[] pixels)
        {
            int minR=255,maxR=0,minA=255,maxA=0;
            foreach(var p in pixels){minR=System.Math.Min(minR,p.r);maxR=System.Math.Max(maxR,p.r);minA=System.Math.Min(minA,p.a);maxA=System.Math.Max(maxA,p.a);}
            bool alpha=maxA-minA>2;
            for(int i=0;i<pixels.Length;i++){byte coverage=alpha?pixels[i].a:pixels[i].r;pixels[i]=new Color32(255,255,255,coverage);}
        }
        private void RefreshCoverage(Texture atlas=null,bool force=false)
        {
            if(atlas==null)atlas=font.atlasTexture;
            int id=atlas.GetInstanceID(),count=font.glyphTable.Count,characters=font.characterTable.Count;
            bool known=masks.TryGetValue(id,out var mask);
            if(!known){if(masks.Count>=4)masks.Clear();mask=new AtlasMask();masks[id]=mask;}
            bool changed=mask.Pixels==null||mask.Version!=atlas.updateCount||mask.GlyphCount!=count||mask.Characters!=characters||mask.Width!=System.Math.Min(1024,atlas.width)||mask.Height!=System.Math.Min(1024,atlas.height);
            if(changed||(force&&mask.ForcedEpoch!=epoch))
            {
                int width=System.Math.Min(1024,atlas.width),height=System.Math.Min(1024,atlas.height);
                var temporary=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                var previous=RenderTexture.active;Texture2D readable=null;
                try
                {
                    Graphics.Blit(atlas,temporary);RenderTexture.active=temporary;
                    readable=new Texture2D(width,height,TextureFormat.RGBA32,false,true);readable.ReadPixels(new Rect(0,0,width,height),0,0,false);atlasReadbacks++;
                    var pixels=readable.GetPixels32();PhysicalCoverage(pixels);mask.Pixels=new byte[pixels.Length];for(int i=0;i<pixels.Length;i++)mask.Pixels[i]=pixels[i].a;
                    mask.Width=width;mask.Height=height;mask.Version=atlas.updateCount;mask.GlyphCount=count;mask.Characters=characters;mask.Glyphs.Clear();mask.ForcedEpoch=epoch;
                    if(changed)foreach(var label in labels.Values)label.Name=null;
                    Mod.Log.Info("Street-name atlas mask refreshed: id="+id+" size="+width+"x"+height+" glyphs="+count+" characters="+characters+" pages="+font.atlasTextures.Length+" delayed="+force);
                }
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(temporary);if(readable!=null)Object.Destroy(readable);}
            }
            coverage=mask.Pixels;atlasWidth=mask.Width;atlasHeight=mask.Height;atlasVersion=mask.Version;glyphs=mask.Glyphs;
        }
        private bool EnsureMaterial()
        {
            if(material!=null)return true;
            if(materialChecked)return false;
            materialChecked=true;
            font=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetTextMesh().font;
            var shader=Shader.Find("HDRP/Lit");
            if(font==null||shader==null||!shader.isSupported){Mod.Log.Warn("Physical street-name lettering unavailable: native font or HDRP/Lit missing; leaving unlit sign plates.");return false;}
            // Use native font silhouettes as opaque geometry. The diagnostic
            // atlas contains valid letters, but the runtime cutout displayed
            // their full quads. No glow or alternate UI font shader is needed.
            material=new Material(shader){name="Curbside shared physical street-name lettering",hideFlags=HideFlags.DontSave};
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_AlphaCutoffEnable",0);material.SetTexture("_BaseColorMap",Texture2D.whiteTexture);
            material.SetFloat("_Smoothness",.15f);material.SetFloat("_Metallic",0);
            material.SetColor("_EmissiveColor",Color.black);material.SetColor("_EmissiveColorLDR",Color.black);
            material.SetFloat("_EmissiveIntensity",0);material.SetFloat("_SurfaceType",0);
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType","Opaque");material.renderQueue=2000;
            RefreshCoverage();
            // HDRP does not validate materials changed from C# automatically.
            // Validation configures stencil, clipping, keywords and render passes.
            if(!UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material))throw new System.InvalidOperationException("HDRP rejected the physical street-name material");
            Mod.Log.Info("Street-name material validated: shader="+material.shader.name+" alphaTest="+material.IsKeywordEnabled("_ALPHATEST_ON")+" queue="+material.renderQueue+" stencil="+material.GetFloat("_StencilRef"));
            return true;
        }
        internal void ExportDiagnostics()
        {
            if(font==null||material==null)return;
            try
            {
                string folder=System.IO.Path.Combine(Application.persistentDataPath,"ModsData","TheCurbsideMod","Diagnostics");System.IO.Directory.CreateDirectory(folder);
                string prefix=System.IO.Path.Combine(folder,"street-name-"+System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                ExportTexture(font.atlasTexture,prefix+"-source.png");
                if(coverage!=null)
                {
                    var image=new Texture2D(atlasWidth,atlasHeight,TextureFormat.RGBA32,false,true);
                    try{var pixels=new Color32[coverage.Length];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(coverage[i],coverage[i],coverage[i],255);image.SetPixels32(pixels);image.Apply(false,false);System.IO.File.WriteAllBytes(prefix+"-coverage.png",image.EncodeToPNG());}
                    finally{Object.Destroy(image);}
                }
                System.IO.File.WriteAllText(prefix+"-material.txt","Shader="+material.shader.name+"\nKeywords="+string.Join(",",material.shaderKeywords)+"\nQueue="+material.renderQueue+"\nFontFormat="+font.atlasTexture.format+"\nFontDimensions="+font.atlasTexture.width+"x"+font.atlasTexture.height+"\nAlphaCutoff="+material.GetFloat("_AlphaCutoff")+"\nAlphaEnabled="+material.GetFloat("_AlphaCutoffEnable")+"\nStencil="+material.GetFloat("_StencilRef")+"\nLabels="+labels.Count);
                Mod.Log.Info("Street-name texture/material diagnostics exported: "+prefix);
            }
            catch(System.Exception error){Mod.Log.Warn("Street-name diagnostic export failed: "+error.Message);}
        }
        private static void ExportTexture(Texture texture,string path)
        {
            int width=System.Math.Min(1024,texture.width),height=System.Math.Min(1024,texture.height);
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;Texture2D readable=null;
            try{Graphics.Blit(texture,target);RenderTexture.active=target;readable=new Texture2D(width,height,TextureFormat.RGBA32,false,true);readable.ReadPixels(new Rect(0,0,width,height),0,0,false);readable.Apply(false,false);System.IO.File.WriteAllBytes(path,readable.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(readable!=null)Object.Destroy(readable);}
        }
        internal void Set(Entity prop,Entity node,Entity road,float3 position,float3 facing)
        {
            if(failed)return;
            try{SetCore(prop,node,road,position,facing);}
            catch(System.Exception error){failed=true;Clear();Mod.Log.Warn("Street-name lettering disabled for this session: "+error.Message);}
        }
        internal static bool LetteringIsolationEnabled => true;
        private void SetCore(Entity prop,Entity node,Entity road,float3 position,float3 facing)
        {
            if(LetteringIsolationEnabled)return;
            if(!OwnersValid(prop,node,road)||!math.all(math.isfinite(position))||math.lengthsq(facing)<.5f)return;
            if(!labels.TryGetValue(prop,out var label))
            {
                if(labels.Count>=256||!EnsureMaterial())return;
                var go=new GameObject("Curbside street name");go.hideFlags=HideFlags.DontSave;
                var text=go.AddComponent<TextMeshPro>();text.font=font;text.fontSharedMaterial=font.material;
                text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.richText=false;
                text.enableWordWrapping=false;text.enableAutoSizing=true;text.fontSize=2.4f;text.fontSizeMin=.7f;text.fontSizeMax=2.4f;
                text.rectTransform.sizeDelta=new Vector2(1.48f,.26f);
                text.enabled=false;text.renderer.enabled=false;
                var surface=new GameObject("Physical lettering"){hideFlags=HideFlags.DontSave};surface.transform.SetParent(go.transform,false);
                var filter=surface.AddComponent<MeshFilter>();var renderer=surface.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=true;
                go.SetActive(false);
                var mesh=new Mesh{name="Curbside street-name layout",hideFlags=HideFlags.DontSave};mesh.MarkDynamic();filter.sharedMesh=mesh;
                label=new Label{Text=text,Surface=filter,Mesh=mesh};labels.Add(prop,label);
            }
            label.Node=node;label.Road=road;label.Prefab=EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab;label.Position=position;
            label.Text.transform.position=new Vector3(position.x,position.y,position.z);
            label.Text.transform.rotation=Quaternion.LookRotation(new Vector3(-facing.x,-facing.y,-facing.z),Vector3.up);
        }
        private bool LiveOwner(Entity entity)=>entity!=Entity.Null&&EntityManager.Exists(entity)&&!EntityManager.HasComponent<Deleted>(entity)&&!EntityManager.HasComponent<Game.Tools.Temp>(entity)&&EntityManager.HasComponent<PrefabRef>(entity);
        private bool OwnersValid(Entity prop,Entity node,Entity road)=>LiveOwner(prop)&&LiveOwner(node)&&LiveOwner(road)&&EntityManager.HasComponent<MGRGardenCitySignals>(node)&&EntityManager.HasComponent<TrafficLights>(node);
        private static void DestroyLabel(Label label)
        {
            if(label.Text!=null)label.Text.gameObject.SetActive(false);
            if(label.Surface!=null)label.Surface.sharedMesh=null;
            if(label.Mesh!=null)Object.Destroy(label.Mesh);
            if(label.Text!=null)Object.Destroy(label.Text.gameObject);
        }
        protected override void OnUpdate()
        {long started=ModDiagnostics.RuntimeCosts.Begin();try{UpdateMeasuredCore();}finally{ModDiagnostics.RuntimeCosts.End("SignalStreetNameSystem",started);}}
        private void UpdateMeasuredCore()
        {
            if(failed)return;
            try{UpdateNames();}
            catch(System.Exception error){failed=true;Clear();Mod.Log.Warn("Street-name lettering disabled for this session: "+error.Message);}
        }
        private void UpdateNames()
        {
            if(labels.Count==0||Mod.Saving||UnityEngine.Time.realtimeSinceStartup<next)return;next=UnityEngine.Time.realtimeSinceStartup+2;
            Dependency.Complete();epoch++;removed.Clear();var camera=World.GetOrCreateSystemManaged<Game.Rendering.CameraUpdateSystem>().activeCamera;
            // Remove invalid owners before touching the shared font/atlas or rebuilding letters.
            foreach(var item in labels)
            {
                var label=item.Value;
                if(!OwnersValid(item.Key,label.Node,label.Road)||EntityManager.GetComponentData<PrefabRef>(item.Key).m_Prefab!=label.Prefab||label.Text==null||label.Mesh==null)removed.Add(item.Key);
            }
            foreach(var prop in removed){DestroyLabel(labels[prop]);labels.Remove(prop);}
            removed.Clear();if(labels.Count==0)return;
            if(material!=null)RefreshCoverage();
            int rebuiltThisPass=0;
            var names=World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            foreach(var item in labels)
            {
                var label=item.Value;

                bool active=label.Text.gameObject.activeSelf;
                bool near=camera!=null&&Vector3.SqrMagnitude(label.Text.transform.position-camera.transform.position)<(active?10000:6400);
                if(near!=active)label.Text.gameObject.SetActive(near);if(!near)continue;
                Entity road=label.Road;if(EntityManager.HasComponent<Aggregated>(road))road=EntityManager.GetComponentData<Aggregated>(road).m_Aggregate;
                if(!EntityManager.Exists(road)||EntityManager.HasComponent<Deleted>(road))road=label.Road;string name=names.GetRenderedLabelName(road);
                if(string.IsNullOrWhiteSpace(name)&&road!=label.Road)name=names.GetRenderedLabelName(label.Road);
                if(name!=label.Name||label.RetryAtlas)
                {
                    if(rebuiltThisPass>=8)continue;rebuiltThisPass++;meshRebuilds++;
                    bool retry=name==label.Name&&label.RetryAtlas;label.RetryAtlas=!retry;label.Text.text=name??"";label.Text.ForceMeshUpdate(true,true);
                    // Disabled TMP calculates layout, but deliberately does NOT upload
                    // m_mesh. Copy the calculated layout to our independently lit mesh.
                    var info=label.Text.textInfo;
                    BuildLetterGeometry(label.Mesh,info,retry);label.Name=name;
                    label.Text.renderer.enabled=false;
                    Mod.Log.Info("Physical street-name layout: prop="+item.Key+" characters="+label.Text.textInfo.characterCount+" triangles="+label.Mesh.triangles.Length/3+" size="+label.Mesh.bounds.size);
                }
            }
            foreach(var prop in removed){DestroyLabel(labels[prop]);labels.Remove(prop);}
        }
        private void BuildLetterGeometry(Mesh mesh,TMP_TextInfo text,bool force)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var normals=new List<Vector3>();
            int emptyGlyphs=0,pages=System.Math.Min(4,text.materialCount);
            for(int page=0;page<pages;page++)
            {
            var info=text.meshInfo[page];if(info.vertexCount==0)continue;
            Texture atlas=info.material!=null?info.material.GetTexture("_MainTex"):null;
            RefreshCoverage(atlas,force);
            for(int i=0;i+3<info.vertexCount;i+=4)
            {
                var uv=info.uvs0[i];var du=info.uvs0[i+3]-uv;var dv=info.uvs0[i+1]-uv;
                if(du.x<=0||dv.y<=0||Mathf.Abs(du.y)>.0001f||Mathf.Abs(dv.x)>.0001f)continue;
                var bounds=new int4(Mathf.Clamp(Mathf.FloorToInt(uv.x*atlasWidth),0,atlasWidth-1),Mathf.Clamp(Mathf.FloorToInt(uv.y*atlasHeight),0,atlasHeight-1),Mathf.Clamp(Mathf.CeilToInt((uv.x+du.x)*atlasWidth),1,atlasWidth),Mathf.Clamp(Mathf.CeilToInt((uv.y+dv.y)*atlasHeight),1,atlasHeight));
                if(!glyphs.TryGetValue(bounds,out var rectangles)){rectangles=GlyphMaskGeometry.Rectangles(coverage,atlasWidth,atlasHeight,bounds);if(glyphs.Count>=1024)glyphs.Clear();glyphs.Add(bounds,rectangles);}
                if(rectangles.Count==0)emptyGlyphs++;
                foreach(var r in rectangles)
                {
                    // A hard per-sign limit prevents pathological names or
                    // replacement fonts from expanding visual work unboundedly.
                    if(vertices.Count>=4096){mesh.Clear();Mod.Log.Warn("Street-name geometry budget exceeded: "+text.characterCount+" characters; no partial label displayed.");return;}
                    float left=Mathf.Clamp01((r.x/(float)atlasWidth-uv.x)/du.x),right=Mathf.Clamp01((r.z/(float)atlasWidth-uv.x)/du.x);
                    float bottom=Mathf.Clamp01((r.y/(float)atlasHeight-uv.y)/dv.y),top=Mathf.Clamp01((r.w/(float)atlasHeight-uv.y)/dv.y);
                    var origin=info.vertices[i];var across=info.vertices[i+3]-origin;var up=info.vertices[i+1]-origin;int v=vertices.Count;
                    vertices.Add(origin+across*left+up*bottom);vertices.Add(origin+across*left+up*top);vertices.Add(origin+across*right+up*top);vertices.Add(origin+across*right+up*bottom);
                    for(int n=0;n<4;n++)normals.Add(info.normals[i]);
                    triangles.Add(v);triangles.Add(v+1);triangles.Add(v+2);triangles.Add(v+2);triangles.Add(v+3);triangles.Add(v);
                }
            }
            }
            if(emptyGlyphs>0||text.materialCount>4)Mod.Log.Warn("Street-name coverage gaps: characters="+text.characterCount+" empty="+emptyGlyphs+" materialPages="+text.materialCount);
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();if(mesh.bounds.size.magnitude>2){mesh.Clear();Mod.Log.Warn("Invalid street-name layout bounds rejected.");}
        }
        internal void Clear()
        {
            foreach(var label in labels.Values)DestroyLabel(label);labels.Clear();removed.Clear();
            if(material!=null)Object.Destroy(material);material=null;font=null;next=0;materialChecked=false;
            coverage=null;glyphs.Clear();masks.Clear();epoch=0;atlasReadbacks=meshRebuilds=0;atlasWidth=atlasHeight=0;atlasVersion=0;
        }
        protected override void OnGamePreload(Purpose purpose,GameMode mode){base.OnGamePreload(purpose,mode);Clear();failed=false;}
        protected override void OnDestroy(){Clear();base.OnDestroy();}
    }
}
