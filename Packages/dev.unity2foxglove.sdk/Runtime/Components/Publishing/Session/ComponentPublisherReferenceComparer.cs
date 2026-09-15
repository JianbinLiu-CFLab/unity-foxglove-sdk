using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ComponentPublisherReferenceComparer Instance = new ComponentPublisherReferenceComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
