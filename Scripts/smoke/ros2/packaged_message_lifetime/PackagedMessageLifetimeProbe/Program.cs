// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Exercise packaged ros2cs message construction and disposal on real runtime threads.

using System.Diagnostics;
using System.Reflection;
using RosImu = sensor_msgs.msg.Imu;
using RosJoy = sensor_msgs.msg.Joy;
using RosPose = geometry_msgs.msg.Pose;
using RosPoseArray = geometry_msgs.msg.PoseArray;
using RosString = std_msgs.msg.String;
using RosTwist = geometry_msgs.msg.Twist;

internal static class Program
{
    private static int Main(string[] args)
    {
        var distro = args.Length > 0 ? args[0] : "unknown";
        var iterations = args.Length > 1 ? int.Parse(args[1]) : 128;

        try
        {
            Console.WriteLine("PROBE distro=" + distro + " pid=" + Environment.ProcessId + " iterations=" + iterations);
            VerifyConstructorDefaultsAndNullSemantics();
            VerifyExecutorCallbackThread(distro);
            ProbeType<RosString>(iterations);
            ProbeType<RosTwist>(iterations);
            ProbeType<RosJoy>(iterations);
            ProbeType<RosImu>(iterations);
            VerifyDirectNestedCascade();
            VerifyAssignedMessageSequenceCascade();
            Console.WriteLine("RESULT distro=" + distro + " status=PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RESULT distro=" + distro + " status=FAIL");
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void VerifyExecutorCallbackThread(string distro)
    {
        ROS2.INode node = null;
        ROS2.Subscription<RosString> subscription = null;
        ROS2.Publisher<RosString> publisher = null;
        var mainOwned = new List<CallbackOwnedCopy>();
        RosString rawCallbackMessage = null;
        Exception executorFailure = null;
        var executorThreadId = 0;
        using var executorStarted = new ManualResetEventSlim(false);
        using var callbackCompleted = new ManualResetEventSlim(false);

        try
        {
            ROS2.Ros2cs.Init();
            node = ROS2.Ros2cs.CreateNode("packaged_message_lifetime_" + distro + "_" + Environment.ProcessId);
            var topic = "/r2fu/packaged_message_lifetime/" + distro + "/" + Environment.ProcessId;
            using var qos = new ROS2.QualityOfServiceProfile(ROS2.QosPresetProfile.DEFAULT);
            subscription = node.CreateSubscription<RosString>(topic, message =>
            {
                try
                {
                    Check(Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref executorThreadId),
                        "Subscription callback did not run on the SpinOnce executor thread.");
                    Check(IsInRos2SpinCallback(), "Ros2cs did not mark the subscription as an active spin callback.");
                    rawCallbackMessage = message;

                    CreateCallbackOwnedCopy<RosString>(mainOwned);
                    CreateCallbackOwnedCopy<RosTwist>(mainOwned);
                    CreateCallbackOwnedCopy<RosJoy>(mainOwned);
                    CreateCallbackOwnedCopy<RosImu>(mainOwned);
                }
                finally
                {
                    callbackCompleted.Set();
                }
            }, qos);
            publisher = node.CreatePublisher<RosString>(topic, qos);

            var executor = new Thread(() =>
            {
                try
                {
                    Volatile.Write(ref executorThreadId, Thread.CurrentThread.ManagedThreadId);
                    executorStarted.Set();
                    var deadline = Stopwatch.StartNew();
                    while (!callbackCompleted.IsSet && deadline.Elapsed < TimeSpan.FromSeconds(8))
                        ROS2.Ros2cs.SpinOnce(node, 0.01);
                }
                catch (Exception error)
                {
                    executorFailure = error;
                    callbackCompleted.Set();
                }
            }) { Name = "packaged-message-lifetime-ros2cs-executor" };

            executor.Start();
            Check(executorStarted.Wait(TimeSpan.FromSeconds(2)), "ROS2 executor thread did not start.");
            using (var outbound = new RosString { Data = "packaged-message-lifetime" })
            {
                var publishDeadline = Stopwatch.StartNew();
                while (!callbackCompleted.IsSet && publishDeadline.Elapsed < TimeSpan.FromSeconds(8))
                {
                    publisher.Publish(outbound);
                    Thread.Sleep(25);
                }
            }

            Check(callbackCompleted.IsSet, "Timed out waiting for the ROS2 loopback subscription callback.");
            JoinOrThrow(executor, "ROS2 executor callback");
            ThrowThreadFailure(executorFailure, "ROS2 executor callback");
            Check(rawCallbackMessage != null && IsDisposed(rawCallbackMessage) && ReadHandle(rawCallbackMessage) == IntPtr.Zero,
                "TriggerCallback did not dispose the callback-owned message after callback return.");
            var expectedTypes = new[]
            {
                typeof(RosString).FullName,
                typeof(RosTwist).FullName,
                typeof(RosJoy).FullName,
                typeof(RosImu).FullName,
            };
            Check(mainOwned.Select(item => item.TypeName).SequenceEqual(expectedTypes),
                "Callback did not produce one main-owned copy of every accepted message type.");
            foreach (var owned in mainOwned)
            {
                Check(!IsDisposed(owned.Message), owned.TypeName + " callback-created copy was disposed before main apply.");
                owned.Message.Dispose();
                Check(IsDisposed(owned.Message) && ReadHandle(owned.Message) == IntPtr.Zero,
                    owned.TypeName + " main-thread disposal did not clear its native handle.");
            }
            mainOwned.Clear();
            Console.WriteLine(
                "EXECUTOR_CALLBACK actual_spin=true callback_owned_disposed=true"
                + " types=String,Twist,Joy,Imu producer_replace=PASS split_dispose=PASS");
        }
        finally
        {
            foreach (var owned in mainOwned)
                owned.Message.Dispose();
            if (node != null)
            {
                if (subscription != null)
                    node.RemoveSubscription(subscription);
                if (publisher != null)
                    node.RemovePublisher(publisher);
                ROS2.Ros2cs.RemoveNode(node);
            }
            if (ROS2.Ros2cs.Ok())
                ROS2.Ros2cs.Shutdown();
        }
    }

    private static void CreateCallbackOwnedCopy<T>(ICollection<CallbackOwnedCopy> mainOwned)
        where T : ROS2.Message, new()
    {
        IDisposable replaced = null;
        IDisposable replacement = null;
        try
        {
            replaced = NewNativeOwned<T>();
            replacement = NewNativeOwned<T>();
            var disposedReplacement = replaced;
            disposedReplacement.Dispose();
            replaced = null;
            var replacedDisposed = IsDisposed(disposedReplacement) && ReadHandle(disposedReplacement) == IntPtr.Zero;
            Check(replacedDisposed,
                $"{typeof(T).FullName} callback-thread replacement did not dispose its replaced native copy.");
            mainOwned.Add(new CallbackOwnedCopy(typeof(T).FullName, replacement));
            replacement = null;
        }
        finally
        {
            replaced?.Dispose();
            replacement?.Dispose();
        }
    }

    private static bool IsInRos2SpinCallback()
    {
        var property = typeof(ROS2.Ros2cs).GetProperty(
            "IsInSpinCallback",
            BindingFlags.Static | BindingFlags.NonPublic);
        Check(property != null, "Ros2cs.IsInSpinCallback metadata is missing.");
        return (bool)property.GetValue(null);
    }

    private static void VerifyConstructorDefaultsAndNullSemantics()
    {
        using (var message = new RosString())
        {
            Check(message.Data == string.Empty, "String.Data must default to empty.");
            Check(ReadHandle(message) == IntPtr.Zero, "String constructor must leave its native handle lazy.");
            message.Data = null;
            Check(message.Data == null, "String.Data must preserve an assigned null.");
        }

        var twist = new RosTwist();
        var linear = twist.Linear;
        var angular = twist.Angular;
        Check(linear != null && angular != null, "Twist nested messages must default non-null.");
        Check(ReadHandle(twist) == IntPtr.Zero, "Twist constructor must leave its native handle lazy.");
        Check(ReadHandle(linear) == IntPtr.Zero && ReadHandle(angular) == IntPtr.Zero,
            "Twist nested constructors must leave native handles lazy.");
        twist.Linear = null;
        twist.Angular = null;
        Check(twist.Linear == null && twist.Angular == null, "Twist writable nested messages must preserve assigned nulls.");
        twist.Dispose();
        Check(IsDisposed(twist), "Twist with null nested messages must dispose safely.");
        linear.Dispose();
        angular.Dispose();

        var joy = new RosJoy();
        var joyHeader = joy.Header;
        Check(joyHeader != null, "Joy.Header must default non-null.");
        Check(joy.Axes != null && joy.Axes.Length == 0, "Joy.Axes must default to an empty array.");
        Check(joy.Buttons != null && joy.Buttons.Length == 0, "Joy.Buttons must default to an empty array.");
        Check(ReadHandle(joy) == IntPtr.Zero, "Joy constructor must leave its native handle lazy.");
        joy.Header = null;
        joy.Axes = null;
        joy.Buttons = null;
        Check(joy.Header == null && joy.Axes == null && joy.Buttons == null,
            "Joy writable nested and primitive sequence properties must preserve assigned nulls.");
        joy.Dispose();
        Check(IsDisposed(joy), "Joy with null writable reference properties must dispose safely.");
        joyHeader.Dispose();

        var imu = new RosImu();
        var imuHeader = imu.Header;
        var orientation = imu.Orientation;
        var angularVelocity = imu.Angular_velocity;
        var linearAcceleration = imu.Linear_acceleration;
        Check(imuHeader != null && orientation != null && angularVelocity != null && linearAcceleration != null,
            "Imu nested messages must default non-null.");
        Check(imu.Orientation_covariance?.Length == 9
              && imu.Angular_velocity_covariance?.Length == 9
              && imu.Linear_acceleration_covariance?.Length == 9,
            "Imu read-only covariance arrays must default to length nine.");
        Check(ReadHandle(imu) == IntPtr.Zero, "Imu constructor must leave its native handle lazy.");
        imu.Header = null;
        imu.Orientation = null;
        imu.Angular_velocity = null;
        imu.Linear_acceleration = null;
        Check(imu.Header == null && imu.Orientation == null
              && imu.Angular_velocity == null && imu.Linear_acceleration == null,
            "Imu writable nested messages must preserve assigned nulls.");
        imu.Dispose();
        Check(IsDisposed(imu), "Imu with null writable nested messages must dispose safely.");
        imuHeader.Dispose();
        orientation.Dispose();
        angularVelocity.Dispose();
        linearAcceleration.Dispose();

        Console.WriteLine(
            "DEFAULTS String(data=empty,null=preserved) Twist(nested=non-null) "
            + "Joy(header=non-null,arrays=empty,null=preserved) "
            + "Imu(nested=non-null,null=preserved,covariance=readonly-length-9) "
            + "null_top_level_dispose=PASS constructor_native_handle=lazy-zero");
    }

    private static void ProbeType<T>(int iterations) where T : ROS2.Message, new()
    {
        var typeName = typeof(T).FullName;
        var stopwatch = Stopwatch.StartNew();
        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var message = NewOwned<T>();
            Check(ReadHandle(message) == IntPtr.Zero, typeName + " new T() unexpectedly allocated a native handle.");
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        stopwatch.Stop();

        RunProducerReplacement<T>(Math.Min(iterations, 64));
        RunSplitProducerMainDisposal<T>();
        RunConcurrentProducerMainDisposal<T>();

        Console.WriteLine(
            "TYPE " + typeName
            + " constructor_native_handle=lazy-zero"
            + " managed_alloc_bytes_per_op=" + (allocatedBytes / Math.Max(1, iterations))
            + " construct_dispose_ns_per_op=" + (stopwatch.Elapsed.TotalNanoseconds / Math.Max(1, iterations)).ToString("F0")
            + " producer_replace=PASS split_dispose=PASS concurrent_dispose=PASS");
    }

    private static void RunProducerReplacement<T>(int iterations) where T : ROS2.Message, new()
    {
        Exception failure = null;
        var producer = new Thread(() =>
        {
            try
            {
                IDisposable pending = null;
                for (var i = 0; i < iterations; i++)
                {
                    var current = NewNativeOwned<T>();
                    pending?.Dispose();
                    pending = current;
                }
                pending?.Dispose();
            }
            catch (Exception error)
            {
                failure = error;
            }
        }) { Name = "packaged-message-lifetime-producer-replace" };

        producer.Start();
        JoinOrThrow(producer, "producer replacement");
        ThrowThreadFailure(failure, "producer replacement");
    }

    private static void RunSplitProducerMainDisposal<T>() where T : ROS2.Message, new()
    {
        Exception failure = null;
        IDisposable mainOwned = null;
        var producer = new Thread(() =>
        {
            try
            {
                mainOwned = NewNativeOwned<T>();
            }
            catch (Exception error)
            {
                failure = error;
            }
        }) { Name = "packaged-message-lifetime-producer-split" };

        producer.Start();
        JoinOrThrow(producer, "split construction");
        ThrowThreadFailure(failure, "split construction");
        Check(mainOwned != null, "Split construction did not return an owned instance.");
        mainOwned.Dispose();
        Check(IsDisposed(mainOwned) && ReadHandle(mainOwned) == IntPtr.Zero, "Main-thread disposal did not clear the owned handle.");
    }

    private static void RunConcurrentProducerMainDisposal<T>() where T : ROS2.Message, new()
    {
        Exception failure = null;
        IDisposable mainOwned = null;
        using var ready = new ManualResetEventSlim(false);
        using var barrier = new Barrier(2);
        var producer = new Thread(() =>
        {
            IDisposable producerOwned = null;
            try
            {
                producerOwned = NewNativeOwned<T>();
                mainOwned = NewNativeOwned<T>();
                ready.Set();
                Check(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "Producer concurrent-disposal barrier timed out.");
                producerOwned.Dispose();
                Check(IsDisposed(producerOwned) && ReadHandle(producerOwned) == IntPtr.Zero,
                    "Producer concurrent disposal did not clear its owned handle.");
            }
            catch (Exception error)
            {
                failure = error;
                ready.Set();
            }
        }) { Name = "packaged-message-lifetime-producer-concurrent" };

        producer.Start();
        Check(ready.Wait(TimeSpan.FromSeconds(5)), "Concurrent-disposal producer did not become ready.");
        ThrowThreadFailure(failure, "concurrent construction");
        Check(mainOwned != null, "Concurrent construction did not return a main-owned instance.");
        Check(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "Main concurrent-disposal barrier timed out.");
        mainOwned.Dispose();
        Check(IsDisposed(mainOwned) && ReadHandle(mainOwned) == IntPtr.Zero,
            "Main concurrent disposal did not clear its owned handle.");
        JoinOrThrow(producer, "concurrent disposal");
        ThrowThreadFailure(failure, "concurrent disposal");
    }

    private static void VerifyDirectNestedCascade()
    {
        var twist = new RosTwist();
        var linear = twist.Linear;
        var angular = twist.Angular;
        ForceNativeHandle(twist);
        ForceNativeHandle(linear);
        ForceNativeHandle(angular);
        twist.Dispose();
        Check(IsDisposed(linear) && IsDisposed(angular), "Twist.Dispose must cascade to direct nested messages.");

        var joy = new RosJoy();
        var joyHeader = joy.Header;
        var joyStamp = joyHeader.Stamp;
        ForceNativeHandle(joy);
        ForceNativeHandle(joyHeader);
        ForceNativeHandle(joyStamp);
        joy.Dispose();
        Check(IsDisposed(joyHeader) && IsDisposed(joyStamp),
            "Joy.Dispose must cascade transitively through Header to Header.Stamp.");

        var imu = new RosImu();
        var header = imu.Header;
        var stamp = header.Stamp;
        var orientation = imu.Orientation;
        var angularVelocity = imu.Angular_velocity;
        var linearAcceleration = imu.Linear_acceleration;
        ForceNativeHandle(imu);
        ForceNativeHandle(header);
        ForceNativeHandle(stamp);
        ForceNativeHandle(orientation);
        ForceNativeHandle(angularVelocity);
        ForceNativeHandle(linearAcceleration);
        imu.Dispose();
        Check(IsDisposed(header) && IsDisposed(stamp) && IsDisposed(orientation)
              && IsDisposed(angularVelocity) && IsDisposed(linearAcceleration),
            "Imu.Dispose must cascade transitively through Header to Header.Stamp and to its direct nested messages.");

        Console.WriteLine("CASCADE direct_nested=true transitive_header_stamp=true");
    }

    private static void VerifyAssignedMessageSequenceCascade()
    {
        var poseArray = new RosPoseArray();
        var pose = new RosPose();
        poseArray.Poses = new[] { pose };
        ForceNativeHandle(poseArray);
        ForceNativeHandle(pose);
        poseArray.Dispose();
        var cascaded = IsDisposed(pose);
        if (!cascaded)
            pose.Dispose();

        Console.WriteLine(
            "CASCADE assigned_message_sequence=" + cascaded.ToString().ToLowerInvariant()
            + " generated_recursive_disposer_required=" + (!cascaded).ToString().ToLowerInvariant());
    }

    private static T NewOwned<T>() where T : ROS2.Message, new() => new T();

    private static T NewNativeOwned<T>() where T : ROS2.Message, new()
    {
        var message = new T();
        try
        {
            Check(ReadHandle(message) == IntPtr.Zero, typeof(T).FullName + " constructor allocated a native handle eagerly.");
            Check(ForceNativeHandle(message) != IntPtr.Zero, typeof(T).FullName + " failed to materialize a native handle.");
            return message;
        }
        catch
        {
            ((IDisposable)message).Dispose();
            throw;
        }
    }

    private static IntPtr ForceNativeHandle(object message)
    {
        var property = message.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(property != null, message.GetType().FullName + " has no Handle property.");
        return (IntPtr)property.GetValue(message);
    }

    private static IntPtr ReadHandle(object message)
    {
        var field = message.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, message.GetType().FullName + " has no _handle field.");
        return (IntPtr)field.GetValue(message);
    }

    private static bool IsDisposed(object message)
    {
        var property = message.GetType().GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.Public);
        Check(property != null, message.GetType().FullName + " has no public IsDisposed property.");
        return (bool)property.GetValue(message);
    }

    private static void JoinOrThrow(Thread thread, string operation)
    {
        Check(thread.Join(TimeSpan.FromSeconds(10)), operation + " thread timed out.");
    }

    private static void ThrowThreadFailure(Exception failure, string operation)
    {
        if (failure != null)
            throw new InvalidOperationException(operation + " failed on the producer thread.", failure);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CallbackOwnedCopy
    {
        public CallbackOwnedCopy(string typeName, IDisposable message)
        {
            TypeName = typeName;
            Message = message;
        }

        public string TypeName { get; }

        public IDisposable Message { get; }
    }
}
