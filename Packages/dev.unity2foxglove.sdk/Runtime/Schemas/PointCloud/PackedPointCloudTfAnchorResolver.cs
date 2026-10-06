namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    public readonly struct PackedPointCloudTfAnchorPose
    {
        public PackedPointCloudTfAnchorPose(
            float translationX,
            float translationY,
            float translationZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW)
        {
            TranslationX = translationX;
            TranslationY = translationY;
            TranslationZ = translationZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
        }

        public float TranslationX { get; }
        public float TranslationY { get; }
        public float TranslationZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }
    }

    public static class PackedPointCloudTfAnchorResolver
    {
        public static PackedPointCloudTfAnchorPose Resolve(
            float positionX,
            float positionY,
            float positionZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW,
            float offsetX,
            float offsetY,
            float offsetZ,
            float offsetRotationX,
            float offsetRotationY,
            float offsetRotationZ,
            float offsetRotationW)
        {
            var convertedX = positionZ + offsetX;
            var convertedY = -positionX + offsetY;
            var convertedZ = positionY + offsetZ;

            var sourceX = -rotationZ;
            var sourceY = rotationX;
            var sourceZ = -rotationY;
            var sourceW = rotationW;

            var resultX = sourceW * offsetRotationX + sourceX * offsetRotationW
                          + sourceY * offsetRotationZ - sourceZ * offsetRotationY;
            var resultY = sourceW * offsetRotationY - sourceX * offsetRotationZ
                          + sourceY * offsetRotationW + sourceZ * offsetRotationX;
            var resultZ = sourceW * offsetRotationZ + sourceX * offsetRotationY
                          - sourceY * offsetRotationX + sourceZ * offsetRotationW;
            var resultW = sourceW * offsetRotationW - sourceX * offsetRotationX
                          - sourceY * offsetRotationY - sourceZ * offsetRotationZ;

            return new PackedPointCloudTfAnchorPose(
                convertedX,
                convertedY,
                convertedZ,
                resultX,
                resultY,
                resultZ,
                resultW);
        }
    }
}
