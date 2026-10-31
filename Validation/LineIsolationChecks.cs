using System;
using Unity.Entities;
using System.Reflection;
using Colossal.Mathematics;
using Unity.Mathematics;
class LineIsolationChecks
{
 static readonly Assembly A=Assembly.LoadFrom("MedianGrassReplacement.dll");
 static MethodInfo M(string type,string method)=>A.GetType("MedianGrassReplacement.Systems."+type,true).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static);
 static int count;
 static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
 static void Main()
 {
  // Connected-owner targeting: owner of the raycast is not necessarily the
  // owner of the closest painted path at a merge boundary.
  var choiceType=A.GetType("MedianGrassReplacement.Systems.HoverLineChoice",true);
  var choiceListType=typeof(System.Collections.Generic.List<>).MakeGenericType(choiceType);
  var pick=M("HoverLinePolicy","Pick");
  var fields=BindingFlags.Instance|BindingFlags.NonPublic;
  for(int mirror=-1;mirror<=1;mirror+=2)for(int angle=0;angle<8;angle++){
   float radians=angle*math.PI/4;
   float3 Rotate(float3 p)=>new float3(mirror*p.x*math.cos(radians)-p.z*math.sin(radians),p.y,mirror*p.x*math.sin(radians)+p.z*math.cos(radians));
   var choices=(System.Collections.IList)Activator.CreateInstance(choiceListType);
   void AddChoice(int owner,bool node,float offset){
    var choice=Activator.CreateInstance(choiceType);
    choiceType.GetField("Target",fields).SetValue(choice,new Entity{Index=owner,Version=2});
    choiceType.GetField("Node",fields).SetValue(choice,node);
    var path=new Bezier4x3(Rotate(new float3(offset,0,-10)),Rotate(new float3(offset,0,-3)),Rotate(new float3(offset,0,3)),Rotate(new float3(offset,0,10)));
    choiceType.GetField("Paths",fields).SetValue(choice,new System.Collections.Generic.List<Bezier4x3>{path});choices.Add(choice);
   }
   AddChoice(100,true,0);AddChoice(200,false,8);
   foreach(int expected in new[]{0,1}){
    int selected=(int)pick.Invoke(null,new object[]{choices,Rotate(new float3(expected*8+.1f,0,0))});
    Check(selected==expected,"Connected owner path is unreachable");
    var owner=(Entity)choiceType.GetField("Target",fields).GetValue(choices[selected]);
    Check(owner.Index==(expected==0?100:200),"Selected path routed to raycast owner instead of actual owner");
    Check((bool)choiceType.GetField("Node",fields).GetValue(choices[selected])==(expected==0),"Node/section apply mode mismatches selected owner");
   }
   Check((int)pick.Invoke(null,new object[]{choices,Rotate(new float3(20,0,0))})==-1,"Distant miss edits an unrelated path");
  }
  var matches=M("GranularHighwayLinePolicy","Matches");var valid=M("GranularHighwayLinePolicy","ValidStyle");
  var curve=new Bezier4x3(new float3(0,0,0),new float3(1,0,4),new float3(2,0,8),new float3(4,0,12));
  Check((bool)matches.Invoke(null,new object[]{curve,curve}),"Same line not matched");
  Check((bool)matches.Invoke(null,new object[]{curve,new Bezier4x3(curve.d,curve.c,curve.b,curve.a)}),"Reversed path lost its intent");
  foreach(float offset in new[]{.001f,.01f,.039f,.041f,.15f,.3f,1f,3f}){
   var d=new float3(offset,0,0);var other=new Bezier4x3(curve.a+d,curve.b+d,curve.c+d,curve.d+d);
   Check((bool)matches.Invoke(null,new object[]{curve,other})==(offset<.04f),"Parallel path selection leaked");
  }
  var bad=curve;bad.b.x=float.NaN;Check(!(bool)matches.Invoke(null,new object[]{curve,bad}),"Non-finite path matched");
  bad=curve;bad.b.x+=1;Check(!(bool)matches.Invoke(null,new object[]{curve,bad}),"Changed fork control point matched");
  for(int style=0;style<256;style++)Check((bool)valid.Invoke(null,new object[]{(byte)style})==(style>=1&&style<=12||style>=254),"Style contract changed");
  var legacy=M("GranularHighwayLinePolicy","LegacyStyle");
  var center=M("LineShapePolicy","Center");var edge=M("LineShapePolicy","Edge");var divider=M("LineShapePolicy","Divider");
  for(byte style=1;style<=12;style++)foreach(int role in new[]{1,2,3}){
   byte old=(byte)(role==1?center:role==2?edge:divider).Invoke(null,new object[]{style});
   Check((byte)legacy.Invoke(null,new object[]{role,old,true})==style,"Existing saved line style changed during mapping");
  }
  var intent=A.GetType("MedianGrassReplacement.Systems.MGRHighwayLineIntent",true);
  foreach(var field in intent.GetFields())Check(field.FieldType==typeof(float3)||field.FieldType==typeof(byte),"Intent retains a native/entity reference");
  var target=M("RoadSideUpgrades","DecalTarget");var select=M("RoadSideUpgrades","SelectDecals");
  foreach(bool both in new[]{false,true})foreach(bool left in new[]{false,true})foreach(bool remove in new[]{false,true})for(int before=0;before<4;before++){
   byte mask=(byte)target.Invoke(null,new object[]{left,both});byte after=(byte)select.Invoke(null,new object[]{(byte)before,left,remove,both});
   Check(mask==(both?3:left?1:2),"Preview side disagrees with selected side");Check(after==((remove?before&~mask:before|mask)&3),"Decal side update leaked");
  }
  var anchorType=A.GetType("MedianGrassReplacement.Systems.HighwayPaintAnchor",true);
  var listType=typeof(System.Collections.Generic.List<>).MakeGenericType(anchorType);
  var anchors=(System.Collections.IList)Activator.CreateInstance(listType);
  void Add(float3 native,float3 paint,bool yellow){var item=Activator.CreateInstance(anchorType);anchorType.GetField("Native",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item,native);anchorType.GetField("Paint",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item,paint);anchorType.GetField("Yellow",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item,yellow);anchors.Add(item);}
  var join=M("HighwayPaintAnchorPolicy","Join");var yellowRole=M("HighwayPaintAnchorPolicy","Yellow");
  // Captured native segment/node endpoints match exactly. Their automatic
  // paint offsets must meet without changing native identity or endpoint tangents.
  var captured=new Bezier4x3(new float3(-545.1696f,125.0225f,-3204.066f),new float3(-551,125.02f,-3198),new float3(-558,125.02f,-3191),new float3(-563.3069f,125.0166f,-3186.619f));
  var da=new float3(.18f,.01f,.22f);var dd=new float3(-.13f,.01f,.26f);
  Add(captured.a,captured.a+da,true);Add(captured.d,captured.d+dd,true);
  var joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});
  Check(math.distance(joined.a,captured.a+da)<.0001f&&math.distance(joined.d,captured.d+dd)<.0001f,"Captured automatic segment join has a gap");
  Check(math.distance(joined.b-joined.a,captured.b-captured.a)<.001f&&math.distance(joined.d-joined.c,captured.d-captured.c)<.001f,"Joining altered endpoint tangents");
  Check((bool)yellowRole.Invoke(null,new object[]{captured,anchors}),"Traffic-left boundary lost yellow");
  anchors.RemoveAt(1);joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});
  Check(math.distance(joined.d,captured.d)<.0001f,"Unattached gore tip moved");
  Check((bool)yellowRole.Invoke(null,new object[]{captured,anchors}),"Gore tip lost attached yellow role");
  Add(captured.d,captured.d,false);Check(!(bool)yellowRole.Invoke(null,new object[]{captured,anchors}),"Known white outer edge inherited yellow");
  anchors.Clear();Add(captured.a+new float3(.1f,0,0),captured.a+da,true);
  joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});Check((bool)matches.Invoke(null,new object[]{captured,joined}),"Neighboring path endpoint was snapped");
  anchors.Clear();joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});Check((bool)matches.Invoke(null,new object[]{captured,joined}),"Native-only/automatic-off geometry changed");
  Add(captured.a,captured.a+da,true);
  var withTangent=anchors[0];anchorType.GetField("Outward",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(withTangent,new float3(-1,0,1));anchors[0]=withTangent;
  joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});Check(math.dot(math.normalizesafe(joined.b-joined.a),math.normalizesafe(new float3(-1,0,1)))>.999f,"Join did not follow segment tangent");
  Check(math.abs(math.length(joined.b-joined.a)-math.length(captured.b-captured.a))<.001f,"Join changed tangent influence length");
  Add(captured.a,captured.a+dd,false);joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});Check((bool)matches.Invoke(null,new object[]{captured,joined}),"Ambiguous endpoints were snapped");
  anchors.Clear();Add(captured.a,captured.a,true);Add(captured.d,captured.d,true);
  var nativeAnchor=anchors[0];anchorType.GetField("Outward",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(nativeAnchor,new float3(1,0,0));anchors[0]=nativeAnchor;
  joined=(Bezier4x3)join.Invoke(null,new object[]{captured,anchors});Check((bool)matches.Invoke(null,new object[]{captured,joined}),"Vanilla-aligned endpoints caused unnecessary junction reshaping");
  var sectionType=A.GetType("MedianGrassReplacement.Systems.HighwaySectionPath",true);
  var sectionListType=typeof(System.Collections.Generic.List<>).MakeGenericType(sectionType);
  var sectionList=(System.Collections.IList)Activator.CreateInstance(sectionListType);
  var forward=M("HighwaySectionPaintPolicy","Forward");var pairPaths=M("HighwaySectionPaintPolicy","Pairs");
  void Section(Bezier4x3 p,float start,float end,bool left){sectionList.Add(forward.Invoke(null,new object[]{p,start,end,left}));}
  bool Pair(out System.Collections.Generic.List<Bezier4x3> result){var args=new object[]{sectionList,null};bool ok=(bool)pairPaths.Invoke(null,args);result=(System.Collections.Generic.List<Bezier4x3>)args[1];return ok;}
  var halfA=MathUtils.Cut(captured,new float2(0,.5f));var halfB=MathUtils.Cut(captured,new float2(.5f,1));
  Section(new Bezier4x3(halfB.d,halfB.c,halfB.b,halfB.a),1,.5f,true);Section(halfA,0,.5f,true);
  Check(Pair(out var paired)&&paired.Count==2,"Native halves not paired across reversed source order");
  Check((bool)matches.Invoke(null,new object[]{halfA,paired[0]})&&(bool)matches.Invoke(null,new object[]{halfB,paired[1]}),"Manual section plan reshaped native geometry");
  sectionList.Clear();Section(captured,0,1,true);Check(Pair(out paired)&&paired.Count==2,"Whole native section not supported");
  for(int sample=0;sample<=20;sample++){float t=sample/20f;var actual=t<=.5f?MathUtils.Position(paired[0],t*2):MathUtils.Position(paired[1],t*2-1);Check(math.distancesq(actual,MathUtils.Position(captured,t))<.00001f,"Whole section split changed its path");}
  sectionList.Clear();Section(halfA,0,.5f,true);var displaced=halfB;displaced.a.x+=.2f;Section(displaced,.5f,1,true);Check(!Pair(out paired)&&paired.Count==0,"Disconnected native halves were guessed together");
  sectionList.Clear();Section(halfA,0,.5f,true);Section(halfB,.5f,1,false);Check(!Pair(out paired),"Opposite road sides were paired");
  sectionList.Clear();Section(halfA,0,.5f,true);Section(halfB,.5f,1,true);Section(halfB,.5f,1,true);Section(halfB,.5f,1,true);Check(!Pair(out paired),"Ambiguous native joins were accepted");
  sectionList.Clear();Section(halfA,float.NaN,.5f,true);Section(halfB,.5f,1,true);Check(!Pair(out paired),"Invalid native range was accepted");
  var otherBoundary=new Bezier4x3(halfA.a+10,halfA.b+10,halfA.c+10,halfA.d+10);var otherEnd=new Bezier4x3(halfB.a+10,halfB.b+10,halfB.c+10,halfB.d+10);
  sectionList.Clear();Section(otherEnd,.5f,1,false);Section(halfB,.5f,1,true);Section(otherBoundary,0,.5f,false);Section(halfA,0,.5f,true);
  Check(Pair(out paired)&&paired.Count==4&&(bool)matches.Invoke(null,new object[]{halfA,paired[0]})&&(bool)matches.Invoke(null,new object[]{otherBoundary,paired[2]}),"Section side order changed or pair ownership leaked");
  var addEditable=M("HighwaySectionPaintPolicy","AddEditable");
  for(int mirror=-1;mirror<=1;mirror+=2)for(int rotation=0;rotation<8;rotation++)for(int reverse=0;reverse<2;reverse++)
  {
   var editable=new System.Collections.Generic.List<Bezier4x3>();
   float angle=rotation*math.PI/4;
   float3 Transform(float3 v)=>new float3(mirror*v.x*math.cos(angle)-v.z*math.sin(angle),v.y,mirror*v.x*math.sin(angle)+v.z*math.cos(angle))+new float3(100,20,-200);
   for(int piece=0;piece<5;piece++)
   {
    // Odd native piece count and shortened tapers must remain editable without
    // inventing full-width surface-interpolation paths or joining neighboring lanes.
    float x=piece*7;var sampleCurve=new Bezier4x3(Transform(new float3(x,0,0)),Transform(new float3(x+2,0,1)),Transform(new float3(x+4,0,3)),Transform(new float3(x+6,0,4)));
    if(reverse==1)sampleCurve=new Bezier4x3(sampleCurve.d,sampleCurve.c,sampleCurve.b,sampleCurve.a);
    addEditable.Invoke(null,new object[]{editable,sampleCurve});
    Check(editable.Count==piece+1&&editable[piece].a.Equals(sampleCurve.a)&&editable[piece].b.Equals(sampleCurve.b)&&editable[piece].c.Equals(sampleCurve.c)&&editable[piece].d.Equals(sampleCurve.d),"Native piece shape changed");
    addEditable.Invoke(null,new object[]{editable,new Bezier4x3(sampleCurve.d,sampleCurve.c,sampleCurve.b,sampleCurve.a)});
    Check(editable.Count==piece+1,"Reverse duplicate created a phantom selectable path");
   }
   var invalid=editable[0];invalid.b.x=float.NaN;addEditable.Invoke(null,new object[]{editable,invalid});Check(editable.Count==5,"Invalid native piece accepted");
   var bow=editable[0];bow.b+=new float3(0,0,2);addEditable.Invoke(null,new object[]{editable,bow});Check(editable.Count==6,"Different interior sampleCurve with matching endpoints lost");
  }
  Console.WriteLine("PASS: "+count+" line identity/style/native-reference and roadside preview/apply/remove cases. Native save and visual validation pending.");
 }
}
