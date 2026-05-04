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
            TestFoxgloveLogAttribute();
            TestFoxgloveLogAttributeTopic();
            TestFoxgloveLogAttributeRateHz();
            TestFoxgloveLogAttributeSchemaName();
            TestFoxgloveLogAllowMultiple();
            Console.WriteLine($"Phase 14: {_passCount} checks passed.");
        }

        // ── [FoxgloveLog] Attribute ──

        static void TestFoxgloveLogAttribute()
        {
            var attr = new Components.FoxgloveLogAttribute("/test/topic");
            Assert(attr.Topic == "/test/topic", "Attribute Topic stored correctly");
            Assert(attr.RateHz == 10f, "Default RateHz is 10");
            Assert(attr.SchemaName == null, "Default SchemaName is null");
        }

        static void TestFoxgloveLogAttributeTopic()
        {
            try
            {
                new Components.FoxgloveLogAttribute(null);
                throw new Exception("Expected ArgumentNullException");
            }
            catch (ArgumentNullException)
            {
                Assert(true, "Null topic throws ArgumentNullException");
            }
        }

        static void TestFoxgloveLogAttributeRateHz()
        {
            var attr = new Components.FoxgloveLogAttribute("/t") { RateHz = 30f };
            Assert(attr.RateHz == 30f, "Custom RateHz stored");
        }

        static void TestFoxgloveLogAttributeSchemaName()
        {
            var attr = new Components.FoxgloveLogAttribute("/t") { SchemaName = "foxglove.Point3" };
            Assert(attr.SchemaName == "foxglove.Point3", "Custom SchemaName stored");
        }

        static void TestFoxgloveLogAllowMultiple()
        {
            var attrs = typeof(Components.FoxgloveLogAttribute).GetCustomAttributes(
                typeof(AttributeUsageAttribute), false);
            Assert(attrs.Length > 0, "AttributeUsage defined on FoxgloveLogAttribute");
            var usage = (AttributeUsageAttribute)attrs[0];
            Assert(usage.AllowMultiple, "AllowMultiple is true");
            Assert((usage.ValidOn & (AttributeTargets.Field | AttributeTargets.Property)) != 0,
                "ValidOn includes Field and Property");
        }
    }
}
