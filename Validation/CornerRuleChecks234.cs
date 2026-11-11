using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Unity.Mathematics;
using Curbside.CornerStudy;
class CornerRuleChecks234
{
 static int checks, cases;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static float3 P(float x,float z)=>new float3(x,0,z);
 static Bezier4x3 Line(float3 a,float3 b)=>new Bezier4x3(a,math.lerp(a,b,1f/3),math.lerp(a,b,2f/3),b);
 static Bezier4x3 Shift(Bezier4x3 c,float3 s)=>new Bezier4x3(c.a+s,c.b+s,c.c+s,c.d+s);
 static float3 Turn(float3 p,int q){for(int i=0;i<q;i++)p=P(-p.z,p.x);return p;}
 static void Corner(List<CurbSlice> all,int q,int roadA,int roadB,float width,BoundaryKind kind)
 {
  // Split one curved corner into two native-style per-road halves.
  var a=P(5,15);var b=P(5,25f/3);var c=P(25f/3,5);var d=P(15,5);
  var ab=(a+b)/2;var bc=(b+c)/2;var cd=(c+d)/2;var abc=(ab+bc)/2;var bcd=(bc+cd)/2;var m=(abc+bcd)/2;
  var left=new Bezier4x3(Turn(a,q),Turn(ab,q),Turn(abc,q),Turn(m,q));
  var right=new Bezier4x3(Turn(d,q),Turn(cd,q),Turn(bcd,q),Turn(m,q));
  var shift=Turn(P(width,width),q);
  all.Add(new CurbSlice{Road=roadA,Side=0,Kind=kind,Curb=left,Back=Shift(left,shift),HasBack=true,Outside=Turn(P(1,0),q)});
  all.Add(new CurbSlice{Road=roadB,Side=1,Kind=kind,Curb=right,Back=Shift(right,shift),HasBack=true,Outside=Turn(P(0,1),q)});
 }
 static List<CurbSlice> Four(float width,BoundaryKind kind){var a=new List<CurbSlice>();for(int q=0;q<4;q++)Corner(a,q,q,(q+1)%4,width,kind);return a;}
 static List<CurbSlice> Tee(float width,BoundaryKind kind)
 {
  var a=new List<CurbSlice>();Corner(a,2,2,0,width,kind);Corner(a,3,1,2,width,kind);
  var l=Line(P(-15,5),P(0,5));var r=Line(P(15,5),P(0,5));
  a.Add(new CurbSlice{Road=0,Side=0,Kind=kind,Curb=l,Back=Shift(l,P(0,width)),HasBack=true,Outside=P(0,1)});
  a.Add(new CurbSlice{Road=1,Side=1,Kind=kind,Curb=r,Back=Shift(r,P(0,width)),HasBack=true,Outside=P(0,1)});return a;
 }
 static List<CurbSlice> Radial(int roads,int pass,BoundaryKind kind)
 {
  var result=new List<CurbSlice>();float radius=10+pass*.5f;
  for(int i=0;i<roads;i++){
   float step=2*math.PI/roads,mid=(i+.5f)*step,half=step*.3f;
   Func<float,float3> point=t=>P(math.cos(t),math.sin(t));
   foreach(int side in new[]{-1,1}){
    float start=mid+side*half,finish=mid,k=4f/3*math.tan((finish-start)/4);
    var a=point(start)*radius;var d=point(finish)*radius;
    var b=a+P(-math.sin(start),math.cos(start))*radius*k;var c=d-P(-math.sin(finish),math.cos(finish))*radius*k;
    var curb=new Bezier4x3(a,b,c,d);float scale=(radius+3)/radius;
    result.Add(new CurbSlice{Road=side<0?i:(i+1)%roads,Side=side<0?1:0,Kind=kind,Curb=curb,Back=new Bezier4x3(a*scale,b*scale,c*scale,d*scale),HasBack=true,Outside=point(start)});
   }
  }return result;
 }
 static void Main()
 {
  for(int pass=0;pass<20;pass++)foreach(var kind in new[]{BoundaryKind.Curb,BoundaryKind.TrafficLaneEdge})
  {
   foreach(bool t in new[]{false,true})
   {
    var input=t?Tee(2.5f+pass*.07f,kind):Four(2.5f+pass*.07f,kind);
    if(!t&&pass>0){float skew=pass*.012f;Func<float3,float3> shear=p=>P(p.x+skew*p.z,p.z);for(int i=0;i<input.Count;i++){var s=input[i];s.Curb=new Bezier4x3(shear(s.Curb.a),shear(s.Curb.b),shear(s.Curb.c),shear(s.Curb.d));s.Back=new Bezier4x3(shear(s.Back.a),shear(s.Back.b),shear(s.Back.c),shear(s.Back.d));s.Outside=shear(s.Outside);input[i]=s;}}
    var result=CornerRule.Calculate(input,.724f);cases++;
    Check(result.Count==4,"corner count "+t);
    foreach(var c in result){Check(c.Fits,c.Reason+" T="+t);Check(math.abs(math.distance(c.Position,c.CurbPoint)-1f)<.01f,"setback");}
    if(t){var top=result.Where(p=>p.Position.z>0).OrderBy(p=>p.Position.x).ToArray();var bottom=result.Where(p=>p.Position.z<0).OrderBy(p=>p.Position.x).ToArray();Check(top.Length==2&&bottom.Length==2,"T counts");Check(math.abs(top[0].Position.x-bottom[0].Position.x)<.001f&&math.abs(top[1].Position.x-bottom[1].Position.x)<.001f,"T perpendicular");}
    var walk=new[]{Line(result[0].Position-P(0,2),result[0].Position+P(0,2))};
    Check(CornerRule.Calculate(input,.724f,walk)[0].Fits,"route centreline incorrectly vetoed physical corner fit");
    var missing=input.Select(s=>{s.HasBack=false;return s;}).ToArray();Check(CornerRule.Calculate(missing,.724f).TrueForAll(p=>!p.Fits),"missing boundary");
   }
   Check(CornerRule.Calculate(Four(.5f,kind),.724f).TrueForAll(p=>!p.Fits),"narrow supporting surface");
   Check(CornerRule.DrivingEdges(new[]{new float2(-1.75f,3.5f),new float2(1.75f,3.5f)},13,out var low,out var high)&&low==-3.5f&&high==3.5f,"shoulder reference");
   Check(!CornerRule.DrivingEdges(new[]{new float2(0,20)},13,out low,out high),"invalid driving edge");
   foreach(int roads in new[]{3,5,6}){var points=CornerRule.Calculate(Radial(roads,pass,kind),.724f);cases++;Check(points.Count==roads,"radial corner count");foreach(var p in points){Check(p.Fits,"radial "+roads+": "+p.Reason);Check(math.abs(math.distance(p.Position,p.CurbPoint)-1f)<.01f,"radial setback");}}
  }
  Check(CornerRule.Calculate(null,.724f).Count==0,"null");Check(CornerRule.Calculate(Four(3,BoundaryKind.Curb),float.NaN).Count==0,"invalid envelope");
  Console.WriteLine("PASS: "+checks+" assertions, "+cases+" constructed four-way/T cases; curbs and shoulders. Not native World execution.");
 }
}
