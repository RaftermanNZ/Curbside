using System;using System.Collections.Generic;
namespace MedianGrassReplacement.Systems
{
    // Managed FIFO ring of IDs only. Repeated edits cannot overtake work.
    internal sealed class ManagedWorkQueue<T>
    {
        private T[] items=new T[16];private int head,count;private readonly HashSet<T> queued=new HashSet<T>();
        internal int Count=>count;
        internal void Add(T item)
        {
            if(!queued.Add(item))return;
            if(count==items.Length){var grown=new T[items.Length*2];for(int i=0;i<count;i++)grown[i]=items[(head+i)%items.Length];items=grown;head=0;}
            items[(head+count)%items.Length]=item;count++;
        }
        internal bool Take(out T item)
        {
            if(count==0){item=default;return false;}
            item=items[head];items[head]=default;head=(head+1)%items.Length;count--;queued.Remove(item);return true;
        }
        internal void Clear(){Array.Clear(items,0,items.Length);queued.Clear();head=0;count=0;}
    }
}
