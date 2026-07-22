// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Roslyn-backed Phase 140-28 checks for Unity demo editor and experimental scripts.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    /// <summary>
    /// Source architecture checks for demo editor and experimental OpenH264 scripts.
    /// </summary>
    [Trait("Phase", "140-28")]
    [Trait("Domain", "Architecture")]
    public sealed class UnityDemoEditorExperimentalTests
    {
        private const string SidecarPath = "Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs";
        private const string PublisherPath = "Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs";
        private const string BuildPath = "Unity2Foxglove/Assets/Editor/FoxgloveBuild.cs";

        [Fact]
        public void ProbeSidecarStopWaitsForCapturedWorkerTasksBeforeRestart()
        {
            var sidecar = Source(SidecarPath);
            var stop = Method(sidecar, "Stop").ToFullString();

            Assert.Contains("CleanupWorkers(process, stop, stdinTask, stdoutTask, stderrTask);", stop);
            Assert.Contains("WaitForWorkerTasks", sidecar.Text);
            Assert.Contains("process.WaitForExit(200)", sidecar.Text);
            Assert.Contains("StopFromWorker", sidecar.Text);
            Assert.DoesNotContain("Task.CurrentId", sidecar.Text);
            Assert.DoesNotContain("ScheduleWorkerCleanup", sidecar.Text);
        }

        [Fact]
        public void ProbeSidecarStopCapturesLifecycleStateAtomically()
        {
            var sidecar = Source(SidecarPath);
            var stop = Method(sidecar, "Stop").ToFullString();
            var capture = Method(sidecar, "TryCaptureStopState").ToFullString();
            var clear = Method(sidecar, "ClearStoppingFlag").ToFullString();

            Assert.DoesNotContain("lock (_lifecycleLock)", stop);
            Assert.Contains("lock (_lifecycleLock)", capture);
            Assert.Contains("_stopping = true;", capture);
            Assert.Contains("_process = null;", capture);
            Assert.Contains("lock (_lifecycleLock)", clear);
            Assert.Contains("_stopping = false;", clear);
        }

        [Fact]
        public void ProbeSidecarClosesStderrToUnblockReadLineAsync()
        {
            var sidecar = Source(SidecarPath).Text;

            Assert.Contains("StandardError.BaseStream.Close()", sidecar);
            Assert.Contains("ReadLineAsync cannot take a CancellationToken", sidecar);
        }

        [Fact]
        public void BuildHelperRejectsMissingFlagValuesAndMissingScenes()
        {
            var build = Source(BuildPath).Text;
            var getValue = Method(Source(BuildPath), "GetCommandLineValue").ToFullString();

            Assert.Contains("args[i + 1].StartsWith(\"-\"", getValue);
            Assert.Contains("ValidateScenesExist", build);
            Assert.Contains("Path.Combine(Application.dataPath, \"..\")", build);
            Assert.Contains("File.Exists(scenePath)", build);
            Assert.Contains("Missing Unity build scene", build);
        }

        [Fact]
        public void ProbePublisherRuntimeCountersAreNotSerializedIntoScenes()
        {
            var publisher = Source(PublisherPath);
            var runtimeFields = new[]
            {
                "_framesCaptured",
                "_framesSubmitted",
                "_accessUnitsReceived",
                "_publishedMessages",
                "_droppedInputFrames",
                "_invalidAccessUnits",
                "_lastHelperError",
                "_lastHelperStderr"
            };

            foreach (var fieldName in runtimeFields)
            {
                var field = Field(publisher, fieldName);
                var attributes = string.Join(" ", field.AttributeLists.Select(a => a.ToFullString()));
                Assert.DoesNotContain("SerializeField", attributes);
                Assert.Contains("NonSerialized", attributes);
            }
        }

        [Fact]
        public void ProbeSidecarCrossThreadDiagnosticsUseVolatileBackingFields()
        {
            var sidecar = Source(SidecarPath).Text;

            Assert.Contains("Volatile.Read(ref _lastStderrLine)", sidecar);
            Assert.Contains("Volatile.Write(ref _lastStderrLine", sidecar);
            Assert.Contains("Volatile.Read(ref _lastError)", sidecar);
            Assert.Contains("Volatile.Write(ref _lastError", sidecar);
        }

        [Fact]
        public void ProbeSidecarUsesQueueCountInsteadOfMirrorInputCounter()
        {
            var sidecar = Source(SidecarPath).Text;

            Assert.Contains("_inputFrames.Count >= capacity", sidecar);
            Assert.DoesNotContain("_inputCount", sidecar);
        }

        [Fact]
        public void ProbeSidecarCanonicalizesExecutablePathsBeforeLaunch()
        {
            var startInfo = Method(Source(SidecarPath), "CreateStartInfo").ToFullString();

            Assert.Contains("Path.GetFullPath(options.HelperExecutablePath)", startInfo);
            Assert.Contains("Path.GetFullPath(options.OpenH264DllPath)", startInfo);
        }

        [Fact]
        public void ProbePublisherResetsPendingReadbacksWhenReenabled()
        {
            var publisher = Source(PublisherPath);
            var onEnable = Method(publisher, "OnEnable").ToFullString();
            var onReadbackComplete = Method(publisher, "OnReadbackComplete").ToFullString();

            Assert.Contains("_pendingRequests = 0;", onEnable);
            Assert.Contains("generation != _captureGeneration", onReadbackComplete);
            Assert.True(
                onReadbackComplete.IndexOf("generation != _captureGeneration", StringComparison.Ordinal)
                < onReadbackComplete.IndexOf("CompletePendingReadback();", StringComparison.Ordinal));
        }

        [Fact]
        public void ProbePublisherHidesInternalCaptureCamera()
        {
            var ensure = Method(Source(PublisherPath), "EnsureCaptureResources").ToFullString();

            Assert.Contains("go.hideFlags = HideFlags.HideAndDontSave;", ensure);
        }

        [Fact]
        public void ProbePublisherAvoidsUnconditionalCopyFromHotPath()
        {
            var publisher = Source(PublisherPath);
            var ensure = Method(publisher, "EnsureCaptureResources").ToFullString();
            var sync = Method(publisher, "SyncCaptureCameraIfDirty").ToFullString();

            Assert.DoesNotContain("_captureCamera.CopyFrom(_sourceCamera);", ensure);
            Assert.Contains("SyncCaptureCameraIfDirty", ensure);
            Assert.Contains("_captureCamera.CopyFrom(_sourceCamera);", sync);
            Assert.Contains("_lastCaptureFieldOfView", publisher.Text);
            Assert.Contains("_lastCaptureCullingMask", publisher.Text);
            Assert.Contains("_lastCaptureClearFlags", publisher.Text);
        }

        [Fact]
        public void ProbePublisherRepeatsConversionWarningsWithBackoff()
        {
            var publisher = Source(PublisherPath).Text;
            var log = Method(Source(PublisherPath), "LogConversionFailure").ToFullString();

            Assert.Contains("_conversionFailureCount", publisher);
            Assert.Contains("IsPowerOfTwo", publisher);
            Assert.Contains("_conversionFailureCount++", log);
            Assert.Contains("conversion failure count=", log);
        }

        private static SourceFile Source(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
            return new SourceFile(text, root);
        }

        private static MethodDeclarationSyntax Method(SourceFile source, string name)
        {
            var method = source.Root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == name);
            Assert.NotNull(method);
            return method;
        }

        private static FieldDeclarationSyntax Field(SourceFile source, string name)
        {
            var field = source.Root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == name));
            Assert.NotNull(field);
            return field;
        }

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                        || File.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }

        private readonly struct SourceFile
        {
            public SourceFile(string text, CompilationUnitSyntax root)
            {
                Text = text;
                Root = root;
            }

            public string Text { get; }
            public CompilationUnitSyntax Root { get; }
        }
    }
}
