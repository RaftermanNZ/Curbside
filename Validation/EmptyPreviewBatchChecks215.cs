using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Unity.Entities;

class EmptyPreviewBatchChecks215
{
    static int checks;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        var assembly=Assembly.LoadFrom("MedianGrassReplacement.dll");
        var specs=new Dictionary<string,string[]>{
            {"SignalPreviewSystem",new[]{"saved","writes"}},
            {"PreviewPaintInheritanceSystem",new[]{"aliases","entries","writes","retired","retainedOriginals"}},
            {"CrosswalkAppearancePreviewSystem",new[]{"aliases","writes"}},
            {"DecalHoverPreviewSystem",new[]{"bindings"}}
        };
        var states=new List<object>();var getters=new List<PropertyInfo>();
        foreach(var spec in specs)
        {
            var type=assembly.GetType("MedianGrassReplacement.Systems."+spec.Key,true);
            // CLR stubs only: no World, OnCreate, EntityManager or native system
            // update is invoked. The tested properties read managed counts.
            var system=FormatterServices.GetUninitializedObject(type);
            var getter=type.GetProperty("HasRefreshWork",Flags);
            void Empty(){foreach(var field in spec.Value){var f=type.GetField(field,Flags);f.SetValue(system,Activator.CreateInstance(f.FieldType));}}
            Empty();Check(!(bool)getter.GetValue(system),spec.Key+" empty state still takes native scope snapshots");
            foreach(var field in spec.Value)
            {
                Empty();var f=type.GetField(field,Flags);object value=f.GetValue(system);
                if(value is IDictionary dictionary)
                {
                    var args=f.FieldType.GetGenericArguments();
                    var item=args[1]==typeof(Entity)?(object)new Entity{Index=9,Version=3}:Activator.CreateInstance(args[1],true);
                    dictionary.Add(new Entity{Index=7,Version=2},item);
                }
                else if(value is ICollection<Entity> collection)collection.Add(new Entity{Index=7,Version=2});
                else throw new Exception("Unreviewed restore collection: "+field);
                Check((bool)getter.GetValue(system),spec.Key+" skipped pending "+field);
                Empty();Check(!(bool)getter.GetValue(system),spec.Key+" stale work gate survived clearing "+field);
            }
            states.Add(system);getters.Add(getter);Empty();
        }
        for(int mask=0;mask<16;mask++)
        {
            // Any family with real state must retain the full native refresh
            // path. Only a completely empty Curbside state may avoid it.
            bool any=false;
            for(int n=0;n<states.Count;n++)
            {
                var field=states[n].GetType().GetField(new[]{"saved","aliases","aliases","bindings"}[n],Flags);
                var dictionary=(IDictionary)field.GetValue(states[n]);dictionary.Clear();
                if((mask&(1<<n))!=0)
                {
                    var valueType=field.FieldType.GetGenericArguments()[1];
                    dictionary.Add(new Entity{Index=77,Version=9},valueType==typeof(Entity)?(object)new Entity{Index=90,Version=2}:Activator.CreateInstance(valueType,true));
                }
                any|=(bool)getters[n].GetValue(states[n]);
            }
            Check(any==(mask!=0),"mixed-family refresh work lost");
        }
        Console.WriteLine("PASS: "+checks+" empty-batch and unfinished-work cases; pending aliases, copied actors, retirements, retained claims, identity writes and decals retain the refresh path. CLR count getters only; native query costs/FPS not measured.");
    }
}
