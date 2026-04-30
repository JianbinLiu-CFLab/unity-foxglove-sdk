using System;
using Unity.FoxgloveSDK.Tests;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== FoxgloveSDK Phase 0 Skeleton Validation ===\n");

        try
        {
            SkeletonValidation.Validate();
            Console.WriteLine("\nAll checks passed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex}");
            Environment.Exit(1);
        }
    }
}
