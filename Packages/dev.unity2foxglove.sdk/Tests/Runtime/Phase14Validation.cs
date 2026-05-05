using System;
using System.Text;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase14Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 14 Tests ---");
            _passCount = 0;
            TestFoxRunAttribute();
            TestFoxRunAttributeTopic();
            TestFoxRunAttributeRateHz();
            TestFoxRunAttributeSchemaName();
            TestFoxRunAllowMultiple();
            Console.WriteLine($"Phase 14: {_passCount} checks passed.");
        }

        // ── [FoxRun] Attribute ──

        static void TestFoxRunAttribute()
        {
            var attr = new Components.FoxRunAttribute("/test/topic");
            Assert(attr.Topic == "/test/topic", "Attribute Topic stored correctly");
            Assert(attr.RateHz == 10f, "Default RateHz is 10");
            Assert(attr.SchemaName == null, "Default SchemaName is null");
        }

        static void TestFoxRunAttributeTopic()
        {
            try
            {
                new Components.FoxRunAttribute(null);
                throw new Exception("Expected ArgumentNullException");
            }
            catch (ArgumentNullException)
            {
                Assert(true, "Null topic throws ArgumentNullException");
            }
        }

        static void TestFoxRunAttributeRateHz()
        {
            var attr = new Components.FoxRunAttribute("/t") { RateHz = 30f };
            Assert(attr.RateHz == 30f, "Custom RateHz stored");
        }

        static void TestFoxRunAttributeSchemaName()
        {
            var attr = new Components.FoxRunAttribute("/t") { SchemaName = "foxglove.Point3" };
            Assert(attr.SchemaName == "foxglove.Point3", "Custom SchemaName stored");
        }

        static void TestFoxRunAllowMultiple()
        {
            var attrs = typeof(Components.FoxRunAttribute).GetCustomAttributes(
                typeof(AttributeUsageAttribute), false);
            Assert(attrs.Length > 0, "AttributeUsage defined on FoxRunAttribute");
            var usage = (AttributeUsageAttribute)attrs[0];
            Assert(usage.AllowMultiple, "AllowMultiple is true");
            Assert((usage.ValidOn & (AttributeTargets.Field | AttributeTargets.Property)) != 0,
                "ValidOn includes Field and Property");
        }
    }
}
