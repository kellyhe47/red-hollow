#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Drop /workspace/unity/editmode.request (body = fixture class name, default T10)
    /// and the open editor runs that EditMode fixture, writing /workspace/unity/editmode.status.
    /// </summary>
    [InitializeOnLoad]
    public static class EditModeRunner
    {
        private const string RequestPath = "/workspace/unity/editmode.request";
        private const string StatusPath = "/workspace/unity/editmode.status";
        private static bool _running;

        static EditModeRunner()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_running
                || EditorApplication.isCompiling
                || EditorApplication.isPlaying
                || EditorApplication.isUpdating)
            {
                return;
            }

            if (!File.Exists(RequestPath))
            {
                return;
            }

            string filter;
            try
            {
                filter = File.ReadAllText(RequestPath).Trim();
                File.Delete(RequestPath);
            }
            catch (Exception)
            {
                return;
            }

            if (string.IsNullOrEmpty(filter))
            {
                filter = "T10_HostLoopTests";
            }

            if (!filter.Contains("."))
            {
                filter = "RedHollow.Tests.EditMode." + filter;
            }

            _running = true;
            try
            {
                File.WriteAllText(StatusPath, "running " + filter + "\n");
            }
            catch (Exception)
            {
                _running = false;
                return;
            }

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[] { filter },
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private int _pass;
            private int _fail;

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _sb.Insert(0, "pass=" + _pass + " fail=" + _fail
                    + " status=" + result.TestStatus + "\n");
                try
                {
                    File.WriteAllText(StatusPath, _sb.ToString());
                }
                catch (Exception)
                {
                }

                _running = false;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren)
                {
                    return;
                }

                if (result.TestStatus == TestStatus.Passed)
                {
                    _pass++;
                    return;
                }

                _fail++;
                _sb.Append(result.TestStatus).Append(' ').Append(result.FullName);
                if (!string.IsNullOrEmpty(result.Message))
                {
                    _sb.Append(" :: ").Append(result.Message.Replace('\n', ' '));
                }

                _sb.Append('\n');
            }
        }
    }
}
#endif
