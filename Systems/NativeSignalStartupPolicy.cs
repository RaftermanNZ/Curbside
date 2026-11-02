namespace GardenCitySignals
{
 internal static class NativeSignalStartupPolicy
 {
  // Partial, verified native packs receive progress-aware grace. An absent
  // pack still uses the existing 30-second boot probe and no idle scans.
  internal static bool Expired(double now,double start,double progress)
   =>now-start>=120||(now-start>=30&&now-progress>=30);
 }
}
