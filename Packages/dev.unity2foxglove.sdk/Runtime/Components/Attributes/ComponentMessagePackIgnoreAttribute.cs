using System;

namespace Unity.FoxgloveSDK.Components
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ComponentMessagePackIgnoreAttribute : Attribute { }
}
