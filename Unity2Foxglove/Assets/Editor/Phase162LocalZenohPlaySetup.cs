// Local editor helper for Phase162 Lyrical Zenoh RViz acceptance.

using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Samples.LidarMaze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Phase162LocalZenohPlaySetup
{
    private const string ScenePath = "Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity";
    private const string LyricalCommunicationModeKey = "Unity2Foxglove.R2FU.LyricalCommunicationMode";
    private const string PlayRequestedKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PlayRequested";
    private const string AutoExitKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.AutoExit";
    private const string EnvironmentCapturedKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.EnvironmentCaptured";
    private const string PreviousRosDomainIdKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousRosDomainId";
    private const string PreviousRosDomainIdWasSetKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousRosDomainIdWasSet";
    private const string PreviousRmwImplementationKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousRmwImplementation";
    private const string PreviousRmwImplementationWasSetKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousRmwImplementationWasSet";
    private const string PreviousCommunicationModeKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousCommunicationMode";
    private const string PreviousCommunicationModeWasSetKey = "Unity2Foxglove.Phase162LocalZenohPlaySetup.PreviousCommunicationModeWasSet";
    private const double AutoExitAfterSeconds = 60.0;
    private const double MotionTargetSearchIntervalSeconds = 0.5;
    private static GameObject motionTarget;
    private static Vector3 motionOrigin;
    private static bool motionOriginCaptured;
    private static double motionStartedAt;
    private static double nextMotionTargetSearchAt;

    [InitializeOnLoadMethod]
    private static void AutoConfigureFromCommandLine()
    {
        var commandLine = string.Join(" ", Environment.GetCommandLineArgs());
        if (!commandLine.Contains("Phase162LocalZenohPlaySetup.ConfigureAndPlay", StringComparison.Ordinal))
            return;

        SessionState.SetBool(AutoExitKey, true);
        if (SessionState.GetBool(PlayRequestedKey, false))
        {
            EditorApplication.update -= DriveVehicleDuringPlay;
            EditorApplication.update += DriveVehicleDuringPlay;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        ConfigureAndPlay();
    }

    public static void ConfigureAndPlay()
    {
        if (SessionState.GetBool(PlayRequestedKey, false))
        {
            Debug.Log("[Phase162LocalZenohPlaySetup] Play was already requested in this Editor session; skipping duplicate configure.");
            return;
        }

        EditorApplication.delayCall += TryConfigureAndPlay;
    }

    private static void TryConfigureAndPlay()
    {
        if (SessionState.GetBool(PlayRequestedKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryConfigureAndPlay;
            return;
        }

        CaptureEnvironmentBeforeOverride();
        ApplyZenohEnvironmentOverride();

        EditorSceneManager.OpenScene(ScenePath);

        var manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
        if (manager == null)
            throw new InvalidOperationException("Could not find FoxgloveManager in " + ScenePath);

        SetField(manager, "_foxgloveOutputEnabled", false, "configure FoxgloveManager output mode");
        SetField(manager, "_ros2NativeEnabled", true, "configure FoxgloveManager output mode");
        SetField(manager, "_defaultPublisherEncoding", GlobalEncoding.Protobuf, "configure FoxgloveManager publisher encoding");

        var publisher = UnityEngine.Object.FindFirstObjectByType<FoxglovePointCloudPublisher>();
        if (publisher == null)
            throw new InvalidOperationException("Could not find FoxglovePointCloudPublisher in " + ScenePath);

        SetField(publisher, "_outputMode", PointCloudOutputMode.PointCloud2Native, "configure PointCloud2 Native output");
        SetField(publisher, "_topic", "/unity/point_cloud2", "configure raw PointCloud2 topic");
        SetField(publisher, "_frameId", "os_lidar", "configure PointCloud2 frame id");
        SetField(publisher, "_publishPointCloud2NativeTfAnchor", true, "configure PointCloud2 TF anchor");
        SetField(publisher, "_enableMotionCompensation", true, "configure point cloud deskew");
        SetField(publisher, "_motionCompensationOutputPolicy", PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic, "configure point cloud deskew output policy");
        SetField(publisher, "_deskewedPointCloud2NativeTopic", "/unity/point_cloud2_deskewed", "configure deskewed PointCloud2 topic");
        SetField(publisher, "_motionCompensationReferenceTime", PointCloudMotionCompensationReferenceTime.ScanStart, "configure deskew reference time");
        SetField(publisher, "_motionCompensationSource", PointCloudMotionCompensationSource.SensorTransform, "configure deskew motion source");

        var controller = UnityEngine.Object.FindFirstObjectByType<Phase138LidarVehicleController>();
        if (controller != null)
        {
            SetField(controller, "_useAutoWander", true, "enable Phase162 vehicle motion");
            motionTarget = controller.gameObject;
            motionOriginCaptured = false;
            motionStartedAt = EditorApplication.timeSinceStartup;
            nextMotionTargetSearchAt = 0.0;
            EditorApplication.update -= DriveVehicleDuringPlay;
            EditorApplication.update += DriveVehicleDuringPlay;
        }

        SessionState.SetBool(PlayRequestedKey, true);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Debug.Log("[Phase162LocalZenohPlaySetup] Configured Lyrical Zenoh PointCloud2 Native RViz acceptance scene.");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode && state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.update -= DriveVehicleDuringPlay;
        motionTarget = null;
        motionOriginCaptured = false;
        nextMotionTargetSearchAt = 0.0;
        RestoreEnvironmentAfterOverride();
    }

    private static void CaptureEnvironmentBeforeOverride()
    {
        if (SessionState.GetBool(EnvironmentCapturedKey, false))
            return;

        CaptureEnvironmentVariable("ROS_DOMAIN_ID", PreviousRosDomainIdKey, PreviousRosDomainIdWasSetKey);
        CaptureEnvironmentVariable("RMW_IMPLEMENTATION", PreviousRmwImplementationKey, PreviousRmwImplementationWasSetKey);

        var communicationMode = EditorUserSettings.GetConfigValue(LyricalCommunicationModeKey);
        SessionState.SetBool(PreviousCommunicationModeWasSetKey, !string.IsNullOrEmpty(communicationMode));
        SessionState.SetString(PreviousCommunicationModeKey, communicationMode ?? string.Empty);
        SessionState.SetBool(EnvironmentCapturedKey, true);
    }

    private static void CaptureEnvironmentVariable(string variableName, string valueKey, string wasSetKey)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        SessionState.SetBool(wasSetKey, value != null);
        SessionState.SetString(valueKey, value ?? string.Empty);
    }

    private static void ApplyZenohEnvironmentOverride()
    {
        Environment.SetEnvironmentVariable("ROS_DOMAIN_ID", "0");
        Environment.SetEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_zenoh_cpp");
        EditorUserSettings.SetConfigValue(LyricalCommunicationModeKey, "zenoh");
    }

    private static void RestoreEnvironmentAfterOverride()
    {
        if (!SessionState.GetBool(EnvironmentCapturedKey, false))
            return;

        RestoreEnvironmentVariable("ROS_DOMAIN_ID", PreviousRosDomainIdKey, PreviousRosDomainIdWasSetKey);
        RestoreEnvironmentVariable("RMW_IMPLEMENTATION", PreviousRmwImplementationKey, PreviousRmwImplementationWasSetKey);

        var previousMode = SessionState.GetString(PreviousCommunicationModeKey, string.Empty);
        EditorUserSettings.SetConfigValue(
            LyricalCommunicationModeKey,
            SessionState.GetBool(PreviousCommunicationModeWasSetKey, false) ? previousMode : string.Empty);

        SessionState.SetBool(EnvironmentCapturedKey, false);
    }

    private static void RestoreEnvironmentVariable(string variableName, string valueKey, string wasSetKey)
    {
        Environment.SetEnvironmentVariable(
            variableName,
            SessionState.GetBool(wasSetKey, false)
                ? SessionState.GetString(valueKey, string.Empty)
                : null);
    }

    private static void DriveVehicleDuringPlay()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (motionStartedAt <= 0.0)
            motionStartedAt = EditorApplication.timeSinceStartup;

        if (SessionState.GetBool(AutoExitKey, false)
            && EditorApplication.timeSinceStartup - motionStartedAt > AutoExitAfterSeconds)
        {
            SessionState.SetBool(AutoExitKey, false);
            Debug.Log("[Phase162LocalZenohPlaySetup] Auto-exiting Play Mode after Lyrical Zenoh smoke window.");
            EditorApplication.ExitPlaymode();
            return;
        }

        if (motionTarget == null && EditorApplication.timeSinceStartup >= nextMotionTargetSearchAt)
        {
            nextMotionTargetSearchAt = EditorApplication.timeSinceStartup + MotionTargetSearchIntervalSeconds;
            motionTarget = GameObject.Find("Vehicle");
        }
        if (motionTarget == null)
            return;

        if (!motionOriginCaptured)
        {
            motionOrigin = motionTarget.transform.position;
            motionOriginCaptured = true;
            motionStartedAt = EditorApplication.timeSinceStartup;
        }

        var elapsed = (float)(EditorApplication.timeSinceStartup - motionStartedAt);
        var offset = new Vector3(
            Mathf.Sin(elapsed * 0.8f) * 1.5f,
            0f,
            Mathf.Cos(elapsed * 0.5f) * 0.6f);
        var nextPosition = motionOrigin + offset;
        motionTarget.transform.position = nextPosition;
        motionTarget.transform.rotation = Quaternion.Euler(0f, elapsed * 25f, 0f);

        var rb = motionTarget.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = nextPosition;
            rb.rotation = motionTarget.transform.rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private static void SetField(object target, string name, object value, string context)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(
                target.GetType().FullName,
                name + " while trying to " + context + " for Phase162 Lyrical Zenoh acceptance");
        }

        field.SetValue(target, value);
    }
}
