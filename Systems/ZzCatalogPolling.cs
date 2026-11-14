using Game.Prefabs;
namespace MedianGrassReplacement.Systems
{
    public partial class CurbLineEmissionSystem
    {
        internal void ObserveDefinitionsIfReady() => RefreshDefinitions(false);

        private void RefreshDefinitions(bool required)
        {
            if (!required && !_catalogRequested && _definitionCount >= 0
                && !CatalogQueryReadiness.Ready(_lanePrefabs, _secondaryDefinitions)) return;
            int count = _lanePrefabs.CalculateEntityCount() + _secondaryDefinitions.CalculateEntityCount();

            if (_catalogRequested || count != _definitionCount) { RefreshCatalog(); _definitionCount = count; }

        }


    }
}
