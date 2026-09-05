using System.Reflection;
using NUnit.Framework;

namespace MobileDemo.Tests.EditMode
{
    // Tower, TowerDefinition and Projectile carry their tuning in [SerializeField] private
    // fields, which is what makes them authorable in the Inspector -- and unreachable from a
    // test. The alternatives were both worse than reflection: internal setters would be a seam
    // in production code that only tests use, and SerializedObject would drag UnityEditor and a
    // serialization round-trip into fixtures that otherwise need neither.
    //
    // ARCHITECTURE.md §14 gives EnemyPath as the case where a test was *declined* rather than
    // buy this seam. The difference is what the reflection reaches: EnemyPath needed it to
    // exercise ~15 lines of bake-and-index, where these fixtures need it only to build the
    // fixture, and then test real behaviour through public methods.
    static class SerializedFields
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static T Set<T>(T target, string fieldName, object value) where T : class
        {
            FieldInfo field = target.GetType().GetField(fieldName, Flags);

            // Asserted rather than silently skipped: a renamed field would otherwise turn every
            // test using it into a green test of default values.
            Assert.IsNotNull(field, $"'{target.GetType().Name}' has no private field '{fieldName}'");
            field.SetValue(target, value);
            return target;
        }
    }
}
