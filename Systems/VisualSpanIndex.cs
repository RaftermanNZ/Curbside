using System.Collections.Generic;

namespace MedianGrassReplacement.Systems

{

    // Runtime-only reverse dependencies; removing a pole can find every span

    // without scanning all upgraded roads or all native entities.

    internal sealed class VisualSpanIndex<T>

    {

        private struct Link { internal T A,B,Owner; }

        private readonly Dictionary<T,Link> _links=new Dictionary<T,Link>();

        private readonly Dictionary<T,HashSet<T>> _poles=new Dictionary<T,HashSet<T>>();

        internal void Add(T span,T a,T b,T owner)

        {

            Remove(span);_links[span]=new Link {A=a,B=b,Owner=owner};Attach(a,span);Attach(b,span);

        }

        private void Attach(T pole,T span) {if(!_poles.TryGetValue(pole,out var set)) _poles[pole]=set=new HashSet<T>();set.Add(span);}

        internal bool Owner(T span,out T owner) {if(_links.TryGetValue(span,out var link)) {owner=link.Owner;return true;}owner=default(T);return false;}

        internal bool Other(T span,T pole,out T other)

        {

            if(_links.TryGetValue(span,out var link)) {other=EqualityComparer<T>.Default.Equals(link.A,pole)?link.B:link.A;return true;}other=default(T);return false;

        }

        internal List<T> At(T pole) => _poles.TryGetValue(pole,out var set)?new List<T>(set):new List<T>();

        internal void Remove(T span)

        {

            if(!_links.TryGetValue(span,out var link)) return;_links.Remove(span);Detach(link.A,span);Detach(link.B,span);

        }

        private void Detach(T pole,T span) {if(_poles.TryGetValue(pole,out var set)) {set.Remove(span);if(set.Count==0) _poles.Remove(pole);}}

        internal void Clear() {_links.Clear();_poles.Clear();}

    }

}

