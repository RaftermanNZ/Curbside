using System;
using System.IO;
using System.Threading;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Game;

namespace MedianGrassReplacement.Systems
{
    // Offline candidate: runs before native texture consumption. Asset events
    // only set a flag, so no renderer or tag-list mutation occurs on a watcher
    // thread. Stable updates do one atomic flag check, with no asset scans.
    public partial class SignalMidMipCacheRepairSystem : GameSystemBase
    {
        private static readonly Hash128 CacheId = new Hash128("ef6364463b023d56e90c14805cb8a306");
        private int pending = 1;
        private MidMipCacheAsset knownCache;
        private TextureStreamingSystem knownStreaming;
        private bool hasObservedPair;

        protected override void OnCreate()
        {
            base.OnCreate();
            AssetDatabase.global.onAssetDatabaseChanged.Subscribe(OnAssetChanged);
            // Mod loading precedes settings.Apply/VT initialization in 1.6.4f1.
            // Repair an already registered cache now, before fallback rectangles
            // can be allocated, rather than waiting for the first prefab update.
            Interlocked.Exchange(ref pending, 0);
            RepairKnownCache(initial: true);
        }

        private void OnAssetChanged(AssetChangedEventArgs args)
        {
            if (args.change == ChangeType.BulkAssetsChange ||
                args.change == ChangeType.DatabaseRegistered ||
                args.change == ChangeType.DatabaseUnregistered ||
                (args.asset is MidMipCacheAsset cache && cache.id.guid == CacheId))
                Interlocked.Exchange(ref pending, 1);
        }

        protected override void OnUpdate()
        {
            if (Interlocked.Exchange(ref pending, 0) == 0) return;
            RepairKnownCache(initial: false);
        }

        private void RepairKnownCache(bool initial)
        {
            if (!AssetDatabase.global.TryGetAsset<MidMipCacheAsset>(CacheId, out var cache))
            {
                hasObservedPair = false; knownCache = null; knownStreaming = null;
                return;
            }
            if (!string.Equals(Path.GetFileName(cache.name), SignalMidMipCacheTags.ExpectedTag, StringComparison.Ordinal)) return;

            int removed = SignalMidMipCacheTags.Normalize(cache.tags);
            if (cache.tags == null || cache.tags.Count != 1 || cache.tags[0] != SignalMidMipCacheTags.ExpectedTag)
            {
                if (removed > 0) Mod.Log.Warn("Signal mid-mip cache contains unfamiliar tags; retained them and skipped automatic renderer refresh.");
                return;
            }

            var streaming = World.GetExistingSystemManaged<TextureStreamingSystem>();
            if (hasObservedPair && ReferenceEquals(knownCache, cache) && ReferenceEquals(knownStreaming, streaming)) return;
            if (streaming != null && (!initial || removed > 0))
            {
                // At initial mod load, an unchanged single-tag cache was already
                // selected by native OnCreate (Pdx caching precedes CreateSystems).
                // A repaired or late cache needs one native refresh. Repeated
                // PostCreate on that same object does not remove its existing
                // native grouper; refreshing again would requeue every surface
                // in the database. Native VT reset retains those groupers too.
                streaming.MarkVTAssetsDirty();
                streaming.RefreshVT(cache.database);
                Mod.Log.Info("Restored native shared-cache registration for the signal texture cache.");
            }
            // Commit only after a successful refresh; a native failure must not
            // be mistaken for completed registration. No timer retries are added.
            knownCache = cache; knownStreaming = streaming; hasObservedPair = true;
        }

        protected override void OnDestroy()
        {
            if (AssetDatabase.exists)
                AssetDatabase.global.onAssetDatabaseChanged.Unsubscribe(OnAssetChanged);
            base.OnDestroy();
        }
    }
}
