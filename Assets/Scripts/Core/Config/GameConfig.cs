using UnityEngine;

namespace MobileDemo.Core.Config
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "MobileDemo/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] int startingLives = 20;
        [SerializeField] int startingCurrency = 100;
        [SerializeField] int enemyPoolPrewarm = 64;
        [SerializeField] int projectilePoolPrewarm = 128;
        [SerializeField] float towerScanIntervalSec = 0.1f;
        [SerializeField] float sellRefundFraction = 0.5f;
        [SerializeField] float buildRoadClearance = 0.9f;
        [SerializeField] float buildTowerSpacing = 0.7f;

        public int TargetFrameRate => targetFrameRate;

        public int StartingLives => startingLives;

        public int StartingCurrency => startingCurrency;

        // Supplied to ObjectPool<T>'s constructor by Bootstrap. The pool still knows nothing about
        // this asset, which is what keeps it testable with no asset and no scene.
        public int EnemyPoolPrewarm => enemyPoolPrewarm;

        // Per projectile *prefab*, not per projectile type: the pools are keyed by prefab, and
        // since every type shares one Projectile.prefab that is one pool this number sizes on its
        // own. A type that ever needs its own prefab gets a second pool of this size. See
        // ProjectileFactory.
        public int ProjectilePoolPrewarm => projectilePoolPrewarm;

        // 10 Hz. Every tower re-scans on this interval rather than every frame -- the polling
        // decision in ARCHITECTURE.md §9, and the number that makes it 6x cheaper.
        public float TowerScanIntervalSec => towerScanIntervalSec;

        // Clamped, unlike every other field here, because a fraction above 1 is a money printer
        // rather than merely mistuned. object-pool.md's stance applies: a bad tuning number should
        // be recoverable where a missing dependency is not.
        //
        // A run rule, not a per-tower one -- the same category as StartingLives, and the same
        // plausible wrong guess. Had the refund been a per-type salvage stat it would belong on
        // TowerDefinition and SellTowerCommand would read it from there.
        public float SellRefundFraction => Mathf.Clamp01(sellRefundFraction);

        // How far a tower must sit from the road. Not a new number: this is the RoadClearance the
        // slice-two authoring script placed the existing towers with, promoted from a const in a
        // deleted one-shot to the asset the runtime rule reads. Read by PlacementRules.
        public float BuildRoadClearance => buildRoadClearance;

        // Doubles as the tap radius for selecting a tower to sell, so the radius that blocks a
        // placement is the radius that selects one -- one number, and the two branches of a tap
        // can never both be true.
        public float BuildTowerSpacing => buildTowerSpacing;
    }
}
