using System;
namespace MedianGrassReplacement.Systems
{
    // Pure managed coalescing/retry state; never blocks the simulation thread.
    internal sealed class DeferredSettingsWrite<T> where T:class
    {
        internal T Pending{get;private set;}
        internal bool Saving{get;private set;}
        internal int Failures{get;private set;}
        internal double Due{get;private set;}
        private T active;private long revision,writingRevision;
        private bool force;
        internal void Request(T value,double now){Pending=value;revision++;Failures=0;Due=now+1;}
        internal void Force(double now){if(Pending==null&&!Saving)return;force=true;Due=now;}
        internal T Begin(double now)
        {
            if(Saving||Pending==null||(!force&&(Failures>=3||now<Due)))return null;
            active=Pending;Pending=null;writingRevision=revision;Saving=true;force=false;return active;
        }
        internal void Complete(bool success,double now)
        {
            if(!Saving)return;
            if(!success&&writingRevision==revision)
            {
                if(Pending==null)Pending=active;
                Failures++;Due=now+Math.Min(8,Math.Pow(2,Failures));
            }
            else if(success&&writingRevision==revision)Failures=0;
            active=null;Saving=false;
            // A force request made during a write applies to a queued newer value.
            if(force)Due=now;
        }
    }
}
