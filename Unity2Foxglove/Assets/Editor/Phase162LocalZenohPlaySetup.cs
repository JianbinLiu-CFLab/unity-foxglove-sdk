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
    private const double AutoExitAfterSeconds = 60.0;
    private static GameObject motionTarget;
    private static Vector3 motionOrigin;
    private static bool motionOriginCaptured;
    private static double motionStartedAt;

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

        Environment.SetEnvironmentVariable("ROS_DOMAIN_ID", "0");
        Environment.SetEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_zenoh_cpp");
        EditorUserSettings.SetConfigValue(LyricalCommunicationModeKey, "zenoh");

        EditorSceneManager.OpenScene(ScenePath);

        var manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
        if (manager == null)
            throw new InvalidOperationException("Could not find FoxgloveManager in " + ScenePath);

        SetField(manager, "_foxgloveOutputEnabled", false);
        SetField(manager, "_ros2NativeEnabled", true);
        SetField(manager, "_defaultPublisherEncoding", GlobalEncoding.Protobuf);

        var publisher = UnityEngine.Object.FindFirstObjectByType<FoxglovePointCloudPublisher>();
        if (publisher == null)
            throw new InvalidOperationException("Could not find FoxglovePointCloudPublisher in " + ScenePath);

        SetField(publisher, "_outputMode", PointCloudOutputMode.PointCloud2Native);
        SetField(publisher, "_topic", "/unity/point_cloud2");
        SetField(publisher, "_frameId", "os_lidar");
        SetField(publisher, "_publishPointCloud2NativeTfAnchor", true);
        SetField(publisher, "_enableMotionCompensation", true);
        SetField(publisher, "_motionCompensationOutputPolicy", PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic);
        SetField(publisher, "_deskewedPointCloud2NativeTopic", "/unity/point_cloud2_deskewed");
        SetField(publisher, "_motionCompensationReferenceTime", PointCloudMotionCompensationReferenceTime.ScanStart);
        SetField(publisher, "_motionCompensationSource", PointCloudMotionCompensationSource.SensorTransform);

        var controller = UnityEngine.Object.FindFirstObjectByType<Phase138LidarVehicleController>();
        if (controller != null)
        {
            SetField(controller, "_useAutoWander", true);
            motionTarget = controller.gameObject;
            motionOriginCaptured = false;
            motionStartedAt = EditorApplication.timeSinceStartup;
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

        if (motionTarget == null)
            motionTarget = GameObject.Find("Vehicle");
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

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(target.GetType().FullName, name);

        field.SetValue(target, value);
    }
}
