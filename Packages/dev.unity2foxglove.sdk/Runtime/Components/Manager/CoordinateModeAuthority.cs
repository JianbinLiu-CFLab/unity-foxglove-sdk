using System;

namespace Unity.FoxgloveSDK.Components
{
    public static class CoordinateModeAuthority
    {
        public const string OutputFieldName = "_outputCoordinateMode";
        public const string InputFieldName = "_inputCoordinateMode";

        public static void Apply<T>(Action<string, T> setter, T mode)
        {
            if (setter == null)
                throw new ArgumentNullException(nameof(setter));

            setter(OutputFieldName, mode);
            setter(InputFieldName, mode);
        }
    }
}
